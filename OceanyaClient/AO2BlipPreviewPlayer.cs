using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ManagedBass;
using AOBot_Testing.Structures;

namespace OceanyaClient
{
    /// <summary>
    /// Minimal AO2-style blip player using BASS + BASS_OPUS with a 5-stream cycle.
    /// </summary>
    public sealed class AO2BlipPreviewPlayer : IDisposable
    {
        private const int StreamCount = 5;
        private static bool bassInitialized;
        private static bool bassUnavailable;
        private static bool bassOpusPluginLoaded;
        private static string bassPluginLoadError = string.Empty;

        private readonly int[] streams = new int[StreamCount];
        private int cycleIndex;
        private bool disposed;
        private float volume = 1.0f;

        public string LastErrorMessage { get; private set; } = string.Empty;

        public float Volume
        {
            get => volume;
            set
            {
                volume = Math.Clamp(value, 0.0f, 1.0f);
                ApplyVolumeToStreams();
            }
        }

        public AO2BlipPreviewPlayer()
        {
            EnsureBassInitialized();
        }

        public bool TrySetBlip(string fullPath)
        {
            EnsureBassInitialized();

            if (string.IsNullOrWhiteSpace(fullPath))
            {
                LastErrorMessage = "No blip file path provided.";
                return false;
            }

            if (!bassInitialized)
            {
                LastErrorMessage = "Audio engine failed to initialize.";
                return false;
            }

            FreeStreams();

            bool anyCreated = false;
            bool isOpus = fullPath.EndsWith(".opus", StringComparison.OrdinalIgnoreCase);
            if (isOpus && !bassOpusPluginLoaded)
            {
                LastErrorMessage = string.IsNullOrWhiteSpace(bassPluginLoadError)
                    ? "bassopus.dll plugin could not be loaded."
                    : bassPluginLoadError;
                return false;
            }

            for (int i = 0; i < StreamCount; i++)
            {
                int streamHandle = Bass.CreateStream(fullPath, 0, 0, BassFlags.Unicode | BassFlags.AsyncFile);
                streams[i] = streamHandle;
                if (streamHandle != 0)
                {
                    _ = Bass.ChannelSetAttribute(streamHandle, ChannelAttribute.Volume, volume);
                    anyCreated = true;
                }
            }

            if (!anyCreated)
            {
                LastErrorMessage = $"No stream could be created for '{fullPath}'. BASS error: {Bass.LastError}.";
                return false;
            }

            LastErrorMessage = string.Empty;
            return true;
        }

        public bool PlayBlip()
        {
            int stream = streams[cycleIndex];
            if (stream == 0)
            {
                return false;
            }

            _ = Bass.ChannelSetDevice(stream, Bass.CurrentDevice);
            bool played = Bass.ChannelPlay(stream, true);
            cycleIndex = (cycleIndex + 1) % StreamCount;
            return played;
        }

        public void Stop()
        {
            for (int i = 0; i < StreamCount; i++)
            {
                if (streams[i] == 0)
                {
                    continue;
                }

                _ = Bass.ChannelStop(streams[i]);
            }
        }

        public double GetLoadedDurationMs()
        {
            int stream = streams.FirstOrDefault(handle => handle != 0);
            if (stream == 0)
            {
                return 0;
            }

            long lengthBytes = Bass.ChannelGetLength(stream);
            if (lengthBytes <= 0)
            {
                return 0;
            }

            double seconds = Bass.ChannelBytes2Seconds(stream, lengthBytes);
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
            {
                return 0;
            }

            return seconds * 1000.0;
        }

        private void ApplyVolumeToStreams()
        {
            for (int i = 0; i < StreamCount; i++)
            {
                if (streams[i] == 0)
                {
                    continue;
                }

                _ = Bass.ChannelSetAttribute(streams[i], ChannelAttribute.Volume, volume);
            }
        }

