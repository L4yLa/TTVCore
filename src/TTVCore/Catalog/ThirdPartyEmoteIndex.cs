namespace TTVCore.Catalog
{
    /// <summary>本文の語に当たるサードパーティエモートを探す。チャンネルのものを先に、次にグローバルを見る。</summary>
    internal static class ThirdPartyEmoteIndex
    {
        public static bool TryFind(string channelId, string word, out EmoteEntry emote)
        {
            var channel = CatalogStore.GetChannel(channelId);
            var global = CatalogStore.Global;
            return channel.BttvEmotes.TryGetValue(word, out emote)
                   || channel.FfzEmotes.TryGetValue(word, out emote)
                   || global.BttvEmotes.TryGetValue(word, out emote)
                   || global.FfzEmotes.TryGetValue(word, out emote);
        }
    }
}
