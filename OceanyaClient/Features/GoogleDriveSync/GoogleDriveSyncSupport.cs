using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OceanyaClient.Features.GoogleDriveSync
{
    public interface ISecretProtector
    {
        byte[] Protect(byte[] value);

        byte[] Unprotect(byte[] value);
    }

    public sealed class DpapiSecretProtector : ISecretProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OceanyaClient.GoogleDriveSync");

        public byte[] Protect(byte[] value)
        {
            return ProtectedData.Protect(value, Entropy, DataProtectionScope.CurrentUser);
        }

        public byte[] Unprotect(byte[] value)
        {
            return ProtectedData.Unprotect(value, Entropy, DataProtectionScope.CurrentUser);
        }
    }

    public sealed class GoogleDriveSecureTokenStore
    {
        private readonly string rootDirectory;
        private readonly ISecretProtector secretProtector;

        public GoogleDriveSecureTokenStore(string? rootDirectory = null, ISecretProtector? secretProtector = null)
        {
            this.rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OceanyaClient",
                    "google_drive_tokens")
                : rootDirectory;
            this.secretProtector = secretProtector ?? new DpapiSecretProtector();
        }

        public void Save(string tokenStoreKey, GoogleDriveTokenSet tokens)
        {
            if (string.IsNullOrWhiteSpace(tokenStoreKey))
            {
                throw new ArgumentException("A token store key is required.", nameof(tokenStoreKey));
            }

            Directory.CreateDirectory(rootDirectory);
            GoogleDriveTokenSet normalizedTokens = new GoogleDriveTokenSet
            {
                AccessToken = tokens.AccessToken?.Trim() ?? string.Empty,
                RefreshToken = tokens.RefreshToken?.Trim() ?? string.Empty,
                AccessTokenExpiresUtc = tokens.AccessTokenExpiresUtc
            };

            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(normalizedTokens);
            byte[] ciphertext = secretProtector.Protect(plaintext);
            File.WriteAllBytes(GetFilePath(tokenStoreKey), ciphertext);
        }

        public GoogleDriveTokenSet? Load(string tokenStoreKey)
        {
            string filePath = GetFilePath(tokenStoreKey);
            if (!File.Exists(filePath))
            {
                return null;
            }

            byte[] ciphertext = File.ReadAllBytes(filePath);
            byte[] plaintext = secretProtector.Unprotect(ciphertext);
            return JsonSerializer.Deserialize<GoogleDriveTokenSet>(plaintext);
        }

        public void Delete(string tokenStoreKey)
        {
            string filePath = GetFilePath(tokenStoreKey);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        public string GetFilePath(string tokenStoreKey)
        {
            string sanitized = string.IsNullOrWhiteSpace(tokenStoreKey)
                ? "default"
                : tokenStoreKey.Trim();
            return Path.Combine(rootDirectory, sanitized + ".bin");
        }
    }

    public static class GoogleDriveInviteSerializer
    {
        public static string Serialize(GoogleDriveInvite invite)
        {
            return JsonSerializer.Serialize(invite, new JsonSerializerOptions { WriteIndented = true });
        }

        public static GoogleDriveInvite Parse(string input)
        {
            string value = input?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Invite text is empty.");
            }

            if (value.StartsWith("{", StringComparison.Ordinal))
            {
                GoogleDriveInvite? parsed = JsonSerializer.Deserialize<GoogleDriveInvite>(value);
                if (parsed == null || string.IsNullOrWhiteSpace(parsed.FolderId))
                {
                    throw new InvalidOperationException("Invite JSON is missing a Google Drive folder ID.");
                }

                parsed.Provider = "google_drive";
                parsed.FolderId = parsed.FolderId.Trim();
                parsed.FolderName = parsed.FolderName?.Trim() ?? string.Empty;
                return parsed;
            }

            string folderId = ExtractFolderId(value);
            if (string.IsNullOrWhiteSpace(folderId))
            {
                throw new InvalidOperationException("Could not find a Google Drive folder ID in the provided text.");
            }

            return new GoogleDriveInvite
            {
                FolderId = folderId,
                FolderName = string.Empty
            };
        }

        public static string ExtractFolderId(string input)
        {
            string value = input?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            {
                string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < segments.Length - 1; i++)
                {
                    if (string.Equals(segments[i], "folders", StringComparison.OrdinalIgnoreCase))
                    {
                        return SanitizeFolderId(segments[i + 1]);
                    }
                }

                Dictionary<string, string> queryValues = ParseQueryString(uri.Query);
                if (queryValues.TryGetValue("id", out string? queryFolderId))
                {
                    return SanitizeFolderId(queryFolderId);
                }
            }

            return SanitizeFolderId(value);
        }

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in (query ?? string.Empty).TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = pair.Split('=', 2);
                string key = Uri.UnescapeDataString(parts[0]);
                string value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    values[key] = value;
                }
            }

            return values;
        }

        private static string SanitizeFolderId(string value)
        {
            string candidate = (value ?? string.Empty).Trim().Trim('/');
            StringBuilder builder = new StringBuilder(candidate.Length);
            foreach (char character in candidate)
            {
                if (char.IsLetterOrDigit(character) || character == '-' || character == '_')
                {
                    builder.Append(character);
                }
            }

            string sanitized = builder.ToString();
            return sanitized.Length < 10 ? string.Empty : sanitized;
        }
    }

    public static class FileHivemindConnectionExchangeSerializer
    {
        public static string Serialize(FileHivemindConnectionProfile connection)
        {
            FileHivemindConnectionExchangeDocument document = new FileHivemindConnectionExchangeDocument
            {
                Connection = CreateSanitizedConnectionCopy(connection)
            };

            return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        }

        public static FileHivemindConnectionProfile Parse(string input)
        {
            string value = input?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Connection file text is empty.");
            }

            FileHivemindConnectionExchangeDocument? document =
                JsonSerializer.Deserialize<FileHivemindConnectionExchangeDocument>(value);
            if (document?.Connection == null)
            {
                throw new InvalidOperationException("Connection file is missing connection data.");
            }

            return CreateSanitizedConnectionCopy(document.Connection);
        }

        public static FileHivemindConnectionProfile CreateImportReadyProfile(FileHivemindConnectionProfile connection)
        {
            FileHivemindConnectionProfile imported = CreateSanitizedConnectionCopy(connection);
            imported.Id = Guid.NewGuid().ToString("N");

            if (string.Equals(imported.ProviderId, FileHivemindProviderIds.GoogleDrive, StringComparison.OrdinalIgnoreCase))
            {
                imported.GoogleDrive.TokenStoreKey = Guid.NewGuid().ToString("N");
                imported.GoogleDrive.LocalFolderPath = GoogleDriveClientAssetIntegration.BuildManagedLocalFolderPath(
                    imported.EffectiveDisplayName,
                    imported.GoogleDrive.RemoteFolderName,
                    imported.GoogleDrive.RemoteFolderId);
                imported.GoogleDrive.IsOceanyaManagedLocalFolder = true;
                imported.GoogleDrive.UseExistingMountPath = false;
            }

            return imported;
        }

        private static FileHivemindConnectionProfile CreateSanitizedConnectionCopy(FileHivemindConnectionProfile connection)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            string providerId = connection.ProviderId?.Trim() ?? string.Empty;
            if (!string.Equals(providerId, FileHivemindProviderIds.GoogleDrive, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only Google Drive connection files are supported right now.");
            }

            string remoteFolderId = connection.GoogleDrive.RemoteFolderId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(remoteFolderId))
            {
                throw new InvalidOperationException("Connection file is missing a Google Drive folder ID.");
            }

            return new FileHivemindConnectionProfile
            {
                Id = string.Empty,
                DisplayName = connection.DisplayName?.Trim() ?? string.Empty,
                ProviderId = FileHivemindProviderIds.GoogleDrive,
                GoogleDrive = new GoogleDriveSyncSettings
                {
                    OAuthClientId = string.Empty,
                    OAuthClientSecret = string.Empty,
                    TokenStoreKey = string.Empty,
                    LastSignedInEmail = string.Empty,
                    LastSignedInDisplayName = string.Empty,
                    RemoteFolderId = remoteFolderId,
                    RemoteFolderName = connection.GoogleDrive.RemoteFolderName?.Trim() ?? string.Empty,
                    LocalFolderPath = string.Empty,
                    IsOceanyaManagedLocalFolder = false,
                    AutoAddMountPath = connection.GoogleDrive.AutoAddMountPath,
                    MirrorDeletes = connection.GoogleDrive.MirrorDeletes,
                    UseExistingMountPath = false,
                    LastSyncUtc = null
                }
            };
        }
    }

    public static class GoogleDriveMountPathManager
    {
        public static bool EnsureMounted(string configIniPath, string mountPath)
        {
            string normalizedConfigPath = Path.GetFullPath(configIniPath ?? string.Empty);
            string normalizedMountPath = Path.GetFullPath(mountPath ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(normalizedConfigPath) || !File.Exists(normalizedConfigPath))
            {
                throw new FileNotFoundException("config.ini was not found.", normalizedConfigPath);
            }

            if (string.IsNullOrWhiteSpace(normalizedMountPath) || !Directory.Exists(normalizedMountPath))
            {
                throw new DirectoryNotFoundException("The selected mount path does not exist.");
            }

            string configDirectory = (Path.GetDirectoryName(normalizedConfigPath) ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string configMountParentDirectory = ResolveMountPathsParentDirectory(normalizedConfigPath);
            if (string.Equals(configDirectory, normalizedMountPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            List<string> lines = File.ReadAllLines(normalizedConfigPath).ToList();
            int mountPathsIndex = lines.FindIndex(line => line.StartsWith("mount_paths=", StringComparison.OrdinalIgnoreCase));
            List<string> existingMounts = new List<string>();

            if (mountPathsIndex >= 0)
            {
                string raw = lines[mountPathsIndex]["mount_paths=".Length..].Trim();
                if (!string.Equals(raw, "@Invalid()", StringComparison.OrdinalIgnoreCase))
                {
                    existingMounts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(value => value.Trim())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .ToList();
                }
            }

            bool alreadyMounted = existingMounts.Any(existing =>
            {
                try
                {
                    string fullExistingPath = TryResolveConfiguredMountPath(existing, configMountParentDirectory, out string resolvedPath)
                        ? resolvedPath
                        : Path.GetFullPath(existing).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return string.Equals(fullExistingPath, normalizedMountPath, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return string.Equals(existing, normalizedMountPath, StringComparison.OrdinalIgnoreCase);
                }
            });
            if (alreadyMounted)
            {
                return false;
            }

            existingMounts.Add(NormalizeMountPathForConfigValue(normalizedMountPath));
            string mountLine = "mount_paths=" + string.Join(",", existingMounts);
            if (mountPathsIndex >= 0)
            {
                lines[mountPathsIndex] = mountLine;
            }
            else
            {
                lines.Insert(0, mountLine);
            }

            File.WriteAllLines(normalizedConfigPath, lines);
            return true;
        }

        public static bool ReplaceMountedPath(string configIniPath, string oldMountPath, string newMountPath)
        {
            string normalizedConfigPath = Path.GetFullPath(configIniPath ?? string.Empty);
            string normalizedOldMountPath = Path.GetFullPath(oldMountPath ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedNewMountPath = Path.GetFullPath(newMountPath ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(normalizedConfigPath) || !File.Exists(normalizedConfigPath))
            {
                throw new FileNotFoundException("config.ini was not found.", normalizedConfigPath);
            }

            if (string.IsNullOrWhiteSpace(normalizedOldMountPath))
            {
                return EnsureMounted(normalizedConfigPath, normalizedNewMountPath);
            }

            if (string.IsNullOrWhiteSpace(normalizedNewMountPath) || !Directory.Exists(normalizedNewMountPath))
            {
                throw new DirectoryNotFoundException("The selected mount path does not exist.");
            }

            List<string> lines = File.ReadAllLines(normalizedConfigPath).ToList();
            int mountPathsIndex = lines.FindIndex(line => line.StartsWith("mount_paths=", StringComparison.OrdinalIgnoreCase));
            if (mountPathsIndex < 0)
            {
                return EnsureMounted(normalizedConfigPath, normalizedNewMountPath);
            }

            string configMountParentDirectory = ResolveMountPathsParentDirectory(normalizedConfigPath);
            string raw = lines[mountPathsIndex]["mount_paths=".Length..].Trim();
            List<string> existingMounts = string.Equals(raw, "@Invalid()", StringComparison.OrdinalIgnoreCase)
                ? new List<string>()
                : raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();

            string newConfigValue = NormalizeMountPathForConfigValue(normalizedNewMountPath);
            bool sawOldMount = false;
            bool sawNewMount = false;
            List<string> rewrittenMounts = new List<string>();
            foreach (string existing in existingMounts)
            {
                string comparablePath = existing;
                try
                {
                    comparablePath = TryResolveConfiguredMountPath(existing, configMountParentDirectory, out string resolvedPath)
                        ? resolvedPath
                        : Path.GetFullPath(existing).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch
                {
                    comparablePath = existing;
                }

                if (string.Equals(comparablePath, normalizedOldMountPath, StringComparison.OrdinalIgnoreCase))
                {
                    sawOldMount = true;
                    if (!sawNewMount)
                    {
                        rewrittenMounts.Add(newConfigValue);
                        sawNewMount = true;
                    }

                    continue;
                }

                if (string.Equals(comparablePath, normalizedNewMountPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!sawNewMount)
                    {
                        rewrittenMounts.Add(newConfigValue);
                        sawNewMount = true;
                    }

                    continue;
                }

                rewrittenMounts.Add(existing);
            }

            if (!sawOldMount)
            {
                return EnsureMounted(normalizedConfigPath, normalizedNewMountPath);
            }

            string mountLine = "mount_paths=" + (rewrittenMounts.Count == 0 ? "@Invalid()" : string.Join(",", rewrittenMounts));
            lines[mountPathsIndex] = mountLine;
            File.WriteAllLines(normalizedConfigPath, lines);
            return true;
        }

        public static string NormalizeMountPathForConfigValue(string mountPath)
        {
            return Path.GetFullPath(mountPath ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
        }

        private static string ResolveMountPathsParentDirectory(string configIniPath)
        {
            string configDirectory = Path.GetDirectoryName(configIniPath) ?? string.Empty;
            return (Path.GetDirectoryName(configDirectory) ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool TryResolveConfiguredMountPath(
            string configuredValue,
            string configMountParentDirectory,
            out string resolvedPath)
        {
            resolvedPath = string.Empty;
            List<string> candidates = new List<string> { configuredValue };
            if (!string.IsNullOrWhiteSpace(configMountParentDirectory))
            {
                candidates.Add(Path.Combine(configMountParentDirectory, configuredValue));
            }

            foreach (string candidate in candidates)
            {
                try
                {
                    string fullPath = Path.GetFullPath(candidate)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (!Directory.Exists(fullPath))
                    {
                        continue;
                    }

                    resolvedPath = fullPath;
                    return true;
                }
                catch
                {
                    // Ignore malformed configured mount entries while comparing.
                }
            }

            return false;
        }
    }

    public static class GoogleDriveClientAssetIntegration
    {
        public static readonly string ManagedLocalFolderRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OceanyaClient",
            "GoogleDriveSync");

        public static void EnsureMounted(
            string configIniPath,
            GoogleDriveSyncSettings settings,
            bool useExistingMountPath,
            Action<string>? statusSink = null)
        {
            string trimmedConfigPath = configIniPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedConfigPath))
            {
                throw new InvalidOperationException("No config.ini is configured in Oceanya.");
            }

            Directory.CreateDirectory(settings.LocalFolderPath);
            GoogleDriveManagedLocalFolderMarkerService.EnsureMarkerIfNeeded(settings);
            string configDirectory = Path.GetDirectoryName(Path.GetFullPath(trimmedConfigPath)) ?? string.Empty;
            string normalizedLocalPath = Path.GetFullPath(settings.LocalFolderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!useExistingMountPath
                && string.Equals(
                    configDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    normalizedLocalPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The sync folder cannot be the same folder that contains config.ini. Use a dedicated mount folder instead.");
            }

            bool changed = GoogleDriveMountPathManager.EnsureMounted(trimmedConfigPath, settings.LocalFolderPath);
            Globals.UpdateConfigINI(trimmedConfigPath);
            statusSink?.Invoke(changed
                ? "Added the local sync folder to config.ini mount paths."
                : "Local sync folder was already available to AO through mount paths.");
        }

        public static void RefreshMountedAssets(
            string configIniPath,
            GoogleDriveSyncSettings settings,
            GoogleDriveSyncLocalChangeSet? localChanges,
            Action<string>? progress)
        {
            string trimmedConfigPath = configIniPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedConfigPath))
            {
                return;
            }

            Globals.UpdateConfigINI(trimmedConfigPath);
            if (localChanges != null && localChanges.HasAnyChanges)
            {
                ClientAssetRefreshService.RefreshChangedAssets(
                    trimmedConfigPath,
                    settings.LocalFolderPath,
                    localChanges,
                    progress);
            }
        }

        public static string BuildManagedLocalFolderPath(string folderName, string folderId)
        {
            return BuildManagedLocalFolderPath(string.Empty, folderName, folderId);
        }

        public static string BuildManagedLocalFolderPath(string connectionName, string folderName, string folderId)
        {
            string resolvedConnectionName = string.IsNullOrWhiteSpace(connectionName)
                ? "Unnamed Oceanya Connection"
                : connectionName.Trim();
            string resolvedFolderName = string.IsNullOrWhiteSpace(folderName)
                ? "Unnamed Google Drive Folder"
                : folderName.Trim();
            string resolvedFolderId = string.IsNullOrWhiteSpace(folderId)
                ? "unknown"
                : folderId.Trim();
            string candidate = resolvedConnectionName + " - " + resolvedFolderName + " (" + resolvedFolderId + ")";
            string finalName = SanitizeManagedFolderName(candidate);
            return Path.Combine(ManagedLocalFolderRoot, finalName);
        }

        public static bool IsManagedLocalFolderPath(string? path)
        {
            string trimmedPath = path?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedPath))
            {
                return false;
            }

            try
            {
                string normalizedPath = Path.GetFullPath(trimmedPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string normalizedManagedRoot = Path.GetFullPath(ManagedLocalFolderRoot)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(normalizedPath, normalizedManagedRoot, StringComparison.OrdinalIgnoreCase)
                    || normalizedPath.StartsWith(
                        normalizedManagedRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string SanitizeManagedFolderName(string candidate)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char character in candidate ?? string.Empty)
            {
                if (Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0 || char.IsControl(character))
                {
                    builder.Append('_');
                    continue;
                }

                builder.Append(character);
            }

            string sanitized = builder.ToString().Trim().TrimEnd('.', ' ');
            return string.IsNullOrWhiteSpace(sanitized) ? "google_drive_sync" : sanitized;
        }
    }

    public static class GoogleDriveLocalSnapshotBuilder
    {
        public static GoogleDriveSyncSnapshot FilterReservedSupportFiles(GoogleDriveSyncSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            GoogleDriveSyncSnapshot filtered = new GoogleDriveSyncSnapshot();
            foreach (KeyValuePair<string, GoogleDriveSyncFolderEntry> folder in snapshot.Folders)
            {
                filtered.Folders[folder.Key] = new GoogleDriveSyncFolderEntry
                {
                    RelativePath = folder.Value.RelativePath,
                    ItemId = folder.Value.ItemId
                };
            }

            foreach (KeyValuePair<string, GoogleDriveSyncFileEntry> file in snapshot.Files)
            {
                if (IsReservedSupportFile(file.Value.RelativePath))
                {
                    continue;
                }

                filtered.Files[file.Key] = new GoogleDriveSyncFileEntry
                {
                    RelativePath = file.Value.RelativePath,
                    ItemId = file.Value.ItemId,
                    ParentId = file.Value.ParentId,
                    Size = file.Value.Size,
                    Hash = file.Value.Hash
                };
            }

            return filtered;
        }

        public static GoogleDriveSyncSnapshot Build(string rootDirectory)
        {
            string normalizedRoot = Path.GetFullPath(rootDirectory ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(normalizedRoot))
            {
                throw new DirectoryNotFoundException("The local sync directory was not found.");
            }

            GoogleDriveSyncSnapshot snapshot = new GoogleDriveSyncSnapshot();
            foreach (string directory in Directory.EnumerateDirectories(normalizedRoot, "*", SearchOption.AllDirectories))
            {
                string relativePath = NormalizeRelativePath(Path.GetRelativePath(normalizedRoot, directory));
                snapshot.Folders[relativePath] = new GoogleDriveSyncFolderEntry
                {
                    RelativePath = relativePath
                };
            }

            foreach (string filePath in Directory.EnumerateFiles(normalizedRoot, "*", SearchOption.AllDirectories))
            {
                string relativePath = NormalizeRelativePath(Path.GetRelativePath(normalizedRoot, filePath));
                if (IsReservedSupportFile(relativePath))
                {
                    continue;
                }

                FileInfo info = new FileInfo(filePath);
                snapshot.Files[relativePath] = new GoogleDriveSyncFileEntry
                {
                    RelativePath = relativePath,
                    Size = info.Length,
                    Hash = ComputeMd5(filePath)
                };
            }

            return snapshot;
        }

        public static string NormalizeRelativePath(string value)
        {
            return (value ?? string.Empty).Trim().Replace('\\', '/').Trim('/');
        }

        public static bool IsReservedSupportFile(string? relativePath)
        {
            string normalizedPath = NormalizeRelativePath(relativePath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            string fileName = Path.GetFileName(normalizedPath);
            return string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, "thumbs.db", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, ".ds_store", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    fileName,
                    GoogleDriveManagedLocalFolderMarkerService.MarkerIconFileName,
                    StringComparison.OrdinalIgnoreCase);
        }

        public static string ComputeMd5(string filePath)
        {
            using MD5 md5 = MD5.Create();
            using FileStream stream = File.OpenRead(filePath);
            byte[] hash = md5.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
