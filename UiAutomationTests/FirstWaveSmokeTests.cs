using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using NUnit.Framework;

namespace UiAutomationTests;

[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
public sealed class FirstWaveSmokeTests
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
    public void Launch_InitialConfigurationWindow_IsReady()
    {
        app = FlaUiSmokeApp.Launch(SmokeFixturePaths.BuildInitialConfigurationArguments());

        Window initialConfigWindow = app.WaitForReadyWindow("InitialConfig.Launch");

        Assert.Multiple(() =>
        {
            Assert.That(app.WaitForDescendantById(initialConfigWindow, "InitialConfig.ConfigIniPath"), Is.Not.Null);
            Assert.That(app.WaitForDescendantById(initialConfigWindow, "InitialConfig.StartupFunctionality"), Is.Not.Null);
            Assert.That(app.WaitForDescendantById(initialConfigWindow, "InitialConfig.Launch"), Is.Not.Null);
        });
    }

    [Test]
    public void InitialConfig_OpenServerSelection_SelectFavoriteAndReturn()
    {
        app = FlaUiSmokeApp.Launch(SmokeFixturePaths.BuildInitialConfigurationArguments());

        Window initialConfigWindow = app.WaitForReadyWindow("InitialConfig.Launch");
        SelectGmMultiClient(initialConfigWindow);
        app.WaitForDescendantById(initialConfigWindow, "InitialConfig.SelectServer").AsButton().Invoke();

        Window serverSelectionWindow = app.WaitForReadyWindow("ServerSelection.Select");
        Tab serverTabs = app.WaitForDescendantById(serverSelectionWindow, "ServerSelection.Tabs").AsTab();
        serverTabs.TabItems[2].Select();

        AutomationElement favoritesList = app.WaitForDescendantById(serverSelectionWindow, "ServerSelection.FavoritesList");
        RetrySelectFirstItem(favoritesList);
        InvokeWhenEnabled(app.WaitForDescendantById(serverSelectionWindow, "ServerSelection.Select").AsButton());

        Window returnedInitialConfigWindow = app.WaitForReadyWindow("InitialConfig.Launch");
        string selectedServerText = app.WaitForDescendantById(returnedInitialConfigWindow, "InitialConfig.SelectedServerText")
            .AsTextBox()
            .Text;

        Assert.That(selectedServerText, Is.EqualTo("Smoke Favorite"));
    }

    [Test]
    public void InitialConfig_LaunchGmMultiClient_OpensMainWindow()
    {
        app = FlaUiSmokeApp.Launch(SmokeFixturePaths.BuildInitialConfigurationArguments());

        Window initialConfigWindow = app.WaitForReadyWindow("InitialConfig.Launch");
        SelectGmMultiClient(initialConfigWindow);
        app.WaitForDescendantById(initialConfigWindow, "InitialConfig.Launch").AsButton().Invoke();

        Window mainWindow = app.WaitForReadyWindow("Main.AddClient");

        Assert.Multiple(() =>
        {
            Assert.That(app.WaitForDescendantById(mainWindow, "Main.AddClient"), Is.Not.Null);
            Assert.That(app.WaitForDescendantById(mainWindow, "Main.RemoveClient"), Is.Not.Null);
            Assert.That(app.WaitForDescendantById(mainWindow, "Main.Ooc.Message"), Is.Not.Null);
            Assert.That(app.WaitForDescendantById(mainWindow, "Main.Ic.Message"), Is.Not.Null);
            Assert.That(app.WaitForDescendantById(mainWindow, "Main.AreaNavigator.Open"), Is.Not.Null);
        });
    }

    [Test]
    public void MainWindow_OpenCharacterFolderVisualizer_LoadsList()
    {
        app = FlaUiSmokeApp.Launch(SmokeFixturePaths.BuildMainWindowArguments());

        Window mainWindow = app.WaitForReadyWindow("Main.AddClient");
        app.WaitForDescendantById(mainWindow, "Main.OpenCharacterFolderVisualizer").AsButton().Invoke();

        Window folderVisualizerWindow = app.WaitForReadyWindow("FolderVisualizer.List");

        Assert.Multiple(() =>
        {
            Assert.That(app.WaitForDescendantById(folderVisualizerWindow, "FolderVisualizer.Search"), Is.Not.Null);
            Assert.That(app.WaitForDescendantById(folderVisualizerWindow, "FolderVisualizer.ViewMode"), Is.Not.Null);
            Assert.That(app.WaitForDescendantById(folderVisualizerWindow, "FolderVisualizer.List"), Is.Not.Null);
            Assert.That(app.WaitForDescendantById(folderVisualizerWindow, "FolderVisualizer.Summary"), Is.Not.Null);
        });
    }

    [Test]
    public void FolderVisualizer_OpenEmoteVisualizer_LoadsList()
    {
        app = FlaUiSmokeApp.Launch(SmokeFixturePaths.BuildFolderVisualizerArguments());

        Window folderVisualizerWindow = app.WaitForReadyWindow("FolderVisualizer.List");
        AutomationElement folderList = app.WaitForDescendantById(folderVisualizerWindow, "FolderVisualizer.List");
        DoubleClickFirstItem(folderList);

        Window emoteVisualizerWindow = app.WaitForReadyWindow("EmoteVisualizer.List");

        Assert.Multiple(() =>
        {
            Assert.That(app.WaitForDescendantById(emoteVisualizerWindow, "EmoteVisualizer.Search"), Is.Not.Null);
            Assert.That(app.WaitForDescendantById(emoteVisualizerWindow, "EmoteVisualizer.ViewMode"), Is.Not.Null);
            Assert.That(app.WaitForDescendantById(emoteVisualizerWindow, "EmoteVisualizer.List"), Is.Not.Null);
            Assert.That(app.WaitForDescendantById(emoteVisualizerWindow, "EmoteVisualizer.Summary"), Is.Not.Null);
        });
    }

    private static void RetrySelectFirstItem(AutomationElement listElement)
    {
        RetryAction(
            () =>
            {
                AutomationElement firstItem = FindFirstListItem(listElement);
                if (firstItem.Patterns.SelectionItem.IsSupported)
                {
                    firstItem.Patterns.SelectionItem.Pattern.Select();
                    return;
                }

                firstItem.Focus();
                firstItem.Click();
            },
            "select first list item");
    }

    private static void DoubleClickFirstItem(AutomationElement listElement)
    {
        RetryAction(
            () =>
            {
                AutomationElement firstItem = FindFirstListItem(listElement);
                firstItem.Focus();
                firstItem.DoubleClick();
            },
            "double-click first list item");
    }

    private static void RetryAction(Action action, string operation)
    {
        Exception? lastException = null;
        RetryResult<bool> result = Retry.WhileFalse(
            () =>
            {
                try
                {
                    action();
                    return true;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    return false;
                }
            },
            timeout: TimeSpan.FromSeconds(3),
            interval: TimeSpan.FromMilliseconds(150),
            throwOnTimeout: false,
            ignoreException: true);

        if (result.Success)
        {
            return;
        }

        throw new InvalidOperationException("Failed to " + operation + ".", lastException);
    }

    private static void SelectGmMultiClient(Window initialConfigWindow)
    {
        FlaUI.Core.AutomationElements.ComboBox comboBox = initialConfigWindow
            .FindFirstDescendant(cf => cf.ByAutomationId("InitialConfig.StartupFunctionality"))
            ?.AsComboBox()
            ?? throw new InvalidOperationException("Startup functionality combo box was not found.");

        RetryAction(
            () =>
            {
                ComboBoxItem firstItem = comboBox.Items[0];
                firstItem.Select();
            },
            "select GM multi-client startup functionality");
    }

    private static void InvokeWhenEnabled(FlaUI.Core.AutomationElements.Button button)
    {
        RetryAction(
            () =>
            {
                if (!button.IsEnabled)
                {
                    throw new InvalidOperationException("Button is not enabled yet.");
                }

                button.Invoke();
            },
            "invoke enabled button");
    }

    private static AutomationElement FindFirstListItem(AutomationElement listElement)
    {
        AutomationElement? dataItem = listElement.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem));
        if (dataItem != null)
        {
            return dataItem;
        }

        return listElement.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem))
            ?? throw new InvalidOperationException("List had no items.");
    }
}
