using System;
using System.Collections.Generic;

namespace OceanyaClient
{
    /// <summary>
    /// Stores optional test-mode startup overrides for deterministic in-process and UI automation runs.
    /// </summary>
    public sealed class OceanyaTestModeOptions
    {
        public bool IsEnabled { get; set; }
        public bool DisableFakeLoading { get; set; }
        public bool DisableLoadingScreen { get; set; }
        public bool DisableWaitForms { get; set; }
        public bool AutoLaunchStartupFunctionality { get; set; }
        public bool SkipServerValidation { get; set; }
        public bool SkipAssetRefreshPrompts { get; set; }
        public string StartupFunctionalityId { get; set; } = string.Empty;
        public string ConfigIniPath { get; set; } = string.Empty;
        public string ServerEndpoint { get; set; } = string.Empty;
        public string SaveFilePath { get; set; } = string.Empty;
        public string ServerJsonPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Central access point for deterministic test-mode behavior.
    /// </summary>
    public static class OceanyaTestMode
    {
        private static OceanyaTestModeOptions current = new OceanyaTestModeOptions();

        public static OceanyaTestModeOptions Current => current;

        public static bool IsEnabled => current.IsEnabled;

        public static void SetCurrent(OceanyaTestModeOptions? options)
        {
            current = options ?? new OceanyaTestModeOptions();
        }

        public static void Reset()
        {
            current = new OceanyaTestModeOptions();
        }

        public static OceanyaTestModeOptions ParseArgs(IEnumerable<string>? args)
        {
            OceanyaTestModeOptions options = new OceanyaTestModeOptions();
            if (args == null)
            {
                return options;
            }

            foreach (string rawArg in args)
            {
                string arg = rawArg?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                if (string.Equals(arg, "--test-mode", StringComparison.OrdinalIgnoreCase))
                {
                    options.IsEnabled = true;
                }
                else if (string.Equals(arg, "--test-disable-fake-loading", StringComparison.OrdinalIgnoreCase))
                {
                    options.IsEnabled = true;
                    options.DisableFakeLoading = true;
                }
                else if (string.Equals(arg, "--test-disable-loading-screen", StringComparison.OrdinalIgnoreCase))
                {
                    options.IsEnabled = true;
                    options.DisableLoadingScreen = true;
                }
                else if (string.Equals(arg, "--test-disable-waitforms", StringComparison.OrdinalIgnoreCase))
                {
                    options.IsEnabled = true;
                    options.DisableWaitForms = true;
                }
                else if (string.Equals(arg, "--test-auto-launch-startup", StringComparison.OrdinalIgnoreCase))
                {
                    options.IsEnabled = true;
                    options.AutoLaunchStartupFunctionality = true;
                }
                else if (string.Equals(arg, "--test-skip-server-validation", StringComparison.OrdinalIgnoreCase))
                {
                    options.IsEnabled = true;
                    options.SkipServerValidation = true;
                }
                else if (string.Equals(arg, "--test-skip-asset-refresh-prompts", StringComparison.OrdinalIgnoreCase))
                {
                    options.IsEnabled = true;
                    options.SkipAssetRefreshPrompts = true;
                }
                else if (TryParseAssignment(arg, "--test-startup-functionality", out string startupFunctionalityId))
                {
                    options.IsEnabled = true;
                    options.StartupFunctionalityId = startupFunctionalityId;
                }
                else if (TryParseAssignment(arg, "--test-config-ini", out string configIniPath))
                {
                    options.IsEnabled = true;
                    options.ConfigIniPath = configIniPath;
                }
                else if (TryParseAssignment(arg, "--test-server-endpoint", out string serverEndpoint))
                {
                    options.IsEnabled = true;
                    options.ServerEndpoint = serverEndpoint;
                }
                else if (TryParseAssignment(arg, "--test-savefile", out string saveFilePath))
                {
                    options.IsEnabled = true;
                    options.SaveFilePath = saveFilePath;
                }
                else if (TryParseAssignment(arg, "--test-server-json", out string serverJsonPath))
                {
                    options.IsEnabled = true;
                    options.ServerJsonPath = serverJsonPath;
                }
            }

            if (options.DisableLoadingScreen)
            {
                options.DisableFakeLoading = true;
            }

            return options;
        }

        private static bool TryParseAssignment(string arg, string prefix, out string value)
        {
            string assignmentPrefix = prefix + "=";
            if (arg.StartsWith(assignmentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                value = arg.Substring(assignmentPrefix.Length).Trim();
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
