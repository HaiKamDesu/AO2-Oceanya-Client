using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Common;

namespace OceanyaClient.Features.Viewport
{
    internal static class AO2ThemeCatalog
    {
        public const string DefaultThemeName = "AceAttorney2x";
        public const string ServerSubthemeValue = "server";

        private static readonly HashSet<string> ExcludedSubthemeDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "server",
            "default",
            "effects",
            "misc"
        };

        public static IReadOnlyList<string> GetThemes()
        {
            List<string> themes = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string baseFolder in GetAo2ThemeScanFolders())
            {
                string themesRoot = Path.Combine(baseFolder, "themes");
                if (!Directory.Exists(themesRoot))
                {
                    continue;
                }

                IEnumerable<string> directories;
                try
                {
                    directories = Directory.EnumerateDirectories(themesRoot, "*", SearchOption.TopDirectoryOnly)
                        .OrderBy(Path.GetFileName, NaturalStringComparer.Instance)
                        .ToList();
                }
                catch
                {
                    continue;
                }

                foreach (string directory in directories)
                {
                    string name = Path.GetFileName(directory)?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name) || name.StartsWith(".", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (seen.Add(name))
                    {
                        themes.Add(name);
                    }
                }
            }

            return themes;
        }

        public static IReadOnlyList<AO2SubthemeOption> GetSubthemes(string? themeName)
        {
            List<AO2SubthemeOption> subthemes = new List<AO2SubthemeOption>
            {
                new AO2SubthemeOption("server", ServerSubthemeValue),
                new AO2SubthemeOption("default", ServerSubthemeValue)
            };
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "server",
                "default"
            };

            string themeRoot = ResolveThemeRoot(themeName);
            if (string.IsNullOrWhiteSpace(themeRoot) || !Directory.Exists(themeRoot))
            {
                return subthemes;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(themeRoot, "*", SearchOption.TopDirectoryOnly)
                    .OrderBy(Path.GetFileName, NaturalStringComparer.Instance)
                    .ToList();
            }
            catch
            {
                return subthemes;
            }

            foreach (string directory in directories)
            {
                string name = Path.GetFileName(directory)?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)
                    || name.StartsWith(".", StringComparison.Ordinal)
                    || ExcludedSubthemeDirectoryNames.Contains(name))
                {
                    continue;
                }

                if (seen.Add(name))
                {
                    subthemes.Add(new AO2SubthemeOption(name, name));
                }
            }

            return subthemes;
        }

        public static string GetConfiguredThemeName(IDictionary<string, string>? configValues = null)
        {
            configValues ??= Ao2ConfigIniSettings.Load();
            return configValues.TryGetValue("theme", out string? theme) && !string.IsNullOrWhiteSpace(theme)
                ? theme.Trim()
                : DefaultThemeName;
        }

        public static string GetConfiguredSubthemeValue(IDictionary<string, string>? configValues = null)
        {
            configValues ??= Ao2ConfigIniSettings.Load();
            return configValues.TryGetValue("subtheme", out string? subtheme) && !string.IsNullOrWhiteSpace(subtheme)
                ? subtheme.Trim()
                : ServerSubthemeValue;
        }

        public static IEnumerable<string> GetAo2ThemeScanFolders()
        {
            string configBaseFolder = Path.GetDirectoryName(Ao2ConfigIniSettings.ConfigPath) ?? string.Empty;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(configBaseFolder) && Directory.Exists(configBaseFolder) && seen.Add(configBaseFolder))
            {
                yield return configBaseFolder;
            }

            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder) || !seen.Add(baseFolder))
                {
                    continue;
                }

                yield return baseFolder;
            }
        }

        private static string ResolveThemeRoot(string? themeName)
        {
            string normalizedTheme = string.IsNullOrWhiteSpace(themeName)
                ? DefaultThemeName
                : themeName.Trim();

            foreach (string baseFolder in GetAo2ThemeScanFolders())
            {
                string themeRoot = Path.Combine(baseFolder, "themes", normalizedTheme);
                if (Directory.Exists(themeRoot))
                {
                    return themeRoot;
                }
            }

            return string.Empty;
        }

        private sealed class NaturalStringComparer : IComparer<string?>
        {
            public static readonly NaturalStringComparer Instance = new NaturalStringComparer();

            public int Compare(string? x, string? y)
            {
                return CompareNatural(x ?? string.Empty, y ?? string.Empty);
            }

            private static int CompareNatural(string left, string right)
            {
                int leftIndex = 0;
                int rightIndex = 0;
                while (leftIndex < left.Length && rightIndex < right.Length)
                {
                    char leftChar = left[leftIndex];
                    char rightChar = right[rightIndex];
                    if (char.IsDigit(leftChar) && char.IsDigit(rightChar))
                    {
                        long leftNumber = ReadNumber(left, ref leftIndex);
                        long rightNumber = ReadNumber(right, ref rightIndex);
                        int numberCompare = leftNumber.CompareTo(rightNumber);
                        if (numberCompare != 0)
                        {
                            return numberCompare;
                        }

                        continue;
                    }

                    int charCompare = string.Compare(
                        leftChar.ToString(),
                        rightChar.ToString(),
                        CultureInfo.CurrentCulture,
                        CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth);
                    if (charCompare != 0)
                    {
                        return charCompare;
                    }

                    leftIndex++;
                    rightIndex++;
                }

                return left.Length.CompareTo(right.Length);
            }

            private static long ReadNumber(string value, ref int index)
            {
                long result = 0;
                while (index < value.Length && char.IsDigit(value[index]))
                {
                    int digit = value[index] - '0';
                    if (result <= (long.MaxValue - digit) / 10)
                    {
                        result = (result * 10) + digit;
                    }

                    index++;
                }

                return result;
            }
        }
    }

    internal sealed class AO2SubthemeOption
    {
        public AO2SubthemeOption(string displayName, string value)
        {
            DisplayName = displayName;
            Value = value;
        }

        public string DisplayName { get; }

        public string Value { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
