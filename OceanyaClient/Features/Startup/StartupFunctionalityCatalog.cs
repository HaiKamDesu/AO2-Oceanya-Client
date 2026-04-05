using System;
using System.Collections.Generic;
using System.Linq;

namespace OceanyaClient.Features.Startup
{
    public static class StartupFunctionalityIds
    {
        public const string GmMultiClient = "gm_multi_client";
        public const string Ao2AiBot = "ao2_ai_bot";
        public const string CharacterDatabaseViewer = "character_database_viewer";
        public const string CharacterFileCreator = "character_file_creator";
        public const string OceanyanFileHivemind = "oceanyan_file_hivemind";
    }

    public sealed class StartupFunctionalityOption
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public bool RequiresServerEndpoint { get; init; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public static class StartupFunctionalityCatalog
    {
        private static readonly IReadOnlyList<StartupFunctionalityOption> allOptions = new List<StartupFunctionalityOption>
        {
            new StartupFunctionalityOption
            {
                Id = StartupFunctionalityIds.GmMultiClient,
                DisplayName = "Attorney Online GM Multi-Client",
                RequiresServerEndpoint = true
            },
            new StartupFunctionalityOption
            {
                Id = StartupFunctionalityIds.Ao2AiBot,
                DisplayName = "AO2 AI Bot (Dev)",
                RequiresServerEndpoint = true
            },
            new StartupFunctionalityOption
            {
                Id = StartupFunctionalityIds.CharacterDatabaseViewer,
                DisplayName = "AO Character Database Viewer",
                RequiresServerEndpoint = false
            },
            new StartupFunctionalityOption
            {
                Id = StartupFunctionalityIds.CharacterFileCreator,
                DisplayName = "AO Character File Creator",
                RequiresServerEndpoint = false
            },
            new StartupFunctionalityOption
            {
                Id = StartupFunctionalityIds.OceanyanFileHivemind,
                DisplayName = "The Oceanyan File Hivemind",
                RequiresServerEndpoint = false
            }
        };

        public static IReadOnlyList<StartupFunctionalityOption> Options => GetVisibleOptions();

        private static IReadOnlyList<StartupFunctionalityOption> GetVisibleOptions()
        {
            return allOptions
                .Where(IsVisible)
                .ToList()
                .AsReadOnly();
        }

        private static bool IsVisible(StartupFunctionalityOption option)
        {
            if (option == null)
            {
                return false;
            }

            if (string.Equals(option.Id, StartupFunctionalityIds.Ao2AiBot, StringComparison.OrdinalIgnoreCase))
            {
                return AO2AiBotDeveloperAccess.IsStartupModeVisible;
            }

            return true;
        }

        public static bool IsValid(string? functionalityId)
        {
            if (string.IsNullOrWhiteSpace(functionalityId))
            {
                return false;
            }

            foreach (StartupFunctionalityOption option in GetVisibleOptions())
            {
                if (string.Equals(option.Id, functionalityId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static StartupFunctionalityOption GetByIdOrDefault(string? functionalityId)
        {
            IReadOnlyList<StartupFunctionalityOption> visibleOptions = GetVisibleOptions();
            foreach (StartupFunctionalityOption option in visibleOptions)
            {
                if (string.Equals(option.Id, functionalityId?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }

            return visibleOptions[0];
        }
    }
}
