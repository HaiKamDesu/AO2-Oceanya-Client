using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OceanyaClient
{
    public static class FileHivemindProviderIds
    {
        public const string GoogleDrive = "google_drive";
    }

    public sealed class FileHivemindSettings
    {
        public List<FileHivemindConnectionProfile> Connections { get; set; } = new List<FileHivemindConnectionProfile>();
        public string SelectedConnectionId { get; set; } = string.Empty;
        public bool RunAgentAtStartup { get; set; }
        public bool BackgroundStartupPreferenceConfigured { get; set; }
        public int RemotePollIntervalSeconds { get; set; } = 20;
    }

    public sealed class FileHivemindConnectionProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string DisplayName { get; set; } = string.Empty;
        public string ProviderId { get; set; } = FileHivemindProviderIds.GoogleDrive;
        public GoogleDriveSyncSettings GoogleDrive { get; set; } = new GoogleDriveSyncSettings();

        [JsonIgnore]
        public string EffectiveDisplayName
        {
            get
            {
                string displayName = DisplayName?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    return displayName;
                }

                string remoteFolderName = GoogleDrive.RemoteFolderName?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(remoteFolderName))
                {
                    return remoteFolderName;
                }

                string email = GoogleDrive.LastSignedInEmail?.Trim() ?? string.Empty;
                return string.IsNullOrWhiteSpace(email) ? "Unnamed Connection" : "Drive Connection (" + email + ")";
            }
        }

        [JsonIgnore]
        public string ProviderDisplayName => string.Equals(ProviderId, FileHivemindProviderIds.GoogleDrive, StringComparison.OrdinalIgnoreCase)
            ? "Google Drive"
            : ProviderId?.Trim() ?? string.Empty;

        [JsonIgnore]
        public string AccountDisplayName
        {
            get
            {
                string displayName = GoogleDrive.LastSignedInDisplayName?.Trim() ?? string.Empty;
                string email = GoogleDrive.LastSignedInEmail?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(email))
                {
                    return displayName + " <" + email + ">";
                }

                return string.IsNullOrWhiteSpace(displayName) ? email : displayName;
            }
        }

        [JsonIgnore]
        public string RemoteDisplayName
        {
            get
            {
                string folderName = GoogleDrive.RemoteFolderName?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(folderName))
                {
                    return folderName;
                }

                return GoogleDrive.RemoteFolderId?.Trim() ?? string.Empty;
            }
        }

        public static FileHivemindConnectionProfile CreateGoogleDriveProfile()
        {
            return new FileHivemindConnectionProfile
            {
                ProviderId = FileHivemindProviderIds.GoogleDrive,
                GoogleDrive = new GoogleDriveSyncSettings()
            };
        }
    }

    public sealed class FileHivemindConnectionExchangeDocument
    {
        public int FormatVersion { get; set; } = 1;
        public FileHivemindConnectionProfile Connection { get; set; } = FileHivemindConnectionProfile.CreateGoogleDriveProfile();
    }

    /// <summary>
    /// Persists Google Drive sync configuration for the local client.
    /// </summary>
    public sealed class GoogleDriveSyncSettings
    {
        public string OAuthClientId { get; set; } = string.Empty;
        public string OAuthClientSecret { get; set; } = string.Empty;
        public string TokenStoreKey { get; set; } = string.Empty;
        public string LastSignedInEmail { get; set; } = string.Empty;
        public string LastSignedInDisplayName { get; set; } = string.Empty;
        public string RemoteFolderId { get; set; } = string.Empty;
        public string RemoteFolderName { get; set; } = string.Empty;
        public string LocalFolderPath { get; set; } = string.Empty;
        public bool IsOceanyaManagedLocalFolder { get; set; }
        public bool AutoAddMountPath { get; set; } = true;
        public bool MirrorDeletes { get; set; } = true;
        public bool UseExistingMountPath { get; set; }
        public DateTimeOffset? LastSyncUtc { get; set; }
    }
}
