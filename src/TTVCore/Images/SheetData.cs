using System;
using System.Collections.Generic;
using System.IO;

namespace TTVCore.Images
{
    /// <summary>全フレームを1枚にまとめたテクスチャの画素と配置。GPU へ送る前の状態。</summary>
    internal sealed class SheetData
    {
        private const int Padding = 2;

        /// <summary>各フレームの周りの余白（px）。</summary>
        public const int FramePadding = Padding;
        private const int MaxSize = 4096;
        private const int FormatVersion = 1;

        public int Width;
        public int Height;
        public int FrameWidth;
        public int FrameHeight;
        /// <summary>RGBA32、下の行から。</summary>
        public byte[] Pixels;
        /// <summary>各フレームの左下の位置（px）。</summary>
        public List<(int X, int Y)> FramePositions = new List<(int, int)>();
        public List<float> Durations = new List<float>();

        /// <summary>フレームを格子状に並べる。テクスチャの上限を超える分のフレームは間引く。</summary>
        public static SheetData Build(DecodedImage image)
        {
            var fw = image.Width;
            var fh = image.Height;
            var cellW = fw + Padding * 2;
            var cellH = fh + Padding * 2;
            var frames = image.Frames.Count;
            var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(frames)));
            var step = 1;
            while (true) {
                var used = (frames + step - 1) / step;
                columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(used)));
                var rows = (used + columns - 1) / columns;
                if ((columns * cellW <= MaxSize && rows * cellH <= MaxSize) || used == 1) {
                    break;
                }
                step++;
            }

            var sheet = new SheetData { FrameWidth = fw, FrameHeight = fh };
            var indices = new List<int>();
            for (var i = 0; i < frames; i += step) {
                indices.Add(i);
            }
            var rowCount = (indices.Count + columns - 1) / columns;
            sheet.Width = NextPowerOfTwo(Math.Min(MaxSize, columns * cellW));
            sheet.Height = NextPowerOfTwo(Math.Min(MaxSize, rowCount * cellH));
            sheet.Pixels = new byte[sheet.Width * sheet.Height * 4];

            for (var n = 0; n < indices.Count; n++) {
                var col = n % columns;
                var row = n / columns;
                var x = col * cellW + Padding;
                var y = row * cellH + Padding;
                var src = image.Frames[indices[n]];
                for (var line = 0; line < fh && y + line < sheet.Height; line++) {
                    Buffer.BlockCopy(src, line * fw * 4, sheet.Pixels, ((y + line) * sheet.Width + x) * 4, Math.Min(fw, sheet.Width - x) * 4);
                }
                sheet.FramePositions.Add((x, y));
                var duration = 0f;
                for (var k = indices[n]; k < Math.Min(indices[n] + step, frames); k++) {
                    duration += image.Durations[k];
                }
                sheet.Durations.Add(duration);
            }
            return sheet;
        }

        private static int NextPowerOfTwo(int v)
        {
            var p = 1;
            while (p < v) {
                p <<= 1;
            }
            return p;
        }

        public void Write(Stream stream)
        {
            using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) {
                w.Write(FormatVersion);
                w.Write(this.Width);
                w.Write(this.Height);
                w.Write(this.FrameWidth);
                w.Write(this.FrameHeight);
                w.Write(this.FramePositions.Count);
                for (var i = 0; i < this.FramePositions.Count; i++) {
                    w.Write(this.FramePositions[i].X);
                    w.Write(this.FramePositions[i].Y);
                    w.Write(this.Durations[i]);
                }
                w.Write(this.Pixels.Length);
                w.Write(this.Pixels);
            }
        }

        public static SheetData Read(Stream stream)
        {
            using (var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true)) {
                if (r.ReadInt32() != FormatVersion) {
                    return null;
                }
                var sheet = new SheetData {
                    Width = r.ReadInt32(),
                    Height = r.ReadInt32(),
                    FrameWidth = r.ReadInt32(),
                    FrameHeight = r.ReadInt32(),
                };
                var count = r.ReadInt32();
                for (var i = 0; i < count; i++) {
                    sheet.FramePositions.Add((r.ReadInt32(), r.ReadInt32()));
                    sheet.Durations.Add(r.ReadSingle());
                }
                var length = r.ReadInt32();
                if (length != sheet.Width * sheet.Height * 4) {
                    return null;
                }
                sheet.Pixels = r.ReadBytes(length);
                return sheet.Pixels.Length == length ? sheet : null;
            }
        }
    }
}
