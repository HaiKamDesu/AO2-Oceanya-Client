
namespace AOBot_Testing.Structures
{
    public class Emote(int id)
    {
        public string DisplayID { get => ID + ": " + Name; }
        public int ID { get; set; } = id;
        public string Name { get; set; } = "normal";
        public string PreAnimation { get; set; } = "";
        public string Animation { get; set; } = "normal";
        public ICMessage.EmoteModifiers Modifier { get; set; }
        public ICMessage.DeskMods DeskMod { get; set; }

        public string PathToImage_off { get; set; } = "";
        public string PathToImage_on { get; set; } = "";

        public string sfxName { get; set; } = "1";
        public int sfxDelay { get; set; } = 1;
        public string sfxLooping { get; set; } = "0";

        public static Emote ParseEmoteLine(string data)
        {
            var parts = data.Split('#');

            return new Emote(id: -1)
            {
                Name = parts.Length > 0 ? parts[0] : "",
                PreAnimation = parts.Length > 1 ? parts[1] : "",
                Animation = parts.Length > 2 ? parts[2] : "",
                Modifier = parts.Length > 3 ? (int.TryParse(parts[3], out int newEmoteMod) ?
                        (ICMessage.EmoteModifiers)newEmoteMod :
                        ICMessage.EmoteModifiers.NoPreanimation)
                    : ICMessage.EmoteModifiers.NoPreanimation,
                DeskMod = ParseDeskMod(parts.Length > 4 ? parts[4] : null)
            };
        }

        private static ICMessage.DeskMods ParseDeskMod(string? value)
        {
            if (value == null)
            {
                return ICMessage.DeskMods.Unspecified;
            }

            string normalized = value.Trim();
            if (normalized.Length == 0)
            {
                return ICMessage.DeskMods.Unspecified;
            }

            return int.TryParse(normalized, out int newDeskmod)
                ? (ICMessage.DeskMods)newDeskmod
                : ICMessage.DeskMods.Hidden;
        }
    }
}


