using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml.Linq;

namespace AOBot_Testing.Structures
{
    public class Background
    {
        public static List<string> BackgroundFolders => Globals.BaseFolders.Select(x => Path.Combine(x, "background")).ToList();
        public static Dictionary<string, string> posToBGName = new Dictionary<string, string>()
        {
            {"def","defenseempty"},
            {"hld","helperstand"},
            {"jud","judgestand"},
            {"hlp","prohelperstand"},
            {"pro","prosecutorempty"},
            {"wit","witnessempty"},
        };


        public string Name { get; set; } = string.Empty;
        public string PathToFile { get; set; } = string.Empty;
        public List<string> bgImages { get; set; } = new List<string>();

        public static Background? FromBGPath(string curBG)
        {
            foreach (var BackgroundFolder in BackgroundFolders)
            {
                var bgFolder = Path.Combine(BackgroundFolder, curBG);

                if (Directory.Exists(bgFolder))
                {
                    var newBG = new Background();
                    newBG.Name = Path.GetFileName(bgFolder) ?? string.Empty;
                    newBG.PathToFile = bgFolder;

                    var bgFiles = Directory.GetFiles(bgFolder, "*.*", SearchOption.TopDirectoryOnly);

                    var extensions = Globals.AllowedImageExtensions;
                    var exclude = new List<string>() { "defensedesk", "prosecutiondesk", "stand", "judgedesk" };

                    List<string> bgFilesFiltered = new();

                    foreach (var file in bgFiles)
                    {
                        var fileExtension = Path.GetExtension(file).TrimStart('.').ToLower();
                        if (extensions.Contains(fileExtension) && !exclude.Any(ex => Path.GetFileNameWithoutExtension(file).ToLower() == ex))
                        {
                            var existingFile = bgFilesFiltered.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == Path.GetFileNameWithoutExtension(file));
                            if (existingFile != null)
                            {
                                var existingFileExtension = Path.GetExtension(existingFile).TrimStart('.').ToLower();
                                if (extensions.IndexOf(fileExtension) < extensions.IndexOf(existingFileExtension))
                                {
                                    bgFilesFiltered.Remove(existingFile);
                                    bgFilesFiltered.Add(file);
                                }
                            }
                            else
                            {
                                bgFilesFiltered.Add(file);
                            }
                        }
                    }

                    newBG.bgImages = bgFilesFiltered;


                    return newBG;
                }
            }
            
            return null;
        }

        public string? GetBGImage(string pos)
        {
            var imageName = pos;
            if (posToBGName.ContainsKey(pos))
            {
                imageName = posToBGName[pos];
            }

            return bgImages.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).ToLower() == imageName);
        }

        public Dictionary<string, string> GetPossiblePositions()
        {
            // Step 1: Iterate over bgImages  
            return bgImages.GroupBy(f =>
            {
                // Step 2: Get the file name without extension and convert to lower case  
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(f).ToLower();

                // Step 3: Check if the file name is in the posToBGName dictionary  
                var posKey = posToBGName.FirstOrDefault(p => fileNameWithoutExtension == p.Value).Key;

                // Step 4: If the key is found, return the key, otherwise return the file name  
                return posKey ?? fileNameWithoutExtension;
            })
            .Where(g => g.Count() == 1) // Ignore repeated key values  
            .ToDictionary(g => g.Key, g => g.First());
        }
    }
}
