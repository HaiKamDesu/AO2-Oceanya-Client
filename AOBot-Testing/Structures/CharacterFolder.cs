using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Common;

namespace AOBot_Testing.Structures
{
    [Serializable]
    public class CharacterFolder
    {
        #region Static index and lazy parse
        // ---------------------------------------------------------------------------------------------
        // Characters are stored as a CHEAP INDEX plus a bounded cache of PARSED characters.
        //
        // Everything used to live in one list of fully parsed CharacterFolder objects, persisted as a
        // single JSON file. On a 2627-character install that file was 26 MB: 677 ms of
        // JsonSerializer.Deserialize on the launch critical path, a permanently resident object graph of
        // ~100k Emote objects, and - worst of all - a full 26 MB re-serialise every time ONE character was
        // upserted, which is exactly what the live asset watcher does.
        //
        // The index holds only what lookups and lists actually need (name, paths, showname, icon). It is a
        // few hundred KB, loads in milliseconds, and is enough to answer "does this character exist", "what
        // is in the dropdown", and "which character has this showname". The expensive part - char.ini with
        // every emote - is parsed on first real use and held in an LRU bounded by ParsedCacheLimit, so
        // memory no longer scales with the size of the install.
        // ---------------------------------------------------------------------------------------------

        private const int IndexVersion = 2;

        /// <summary>
        /// How many fully parsed characters are kept in memory at once.
        /// </summary>
        /// <remarks>
        /// Parsing one char.ini is ~1 ms, so evicting is cheap; holding thousands is not. This is
        /// comfortably more than any session touches at once (the active clients, the emote grid, whatever
        /// is on screen) while being a tiny fraction of a large install.
        /// </remarks>
        private const int ParsedCacheLimit = 128;

        public static List<string> CharacterFolders => Globals.BaseFolders.Select(x => Path.Combine(x, "characters")).ToList();

        private static string indexFile = Path.Combine(Path.GetTempPath(), "characters_index.json");
        private static bool indexPathInitialized;
        private static List<CharacterIndexEntry> characterIndex = new List<CharacterIndexEntry>();
        private static Dictionary<string, CharacterIndexEntry> indexByName =
            new Dictionary<string, CharacterIndexEntry>(StringComparer.OrdinalIgnoreCase);
        private static bool indexLoaded;

        private static readonly Dictionary<string, CharacterFolder> parsedCharacters =
            new Dictionary<string, CharacterFolder>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> parsedOrder = new LinkedList<string>();

        /// <summary>Diagnostics for the first index load this process — set once per cold load.</summary>
        public static bool? LastFullListWasCacheHit { get; private set; }
        public static long LastFullListLoadMs { get; private set; }
        public static int LastFullListLoadCount { get; private set; }
        /// <summary>Size in bytes of the index file read on the last cache-hit load (-1 if not a cache hit).</summary>
        public static long LastFullListCacheFileBytes { get; private set; } = -1;
        /// <summary>Milliseconds spent on the raw <c>File.ReadAllText</c> disk read of the index file.</summary>
        public static long LastFullListFileReadMs { get; private set; } = -1;
        /// <summary>Milliseconds spent on <c>JsonSerializer.Deserialize</c> of the index file contents.</summary>
        public static long LastFullListDeserializeMs { get; private set; } = -1;
        /// <summary>Milliseconds spent validating the loaded index against the current environment.</summary>
        public static long LastFullListCompatibilityCheckMs { get; private set; } = -1;

        /// <summary>Guards the index and the parsed-character cache.</summary>
        private static readonly object fullListLoadLock = new object();

        /// <summary>Characters known to exist, without parsing any of them.</summary>
        public static IReadOnlyList<CharacterIndexEntry> Index
        {
            get
            {
                lock (fullListLoadLock)
                {
                    EnsureIndexLoadedLocked();
                    return characterIndex;
                }
            }
        }

        /// <summary>Number of characters in the index.</summary>
        public static int CachedCharacterCount
        {
            get
            {
                lock (fullListLoadLock)
                {
                    return characterIndex.Count;
                }
            }
        }

        /// <summary>Number of fully parsed characters currently held in memory.</summary>
        public static int ParsedCharacterCount
        {
            get
            {
                lock (fullListLoadLock)
                {
                    return parsedCharacters.Count;
                }
            }
        }

        /// <summary>
        /// Every character, fully parsed.
        /// </summary>
        /// <remarks>
        /// Only for callers that genuinely need all of them at once - the Character Database Viewer and
        /// tests. It parses whatever is not already cached and returns a fresh list the CALLER owns, so the
        /// memory is released when the caller drops it. Anything that only needs names, existence or a
        /// single character must use <see cref="Index"/>, <see cref="GetByName"/> or
        /// <see cref="GetByNameOrShowName"/> instead; calling this on a hot path re-parses the whole
        /// install.
        /// </remarks>
        public static List<CharacterFolder> FullList
        {
            get
            {
                List<CharacterIndexEntry> entries;
                lock (fullListLoadLock)
                {
                    EnsureIndexLoadedLocked();
                    entries = new List<CharacterIndexEntry>(characterIndex);
                }

                List<CharacterFolder> parsed = new List<CharacterFolder>(entries.Count);
                foreach (CharacterIndexEntry entry in entries)
                {
                    CharacterFolder? character = Get(entry);
                    if (character != null)
                    {
                        parsed.Add(character);
                    }
                }

                return parsed;
            }
        }

        /// <summary>Parses (or returns the cached parse of) the character an index entry points at.</summary>
        public static CharacterFolder? Get(CharacterIndexEntry? entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.DirectoryPath))
            {
                return null;
            }

