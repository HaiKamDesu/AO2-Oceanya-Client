using System.Text.RegularExpressions;
using AOBot_Testing.Structures;

namespace AOBot_Testing
{
    public class AO2Parser
    {
        private static readonly Regex PlayerEntryRegex = new Regex(
            @"(?:^|\r?\n|\s)(?<flags>(?:\[(?:CM|RCM|M|AFK|Hidden)\]\s*)*)\[(?<id>\d+)\]\s*(?<details>.*?)(?=(?:\s+(?:\[(?:CM|RCM|M|AFK|Hidden)\]\s*)*\[\d+\]\s*)|\r?\n|$)",
            RegexOptions.Compiled);

        public static List<Player> ParseGetArea(string input)
        {
            return ParseGetAreaDetailed(input).Players;
        }

        public static GetAreaParseResult ParseGetAreaDetailed(string input)
        {
            List<Player> players = new List<Player>();
            string normalizedInput = input?.Trim() ?? string.Empty;
            bool hasStandardPeopleHeader = normalizedInput.Contains("People in this area:", StringComparison.OrdinalIgnoreCase);
            bool hasClientsInHeader = Regex.IsMatch(
                normalizedInput,
                @"\bClients\s+in\s+\[\d+\]\s+",
                RegexOptions.IgnoreCase);
            bool hasAreaHeading = Regex.IsMatch(
                normalizedInput,
                @"(?:^|[:\r\n])\s*===\s*.+?\s*===",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            bool hasUserStatusHeader = Regex.IsMatch(
                normalizedInput,
                @"\[\s*\d+\s+users?\s*\]\s*\[[^\]]+\]",
                RegexOptions.IgnoreCase);
            string areaName = string.Empty;
            Match areaMatch = Regex.Match(normalizedInput, @"===\s*(?<area>.+?)\s*===", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (areaMatch.Success)
            {
                areaName = areaMatch.Groups["area"].Value.Trim();
            }
            else
            {
                Match clientsInAreaMatch = Regex.Match(
                    normalizedInput,
                    @"=\s*Clients\s+in\s+\[\d+\]\s*(?<area>.+?)\s*\(users:\s*\d+\)\s*(?:\[[^\]]*\])?\s*=",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (clientsInAreaMatch.Success)
                {
                    areaName = clientsInAreaMatch.Groups["area"].Value.Trim();
                }
            }

            foreach (Match match in PlayerEntryRegex.Matches(normalizedInput))
            {
                if (!int.TryParse(match.Groups["id"].Value, out int playerId))
                {
                    continue;
                }

                string details = match.Groups["details"].Value.Trim();
                if (!TryParsePlayerDetails(details, out string characterName, out string? oocShowname))
                {
                    continue;
                }

                string flags = match.Groups["flags"].Value;
                players.Add(new Player
                {
                    IsCM = flags.Contains("[CM]", StringComparison.OrdinalIgnoreCase)
                        || flags.Contains("[RCM]", StringComparison.OrdinalIgnoreCase),
                    CharacterId = playerId,
                    ICCharacterName = characterName,
                    OOCShowname = oocShowname,
                    RawGetAreaLine = match.Value.Trim()
                });
            }

            bool looksLikeGetArea = hasStandardPeopleHeader
                || hasClientsInHeader
                || (players.Count > 0 && (hasAreaHeading || hasUserStatusHeader));

            return new GetAreaParseResult
            {
                IsGetAreaReport = looksLikeGetArea,
                AreaName = areaName,
                Players = players,
                ParsedPlayers = players.Count > 0,
                FailureReason = looksLikeGetArea && players.Count == 0
                    ? "No player rows matched a supported /getarea format."
                    : string.Empty
            };
        }

        public sealed class GetAreaParseResult
        {
            public bool IsGetAreaReport { get; init; }
            public string AreaName { get; init; } = string.Empty;
            public List<Player> Players { get; init; } = new List<Player>();
            public bool ParsedPlayers { get; init; }
            public string FailureReason { get; init; } = string.Empty;
        }

        private static bool TryParsePlayerDetails(string details, out string characterName, out string? oocShowname)
        {
            characterName = string.Empty;
            oocShowname = null;
            if (string.IsNullOrWhiteSpace(details)
                || details.Contains("Users]", StringComparison.OrdinalIgnoreCase)
                || details.Contains("(users:", StringComparison.OrdinalIgnoreCase)
                || details.Contains("Clients in", StringComparison.OrdinalIgnoreCase)
                || details.Contains("===", StringComparison.Ordinal))
            {
                return false;
            }

            string trimmed = details.Trim();
            Match withShowname = Regex.Match(trimmed, @"^(?<char>.*?)(?:\s+\((?<show>[^()]*)\)(?::.*)?(?:\s+\[[^\]]+\])?)?$");
            if (withShowname.Success)
            {
                characterName = withShowname.Groups["char"].Value.Trim();
                if (withShowname.Groups["show"].Success)
                {
                    oocShowname = withShowname.Groups["show"].Value.Trim();
                }
            }
            else
            {
                characterName = trimmed;
            }

            return !string.IsNullOrWhiteSpace(characterName);
        }
    }
}
