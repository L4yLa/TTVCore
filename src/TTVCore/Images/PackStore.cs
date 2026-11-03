using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TTVCore.Api.V1;
using TTVCore.Catalog;
using TTVCore.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TTVCore.Images
{
    /// <summary>パックの1ページ（共有テクスチャ）。</summary>
    internal sealed class PackPage
    {
        public Texture2D Texture;
        public Material Material;
        /// <summary>このページを使っている <see cref="ImageEntry"/> の数。</summary>
        public int Entries;
        /// <summary>新しいパックに置き換えられた。使われなくなったら破棄する。</summary>
        public bool Retired;
    }

    /// <summary>
    /// パック。よく使われると見込める画像をメニュー画面でまとめて読み込み、少数の共有テクスチャ（ページ）に詰める。
    /// <list type="bullet">
    /// <item>読み込み、配置、ページの画素の合成は別スレッドで行い、GPU への転送だけをメインスレッドで1フレームに1ページまで行う。</item>
    /// <item>曲の最中は作らない。曲が始まったら中断し、メニューに戻ってから作り直す。</item>
    /// <item>FFZ の画像は FFZ だけのページに入れる（ページをディスクに保存しないのは全ページ共通）。</item>
    /// <item>すでに読み込まれている画像は差し替えない。パックに入った画像は、次に新しく読み込むときからページを使う。</item>
    /// </list>
    /// </summary>
    internal static class PackStore
    {
        private const string MenuScene = "MainMenu";
        private const string GameScene = "GameCore";

        private sealed class PackedImage
        {
            public PackPage Page;
            public Rect[] UVs;
            public float[] Durations;
            public int Width;
            public int Height;
        }

        /// <summary>別スレッドで作った、GPU へ送る前のページ。</summary>
        private sealed class PageData
        {
            public int Size;
            public byte[] Pixels;
            public readonly List<(ImageKey Key, Rect[] UVs, float[] Durations, int Width, int Height)> Items =
                new List<(ImageKey, Rect[], float[], int, int)>();
        }

        private static Dictionary<string, PackedImage> s_images = new Dictionary<string, PackedImage>(StringComparer.Ordinal);
        private static readonly List<PackPage> s_pages = new List<PackPage>();

        // 作成中のパック
        private static List<PageData> s_built;
        private static Dictionary<string, PackedImage> s_next;
        private static int s_uploadIndex;
        private static CancellationTokenSource s_cts;
        private static bool s_building;
        private static bool s_dirty = true;
        private static bool s_inSong;
        private static bool s_initialized;
        private static string s_lastSignature;

        private static void Initialize()
        {
            s_initialized = true;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            TTVCoreApi.Catalog.EmotesUpdated += _ => MarkDirty();
            TTVCoreApi.Channel.SharedChatChanged += _ => MarkDirty();
            TTVCoreApi.Chat.MessageReceived += DisplayStats.Record;
        }

        /// <summary>作り直しを求める（設定の変更、一覧の更新など）。</summary>
        public static void MarkDirty() => s_dirty = true;

        private static void OnActiveSceneChanged(Scene previous, Scene next)
        {
            s_inSong = next.name == GameScene;
            if (s_inSong && s_building) {
                // 曲が始まったら中断し、メニューに戻ってから作り直す
                s_cts?.Cancel();
                s_dirty = true;
            }
        }

        /// <summary>パックに入っている画像なら、読み込み済みの <see cref="ImageEntry"/> を作る。なければ null。メインスレッドで呼ぶ。</summary>
        public static ImageEntry CreateEntry(ImageKey key)
        {
            if (!s_images.TryGetValue(key.Value, out var p) || p.Page.Texture == null) {
                return null;
            }
            p.Page.Entries++;
            return new ImageEntry {
                Key = key,
                State = ImageState.Ready,
                Sheet = p.Page.Texture,
                Material = p.Page.Material,
                Width = p.Width,
                Height = p.Height,
                FrameUVs = p.UVs,
                Durations = p.Durations,
                TotalDuration = p.Durations.Sum(),
                Page = p.Page,
            };
        }

        /// <summary>メインスレッドで毎フレーム呼ぶ。</summary>
        public static void Update()
        {
            if (!s_initialized) {
                Initialize();
            }
            if (s_inSong) {
                return;
            }
            if (s_built != null) {
                UploadOne();
                return;
            }
            if (s_dirty && !s_building && SceneManager.GetActiveScene().name == MenuScene) {
                TryStartBuild();
            }
        }

        /// <summary>使われなくなった古いページを破棄する。シーンの切り替え時に呼ぶ。</summary>
        public static void DestroyUnusedPages()
        {
            foreach (var page in s_pages.Where(p => p.Retired && p.Entries <= 0).ToList()) {
                if (page.Material != null) {
                    UnityEngine.Object.Destroy(page.Material);
                }
                if (page.Texture != null) {
                    UnityEngine.Object.Destroy(page.Texture);
                }
                s_pages.Remove(page);
            }
        }

        // ================= 作成の開始（メインスレッド） =================

        private static void TryStartBuild()
        {
            var broadcasterId = TTVCoreApi.Channel.Broadcaster?.Id;
            if (string.IsNullOrEmpty(broadcasterId) || !CatalogStore.HasChannel(broadcasterId)) {
                // 配信者の一覧が読み込まれたら EmotesUpdated で呼び直される
                return;
            }
            s_dirty = false;
            var settings = CoreSettings.Instance;
            var pageSize = settings.PackPageSize >= 2048 ? 2048 : 1024;
            var maxPages = (int)(Math.Max(0, settings.PackVramLimitMb) * 1024L * 1024L / ((long)pageSize * pageSize * 4));
            if (maxPages <= 0) {
                Replace(new Dictionary<string, PackedImage>(StringComparer.Ordinal));
                s_lastSignature = null;
                return;
            }
            var keys = CollectKeys(broadcasterId);
            var signature = $"{pageSize}:{maxPages}:" + string.Join("|", keys.Select(k => k.Value));
            if (signature == s_lastSignature) {
                return;
            }
            s_building = true;
            s_cts = new CancellationTokenSource();
            var ct = s_cts.Token;
            _ = Task.Run(async () => {
                try {
                    var pages = await BuildAsync(keys, pageSize, maxPages, ct).ConfigureAwait(false);
                    Utilities.MainThread.Post(() => {
                        s_building = false;
                        if (!ct.IsCancellationRequested) {
                            s_lastSignature = signature;
                            s_built = pages;
                            s_next = new Dictionary<string, PackedImage>(StringComparer.Ordinal);
                            s_uploadIndex = 0;
                        }
                    });
                }
                catch (OperationCanceledException) {
                    Utilities.MainThread.Post(() => s_building = false);
                }
                catch (Exception e) {
                    Plugin.Log.Error($"Pack: failed to build: {e}");
                    Utilities.MainThread.Post(() => s_building = false);
                }
            });
        }

        /// <summary>
        /// パックに入れる画像を優先順に集める。
        /// 1 配信者の Twitch エモート、2 配信者の BTTV / FFZ エモート、3 配信者のバッジ、4 表示の統計で選ばれたもの、
        /// 最後に共有チャットの参加チャンネルの BTTV / FFZ エモートとバッジ。
        /// </summary>
        private static List<ImageKey> CollectKeys(string broadcasterId)
        {
            var channel = CatalogStore.GetChannel(broadcasterId);
            var global = CatalogStore.Global;
            var keys = new List<ImageKey>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Add(ImageKey k)
            {
                if (k.IsValid && seen.Add(k.Value)) {
                    keys.Add(k);
                }
            }

            foreach (var e in channel.TwitchEmotes.Values.OrderBy(e => e.Name, StringComparer.Ordinal)) {
                Add(e.Image);
            }
            foreach (var e in channel.BttvEmotes.Values.Concat(channel.FfzEmotes.Values).OrderBy(e => e.Name, StringComparer.Ordinal)) {
                Add(e.Image);
            }
            foreach (var b in channel.Badges.Values) {
                Add(b.Image);
            }
            foreach (var (provider, name) in DisplayStats.Candidates()) {
                EmoteEntry e = null;
                switch (provider) {
                    case EmoteProvider.Twitch:
                        // Twitch には名前からエモートを引く API がないため、配信者とグローバルの一覧にあるものだけ
                        if (!channel.TwitchEmotes.TryGetValue(name, out e)) {
                            global.TwitchEmotes.TryGetValue(name, out e);
                        }
                        break;
                    case EmoteProvider.Bttv:
                        if (!channel.BttvEmotes.TryGetValue(name, out e)) {
                            global.BttvEmotes.TryGetValue(name, out e);
                        }
                        break;
                    case EmoteProvider.Ffz:
                        if (!channel.FfzEmotes.TryGetValue(name, out e)) {
                            global.FfzEmotes.TryGetValue(name, out e);
                        }
                        break;
                }
                if (e != null) {
                    Add(e.Image);
                }
            }
            var participants = TTVCoreApi.Channel.SharedChat?.Participants;
            if (participants != null) {
                foreach (var p in participants.Where(p => p != null && p.Id != broadcasterId)) {
                    var c = CatalogStore.GetChannel(p.Id);
                    foreach (var e in c.BttvEmotes.Values.Concat(c.FfzEmotes.Values)) {
                        Add(e.Image);
                    }
                    foreach (var b in c.Badges.Values) {
                        Add(b.Image);
                    }
                }
            }
            return keys;
        }

        // ================= 読み込みと配置（別スレッド） =================

        /// <summary>ページへの詰め込み。棚のように左から右、下から上へ並べる。</summary>
        private sealed class Packer
        {
            private readonly int _size;
            private readonly int _maxPages;
            private readonly Func<int> _pagesUsed;
            private readonly Action _usePage;
            private PageData _page;
            private int _x;
            private int _y;
            private int _shelf;

            public readonly List<PageData> Pages = new List<PageData>();

            public Packer(int size, int maxPages, Func<int> pagesUsed, Action usePage)
            {
                _size = size;
                _maxPages = maxPages;
                _pagesUsed = pagesUsed;
                _usePage = usePage;
            }

            /// <summary>置ければ、置いたページと左下の位置を返す。</summary>
            public bool TryPlace(int w, int h, out PageData page, out int x, out int y)
            {
                page = null;
                x = y = 0;
                if (w > _size || h > _size) {
                    return false;
                }
                if (_page != null) {
                    if (_x + w > _size) {
                        _x = 0;
                        _y += _shelf;
                        _shelf = 0;
                    }
                    if (_y + h > _size) {
                        _page = null;
                    }
                }
                if (_page == null) {
                    if (_pagesUsed() >= _maxPages) {
                        return false;
                    }
                    _usePage();
                    _page = new PageData { Size = _size, Pixels = new byte[_size * _size * 4] };
                    this.Pages.Add(_page);
                    _x = _y = _shelf = 0;
                }
                page = _page;
                x = _x;
                y = _y;
                _x += w;
                _shelf = Math.Max(_shelf, h);
                return true;
            }
        }

        private static async Task<List<PageData>> BuildAsync(List<ImageKey> keys, int pageSize, int maxPages, CancellationToken ct)
        {
            var watch = Stopwatch.StartNew();
            var pagesUsed = 0;
            var normal = new Packer(pageSize, maxPages, () => pagesUsed, () => pagesUsed++);
            var ffz = new Packer(pageSize, maxPages, () => pagesUsed, () => pagesUsed++);
            var packed = 0;
            var skipped = 0;
            foreach (var key in keys) {
                ct.ThrowIfCancellationRequested();
                var sheet = await ImageManager.LoadSheetAsync(key, ct).ConfigureAwait(false);
                if (sheet == null || sheet.FramePositions.Count == 0) {
                    skipped++;
                    continue;
                }
                // シートのうち、フレームが並んでいる範囲（余白を含む）だけをページに写す
                var padding = SheetData.FramePadding;
                var usedW = Math.Min(sheet.Width, sheet.FramePositions.Max(p => p.X) + sheet.FrameWidth + padding);
                var usedH = Math.Min(sheet.Height, sheet.FramePositions.Max(p => p.Y) + sheet.FrameHeight + padding);
                var packer = ImageManager.OriginOf(key) == ImageOrigin.Ffz ? ffz : normal;
                if (!packer.TryPlace(usedW, usedH, out var page, out var bx, out var by)) {
                    skipped++;
                    continue;
                }
                for (var row = 0; row < usedH; row++) {
                    Buffer.BlockCopy(sheet.Pixels, row * sheet.Width * 4, page.Pixels, ((by + row) * page.Size + bx) * 4, usedW * 4);
                }
                var uvs = sheet.FramePositions
                    .Select(p => new Rect((float)(bx + p.X) / page.Size, (float)(by + p.Y) / page.Size,
                                          (float)sheet.FrameWidth / page.Size, (float)sheet.FrameHeight / page.Size))
                    .ToArray();
                page.Items.Add((key, uvs, sheet.Durations.ToArray(), sheet.FrameWidth, sheet.FrameHeight));
                packed++;
            }
            var pages = normal.Pages.Concat(ffz.Pages).Where(p => p.Items.Count > 0).ToList();
            Plugin.Log.Info($"Pack: {packed} image(s) in {pages.Count} page(s) of {pageSize}px, {skipped} skipped, " +
                            $"{keys.Count} candidate(s), built in {watch.ElapsedMilliseconds} ms.");
            return pages;
        }

        // ================= GPU への転送（メインスレッド） =================

        private static void UploadOne()
        {
            if (s_uploadIndex >= s_built.Count) {
                Replace(s_next);
                s_built = null;
                s_next = null;
                return;
            }
            var baseMaterial = ImageManager.GetBaseMaterial();
            if (baseMaterial == null) {
                // UI のマテリアルが読み込まれるまで待つ
                return;
            }
            var data = s_built[s_uploadIndex++];
            try {
                var watch = Stopwatch.StartNew();
                var texture = new Texture2D(data.Size, data.Size, TextureFormat.RGBA32, false) {
                    name = $"TTVCore Pack {s_pages.Count}",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                texture.LoadRawTextureData(data.Pixels);
                texture.Apply(false, true);
                var page = new PackPage { Texture = texture, Material = new Material(baseMaterial) { mainTexture = texture, name = texture.name } };
                s_pages.Add(page);
                foreach (var item in data.Items) {
                    s_next[item.Key.Value] = new PackedImage { Page = page, UVs = item.UVs, Durations = item.Durations, Width = item.Width, Height = item.Height };
                }
                Plugin.Log.Info($"Pack: uploaded a {data.Size}px page with {data.Items.Count} image(s) in {watch.Elapsed.TotalMilliseconds:F1} ms.");
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Pack: failed to create a page ({e.GetType().Name}: {e.Message}).");
            }
            data.Pixels = null;
        }

        /// <summary>新しいパックに切り替える。古いページは使われなくなってから破棄する。</summary>
        private static void Replace(Dictionary<string, PackedImage> next)
        {
            var current = new HashSet<PackPage>(next.Values.Select(p => p.Page));
            foreach (var page in s_pages) {
                if (!current.Contains(page)) {
                    page.Retired = true;
                }
            }
            s_images = next;
        }
    }
}
