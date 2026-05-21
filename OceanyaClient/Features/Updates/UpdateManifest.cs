using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OceanyaClient.Features.Updates
{
    public sealed class UpdateManifest
    {
        private static readonly Regex StableAssetNamePattern =
            new Regex(@"^Oceanya\.Client\.win-x64\.v[0-9]+(?:\.[0-9]+){1,3}\.zip$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex TestAssetNamePattern =
            new Regex(@"^Oceanya\.Client\.win-x64\.(?:test-v[0-9]+(?:\.[0-9]+){1,3}|v[0-9]+(?:\.[0-9]+){1,3}-test\.[0-9]+)\.zip$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex StableTagPattern =
            new Regex(@"^v[0-9]+(?:\.[0-9]+){1,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex TestTagPattern =
            new Regex(@"^(?:test-v[0-9]+(?:\.[0-9]+){1,3}|v[0-9]+(?:\.[0-9]+){1,3}-test\.[0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public string Version { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Channel { get; set; } = "stable";
        public string Os { get; set; } = "win";
        public string Arch { get; set; } = "x64";
        public string AssetName { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public string MinimumSupportedVersion { get; set; } = string.Empty;
        public string EntryExe { get; set; } = "OceanyaClient.exe";
        public string ReleaseNotesSource { get; set; } = "github_release_body";

        [JsonIgnore]
        public UpdateVersion ParsedVersion { get; private set; }

        [JsonIgnore]
        public UpdateVersion? ParsedMinimumSupportedVersion { get; private set; }

        public static bool TryParse(string json, out UpdateManifest manifest, out string error)
        {
            return TryParse(json, UpdateEnvironment.Stable, out manifest, out error);
        }

        public static bool TryParse(string json, UpdateEnvironment environment, out UpdateManifest manifest, out string error)
        {
            manifest = new UpdateManifest();
            error = string.Empty;

            try
            {
                UpdateManifest? parsed = JsonSerializer.Deserialize<UpdateManifest>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Disallow,
                        AllowTrailingCommas = false
                    });
                if (parsed == null)
                {
                    error = "The update manifest was empty.";
                    return false;
                }

                manifest = parsed;
                return manifest.Validate(environment, out error);
            }
            catch (JsonException ex)
            {
                error = "The update manifest is not valid JSON: " + ex.Message;
                return false;
            }
        }

        public bool Validate(out string error)
        {
            return Validate(UpdateEnvironment.Stable, out error);
        }

        public bool Validate(UpdateEnvironment environment, out string error)
        {
            error = string.Empty;
            Version = Version?.Trim() ?? string.Empty;
            Tag = Tag?.Trim() ?? string.Empty;
            Channel = Channel?.Trim() ?? string.Empty;
            Os = Os?.Trim() ?? string.Empty;
            Arch = Arch?.Trim() ?? string.Empty;
            AssetName = AssetName?.Trim() ?? string.Empty;
            Sha256 = NormalizeSha256(Sha256);
            MinimumSupportedVersion = MinimumSupportedVersion?.Trim() ?? string.Empty;
            EntryExe = EntryExe?.Trim() ?? string.Empty;
            ReleaseNotesSource = ReleaseNotesSource?.Trim() ?? string.Empty;

            UpdateChannel expectedChannel = environment.Channel;
            if (!UpdateVersion.TryParseForChannel(Version, expectedChannel, out UpdateVersion parsedVersion))
            {
                error = "The update manifest version is invalid.";
                return false;
            }

            ParsedVersion = parsedVersion;

            Regex expectedTagPattern = expectedChannel == UpdateChannel.Test ? TestTagPattern : StableTagPattern;
            if (string.IsNullOrWhiteSpace(Tag) || !expectedTagPattern.IsMatch(Tag))
            {
                error = "The update manifest tag does not match the expected update channel.";
                return false;
            }

            if (!UpdateVersion.TryParseForChannel(Tag, expectedChannel, out UpdateVersion tagVersion) || tagVersion.CompareTo(parsedVersion) != 0)
            {
                error = "The update manifest tag and version do not match.";
                return false;
            }

            if (!string.Equals(Channel, expectedChannel.ToManifestValue(), StringComparison.OrdinalIgnoreCase))
            {
                error = "The update manifest channel does not match the running update channel.";
                return false;
            }

            if (!string.Equals(Os, "win", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Arch, "x64", StringComparison.OrdinalIgnoreCase))
            {
                error = "The update manifest is not for Windows x64.";
                return false;
            }

            Regex expectedAssetPattern = expectedChannel == UpdateChannel.Test ? TestAssetNamePattern : StableAssetNamePattern;
            if (!expectedAssetPattern.IsMatch(AssetName))
            {
                error = "The update manifest asset name is not recognized.";
                return false;
            }

            if (!IsValidSha256(Sha256))
            {
                error = "The update manifest is missing a valid SHA-256 hash.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(MinimumSupportedVersion))
            {
                if (!UpdateVersion.TryParseForChannel(MinimumSupportedVersion, expectedChannel, out UpdateVersion minimum))
                {
                    error = "The update manifest minimum supported version is invalid.";
                    return false;
                }

                ParsedMinimumSupportedVersion = minimum;
            }

            if (!string.Equals(EntryExe, "OceanyaClient.exe", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(EntryExe) != EntryExe)
            {
                error = "The update manifest entry executable is invalid.";
                return false;
            }

            return true;
        }

        public static string NormalizeSha256(string? value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            const string prefix = "sha256:";
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[prefix.Length..];
            }

            return normalized.ToLowerInvariant();
        }

        public static bool IsValidSha256(string? value)
        {
            string normalized = NormalizeSha256(value);
            if (normalized.Length != 64)
            {
                return false;
            }

            foreach (char c in normalized)
            {
                bool hex = c is >= '0' and <= '9' or >= 'a' and <= 'f';
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
