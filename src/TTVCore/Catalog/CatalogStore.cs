using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TTVCore.Api.V1;
using TTVCore.Images;
using TTVCore.Utilities;

namespace TTVCore.Catalog
{
    internal sealed class EmoteEntry
    {
        public EmoteProvider Provider;
        public string EmoteId;
        public string Name;
        public bool IsAnimated;
        public bool IsGlobal;
        public ImageKey Image;
    }

    internal sealed class BadgeEntry
    {
        public string SetId;
        public string Id;
        public string Title;
        public ImageKey Image;
    }

    /// <summary>1つのチャンネル（またはグローバル）の一覧。作り終えたら変更しない。</summary>
    internal sealed class CatalogSnapshot
    {
        public static readonly CatalogSnapshot Empty = new CatalogSnapshot();

        public Dictionary<string, EmoteEntry> TwitchEmotes = new Dictionary<string, EmoteEntry>(StringComparer.Ordinal);
        public Dictionary<string, EmoteEntry> BttvEmotes = new Dictionary<string, EmoteEntry>(StringComparer.Ordinal);
        public Dictionary<string, EmoteEntry> FfzEmotes = new Dictionary<string, EmoteEntry>(StringComparer.Ordinal);
        public Dictionary<string, BadgeEntry> Badges = new Dictionary<string, BadgeEntry>(StringComparer.Ordinal);

        public static string BadgeKey(string setId, string id) => setId + "/" + id;
    }

    /// <summary>
    /// チャンネルとグローバルのエモート・バッジ・Cheermote の一覧を取得して持つ。
    /// 画像の取得先もここで登録する。
    /// </summary>
    internal static class CatalogStore
    {
        // エモートとバッジの解像度。見た目と負荷は試作で確かめる。
        private const string TwitchEmoteScale = "3.0";
        private const string BttvScale = "3x";

        private static readonly ConcurrentDictionary<string, CatalogSnapshot> s_channels = new ConcurrentDictionary<string, CatalogSnapshot>();
        private static readonly ConcurrentDictionary<string, int> s_loading = new ConcurrentDictionary<string, int>();
        private static volatile CatalogSnapshot s_global = CatalogSnapshot.Empty;
        private static volatile Dictionary<string, Dictionary<int, string>> s_cheermotes = new Dictionary<string, Dictionary<int, string>>();
        private static int s_globalLoaded;

        public static CatalogSnapshot Global => s_global;

        public static CatalogSnapshot GetChannel(string broadcasterId)
        {
            return broadcasterId != null && s_channels.TryGetValue(broadcasterId, out var c) ? c : CatalogSnapshot.Empty;
        }

        public static bool HasChannel(string broadcasterId) => broadcasterId != null && s_channels.ContainsKey(broadcasterId);

        /// <summary>Cheermote の画像の URL。見つからなければ null。</summary>
        public static string GetCheermoteUrl(string prefix, int tier)
        {
            if (!s_cheermotes.TryGetValue(prefix, out var tiers) || tiers.Count == 0) {
                return null;
            }
            if (tiers.TryGetValue(tier, out var url)) {
                return url;
            }
            // tier がビッツ数で届いた場合に備え、それ以下で最も大きい段を使う
            var best = tiers.Keys.Where(k => k <= tier).DefaultIfEmpty(tiers.Keys.Min()).Max();
            return tiers[best];
        }

        public static string TwitchEmoteUrl(string emoteId, bool animated) =>
            $"https://static-cdn.jtvnw.net/emoticons/v2/{emoteId}/{(animated ? "animated" : "static")}/dark/{TwitchEmoteScale}";

        /// <summary>グローバルの一覧を（まだなら）読み込む。</summary>
        public static async Task EnsureGlobalAsync(CancellationToken ct)
        {
            if (Interlocked.CompareExchange(ref s_globalLoaded, 1, 0) != 0) {
                return;
            }
            var snapshot = new CatalogSnapshot();
            var ok = false;
            try {
                ok = await LoadTwitchEmotesAsync("chat/emotes/global", snapshot, true, ct).ConfigureAwait(false);
                await LoadBadgesAsync("chat/badges/global", null, snapshot, ct).ConfigureAwait(false);
                await LoadBttvGlobalAsync(snapshot, ct).ConfigureAwait(false);
                await LoadFfzGlobalAsync(snapshot, ct).ConfigureAwait(false);
                s_global = snapshot;
                Plugin.Log.Info($"Catalog: global emotes Twitch={snapshot.TwitchEmotes.Count} BTTV={snapshot.BttvEmotes.Count} FFZ={snapshot.FfzEmotes.Count}, badges={snapshot.Badges.Count}.");
            }
            finally {
                if (!ok) {
                    // Twitch の一覧が取れなければ、次の接続で取り直す
                    Interlocked.Exchange(ref s_globalLoaded, 0);
                }
            }
            TTVCoreApi.Catalog.RaiseEmotesUpdated(null);
        }

