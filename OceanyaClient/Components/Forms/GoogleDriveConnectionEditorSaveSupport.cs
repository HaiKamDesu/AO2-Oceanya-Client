using OceanyaClient.Features.GoogleDriveSync;
using System;

namespace OceanyaClient
{
    internal static class GoogleDriveConnectionEditorSaveSupport
    {
        internal static FileHivemindConnectionProfile CloneConnectionProfile(FileHivemindConnectionProfile source)
        {
            return new FileHivemindConnectionProfile
            {
                Id = source.Id?.Trim() ?? string.Empty,
                DisplayName = source.DisplayName?.Trim() ?? string.Empty,
                ProviderId = source.ProviderId?.Trim() ?? FileHivemindProviderIds.GoogleDrive,
                AutoSyncEnabled = source.AutoSyncEnabled,
                GoogleDrive = new GoogleDriveSyncSettings
                {
                    OAuthClientId = source.GoogleDrive.OAuthClientId?.Trim() ?? string.Empty,
                    OAuthClientSecret = source.GoogleDrive.OAuthClientSecret?.Trim() ?? string.Empty,
                    OAuthClientSecretStoreKey = source.GoogleDrive.OAuthClientSecretStoreKey?.Trim() ?? string.Empty,
                    TokenStoreKey = source.GoogleDrive.TokenStoreKey?.Trim() ?? string.Empty,
                    LastSignedInEmail = source.GoogleDrive.LastSignedInEmail?.Trim() ?? string.Empty,
                    LastSignedInDisplayName = source.GoogleDrive.LastSignedInDisplayName?.Trim() ?? string.Empty,
                    RemoteFolderId = source.GoogleDrive.RemoteFolderId?.Trim() ?? string.Empty,
                    RemoteFolderName = source.GoogleDrive.RemoteFolderName?.Trim() ?? string.Empty,
                    LocalFolderPath = source.GoogleDrive.LocalFolderPath?.Trim() ?? string.Empty,
                    IsOceanyaManagedLocalFolder = source.GoogleDrive.IsOceanyaManagedLocalFolder,
                    AutoAddMountPath = source.GoogleDrive.AutoAddMountPath,
                    MirrorDeletes = source.GoogleDrive.MirrorDeletes,
                    UseExistingMountPath = source.GoogleDrive.UseExistingMountPath,
                    LastSyncUtc = source.GoogleDrive.LastSyncUtc
                }
            };
        }

        internal static void CopyConnectionProfile(FileHivemindConnectionProfile source, FileHivemindConnectionProfile target)
        {
            target.Id = source.Id?.Trim() ?? string.Empty;
            target.DisplayName = source.DisplayName?.Trim() ?? string.Empty;
            target.ProviderId = source.ProviderId?.Trim() ?? FileHivemindProviderIds.GoogleDrive;
            target.AutoSyncEnabled = source.AutoSyncEnabled;
            target.GoogleDrive ??= new GoogleDriveSyncSettings();
            target.GoogleDrive.OAuthClientId = source.GoogleDrive.OAuthClientId?.Trim() ?? string.Empty;
            target.GoogleDrive.OAuthClientSecret = string.Empty;
            target.GoogleDrive.OAuthClientSecretStoreKey = source.GoogleDrive.OAuthClientSecretStoreKey?.Trim() ?? string.Empty;
            target.GoogleDrive.TokenStoreKey = source.GoogleDrive.TokenStoreKey?.Trim() ?? string.Empty;
            target.GoogleDrive.LastSignedInEmail = source.GoogleDrive.LastSignedInEmail?.Trim() ?? string.Empty;
            target.GoogleDrive.LastSignedInDisplayName = source.GoogleDrive.LastSignedInDisplayName?.Trim() ?? string.Empty;
            target.GoogleDrive.RemoteFolderId = source.GoogleDrive.RemoteFolderId?.Trim() ?? string.Empty;
            target.GoogleDrive.RemoteFolderName = source.GoogleDrive.RemoteFolderName?.Trim() ?? string.Empty;
            target.GoogleDrive.LocalFolderPath = source.GoogleDrive.LocalFolderPath?.Trim() ?? string.Empty;
            target.GoogleDrive.IsOceanyaManagedLocalFolder = source.GoogleDrive.IsOceanyaManagedLocalFolder;
            target.GoogleDrive.AutoAddMountPath = source.GoogleDrive.AutoAddMountPath;
            target.GoogleDrive.MirrorDeletes = source.GoogleDrive.MirrorDeletes;
            target.GoogleDrive.UseExistingMountPath = source.GoogleDrive.UseExistingMountPath;
            target.GoogleDrive.LastSyncUtc = source.GoogleDrive.LastSyncUtc;
        }

        internal static void PersistStoredSecrets(
            FileHivemindConnectionProfile previousPersistedConnection,
            FileHivemindConnectionProfile currentConnection,
            GoogleDriveSecureClientCredentialStore credentialStore)
        {
            string previousClientId = previousPersistedConnection.GoogleDrive.OAuthClientId?.Trim() ?? string.Empty;
            string currentClientId = currentConnection.GoogleDrive.OAuthClientId?.Trim() ?? string.Empty;
            bool hadStoredSecret = GoogleDriveConnectionCredentialSupport.HasStoredSecret(
                previousPersistedConnection.GoogleDrive,
                credentialStore);
            bool hasTypedSecret = !string.IsNullOrWhiteSpace(currentConnection.GoogleDrive.OAuthClientSecret?.Trim());

            if ((!string.Equals(previousClientId, currentClientId, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(currentClientId))
                && hadStoredSecret
                && !hasTypedSecret)
            {
                GoogleDriveConnectionCredentialSupport.DeleteStoredSecret(
                    previousPersistedConnection.GoogleDrive,
                    credentialStore);
            }

            if (hasTypedSecret)
            {
                GoogleDriveConnectionCredentialSupport.SaveSecretIfPresent(currentConnection.GoogleDrive, credentialStore);
            }
        }
    }
}
