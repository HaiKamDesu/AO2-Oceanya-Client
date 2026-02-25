using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OceanyaClient.Features.CharacterCreator
{
    public enum CharacterFrameTarget
    {
        PreAnimation = 0,
        AnimationA = 1,
        AnimationB = 2,
        Custom = 3
    }

    public enum CharacterFrameEventType
    {
        Sfx = 0,
        Screenshake = 1,
        Realization = 2
    }

    public sealed class CharacterCreationFrameEvent
    {
        public CharacterFrameTarget Target { get; set; } = CharacterFrameTarget.PreAnimation;
        public CharacterFrameEventType EventType { get; set; } = CharacterFrameEventType.Sfx;
        public int Frame { get; set; } = 1;
        public string Value { get; set; } = string.Empty;
        public string CustomTargetPath { get; set; } = string.Empty;
    }

    public sealed class CharacterCreationEmote
    {
        public string Name { get; set; } = "Normal";
        public string PreAnimation { get; set; } = "-";
        public string Animation { get; set; } = "normal";
        public int EmoteModifier { get; set; }
        public int DeskModifier { get; set; } = 1;
        public string SfxName { get; set; } = "1";
        public int SfxDelayMs { get; set; } = 1;
        public bool SfxLooping { get; set; }
        public int? PreAnimationDurationMs { get; set; }
        public int? StayTimeMs { get; set; }
        public string BlipsOverride { get; set; } = string.Empty;
        public List<CharacterCreationFrameEvent> FrameEvents { get; set; } = new List<CharacterCreationFrameEvent>();
    }

    public sealed class CharacterCreationAdvancedEntry
    {
        public string Section { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public sealed class CharacterCreationProject
    {
        public string MountPath { get; set; } = string.Empty;
        public string CharacterFolderName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ShowName { get; set; } = string.Empty;
        public string Side { get; set; } = "wit";
        public string Gender { get; set; } = "male";
        public string Blips { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Chat { get; set; } = string.Empty;
        public string Shouts { get; set; } = string.Empty;
        public string Realization { get; set; } = string.Empty;
        public string Effects { get; set; } = string.Empty;
        public string Scaling { get; set; } = string.Empty;
        public string Stretch { get; set; } = string.Empty;
        public string NeedsShowName { get; set; } = string.Empty;
        public List<string> AssetFolders { get; set; } = new List<string>();
        public List<CharacterCreationAdvancedEntry> AdvancedEntries { get; set; } = new List<CharacterCreationAdvancedEntry>();
        public List<CharacterCreationEmote> Emotes { get; set; } = new List<CharacterCreationEmote>();
    }

    public static class AOCharacterFileCreatorBuilder
    {
        private const string GeneratorReadme =
            "This character folder was originally created with Oceanya Client's \"AO Character File Creator\", " +
            "DM Scorpio2#3602 on discord if you are interested";

        public static string BuildCharIni(CharacterCreationProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            List<CharacterCreationEmote> emotes = project.Emotes ?? new List<CharacterCreationEmote>();
            if (emotes.Count == 0)
            {
                throw new InvalidOperationException("At least one emote is required.");
            }

            Dictionary<string, List<KeyValuePair<string, string>>> sections =
                new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.OrdinalIgnoreCase);

            void AddEntry(string section, string key, string value)
            {
                if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(key))
                {
                    return;
                }

                if (!sections.TryGetValue(section, out List<KeyValuePair<string, string>>? entries))
                {
                    entries = new List<KeyValuePair<string, string>>();
                    sections[section] = entries;
                }

                entries.Add(new KeyValuePair<string, string>(key.Trim(), value?.Trim() ?? string.Empty));
            }

            AddEntry("version", "major", "1");

            AddEntry("Options", "name", FirstNonEmpty(project.Name, project.CharacterFolderName));
            AddEntry("Options", "showname", FirstNonEmpty(project.ShowName, project.CharacterFolderName));
            AddEntry("Options", "side", FirstNonEmpty(project.Side, "wit"));
            AddEntry("Options", "gender", FirstNonEmpty(project.Gender, "male"));
            AddOptionalOption("blips", project.Blips);
            AddOptionalOption("category", project.Category);
            AddOptionalOption("chat", project.Chat);
            AddOptionalOption("shouts", project.Shouts);
            AddOptionalOption("realization", project.Realization);
            AddOptionalOption("effects", project.Effects);
            AddOptionalOption("scaling", project.Scaling);
            AddOptionalOption("stretch", project.Stretch);
            AddOptionalOption("needs_showname", project.NeedsShowName);

            void AddOptionalOption(string key, string value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    AddEntry("Options", key, value);
                }
            }

            AddEntry("Emotions", "number", emotes.Count.ToString());
            for (int i = 0; i < emotes.Count; i++)
            {
                CharacterCreationEmote emote = emotes[i] ?? new CharacterCreationEmote();
                int emoteId = i + 1;
                string emoteName = string.IsNullOrWhiteSpace(emote.Name) ? $"Emote{emoteId}" : emote.Name.Trim();
                string preAnimation = string.IsNullOrWhiteSpace(emote.PreAnimation) ? "-" : emote.PreAnimation.Trim();
                string animation = string.IsNullOrWhiteSpace(emote.Animation) ? "normal" : emote.Animation.Trim();
                AddEntry("Emotions", emoteId.ToString(), $"{emoteName}#{preAnimation}#{animation}#{emote.EmoteModifier}#{emote.DeskModifier}");

                AddEntry("SoundN", emoteId.ToString(), string.IsNullOrWhiteSpace(emote.SfxName) ? "1" : emote.SfxName.Trim());
                AddEntry("SoundT", emoteId.ToString(), Math.Max(0, emote.SfxDelayMs).ToString());
                AddEntry("SoundL", emoteId.ToString(), emote.SfxLooping ? "1" : "0");

                if (!string.Equals(preAnimation, "-", StringComparison.Ordinal) && emote.PreAnimationDurationMs.HasValue)
                {
                    AddEntry("Time", preAnimation, Math.Max(0, emote.PreAnimationDurationMs.Value).ToString());
                }

                if (emote.StayTimeMs.HasValue)
                {
                    string stayTimeKey = !string.Equals(preAnimation, "-", StringComparison.Ordinal)
                        ? preAnimation
                        : animation;
                    AddEntry("stay_time", stayTimeKey, Math.Max(0, emote.StayTimeMs.Value).ToString());
                }

                foreach (CharacterCreationFrameEvent frameEvent in emote.FrameEvents ?? new List<CharacterCreationFrameEvent>())
                {
                    string target = ResolveFrameTargetPath(emote, frameEvent);
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        continue;
                    }

                    string sectionSuffix = frameEvent.EventType switch
                    {
                        CharacterFrameEventType.Sfx => "_FrameSFX",
                        CharacterFrameEventType.Screenshake => "_FrameScreenshake",
                        CharacterFrameEventType.Realization => "_FrameRealization",
                        _ => "_FrameSFX"
                    };
                    string sectionName = target + sectionSuffix;
                    string frameKey = Math.Max(1, frameEvent.Frame).ToString();
                    string value = frameEvent.EventType == CharacterFrameEventType.Sfx
                        ? (string.IsNullOrWhiteSpace(frameEvent.Value) ? "1" : frameEvent.Value.Trim())
                        : (string.IsNullOrWhiteSpace(frameEvent.Value) ? "1" : frameEvent.Value.Trim());
                    AddEntry(sectionName, frameKey, value);
                }
            }

            Dictionary<string, int> optionsProfileIndexes =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < emotes.Count; i++)
            {
                string overrideBlips = emotes[i]?.BlipsOverride?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(overrideBlips))
                {
                    continue;
                }

                if (!optionsProfileIndexes.TryGetValue(overrideBlips, out int index))
                {
                    index = optionsProfileIndexes.Count + 1;
                    optionsProfileIndexes[overrideBlips] = index;
                    AddEntry("Options" + index, "blips", overrideBlips);
                }

                AddEntry("OptionsN", (i + 1).ToString(), index.ToString());
            }

            foreach (CharacterCreationAdvancedEntry entry in project.AdvancedEntries ?? new List<CharacterCreationAdvancedEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Section) || string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                AddEntry(entry.Section.Trim(), entry.Key.Trim(), entry.Value?.Trim() ?? string.Empty);
            }

            string[] preferredSectionOrder =
            {
                "version",
                "Options",
                "Shouts",
                "Time",
                "stay_time",
                "Emotions",
                "SoundN",
                "SoundT",
                "SoundL",
                "OptionsN"
            };

            StringBuilder builder = new StringBuilder(8192);
            HashSet<string> emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string sectionName in preferredSectionOrder)
            {
                EmitSection(sectionName);
            }

            foreach (string sectionName in sections.Keys.OrderBy(static s => s, StringComparer.OrdinalIgnoreCase))
            {
                EmitSection(sectionName);
            }

            void EmitSection(string sectionName)
            {
                if (!sections.TryGetValue(sectionName, out List<KeyValuePair<string, string>>? entries) || entries.Count == 0)
                {
                    return;
                }

                if (!emitted.Add(sectionName))
                {
                    return;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append('[').Append(sectionName).AppendLine("]");
                foreach (KeyValuePair<string, string> entry in entries)
                {
                    builder.Append(entry.Key).Append('=').AppendLine(entry.Value);
                }
            }

            return builder.ToString();
        }

        public static string CreateCharacterFolder(CharacterCreationProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            string mountPath = project.MountPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(mountPath))
            {
                throw new InvalidOperationException("A mount path is required.");
            }

            if (!Directory.Exists(mountPath))
            {
                throw new DirectoryNotFoundException("Mount path does not exist: " + mountPath);
            }

            string folderName = SanitizeFolderName(project.CharacterFolderName);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                throw new InvalidOperationException("Character folder name is required.");
            }

            string charactersRoot = Path.Combine(mountPath, "characters");
            Directory.CreateDirectory(charactersRoot);

            string characterDirectory = Path.Combine(charactersRoot, folderName);
            if (Directory.Exists(characterDirectory))
            {
                throw new IOException("Character directory already exists: " + characterDirectory);
            }

            Directory.CreateDirectory(characterDirectory);
            Directory.CreateDirectory(Path.Combine(characterDirectory, "Emotions"));

            foreach (string requested in project.AssetFolders ?? new List<string>())
            {
                string relative = NormalizeRelativeFolder(requested);
                if (string.IsNullOrWhiteSpace(relative))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.Combine(characterDirectory, relative));
            }

            string ini = BuildCharIni(project);
            File.WriteAllText(Path.Combine(characterDirectory, "char.ini"), ini);
            File.WriteAllText(Path.Combine(characterDirectory, "readme.txt"), GeneratorReadme);
            return characterDirectory;
        }

        public static int ConvertFrameToMilliseconds(int frame, double fps)
        {
            if (fps <= 0)
            {
                return 0;
            }

            int safeFrame = Math.Max(0, frame);
            double milliseconds = (safeFrame * 1000.0) / fps;
            return (int)Math.Round(milliseconds, MidpointRounding.AwayFromZero);
        }

        private static string ResolveFrameTargetPath(CharacterCreationEmote emote, CharacterCreationFrameEvent frameEvent)
        {
            string preAnimation = emote.PreAnimation?.Trim() ?? string.Empty;
            string animation = emote.Animation?.Trim() ?? string.Empty;

            return frameEvent.Target switch
            {
                CharacterFrameTarget.PreAnimation => string.Equals(preAnimation, "-", StringComparison.Ordinal) ? string.Empty : preAnimation,
                CharacterFrameTarget.AnimationA => string.IsNullOrWhiteSpace(animation) ? string.Empty : "(a)/" + animation,
                CharacterFrameTarget.AnimationB => string.IsNullOrWhiteSpace(animation) ? string.Empty : "(b)/" + animation,
                CharacterFrameTarget.Custom => frameEvent.CustomTargetPath?.Trim() ?? string.Empty,
                _ => string.Empty
            };
        }

        private static string FirstNonEmpty(string first, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(first))
            {
                return first.Trim();
            }

            return fallback?.Trim() ?? string.Empty;
        }

        private static string SanitizeFolderName(string folderName)
        {
            string name = folderName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Trim();
        }

        private static string NormalizeRelativeFolder(string folder)
        {
            string raw = (folder ?? string.Empty).Trim().Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string[] segments = raw.Split('/', StringSplitOptions.RemoveEmptyEntries);
            List<string> safeSegments = new List<string>();
            foreach (string segment in segments)
            {
                string trimmed = segment.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "." || trimmed == "..")
                {
                    continue;
                }

                string safe = trimmed;
                foreach (char c in Path.GetInvalidFileNameChars())
                {
                    safe = safe.Replace(c, '_');
                }

                safe = safe.Trim();
                if (!string.IsNullOrWhiteSpace(safe))
                {
                    safeSegments.Add(safe);
                }
            }

            return safeSegments.Count == 0 ? string.Empty : Path.Combine(safeSegments.ToArray());
        }
    }
}
