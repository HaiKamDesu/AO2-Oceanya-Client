using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using NUnit.Framework;
using OceanyaClient;

namespace UiAutomationTests;

internal sealed class FlaUiSmokeApp : IDisposable
{
    private bool disposed;

    private FlaUiSmokeApp(FlaUI.Core.Application app, UIA3Automation automation)
    {
        App = app;
        Automation = automation;
    }

    public FlaUI.Core.Application App { get; }

    public UIA3Automation Automation { get; }

    public string? LastWindowAutomationId { get; set; }

    public static FlaUiSmokeApp Launch(string arguments)
    {
        if (!File.Exists(SmokeFixturePaths.AppExePath))
        {
            throw new FileNotFoundException("OceanyaClient.exe was not found.", SmokeFixturePaths.AppExePath);
        }

        FlaUI.Core.Application app = FlaUI.Core.Application.Launch(SmokeFixturePaths.AppExePath, arguments);
        UIA3Automation automation = new UIA3Automation();
        return new FlaUiSmokeApp(app, automation);
    }

    public Window WaitForReadyWindow(string requiredDescendantAutomationId, int timeoutMs = 30000)
    {
        RetryResult<Window?> result = Retry.WhileNull(
            () => FindReadyWindow(requiredDescendantAutomationId),
            timeout: TimeSpan.FromMilliseconds(timeoutMs),
            interval: TimeSpan.FromMilliseconds(200),
            throwOnTimeout: false,
            ignoreException: true);

        if (!result.Success || result.Result == null)
        {
            throw new TimeoutException(
                $"Timed out waiting for ready window containing '{requiredDescendantAutomationId}'.");
        }

        LastWindowAutomationId = requiredDescendantAutomationId;
        return result.Result;
    }

    public AutomationElement WaitForDescendantById(Window window, string automationId, int timeoutMs = 10000)
    {
        AutomationElement? result = Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            timeout: TimeSpan.FromMilliseconds(timeoutMs),
            interval: TimeSpan.FromMilliseconds(150),
            throwOnTimeout: false,
            ignoreException: true).Result;

        if (result == null)
        {
            throw new TimeoutException($"Timed out waiting for descendant '{automationId}'.");
        }

        return result;
    }

    public void CaptureFailureScreenshot(string testName)
    {
        try
        {
            string filePath = SmokeFixturePaths.GetScreenshotPath(testName);

            Rectangle bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1600, 900);
            using Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            bitmap.Save(filePath, ImageFormat.Png);
            TestContext.WriteLine("Saved UI automation failure screenshot: " + filePath);
        }
        catch (Exception ex)
        {
            TestContext.WriteLine("Failed to capture UI automation screenshot: " + ex.Message);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        try
        {
            App.Close();
        }
        catch
        {
            // Best-effort close only.
        }

        try
        {
            if (!App.HasExited)
            {
                Retry.WhileFalse(
                    () => App.HasExited,
                    timeout: TimeSpan.FromSeconds(2),
                    interval: TimeSpan.FromMilliseconds(100),
                    throwOnTimeout: false,
                    ignoreException: true);
            }
        }
        catch
        {
            // Best-effort wait only.
        }

        try
        {
            if (!App.HasExited)
            {
                using Process process = Process.GetProcessById(App.ProcessId);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
            }
        }
        catch
        {
            // Best-effort kill only.
        }

        Automation.Dispose();
    }

    private Window? FindReadyWindow(string requiredDescendantAutomationId)
    {
        foreach (Window window in App.GetAllTopLevelWindows(Automation))
        {
            AutomationElement? readyMarker = window.FindFirstDescendant(
                cf => cf.ByAutomationId(OceanyaWindowContentControl.AutomationReadyMarkerAutomationId));
            if (readyMarker == null)
            {
                continue;
            }

            string markerName = readyMarker.Name?.Trim() ?? string.Empty;
            if (!string.Equals(markerName, OceanyaWindowContentControl.AutomationReadyStateReady, StringComparison.Ordinal))
            {
                continue;
            }

            AutomationElement? requiredElement = window.FindFirstDescendant(
                cf => cf.ByAutomationId(requiredDescendantAutomationId));
            if (requiredElement != null)
            {
                return window;
            }
        }

        return null;
    }
}
