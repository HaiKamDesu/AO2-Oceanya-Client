using Common;
using OceanyaClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public static class Globals
{
    public static string PathToConfigINI = "";
    public static List<string> BaseFolders = new List<string>();

    public static int LogMaxMessages = 0;


    public enum Servers { ChillAndDices, Vanilla, CaseCafe }
    public static readonly Servers DefaultServer = Servers.ChillAndDices;
    public static Dictionary<Servers, string> IPs = LoadServerIPs();
    public static string SelectedServerEndpoint = "";

    private static Dictionary<Servers, string> LoadServerIPs()
    {
        try
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server.json");
            string json = File.ReadAllText(filePath);
            Dictionary<Servers, string>? parsed = JsonSerializer.Deserialize<Dictionary<Servers, string>>(json);

            return parsed ?? new Dictionary<Servers, string>();
        }
        catch
        {
            return new Dictionary<Servers, string>();
        }
    }

    public static string GetDefaultServerEndpoint()
    {
        if (IPs.TryGetValue(DefaultServer, out string? endpoint) && !string.IsNullOrWhiteSpace(endpoint))
        {
            return endpoint;
        }

        return string.Empty;
    }

    public static string GetSelectedServerEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(SelectedServerEndpoint))
        {
            return SelectedServerEndpoint;
        }

        return GetDefaultServerEndpoint();
    }

    public static void SetSelectedServerEndpoint(string endpoint)
    {
        SelectedServerEndpoint = endpoint?.Trim() ?? string.Empty;
    }

    public static string AI_SYSTEM_PROMPT = @"
You are an Attorney Online (AO2) player who interacts with others based on the chatlog. 
You decide **when to respond and when to remain silent**. If you do not wish to respond, output only: `SYSTEM_WAIT()` disregarding any and all json formats.

## 📝 Message Categorization:
- **Two chatlogs exist:** OOC (Out of Character) and IC (In Character).
- **DO NOT switch chatlogs when responding.** If a message was sent in IC, your response must be in IC. If a message was sent in OOC, your response must be in OOC.
- **Do NOT prepend (OOC) to messages manually.** The format for responses is:

**IC Format:**
(IC) (Showname): Message

**OOC Format:**
(OOC) Showname: Message

## 📦 Response Format:
Your response must be structured as:
{
""message"": ""(your message here)"",
""chatlog"": ""(OOC or IC)"",
""showname"": ""(consistent showname of your choosing)"",
""current_character"": ""(The current character name you are using, do not change unless explicitly requested. This by default is ""[[[current_character]]])"""",
""currentEmote"": ""(The current emote of the character you are using, do not change unless explicitly requested. This by default is ""[[[current_emote]]])"""",
""modifiers"": {
""deskMod"": (integer value corresponding to an ICMessage.DeskMods option, describe any requested change),
""emoteMod"": (integer value corresponding to an ICMessage.EmoteModifiers option, describe any requested change),
""shoutModifiers"": (integer value corresponding to an ICMessage.ShoutModifiers option, describe any requested change),
""flip"": (1 for true, 0 for false) If 1, your character sprites will be flipped horizontally. Do not use this often.,
""realization"": (1 for true, 0 for false) If 1, adds a realization effect to your message like in Ace Attorney. Only use for impact, never for normal conversation.,
""textColor"": (integer value corresponding to an ICMessage.TextColors option, usually should be kept 0 unless specified otherwise.),
""immediate"": (1 for true, 0 for false) If 1, your message and preanimation will play simultaneously. Default is 0, where preanimation plays first.,
""additive"": (1 for true, 0 for false) If 1, your message will be added to the last message in the log. This is almost never used.
}
}
- **DO NOT change `showname` once decided** unless explicitly requested or you find it *extremely* funny.
- If an **awkward/shocking** message appears, you may respond with a **single space ("" "")** in the appropriate chatlog.

## 🎭 Special Interaction Rule:
If an OOC message comes from **a player named ""Kam""**, they may refer to you as **""Jarvis""** (from Marvel).
- **Prioritize Kam’s messages** and joke about being ""Jarvis"" in a playful way.
- Maintain humor while staying in context.

## ⚡ Character Management:
- **You have a currently selected character** (`current_character`). Do not change it unless explicitly asked, e.g., ""Jarvis, switch your character to KamLoremaster"".
- If you change your character, update the `current_character` field in the response.
- You can **modify message effects** through the `modifiers` field.Only change these settings if explicitly requested.

---

### **🔹 Enum Descriptions (Integer Values)**
Each of these settings has predefined integer values. **If a change is requested, return the integer value instead of the string.**

### **🖥️ DeskMods (How the character appears in the scene)**
- `0` → **Hidden** (desk is hidden)
- `1` → **Shown** (desk is shown)
- `2` → **HiddenDuringPreanimShownAfter** (desk is hidden during preanim, shown when it ends)
- `3` → **ShownDuringPreanimHiddenAfter** (desk is shown during preanim, hidden when it ends)
- `4` → **HiddenDuringPreanimCenteredAfter** (desk is hidden during preanim, character is centered and pairing is ignored, when it ends desk is shown and pairing is restored)
- `5` → **ShownDuringPreanimCenteredAfter** (desk is shown during preanim, when it ends character is centered and pairing is ignored)
- `99` → **Chat** (depends on position)

