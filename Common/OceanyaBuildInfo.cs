using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Common
{
    /// <summary>
    /// The exact source state this build came from, stamped in at build time.
    /// </summary>
    /// <remarks>
    /// The whole point is diagnosing a <c>DEBUG.txt</c> somebody sends back: the version alone does not say
    /// which commit it was built from, and a developer build is often ahead of, or dirty against, whatever
    /// the version number claims. The values are baked into assembly metadata by the
    /// <c>OceanyaStampGitInfo</c> target in <c>Directory.Build.props</c>, because a released client has no
    /// repository and no git binary to ask at runtime.
    ///
    /// Every value degrades to "unknown" rather than throwing: a source zip, a machine without git, or a
    /// history-less CI checkout must still produce a usable log.
    /// </remarks>
    public static class OceanyaBuildInfo
    {
        private const string UnknownValue = "unknown";

        private static readonly Lazy<IReadOnlyDictionary<string, string>> Metadata =
            new Lazy<IReadOnlyDictionary<string, string>>(ReadMetadata);

        /// <summary>Application version, e.g. <c>7.13</c>.</summary>
        public static string Version => ReadVersion();

        /// <summary>Short commit hash the build came from, or <c>unknown</c>.</summary>
        public static string Commit => Lookup("OceanyaGitCommit");

        /// <summary>Branch the build came from, or <c>unknown</c>.</summary>
        public static string Branch => Lookup("OceanyaGitBranch");

        /// <summary>Build configuration (<c>Debug</c>/<c>Release</c>), or <c>unknown</c>.</summary>
        public static string Configuration => Lookup("OceanyaBuildConfiguration");

        /// <summary>UTC timestamp of the build, or <c>unknown</c>.</summary>
        public static string BuildTimestampUtc => Lookup("OceanyaBuildTimestampUtc");

        /// <summary>
        /// True when the working tree had uncommitted changes to Oceanya's own code at build time.
        /// </summary>
        /// <remarks>
        /// Submodules are deliberately excluded from the check: the reference clones carry permanent
        /// whole-file CRLF churn here, so counting them would mark every build dirty and say nothing.
        /// </remarks>
        public static bool? IsDirty
        {
            get
            {
                string value = Lookup("OceanyaGitDirty");
                return bool.TryParse(value, out bool parsed) ? parsed : null;
            }
        }

        /// <summary>
        /// One line naming the exact source state, for the top of a log.
        /// </summary>
        /// <returns>
        /// For example <c>7.13 (Debug) commit 1a2b3c4d5e6f on release/8-0, DIRTY - built 2026-09-16 04:12:33 UTC</c>.
        /// </returns>
        public static string DescribeBuild()
        {
            string commit = Commit;
            string branch = Branch;
            bool? dirty = IsDirty;

            // "DIRTY" in capitals because it is the single most important thing to notice when a log does
            // not match the source: the build contained changes that were never committed anywhere.
            string dirtyText = dirty switch
            {
                true => ", DIRTY (uncommitted changes at build time)",
                false => string.Empty,
                _ => ", dirty state unknown"
            };

            string commitText = string.Equals(commit, UnknownValue, StringComparison.Ordinal)
                ? "commit unknown"
                : "commit " + commit;

            string branchText = string.Equals(branch, UnknownValue, StringComparison.Ordinal)
                ? string.Empty
                : " on " + branch;

            return $"{Version} ({Configuration}) {commitText}{branchText}{dirtyText} - built {BuildTimestampUtc} UTC";
        }

        private static string Lookup(string key)
        {
            return Metadata.Value.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : UnknownValue;
        }

        private static string ReadVersion()
        {
            try
            {
                Assembly assembly = typeof(OceanyaBuildInfo).Assembly;
                string? informational = assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(informational))
                {
                    // The SDK appends "+<sourcerevisionid>" when source link is on; the commit is reported
                    // separately here, so keep the version itself readable.
                    int plus = informational.IndexOf('+');
                    return plus > 0 ? informational.Substring(0, plus) : informational;
                }

                return assembly.GetName().Version?.ToString() ?? UnknownValue;
            }
            catch (Exception)
            {
                return UnknownValue;
            }
        }

        private static IReadOnlyDictionary<string, string> ReadMetadata()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (AssemblyMetadataAttribute attribute in typeof(OceanyaBuildInfo).Assembly
                             .GetCustomAttributes<AssemblyMetadataAttribute>())
                {
                    if (!string.IsNullOrWhiteSpace(attribute.Key) && !values.ContainsKey(attribute.Key))
                    {
                        values[attribute.Key] = attribute.Value ?? string.Empty;
                    }
                }
            }
            catch (Exception)
            {
                // A trimmed or otherwise unusual assembly simply reports everything as unknown.
            }

            return values;
        }
    }
}