            string cacheKey = NormalizePathForCompare(entry.DirectoryPath);
            lock (fullListLoadLock)
            {
                if (parsedCharacters.TryGetValue(cacheKey, out CharacterFolder? cached))
                {
                    TouchParsedLocked(cacheKey);
                    return cached;
                }
            }

            CharacterFolder parsedCharacter;
            try
            {
                string charIniPath = string.IsNullOrWhiteSpace(entry.PathToConfigIni)
                    ? Path.Combine(entry.DirectoryPath, "char.ini")
                    : entry.PathToConfigIni;
                if (!File.Exists(charIniPath))
                {
                    return null;
                }

                parsedCharacter = Create(charIniPath);
            }
            catch (Exception ex)
            {
                CustomConsole.Warning($"Failed to parse character '{entry.Name}'.", ex);
                return null;
            }

            lock (fullListLoadLock)
            {
                parsedCharacters[cacheKey] = parsedCharacter;
                TouchParsedLocked(cacheKey);
                TrimParsedCacheLocked();
            }

            return parsedCharacter;
        }

        /// <summary>Finds a character by folder name and parses it.</summary>
        public static CharacterFolder? GetByName(string? characterName)
        {
            return Get(FindIndexEntryByName(characterName));
        }

        /// <summary>Finds a character by its folder path and parses it.</summary>
        public static CharacterFolder? GetByDirectory(string? directoryPath)
        {
            return Get(FindIndexEntryByDirectory(directoryPath));
        }

        /// <summary>
        /// Finds a character by folder name, falling back to its AO2 showname, and parses it.
        /// </summary>
        /// <remarks>
        /// The showname lives in the index precisely so this reverse lookup does not have to parse every
        /// character to answer it - that was the single worst consequence of the old all-or-nothing cache.
        /// </remarks>
        public static CharacterFolder? GetByNameOrShowName(string? characterName)
        {
            return Get(FindIndexEntryByNameOrShowName(characterName));
        }

        /// <summary>Index entry matching a folder name, <c>[Options] name</c>, or showname.</summary>
        public static CharacterIndexEntry? FindIndexEntryByNameOrShowName(string? characterName)
        {
            CharacterIndexEntry? entry = FindIndexEntryByName(characterName);
            if (entry != null)
            {
                return entry;
            }

            string normalized = (characterName ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return null;
            }

            lock (fullListLoadLock)
            {
                EnsureIndexLoadedLocked();
                foreach (CharacterIndexEntry candidate in characterIndex)
                {
                    if (string.Equals(candidate.OptionsName, normalized, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(candidate.ShowName, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>Whether any indexed character matches this folder name, option name, or showname.</summary>
        public static bool ExistsByNameOrShowName(string? characterName)
        {
            return FindIndexEntryByNameOrShowName(characterName) != null;
        }

        /// <summary>Whether a character folder with this name is indexed.</summary>
        public static bool Exists(string? characterName)
        {
            return FindIndexEntryByName(characterName) != null;
        }

        /// <summary>Index entry for a folder name, or <c>null</c>.</summary>
        public static CharacterIndexEntry? FindIndexEntryByName(string? characterName)
        {
            string normalized = (characterName ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return null;
            }

            lock (fullListLoadLock)
            {
                EnsureIndexLoadedLocked();
                return indexByName.TryGetValue(normalized, out CharacterIndexEntry? entry) ? entry : null;
            }
        }

        /// <summary>Index entry for a folder path, or <c>null</c>.</summary>
        public static CharacterIndexEntry? FindIndexEntryByDirectory(string? directoryPath)
        {
            string normalized = NormalizePathForCompare(directoryPath ?? string.Empty);
            if (normalized.Length == 0)
            {
                return null;
            }

            lock (fullListLoadLock)
            {
                EnsureIndexLoadedLocked();
                foreach (CharacterIndexEntry entry in characterIndex)
                {
                    if (string.Equals(NormalizePathForCompare(entry.DirectoryPath), normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry;
                    }
                }
            }

            return null;
        }

        private static void TouchParsedLocked(string cacheKey)
        {
            for (LinkedListNode<string>? node = parsedOrder.First; node != null; node = node.Next)
            {
                if (string.Equals(node.Value, cacheKey, StringComparison.OrdinalIgnoreCase))
                {
                    parsedOrder.Remove(node);
                    break;
                }
            }

            parsedOrder.AddLast(cacheKey);
        }

        private static void TrimParsedCacheLocked()
        {
            while (parsedOrder.Count > ParsedCacheLimit && parsedOrder.First != null)
            {
                string oldest = parsedOrder.First.Value;
                parsedOrder.RemoveFirst();
                parsedCharacters.Remove(oldest);
            }
        }

        private static void DropParsedLocked(string directoryPath)
        {
            string cacheKey = NormalizePathForCompare(directoryPath);
            if (cacheKey.Length == 0)
            {
                return;
            }

            parsedCharacters.Remove(cacheKey);
            for (LinkedListNode<string>? node = parsedOrder.First; node != null; node = node.Next)
            {
                if (string.Equals(node.Value, cacheKey, StringComparison.OrdinalIgnoreCase))
                {
                    parsedOrder.Remove(node);
                    break;
                }
            }
        }

        /// <summary>Drops every parsed character, keeping the index.</summary>
        public static void ClearParsedCache()
        {
            lock (fullListLoadLock)
            {
                parsedCharacters.Clear();
                parsedOrder.Clear();
            }
        }

        /// <summary>
        /// Whether the last index access found the index already in memory rather than loading it.
        /// </summary>
        /// <remarks>
        /// Closing the GM window and launching again reuses the same process, so the second launch reports
        /// the FIRST launch's load timings unless this is checked - which reads as a slow load that did not
        /// actually happen.
        /// </remarks>
        public static bool LastIndexAccessWasAlreadyResident { get; private set; }

        private static void EnsureIndexLoadedLocked()
        {
            EnsureIndexFilePath();
            if (indexLoaded)
            {
                LastIndexAccessWasAlreadyResident = true;
                return;
            }

            LastIndexAccessWasAlreadyResident = false;

            Stopwatch loadStopwatch = Stopwatch.StartNew();
            if (TryLoadIndexFromJson(indexFile, out List<CharacterIndexEntry> loadedIndex))
            {
                PublishIndexLocked(loadedIndex);
                LastFullListWasCacheHit = true;
                CustomConsole.Info($"Loaded {loadedIndex.Count} characters from the character index.");
            }
            else
            {
                RebuildIndexLocked(null, null, null);
                LastFullListWasCacheHit = false;
            }

            LastFullListLoadMs = loadStopwatch.ElapsedMilliseconds;
            LastFullListLoadCount = characterIndex.Count;
            indexLoaded = true;
        }

        private static void PublishIndexLocked(List<CharacterIndexEntry> entries)
        {
            characterIndex = entries;
            Dictionary<string, CharacterIndexEntry> lookup =
                new Dictionary<string, CharacterIndexEntry>(entries.Count, StringComparer.OrdinalIgnoreCase);
            foreach (CharacterIndexEntry entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Name))
                {
                    lookup.TryAdd(entry.Name, entry);
                }
            }

            indexByName = lookup;
        }

        /// <summary>
        /// Rebuilds the character index by scanning every mounted <c>characters</c> folder.
        /// </summary>
        public static void RefreshCharacterList(
            Action<CharacterFolder>? onParsedCharacter = null,
            Action<string>? onChangedMountPath = null,
            Action<CharacterFolder, int, int>? onParsedCharacterProgress = null)
        {
            lock (fullListLoadLock)
            {
                EnsureIndexFilePath();
                RebuildIndexLocked(onParsedCharacter, onChangedMountPath, onParsedCharacterProgress);
                indexLoaded = true;
            }
        }

        private static void RebuildIndexLocked(
            Action<CharacterFolder>? onParsedCharacter,
            Action<string>? onChangedMountPath,
            Action<CharacterFolder, int, int>? onParsedCharacterProgress)
        {
            List<(string DirectoryPath, string IniFilePath, string FolderName)> candidates =
                new List<(string DirectoryPath, string IniFilePath, string FolderName)>();
            HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string characterFolder in CharacterFolders)
            {
                onChangedMountPath?.Invoke(characterFolder);
                if (!Directory.Exists(characterFolder))
                {
                    continue;
                }

                IEnumerable<string> directories;
                try
                {
                    directories = Directory.EnumerateDirectories(characterFolder);
                }
                catch (Exception ex)
                {
                    CustomConsole.Warning($"Failed to enumerate character folder '{characterFolder}'.");
                    CustomConsole.Error("Character folder enumeration error", ex);
                    continue;
                }

                foreach (string directory in directories)
                {
                    string iniFilePath = Path.Combine(directory, "char.ini");
                    if (!File.Exists(iniFilePath))
                    {
                        continue;
                    }

                    // Mount order decides which duplicate name wins, so the first one seen keeps the name.
                    string folderName = Path.GetFileName(directory);
                    if (!seenNames.Add(folderName))
                    {
                        continue;
                    }

                    candidates.Add((directory, iniFilePath, folderName));
                }
            }

            CharacterIndexEntry?[] entries = new CharacterIndexEntry?[candidates.Count];
            int completed = 0;
            ParallelOptions options = new ParallelOptions
            {
                MaxDegreeOfParallelism = AssetRefreshParallelism.GetDegreeOfParallelism(candidates.Count)
            };

            Parallel.For(0, candidates.Count, options, index =>
            {
                (string directoryPath, string iniFilePath, string folderName) = candidates[index];
                try
                {
                    // The index needs the showname and icon, which means reading char.ini once. The parsed
                    // result is deliberately NOT retained: holding all of them is the memory problem this
                    // whole split exists to remove.
                    CharacterFolder parsedCharacter = Create(iniFilePath);
                    entries[index] = CharacterIndexEntry.FromCharacter(parsedCharacter);

                    int currentCount = Interlocked.Increment(ref completed);
                    onParsedCharacter?.Invoke(parsedCharacter);
                    onParsedCharacterProgress?.Invoke(parsedCharacter, currentCount, candidates.Count);
                }
                catch (Exception ex)
                {
                    CustomConsole.Warning(
                        $"Skipping broken character folder '{directoryPath}' due to parse/validation failure.");
                    CustomConsole.Error("Character parsing error", ex);
                }
            });

            List<CharacterIndexEntry> rebuilt = new List<CharacterIndexEntry>(candidates.Count);
            foreach (CharacterIndexEntry? entry in entries)
            {
                if (entry != null)
                {
                    rebuilt.Add(entry);
                }
            }

            PublishIndexLocked(rebuilt);
            parsedCharacters.Clear();
            parsedOrder.Clear();
            SaveIndexToJson(indexFile, rebuilt);
            CustomConsole.Info($"Character index rebuilt with {rebuilt.Count} characters.");
        }

        public static bool TryUpsertCharacterFolderInCache(
            string targetCharacterDirectoryPath,
            string? previousCharacterDirectoryPath,
            out CharacterFolder? upsertedCharacter,
            out string errorMessage)
        {
            upsertedCharacter = null;
            errorMessage = string.Empty;

            try
            {
                string targetDirectory = NormalizePathForCompare(targetCharacterDirectoryPath);
                if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
                {
                    errorMessage = "Target character directory was not found on disk.";
                    return false;
                }

                string charIniPath = ResolveCharacterIniPath(targetDirectory);
                if (string.IsNullOrWhiteSpace(charIniPath) || !File.Exists(charIniPath))
                {
                    errorMessage = "char.ini was not found in the target character directory.";
                    return false;
                }

                upsertedCharacter = Create(charIniPath);
                CharacterIndexEntry upsertedEntry = CharacterIndexEntry.FromCharacter(upsertedCharacter);
                string normalizedPreviousDirectory = NormalizePathForCompare(previousCharacterDirectoryPath ?? string.Empty);

                lock (fullListLoadLock)
                {
                    EnsureIndexLoadedLocked();

                    // Copy-on-write: Index hands the live list to callers that enumerate it outside this
                    // lock, so the list instance is replaced rather than mutated.
                    List<CharacterIndexEntry> updated = new List<CharacterIndexEntry>(characterIndex.Count + 1);
                    foreach (CharacterIndexEntry existing in characterIndex)
                    {
                        string existingDirectory = NormalizePathForCompare(existing.DirectoryPath);
                        bool isReplaced =
                            string.Equals(existingDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase)
                            || (normalizedPreviousDirectory.Length > 0
                                && string.Equals(existingDirectory, normalizedPreviousDirectory, StringComparison.OrdinalIgnoreCase))
                            || string.Equals(existing.Name, upsertedEntry.Name, StringComparison.OrdinalIgnoreCase);
                        if (isReplaced)
                        {
                            DropParsedLocked(existing.DirectoryPath);
                            continue;
                        }

                        updated.Add(existing);
                    }

                    updated.Add(upsertedEntry);
                    PublishIndexLocked(updated);

                    string cacheKey = NormalizePathForCompare(upsertedCharacter.DirectoryPath);
                    parsedCharacters[cacheKey] = upsertedCharacter;
                    TouchParsedLocked(cacheKey);
                    TrimParsedCacheLocked();
                    SaveIndexToJson(indexFile, updated);
                }

                return true;
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to upsert character folder in cache.", ex);
                errorMessage = ex.Message;
                upsertedCharacter = null;
                return false;
            }
        }

        public static bool TryRemoveCharacterFolderFromCache(
            string? targetCharacterDirectoryPath,
            string? characterName,
            out bool removedAny,
            out string errorMessage)
        {
            removedAny = false;
            errorMessage = string.Empty;

            try
            {
                string normalizedTargetDirectory = NormalizePathForCompare(targetCharacterDirectoryPath ?? string.Empty);
                string normalizedCharacterName = (characterName ?? string.Empty).Trim();

                lock (fullListLoadLock)
                {
                    EnsureIndexLoadedLocked();

                    List<CharacterIndexEntry> remaining = new List<CharacterIndexEntry>(characterIndex.Count);
                    foreach (CharacterIndexEntry existing in characterIndex)
                    {
                        bool isRemoved =
                            (normalizedTargetDirectory.Length > 0
                                && string.Equals(
                                    NormalizePathForCompare(existing.DirectoryPath),
                                    normalizedTargetDirectory,
                                    StringComparison.OrdinalIgnoreCase))
                            || (normalizedCharacterName.Length > 0
                                && string.Equals(existing.Name, normalizedCharacterName, StringComparison.OrdinalIgnoreCase));
                        if (isRemoved)
                        {
                            DropParsedLocked(existing.DirectoryPath);
                            continue;
                        }

                        remaining.Add(existing);
                    }

                    removedAny = remaining.Count != characterIndex.Count;
                    if (removedAny)
                    {
                        PublishIndexLocked(remaining);
                        SaveIndexToJson(indexFile, remaining);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                CustomConsole.Error("Failed to remove character folder from cache.", ex);
                errorMessage = ex.Message;
                removedAny = false;
                return false;
            }
        }

        /// <summary>Short cooldown before a character name that just missed is probed on disk again.</summary>
        private const int OnDemandMissCooldownSeconds = 10;

        private static readonly object onDemandLock = new object();
        private static readonly Dictionary<string, DateTime> onDemandMissUntilUtc =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Resolves a character that is not in the index by probing the mounted folders once, adding it to
        /// the index if it is there now.
        /// </summary>
        /// <remarks>
        /// The index is built from a scan, so a character that appeared after that scan (an archive the
        /// user just extracted, a folder a teammate synced in, an asset the web mirror just materialized)
        /// is invisible until something rescans. The client's live asset watcher covers the normal case,
        /// but it is debounced and it cannot watch every possible location, so the render and packet paths
        /// also self-heal: the first message that names an unknown character probes for it directly.
        ///
        /// A negative result is remembered for <see cref="OnDemandMissCooldownSeconds"/> so a genuinely
        /// missing character (very common - most servers reference characters the user does not have) costs
        /// one probe every ten seconds rather than one per message or per rendered frame.
        /// </remarks>
        public static CharacterFolder? ResolveOnDemand(string? characterName)
        {
            string normalizedName = (characterName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return null;
            }

            lock (onDemandLock)
            {
                if (onDemandMissUntilUtc.TryGetValue(normalizedName, out DateTime missUntilUtc)
                    && DateTime.UtcNow < missUntilUtc)
                {
                    return null;
                }
            }

            string? resolvedDirectory = null;
            try
            {
                foreach (string baseFolder in Globals.BaseFolders ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(baseFolder))
                    {
                        continue;
                    }

                    string candidateDirectory = Path.Combine(baseFolder, "characters", normalizedName);
                    if (File.Exists(Path.Combine(candidateDirectory, "char.ini")))
                    {
                        resolvedDirectory = candidateDirectory;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                CustomConsole.Warning($"On-demand probe for character '{normalizedName}' failed.", ex);
                resolvedDirectory = null;
            }

            if (string.IsNullOrWhiteSpace(resolvedDirectory))
            {
                lock (onDemandLock)
                {
                    onDemandMissUntilUtc[normalizedName] =
                        DateTime.UtcNow.AddSeconds(OnDemandMissCooldownSeconds);
                }

                return null;
            }

            if (!TryUpsertCharacterFolderInCache(
                    resolvedDirectory,
                    previousCharacterDirectoryPath: null,
                    out CharacterFolder? resolvedCharacter,
                    out string errorMessage)
                || resolvedCharacter == null)
            {
                CustomConsole.Warning(
                    $"On-demand resolve found '{normalizedName}' at '{resolvedDirectory}' but could not cache it: {errorMessage}");
                lock (onDemandLock)
                {
                    onDemandMissUntilUtc[normalizedName] =
                        DateTime.UtcNow.AddSeconds(OnDemandMissCooldownSeconds);
                }

                return null;
            }

            lock (onDemandLock)
            {
                onDemandMissUntilUtc.Remove(normalizedName);
            }

            CustomConsole.Info($"Resolved character '{normalizedName}' on demand from '{resolvedDirectory}'.");
            return resolvedCharacter;
        }

        /// <summary>Clears the on-demand negative cache, so every name is probed again on next use.</summary>
        public static void ClearOnDemandMissCache()
        {
            lock (onDemandLock)
            {
                onDemandMissUntilUtc.Clear();
            }
        }

        static JsonSerializerOptions jsonOptions = new JsonSerializerOptions { WriteIndented = false };

        private static void EnsureIndexFilePath()
        {
            string cacheRoot = CacheEnvironment.GetCacheRoot();
            Directory.CreateDirectory(cacheRoot);
            string desiredIndexPath = Path.Combine(cacheRoot, $"characters_index_{BuildCacheKey()}.json");

            if (!indexPathInitialized || !string.Equals(indexFile, desiredIndexPath, StringComparison.OrdinalIgnoreCase))
            {
                indexFile = desiredIndexPath;
                characterIndex = new List<CharacterIndexEntry>();
                indexByName = new Dictionary<string, CharacterIndexEntry>(StringComparer.OrdinalIgnoreCase);
                parsedCharacters.Clear();
                parsedOrder.Clear();
                indexLoaded = false;
                indexPathInitialized = true;
            }

            CacheFilePruner.PruneStaleCacheFiles(cacheRoot, "characters_index_", indexFile);
            // Legacy 26 MB full-character caches from before the index/parse split are pure dead weight.
            CacheFilePruner.PruneStaleCacheFiles(cacheRoot, "characters_", indexFile);
        }

        private static string BuildCacheKey()
        {
            string payload = $"{Globals.PathToConfigINI}|{string.Join("|", Globals.PhysicalBaseFolders)}";
            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private static string ResolveCharacterIniPath(string characterDirectoryPath)
        {
            string rootIni = Path.Combine(characterDirectoryPath, "char.ini");
            if (File.Exists(rootIni))
            {
                return rootIni;
            }

            string[] iniFiles = Directory.GetFiles(characterDirectoryPath, "char.ini", SearchOption.AllDirectories);
            return iniFiles.FirstOrDefault() ?? string.Empty;
        }

        private static string NormalizePathForCompare(string path)
        {
            string value = (path ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private static void SaveIndexToJson(string filePath, List<CharacterIndexEntry> entries)
        {
            try
            {
                CharacterIndexContainer container = new CharacterIndexContainer
                {
                    Version = IndexVersion,
                    ConfigPath = Globals.PathToConfigINI,
                    BaseFolders = new List<string>(Globals.PhysicalBaseFolders),
                    Characters = entries
                };

                File.WriteAllText(filePath, JsonSerializer.Serialize(container, jsonOptions));
            }
            catch (Exception ex)
            {
                CustomConsole.Warning("Character index could not be saved.", ex);
            }
        }

        private static bool TryLoadIndexFromJson(string filePath, out List<CharacterIndexEntry> entries)
        {
            entries = new List<CharacterIndexEntry>();
            LastFullListCacheFileBytes = -1;
            LastFullListFileReadMs = -1;
            LastFullListDeserializeMs = -1;
            LastFullListCompatibilityCheckMs = -1;

            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                try
                {
                    LastFullListCacheFileBytes = new FileInfo(filePath).Length;
                }
                catch
                {
                    LastFullListCacheFileBytes = -1;
                }

                Stopwatch readStopwatch = Stopwatch.StartNew();
                string json = File.ReadAllText(filePath);
                readStopwatch.Stop();
                LastFullListFileReadMs = readStopwatch.ElapsedMilliseconds;

                Stopwatch deserializeStopwatch = Stopwatch.StartNew();
                CharacterIndexContainer? container = JsonSerializer.Deserialize<CharacterIndexContainer>(json);
                deserializeStopwatch.Stop();
                LastFullListDeserializeMs = deserializeStopwatch.ElapsedMilliseconds;
                if (container == null)
                {
                    return false;
                }

                Stopwatch compatibilityStopwatch = Stopwatch.StartNew();
                bool compatible = IsIndexCompatible(container);
                compatibilityStopwatch.Stop();
                LastFullListCompatibilityCheckMs = compatibilityStopwatch.ElapsedMilliseconds;
                if (!compatible)
                {
                    return false;
                }

                entries = container.Characters ?? new List<CharacterIndexEntry>();
                return true;
            }
            catch (Exception ex)
            {
                CustomConsole.Warning("Character index could not be loaded; rebuilding from disk.");
                CustomConsole.Error("Index load error", ex);
                return false;
            }
        }

        private static bool IsIndexCompatible(CharacterIndexContainer container)
        {
            if (container.Version != IndexVersion)
            {
                return false;
            }

            if (!string.Equals(container.ConfigPath ?? string.Empty, Globals.PathToConfigINI ?? string.Empty,
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Web mirror mounts are excluded on purpose: they come and go with the server connection
            // and must never invalidate this index (see Globals.PhysicalBaseFolders).
            List<string> cachedBaseFolders = container.BaseFolders ?? new List<string>();
            List<string> currentBaseFolders = Globals.PhysicalBaseFolders;
            if (cachedBaseFolders.Count != currentBaseFolders.Count)
            {
                return false;
            }

            for (int i = 0; i < cachedBaseFolders.Count; i++)
            {
                if (!string.Equals(cachedBaseFolders[i], currentBaseFolders[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // Per-character edits/adds/removes are NOT validated here. They are detected off the launch
            // critical path by the live asset watcher and the post-launch change check, and anything those
            // miss still self-heals through ResolveOnDemand.
            return true;
        }
        private static string ResolveCharacterIconPath(string directoryPath, CharacterConfigINI? parsedConfig = null)
        {
            string explicitCharacterIcon = FindFirstExistingFile(directoryPath, "char_icon");
            if (!string.IsNullOrWhiteSpace(explicitCharacterIcon))
            {
                return explicitCharacterIcon;
            }

            if (parsedConfig != null)
            {
                for (int i = 1; i <= parsedConfig.EmotionsCount; i++)
                {
                    if (!parsedConfig.Emotions.TryGetValue(i, out Emote? emote))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(emote.PathToImage_off))
                    {
                        return emote.PathToImage_off;
                    }

                    if (!string.IsNullOrWhiteSpace(emote.PathToImage_on))
                    {
                        return emote.PathToImage_on;
                    }
                }

                foreach (Emote emote in parsedConfig.Emotions.Values)
                {
                    if (!string.IsNullOrWhiteSpace(emote.PathToImage_off))
                    {
                        return emote.PathToImage_off;
                    }

                    if (!string.IsNullOrWhiteSpace(emote.PathToImage_on))
                    {
                        return emote.PathToImage_on;
                    }
                }
            }

            return string.Empty;
        }

        private static string FindFirstExistingFile(string directoryPath, string baseFileName)
        {
            foreach (string extension in Globals.AllowedImageExtensions)
            {
                string curPath = Path.Combine(directoryPath, baseFileName + "." + extension);
                if (File.Exists(curPath))
                {
                    return curPath;
                }
            }

            return string.Empty;
        }
        #endregion



        public string Name { get; set; } = string.Empty;
        public string DirectoryPath { get; set; } = string.Empty;
        public string PathToConfigIni { get; set; } = string.Empty;
        public string CharIconPath { get; set; } = string.Empty;
        public string ViewportIdleSpritePath { get; set; } = string.Empty;
        public string SoundListPath { get; set; } = string.Empty;

        public CharacterConfigINI configINI { get; set; } = new CharacterConfigINI(string.Empty);

        public void Update(string configINIPath, bool updateConfigINI)
        {
            UpdatePaths(configINIPath);

            if(updateConfigINI)
            {
                configINI.PathToConfigINI = configINIPath;
                configINI.Update();
            }
        }
        private void UpdatePaths(string configINIPath)
        {
            DirectoryPath = Path.GetDirectoryName(configINIPath) ?? string.Empty;
            CharIconPath = ResolveCharacterIconPath(DirectoryPath);
            ViewportIdleSpritePath = string.Empty;

            SoundListPath = Path.Combine(DirectoryPath, "soundlist.ini");
            PathToConfigIni = configINIPath;

            Name = Path.GetFileName(DirectoryPath) ?? string.Empty;
        }
        public static CharacterFolder Create(string configINIPath)
        {
            var folder = new CharacterFolder();
            folder.UpdatePaths(configINIPath);

            folder.configINI = new CharacterConfigINI(configINIPath);
            folder.configINI.Update();
            folder.CharIconPath = ResolveCharacterIconPath(folder.DirectoryPath, folder.configINI);
            folder.ViewportIdleSpritePath = ResolveViewportIdleSpritePath(folder.DirectoryPath, folder.configINI);

            return folder;
        }

        private static string ResolveViewportIdleSpritePath(string directoryPath, CharacterConfigINI? parsedConfig)
        {
            if (parsedConfig == null)
            {
                return string.Empty;
            }

            for (int i = 1; i <= parsedConfig.EmotionsCount; i++)
            {
                if (!parsedConfig.Emotions.TryGetValue(i, out Emote? emote))
                {
                    continue;
                }

                string resolved = CharacterAssetPathResolver.ResolveIdleSpritePath(directoryPath, emote.Animation);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            foreach (var pair in parsedConfig.Emotions.OrderBy(x => x.Key))
            {
                string resolved = CharacterAssetPathResolver.ResolveIdleSpritePath(directoryPath, pair.Value.Animation);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }

            return string.Empty;
        }
    }

    [Serializable]
    public class CharacterConfigINI
    {
        public CharacterConfigINI(string pathToConfigINI)
        {
            PathToConfigINI = pathToConfigINI;
        }

        public string PathToConfigINI { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ShowName { get; set; } = string.Empty;
        public string NeedsShowName { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Blips { get; set; } = string.Empty;
        public string EffectsFolder { get; set; } = string.Empty;
        public string Realization { get; set; } = string.Empty;
        public int PreAnimationTime { get; set; }
        public int EmotionsCount { get; set; }
        public Dictionary<int, Emote> Emotions { get; set; } = new();
        public Dictionary<int, int> ShowNameOverrideIndicesByEmoteId { get; set; } = new();
        public Dictionary<int, string> ShowNameOverridesByIndex { get; set; } = new();

        public void Update()
        {
            string configINIPath = PathToConfigINI;
            EmotionsCount = 0;
            Emotions.Clear();
            ShowNameOverrideIndicesByEmoteId.Clear();
            ShowNameOverridesByIndex.Clear();
            int maxEmotionEntryId = 0;

            #region Config Parsing
            IniDocument document = IniDocument.Load(configINIPath);

            Name = document.GetLatestValueOrDefault("Options", "name");
            ShowName = document.GetLatestValueOrDefault("Options", "showname");
            NeedsShowName = document.GetLatestValueOrDefault("Options", "needs_showname");
            Gender = document.GetLatestValueOrDefault("Options", "gender");
            Side = document.GetLatestValueOrDefault("Options", "side");
            Category = document.GetLatestValueOrDefault("Options", "category");
            Blips = document.GetLatestValueOrDefault("Options", "blips");
            EffectsFolder = document.GetLatestValueOrDefault("Options", "effects");
            Realization = document.GetLatestValueOrDefault("Options", "realization");

            if (int.TryParse(document.GetLatestValueOrDefault("Time", "preanim"), out int preanimTime))
            {
                PreAnimationTime = preanimTime;
            }

            if (int.TryParse(document.GetLatestValueOrDefault("Emotions", "number"), out int emotionCount))
            {
                EmotionsCount = emotionCount;
            }

            foreach (IniEntry entry in document.GetEntries("Emotions"))
            {
                if (string.Equals(entry.Key, "number", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!int.TryParse(entry.Key, out int emotionId))
                {
                    continue;
                }

                maxEmotionEntryId = Math.Max(maxEmotionEntryId, emotionId);
                if (!Emotions.ContainsKey(emotionId))
                {
                    Emotions[emotionId] = new Emote(emotionId);
                }

                Emotions[emotionId] = Emote.ParseEmoteLine(entry.Value);
                Emotions[emotionId].ID = emotionId;
            }

            foreach (IniEntry entry in document.GetEntries("SoundN"))
            {
                if (!int.TryParse(entry.Key, out int soundId))
                {
                    continue;
                }

                if (!Emotions.ContainsKey(soundId))
                {
                    Emotions[soundId] = new Emote(soundId);
                }

                Emotions[soundId].sfxName = string.IsNullOrEmpty(entry.Value) ? "1" : entry.Value;
            }

            foreach (IniEntry entry in document.GetEntries("SoundT"))
            {
                if (!int.TryParse(entry.Key, out int soundTimeId)
                    || !int.TryParse(entry.Value, out int timeValue))
                {
                    continue;
                }

                if (!Emotions.ContainsKey(soundTimeId))
                {
                    Emotions[soundTimeId] = new Emote(soundTimeId);
                }

                Emotions[soundTimeId].sfxDelay = timeValue;
            }

            foreach (IniEntry entry in document.GetEntries("SoundL"))
            {
                if (!int.TryParse(entry.Key, out int soundLoopId))
                {
                    continue;
                }

                if (!Emotions.ContainsKey(soundLoopId))
                {
                    Emotions[soundLoopId] = new Emote(soundLoopId);
                }

                Emotions[soundLoopId].sfxLooping = string.IsNullOrWhiteSpace(entry.Value) ? "0" : entry.Value;
            }

            foreach (IniEntry entry in document.GetEntries("OptionsN"))
            {
                if (int.TryParse(entry.Key, out int emoteId) && int.TryParse(entry.Value, out int overrideIndex))
                {
                    ShowNameOverrideIndicesByEmoteId[emoteId] = overrideIndex;
                }
            }

            foreach (KeyValuePair<string, List<IniEntry>> section in document.Sections)
            {
                const string prefix = "Options";
                if (!section.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || !int.TryParse(section.Key[prefix.Length..], out int overrideIndex)
                    || overrideIndex <= 0)
                {
                    continue;
                }

                string overrideShowName = document.GetLatestValueOrDefault(section.Key, "showname");
                if (!string.IsNullOrWhiteSpace(overrideShowName))
                {
                    ShowNameOverridesByIndex[overrideIndex] = overrideShowName;
                }
            }
            #endregion

            #region Gather Button Paths
            string iniDirectory = Path.GetDirectoryName(PathToConfigINI) ?? string.Empty;
            string buttonPath = Path.Combine(iniDirectory, "Emotions");

            foreach (var item in Emotions)
            {
                int id = item.Key;

                foreach (string extension in Globals.AllowedImageExtensions)
                {
                    string currentButtonPath_off = Path.Combine(buttonPath, $"button{id}_off." + extension);
                    if (File.Exists(currentButtonPath_off) && string.IsNullOrEmpty(item.Value.PathToImage_off))
                    {
                        item.Value.PathToImage_off = currentButtonPath_off;
                    }

                    string currentButtonPath_on = Path.Combine(buttonPath, $"button{id}_on." + extension);
                    if (File.Exists(currentButtonPath_on) && string.IsNullOrEmpty(item.Value.PathToImage_on))
                    {
                        item.Value.PathToImage_on = currentButtonPath_on;
                    }

                    if (!string.IsNullOrEmpty(item.Value.PathToImage_off) &&
                        !string.IsNullOrEmpty(item.Value.PathToImage_on))
                    {
                        break;
                    }
                }
            }
            #endregion

            #region Correct emotes based on config file
            if (EmotionsCount <= 0 && maxEmotionEntryId > 0)
            {
                EmotionsCount = maxEmotionEntryId;
                CustomConsole.Warning(
                    $"Character INI '{configINIPath}' has missing/invalid [Emotions] number. " +
                    $"Inferred emotion count as {EmotionsCount} from highest emotion entry.");
            }

            if (EmotionsCount != Emotions.Count)
            {
                for (int i = 1; i <= EmotionsCount; i++)
                {
                    if (!Emotions.ContainsKey(i))
                    {
                        //add an empty emote, since this is how the AO client works.
                        Emotions.Add(i, new Emote(i));
                    }
                }

                Emotions = Emotions
                    .OrderBy(x => x.Key)
                    .Take(EmotionsCount)
                    .ToDictionary(x => x.Key, x => x.Value);
            }
            #endregion
        }

        public string ResolveShowNameForEmote(int emoteId)
        {
            string resolved = ShowName;
            if (ShowNameOverrideIndicesByEmoteId.TryGetValue(emoteId, out int overrideIndex)
                && ShowNameOverridesByIndex.TryGetValue(overrideIndex, out string overrideShowName)
                && !string.IsNullOrWhiteSpace(overrideShowName))
            {
                resolved = overrideShowName;
            }

            if ((NeedsShowName ?? string.Empty).StartsWith("false", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(resolved)
                ? Path.GetFileName(Path.GetDirectoryName(PathToConfigINI) ?? string.Empty) ?? string.Empty
                : resolved;
        }
    }

    internal sealed class IniDocument
    {
        public Dictionary<string, List<IniEntry>> Sections { get; } =
            new Dictionary<string, List<IniEntry>>(StringComparer.OrdinalIgnoreCase);

        public static IniDocument Load(string path)
        {
            IniDocument document = new IniDocument();
            string currentSection = string.Empty;
            foreach (string rawLine in File.ReadLines(path))
            {
                string line = (rawLine ?? string.Empty).Trim().TrimStart('\uFEFF');
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    currentSection = line[1..^1].Trim();
                    _ = document.GetEntries(currentSection);
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string key = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();
                document.GetEntries(currentSection).Add(new IniEntry(key, value));
            }

            return document;
        }

        public List<IniEntry> GetEntries(string sectionName)
        {
            string key = (sectionName ?? string.Empty).Trim();
            if (!Sections.TryGetValue(key, out List<IniEntry>? entries))
            {
                entries = new List<IniEntry>();
                Sections[key] = entries;
            }

            return entries;
        }

        public string GetLatestValueOrDefault(string sectionName, string key)
        {
            List<IniEntry> entries = GetEntries(sectionName);
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (string.Equals(entries[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return entries[i].Value;
                }
            }

            return string.Empty;
        }
    }

    internal readonly struct IniEntry
    {
        public IniEntry(string key, string value)
        {
            Key = key ?? string.Empty;
            Value = value ?? string.Empty;
        }

        public string Key { get; }

        public string Value { get; }
    }

    /// <summary>
    /// The cheap half of a character: everything lookups and lists need, without parsing char.ini.
    /// </summary>
    /// <remarks>
    /// Name, paths, showname and icon answer essentially every question the client asks about a character
    /// it is not currently using - does it exist, what goes in the dropdown, which character has this
    /// showname, what icon do I draw. Keeping those separate from the parsed emote data is what lets the
    /// index stay resident (a few hundred KB for thousands of characters) while the expensive part is
    /// parsed on demand and evicted.
    /// </remarks>
    [Serializable]
    public sealed class CharacterIndexEntry
    {
        /// <summary>Character folder name, which is also the AO2 character id.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Absolute path of the character folder.</summary>
        public string DirectoryPath { get; set; } = string.Empty;

        /// <summary>Absolute path of the character's <c>char.ini</c>.</summary>
        public string PathToConfigIni { get; set; } = string.Empty;

        /// <summary>AO2 <c>[Options] showname</c>, kept here so reverse lookups need no parse.</summary>
        public string ShowName { get; set; } = string.Empty;

        /// <summary>AO2 <c>[Options] name</c>, which servers sometimes use instead of the folder name.</summary>
        public string OptionsName { get; set; } = string.Empty;

        /// <summary>AO2 <c>[Options] category</c>, used to group characters in pickers.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Resolved character icon path, or empty.</summary>
        public string CharIconPath { get; set; } = string.Empty;

        /// <summary>Resolved idle sprite path, or empty. Kept so previews need no parse.</summary>
        public string ViewportIdleSpritePath { get; set; } = string.Empty;

        public static CharacterIndexEntry FromCharacter(CharacterFolder character)
        {
            return new CharacterIndexEntry
            {
                Name = character.Name,
                DirectoryPath = character.DirectoryPath,
                PathToConfigIni = character.PathToConfigIni,
                ShowName = character.configINI?.ShowName ?? string.Empty,
                OptionsName = character.configINI?.Name ?? string.Empty,
                Category = character.configINI?.Category ?? string.Empty,
                CharIconPath = character.CharIconPath,
                ViewportIdleSpritePath = character.ViewportIdleSpritePath
            };
        }
    }

    internal sealed class CharacterIndexContainer
    {
        public int Version { get; set; }
        public string ConfigPath { get; set; } = string.Empty;
        public List<string> BaseFolders { get; set; } = new List<string>();
        public List<CharacterIndexEntry> Characters { get; set; } = new List<CharacterIndexEntry>();
    }
}
