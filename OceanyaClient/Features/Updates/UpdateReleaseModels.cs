using System;

namespace OceanyaClient.Features.Updates
{
    public sealed class UpdateReleaseAsset
    {
        public string Name { get; set; } = string.Empty;
        public string BrowserDownloadUrl { get; set; } = string.Empty;
        public string Digest { get; set; } = string.Empty;
        public long Size { get; set; }
    }

    public sealed class UpdateRelease
    {
        public string RepositoryFullName { get; set; } = string.Empty;
        public string TagName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string HtmlUrl { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public bool Draft { get; set; }
        public bool Prerelease { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public UpdateManifest Manifest { get; set; } = new UpdateManifest();
        public UpdateReleaseAsset PackageAsset { get; set; } = new UpdateReleaseAsset();

        public string DisplayVersion => Manifest.Tag;
    }
}
