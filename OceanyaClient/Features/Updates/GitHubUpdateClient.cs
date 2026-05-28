using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OceanyaClient.Features.Updates
{
    public sealed class GitHubUpdateClient
    {
        public const string Owner = "HaiKamDesu";
        public const string Repository = "AO2-Oceanya-Client";
        public const string RepositoryFullName = Owner + "/" + Repository;
        private const string ReleasesUrl = "https://api.github.com/repos/HaiKamDesu/AO2-Oceanya-Client/releases?per_page=30";

        private readonly HttpClient httpClient;

        public GitHubUpdateClient(HttpClient? httpClient = null)
        {
            this.httpClient = httpClient ?? new HttpClient();
        }

        public async Task<UpdateRelease?> GetLatestStableReleaseAsync(
            UpdateVersion currentVersion,
            CancellationToken cancellationToken)
        {
            return await GetLatestReleaseAsync(UpdateEnvironment.Stable, currentVersion, cancellationToken).ConfigureAwait(false);
        }

        public async Task<UpdateRelease?> GetLatestReleaseAsync(
            UpdateEnvironment environment,
            UpdateVersion currentVersion,
            CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);

            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("OceanyaClient", AppVersionInfo.DisplayVersion));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return await ParseReleaseListAsync(json, environment, currentVersion, cancellationToken).ConfigureAwait(false);
        }

        public async Task<UpdateRelease?> ParseReleaseAsync(
            string releaseJson,
            UpdateVersion currentVersion,
            CancellationToken cancellationToken)
        {
            return await ParseReleaseAsync(releaseJson, UpdateEnvironment.Stable, currentVersion, cancellationToken).ConfigureAwait(false);
        }

        public async Task<UpdateRelease?> ParseReleaseAsync(
            string releaseJson,
            UpdateEnvironment environment,
            UpdateVersion currentVersion,
            CancellationToken cancellationToken)
        {
            using JsonDocument document = JsonDocument.Parse(releaseJson);
            JsonElement root = document.RootElement.Clone();
            return await ParseReleaseElementAsync(root, environment, currentVersion, cancellationToken).ConfigureAwait(false);
        }

        public async Task<UpdateRelease?> ParseReleaseListAsync(
            string releaseListJson,
            UpdateEnvironment environment,
            UpdateVersion currentVersion,
            CancellationToken cancellationToken)
        {
            using JsonDocument document = JsonDocument.Parse(releaseListJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("The GitHub releases response was not an array.");
            }

            List<string> releaseNoteSections = new List<string>();
            UpdateRelease? selectedRelease = null;
            foreach (JsonElement releaseElement in document.RootElement.EnumerateArray())
            {
                JsonElement releaseRoot = releaseElement.Clone();
                if (TryBuildReleaseNoteSection(releaseRoot, environment.Channel, currentVersion, out string releaseNoteSection))
                {
                    releaseNoteSections.Add(releaseNoteSection);
                }

                if (selectedRelease == null)
                {
                    UpdateRelease? release = await ParseReleaseElementAsync(
                        releaseRoot,
                        environment,
                        currentVersion,
                        cancellationToken).ConfigureAwait(false);
                    if (release != null)
                    {
                        selectedRelease = release;
                    }
                }
            }

            if (selectedRelease != null
                && releaseNoteSections.Count > 0)
            {
                selectedRelease.Body = string.Join("\n\n", releaseNoteSections);
            }

            return selectedRelease;
        }

        private async Task<UpdateRelease?> ParseReleaseElementAsync(
            JsonElement root,
            UpdateEnvironment environment,
            UpdateVersion currentVersion,
            CancellationToken cancellationToken)
        {
            bool draft = GetBoolean(root, "draft");
            bool prerelease = GetBoolean(root, "prerelease");
            if (draft)
            {
                return null;
            }

            string tag = GetString(root, "tag_name");
            if (environment.Channel == UpdateChannel.Stable && prerelease)
            {
                return null;
            }

            if (environment.Channel == UpdateChannel.Test && !prerelease)
            {
                return null;
            }

            if (!UpdateVersion.TryParseForChannel(tag, environment.Channel, out UpdateVersion releaseVersion) || releaseVersion <= currentVersion)
            {
                return null;
            }

            List<UpdateReleaseAsset> assets = ParseAssets(root);
            UpdateReleaseAsset? manifestAsset = assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, "update-manifest.json", StringComparison.OrdinalIgnoreCase));
            if (manifestAsset == null || string.IsNullOrWhiteSpace(manifestAsset.BrowserDownloadUrl))
            {
                throw new InvalidOperationException("The release does not contain update-manifest.json, so automatic update is disabled.");
            }

            string manifestJson = await httpClient.GetStringAsync(manifestAsset.BrowserDownloadUrl, cancellationToken).ConfigureAwait(false);
            if (!UpdateManifest.TryParse(manifestJson, environment, out UpdateManifest manifest, out string manifestError))
            {
                throw new InvalidOperationException(manifestError);
            }

            if (!string.Equals(manifest.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The update manifest tag does not match the GitHub release tag.");
            }

            if (manifest.ParsedVersion <= currentVersion)
            {
                return null;
            }

            UpdateReleaseAsset? packageAsset = assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, manifest.AssetName, StringComparison.OrdinalIgnoreCase));
            if (packageAsset == null || string.IsNullOrWhiteSpace(packageAsset.BrowserDownloadUrl))
            {
                throw new InvalidOperationException("The update package asset listed in the manifest is missing.");
            }

            string assetDigest = UpdateManifest.NormalizeSha256(packageAsset.Digest);
            if (!string.IsNullOrWhiteSpace(assetDigest)
                && UpdateManifest.IsValidSha256(assetDigest)
                && !string.Equals(assetDigest, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The GitHub asset digest does not match update-manifest.json.");
            }

            return new UpdateRelease
            {
                RepositoryFullName = RepositoryFullName,
                TagName = tag,
                Name = GetString(root, "name"),
                HtmlUrl = GetString(root, "html_url"),
                Body = GetString(root, "body"),
                Draft = draft,
                Prerelease = prerelease,
                PublishedAt = TryGetDate(root, "published_at"),
                Manifest = manifest,
                PackageAsset = packageAsset
            };
        }

        private static List<UpdateReleaseAsset> ParseAssets(JsonElement root)
        {
            List<UpdateReleaseAsset> assets = new List<UpdateReleaseAsset>();
            if (!root.TryGetProperty("assets", out JsonElement assetsElement)
                || assetsElement.ValueKind != JsonValueKind.Array)
            {
                return assets;
            }

            foreach (JsonElement assetElement in assetsElement.EnumerateArray())
            {
                assets.Add(new UpdateReleaseAsset
                {
                    Name = GetString(assetElement, "name"),
                    BrowserDownloadUrl = GetString(assetElement, "browser_download_url"),
                    Digest = GetString(assetElement, "digest"),
                    Size = GetInt64(assetElement, "size")
                });
            }

            return assets;
        }

        private static bool TryBuildReleaseNoteSection(
            JsonElement root,
            UpdateChannel channel,
            UpdateVersion currentVersion,
            out string section)
        {
            section = string.Empty;
            bool prerelease = GetBoolean(root, "prerelease");
            if (GetBoolean(root, "draft")
                || (channel == UpdateChannel.Stable && prerelease)
                || (channel == UpdateChannel.Test && !prerelease))
            {
                return false;
            }

            string tag = GetString(root, "tag_name");
            if (!UpdateVersion.TryParseForChannel(tag, channel, out UpdateVersion releaseVersion)
                || releaseVersion <= currentVersion)
            {
                return false;
            }

            string title = string.IsNullOrWhiteSpace(GetString(root, "name"))
                ? tag
                : tag + " - " + GetString(root, "name");
            string body = GetString(root, "body").Trim();
            section = "## " + title;
            if (!string.IsNullOrWhiteSpace(body))
            {
                section += "\n\n" + body;
            }

            return true;
        }

        private static string GetString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : string.Empty;
        }

        private static bool GetBoolean(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement property)
                && property.ValueKind == JsonValueKind.True;
        }

        private static long GetInt64(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement property) && property.TryGetInt64(out long value)
                ? value
                : 0;
        }

        private static DateTimeOffset? TryGetDate(JsonElement element, string name)
        {
            string value = GetString(element, name);
            return DateTimeOffset.TryParse(value, out DateTimeOffset parsed) ? parsed : null;
        }
    }
}
