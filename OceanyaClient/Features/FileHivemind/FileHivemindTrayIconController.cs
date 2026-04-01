using System;
using System.Diagnostics;
using System.Drawing;
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

        void SetStatusText(string statusText);

        void ClearStatusText();
    }

    public sealed class NullFileHivemindAgentNotifier : IFileHivemindAgentNotifier
    {
        public void ShowNotification(string title, string message, FileHivemindAgentNotificationSeverity severity)
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
        private bool disposed;

        public FileHivemindTrayIconController(
            Action exitRequested,
            Func<bool>? openMainApplication = null)
        {
            this.exitRequested = exitRequested ?? throw new ArgumentNullException(nameof(exitRequested));
            this.openMainApplication = openMainApplication ?? OpenMainApplication;
            trayIconImage = LoadTrayIcon();
            uiThreadControl = new Forms.Control();
            uiThreadControl.CreateControl();

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
                notifyIcon.BalloonTipIcon = severity switch
                {
                    FileHivemindAgentNotificationSeverity.Success => Forms.ToolTipIcon.Info,
                    FileHivemindAgentNotificationSeverity.Warning => Forms.ToolTipIcon.Warning,
                    FileHivemindAgentNotificationSeverity.Error => Forms.ToolTipIcon.Error,
                    _ => Forms.ToolTipIcon.None
                };
                notifyIcon.BalloonTipTitle = TrimForBalloon(title, 63);
                notifyIcon.BalloonTipText = TrimForBalloon(message, 255);
                notifyIcon.ShowBalloonTip(5000);
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

        private void InvokeOnUiThread(Action action)
        {
            if (disposed)
            {
                return;
            }

            if (uiThreadControl.IsDisposed)
            {
                return;
            }

            if (uiThreadControl.InvokeRequired)
            {
                uiThreadControl.BeginInvoke(action);
                return;
            }

            action();
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
