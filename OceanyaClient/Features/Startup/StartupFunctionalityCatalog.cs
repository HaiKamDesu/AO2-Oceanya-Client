using System;
using System.Collections.Generic;

namespace OceanyaClient.Features.Startup
{
    public static class StartupFunctionalityIds
    {
        public const string GmMultiClient = "gm_multi_client";
        public const string CharacterDatabaseViewer = "character_database_viewer";
        public const string CharacterFileCreator = "character_file_creator";
        public const string EmptyWindowTemp = "empty_window_temp";
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
        private static readonly IReadOnlyList<StartupFunctionalityOption> options = new List<StartupFunctionalityOption>
        {
            new StartupFunctionalityOption
            {
                Id = StartupFunctionalityIds.GmMultiClient,
                DisplayName = "Attorney Online GM Multi-Client",
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
                Id = StartupFunctionalityIds.EmptyWindowTemp,
                DisplayName = "Empty Window (temp)",
                RequiresServerEndpoint = false
            }
        };

        public static IReadOnlyList<StartupFunctionalityOption> Options => options;

        public static bool IsValid(string? functionalityId)
        {
            if (string.IsNullOrWhiteSpace(functionalityId))
            {
                return false;
            }

            foreach (StartupFunctionalityOption option in options)
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
            foreach (StartupFunctionalityOption option in options)
            {
                if (string.Equals(option.Id, functionalityId?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return option;
                }
            }

            return options[0];
        }
    }
}
