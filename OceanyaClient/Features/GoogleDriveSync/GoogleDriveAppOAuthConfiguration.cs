using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OceanyaClient.Features.GoogleDriveSync
{
    /// <summary>
    /// Central app-level Google OAuth configuration for Oceanya's Drive integration.
    /// End users should never have to type this manually.
    /// </summary>
    public static class GoogleDriveAppOAuthConfiguration
    {
        // Shared Desktop OAuth client ID for Oceanya's Google Drive integration.
        private const string EmbeddedClientId =
            "";

        // Intentionally left blank. Desktop/native OAuth clients are public clients, so the app
        // should not rely on a bundled client secret stored on the end-user machine.
        private const string EmbeddedClientSecret = "";
        private const string LocalJsonPathEnvironmentVariable = "OCEANYA_GOOGLE_DRIVE_CLIENT_JSON_PATH";
        private const string LocalJsonFileName = "google-drive-oauth.local.json";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static string ClientId => ResolveValue(
            "OCEANYA_GOOGLE_DRIVE_CLIENT_ID",
            GetInstalledConfiguration()?.ClientId ?? EmbeddedClientId);

        public static string ClientSecret => ResolveValue(
            "OCEANYA_GOOGLE_DRIVE_CLIENT_SECRET",
            GetInstalledConfiguration()?.ClientSecret ?? EmbeddedClientSecret);

        public static bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

        public static GoogleDriveOAuthClientConfiguration Create()
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException(
                    "Google Drive sign-in is not configured for this Oceanya build yet. " +
                    "Set the app's Desktop OAuth client ID in GoogleDriveAppOAuthConfiguration.cs.");
            }

            return new GoogleDriveOAuthClientConfiguration
            {
                ClientId = ClientId,
                ClientSecret = ClientSecret
            };
        }

        private static string ResolveValue(string environmentVariableName, string embeddedValue)
        {
            string value = Environment.GetEnvironmentVariable(environmentVariableName) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            return embeddedValue.Trim();
        }

        internal static GoogleDriveOAuthClientConfiguration? TryLoadInstalledConfiguration(
            string baseDirectory,
            string? configuredPath = null)
        {
            foreach (string filePath in BuildCandidateFilePaths(baseDirectory, configuredPath))
            {
                GoogleDriveOAuthClientConfiguration? configuration = TryLoadInstalledConfigurationFromFile(filePath);
                if (configuration != null)
                {
                    return configuration;
                }
            }

            return null;
        }

        private static GoogleDriveOAuthClientConfiguration? GetInstalledConfiguration()
        {
            string configuredPath = Environment.GetEnvironmentVariable(LocalJsonPathEnvironmentVariable);
            return TryLoadInstalledConfiguration(AppContext.BaseDirectory, configuredPath);
        }

        private static IEnumerable<string> BuildCandidateFilePaths(string baseDirectory, string? configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                yield return configuredPath.Trim();
            }

            yield return Path.Combine(baseDirectory, LocalJsonFileName);
            yield return Path.Combine(baseDirectory, "Features", "GoogleDriveSync", LocalJsonFileName);
        }

        private static GoogleDriveOAuthClientConfiguration? TryLoadInstalledConfigurationFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                InstalledClientConfigurationFile? configurationFile =
                    JsonSerializer.Deserialize<InstalledClientConfigurationFile>(json, JsonOptions);
                if (string.IsNullOrWhiteSpace(configurationFile?.Installed?.ClientId))
                {
                    return null;
                }

                return new GoogleDriveOAuthClientConfiguration
                {
                    ClientId = configurationFile.Installed.ClientId,
                    ClientSecret = configurationFile.Installed.ClientSecret
                };
            }
            catch
            {
                return null;
            }
        }

        private sealed class InstalledClientConfigurationFile
        {
            [JsonPropertyName("installed")]
            public InstalledClientConfiguration? Installed { get; set; }
        }

        private sealed class InstalledClientConfiguration
        {
            [JsonPropertyName("client_id")]
            public string ClientId { get; set; } = string.Empty;

            [JsonPropertyName("client_secret")]
            public string ClientSecret { get; set; } = string.Empty;
        }
    }
}
