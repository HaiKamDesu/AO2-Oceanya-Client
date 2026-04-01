using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

public static class WindowHelper
{
    private static readonly List<Window> windows = new();

    public static void AddWindow(Window window)
    {
        window.Dispatcher.Invoke(() =>
        {
            windows.Add(window);
            window.Loaded += (s, e) => PositionWindow(window);
            window.Closed += (s, e) =>
            {
                window.Dispatcher.Invoke(() => windows.Remove(window));
            };
        });
    }

    private static void PositionWindow(Window window)
    {
        if (window.WindowStartupLocation == WindowStartupLocation.Manual
            && IsFinite(window.Left)
            && IsFinite(window.Top))
        {
            // Respect explicitly restored manual popup position.
            return;
        }

        if (window.Owner != null && window.Owner.IsVisible)
        {
            CenterOnOwner(window, window.Owner);
            return;
        }

        int index = -1;

        window.Dispatcher.Invoke(() =>
        {
            index = windows.IndexOf(window);
        });

        if (index <= 0)
        {
            CenterOnScreen(window);
            return;
        }

        Window? ownerWindow = windows[index - 1];

        if (ownerWindow == null)
        {
            CenterOnScreen(window);
            return;
        }

        CenterOnOwner(window, ownerWindow);
    }

    private static void CenterOnScreen(Window window)
    {
        window.Dispatcher.Invoke(() =>
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.UpdateLayout();

            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            window.Left = (screenWidth - window.ActualWidth) / 2;
            window.Top = (screenHeight - window.ActualHeight) / 2;
        });
    }

    private static void CenterOnOwner(Window window, Window ownerWindow)
    {
        double ownerLeft = 0;
        double ownerTop = 0;
        double ownerWidth = 0;
        double ownerHeight = 0;

        ownerWindow.Dispatcher.Invoke(() =>
        {
            if (!ownerWindow.IsVisible)
            {
                return;
            }

            ownerWindow.UpdateLayout();
            double width = ownerWindow.ActualWidth > 0 ? ownerWindow.ActualWidth : ownerWindow.Width;
            double height = ownerWindow.ActualHeight > 0 ? ownerWindow.ActualHeight : ownerWindow.Height;
            Point topLeft = new Point(ownerWindow.Left, ownerWindow.Top);

            try
            {
                Point screenPoint = ownerWindow.PointToScreen(new Point(0, 0));
                PresentationSource? source = PresentationSource.FromVisual(ownerWindow);
                if (source?.CompositionTarget != null)
                {
                    Matrix transform = source.CompositionTarget.TransformFromDevice;
                    topLeft = transform.Transform(screenPoint);
                }
                else
                {
                    topLeft = screenPoint;
                }
            }
            catch
            {
                Rect fallbackBounds = ownerWindow.WindowState == WindowState.Normal
                    ? new Rect(ownerWindow.Left, ownerWindow.Top, width, height)
                    : ownerWindow.RestoreBounds;
                topLeft = new Point(fallbackBounds.Left, fallbackBounds.Top);
                width = fallbackBounds.Width > 0 ? fallbackBounds.Width : width;
                height = fallbackBounds.Height > 0 ? fallbackBounds.Height : height;
            }

            ownerLeft = topLeft.X;
            ownerTop = topLeft.Y;
            ownerWidth = width;
            ownerHeight = height;
        });

        if (!ownerWindow.IsVisible || ownerWidth <= 0 || ownerHeight <= 0)
        {
            CenterOnScreen(window);
            return;
        }

        window.Dispatcher.Invoke(() =>
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.UpdateLayout();

            double ownerCenterX = ownerLeft + (ownerWidth / 2);
            double ownerCenterY = ownerTop + (ownerHeight / 2);
            window.Left = ownerCenterX - (window.ActualWidth / 2);
            window.Top = ownerCenterY - (window.ActualHeight / 2);
        });
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public static void RemoveWindow(Window window)
    {
        window.Dispatcher.Invoke(() =>
        {
            windows.Remove(window);
        });
    }
}