        private static void EnsureBassInitialized()
        {
            if (bassInitialized)
            {
                return;
            }

            if (bassUnavailable)
            {
                return;
            }

            try
            {
                EnsureNativeBassLibrariesPresent();
                bassInitialized = Bass.Init();
                if (bassInitialized)
                {
                    string pluginPath = Path.Combine(AppContext.BaseDirectory, "bassopus.dll");
                    int pluginHandle = File.Exists(pluginPath)
                        ? Bass.PluginLoad(pluginPath)
                        : Bass.PluginLoad("bassopus.dll");
                    Errors pluginError = Bass.LastError;
                    bassOpusPluginLoaded = pluginHandle != 0 || pluginError == Errors.Already;
                    if (!bassOpusPluginLoaded && File.Exists(pluginPath))
                    {
                        pluginHandle = Bass.PluginLoad("bassopus.dll");
                        pluginError = Bass.LastError;
                        bassOpusPluginLoaded = pluginHandle != 0 || pluginError == Errors.Already;
                    }
                    bassPluginLoadError = bassOpusPluginLoaded
                        ? string.Empty
                        : $"bassopus.dll plugin could not be loaded. BASS error: {pluginError}. Path tried: {pluginPath}";
                }
            }
            catch (DllNotFoundException)
            {
                bassUnavailable = true;
                bassInitialized = false;
            }
            catch (BadImageFormatException)
            {
                bassUnavailable = true;
                bassInitialized = false;
            }
            catch (FileNotFoundException)
            {
                bassUnavailable = true;
                bassInitialized = false;
            }
            catch (FileLoadException)
            {
                bassUnavailable = true;
                bassInitialized = false;
            }
        }

        private static void EnsureNativeBassLibrariesPresent()
        {
            string appBase = AppContext.BaseDirectory;
            if (HasNativeBassPair(appBase))
            {
                return;
            }

            foreach (string candidateDir in EnumerateBassCandidateDirectories())
            {
                if (!HasNativeBassPair(candidateDir))
                {
                    continue;
                }

                TryCopyIfMissing(Path.Combine(candidateDir, "bass.dll"), Path.Combine(appBase, "bass.dll"));
                TryCopyIfMissing(Path.Combine(candidateDir, "bassopus.dll"), Path.Combine(appBase, "bassopus.dll"));
                if (HasNativeBassPair(appBase))
                {
                    return;
                }
            }
        }

        private static IEnumerable<string> EnumerateBassCandidateDirectories()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddCandidate(string? path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(path);
                }
                catch
                {
                    return;
                }

                if (!Directory.Exists(fullPath) || !seen.Add(fullPath))
                {
                    return;
                }

                // Also check one level up because AO installs frequently place BASS in the install root.
                string? parent = Directory.GetParent(fullPath)?.FullName;
                if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                {
                    _ = seen.Add(parent);
                }
            }

            AddCandidate(AppContext.BaseDirectory);
            AddCandidate(Directory.GetCurrentDirectory());
            AddCandidate(Path.GetDirectoryName(Globals.PathToConfigINI ?? string.Empty));

            foreach (string baseFolder in Globals.BaseFolders ?? Enumerable.Empty<string>())
            {
                AddCandidate(baseFolder);
            }

            return seen;
        }

        private static bool HasNativeBassPair(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            return File.Exists(Path.Combine(directory, "bass.dll"))
                && File.Exists(Path.Combine(directory, "bassopus.dll"));
        }

        private static void TryCopyIfMissing(string source, string destination)
        {
            try
            {
                if (!File.Exists(source) || File.Exists(destination))
                {
                    return;
                }

                File.Copy(source, destination, overwrite: false);
            }
            catch
            {
                // Ignore failed copy attempts and continue searching other candidate directories.
            }
        }

        private void FreeStreams()
        {
            for (int i = 0; i < StreamCount; i++)
            {
                if (streams[i] == 0)
                {
                    continue;
                }

                _ = Bass.StreamFree(streams[i]);
                streams[i] = 0;
            }

            cycleIndex = 0;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            FreeStreams();
        }
    }
}
