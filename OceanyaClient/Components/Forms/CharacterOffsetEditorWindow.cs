using AOBot_Testing.Agents;
using AOBot_Testing.Structures;
using OceanyaClient;
using OceanyaClient.Features.Viewport;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OceanyaClient.Components
{
    /// <summary>
    /// Edits AO2 self-offset values against a live viewport preview.
    /// </summary>
    internal sealed class CharacterOffsetEditorWindow : OceanyaWindowContentControl
    {
        private readonly AO2ViewportControl viewport;
        private readonly ICMessage previewMessage;
        private readonly TextBox xTextBox;
        private readonly TextBox yTextBox;
        private (int Horizontal, int Vertical) currentOffset;
        private bool updatingText;
        private Window? keyboardHostWindow;
        private readonly DispatcherTimer keyboardNudgeTimer;
        private bool keyboardNudgeActive;

        private CharacterOffsetEditorWindow(AOClient client)
        {
            if (client.currentINI == null)
            {
                throw new InvalidOperationException("A character must be selected before editing offsets.");
            }

            currentOffset = client.SelfOffset;
            previewMessage = ICMessageSettings.BuildPreviewICMessage(
                client.currentEmote ?? client.currentINI.configINI.Emotions.Values.FirstOrDefault() ?? new Emote(0),
                client);
            previewMessage.SelfOffset = currentOffset;

            viewport = new AO2ViewportControl();
            AOClient syntheticClient = new AOClient("ws://localhost:1")
            {
                curBG = client.curBG,
                curPos = client.curPos
            };
            viewport.AttachClient(syntheticClient, null, null, null);

            Width = 560;
            Height = 720;
            MinWidth = 380;
            MinHeight = 520;
            Title = "Character Offset";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            xTextBox = CreateOffsetTextBox("Offset.X");
            yTextBox = CreateOffsetTextBox("Offset.Y");
            keyboardNudgeTimer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(45)
            };
            keyboardNudgeTimer.Tick += (_, _) => ApplyHeldArrowKeyNudge();

            Content = BuildContent(client);
            Loaded += (_, _) =>
            {
                UpdateOffsetText();
                viewport.PreviewMessage(previewMessage);
                AttachHostKeyboardHandler();
                Focus();
                Keyboard.Focus(this);
                MarkAutomationReady();
            };
            Closed += (_, _) => DetachHostKeyboardHandler();
            PreviewKeyDown += CharacterOffsetEditorWindow_PreviewKeyDown;
            PreviewKeyUp += CharacterOffsetEditorWindow_PreviewKeyUp;
        }

        public override string HeaderText => "CHARACTER OFFSET";

        public override bool IsUserResizeEnabled => true;

        public (int Horizontal, int Vertical)? ResultOffset { get; private set; }

        public static (int Horizontal, int Vertical)? ShowDialog(Window? owner, AOClient client)
        {
            CharacterOffsetEditorWindow content = new CharacterOffsetEditorWindow(client)
            {
                Owner = owner
            };

            bool? result = OceanyaWindowManager.ShowDialog(content, new OceanyaWindowPresentationOptions
            {
                Owner = owner,
                Title = "Character Offset",
                HeaderText = content.HeaderText,
                Width = content.Width,
                Height = content.Height,
                MinWidth = content.MinWidth,
                MinHeight = content.MinHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                IsUserResizeEnabled = true,
                IsUserMoveEnabled = true,
                IsCloseButtonVisible = true,
                BodyMargin = new Thickness(0)
            });

            return result == true ? content.ResultOffset : null;
        }

        private Grid BuildContent(AOClient client)
        {
            Grid root = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(13, 16, 19)),
                Focusable = true
            };
            root.MouseDown += (_, _) => root.Focus();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid previewHost = new Grid
            {
                Background = Brushes.Black,
                ClipToBounds = true
            };
            Viewbox viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Margin = new Thickness(8),
                Child = viewport
            };
            previewHost.Children.Add(viewbox);
            previewHost.Children.Add(BuildDirectionalOverlay());
            Grid.SetRow(previewHost, 0);
            root.Children.Add(previewHost);

            StackPanel bottomPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(12, 10, 12, 12)
            };
            bottomPanel.Children.Add(BuildInfoRow(client));
            bottomPanel.Children.Add(BuildOffsetSection());
            bottomPanel.Children.Add(BuildCommandRow());
            Grid.SetRow(bottomPanel, 1);
            root.Children.Add(bottomPanel);

            return root;
        }

        private UIElement BuildInfoRow(AOClient client)
        {
            DockPanel row = new DockPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };

            Button replayButton = CreateCommandButton("Replay");
            replayButton.Width = 90;
            replayButton.Height = 28;
            replayButton.Margin = new Thickness(0, 0, 10, 0);
            replayButton.HorizontalAlignment = HorizontalAlignment.Left;
            replayButton.Content = "▶ Replay";
            replayButton.Click += (_, _) => ReplayPreview();

            TextBlock info = new TextBlock
            {
                Text = $"{client.currentINI?.Name ?? "Character"}  ·  {client.currentEmote?.DisplayID ?? "current emote"}",
                Foreground = new SolidColorBrush(Color.FromRgb(198, 212, 226)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            Border badge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(41, 62, 78)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(83, 113, 132)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(7, 2, 7, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = new TextBlock
                {
                    Text = "PREVIEW",
                    Foreground = Brushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                }
            };

            DockPanel.SetDock(replayButton, Dock.Left);
            DockPanel.SetDock(badge, Dock.Right);
            row.Children.Add(replayButton);
            row.Children.Add(badge);
            row.Children.Add(info);
            return row;
        }

        private UIElement BuildDirectionalOverlay()
        {
            Grid overlay = new Grid
            {
                Margin = new Thickness(20),
                IsHitTestVisible = true
            };
            overlay.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            overlay.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            overlay.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            overlay.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            overlay.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            overlay.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            RepeatButton up = CreateArrowButton("▲", "Move up", () => AdjustOffset(0, -1));
            RepeatButton down = CreateArrowButton("▼", "Move down", () => AdjustOffset(0, 1));
            RepeatButton left = CreateArrowButton("◀", "Move left", () => AdjustOffset(-1, 0));
            RepeatButton right = CreateArrowButton("▶", "Move right", () => AdjustOffset(1, 0));

            Grid.SetRow(up, 0);
            Grid.SetColumn(up, 1);
            Grid.SetRow(down, 2);
            Grid.SetColumn(down, 1);
            Grid.SetRow(left, 1);
            Grid.SetColumn(left, 0);
            Grid.SetRow(right, 1);
            Grid.SetColumn(right, 2);

            overlay.Children.Add(up);
            overlay.Children.Add(down);
            overlay.Children.Add(left);
            overlay.Children.Add(right);
            return overlay;
        }

        private UIElement BuildOffsetSection()
        {
            Border section = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 29, 34)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(62, 78, 91)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10)
            };

            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock header = new TextBlock
            {
                Text = "Offset",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetColumnSpan(header, 4);
            grid.Children.Add(header);

            AddOffsetField(grid, "X", xTextBox, -1, 1, 0);
            AddOffsetField(grid, "Y", yTextBox, -1, 1, 2);

            section.Child = grid;
            return section;
        }

        private void AddOffsetField(Grid grid, string label, TextBox textBox, int decrementX, int incrementX, int startColumn)
        {
            TextBlock labelBlock = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(216, 224, 232)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(startColumn == 0 ? 0 : 14, 0, 6, 0)
            };
            Grid.SetRow(labelBlock, 1);
            Grid.SetColumn(labelBlock, startColumn);
            grid.Children.Add(labelBlock);

            DockPanel editor = new DockPanel
            {
                LastChildFill = true
            };
            RepeatButton minus = CreateStepperButton("-", () => AdjustOffset(label == "X" ? decrementX : 0, label == "Y" ? decrementX : 0));
            RepeatButton plus = CreateStepperButton("+", () => AdjustOffset(label == "X" ? incrementX : 0, label == "Y" ? incrementX : 0));
            DockPanel.SetDock(minus, Dock.Left);
            DockPanel.SetDock(plus, Dock.Right);
            editor.Children.Add(minus);
            editor.Children.Add(plus);
            editor.Children.Add(textBox);

            Grid.SetRow(editor, 1);
            Grid.SetColumn(editor, startColumn + 1);
            grid.Children.Add(editor);
        }

        private UIElement BuildCommandRow()
        {
            DockPanel row = new DockPanel
            {
                LastChildFill = false
            };

            Button cancelButton = CreateCommandButton("Cancel");
            cancelButton.Click += (_, _) => RequestHostClose(false);

            Button defaultButton = CreateCommandButton("Default");
            defaultButton.Margin = new Thickness(0, 0, 8, 0);
            defaultButton.Click += (_, _) =>
            {
                SetOffset((0, 0));
                ResultOffset = currentOffset;
                RequestHostClose(true);
            };

            Button saveButton = CreateCommandButton("Save");
            saveButton.Margin = new Thickness(0, 0, 8, 0);
            saveButton.Background = new SolidColorBrush(Color.FromRgb(36, 75, 60));
            saveButton.BorderBrush = new SolidColorBrush(Color.FromRgb(89, 151, 122));
            saveButton.Click += (_, _) =>
            {
                ResultOffset = currentOffset;
                RequestHostClose(true);
            };

            DockPanel.SetDock(cancelButton, Dock.Right);
            DockPanel.SetDock(defaultButton, Dock.Right);
            DockPanel.SetDock(saveButton, Dock.Right);
            row.Children.Add(cancelButton);
            row.Children.Add(defaultButton);
            row.Children.Add(saveButton);
            return row;
        }

        private RepeatButton CreateArrowButton(string content, string tooltip, Action clickAction)
        {
            RepeatButton button = new RepeatButton
            {
                Content = content,
                Width = 42,
                Height = 42,
                Delay = 280,
                Interval = 45,
                Opacity = 0.24,
                Background = new SolidColorBrush(Color.FromArgb(210, 15, 20, 25)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(122, 153, 174)),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                ToolTip = tooltip,
                Focusable = false,
                Template = CreateDarkButtonTemplate(
                    Color.FromArgb(230, 24, 32, 39),
                    Color.FromArgb(245, 11, 15, 18),
                    Color.FromRgb(122, 153, 174),
                    Color.FromRgb(149, 176, 194))
            };
            button.Click += (_, _) => clickAction();
            button.MouseEnter += (_, _) => button.Opacity = 0.88;
            button.MouseLeave += (_, _) => button.Opacity = 0.24;
            button.PreviewMouseDown += (_, _) => button.Opacity = 1;
            button.PreviewMouseUp += (_, _) => button.Opacity = 0.88;
            return button;
        }

        private RepeatButton CreateStepperButton(string content, Action clickAction)
        {
            RepeatButton button = new RepeatButton
            {
                Content = content,
                Width = 24,
                Height = 26,
                Delay = 300,
                Interval = 65,
                Background = new SolidColorBrush(Color.FromRgb(30, 37, 43)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(78, 96, 108)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Focusable = false,
                Template = CreateDarkButtonTemplate(
                    Color.FromRgb(40, 49, 57),
                    Color.FromRgb(17, 22, 27),
                    Color.FromRgb(78, 96, 108),
                    Color.FromRgb(95, 118, 132))
            };
            button.Click += (_, _) => clickAction();
            return button;
        }

        private Button CreateCommandButton(string content)
        {
            return new Button
            {
                Content = content,
                Width = 88,
                Height = 28,
                Background = new SolidColorBrush(Color.FromRgb(31, 38, 44)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(77, 92, 104)),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                FontSize = 12,
                Padding = new Thickness(8, 2, 8, 2),
                Template = CreateDarkButtonTemplate(
                    Color.FromRgb(42, 51, 59),
                    Color.FromRgb(18, 23, 28),
                    Color.FromRgb(77, 92, 104),
                    Color.FromRgb(94, 112, 126))
            };
        }

        private static ControlTemplate CreateDarkButtonTemplate(
            Color hoverBackground,
            Color pressedBackground,
            Color normalBorder,
            Color hoverBorder)
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Chrome";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.AppendChild(presenter);

            ControlTemplate template = new ControlTemplate(typeof(ButtonBase))
            {
                VisualTree = border
            };

            Trigger hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(hoverBackground), "Chrome"));
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(hoverBorder), "Chrome"));
            template.Triggers.Add(hoverTrigger);

            Trigger pressedTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(pressedBackground), "Chrome"));
            pressedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(normalBorder), "Chrome"));
            template.Triggers.Add(pressedTrigger);

            Trigger disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            template.Triggers.Add(disabledTrigger);

            return template;
        }

        private TextBox CreateOffsetTextBox(string automationId)
        {
            TextBox textBox = new TextBox
            {
                Width = 64,
                Height = 26,
                Background = new SolidColorBrush(Color.FromRgb(12, 15, 18)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(69, 87, 99)),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13
            };
            AutomationProperties.SetAutomationId(textBox, automationId);
            textBox.TextChanged += OffsetTextBox_TextChanged;
            textBox.LostFocus += (_, _) => UpdateOffsetText();
            return textBox;
        }

        private void OffsetTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (updatingText)
            {
                return;
            }

            if (!TryParseTextBox(xTextBox, out int x) || !TryParseTextBox(yTextBox, out int y))
            {
                return;
            }

            SetOffset((x, y), updateText: false);
        }

        private static bool TryParseTextBox(TextBox textBox, out int value)
        {
            return int.TryParse(
                textBox.Text?.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        private void CharacterOffsetEditorWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleOffsetPreviewKeyDown(e);
        }

        private void CharacterOffsetEditorWindow_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            HandleOffsetPreviewKeyUp(e);
        }

        private void HostWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleOffsetPreviewKeyDown(e);
        }

        private void HostWindow_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            HandleOffsetPreviewKeyUp(e);
        }

        private void HandleOffsetPreviewKeyDown(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left:
                case Key.Right:
                case Key.Down:
                case Key.Up:
                    StartKeyboardNudge();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    ResultOffset = currentOffset;
                    RequestHostClose(true);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    RequestHostClose(false);
                    e.Handled = true;
                    break;
            }
        }

        private void HandleOffsetPreviewKeyUp(KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left:
                case Key.Right:
                case Key.Down:
                case Key.Up:
                    StopKeyboardNudgeIfNoArrowKeysHeld();
                    e.Handled = true;
                    break;
            }
        }

        private void StartKeyboardNudge()
        {
            if (!keyboardNudgeActive)
            {
                keyboardNudgeActive = true;
                ApplyHeldArrowKeyNudge();
            }

            if (!keyboardNudgeTimer.IsEnabled)
            {
                keyboardNudgeTimer.Start();
            }
        }

        private void StopKeyboardNudgeIfNoArrowKeysHeld()
        {
            if (IsAnyArrowKeyHeld())
            {
                return;
            }

            keyboardNudgeActive = false;
            keyboardNudgeTimer.Stop();
        }

        private void ApplyHeldArrowKeyNudge()
        {
            int horizontalDelta = 0;
            int verticalDelta = 0;

            if (Keyboard.IsKeyDown(Key.Left))
            {
                horizontalDelta--;
            }

            if (Keyboard.IsKeyDown(Key.Right))
            {
                horizontalDelta++;
            }

            if (Keyboard.IsKeyDown(Key.Up))
            {
                verticalDelta--;
            }

            if (Keyboard.IsKeyDown(Key.Down))
            {
                verticalDelta++;
            }

            if (horizontalDelta == 0 && verticalDelta == 0)
            {
                StopKeyboardNudgeIfNoArrowKeysHeld();
                return;
            }

            AdjustOffset(horizontalDelta, verticalDelta);
        }

        private static bool IsAnyArrowKeyHeld()
        {
            return Keyboard.IsKeyDown(Key.Left)
                || Keyboard.IsKeyDown(Key.Right)
                || Keyboard.IsKeyDown(Key.Up)
                || Keyboard.IsKeyDown(Key.Down);
        }

        private void AttachHostKeyboardHandler()
        {
            Window? host = HostWindow;
            if (host == null || ReferenceEquals(host, keyboardHostWindow))
            {
                return;
            }

            DetachHostKeyboardHandler();
            keyboardHostWindow = host;
            host.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(HostWindow_PreviewKeyDown), true);
            host.AddHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(HostWindow_PreviewKeyUp), true);
        }

        private void DetachHostKeyboardHandler()
        {
            if (keyboardHostWindow == null)
            {
                return;
            }

            keyboardHostWindow.RemoveHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(HostWindow_PreviewKeyDown));
            keyboardHostWindow.RemoveHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(HostWindow_PreviewKeyUp));
            keyboardHostWindow = null;
            keyboardNudgeActive = false;
            keyboardNudgeTimer.Stop();
        }

        private void AdjustOffset(int horizontalDelta, int verticalDelta)
        {
            SetOffset((currentOffset.Horizontal + horizontalDelta, currentOffset.Vertical + verticalDelta));
        }

        private void SetOffset((int Horizontal, int Vertical) offset, bool updateText = true)
        {
            currentOffset = offset;
            previewMessage.SelfOffset = currentOffset;
            if (updateText)
            {
                UpdateOffsetText();
            }

            viewport.PreviewSelfOffset(currentOffset);
        }

        private void ReplayPreview()
        {
            previewMessage.SelfOffset = currentOffset;
            viewport.PreviewMessage(previewMessage);
        }

        private void UpdateOffsetText()
        {
            updatingText = true;
            try
            {
                xTextBox.Text = currentOffset.Horizontal.ToString(CultureInfo.InvariantCulture);
                yTextBox.Text = currentOffset.Vertical.ToString(CultureInfo.InvariantCulture);
            }
            finally
            {
                updatingText = false;
            }
        }
    }
}
