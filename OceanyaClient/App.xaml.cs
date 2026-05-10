using Common;
using OceanyaClient.Features.FileHivemind;
using OceanyaClient.Utilities;
using System.Configuration;
using System.Collections.Generic;
using System.Data;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace OceanyaClient;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private readonly HashSet<Window> persistedWindows = new HashSet<Window>();
    private CancellationTokenSource? hivemindAgentCancellationTokenSource;
    private FileHivemindTrayIconController? hivemindTrayIconController;
    private bool isHivemindAgentMode;
    private List<string> loadingMessages = new List<string>
    {
        "Starting completely unnecessary loading...",
        "Pretending to fetch important data...",
        "Looking busy for absolutely no reason...",
        "Connecting to imaginary server...",
        "Loading things you probably won't notice...",
        "Making it look like I'm working hard...",
        "Finalizing stuff that doesn't exist...",
        "Optimizing the slowdowns...",
        "Still loading... Why are you even waiting?",
        "Synchronizing with absolutely nothing...",
        "Establishing useless connections...",
        "Putting in effort into fake progress bars...",
        "Just a few more pointless tasks...",
        "Almost there, promise (maybe)...",
        "Wrapping up pointless loading...",
        "Escaping from Scorpio2's basement...",
        "Loading complete! (Or is it?)",
        "You can stop waiting now...",
        "Waiting for Dredd's coffee...",
        "Loading the loading screen...",
        "Loading the loading screen's loading screen...",
        "Wait, did i forget 7...?",
        "Waiting for GM's countdown...",
        "Waiting for a funni face...",
        "Waiting for Oceanya MMO to come out...",
        "Waiting for Scorpio2 to pass out...",
        "Waiting for Dredd to get another coffee...",
        "Loading Subway Surfers..."
    };


    public App()
    {
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(Window_LoadedForPersistence));
    }


    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        OceanyaTestModeOptions testModeOptions = OceanyaTestMode.ParseArgs(e.Args);
        OceanyaTestMode.SetCurrent(testModeOptions);
        if (testModeOptions.IsEnabled)
        {
            if (!string.IsNullOrWhiteSpace(testModeOptions.SaveFilePath))
            {
                SaveFile.ConfigureStoragePathForTests(testModeOptions.SaveFilePath);
            }

            if (!string.IsNullOrWhiteSpace(testModeOptions.ServerJsonPath))
            {
                Globals.ReloadServerIpsForTests(testModeOptions.ServerJsonPath);
            }
        }

        isHivemindAgentMode = FileHivemindBackgroundAgentCommandLine.IsAgentMode(e.Args);
        if (isHivemindAgentMode)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            hivemindAgentCancellationTokenSource = new CancellationTokenSource();
            hivemindTrayIconController = new FileHivemindTrayIconController(
                () => hivemindAgentCancellationTokenSource?.Cancel());

            try
            {
                FileHivemindBackgroundSyncAgent agent = new FileHivemindBackgroundSyncAgent(
                    backgroundNotifier: hivemindTrayIconController);
                await agent.RunAsync(hivemindAgentCancellationTokenSource.Token);
            }
            finally
            {
                hivemindTrayIconController?.Dispose();
                hivemindTrayIconController = null;
                Shutdown();
            }

            return;
        }

        StartupTimingLogger.Log("app_startup_begin");

        TryStartBackgroundHivemindAgent();

        bool skipFakeLoading = OceanyaTestMode.Current.DisableFakeLoading || SaveFile.Data.SkipLoadingScreen;
        if (!skipFakeLoading)
        {
            StartupTimingLogger.Log("fake_loading_begin");
            await FakeLoadingAsync();
            StartupTimingLogger.Log("fake_loading_end");
        }

        // After loading finishes, show your main window
        StartupTimingLogger.Log("initial_config_window_shown");
        InitialConfigurationWindow mainWindow = new InitialConfigurationWindow();
        mainWindow.Show();
        mainWindow.Activate();
        mainWindow.Focus();
    }

    private readonly Random _rand = new();

    private async Task FakeLoadingAsync()
    {
        if (OceanyaTestMode.Current.DisableFakeLoading)
        {
            return;
        }

        // Randomly choose how many messages you'll display
        int stepsCount = _rand.Next(2, 9);

        // Shuffle the list and take the desired number of messages
        var selectedMessages = loadingScreenShuffle(loadingMessages).Take(stepsCount).ToList();

        await LoadingScreenManager.ShowFormAsync(selectedMessages[0]);

        for (int i = 0; i < selectedMessages.Count; i++)
        {
            LoadingScreenManager.SetSubtitle(selectedMessages[i]);
            var curProgress = (double)(i + 1) / stepsCount;
            LoadingScreenManager.SetProgress(curProgress);

            await Task.Delay(_rand.Next(500, 1200));
        }

        AudioPlayer.PlayEmbeddedSound("Resources/BellDing.mp3", AudioSettings.ScaleEmbeddedSfxVolume(0.25f));
        LoadingScreenManager.SetSubtitle("Loading complete!");
        LoadingScreenManager.SetProgress(1);
        await Task.Delay(600);
        await LoadingScreenManager.CloseFormAsync();
    }

    private List<string> loadingScreenShuffle(List<string> messages)
    {
        return messages.OrderBy(x => _rand.Next()).ToList();
    }

    private List<string> SelectRandomMessages(List<string> source, int take)
    {
        return source.OrderBy(_ => _rand.Next()).Take(take).ToList();
    }


    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        string crashPath = CrashLogger.LogUnhandledException(
            e.Exception,
            "DispatcherUnhandledException",
            isTerminating: true
        );

        if (isHivemindAgentMode)
        {
            e.Handled = true;
            return;
        }

        string message = "An unhandled error occurred.";
        if (!string.IsNullOrWhiteSpace(crashPath))
        {
            message += $"\n\nCrash log:\n{crashPath}";
        }

        OceanyaMessageBox.Show(message, "Unhandled Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Exception exception;
        if (e.ExceptionObject is Exception castException)
        {
            exception = castException;
        }
        else
        {
            exception = new Exception($"Non-exception unhandled error object: {e.ExceptionObject}");
        }

        CrashLogger.LogUnhandledException(
            exception,
            "AppDomain.CurrentDomain.UnhandledException",
            e.IsTerminating
        );
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLogger.LogUnhandledException(
            e.Exception,
            "TaskScheduler.UnobservedTaskException",
            isTerminating: false
        );

        // Prevent finalizer-thread escalation after logging.
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        this.DispatcherUnhandledException -= App_DispatcherUnhandledException;
        hivemindAgentCancellationTokenSource?.Cancel();
        hivemindTrayIconController?.Dispose();
        hivemindTrayIconController = null;

        // Ensure the WaitForm UI thread is properly shut down
        WaitForm.ShutdownThread();

        base.OnExit(e);
    }

    private void Window_LoadedForPersistence(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        if (window.ResizeMode == ResizeMode.NoResize)
        {
            return;
        }

        string key = BuildWindowPersistenceKey(window);
        if (SaveFile.Data.PopupWindowStates.TryGetValue(key, out VisualizerWindowState? state))
        {
            window.Width = Math.Max(window.MinWidth, state.Width);
            window.Height = Math.Max(window.MinHeight, state.Height);

            if (state.Left.HasValue
                && state.Top.HasValue
                && IsFinite(state.Left.Value)
                && IsFinite(state.Top.Value))
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                Rect virtualBounds = new Rect(
                    SystemParameters.VirtualScreenLeft,
                    SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight);
                double maxLeft = virtualBounds.Right - Math.Max(window.MinWidth, state.Width);
                double maxTop = virtualBounds.Bottom - Math.Max(window.MinHeight, state.Height);
                window.Left = Clamp(state.Left.Value, virtualBounds.Left, maxLeft);
                window.Top = Clamp(state.Top.Value, virtualBounds.Top, maxTop);
            }

            if (state.IsMaximized)
            {
                window.WindowState = WindowState.Maximized;
            }
        }

        if (persistedWindows.Add(window))
        {
            window.Closing += Window_ClosingForPersistence;
        }
    }

    private static void Window_ClosingForPersistence(object? sender, CancelEventArgs e)
    {
        if (sender is not Window window || window.ResizeMode == ResizeMode.NoResize)
        {
            return;
        }

        Rect bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.Width, window.Height)
            : window.RestoreBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        string key = BuildWindowPersistenceKey(window);
        SaveFile.Data.PopupWindowStates[key] = new VisualizerWindowState
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Left = bounds.X,
            Top = bounds.Y,
            IsMaximized = window.WindowState == WindowState.Maximized
        };
        SaveFile.Save();
    }

    private static string BuildWindowPersistenceKey(Window window)
    {
        string typeName = window.GetType().FullName ?? window.GetType().Name;
        string title = (window.Title ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(title) ? typeName : $"{typeName}|{title}";
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        if (maximum < minimum)
        {
            return minimum;
        }

        return Math.Max(minimum, Math.Min(maximum, value));
    }

    private static void TryStartBackgroundHivemindAgent()
    {
        try
        {
            FileHivemindBackgroundAgentLauncher launcher = new FileHivemindBackgroundAgentLauncher();
            launcher.EnsureRunningForCurrentSession(SaveFile.Data.FileHivemind);
        }
        catch (Exception ex)
        {
            CustomConsole.Warning("Failed to start the Hivemind background agent.", ex);
        }
    }
}
