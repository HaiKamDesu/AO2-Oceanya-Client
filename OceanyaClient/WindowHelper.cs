using System.Collections.Generic;
using System.Windows;

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

        Window? ownerWindow = null;

        // Retrieve ownerWindow safely
        ownerWindow = windows[index - 1];

        if (ownerWindow == null)
        {
            CenterOnScreen(window);
            return;
        }

        // Safely retrieve ownerWindow visibility and position
        double ownerLeft = 0;
        double ownerTop = 0;
        double ownerWidth = 0;
        double ownerHeight = 0;

        ownerWindow.Dispatcher.Invoke(() =>
        {
            if (ownerWindow.IsVisible)
            {
                ownerWindow.UpdateLayout();
                ownerWindow.Dispatcher.Invoke(() =>
                {
                    ownerHeight = ownerWindow.ActualHeight;
                    ownerWidth = ownerWindow.ActualWidth;
                    ownerLeft = ownerWindow.Left;
                    ownerTop = ownerWindow.Top;
                });
            }
        });

        if (!ownerWindow.IsVisible || ownerWidth == 0 || ownerHeight == 0)
        {
            CenterOnScreen(window);
            return;
        }

        // Finally, position your window safely on its thread:
        window.Dispatcher.Invoke(() =>
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.UpdateLayout();  // Force measurement

            double ownerCenterX = ownerLeft + (ownerWidth / 2);
            double ownerCenterY = ownerTop + (ownerHeight / 2);

            window.Left = ownerCenterX - (window.ActualWidth / 2);
            window.Top = ownerCenterY - (window.ActualHeight / 2);
        });
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