        /// <summary>チャンネルの一覧を読み込み直す。同じチャンネルの読み込み中は何もしない。</summary>
        public static async Task LoadChannelAsync(string broadcasterId, bool includeCheermotes, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(broadcasterId) || !s_loading.TryAdd(broadcasterId, 0)) {
                return;
            }
            try {
                var snapshot = new CatalogSnapshot();
                var id = Uri.EscapeDataString(broadcasterId);
                await LoadTwitchEmotesAsync("chat/emotes?broadcaster_id=" + id, snapshot, false, ct).ConfigureAwait(false);
                await LoadBadgesAsync("chat/badges?broadcaster_id=" + id, broadcasterId, snapshot, ct).ConfigureAwait(false);
                await LoadBttvChannelAsync(broadcasterId, snapshot, ct).ConfigureAwait(false);
                await LoadFfzChannelAsync(broadcasterId, snapshot, ct).ConfigureAwait(false);
                if (includeCheermotes) {
                    await LoadCheermotesAsync(broadcasterId, ct).ConfigureAwait(false);
                }
                s_channels[broadcasterId] = snapshot;
                Plugin.Log.Info($"Catalog: channel emotes Twitch={snapshot.TwitchEmotes.Count} BTTV={snapshot.BttvEmotes.Count} FFZ={snapshot.FfzEmotes.Count}, badges={snapshot.Badges.Count}.");
                TTVCoreApi.Catalog.RaiseEmotesUpdated(broadcasterId);
            }
            finally {
                s_loading.TryRemove(broadcasterId, out _);
            }
        }

        /// <summary>共有チャットから外れたチャンネルの一覧を捨てる。</summary>
        public static void RetainChannels(ICollection<string> keep)
        {
            foreach (var id in s_channels.Keys.ToArray()) {
                if (!keep.Contains(id)) {
                    s_channels.TryRemove(id, out _);
                }
            }
        }

        private static async Task<bool> LoadTwitchEmotesAsync(string path, CatalogSnapshot snapshot, bool global, CancellationToken ct)
        {
            var jo = await Helix.HelixClient.GetJsonAsync(path, ct).ConfigureAwait(false);
            if (!(jo?["data"] is JArray data)) {
                return false;
            }
            foreach (var e in data) {
                var id = JsonUtil.StrOrNull(JsonUtil.At(e, "id"));
                var name = JsonUtil.StrOrNull(JsonUtil.At(e, "name"));
                if (id == null || name == null) {
                    continue;
                }
                var animated = JsonUtil.At(e, "format") is JArray formats && formats.Any(f => JsonUtil.Str(f) == "animated");
                snapshot.TwitchEmotes[name] = new EmoteEntry {
                    Provider = EmoteProvider.Twitch,
                    EmoteId = id,
                    Name = name,
                    IsAnimated = animated,
                    IsGlobal = global,
                    Image = ImageKeys.ForTwitchEmote(id, animated),
                };
            }
            return true;
        }

        private static async Task LoadBadgesAsync(string path, string broadcasterId, CatalogSnapshot snapshot, CancellationToken ct)
        {
            var jo = await Helix.HelixClient.GetJsonAsync(path, ct).ConfigureAwait(false);
            if (!(jo?["data"] is JArray data)) {
                return;
            }
            foreach (var set in data) {
                var setId = JsonUtil.StrOrNull(JsonUtil.At(set, "set_id"));
                if (setId == null || !(JsonUtil.At(set, "versions") is JArray versions)) {
                    continue;
                }
                foreach (var v in versions) {
                    var id = JsonUtil.StrOrNull(JsonUtil.At(v, "id"));
                    var url = JsonUtil.StrOrNull(JsonUtil.At(v, "image_url_4x")) ?? JsonUtil.StrOrNull(JsonUtil.At(v, "image_url_2x"));
                    if (id == null || url == null) {
                        continue;
                    }
                    var key = ImageKeys.ForBadge(broadcasterId ?? "global", setId, id);
                    ImageSources.Register(key, url, ImageOrigin.Twitch);
                    snapshot.Badges[CatalogSnapshot.BadgeKey(setId, id)] = new BadgeEntry {
                        SetId = setId,
                        Id = id,
                        Title = JsonUtil.Str(JsonUtil.At(v, "title")) ?? setId,
                        Image = key,
                    };
                }
            }
        }

