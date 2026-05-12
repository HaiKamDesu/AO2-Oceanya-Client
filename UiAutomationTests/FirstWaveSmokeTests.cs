using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using NUnit.Framework;

namespace UiAutomationTests;

[TestFixture]
[Category("Smoke")]
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

        Window confirmWindow = app.WaitForReadyWindow("MessageBox.Yes");
        app.WaitForDescendantById(confirmWindow, "MessageBox.Yes").AsButton().Invoke();

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

    [Test]
    public void MainWindow_AddAndRemoveClient_TogglesMessagingControls()
    {
        app = FlaUiSmokeApp.Launch(SmokeFixturePaths.BuildMainWindowArguments());

        Window mainWindow = app.WaitForReadyWindow("Main.AddClient");
        AddClientViaDialog(mainWindow, "SmokeClientOne");
        WaitForElementEnabled(mainWindow, "Main.Ooc.Message", expectedEnabled: true);
        WaitForElementEnabled(mainWindow, "Main.Ic.Message", expectedEnabled: true);
        WaitForElementEnabled(mainWindow, "Main.AreaNavigator.Open", expectedEnabled: true);

        app.WaitForDescendantById(mainWindow, "Main.RemoveClient").AsButton().Invoke();

        WaitForElementEnabled(mainWindow, "Main.Ooc.Message", expectedEnabled: false);
        WaitForElementEnabled(mainWindow, "Main.Ic.Message", expectedEnabled: false);
        WaitForElementEnabled(mainWindow, "Main.AreaNavigator.Open", expectedEnabled: false);
    }

    [Test]
    public void MainWindow_AreaNavigator_GoToSecondSeededArea_UpdatesCurrentArea()
    {
        app = FlaUiSmokeApp.Launch(SmokeFixturePaths.BuildMainWindowArguments());

        Window mainWindow = app.WaitForReadyWindow("Main.AddClient");
        AddClientViaDialog(mainWindow, "SmokeClientArea");

        app.WaitForDescendantById(mainWindow, "Main.AreaNavigator.Open").AsButton().Invoke();
        AutomationElement areaList = app.WaitForDescendantById(mainWindow, "Main.AreaNavigator.List");
        SelectListItemAt(areaList, 1);
        app.WaitForDescendantById(mainWindow, "Main.AreaNavigator.Go").AsButton().Invoke();

        WaitForElementName(mainWindow, "Main.AreaNavigator.CurrentArea", "Current: Courtroom 2");
    }

    [Test]
    public void MainWindow_OocSend_EnterClearsMessage()
    {
        app = FlaUiSmokeApp.Launch(SmokeFixturePaths.BuildMainWindowArguments());

        Window mainWindow = app.WaitForReadyWindow("Main.AddClient");
        AddClientViaDialog(mainWindow, "SmokeClientOoc");

        SetText(mainWindow, "Main.Ooc.Showname", "SmokeOoc");
        FlaUI.Core.AutomationElements.TextBox messageTextBox = SetText(mainWindow, "Main.Ooc.Message", "Hello from smoke");
        PressEnter(messageTextBox);

        WaitForText(mainWindow, "Main.Ooc.Message", string.Empty);
    }

    [Test]
    public void MainWindow_IcSend_HoldIt_EnterClearsMessageAndResetsShout()
    {
        app = FlaUiSmokeApp.Launch(SmokeFixturePaths.BuildMainWindowArguments());

        Window mainWindow = app.WaitForReadyWindow("Main.AddClient");
        AddClientViaDialog(mainWindow, "SmokeClientIc");

        app.WaitForDescendantById(mainWindow, "Main.Shout.HoldIt").AsToggleButton().Toggle();
        FlaUI.Core.AutomationElements.TextBox messageTextBox = SetText(mainWindow, "Main.Ic.Message", "Hold it, smoke test");
        PressEnter(messageTextBox);

        WaitForText(mainWindow, "Main.Ic.Message", string.Empty);
        WaitForToggleState(mainWindow, "Main.Shout.HoldIt", ToggleState.Off);
    }

    [Test]
    public void MainWindow_AddClient_CancelCharacterSelector_LeavesMessagingDisabled()
    {
        app = FlaUiSmokeApp.Launch(SmokeFixturePaths.BuildMainWindowArguments());

        Window mainWindow = app.WaitForReadyWindow("Main.AddClient");
        app.WaitForDescendantById(mainWindow, "Main.AddClient").AsButton().Invoke();

        Window selectorWindow = app.WaitForReadyWindow("CharacterSelector.Cancel");
        app.WaitForDescendantById(selectorWindow, "CharacterSelector.Cancel").AsButton().Invoke();

        Window returnedMainWindow = app.WaitForReadyWindow("Main.AddClient");
        WaitForElementEnabled(returnedMainWindow, "Main.Ooc.Message", expectedEnabled: false);
        WaitForElementEnabled(returnedMainWindow, "Main.Ic.Message", expectedEnabled: false);
        WaitForElementEnabled(returnedMainWindow, "Main.AreaNavigator.Open", expectedEnabled: false);
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

    private void AddClientViaDialog(Window mainWindow, string clientName)
    {
        app!.WaitForDescendantById(mainWindow, "Main.AddClient").AsButton().Invoke();

        Window selectorWindow = app.WaitForReadyWindow("CharacterSelector.Cancel");
        SetText(selectorWindow, "CharacterSelector.ClientName", clientName);
        app.WaitForDescendantById(selectorWindow, "CharacterSelector.FirstSelectableCard").Click();

        Window returnedMainWindow = app.WaitForReadyWindow("Main.AddClient");
        WaitForElementEnabled(returnedMainWindow, "Main.Ooc.Message", expectedEnabled: true);
    }

    private static FlaUI.Core.AutomationElements.TextBox SetText(Window window, string automationId, string value)
    {
        FlaUI.Core.AutomationElements.TextBox textBox = window
            .FindFirstDescendant(cf => cf.ByAutomationId(automationId))
            ?.AsTextBox()
            ?? throw new InvalidOperationException("Text box was not found: " + automationId);
        textBox.Text = value;
        return textBox;
    }

    private static void PressEnter(FlaUI.Core.AutomationElements.TextBox textBox)
    {
        textBox.Focus();
        System.Windows.Forms.SendKeys.SendWait("{ENTER}");
    }

    private static void WaitForElementEnabled(Window window, string automationId, bool expectedEnabled)
    {
        RetryAction(
            () =>
            {
                AutomationElement element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
                    ?? throw new InvalidOperationException("Element not found: " + automationId);
                if (element.IsEnabled != expectedEnabled)
                {
                    throw new InvalidOperationException("Enabled state did not match yet.");
                }
            },
            "wait for enabled state on " + automationId);
    }

    private static void WaitForText(Window window, string automationId, string expectedText)
    {
        RetryAction(
            () =>
            {
                FlaUI.Core.AutomationElements.TextBox? textBox = window
                    .FindFirstDescendant(cf => cf.ByAutomationId(automationId))
                    ?.AsTextBox();
                if (textBox == null)
                {
                    throw new InvalidOperationException("Text box not found: " + automationId);
                }

                if (!string.Equals(textBox.Text ?? string.Empty, expectedText, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Text did not match yet.");
                }
            },
            "wait for text on " + automationId);
    }

    private static void WaitForToggleState(Window window, string automationId, ToggleState expectedState)
    {
        RetryAction(
            () =>
            {
                FlaUI.Core.AutomationElements.ToggleButton? toggleButton = window
                    .FindFirstDescendant(cf => cf.ByAutomationId(automationId))
                    ?.AsToggleButton();
                if (toggleButton == null)
                {
                    throw new InvalidOperationException("Toggle button not found: " + automationId);
                }

                if (toggleButton.ToggleState != expectedState)
                {
                    throw new InvalidOperationException("Toggle state did not match yet.");
                }
            },
            "wait for toggle state on " + automationId);
    }

    private static void WaitForElementName(Window window, string automationId, string expectedName)
    {
        RetryAction(
            () =>
            {
                AutomationElement element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
                    ?? throw new InvalidOperationException("Element not found: " + automationId);
                if (!string.Equals(element.Name?.Trim() ?? string.Empty, expectedName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Element name did not match yet.");
                }
            },
            "wait for element name on " + automationId);
    }

    private static void SelectListItemAt(AutomationElement listElement, int index)
    {
        RetryAction(
            () =>
            {
                AutomationElement[] listItems = FindListItems(listElement);
                if (index < 0 || index >= listItems.Length)
                {
                    throw new InvalidOperationException("Requested list index was not available.");
                }

                AutomationElement targetItem = listItems[index];
                if (targetItem.Patterns.SelectionItem.IsSupported)
                {
                    targetItem.Patterns.SelectionItem.Pattern.Select();
                    return;
                }

                targetItem.Focus();
                targetItem.Click();
            },
            "select list item at index " + index);
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
        AutomationElement[] listItems = FindListItems(listElement);
        if (listItems.Length > 0)
        {
            return listItems[0];
        }

        throw new InvalidOperationException("List had no items.");
    }

    private static AutomationElement[] FindListItems(AutomationElement listElement)
    {
        AutomationElement[] dataItems = listElement.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem));
        if (dataItems.Length > 0)
        {
            return dataItems;
        }

        return listElement.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
    }
}
