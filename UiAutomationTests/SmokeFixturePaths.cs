using System.Text;
using NUnit.Framework;
using OceanyaClient.Features.Startup;

namespace UiAutomationTests;

internal static class SmokeFixturePaths
{
    private static readonly string repositoryRoot = Path.GetFullPath(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));

    public static string RepositoryRoot => repositoryRoot;

    public static string AppExePath => Path.Combine(
        RepositoryRoot,
        "OceanyaClient",
        "bin",
        "Debug",
        "net8.0-windows",
        "OceanyaClient.exe");

    public static string FixtureRoot => Path.Combine(RepositoryRoot, "UnitTests", "TestAssets", "FlaUISmoke");

    public static string ArtifactsRoot => Path.Combine(TestContext.CurrentContext.WorkDirectory, "UiAutomationArtifacts");

    public static string ScreenshotsRoot => Path.Combine(ArtifactsRoot, "Screenshots");

    public static string ConfigIniPath => Path.Combine(FixtureRoot, "config.ini");

    public static string ServerJsonPath => Path.Combine(FixtureRoot, "server.json");

    public static string SaveFilePath => Path.Combine(FixtureRoot, "savefile.json");

    public static string FavoriteServersPath => Path.Combine(FixtureRoot, "favorite_servers.ini");

    public static string SmokeCharacterIniPath => Path.Combine(FixtureRoot, "characters", "SmokePhoenix", "char.ini");

    public static string BuildBaseArguments()
    {
        List<string> args = new List<string>
        {
            "--test-mode",
            "--test-disable-fake-loading",
            "--test-disable-loading-screen",
            "--test-disable-waitforms",
            "--test-skip-server-validation",
            "--test-skip-asset-refresh-prompts",
            "--test-disable-gm-snapshot-persistence",
            "--test-disable-viewport-window-persistence",
            "--test-savefile=" + Quote(SaveFilePath),
            "--test-config-ini=" + Quote(ConfigIniPath),
            "--test-server-json=" + Quote(ServerJsonPath),
            "--test-server-endpoint=ws://127.0.0.1:27016"
        };

        return string.Join(" ", args);
    }

    public static string BuildAutoLaunchArguments(string startupFunctionalityId)
    {
        return BuildBaseArguments()
            + " --test-auto-launch-startup"
            + " --test-startup-functionality=" + startupFunctionalityId;
    }

    public static string BuildInitialConfigurationArguments()
    {
        return BuildBaseArguments();
    }

    public static string BuildMainWindowArguments()
    {
        return BuildAutoLaunchArguments(StartupFunctionalityIds.GmMultiClient);
    }

    public static string BuildFolderVisualizerArguments()
    {
        return BuildAutoLaunchArguments(StartupFunctionalityIds.CharacterDatabaseViewer);
    }

    public static string GetScreenshotPath(string testName)
    {
        Directory.CreateDirectory(ScreenshotsRoot);

        string sanitizedName = string.Concat(testName.Select(ch =>
            Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        return Path.Combine(
            ScreenshotsRoot,
            sanitizedName + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".png");
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
