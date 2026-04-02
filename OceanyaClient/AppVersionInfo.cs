using System;
using System.Reflection;

namespace OceanyaClient
{
    /// <summary>
    /// Provides normalized access to the shared Oceanya application version metadata.
    /// </summary>
    internal static class AppVersionInfo
    {
        private static readonly Assembly AppAssembly = typeof(AppVersionInfo).Assembly;

        /// <summary>
        /// Gets the four-part assembly version used for cache markers and diagnostics.
        /// </summary>
        public static string AssemblyVersion { get; } = ResolveAssemblyVersion();

        /// <summary>
        /// Gets the normalized display version used in the UI and generated assets.
        /// </summary>
        public static string DisplayVersion { get; } = ResolveDisplayVersion();

        /// <summary>
        /// Gets the normalized display version prefixed for UI presentation.
        /// </summary>
        public static string DisplayVersionWithPrefix { get; } = DisplayVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? DisplayVersion
            : $"v{DisplayVersion}";

        private static string ResolveAssemblyVersion()
        {
            Version? version = AppAssembly.GetName().Version;
            return version?.ToString() ?? "0.0.0.0";
        }

        private static string ResolveDisplayVersion()
        {
            AssemblyInformationalVersionAttribute? informationalVersionAttribute =
                AppAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string informationalVersion = informationalVersionAttribute?.InformationalVersion?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                int metadataSeparatorIndex = informationalVersion.IndexOf('+');
                return metadataSeparatorIndex >= 0
                    ? informationalVersion[..metadataSeparatorIndex]
                    : informationalVersion;
            }

            Version? assemblyVersion = AppAssembly.GetName().Version;
            if (assemblyVersion != null)
            {
                return $"{assemblyVersion.Major}.{Math.Max(0, assemblyVersion.Minor)}";
            }

            return "?.?";
        }
    }
}
