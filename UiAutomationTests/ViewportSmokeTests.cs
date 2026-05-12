using System.Threading;
using FlaUI.Core.AutomationElements;
using NUnit.Framework;

namespace UiAutomationTests;

/// <summary>
/// Smoke tests for the AO2 viewport window.
/// Verifies the viewport button is present and the viewport window opens when clicked.
/// These tests act as a release gate: the viewport wiring and window lifecycle must be
/// deterministically correct before a release is considered ready.
/// </summary>
[TestFixture]
[Category("Smoke")]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
public sealed class ViewportSmokeTests
{
    private FlaUiSmokeApp? app;

    [TearDown]
    public void TearDown()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status != NUnit.Framework.Interfaces.TestStatus.Passed)
        {
            app?.CaptureFailureScreenshot(TestContext.CurrentContext.Test.Name);
        }

        app?.Dispose();
        app = null;
    }

    [Test]
    public void MainWindow_ViewportOpenButton_IsPresent()
    {
        app = FlaUiSmokeApp.Launch(SmokeFixturePaths.BuildMainWindowArguments());
        Window mainWindow = app.WaitForReadyWindow("Main.AddClient");

        AutomationElement viewportButton = app.WaitForDescendantById(mainWindow, "Main.Viewport.Open");

        Assert.That(viewportButton, Is.Not.Null);
    }

    [Test]
    public void MainWindow_ClickViewportButton_OpensViewportWindow()
    {
        app = FlaUiSmokeApp.Launch(SmokeFixturePaths.BuildMainWindowArguments());
        Window mainWindow = app.WaitForReadyWindow("Main.AddClient");

        app.WaitForDescendantById(mainWindow, "Main.Viewport.Open").AsButton().Invoke();

        // Viewport.Host is the automation anchor inside AO2ViewportWindowContent.
        Window viewportWindow = app.WaitForReadyWindow("Viewport.Host");
        Assert.That(viewportWindow, Is.Not.Null);
    }
}
