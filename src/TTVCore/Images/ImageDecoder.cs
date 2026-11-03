using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace TTVCore.Images
{
    /// <summary>デコード済みの画像。画素は RGBA32 で、Unity のテクスチャと同じく下の行から並ぶ。</summary>
    internal sealed class DecodedImage
    {
        public int Width;
        public int Height;
        public List<byte[]> Frames = new List<byte[]>();
        /// <summary>各フレームの表示時間（秒）。</summary>
        public List<float> Durations = new List<float>();
    }

    /// <summary>PNG / GIF を別スレッドでデコードする（System.Drawing を使う）。</summary>
    internal static class ImageDecoder
    {
        private const int FrameDelayProperty = 0x5100;
        // 極端に短い表示時間は、ブラウザーと同じく 100ms として扱う
        private const float MinFrameSeconds = 0.011f;
        private const float ShortFrameSeconds = 0.1f;
        private const int MaxFrames = 256;

        public static DecodedImage Decode(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var image = Image.FromStream(ms)) {
                var result = new DecodedImage { Width = image.Width, Height = image.Height };
                var dimension = new FrameDimension(image.FrameDimensionsList[0]);
                var count = Math.Min(image.GetFrameCount(dimension), MaxFrames);
                int[] delays = null;
                if (count > 1) {
                    try {
                        var item = image.GetPropertyItem(FrameDelayProperty);
                        delays = new int[item.Value.Length / 4];
                        for (var i = 0; i < delays.Length; i++) {
                            delays[i] = BitConverter.ToInt32(item.Value, i * 4);
                        }
                    }
                    catch (ArgumentException) {
                        delays = null;
                    }
                }
                for (var i = 0; i < count; i++) {
                    if (count > 1) {
                        image.SelectActiveFrame(dimension, i);
                    }
                    result.Frames.Add(ReadPixels(image));
                    var seconds = delays != null && i < delays.Length ? delays[i] / 100f : ShortFrameSeconds;
                    result.Durations.Add(seconds < MinFrameSeconds ? ShortFrameSeconds : seconds);
                }
                return result;
            }
        }

        /// <summary>画像全体を RGBA32（下の行から）に変換する。GIF の各フレームはキャンバス全体の大きさで得られる。</summary>
        private static byte[] ReadPixels(Image image)
        {
            var w = image.Width;
            var h = image.Height;
            using (var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb)) {
                using (var g = Graphics.FromImage(bitmap)) {
                    g.Clear(System.Drawing.Color.Transparent);
                    g.DrawImage(image, 0, 0, w, h);
                }
                var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try {
                    var row = new byte[w * 4];
                    var pixels = new byte[w * h * 4];
                    for (var y = 0; y < h; y++) {
                        Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                        var dst = (h - 1 - y) * w * 4;
                        for (var x = 0; x < w; x++) {
                            // BGRA → RGBA
                            pixels[dst + x * 4] = row[x * 4 + 2];
                            pixels[dst + x * 4 + 1] = row[x * 4 + 1];
                            pixels[dst + x * 4 + 2] = row[x * 4];
                            pixels[dst + x * 4 + 3] = row[x * 4 + 3];
                        }
                    }
                    return pixels;
                }
                finally {
                    bitmap.UnlockBits(data);
                }
            }
        }
    }
}
