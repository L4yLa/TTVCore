using System;
using System.Collections.Generic;
using System.Linq;
using TTVCore.Catalog;
using TTVCore.Utilities;

namespace TTVCore.Api.V1
{
    /// <summary>チャンネルで使えるエモートとバッジの一覧。</summary>
    public sealed class CatalogApi
    {
        internal CatalogApi()
        {
        }

        /// <summary>
        /// そのチャンネルで使えるエモート（グローバルを含む）。Twitch エモートは、そのチャンネルのものとグローバルのもの。
        /// 視聴者が他のチャンネルのエモートを使った場合は、メッセージのフラグメントで初めて分かる。
        /// </summary>
        public IReadOnlyList<EmoteInfo> GetEmotes(string broadcasterId)
        {
            var channel = CatalogStore.GetChannel(broadcasterId);
            var global = CatalogStore.Global;
            return Enumerate(channel).Concat(Enumerate(global)).Select(ToInfo).ToList();
        }

        /// <summary>名前からエモートを探す。チャンネルのものを先に、次にグローバルを見る。</summary>
        public bool TryFindEmote(string broadcasterId, string name, out EmoteInfo emote)
        {
            emote = null;
            if (string.IsNullOrEmpty(name)) {
                return false;
            }
            var channel = CatalogStore.GetChannel(broadcasterId);
            var global = CatalogStore.Global;
            if (channel.TwitchEmotes.TryGetValue(name, out var e) || global.TwitchEmotes.TryGetValue(name, out e)
                || ThirdPartyEmoteIndex.TryFind(broadcasterId, name, out e)) {
                emote = ToInfo(e);
                return true;
            }
            return false;
        }

        /// <summary>バッジの情報。チャンネルのものを先に、次にグローバルを見る。見つからなければ null。</summary>
        public BadgeInfo GetBadge(string broadcasterId, BadgeRef badge)
        {
            if (badge == null) {
                return null;
            }
            var key = CatalogSnapshot.BadgeKey(badge.SetId, badge.Id);
            if (CatalogStore.GetChannel(broadcasterId).Badges.TryGetValue(key, out var b) || CatalogStore.Global.Badges.TryGetValue(key, out b)) {
                return new BadgeInfo { SetId = b.SetId, Id = b.Id, Title = b.Title, Image = b.Image };
            }
            return null;
        }

        /// <summary>一覧が更新された。引数はチャンネルの ID。グローバルの一覧なら null。</summary>
        public event Action<string> EmotesUpdated;

        internal void RaiseEmotesUpdated(string broadcasterId) =>
            EventInvoker.Post(() => this.EmotesUpdated, broadcasterId, "Catalog.EmotesUpdated");

        private static IEnumerable<EmoteEntry> Enumerate(CatalogSnapshot s) =>
            s.TwitchEmotes.Values.Concat(s.BttvEmotes.Values).Concat(s.FfzEmotes.Values);

        private static EmoteInfo ToInfo(EmoteEntry e) => new EmoteInfo {
            Provider = e.Provider,
            EmoteId = e.EmoteId,
            Name = e.Name,
            IsAnimated = e.IsAnimated,
            IsGlobal = e.IsGlobal,
            Image = e.Image,
        };
    }

    public sealed class EmoteInfo
    {
        public EmoteProvider Provider { get; internal set; }
        public string EmoteId { get; internal set; }
        public string Name { get; internal set; }
        public bool IsAnimated { get; internal set; }
        public bool IsGlobal { get; internal set; }
        public ImageKey Image { get; internal set; }
    }

    public sealed class BadgeInfo
    {
        public string SetId { get; internal set; }
        public string Id { get; internal set; }
        public string Title { get; internal set; }
        public ImageKey Image { get; internal set; }
    }
}
