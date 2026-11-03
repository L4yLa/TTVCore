using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TTVCore.Api.V1;
using TTVCore.Images;

namespace TTVCore.Emoji
{
    /// <summary>
    /// 絵文字の並びの表。埋め込みリソース emoji-table.txt（tools/build_emoji.py で作る）から読む。
    /// 1行に「並び（UTF-16 の16進数を空白区切り）<TAB>画像のファイル名」。本文は先頭から最長一致で照合する。
    /// </summary>
    internal static class EmojiTable
    {
        private const string ResourceName = "TTVCore.Resources.emoji-table.txt";

        private sealed class Node
        {
            public Dictionary<char, Node> Next;
            public string FileName;
        }

        private static Node s_root;

        public static void Load()
        {
            var root = new Node();
            var count = 0;
            try {
                using (var stream = typeof(EmojiTable).Assembly.GetManifestResourceStream(ResourceName)) {
                    if (stream == null) {
                        Plugin.Log.Warn("Emoji: the emoji table is missing; emoji are shown as text.");
                        return;
                    }
                    using (var reader = new StreamReader(stream, Encoding.UTF8)) {
                        string line;
                        while ((line = reader.ReadLine()) != null) {
                            var tab = line.IndexOf('\t');
                            if (tab <= 0) {
                                continue;
                            }
                            var node = root;
                            foreach (var hex in line.Substring(0, tab).Split(' ')) {
                                var c = (char)Convert.ToInt32(hex, 16);
                                if (node.Next == null) {
                                    node.Next = new Dictionary<char, Node>();
                                }
                                if (!node.Next.TryGetValue(c, out var child)) {
                                    child = new Node();
                                    node.Next[c] = child;
                                }
                                node = child;
                            }
                            node.FileName = line.Substring(tab + 1);
                            count++;
                        }
                    }
                }
            }
            catch (Exception e) {
                Plugin.Log.Error($"Emoji: failed to read the emoji table ({e.GetType().Name}: {e.Message}).");
                return;
            }
            s_root = root;
            Plugin.Log.Info($"Emoji: {count} sequences.");
        }

        /// <summary><paramref name="start"/> から始まる最も長い絵文字の並びを探す。</summary>
        public static bool TryMatch(string text, int start, out int length, out ImageKey image)
        {
            length = 0;
            image = default(ImageKey);
            var node = s_root;
            if (node == null || text[start] < 0x00A9) {
                return false;
            }
            string found = null;
            for (var i = start; i < text.Length; i++) {
                if (node.Next == null || !node.Next.TryGetValue(text[i], out node)) {
                    break;
                }
                if (node.FileName != null) {
                    found = node.FileName;
                    length = i - start + 1;
                }
            }
            if (found == null) {
                return false;
            }
            image = ImageKeys.ForEmoji(found);
            return true;
        }
    }
}
