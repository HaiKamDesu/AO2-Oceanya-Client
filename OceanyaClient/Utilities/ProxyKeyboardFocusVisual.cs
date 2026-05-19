using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace OceanyaClient.Utilities
{
    public static class ProxyKeyboardFocusVisual
    {
        public static readonly DependencyProperty IsProxyKeyboardFocusTargetProperty =
            DependencyProperty.RegisterAttached(
                "IsProxyKeyboardFocusTarget",
                typeof(bool),
                typeof(ProxyKeyboardFocusVisual),
                new PropertyMetadata(false, OnIsProxyKeyboardFocusTargetChanged));

        private static readonly DependencyProperty OriginalBorderBrushProperty =
            DependencyProperty.RegisterAttached(
                "OriginalBorderBrush",
                typeof(Brush),
                typeof(ProxyKeyboardFocusVisual),
                new PropertyMetadata(null));

        private static readonly DependencyProperty OriginalInactiveSelectionHighlightProperty =
            DependencyProperty.RegisterAttached(
                "OriginalInactiveSelectionHighlight",
                typeof(bool),
                typeof(ProxyKeyboardFocusVisual),
                new PropertyMetadata(false));

        private static readonly DependencyProperty HasOriginalVisualProperty =
            DependencyProperty.RegisterAttached(
                "HasOriginalVisual",
                typeof(bool),
                typeof(ProxyKeyboardFocusVisual),
                new PropertyMetadata(false));

        private static readonly DependencyProperty CaretAdornerProperty =
            DependencyProperty.RegisterAttached(
                "CaretAdorner",
                typeof(ProxyCaretAdorner),
                typeof(ProxyKeyboardFocusVisual),
                new PropertyMetadata(null));

        private static readonly Brush ProxyBorderBrush = new SolidColorBrush(Color.FromRgb(76, 166, 255));

        static ProxyKeyboardFocusVisual()
        {
            ProxyBorderBrush.Freeze();
        }

        public static bool GetIsProxyKeyboardFocusTarget(DependencyObject element)
        {
            return (bool)element.GetValue(IsProxyKeyboardFocusTargetProperty);
        }

        public static void SetIsProxyKeyboardFocusTarget(DependencyObject element, bool value)
        {
            element.SetValue(IsProxyKeyboardFocusTargetProperty, value);
        }

        private static void OnIsProxyKeyboardFocusTargetChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is not TextBox textBox)
            {
                return;
            }

            if ((bool)e.NewValue)
            {
                ApplyProxyFocusVisual(textBox);
            }
            else
            {
                ClearProxyFocusVisual(textBox);
            }
        }

        private static void ApplyProxyFocusVisual(TextBox textBox)
        {
            if (!(bool)textBox.GetValue(HasOriginalVisualProperty))
            {
                textBox.SetValue(OriginalBorderBrushProperty, textBox.BorderBrush);
                textBox.SetValue(
                    OriginalInactiveSelectionHighlightProperty,
                    (bool)textBox.GetValue(TextBoxBase.IsInactiveSelectionHighlightEnabledProperty));
                textBox.SetValue(HasOriginalVisualProperty, true);
            }

            textBox.BorderBrush = ProxyBorderBrush;
            textBox.SetValue(TextBoxBase.IsInactiveSelectionHighlightEnabledProperty, true);
            EnsureCaretAdorner(textBox);
        }

        private static void ClearProxyFocusVisual(TextBox textBox)
        {
            if ((bool)textBox.GetValue(HasOriginalVisualProperty))
            {
                textBox.BorderBrush = (Brush?)textBox.GetValue(OriginalBorderBrushProperty);
                textBox.SetValue(
                    TextBoxBase.IsInactiveSelectionHighlightEnabledProperty,
                    (bool)textBox.GetValue(OriginalInactiveSelectionHighlightProperty));
                textBox.ClearValue(OriginalBorderBrushProperty);
                textBox.ClearValue(OriginalInactiveSelectionHighlightProperty);
                textBox.SetValue(HasOriginalVisualProperty, false);
            }

            if (textBox.GetValue(CaretAdornerProperty) is ProxyCaretAdorner adorner)
            {
                adorner.Dispose();
                textBox.ClearValue(CaretAdornerProperty);
            }
        }

        private static void EnsureCaretAdorner(TextBox textBox)
        {
            if (textBox.GetValue(CaretAdornerProperty) is ProxyCaretAdorner existing)
            {
                existing.InvalidateVisual();
                return;
            }

            if (!textBox.IsLoaded)
            {
                RoutedEventHandler? loadedHandler = null;
                loadedHandler = (_, _) =>
                {
                    textBox.Loaded -= loadedHandler;
                    if (GetIsProxyKeyboardFocusTarget(textBox))
                    {
                        EnsureCaretAdorner(textBox);
                    }
                };
                textBox.Loaded += loadedHandler;
                return;
            }

            AdornerLayer? layer = AdornerLayer.GetAdornerLayer(textBox);
            if (layer == null)
            {
                return;
            }

            ProxyCaretAdorner adorner = new ProxyCaretAdorner(textBox);
            layer.Add(adorner);
            textBox.SetValue(CaretAdornerProperty, adorner);
        }

        private sealed class ProxyCaretAdorner : Adorner, IDisposable
        {
            private readonly TextBox textBox;
            private readonly DispatcherTimer blinkTimer;
            private bool showCaret = true;
            private bool disposed;

            public ProxyCaretAdorner(TextBox textBox)
                : base(textBox)
            {
                this.textBox = textBox;
                IsHitTestVisible = false;

                textBox.SelectionChanged += TextBox_SelectionChanged;
                textBox.TextChanged += TextBox_TextChanged;
                textBox.SizeChanged += TextBox_SizeChanged;
                textBox.Unloaded += TextBox_Unloaded;

                blinkTimer = new DispatcherTimer(DispatcherPriority.Background, textBox.Dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(530)
                };
                blinkTimer.Tick += (_, _) =>
                {
                    showCaret = !showCaret;
                    InvalidateVisual();
                };
                blinkTimer.Start();
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                blinkTimer.Stop();
                textBox.SelectionChanged -= TextBox_SelectionChanged;
                textBox.TextChanged -= TextBox_TextChanged;
                textBox.SizeChanged -= TextBox_SizeChanged;
                textBox.Unloaded -= TextBox_Unloaded;
                if (ReferenceEquals(textBox.GetValue(CaretAdornerProperty), this))
                {
                    textBox.ClearValue(CaretAdornerProperty);
                }

                AdornerLayer.GetAdornerLayer(textBox)?.Remove(this);
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                base.OnRender(drawingContext);

                if (!GetIsProxyKeyboardFocusTarget(textBox)
                    || !textBox.IsVisible
                    || !textBox.IsEnabled)
                {
                    return;
                }

                if (textBox.SelectionLength > 0)
                {
                    DrawSelectionHighlight(drawingContext);
                    return;
                }

                if (!showCaret)
                {
                    return;
                }

                Rect caretRect = GetCaretRect();
                if (caretRect.IsEmpty || double.IsNaN(caretRect.X) || double.IsNaN(caretRect.Y))
                {
                    return;
                }

                double height = caretRect.Height > 0 ? caretRect.Height : Math.Max(1, textBox.FontSize + 2);
                Brush caretBrush = textBox.CaretBrush ?? textBox.Foreground;
                Rect drawRect = new Rect(Math.Round(caretRect.X) + 0.5, caretRect.Y, 1, height);
                drawingContext.DrawRectangle(caretBrush, null, drawRect);
            }

            private void DrawSelectionHighlight(DrawingContext drawingContext)
            {
                int textLength = textBox.Text?.Length ?? 0;
                int selectionStart = Math.Clamp(textBox.SelectionStart, 0, textLength);
                int selectionEnd = Math.Clamp(selectionStart + textBox.SelectionLength, 0, textLength);
                if (selectionStart >= selectionEnd)
                {
                    return;
                }

                Brush selectionBrush = textBox.SelectionBrush ?? SystemColors.HighlightBrush;
                drawingContext.PushOpacity(Math.Clamp(textBox.SelectionOpacity, 0.25, 0.75));
                try
                {
                    Rect? currentRun = null;
                    for (int index = selectionStart; index < selectionEnd; index++)
                    {
                        Rect rect = GetCharacterSelectionRect(index);
                        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
                        {
                            continue;
                        }

                        if (currentRun.HasValue && Math.Abs(currentRun.Value.Y - rect.Y) < 1.0)
                        {
                            currentRun = Rect.Union(currentRun.Value, rect);
                            continue;
                        }

                        if (currentRun.HasValue)
                        {
                            drawingContext.DrawRectangle(selectionBrush, null, currentRun.Value);
                        }

                        currentRun = rect;
                    }

                    if (currentRun.HasValue)
                    {
                        drawingContext.DrawRectangle(selectionBrush, null, currentRun.Value);
                    }
                }
                finally
                {
                    drawingContext.Pop();
                }
            }

            private Rect GetCaretRect()
            {
                int textLength = textBox.Text?.Length ?? 0;
                int caretIndex = Math.Clamp(textBox.CaretIndex, 0, textLength);
                try
                {
                    if (caretIndex > 0)
                    {
                        Rect trailingPrevious = textBox.GetRectFromCharacterIndex(caretIndex - 1, true);
                        if (!trailingPrevious.IsEmpty)
                        {
                            return trailingPrevious;
                        }
                    }

                    Rect leadingCurrent = textBox.GetRectFromCharacterIndex(caretIndex, false);
                    if (!leadingCurrent.IsEmpty)
                    {
                        return leadingCurrent;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                }

                return Rect.Empty;
            }

            private Rect GetCharacterSelectionRect(int characterIndex)
            {
                try
                {
                    Rect leading = textBox.GetRectFromCharacterIndex(characterIndex, false);
                    Rect trailing = textBox.GetRectFromCharacterIndex(characterIndex, true);
                    if (leading.IsEmpty || trailing.IsEmpty)
                    {
                        return Rect.Empty;
                    }

                    double x = Math.Min(leading.X, trailing.X);
                    double width = Math.Abs(trailing.X - leading.X);
                    if (width < 1)
                    {
                        width = Math.Max(1, textBox.FontSize * 0.5);
                    }

                    double height = Math.Max(leading.Height, trailing.Height);
                    if (height <= 0)
                    {
                        height = Math.Max(1, textBox.FontSize + 2);
                    }

                    return new Rect(x, leading.Y, width, height);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return Rect.Empty;
                }
            }

            private void ResetBlink()
            {
                showCaret = true;
                blinkTimer.Stop();
                blinkTimer.Start();
                InvalidateVisual();
            }

            private void TextBox_SelectionChanged(object sender, RoutedEventArgs e)
            {
                ResetBlink();
            }

            private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
            {
                ResetBlink();
            }

            private void TextBox_SizeChanged(object sender, SizeChangedEventArgs e)
            {
                InvalidateVisual();
            }

            private void TextBox_Unloaded(object sender, RoutedEventArgs e)
            {
                Dispose();
            }
        }
    }
}