        private static async Task LoadCheermotesAsync(string broadcasterId, CancellationToken ct)
        {
            var jo = await Helix.HelixClient.GetJsonAsync("bits/cheermotes?broadcaster_id=" + Uri.EscapeDataString(broadcasterId), ct).ConfigureAwait(false);
            if (!(jo?["data"] is JArray data)) {
                return;
            }
            var result = new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);
            foreach (var c in data) {
                var prefix = JsonUtil.StrOrNull(JsonUtil.At(c, "prefix"))?.ToLowerInvariant();
                if (prefix == null || !(JsonUtil.At(c, "tiers") is JArray tiers)) {
                    continue;
                }
                var map = new Dictionary<int, string>();
                foreach (var t in tiers) {
                    var minBits = JsonUtil.Int(JsonUtil.At(t, "min_bits"));
                    var url = JsonUtil.StrOrNull(JsonUtil.At(t, "images", "dark", "animated", "3"))
                              ?? JsonUtil.StrOrNull(JsonUtil.At(t, "images", "dark", "static", "3"));
                    if (url != null) {
                        map[minBits] = url;
                    }
                }
                result[prefix] = map;
            }
            s_cheermotes = result;
        }

        private static async Task LoadBttvGlobalAsync(CatalogSnapshot snapshot, CancellationToken ct)
        {
            var arr = await PublicHttp.GetJsonAsync("https://api.betterttv.net/3/cached/emotes/global", "BTTV", ct).ConfigureAwait(false) as JArray;
            AddBttv(arr, snapshot, true);
        }

        private static async Task LoadBttvChannelAsync(string broadcasterId, CatalogSnapshot snapshot, CancellationToken ct)
        {
            var jo = await PublicHttp.GetJsonAsync("https://api.betterttv.net/3/cached/users/twitch/" + Uri.EscapeDataString(broadcasterId), "BTTV", ct).ConfigureAwait(false) as JObject;
            if (jo == null) {
                return;
            }
            AddBttv(jo["channelEmotes"] as JArray, snapshot, false);
            AddBttv(jo["sharedEmotes"] as JArray, snapshot, false);
        }

        private static void AddBttv(JArray arr, CatalogSnapshot snapshot, bool global)
        {
            if (arr == null) {
                return;
            }
            foreach (var e in arr) {
                var id = JsonUtil.StrOrNull(JsonUtil.At(e, "id"));
                var code = JsonUtil.StrOrNull(JsonUtil.At(e, "code"));
                if (id == null || code == null) {
                    continue;
                }
                var animated = JsonUtil.Bool(JsonUtil.At(e, "animated"));
                var key = ImageKeys.ForBttv(id, animated);
                ImageSources.Register(key, $"https://cdn.betterttv.net/emote/{id}/{BttvScale}.{(animated ? "gif" : "png")}", ImageOrigin.Bttv);
                snapshot.BttvEmotes[code] = new EmoteEntry {
                    Provider = EmoteProvider.Bttv,
                    EmoteId = id,
                    Name = code,
                    IsAnimated = animated,
                    IsGlobal = global,
                    Image = key,
                };
            }
        }

        private static async Task LoadFfzGlobalAsync(CatalogSnapshot snapshot, CancellationToken ct)
        {
            var jo = await PublicHttp.GetJsonAsync("https://api.frankerfacez.com/v1/set/global", "FFZ", ct).ConfigureAwait(false) as JObject;
            if (jo == null) {
                return;
            }
            var defaults = (jo["default_sets"] as JArray)?.Select(JsonUtil.Str).ToList() ?? new List<string>();
            AddFfzSets(jo["sets"] as JObject, s => defaults.Contains(s), snapshot, true);
        }

        private static async Task LoadFfzChannelAsync(string broadcasterId, CatalogSnapshot snapshot, CancellationToken ct)
        {
            var jo = await PublicHttp.GetJsonAsync("https://api.frankerfacez.com/v1/room/id/" + Uri.EscapeDataString(broadcasterId), "FFZ", ct).ConfigureAwait(false) as JObject;
            if (jo == null) {
                return;
            }
            AddFfzSets(jo["sets"] as JObject, s => true, snapshot, false);
        }

        private static void AddFfzSets(JObject sets, Func<string, bool> include, CatalogSnapshot snapshot, bool global)
        {
            if (sets == null) {
                return;
            }
            foreach (var prop in sets.Properties()) {
                if (!include(prop.Name) || !(JsonUtil.At(prop.Value, "emoticons") is JArray emotes)) {
                    continue;
                }
                foreach (var e in emotes) {
                    var id = JsonUtil.StrOrNull(JsonUtil.At(e, "id"));
                    var name = JsonUtil.StrOrNull(JsonUtil.At(e, "name"));
                    if (id == null || name == null) {
                        continue;
                    }
                    var animatedUrl = LargestUrl(JsonUtil.At(e, "animated") as JObject);
                    var staticUrl = LargestUrl(JsonUtil.At(e, "urls") as JObject);
                    var animated = animatedUrl != null;
                    var url = animated ? animatedUrl + ".gif" : staticUrl;
                    if (url == null) {
                        continue;
                    }
                    var key = ImageKeys.ForFfz(id, animated);
                    ImageSources.Register(key, url, ImageOrigin.Ffz);
                    snapshot.FfzEmotes[name] = new EmoteEntry {
                        Provider = EmoteProvider.Ffz,
                        EmoteId = id,
                        Name = name,
                        IsAnimated = animated,
                        IsGlobal = global,
                        Image = key,
                    };
                }
            }
        }

        private static string LargestUrl(JObject urls)
        {
            if (urls == null) {
                return null;
            }
            var best = urls.Properties()
                .Select(p => (Scale: int.TryParse(p.Name, out var s) ? s : 0, Url: JsonUtil.StrOrNull(p.Value)))
                .Where(x => x.Url != null)
                .OrderByDescending(x => x.Scale)
                .FirstOrDefault();
            if (best.Url == null) {
                return null;
            }
            return best.Url.StartsWith("//", StringComparison.Ordinal) ? "https:" + best.Url : best.Url;
        }
    }
}
