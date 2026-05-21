using System;
using System.Diagnostics;
using System.Linq;

namespace OceanyaClient.Features.Updates
{
    public enum UpdateChannel
    {
        Stable = 0,
        Test = 1
    }

    public sealed class UpdateEnvironment
    {
        private UpdateEnvironment(UpdateChannel channel, bool developerBuild)
        {
            Channel = channel;
            IsDeveloperBuild = developerBuild;
        }

        public UpdateChannel Channel { get; }
        public bool IsDeveloperBuild { get; }
        public bool IsTest => Channel == UpdateChannel.Test;
        public string ChannelName => Channel == UpdateChannel.Test ? "test" : "stable";
        public string DisplayName => Channel == UpdateChannel.Test ? "Test" : "Stable";
        public string AppDataProfileName => Channel == UpdateChannel.Test ? "OceanyaClientDev" : "OceanyaClient";

        public static UpdateEnvironment Stable { get; } = new UpdateEnvironment(UpdateChannel.Stable, developerBuild: false);
        public static UpdateEnvironment Test { get; } = new UpdateEnvironment(UpdateChannel.Test, developerBuild: true);

        public static UpdateEnvironment Current { get; } = Resolve(
#if DEBUG
            isDeveloperBuild: true,
#else
            isDeveloperBuild: false,
#endif
            debuggerAttached: Debugger.IsAttached,
            args: Environment.GetCommandLineArgs());

        public static UpdateEnvironment ResolveForTests(
            bool isDeveloperBuild,
            bool debuggerAttached,
            string[] args)
        {
            return Resolve(isDeveloperBuild, debuggerAttached, args);
        }

        private static UpdateEnvironment Resolve(bool isDeveloperBuild, bool debuggerAttached, string[] args)
        {
            if (!isDeveloperBuild)
            {
                return Stable;
            }

            if (debuggerAttached || HasDeveloperTestOverride(args) || isDeveloperBuild)
            {
                return Test;
            }

            return Stable;
        }

        private static bool HasDeveloperTestOverride(string[] args)
        {
            return args.Any(arg =>
                string.Equals(NormalizeArg(arg), "--update-channel=test", StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeArg(arg), "--update-channel=prerelease", StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeArg(string? arg)
        {
            return (arg ?? string.Empty).Trim().Trim('"');
        }

        public override string ToString()
        {
            return ChannelName;
        }
    }

    public static class UpdateChannelExtensions
    {
        public static string ToManifestValue(this UpdateChannel channel)
        {
            return channel == UpdateChannel.Test ? "test" : "stable";
        }
    }
}
