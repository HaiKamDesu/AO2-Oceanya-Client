using System.Text.RegularExpressions;
using AOBot_Testing.Structures;

namespace AOBot_Testing
{
    public class AO2Parser
    {
        private static readonly Regex PlayerRegex = new Regex(
            @"(?:\[(CM)\])?\s*\[(\d+)\]\s*([^\(\n]+)(?:\s*\(([^)]+)\))?$",
            RegexOptions.Compiled
        );

        public static List<Player> ParseGetArea(string input)
        {
            List<Player> players = new List<Player>();

            // Split by lines while handling extra spaces
            string[] playerEntries = input.Split(new[] { "\n", "\r", "-" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string entry in playerEntries)
            {
                string trimmedEntry = entry.Trim();
                Match match = PlayerRegex.Match(trimmedEntry);
                if (match.Success)
                {
                    //It's a player row


                    players.Add(new Player
                    {
                        IsCM = !string.IsNullOrEmpty(match.Groups[1].Value),
                        PlayerID = int.Parse(match.Groups[2].Value),
                        ICCharacterName = match.Groups[3].Value.Trim(),
                        OOCShowname = match.Groups[4].Success ? match.Groups[4].Value.Trim() : null
                    });
                }
            }

            return players;
        }
    }
}
