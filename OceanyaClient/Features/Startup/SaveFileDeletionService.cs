using Common;
using System;
using System.IO;

namespace OceanyaClient.Features.Startup
{
    public static class SaveFileDeletionService
    {
        public static string ResolveCurrentSaveDirectory()
        {
            return ResolveSaveDirectory(SaveFile.CurrentStoragePath);
        }

        public static string ResolveSaveDirectory(string saveFilePath)
        {
            string normalizedPath = Path.GetFullPath(saveFilePath?.Trim() ?? string.Empty);
            string fileName = Path.GetFileName(normalizedPath);
            if (!string.Equals(fileName, "savefile.json", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Savefile deletion was blocked because the active save path is not savefile.json.");
            }

            string directory = Path.GetDirectoryName(normalizedPath) ?? string.Empty;
            string directoryName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.Equals(directoryName, "OceanyaClient", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(directoryName, "OceanyaClientDev", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Savefile deletion was blocked because the active save folder is not an OceanyaClient app-data folder.");
            }

            return directory;
        }

        public static void DeleteCurrentSaveDirectory()
        {
            string directory = ResolveCurrentSaveDirectory();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
