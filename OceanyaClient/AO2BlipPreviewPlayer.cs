using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ManagedBass;
using AOBot_Testing.Structures;

namespace OceanyaClient
{
    /// <summary>
    /// Minimal AO2-style blip player using BASS + BASS_OPUS with a configurable stream cycle.
    /// </summary>
    public sealed class AO2BlipPreviewPlayer : IDisposable
    {
        private static bool bassInitialized;
        private static bool bassUnavailable;
        private static bool bassOpusPluginLoaded;
        private static string bassPluginLoadError = string.Empty;

        private readonly int streamCount;
        private readonly int[] streams;
        private int cycleIndex;
        private bool disposed;
        private float volume = 1.0f;
        private SyncProcedure? activeLoopSyncProcedure;

        public string LastErrorMessage { get; private set; } = string.Empty;

        public float Volume
        {
            get => volume;
            set
            {
                volume = Math.Max(0.0f, value);
                ApplyVolumeToStreams();
            }
        }

        public AO2BlipPreviewPlayer(int streamCount = 5)
        {
            this.streamCount = Math.Max(1, streamCount);
            streams = new int[this.streamCount];
            EnsureBassInitialized();
        }

        public bool TrySetBlip(string fullPath, bool loop = false)
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

            // AO2 parity: HTTP/HTTPS/FTP tokens are streamed directly from the URL
            // using BASS_StreamCreateURL instead of loading a local file.
            bool isUrl = fullPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase);

            if (isUrl)
            {
                FreeStreams();
                // URL streaming (BASS_StreamCreateURL). Loop is not supported for URL streams.
                int streamHandle = Bass.CreateStream(fullPath, 0, BassFlags.Default, null, IntPtr.Zero);
                streams[0] = streamHandle;
                if (streamHandle != 0)
                {
                    _ = Bass.ChannelSetAttribute(streamHandle, ChannelAttribute.Volume, volume);
                    LastErrorMessage = string.Empty;
                    return true;
                }

                LastErrorMessage = $"Could not create URL stream for '{fullPath}'. BASS error: {Bass.LastError}.";
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

            BassFlags flags = BassFlags.Unicode | BassFlags.AsyncFile;
            if (loop)
            {
                flags |= BassFlags.Loop;
            }

            for (int i = 0; i < streamCount; i++)
            {
                int streamHandle = Bass.CreateStream(fullPath, 0, 0, flags);
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
            bool played = Bass.ChannelPlay(stream, false);
            cycleIndex = (cycleIndex + 1) % streamCount;
            return played;
        }

        public void Stop()
        {
            for (int i = 0; i < streamCount; i++)
            {
                if (streams[i] == 0)
                {
                    continue;
                }

                _ = Bass.ChannelStop(streams[i]);
            }
        }

        /// <summary>
        /// Detaches and returns the first active stream handle, removing it from this player's management.
        /// The caller takes full ownership and must eventually free it via <c>Bass.StreamFree</c>.
        /// Returns 0 if no stream is loaded.
        /// </summary>
        internal int TakeFirstActiveStream()
        {
            for (int i = 0; i < streamCount; i++)
            {
                if (streams[i] == 0)
                {
                    continue;
                }

                int handle = streams[i];
                streams[i] = 0;
                return handle;
            }

            return 0;
        }

        /// <summary>
        /// Returns the byte position of the first loaded stream, or 0 when no stream is loaded.
        /// </summary>
        internal long GetFirstStreamPosition()
        {
            int stream = streams.FirstOrDefault(handle => handle != 0);
            return stream == 0 ? 0 : Bass.ChannelGetPosition(stream);
        }

        /// <summary>
        /// Applies <paramref name="positionBytes"/> to all loaded streams that can accept it.
        /// </summary>
        internal void SetStreamPosition(long positionBytes)
        {
            if (positionBytes <= 0)
            {
                return;
            }

            for (int i = 0; i < streamCount; i++)
            {
                if (streams[i] == 0)
                {
                    continue;
                }

                _ = Bass.ChannelSetPosition(streams[i], positionBytes);
            }
        }

        /// <summary>
        /// Starts playback at volume 0 then slides to <paramref name="targetVolume"/> over
        /// <paramref name="durationMs"/> milliseconds — equivalent to AO2's FADE_IN effect.
        /// </summary>
        internal void FadeInPlay(float targetVolume, int durationMs)
        {
            for (int i = 0; i < streamCount; i++)
            {
                if (streams[i] == 0)
                {
                    continue;
                }

                _ = Bass.ChannelSetAttribute(streams[i], ChannelAttribute.Volume, 0f);
            }

            volume = 0f;
            _ = PlayBlip();

            for (int i = 0; i < streamCount; i++)
            {
                if (streams[i] == 0)
                {
                    continue;
                }

                _ = Bass.ChannelSlideAttribute(streams[i], ChannelAttribute.Volume, targetVolume, durationMs);
            }

            volume = targetVolume;
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
            for (int i = 0; i < streamCount; i++)
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

        /// <summary>
        /// Registers a BASS position sync that seeks to <paramref name="loopStartBytes"/> whenever
        /// playback reaches <paramref name="loopEndBytes"/>, enabling gapless music loop regions.
        /// Call after <see cref="TrySetBlip"/> while streams are loaded.
        /// AO2 parity: BASS_SYNC_POS | BASS_SYNC_MIXTIME at loop_end, callback seeks to loop_start.
        /// </summary>
        internal void ApplyLoopRegion(long loopStartBytes, long loopEndBytes)
        {
            if (loopStartBytes < 0 || loopEndBytes <= loopStartBytes)
            {
                return;
            }

            long capturedStart = loopStartBytes;
            activeLoopSyncProcedure = (handle, channel, data, user) =>
                Bass.ChannelSetPosition(channel, capturedStart);

            for (int i = 0; i < streamCount; i++)
            {
                if (streams[i] == 0)
                {
                    continue;
                }

                Bass.ChannelSetSync(streams[i], SyncFlags.Position | SyncFlags.Mixtime,
                    loopEndBytes, activeLoopSyncProcedure, IntPtr.Zero);
            }
        }

        /// <summary>
        /// Converts a loop region value to a BASS byte offset.
        /// When <paramref name="isSeconds"/> is true, uses the first loaded stream for the conversion.
        /// When false, uses AO2's fixed formula: sample frames × 4 (stereo 16-bit).
        /// Returns 0 if conversion fails or no stream is loaded.
        /// </summary>
        internal long ConvertLoopValueToBytes(bool isSeconds, double value)
        {
            if (value <= 0)
            {
                return 0;
            }

            if (!isSeconds)
            {
                // AO2 parity: sample frames for stereo 16-bit PCM = frames * 2 channels * 2 bytes
                return (long)(value * 4);
            }

            int stream = streams.FirstOrDefault(s => s != 0);
            if (stream == 0)
            {
                return 0;
            }

            long bytes = Bass.ChannelSeconds2Bytes(stream, value);
            return bytes < 0 ? 0 : bytes;
        }

        private void FreeStreams()
        {
            for (int i = 0; i < streamCount; i++)
            {
                if (streams[i] == 0)
                {
                    continue;
                }

                _ = Bass.StreamFree(streams[i]);
                streams[i] = 0;
            }

            activeLoopSyncProcedure = null;
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
