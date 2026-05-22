using System;
using System.Diagnostics;
using System.Drawing;
using Common;
using Forms = System.Windows.Forms;

namespace OceanyaClient.Features.FileHivemind
{
    public enum FileHivemindAgentNotificationSeverity
    {
        Info = 0,
        Success = 1,
        Warning = 2,
        Error = 3
    }

    public interface IFileHivemindAgentNotifier
    {
        void ShowNotification(string title, string message, FileHivemindAgentNotificationSeverity severity);

        void ShowProgressNotification(
            string operationKey,
            string title,
            string message,
            string detail,
            double? progressFraction);

        void UpdateProgressNotification(
            string operationKey,
            string detail,
            double? progressFraction);

        void CloseProgressNotification(string operationKey);

        void SetStatusText(string statusText);

        void ClearStatusText();
    }

    public sealed class NullFileHivemindAgentNotifier : IFileHivemindAgentNotifier
    {
        public void ShowNotification(string title, string message, FileHivemindAgentNotificationSeverity severity)
        {
        }

        public void ShowProgressNotification(
            string operationKey,
            string title,
            string message,
            string detail,
            double? progressFraction)
        {
        }

        public void UpdateProgressNotification(
            string operationKey,
            string detail,
            double? progressFraction)
        {
        }

        public void CloseProgressNotification(string operationKey)
        {
        }

        public void SetStatusText(string statusText)
        {
        }

        public void ClearStatusText()
        {
        }
    }

    public sealed class FileHivemindTrayIconController : IDisposable, IFileHivemindAgentNotifier
    {
        private const string DefaultTrayText = "The Oceanyan File Hivemind";
        private readonly Action exitRequested;
        private readonly Func<bool> openMainApplication;
        private readonly Icon trayIconImage;
        private readonly Forms.NotifyIcon notifyIcon;
        private readonly Forms.Control uiThreadControl;
        private readonly Func<bool> desktopToastEnabledResolver;
        private readonly FileHivemindDesktopToastPresenter desktopToastPresenter;
        private bool disposed;

        public FileHivemindTrayIconController(
            Action exitRequested,
            Func<bool>? openMainApplication = null,
            Func<bool>? desktopToastEnabledResolver = null)
        {
            this.exitRequested = exitRequested ?? throw new ArgumentNullException(nameof(exitRequested));
            this.openMainApplication = openMainApplication ?? OpenMainApplication;
            this.desktopToastEnabledResolver = desktopToastEnabledResolver ?? IsDesktopToastEnabled;
            trayIconImage = LoadTrayIcon();
            desktopToastPresenter = new FileHivemindDesktopToastPresenter(() => (Icon)trayIconImage.Clone());
            uiThreadControl = new Forms.Control();
            uiThreadControl.CreateControl();
            _ = uiThreadControl.Handle;

            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open Oceanya Client", null, (_, _) => this.openMainApplication());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit Background Sync", null, (_, _) => this.exitRequested());

            notifyIcon = new Forms.NotifyIcon
            {
                Icon = trayIconImage,
                Visible = true,
                Text = DefaultTrayText,
                ContextMenuStrip = menu
            };
            notifyIcon.DoubleClick += (_, _) => this.openMainApplication();
        }

        public void ShowNotification(string title, string message, FileHivemindAgentNotificationSeverity severity)
        {
            if (disposed)
            {
                return;
            }

            InvokeOnUiThread(() =>
            {
                if (!desktopToastEnabledResolver())
                {
                    return;
                }

                desktopToastPresenter.Show(
                    TrimForBalloon(title, 96),
                    TrimForBalloon(message, 280),
                    severity);
            });
        }

        public void ShowProgressNotification(
            string operationKey,
            string title,
            string message,
            string detail,
            double? progressFraction)
        {
            if (disposed)
            {
                return;
            }

            InvokeOnUiThread(() =>
            {
                if (!desktopToastEnabledResolver())
                {
                    return;
                }

                desktopToastPresenter.ShowProgress(
                    operationKey,
                    TrimForBalloon(title, 96),
                    TrimForBalloon(message, 280),
                    TrimForBalloon(detail, 320),
                    progressFraction);
            });
        }

        public void UpdateProgressNotification(
            string operationKey,
            string detail,
            double? progressFraction)
        {
            if (disposed)
            {
                return;
            }

            InvokeOnUiThread(() =>
            {
                if (!desktopToastEnabledResolver())
                {
                    return;
                }

                desktopToastPresenter.UpdateProgress(
                    operationKey,
                    TrimForBalloon(detail, 320),
                    progressFraction);
            });
        }

        public void CloseProgressNotification(string operationKey)
        {
            if (disposed)
            {
                return;
            }

            InvokeOnUiThread(() =>
            {
                desktopToastPresenter.CloseProgress(operationKey);
            });
        }

        public void SetStatusText(string statusText)
        {
            if (disposed)
            {
                return;
            }

            InvokeOnUiThread(() =>
            {
                notifyIcon.Text = BuildTrayText(statusText);
            });
        }

        public void ClearStatusText()
        {
            if (disposed)
            {
                return;
            }

            InvokeOnUiThread(() =>
            {
                notifyIcon.Text = DefaultTrayText;
            });
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            desktopToastPresenter.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            uiThreadControl.Dispose();
            trayIconImage.Dispose();
        }

        private static Icon LoadTrayIcon()
        {
            string? executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                Icon? extractedIcon = Icon.ExtractAssociatedIcon(executablePath);
                if (extractedIcon != null)
                {
                    return (Icon)extractedIcon.Clone();
                }
            }

            return (Icon)SystemIcons.Application.Clone();
        }

        private static bool OpenMainApplication()
        {
            string executablePath = FileHivemindBackgroundAgentCommandLine.ResolveMainApplicationExecutablePath();

            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true
            });
            return true;
        }

        private static bool IsDesktopToastEnabled()
        {
            try
            {
                SaveData snapshot = SaveFile.LoadSnapshotFromDisk();
                return snapshot.FileHivemind?.ShowDesktopToasts == true;
            }
            catch
            {
                return false;
            }
        }

        private void InvokeOnUiThread(Action action)
        {
            if (disposed)
            {
                return;
            }

            if (uiThreadControl.IsDisposed || !uiThreadControl.IsHandleCreated)
            {
                return;
            }

            try
            {
                if (uiThreadControl.InvokeRequired)
                {
                    uiThreadControl.BeginInvoke(action);
                    return;
                }

                action();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException ex)
            {
                CustomConsole.Warning("File Hivemind tray UI action skipped because the tray handle is unavailable.", ex);
            }
        }

        private static string BuildTrayText(string? statusText)
        {
            string trimmedStatus = statusText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedStatus))
            {
                return DefaultTrayText;
            }

            return TrimForBalloon("Hivemind: " + trimmedStatus, 63);
        }

        private static string TrimForBalloon(string? value, int maxLength)
        {
            string trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length <= maxLength)
            {
                return trimmed;
            }

            if (maxLength <= 3)
            {
                return trimmed[..Math.Max(0, maxLength)];
            }

            return trimmed[..(maxLength - 3)] + "...";
        }
    }
}
