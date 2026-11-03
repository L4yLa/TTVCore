using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TTVCore.Images
{
    /// <summary>
    /// 埋め込みリソース twemoji.bin（tools/build_emoji.py で作る）から、絵文字の PNG を取り出す。
    /// 形式: "TWEM"、件数（int32）、件数分の [名前の長さ（uint8）、名前（ASCII）、開始位置（int32）、長さ（int32）]、画像の本体。
    /// </summary>
    internal static class EmojiImages
    {
        private const string ResourceName = "TTVCore.Resources.twemoji.bin";

        private static readonly object s_gate = new object();
        private static Dictionary<string, (int Offset, int Length)> s_index;
        private static bool s_failed;

        public static byte[] Read(string name)
        {
            lock (s_gate) {
                if (!EnsureIndex() || !s_index.TryGetValue(name, out var entry)) {
                    return null;
                }
                using (var stream = typeof(EmojiImages).Assembly.GetManifestResourceStream(ResourceName)) {
                    stream.Seek(entry.Offset, SeekOrigin.Begin);
                    var data = new byte[entry.Length];
                    var read = 0;
                    while (read < data.Length) {
                        var n = stream.Read(data, read, data.Length - read);
                        if (n <= 0) {
                            return null;
                        }
                        read += n;
                    }
                    return data;
                }
            }
        }

        private static bool EnsureIndex()
        {
            if (s_index != null) {
                return true;
            }
            if (s_failed) {
                return false;
            }
            try {
                using (var stream = typeof(EmojiImages).Assembly.GetManifestResourceStream(ResourceName))
                using (var reader = new BinaryReader(stream, Encoding.ASCII)) {
                    if (new string(reader.ReadChars(4)) != "TWEM") {
                        throw new InvalidDataException("bad header");
                    }
                    var count = reader.ReadInt32();
                    var index = new Dictionary<string, (int, int)>(count, StringComparer.Ordinal);
                    for (var i = 0; i < count; i++) {
                        var len = reader.ReadByte();
                        var name = new string(reader.ReadChars(len));
                        index[name] = (reader.ReadInt32(), reader.ReadInt32());
                    }
                    s_index = index;
                    return true;
                }
            }
            catch (Exception e) {
                s_failed = true;
                Plugin.Log.Error($"Emoji: failed to read the emoji images ({e.GetType().Name}: {e.Message}).");
                return false;
            }
        }
    }
}
