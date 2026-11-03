using System.Collections.Generic;
using System.Text;
using TTVCore.Api.V1;
using TTVCore.Catalog;
using TTVCore.Emoji;

namespace TTVCore.Chat
{
    /// <summary>
    /// Twitch から届く文字の部品を、サードパーティエモートと絵文字で分割する。
    /// サードパーティエモートは空白で区切られた語の完全一致、絵文字は最長一致で探す。
    /// </summary>
    internal static class TextExpander
    {
        public static void Expand(string text, string channelId, List<ChatFragment> output)
        {
            if (string.IsNullOrEmpty(text)) {
                return;
            }
            var pending = new StringBuilder();
            var i = 0;
            while (i < text.Length) {
                if (text[i] == ' ') {
                    pending.Append(' ');
                    i++;
                    continue;
                }
                var end = text.IndexOf(' ', i);
                if (end < 0) {
                    end = text.Length;
                }
                var word = text.Substring(i, end - i);
                if (ThirdPartyEmoteIndex.TryFind(channelId, word, out var emote)) {
                    Flush(pending, output);
                    output.Add(new ThirdPartyEmoteFragment(word, emote.Provider, emote.EmoteId, emote.IsAnimated, emote.Image));
                }
                else {
                    AppendWithEmoji(word, pending, output);
                }
                i = end;
            }
            Flush(pending, output);
        }

        private static void AppendWithEmoji(string word, StringBuilder pending, List<ChatFragment> output)
        {
            var i = 0;
            while (i < word.Length) {
                if (EmojiTable.TryMatch(word, i, out var length, out var image)) {
                    Flush(pending, output);
                    output.Add(new EmojiFragment(word.Substring(i, length), image));
                    i += length;
                }
                else {
                    pending.Append(word[i]);
                    i++;
                }
            }
        }

        private static void Flush(StringBuilder pending, List<ChatFragment> output)
        {
            if (pending.Length == 0) {
                return;
            }
            output.Add(new TextFragment(pending.ToString()));
            pending.Clear();
        }
    }
}
