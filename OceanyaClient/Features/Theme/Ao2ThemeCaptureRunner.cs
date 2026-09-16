using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Common;
using OceanyaClient.Features.Viewport;

namespace OceanyaClient.Features.Theme
{
    /// <summary>
    /// Captures one PNG of the Oceanya surface per AO2 theme, in a single process, for theme-parity work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comparison this feeds is "AO2 rendering theme X" against "Oceanya rendering the same theme X".
    /// The AO2 half never changes, so only this half is re-captured as the theme system improves - which is
    /// why the sweep runs in-process rather than through UI automation. Applying a theme is a savefile write
    /// plus a layout pass, and <see cref="RenderTargetBitmap"/> renders the visual tree without the window
    /// needing focus, so no relaunch, no Settings dialog, no clicking and no screen-grab are involved: a full
    /// sweep costs a couple of seconds per theme instead of a minute.
    /// </para>
    /// <para>
    /// The sweep is a <see cref="DispatcherTimer"/> state machine and contains no <c>await</c> on purpose.
    /// Measured during development, not guessed: while a sweep runs, startup asset work saturates the thread
    /// pool, so an awaited <c>Task.Delay</c> - and equally an awaited <c>TaskCompletionSource</c> - never
    /// resumes, while a Normal-priority dispatcher timer keeps ticking throughout. Everything here therefore
    /// runs on the UI thread's own message loop.
    /// </para>
    /// </remarks>
    public sealed class Ao2ThemeCaptureRunner
    {
        /// <summary>What the next timer tick should do.</summary>
        private enum SweepStep
        {
            ApplyTheme,
            PrimeScene,
            Capture,
        }

        private readonly IReadOnlyList<string> themeNames;
        private readonly string outputDirectory;
        private readonly int settleMilliseconds;
        private readonly string messageTemplate;
        private readonly Func<string, bool> applyTheme;
        private readonly Action<string> primeScene;
        private readonly Func<FrameworkElement?> resolveSurface;

        private DispatcherTimer? timer;
        private Action<int>? completed;
        private string manifestPath = string.Empty;
        private SweepStep step = SweepStep.ApplyTheme;
        private int themeIndex;
        private int capturedCount;

        /// <param name="themeNames">Themes to capture, in order.</param>
        /// <param name="outputDirectory">Directory the PNGs are written to; created when missing.</param>
        /// <param name="settleMilliseconds">Pause between steps so async art finishes decoding.</param>
        /// <param name="messageTemplate">IC line sent before each capture; <c>{theme}</c> is substituted.</param>
        /// <param name="applyTheme">Applies a theme as the live layout; returns false when it has no design file.</param>
        /// <param name="primeScene">Sends one IC line so the chatbox and sprite are populated.</param>
        /// <param name="resolveSurface">Returns the element to render, or <c>null</c> when it is not ready.</param>
        public Ao2ThemeCaptureRunner(
            IReadOnlyList<string> themeNames,
            string outputDirectory,
            int settleMilliseconds,
            string messageTemplate,
            Func<string, bool> applyTheme,
            Action<string> primeScene,
            Func<FrameworkElement?> resolveSurface)
        {
            this.themeNames = themeNames ?? throw new ArgumentNullException(nameof(themeNames));
            this.outputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
            this.settleMilliseconds = Math.Max(1, settleMilliseconds);
            this.messageTemplate = messageTemplate ?? string.Empty;
            this.applyTheme = applyTheme ?? throw new ArgumentNullException(nameof(applyTheme));
            this.primeScene = primeScene ?? throw new ArgumentNullException(nameof(primeScene));
            this.resolveSurface = resolveSurface ?? throw new ArgumentNullException(nameof(resolveSurface));
        }

        /// <summary>
        /// True when this launch was asked to run a capture sweep.
        /// </summary>
        public static bool IsRequested =>
            !string.IsNullOrWhiteSpace(OceanyaTestMode.Current.ThemeCaptureOutputDirectory);

        /// <summary>
        /// Resolves the theme-name specification into an ordered, de-duplicated list.
        /// </summary>
        /// <param name="specification">
        /// <c>*</c> or empty for every theme the catalog finds, <c>@path</c> to read one name per line from a
        /// file, or a comma-separated list of names.
        /// </param>
        /// <remarks>
        /// The file form exists because theme folder names contain commas, spaces and accents far more often
        /// than is comfortable on a command line.
        /// </remarks>
        public static IReadOnlyList<string> ResolveThemeNames(string specification)
        {
            string trimmed = (specification ?? string.Empty).Trim();

            if (trimmed.Length == 0 || trimmed == "*")
            {
                return AO2ThemeCatalog.GetThemes().ToList();
            }

            IEnumerable<string> names;
            if (trimmed.StartsWith("@", StringComparison.Ordinal))
            {
                string path = trimmed.Substring(1).Trim().Trim('"');
                if (!File.Exists(path))
                {
                    CustomConsole.Error("Theme capture list not found: " + path);
                    return Array.Empty<string>();
                }

                names = File.ReadAllLines(path);
            }
            else
            {
                names = trimmed.Split(',');
            }

            List<string> resolved = new List<string>();
            foreach (string name in names)
            {
                string candidate = name.Trim();
                if (candidate.Length == 0 || candidate.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!resolved.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    resolved.Add(candidate);
                }
            }

            return resolved;
        }

