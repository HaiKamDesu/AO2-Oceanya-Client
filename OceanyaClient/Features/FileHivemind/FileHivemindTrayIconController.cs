using System;
using System.Diagnostics;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace OceanyaClient.Features.FileHivemind
{
    public sealed class FileHivemindTrayIconController : IDisposable
    {
        private readonly Action exitRequested;
        private readonly Func<bool> openMainApplication;
        private readonly Icon trayIconImage;
        private readonly Forms.NotifyIcon notifyIcon;
        private bool disposed;

        public FileHivemindTrayIconController(
            Action exitRequested,
            Func<bool>? openMainApplication = null)
        {
            this.exitRequested = exitRequested ?? throw new ArgumentNullException(nameof(exitRequested));
            this.openMainApplication = openMainApplication ?? OpenMainApplication;
            trayIconImage = LoadTrayIcon();

            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open Oceanya Client", null, (_, _) => this.openMainApplication());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit Background Sync", null, (_, _) => this.exitRequested());

            notifyIcon = new Forms.NotifyIcon
            {
                Icon = trayIconImage,
                Visible = true,
                Text = "The Oceanyan File Hivemind",
                ContextMenuStrip = menu
            };
            notifyIcon.DoubleClick += (_, _) => this.openMainApplication();
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
            string? executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true
            });
            return true;
        }
    }
}