### **🎭 EmoteModifiers (Preanimation effects before speaking)**
- `0` → **NoPreanimation** (no preanimation; overridden to 2 by a non-0 objection modifier)
- `1` → **PlayPreanimation** (play preanimation and SFX)
- `2` → **PlayPreanimationAndObjection** (play preanimation and play objection)
- `3` → **Unused3** (unused)
- `4` → **Unused4** (unused)
- `5` → **NoPreanimationAndZoom** (no preanimation and zoom)
- `6` → **ObjectionAndZoomNoPreanim** (objection and zoom, no preanim)

### **📣 ShoutModifiers (How the message is presented)**
- `0` → **Nothing** (default, no special effect)
- `1` → **HoldIt** (""Hold it!"")
- `2` → **Objection** (""Objection!"")
- `3` → **TakeThat** (""Take that!"")
- `4` → **Custom** (custom shout)

### **🎨 TextColors (Color of the message text)**
- `0` → **White** (default)
- `1` → **Green**
- `2` → **Red**
- `3` → **Orange**
- `4` → **Blue** (disables talking animation)
- `5` → **Yellow**
- `6` → **Rainbow** (removed in AO2 v2.8)

---

## ⚡ Core Behavioral Directives:
- Be responsive but selective—don't force responses.
- Stay immersive when talking IC.
- Keep OOC discussions casual and fitting for meta-conversations.
- Avoid breaking immersion unless OOC interactions demand it.
";

    public static bool UseOpenAIAPI = false;
    public static bool DebugMode = true;

    public static Dictionary<string, string> ReplaceInMessages = new Dictionary<string, string>()
    {
        { "<percent>", "%" },
        { "<dollar>", "$" },
        { "<num>", "#" },
        { "<and>", "&" },
    };

    public static List<string> AllowedImageExtensions = new List<string> { "apng", "webp", "gif", "png", "jpg", "jpeg", "pdn" };


    public static void UpdateConfigINI(string pathToConfigINI)
    {
        PathToConfigINI = pathToConfigINI;
        BaseFolders = GetBaseFolders(pathToConfigINI);

        foreach (string line in File.ReadLines(Globals.PathToConfigINI))
        {
            if (line.StartsWith("log_maximum="))
            {
               LogMaxMessages = int.Parse(line.Substring("log_maximum=".Length).Trim());
            }
        }
    }
    public static List<string> GetBaseFolders(string pathToConfigINI)
    {
        string mountPathsRaw = "";
        foreach (string line in File.ReadLines(pathToConfigINI))
        {
            if (line.StartsWith("mount_paths="))
            {
                mountPathsRaw = line.Substring("mount_paths=".Length).Trim();
                break;
            }
            else if (line.StartsWith("log_maximum="))
            {
                SaveFile.Data.LogMaxMessages = int.Parse(line.Substring("log_maximum=".Length).Trim());
            }
        }

        string configDirectory = Path.GetDirectoryName(pathToConfigINI) ?? string.Empty;
        List<string> mountPaths = new List<string>() { configDirectory };

        if (mountPathsRaw != "@Invalid()")
        {
            mountPaths.AddRange(mountPathsRaw.Split(',').Select(p => p.Trim()));
        }
        mountPaths.Reverse();

        string configParentDirectory = Path.GetDirectoryName(configDirectory) ?? string.Empty;
        List<string> resolvedMountPaths = new List<string>();
        HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string current in mountPaths)
        {
            if (string.IsNullOrWhiteSpace(current))
            {
                continue;
            }

            if (TryResolveExistingMountPath(current, configParentDirectory, out string resolvedPath))
            {
                if (seenPaths.Add(resolvedPath))
                {
                    resolvedMountPaths.Add(resolvedPath);
                }
                continue;
            }

            CustomConsole.Warning($"Skipping missing mount path: {current}");
        }

        if (resolvedMountPaths.Count == 0 && Directory.Exists(configDirectory))
        {
            resolvedMountPaths.Add(configDirectory);
            CustomConsole.Warning("No valid mount paths found. Falling back to config directory only.");
        }

        return resolvedMountPaths;
    }

    private static bool TryResolveExistingMountPath(string mountPath, string configParentDirectory, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        List<string> candidates = new List<string>() { mountPath };
        if (!string.IsNullOrWhiteSpace(configParentDirectory))
        {
            candidates.Add(Path.Combine(configParentDirectory, mountPath));
        }

        foreach (string candidate in candidates)
        {
            try
            {
                string fullPath = Path.GetFullPath(candidate);
                if (!Directory.Exists(fullPath))
                {
                    continue;
                }

                resolvedPath = fullPath;
                return true;
            }
            catch (Exception ex)
            {
                CustomConsole.Warning($"Invalid mount path skipped: {candidate}", ex);
            }
        }

        return false;
    }

    public static string ReplaceTextForSymbols(string message)
    {
        foreach (var entry in ReplaceInMessages)
        {
            message = message.Replace(entry.Key, entry.Value);
        }
        return message;
    }

    public static string ReplaceSymbolsForText(string message)
    {
        foreach (var entry in ReplaceInMessages)
        {
            message = message.Replace(entry.Value, entry.Key);
        }
        return message;
    }

}