        /// <summary>
        /// Starts the sweep. Returns immediately; <paramref name="onCompleted"/> fires on the UI thread with
        /// the number of themes captured.
        /// </summary>
        public void Start(Action<int> onCompleted)
        {
            completed = onCompleted;

            if (themeNames.Count == 0)
            {
                CustomConsole.Error("Theme capture asked for, but no themes resolved.");
                onCompleted?.Invoke(0);
                return;
            }

            Directory.CreateDirectory(outputDirectory);
            manifestPath = Path.Combine(outputDirectory, "capture_manifest.tsv");
            File.WriteAllText(manifestPath, "theme\tresult\tdetail" + Environment.NewLine, Encoding.UTF8);

            timer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(settleMilliseconds),
            };
            timer.Tick += OnTick;
            timer.Start();
        }

        /// <summary>
        /// Performs one step of the sweep, leaving the gap until the next tick as the settle time.
        /// </summary>
        private void OnTick(object? sender, EventArgs e)
        {
            if (themeIndex >= themeNames.Count)
            {
                Finish();
                return;
            }

            string themeName = themeNames[themeIndex];

            try
            {
                switch (step)
                {
                    case SweepStep.ApplyTheme:
                        if (!applyTheme(themeName))
                        {
                            AppendManifest(themeName, "skipped", "no courtroom_design.ini");
                            AdvanceTheme();
                            return;
                        }

                        step = messageTemplate.Length > 0 ? SweepStep.PrimeScene : SweepStep.Capture;
                        return;

                    case SweepStep.PrimeScene:
                        primeScene(messageTemplate.Replace("{theme}", themeName, StringComparison.OrdinalIgnoreCase));
                        step = SweepStep.Capture;
                        return;

                    default:
                        CaptureTheme(themeName);
                        AdvanceTheme();
                        return;
                }
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Theme capture failed for \"" + themeName + "\"", ex);
                AppendManifest(themeName, "failed", ex.Message);
                AdvanceTheme();
            }
        }

        /// <summary>
        /// Renders the current theme and records it.
        /// </summary>
        private void CaptureTheme(string themeName)
        {
            FrameworkElement? surface = resolveSurface();
            if (surface == null)
            {
                AppendManifest(themeName, "skipped", "surface not ready");
                return;
            }

            string path = Path.Combine(outputDirectory, BuildFileName(themeName));
            Size size = SaveElementToPng(surface, path);
            capturedCount++;

            string detail = string.Format(
                CultureInfo.InvariantCulture, "{0}x{1}\t{2}", (int)size.Width, (int)size.Height, Path.GetFileName(path));
            AppendManifest(themeName, "captured", detail);
            CustomConsole.Info(string.Format(
                CultureInfo.InvariantCulture,
                "Theme capture [{0}/{1}] {2} - {3}",
                themeIndex + 1,
                themeNames.Count,
                themeName,
                detail.Replace('\t', ' ')));
        }

        /// <summary>
        /// Moves on to the next theme.
        /// </summary>
        private void AdvanceTheme()
        {
            themeIndex++;
            step = SweepStep.ApplyTheme;
            if (themeIndex >= themeNames.Count)
            {
                Finish();
            }
        }

        /// <summary>
        /// Stops the timer and reports the result once.
        /// </summary>
        private void Finish()
        {
            if (timer == null)
            {
                return;
            }

            timer.Stop();
            timer.Tick -= OnTick;
            timer = null;

            AppendManifest("(sweep)", "finished", capturedCount.ToString(CultureInfo.InvariantCulture) + " captured");
            Action<int>? callback = completed;
            completed = null;
            callback?.Invoke(capturedCount);
        }

        /// <summary>
        /// Renders an element to a PNG at its on-screen resolution.
        /// </summary>
        /// <returns>The element's device-independent size.</returns>
        /// <remarks>
        /// Rendered at the element's own DPI scale so a capture on a scaled monitor is not softer than one
        /// taken at 100%, and comparisons between runs stay pixel-comparable.
        /// </remarks>
        public static Size SaveElementToPng(FrameworkElement element, string path)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            element.UpdateLayout();
            double width = element.ActualWidth;
            double height = element.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("The capture surface has no size yet.");
            }

            DpiScale dpi = VisualTreeHelper.GetDpi(element);
            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(width * dpi.DpiScaleX),
                (int)Math.Ceiling(height * dpi.DpiScaleY),
                96d * dpi.DpiScaleX,
                96d * dpi.DpiScaleY,
                PixelFormats.Pbgra32);
            bitmap.Render(element);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }

            return new Size(width, height);
        }

        /// <summary>
        /// Appends one line to the manifest as it happens.
        /// </summary>
        /// <remarks>
        /// Written per theme rather than once at the end so a sweep that stalls or crashes still says which
        /// theme it reached - the difference between a quick diagnosis and a blind re-run.
        /// </remarks>
        private void AppendManifest(string themeName, string result, string detail)
        {
            if (manifestPath.Length == 0)
            {
                return;
            }

            try
            {
                File.AppendAllText(
                    manifestPath,
                    string.Concat(themeName, "\t", result, "\t", detail, Environment.NewLine),
                    Encoding.UTF8);
            }
            catch (IOException ex)
            {
                CustomConsole.Error("Could not append to the theme capture manifest", ex);
            }
        }

        /// <summary>
        /// Turns a theme folder name into a file name, keeping it recognisable.
        /// </summary>
        private static string BuildFileName(string themeName)
        {
            StringBuilder builder = new StringBuilder(themeName.Length + 4);
            foreach (char character in themeName)
            {
                builder.Append(Path.GetInvalidFileNameChars().Contains(character) ? '_' : character);
            }

            builder.Append(".png");
            return builder.ToString();
        }
    }
}
