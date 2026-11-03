using TTVCore.Api.V1;

namespace TTVCore.Images
{
    /// <summary><see cref="ImageKey"/> の作成と解釈。形式は TTVCore の内部でのみ使う。</summary>
    internal static class ImageKeys
    {
        public const string TwitchEmote = "tw";
        public const string Cheermote = "cm";
        public const string Badge = "bg";
        public const string Bttv = "bt";
        public const string Ffz = "fz";
        public const string Emoji = "em";

        public static ImageKey ForTwitchEmote(string emoteId, bool animated) =>
            new ImageKey($"{TwitchEmote}:{emoteId}:{(animated ? "a" : "s")}");

        public static ImageKey ForCheermote(string prefix, int tier) =>
            new ImageKey($"{Cheermote}:{prefix}:{tier}");

        /// <summary>バッジはチャンネルごとに画像が違うため、チャンネルの ID を含める。</summary>
        public static ImageKey ForBadge(string broadcasterId, string setId, string id) =>
            new ImageKey($"{Badge}:{broadcasterId}:{setId}:{id}");

        public static ImageKey ForBttv(string emoteId, bool animated) =>
            new ImageKey($"{Bttv}:{emoteId}:{(animated ? "a" : "s")}");

        public static ImageKey ForFfz(string emoteId, bool animated) =>
            new ImageKey($"{Ffz}:{emoteId}:{(animated ? "a" : "s")}");

        /// <summary><paramref name="fileName"/> は同梱の Twemoji のファイル名（拡張子なし）。</summary>
        public static ImageKey ForEmoji(string fileName) =>
            new ImageKey($"{Emoji}:{fileName}");

        /// <summary>種類と残りの部分に分ける。</summary>
        public static bool TryParse(ImageKey key, out string kind, out string[] parts)
        {
            kind = null;
            parts = null;
            if (!key.IsValid) {
                return false;
            }
            var all = key.Value.Split(':');
            if (all.Length < 2) {
                return false;
            }
            kind = all[0];
            parts = new string[all.Length - 1];
            System.Array.Copy(all, 1, parts, 0, parts.Length);
            return true;
        }
    }
}
