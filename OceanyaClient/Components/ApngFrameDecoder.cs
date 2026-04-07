using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OceanyaClient
{
    /// <summary>
    /// Decodes APNG (Animated PNG) files into composited frames without any external
    /// animation library. Parses PNG chunk structure directly to read acTL/fcTL/fdAT
    /// chunks, then reconstructs each frame as a standalone PNG decoded by WPF's native
    /// BitmapDecoder, preserving full alpha transparency.
    /// </summary>
    internal static class ApngFrameDecoder
    {
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        private static readonly uint[] Crc32Table = BuildCrc32Table();

        private readonly struct PngChunk
        {
            public readonly string Type;
            public readonly byte[] Data;

            public PngChunk(string type, byte[] data)
            {
                Type = type;
                Data = data;
            }
        }

        private readonly struct FrameControl
        {
            public readonly uint Width;
            public readonly uint Height;
            public readonly uint XOffset;
            public readonly uint YOffset;
            public readonly TimeSpan Delay;
            public readonly byte DisposeOp;  // 0=none, 1=background, 2=previous
            public readonly byte BlendOp;    // 0=source, 1=over

            public FrameControl(byte[] data)
            {
                // data[0..3] = sequence_number (skip)
                Width    = ReadBE32(data, 4);
                Height   = ReadBE32(data, 8);
                XOffset  = ReadBE32(data, 12);
                YOffset  = ReadBE32(data, 16);
                ushort delayNum = ReadBE16(data, 20);
                ushort delayDen = ReadBE16(data, 22);
                DisposeOp = data[24];
                BlendOp   = data[25];

                int den = delayDen == 0 ? 100 : delayDen;
                double ms = delayNum * 1000.0 / den;
                if (ms <= 0) ms = 100;
                Delay = TimeSpan.FromMilliseconds(ms);
            }
        }

        /// <summary>
        /// Attempts to decode an APNG file into composited full-canvas frames.
        /// Returns false if the file is not a valid multi-frame APNG.
        /// </summary>
        public static bool TryDecode(
            string path,
            out List<BitmapSource>? frames,
            out List<TimeSpan>? durations)
        {
            frames = null;
            durations = null;

            try
            {
                byte[] fileData = File.ReadAllBytes(path);
                if (!HasPngSignature(fileData))
                {
                    return false;
                }

                List<PngChunk> chunks = ParseChunks(fileData);
                return TryDecodeChunks(chunks, out frames, out durations);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDecodeChunks(
            List<PngChunk> chunks,
            out List<BitmapSource>? frames,
            out List<TimeSpan>? durations)
        {
            frames = null;
            durations = null;

            // Require acTL chunk
            PngChunk actl = default;
            bool hasActl = false;
            foreach (PngChunk c in chunks)
            {
                if (c.Type == "acTL") { actl = c; hasActl = true; break; }
            }
            if (!hasActl || actl.Data.Length < 8)
            {
                return false;
            }

            uint numFrames = ReadBE32(actl.Data, 0);
            if (numFrames <= 1)
            {
                return false;
            }

            // Require IHDR
            PngChunk ihdr = default;
            bool hasIhdr = false;
            foreach (PngChunk c in chunks)
            {
                if (c.Type == "IHDR") { hasIhdr = true; ihdr = c; break; }
            }
            if (!hasIhdr || ihdr.Data.Length < 13)
            {
                return false;
            }

            uint canvasWidth  = ReadBE32(ihdr.Data, 0);
            uint canvasHeight = ReadBE32(ihdr.Data, 4);

            // Collect ancillary chunks to embed in each synthetic frame PNG
            // (e.g. tRNS for palette transparency, pHYs, cHRM, sRGB)
            List<PngChunk> ancillary = new List<PngChunk>();
            foreach (PngChunk c in chunks)
            {
                string t = c.Type;
                if (t != "IHDR" && t != "IDAT" && t != "IEND" &&
                    t != "acTL" && t != "fcTL" && t != "fdAT")
                {
                    ancillary.Add(c);
                }
            }

            // Determine whether the first animation frame uses IDAT data.
            // Per APNG spec: if the first fcTL appears before the first IDAT,
            // then IDAT is the pixel data for animation frame 0.
            bool frame0UsesIdat = false;
            foreach (PngChunk c in chunks)
            {
                if (c.Type == "fcTL") { frame0UsesIdat = true; break; }
                if (c.Type == "IDAT") { break; }
            }

            // Walk chunks to build frame groups: each group is (fcTL, image_data_chunks)
            var animFrames = new List<(FrameControl fctl, List<byte[]> imageData)>();
            FrameControl currentFctl = default;
            List<byte[]> currentData = new List<byte[]>();
            bool collecting = false;
            int framesEmitted = 0;

            foreach (PngChunk c in chunks)
            {
                if (c.Type == "fcTL")
                {
                    if (collecting && currentData.Count > 0)
                    {
                        animFrames.Add((currentFctl, currentData));
                        framesEmitted++;
                        currentData = new List<byte[]>();
                    }
                    currentFctl = new FrameControl(c.Data);
                    collecting = true;
                }
                else if (c.Type == "IDAT")
                {
                    // Only consume IDAT for frame 0 when it is the animation frame
                    if (collecting && framesEmitted == 0 && frame0UsesIdat)
                    {
                        currentData.Add(c.Data);
                    }
                }
                else if (c.Type == "fdAT" && collecting)
                {
                    if (c.Data.Length > 4)
                    {
                        // fdAT prefixes data with a 4-byte sequence number — strip it
                        byte[] idatPayload = new byte[c.Data.Length - 4];
                        Buffer.BlockCopy(c.Data, 4, idatPayload, 0, idatPayload.Length);
                        currentData.Add(idatPayload);
                    }
                }
                else if (c.Type == "IEND")
                {
                    break;
                }
            }

            if (collecting && currentData.Count > 0)
            {
                animFrames.Add((currentFctl, currentData));
            }

            if (animFrames.Count <= 1)
            {
                return false;
            }

            // Composite frames onto a shared canvas and collect BitmapSources
            int cw = (int)canvasWidth;
            int ch = (int)canvasHeight;
            int stride = cw * 4; // Pbgra32: 4 bytes per pixel
            byte[] canvas = new byte[stride * ch]; // starts transparent
            byte[]? savedCanvas = null;             // for dispose_op=previous

            var resultFrames    = new List<BitmapSource>(animFrames.Count);
            var resultDurations = new List<TimeSpan>(animFrames.Count);

            for (int i = 0; i < animFrames.Count; i++)
            {
                (FrameControl fctl, List<byte[]> imageData) = animFrames[i];

                // Apply the previous frame's disposal BEFORE rendering this frame
                if (i > 0)
                {
                    FrameControl prev = animFrames[i - 1].fctl;
                    switch (prev.DisposeOp)
                    {
                        case 1: // background — clear the previous frame's region to transparent
                            ClearRegion(canvas, stride, prev, cw, ch);
                            break;
                        case 2: // previous — restore canvas to state before previous frame
                            if (savedCanvas != null)
                            {
                                Buffer.BlockCopy(savedCanvas, 0, canvas, 0, canvas.Length);
                            }
                            break;
                        // 0 = none: leave canvas unchanged
                    }
                }

                // Save canvas NOW (before blending) if this frame will need "restore to previous"
                savedCanvas = fctl.DisposeOp == 2 ? (byte[])canvas.Clone() : null;

                // Build and decode a synthetic PNG for this frame's pixel data
                byte[] framePng = BuildSyntheticPng(ihdr.Data, ancillary, fctl, imageData);
                BitmapSource? framePixels = DecodePngToFormat(framePng, PixelFormats.Pbgra32);
                if (framePixels == null)
                {
                    return false;
                }

                // Blend the decoded frame onto the canvas
                BlendFrame(canvas, stride, framePixels, fctl, cw, ch);

                // Snapshot the composited canvas
                byte[] snapshot = (byte[])canvas.Clone();
                BitmapSource bmp = BitmapSource.Create(cw, ch, 96, 96, PixelFormats.Pbgra32, null, snapshot, stride);
                bmp.Freeze();
                resultFrames.Add(bmp);
                resultDurations.Add(fctl.Delay);
            }

            if (resultFrames.Count <= 1)
            {
                return false;
            }

            frames    = resultFrames;
            durations = resultDurations;
            return true;
        }

        private static void ClearRegion(byte[] canvas, int stride, FrameControl fctl, int cw, int ch)
        {
            int x1 = Math.Max(0, (int)fctl.XOffset);
            int y1 = Math.Max(0, (int)fctl.YOffset);
            int x2 = Math.Min(cw, (int)(fctl.XOffset + fctl.Width));
            int y2 = Math.Min(ch, (int)(fctl.YOffset + fctl.Height));

            for (int row = y1; row < y2; row++)
            {
                Array.Clear(canvas, row * stride + x1 * 4, (x2 - x1) * 4);
            }
        }

        private static void BlendFrame(byte[] canvas, int stride, BitmapSource src, FrameControl fctl, int cw, int ch)
        {
            int fw = src.PixelWidth;
            int fh = src.PixelHeight;
            int fStride = fw * 4;
            byte[] fPixels = new byte[fStride * fh];
            src.CopyPixels(fPixels, fStride, 0);

            int dstX = (int)fctl.XOffset;
            int dstY = (int)fctl.YOffset;

            for (int row = 0; row < fh; row++)
            {
                int canvasRow = dstY + row;
                if (canvasRow < 0 || canvasRow >= ch) continue;

                int srcRowBase = row * fStride;
                int dstRowBase = canvasRow * stride;

                for (int col = 0; col < fw; col++)
                {
                    int canvasCol = dstX + col;
                    if (canvasCol < 0 || canvasCol >= cw) continue;

                    int si = srcRowBase + col * 4;
                    int di = dstRowBase + canvasCol * 4;

                    byte srcA = fPixels[si + 3];

                    if (fctl.BlendOp == 0 || srcA == 255)
                    {
                        // Source replace: copy premultiplied pixel directly
                        canvas[di + 0] = fPixels[si + 0];
                        canvas[di + 1] = fPixels[si + 1];
                        canvas[di + 2] = fPixels[si + 2];
                        canvas[di + 3] = srcA;
                    }
                    else if (srcA > 0)
                    {
                        // Porter-Duff "over" in premultiplied space:
                        //   out = src + dst * (1 - src.A/255)
                        float invA = (255 - srcA) / 255f;
                        canvas[di + 0] = ClampByte(fPixels[si + 0] + canvas[di + 0] * invA);
                        canvas[di + 1] = ClampByte(fPixels[si + 1] + canvas[di + 1] * invA);
                        canvas[di + 2] = ClampByte(fPixels[si + 2] + canvas[di + 2] * invA);
                        canvas[di + 3] = ClampByte(srcA             + canvas[di + 3] * invA);
                    }
                    // srcA == 0: fully transparent source, do nothing
                }
            }
        }

        private static byte ClampByte(float v) => v >= 255f ? (byte)255 : v <= 0f ? (byte)0 : (byte)v;

        private static BitmapSource? DecodePngToFormat(byte[] pngData, PixelFormat targetFormat)
        {
            try
            {
                using MemoryStream ms = new MemoryStream(pngData, writable: false);
                BitmapDecoder decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0) return null;

                BitmapSource raw = decoder.Frames[0];
                if (raw.Format == targetFormat)
                {
                    if (raw.CanFreeze) raw.Freeze();
                    return raw;
                }

                FormatConvertedBitmap converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = raw;
                converted.DestinationFormat = targetFormat;
                converted.EndInit();
                if (converted.CanFreeze) converted.Freeze();
                return converted;
            }
            catch
            {
                return null;
            }
        }

        private static byte[] BuildSyntheticPng(
            byte[] originalIhdrData,
            List<PngChunk> ancillary,
            FrameControl fctl,
            List<byte[]> idatChunks)
        {
            using MemoryStream ms = new MemoryStream();
            using BinaryWriter w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

            // PNG signature
            w.Write(PngSignature);

            // IHDR with this frame's dimensions
            byte[] ihdrData = new byte[13];
            Buffer.BlockCopy(originalIhdrData, 0, ihdrData, 0, 13);
            WriteBE32(ihdrData, 0, fctl.Width);
            WriteBE32(ihdrData, 4, fctl.Height);
            WriteChunk(w, "IHDR", ihdrData);

            // Ancillary chunks (e.g. tRNS, pHYs)
            foreach (PngChunk ac in ancillary)
            {
                WriteChunk(w, ac.Type, ac.Data);
            }

            // Frame pixel data as IDAT chunks
            foreach (byte[] idat in idatChunks)
            {
                WriteChunk(w, "IDAT", idat);
            }

            // IEND
            WriteChunk(w, "IEND", Array.Empty<byte>());

            return ms.ToArray();
        }

        private static void WriteChunk(BinaryWriter w, string type, byte[] data)
        {
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);

            // Length (big-endian uint32)
            WriteBE32(w, (uint)data.Length);
            // Type
            w.Write(typeBytes);
            // Data
            if (data.Length > 0) w.Write(data);
            // CRC over type + data
            WriteBE32(w, ComputeCrc32(typeBytes, data));
        }

        // ── PNG chunk parsing ──────────────────────────────────────────────────────

        private static List<PngChunk> ParseChunks(byte[] data)
        {
            List<PngChunk> chunks = new List<PngChunk>();
            int pos = 8; // skip 8-byte signature

            while (pos + 12 <= data.Length)
            {
                uint length = ReadBE32(data, pos); pos += 4;
                if (pos + 4 > data.Length) break;
                string type = Encoding.ASCII.GetString(data, pos, 4); pos += 4;
                if (pos + length > data.Length) break;

                byte[] chunkData = new byte[length];
                Buffer.BlockCopy(data, pos, chunkData, 0, (int)length);
                pos += (int)length;
                pos += 4; // skip CRC

                chunks.Add(new PngChunk(type, chunkData));
                if (type == "IEND") break;
            }

            return chunks;
        }

        private static bool HasPngSignature(byte[] data)
        {
            if (data.Length < 8) return false;
            for (int i = 0; i < 8; i++)
            {
                if (data[i] != PngSignature[i]) return false;
            }
            return true;
        }

        // ── Bit/byte helpers ──────────────────────────────────────────────────────

        private static uint ReadBE32(byte[] data, int offset) =>
            ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
            ((uint)data[offset + 2] << 8)  | data[offset + 3];

        private static ushort ReadBE16(byte[] data, int offset) =>
            (ushort)(((uint)data[offset] << 8) | data[offset + 1]);

        private static void WriteBE32(BinaryWriter w, uint value)
        {
            w.Write((byte)(value >> 24));
            w.Write((byte)(value >> 16));
            w.Write((byte)(value >> 8));
            w.Write((byte) value);
        }

        private static void WriteBE32(byte[] buf, int offset, uint value)
        {
            buf[offset]     = (byte)(value >> 24);
            buf[offset + 1] = (byte)(value >> 16);
            buf[offset + 2] = (byte)(value >> 8);
            buf[offset + 3] = (byte) value;
        }

        // ── CRC-32 (PNG polynomial) ───────────────────────────────────────────────

        private static uint ComputeCrc32(byte[] typeBytes, byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in typeBytes) crc = (crc >> 8) ^ Crc32Table[(crc ^ b) & 0xFF];
            foreach (byte b in data)      crc = (crc >> 8) ^ Crc32Table[(crc ^ b) & 0xFF];
            return crc ^ 0xFFFFFFFF;
        }

        private static uint[] BuildCrc32Table()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }
                table[i] = c;
            }
            return table;
        }
    }
}
