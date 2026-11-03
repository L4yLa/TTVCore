using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TTVCore.Api.V1;
using TTVCore.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TTVCore.Images
{
    /// <summary>読み込んだ画像1つ分。テクスチャとマテリアルは同じ画像のすべての参照で共有する。</summary>
    internal sealed class ImageEntry
    {
        public ImageKey Key;
        public ImageState State = ImageState.Loading;
        public Texture2D Sheet;
        public Material Material;
        public int Width;
        public int Height;
        public Rect[] FrameUVs = Array.Empty<Rect>();
        public float[] Durations = Array.Empty<float>();
        public float TotalDuration;
        public int RefCount;
        public float LastReleasedAt;
        public SheetData PendingUpload;
        /// <summary>パックのページの画像なら、そのページ。テクスチャとマテリアルはページのもので、この画像では破棄しない。</summary>
        public PackPage Page;
        public event Action<ImageEntry> StateChanged;

        public bool IsAnimated => this.FrameUVs.Length > 1;

        /// <summary>共通の時計での現在のフレーム。</summary>
        public int CurrentFrame
        {
            get {
                if (this.FrameUVs.Length <= 1 || this.TotalDuration <= 0f) {
                    return 0;
                }
                var t = (float)(ImageManager.ClockSeconds % this.TotalDuration);
                for (var i = 0; i < this.Durations.Length; i++) {
                    t -= this.Durations[i];
                    if (t < 0f) {
                        return i;
                    }
                }
                return this.Durations.Length - 1;
            }
        }

        public void RaiseStateChanged() => this.StateChanged?.Invoke(this);
    }

    /// <summary>
    /// 画像の読み込み、GPU への転送、解放。デコードは別スレッドで行い、転送はメインスレッドで1フレームに1枚まで行う。
    /// 参照がなくなった画像は、シーンの切り替え時にまとめて破棄する。
    /// </summary>
    internal static class ImageManager
    {
        // 参照のない画像を破棄せずに残しておく数。暫定値
        private const int UnusedKeep = 128;
        // 1フレームに GPU へ送るシートの数。暫定値
        private const int UploadsPerFrame = 1;

        private static readonly Stopwatch s_clock = Stopwatch.StartNew();
        private static readonly Dictionary<string, ImageEntry> s_entries = new Dictionary<string, ImageEntry>(StringComparer.Ordinal);
        private static readonly ConcurrentQueue<ImageEntry> s_uploads = new ConcurrentQueue<ImageEntry>();
        private static readonly SemaphoreSlim s_downloads = new SemaphoreSlim(4, 4);
        private static Material s_baseMaterial;
        private static CancellationToken s_ct;

        public static double ClockSeconds => s_clock.Elapsed.TotalSeconds;

        /// <summary>破棄される直前に呼ばれる（TMP のスプライトアセットの枠を返すため）。</summary>
        public static event Action<ImageEntry> EntryDestroying;

        public static void Initialize(CancellationToken ct)
        {
            s_ct = ct;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            _ = Task.Run(DiskCache.Purge);
        }

        /// <summary>メインスレッドで呼ぶ。参照カウントを増やして返す。</summary>
        public static ImageEntry Acquire(ImageKey key)
        {
            if (!key.IsValid) {
                return null;
            }
            if (!s_entries.TryGetValue(key.Value, out var entry)) {
                entry = PackStore.CreateEntry(key);
                if (entry == null) {
                    entry = new ImageEntry { Key = key };
                    var ct = s_ct;
                    _ = Task.Run(() => LoadAsync(entry, ct));
                }
                s_entries[key.Value] = entry;
            }
            entry.RefCount++;
            return entry;
        }

        public static void Release(ImageEntry entry)
        {
            if (entry == null || entry.RefCount <= 0) {
                return;
            }
            entry.RefCount--;
            if (entry.RefCount == 0) {
                entry.LastReleasedAt = Time.realtimeSinceStartup;
            }
        }

        private static async Task LoadAsync(ImageEntry entry, CancellationToken ct)
        {
            SheetData sheet;
            try {
                sheet = await LoadSheetAsync(entry.Key, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                return;
            }
            entry.PendingUpload = sheet;
            s_uploads.Enqueue(entry);
        }

        /// <summary>
        /// 画像をディスクキャッシュかダウンロードで読み、全フレームを並べた画素にする。別スレッドで呼ぶ。
        /// 読めなければ null。取り消されたときは <see cref="OperationCanceledException"/>。
        /// </summary>
        public static async Task<SheetData> LoadSheetAsync(ImageKey key, CancellationToken ct)
        {
            try {
                var source = ImageSources.Resolve(key);
                if (source == null) {
                    Plugin.Log.Debug($"Images: no source for {key}.");
                    return null;
                }
                var sheet = DiskCache.TryLoad(key.Value, source.Origin);
                if (sheet != null) {
                    return sheet;
                }
                byte[] data;
                if (source.Origin == ImageOrigin.Bundled) {
                    data = EmojiImages.Read(source.Url);
                }
                else {
                    await s_downloads.WaitAsync(ct).ConfigureAwait(false);
                    try {
                        data = await PublicHttp.GetBytesAsync(source.Url, ct).ConfigureAwait(false);
                    }
                    finally {
                        s_downloads.Release();
                    }
                }
                if (data == null) {
                    return null;
                }
                sheet = SheetData.Build(ImageDecoder.Decode(data));
                DiskCache.Save(key.Value, source.Origin, sheet);
                return sheet;
            }
            // 取り消し以外の OperationCanceledException（時間切れなど）は失敗として扱う。
            // 投げ直すと読み込み中のまま残り、表示も解放もされなくなる
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                throw;
            }
            catch (Exception e) {
                Plugin.Log.Debug($"Images: failed to load {key} ({e.GetType().Name}: {e.Message}).");
                return null;
            }
        }

        /// <summary>画像の取得元の種類。分からなければ null。</summary>
        public static ImageOrigin? OriginOf(ImageKey key) => ImageSources.Resolve(key)?.Origin;

        /// <summary>メインスレッドで毎フレーム呼ぶ。</summary>
        public static void Update()
        {
            PackStore.Update();
            for (var i = 0; i < UploadsPerFrame; i++) {
                if (!s_uploads.TryPeek(out var entry)) {
                    return;
                }
                if (entry.PendingUpload != null && GetBaseMaterial() == null) {
                    // UI のマテリアルが読み込まれるまで待つ
                    return;
                }
                s_uploads.TryDequeue(out entry);
                Upload(entry);
            }
        }

        private static void Upload(ImageEntry entry)
        {
            var sheet = entry.PendingUpload;
            entry.PendingUpload = null;
            if (sheet == null) {
                entry.State = ImageState.Failed;
                entry.RaiseStateChanged();
                return;
            }
            try {
                var texture = new Texture2D(sheet.Width, sheet.Height, TextureFormat.RGBA32, false) {
                    name = "TTVCore " + entry.Key.Value,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                texture.LoadRawTextureData(sheet.Pixels);
                texture.Apply(false, true);
                entry.Sheet = texture;
                entry.Material = new Material(GetBaseMaterial()) { mainTexture = texture };
                entry.Width = sheet.FrameWidth;
                entry.Height = sheet.FrameHeight;
                entry.FrameUVs = sheet.FramePositions
                    .Select(p => new Rect((float)p.X / sheet.Width, (float)p.Y / sheet.Height,
                                          (float)sheet.FrameWidth / sheet.Width, (float)sheet.FrameHeight / sheet.Height))
                    .ToArray();
                entry.Durations = sheet.Durations.ToArray();
                entry.TotalDuration = entry.Durations.Sum();
                entry.State = ImageState.Ready;
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Images: failed to create the texture for {entry.Key} ({e.GetType().Name}: {e.Message}).");
                entry.State = ImageState.Failed;
            }
            entry.RaiseStateChanged();
        }

        /// <summary>ブルームで光らないよう、ゲームの UI 用マテリアル UINoGlow を元にする。</summary>
        internal static Material GetBaseMaterial()
        {
            if (s_baseMaterial == null) {
                s_baseMaterial = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m.name == "UINoGlow");
            }
            return s_baseMaterial;
        }

        private static void OnActiveSceneChanged(Scene previous, Scene next)
        {
            var unused = s_entries.Values
                .Where(e => e.RefCount == 0 && e.State != ImageState.Loading)
                .OrderByDescending(e => e.LastReleasedAt)
                .Skip(UnusedKeep)
                .ToList();
            foreach (var e in unused) {
                Destroy(e);
            }
            PackStore.DestroyUnusedPages();
            if (next.name == "MainMenu") {
                _ = Task.Run(DiskCache.Purge);
            }
        }

        private static void Destroy(ImageEntry entry)
        {
            try {
                EntryDestroying?.Invoke(entry);
            }
            catch (Exception e) {
                Plugin.Log.Error($"Images: cleanup failed: {e}");
            }
            s_entries.Remove(entry.Key.Value);
            if (entry.Page != null) {
                entry.Page.Entries--;
            }
            else {
                if (entry.Material != null) {
                    UnityEngine.Object.Destroy(entry.Material);
                }
                if (entry.Sheet != null) {
                    UnityEngine.Object.Destroy(entry.Sheet);
                }
            }
            entry.Page = null;
            entry.Material = null;
            entry.Sheet = null;
        }
    }
}
