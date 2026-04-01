using Common;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace OceanyaClient.Features.GoogleDriveSync
{
    public static class GoogleDriveManagedLocalFolderMarkerService
    {
        public const string MarkerIconFileName = ".oceanya_folder_icon.ico";
        private const string DesktopIniFileName = "desktop.ini";
        private const uint ShgfiIcon = 0x000000100;
        private const uint ShgfiLargeIcon = 0x000000000;
        private const uint ShgfiUseFileAttributes = 0x000000010;
        private const uint FileAttributeDirectory = 0x00000010;
        private const uint ShcneUpdateDir = 0x00001000;
        private const uint ShcneUpdateItem = 0x00002000;
        private const uint ShcnfPathW = 0x0005;

        public static void EnsureMarkerIfNeeded(GoogleDriveSyncSettings settings)
        {
            if (settings == null || !settings.IsOceanyaManagedLocalFolder)
            {
                return;
            }

            try
            {
                EnsureMarker(settings.LocalFolderPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                CustomConsole.Warning("Could not update the managed local folder marker.", ex);
            }
            catch (IOException ex)
            {
                CustomConsole.Warning("Could not update the managed local folder marker.", ex);
            }
        }

        public static void EnsureMarker(string? folderPath)
        {
            string trimmedFolderPath = folderPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedFolderPath))
            {
                return;
            }

            string normalizedFolderPath = Path.GetFullPath(trimmedFolderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Directory.CreateDirectory(normalizedFolderPath);

            string iconPath = Path.Combine(normalizedFolderPath, MarkerIconFileName);
            string desktopIniPath = Path.Combine(normalizedFolderPath, DesktopIniFileName);

            EnsureIconFile(iconPath);
            EnsureDesktopIni(desktopIniPath);
            EnsureFolderAttributes(normalizedFolderPath);
            NotifyShellChanged(iconPath);
            NotifyShellChanged(desktopIniPath);
            NotifyShellChanged(normalizedFolderPath, isDirectory: true);
        }

        private static void EnsureIconFile(string iconPath)
        {
            if (HasExistingMarkerIcon(iconPath))
            {
                ApplyMarkerFileAttributes(iconPath);
                return;
            }

            ClearMarkerFileAttributesIfPresent(iconPath);

            using Icon folderIcon = LoadFolderIcon();
            using Icon applicationIcon = LoadApplicationIcon();
            using Bitmap canvas = new Bitmap(64, 64);
            using Graphics graphics = Graphics.FromImage(canvas);
            graphics.Clear(Color.Transparent);
            graphics.DrawIcon(folderIcon, new Rectangle(0, 0, 64, 64));
            graphics.DrawIcon(applicationIcon, new Rectangle(18, 0, 30, 30));

            IntPtr handle = canvas.GetHicon();
            try
            {
                using Icon compositeIcon = (Icon)Icon.FromHandle(handle).Clone();
                using FileStream stream = new FileStream(iconPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                compositeIcon.Save(stream);
            }
            finally
            {
                DestroyIcon(handle);
            }

            ApplyMarkerFileAttributes(iconPath);
        }

        private static void EnsureDesktopIni(string desktopIniPath)
        {
            string content =
                "[.ShellClassInfo]" + Environment.NewLine
                + "IconResource=" + MarkerIconFileName + ",0" + Environment.NewLine
                + "InfoTip=This folder is managed by The Oceanyan File Hivemind." + Environment.NewLine;

            if (!File.Exists(desktopIniPath)
                || !string.Equals(File.ReadAllText(desktopIniPath), content, StringComparison.Ordinal))
            {
                ClearMarkerFileAttributesIfPresent(desktopIniPath);
                File.WriteAllText(desktopIniPath, content);
            }

            ApplyMarkerFileAttributes(desktopIniPath);
        }

        private static void EnsureFolderAttributes(string folderPath)
        {
            FileAttributes attributes = File.GetAttributes(folderPath);
            if ((attributes & FileAttributes.ReadOnly) == 0)
            {
                File.SetAttributes(folderPath, attributes | FileAttributes.ReadOnly);
            }
        }

        private static bool HasExistingMarkerIcon(string iconPath)
        {
            try
            {
                return File.Exists(iconPath) && new FileInfo(iconPath).Length > 0;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (IOException)
            {
                return true;
            }
        }

        private static void ClearMarkerFileAttributesIfPresent(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            FileAttributes attributes = File.GetAttributes(filePath);
            attributes &= ~FileAttributes.Hidden;
            attributes &= ~FileAttributes.System;
            File.SetAttributes(filePath, attributes);
        }

        private static void ApplyMarkerFileAttributes(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            FileAttributes attributes = File.GetAttributes(filePath);
            File.SetAttributes(filePath, attributes | FileAttributes.Hidden | FileAttributes.System);
        }

        private static Icon LoadApplicationIcon()
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

        private static Icon LoadFolderIcon()
        {
            SHFILEINFO fileInfo = new SHFILEINFO();
            IntPtr result = SHGetFileInfo(
                "folder",
                FileAttributeDirectory,
                ref fileInfo,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                ShgfiIcon | ShgfiLargeIcon | ShgfiUseFileAttributes);
            if (result != IntPtr.Zero && fileInfo.hIcon != IntPtr.Zero)
            {
                try
                {
                    using Icon extractedIcon = Icon.FromHandle(fileInfo.hIcon);
                    return (Icon)extractedIcon.Clone();
                }
                finally
                {
                    DestroyIcon(fileInfo.hIcon);
                }
            }

            return (Icon)SystemIcons.WinLogo.Clone();
        }

        private static void NotifyShellChanged(string path, bool isDirectory = false)
        {
            string trimmedPath = path?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedPath))
            {
                return;
            }

            uint eventId = isDirectory ? ShcneUpdateDir : ShcneUpdateItem;
            SHChangeNotify(eventId, ShcnfPathW, trimmedPath, IntPtr.Zero);
        }

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(
            uint wEventId,
            uint uFlags,
            string dwItem1,
            IntPtr dwItem2);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }
    }
}
