using System.Text;
using NUnit.Framework;
using OceanyaClient.Features.Startup;

namespace UiAutomationTests;

/// <summary>
/// Path helpers for the Online integration lane.
/// Reuses the FlaUISmoke character and server fixture assets; only the savefile
/// differs (UseSingleInternalClient: false) so the app performs a real Connect().
/// </summary>
internal static class OnlineFixturePaths
{
    /// <summary>
    /// Path to the online-specific fixture directory (only the savefile lives here).
    /// </summary>
    public static string OnlineFixtureRoot => Path.Combine(SmokeFixturePaths.RepositoryRoot, "UnitTests", "TestAssets", "FlaUIOnline");

    /// <summary>
    /// Savefile with UseSingleInternalClient: false, causing AddClientAsync to call real Connect().
    /// </summary>
    public static string SaveFilePath => Path.Combine(OnlineFixtureRoot, "savefile.json");

    /// <summary>
    /// Builds launch arguments for an online lane test. The app will connect to
    /// a caller-managed in-process TCP server at the given port.
    /// </summary>
    public static string BuildArguments(int serverPort)
    {
        List<string> args = new List<string>
        {
            "--test-mode",
            "--test-disable-fake-loading",
            "--test-disable-loading-screen",
            "--test-disable-waitforms",
            "--test-auto-launch-startup",
            "--test-startup-functionality=" + StartupFunctionalityIds.GmMultiClient,
            "--test-skip-server-validation",
            "--test-skip-asset-refresh-prompts",
            "--test-savefile=" + Quote(SaveFilePath),
            "--test-config-ini=" + Quote(SmokeFixturePaths.ConfigIniPath),
            "--test-server-json=" + Quote(SmokeFixturePaths.ServerJsonPath),
            "--test-server-endpoint=tcp://127.0.0.1:" + serverPort
        };

        return string.Join(" ", args);
    }

    private static string Quote(string value)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append('"');
        foreach (char c in value)
        {
            if (c == '"')
            {
                builder.Append("\\\"");
            }
            else
            {
                builder.Append(c);
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
