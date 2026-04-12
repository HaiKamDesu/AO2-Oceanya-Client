using AOBot_Testing.Agents;
using AOBot_Testing.Structures;

namespace AO2AIBot.Controller
{
    /// <summary>
    /// Builds AI-facing AO client snapshots from live client state.
    /// </summary>
    public static class AOClientControlSnapshotBuilder
    {
        /// <summary>
        /// Builds a snapshot for the provided profile and network clients.
        /// </summary>
        public static AOClientControlSnapshot Build(
            AOClient profileClient,
            AOClient? networkClient = null,
            string serverName = "")
        {
            if (profileClient == null)
            {
                throw new ArgumentNullException(nameof(profileClient));
            }

            AOClient effectiveNetworkClient = networkClient ?? profileClient;
            List<string> availablePositions = GetAvailablePositions(profileClient, effectiveNetworkClient);
            List<string> availableSfx = GetAvailableSfx(profileClient);
            IReadOnlyDictionary<string, IReadOnlyList<string>> availableCharacterEmotes = BuildCharacterEmoteMap();

            return new AOClientControlSnapshot
            {
                ServerName = serverName?.Trim() ?? string.Empty,
                ClientName = profileClient.clientName?.Trim() ?? string.Empty,
                IsConnected = effectiveNetworkClient.IsTransportConnected,
                IcShowname = profileClient.ICShowname?.Trim() ?? string.Empty,
                OocShowname = profileClient.OOCShowname?.Trim() ?? string.Empty,
                CurrentCharacter = profileClient.currentINI?.Name?.Trim() ?? string.Empty,
                CurrentEmote = profileClient.currentEmote?.Name?.Trim() ?? string.Empty,
                CurrentArea = effectiveNetworkClient.CurrentArea?.Trim() ?? string.Empty,
                CurrentPosition = profileClient.curPos?.Trim() ?? string.Empty,
                CurrentBackground = effectiveNetworkClient.curBG?.Trim() ?? string.Empty,
                CurrentIniPuppetName = effectiveNetworkClient.iniPuppetName?.Trim() ?? string.Empty,
                CurrentIniPuppetId = effectiveNetworkClient.iniPuppetID,
                CurrentSfx = profileClient.curSFX?.Trim() ?? string.Empty,
                DeskMod = profileClient.deskMod.ToString(),
                EmoteModifier = profileClient.emoteMod.ToString(),
                ShoutModifier = profileClient.shoutModifiers.ToString(),
                Effect = profileClient.effect.ToString(),
                TextColor = profileClient.textColor.ToString(),
                Flip = profileClient.flip,
                PreanimEnabled = profileClient.PreanimEnabled,
                Additive = profileClient.Additive,
                Immediate = profileClient.Immediate,
                Screenshake = profileClient.screenshake,
                SelfOffsetHorizontal = profileClient.SelfOffset.Horizontal,
                SelfOffsetVertical = profileClient.SelfOffset.Vertical,
                AvailableCharacters = CharacterFolder.FullList
                    .Select(character => character.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                AvailableEmotes = profileClient.currentINI?.configINI?.Emotions?.Values
                    .Select(emote => emote.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    ?? new List<string>(),
                AvailableCharacterEmotes = availableCharacterEmotes,
                AvailablePositions = availablePositions,
                AvailableAreas = effectiveNetworkClient.AvailableAreas
                    .Where(area => !string.IsNullOrWhiteSpace(area))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(area => area, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                AvailableSfx = availableSfx,
                ServerFeatures = effectiveNetworkClient.ServerFeatures
                    .Where(feature => !string.IsNullOrWhiteSpace(feature))
                    .OrderBy(feature => feature, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                AvailableIniPuppets = new Dictionary<string, bool>(
                    effectiveNetworkClient.ServerCharacterAvailability,
                    StringComparer.OrdinalIgnoreCase),
                AvailableAreaInfos = effectiveNetworkClient.AvailableAreaInfos
                    .Where(info => info != null)
                    .ToList()
                    .AsReadOnly(),
                CurrentAreaPlayers = effectiveNetworkClient.CurrentAreaPlayers
                    .Where(player => player != null)
                    .ToList()
                    .AsReadOnly()
            };
        }

        private static List<string> GetAvailablePositions(AOClient profileClient, AOClient effectiveNetworkClient)
        {
            Background? background = Background.FromBGPath(effectiveNetworkClient.curBG);
            if (background != null)
            {
                return background.GetPossiblePositions()
                    .Keys
                    .Where(position => !string.IsNullOrWhiteSpace(position))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(position => position, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            string defaultPosition = profileClient.currentINI?.configINI?.Side?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(defaultPosition))
            {
                return new List<string>();
            }

            return new List<string> { defaultPosition };
        }

        private static List<string> GetAvailableSfx(AOClient profileClient)
        {
            HashSet<string> availableSfx = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Default",
                "Nothing"
            };

            string characterDirectory = profileClient.currentINI?.DirectoryPath?.Trim() ?? string.Empty;
            foreach (string filePath in ResolveSoundListPaths(characterDirectory))
            {
                if (!File.Exists(filePath))
                {
                    continue;
                }

                foreach (string line in File.ReadLines(filePath))
                {
                    string candidate = line.Split('=')[0].Trim();
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        availableSfx.Add(candidate);
                    }
                }
            }

            return availableSfx
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildCharacterEmoteMap()
        {
            Dictionary<string, IReadOnlyList<string>> result =
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (CharacterFolder character in CharacterFolder.FullList)
            {
                string characterName = character.Name?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(characterName))
                {
                    continue;
                }

                IReadOnlyList<string> emotes = character.configINI?.Emotions?.Values == null
                    ? Array.Empty<string>()
                    : character.configINI.Emotions.Values
                        .Select(emote => emote.Name)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                        .AsReadOnly();

                result[characterName] = emotes;
            }

            return result;
        }

        private static IEnumerable<string> ResolveSoundListPaths(string characterDirectory)
        {
            if (!string.IsNullOrWhiteSpace(characterDirectory))
            {
                yield return Path.Combine(characterDirectory, "soundlist.ini");
                yield return Path.Combine(characterDirectory, "sounds.ini");
            }

            foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(baseFolder))
                {
                    continue;
                }

                yield return Path.Combine(baseFolder, "soundlist.ini");
            }
        }
    }
}
