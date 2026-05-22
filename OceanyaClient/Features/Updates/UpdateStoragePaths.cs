using System;
using System.IO;

namespace OceanyaClient.Features.Updates
{
    public sealed class UpdateStoragePaths
    {
        public UpdateStoragePaths(string? localAppDataRoot = null)
            : this(UpdateEnvironment.Stable, localAppDataRoot)
        {
        }

        public UpdateStoragePaths(UpdateEnvironment environment, string? localAppDataRoot = null)
        {
            string root = string.IsNullOrWhiteSpace(localAppDataRoot)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    environment.AppDataProfileName,
                    "Updates")
                : localAppDataRoot;

            Root = root;
            Downloads = Path.Combine(root, "downloads");
            Staged = Path.Combine(root, "staged");
            Backups = Path.Combine(root, "backups");
            Logs = Path.Combine(root, "logs");
            Handoffs = Path.Combine(root, "handoffs");
        }

        public string Root { get; }
        public string Downloads { get; }
        public string Staged { get; }
        public string Backups { get; }
        public string Logs { get; }
        public string Handoffs { get; }
        public string UpdaterLogPath => Path.Combine(Logs, "updater.log");

        public void EnsureCreated()
        {
            Directory.CreateDirectory(Downloads);
            Directory.CreateDirectory(Staged);
            Directory.CreateDirectory(Backups);
            Directory.CreateDirectory(Logs);
            Directory.CreateDirectory(Handoffs);
        }
    }
}
