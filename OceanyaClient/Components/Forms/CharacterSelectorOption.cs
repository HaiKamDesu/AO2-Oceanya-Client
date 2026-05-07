namespace OceanyaClient
{
    /// <summary>
    /// Lightweight character picker row used by settings dialogs.
    /// </summary>
    public sealed class CharacterSelectorOption
    {
        public CharacterSelectorOption(string name, string showname, string directoryPath, string iconPath = "")
        {
            Name = name ?? string.Empty;
            Showname = showname ?? string.Empty;
            DirectoryPath = directoryPath ?? string.Empty;
            IconPath = iconPath ?? string.Empty;
        }

        public string Name { get; }

        public string Showname { get; }

        public string DirectoryPath { get; }

        public string IconPath { get; }

        public string DisplayText => string.IsNullOrWhiteSpace(Showname)
            ? Name
            : $"{Name}  |  {Showname}";
    }
}
