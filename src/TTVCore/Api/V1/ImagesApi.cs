using System;
using System.Collections.Generic;
using TMPro;
using TTVCore.Images;
using TTVCore.Tmp;
using UnityEngine;

namespace TTVCore.Api.V1
{
    public enum ImageState
    {
        Unknown = 0,
        Loading,
        Ready,
        Failed,
    }

    /// <summary>
    /// 画像の取得とアニメーション。メソッドはメインスレッドから呼ぶこと。
    /// 画像は TTVCore が読み込み・キャッシュ・解放する。利用側は要求して使い、終わったら Dispose する。
    /// </summary>
    public sealed class ImagesApi
    {
        internal ImagesApi()
        {
        }

        /// <summary>参照を取る。読み込み中でもすぐ返る。終わったら <see cref="ImageHandle.Dispose"/> する。</summary>
        public ImageHandle Acquire(ImageKey key) => new ImageHandle(key, ImageManager.Acquire(key));

        /// <summary>TMP に埋め込む内容を組み立てる。</summary>
        public TmpContentBuilder CreateTmpContent() => new TmpContentBuilder();

        /// <summary>
        /// 内容を <paramref name="text"/> に表示する。結び付けている間、利用側は text.text を直接変えないこと。
        /// 内容を変えるときは、返された <see cref="TmpBinding"/> を Dispose してから、新しい内容で結び付け直す。
        /// </summary>
        public TmpBinding Bind(TextMeshProUGUI text, TmpContent content) => TmpBindings.Bind(text, content);
    }

    /// <summary>画像への参照。同じ画像のテクスチャとマテリアルは、すべての参照で共有される。</summary>
    public sealed class ImageHandle : IDisposable
    {
        private ImageEntry _entry;

        public ImageKey Key { get; }

        public ImageState State => _entry?.State ?? ImageState.Failed;

        /// <summary>読み込みが終わった（Ready / Failed になった）。</summary>
        public event Action<ImageHandle> StateChanged;

        /// <summary>1フレームの大きさ（px）。</summary>
        public int Width => _entry?.Width ?? 0;
        public int Height => _entry?.Height ?? 0;
        public bool IsAnimated => _entry?.IsAnimated ?? false;

        /// <summary>全フレームを含むテクスチャ。他の画像を含むことがあるため、全体を自分の用途に使わないこと。</summary>
        public Texture2D Sheet => _entry?.Sheet;

        /// <summary>この画像の描画に使う共有のマテリアル。変更しないこと。</summary>
        public Material SharedMaterial => _entry?.Material;

        /// <summary>各フレームの <see cref="Sheet"/> 上の位置（UV）。</summary>
        public IReadOnlyList<Rect> FrameUVs => _entry?.FrameUVs ?? Array.Empty<Rect>();

        /// <summary>各フレームの表示時間（秒）。</summary>
        public IReadOnlyList<float> FrameDurations => _entry?.Durations ?? Array.Empty<float>();

        /// <summary>TTVCore の共通の時計による現在のフレーム。同じ画像はすべて同期して動く。</summary>
        public int CurrentFrame => _entry?.CurrentFrame ?? 0;

        public Rect CurrentUV
        {
            get {
                var uvs = this.FrameUVs;
                return uvs.Count == 0 ? default(Rect) : uvs[Math.Min(this.CurrentFrame, uvs.Count - 1)];
            }
        }

        internal ImageEntry Entry => _entry;

        internal ImageHandle(ImageKey key, ImageEntry entry)
        {
            this.Key = key;
            _entry = entry;
            if (entry != null && entry.State == ImageState.Loading) {
                entry.StateChanged += this.OnEntryStateChanged;
            }
        }

        private void OnEntryStateChanged(ImageEntry entry)
        {
            entry.StateChanged -= this.OnEntryStateChanged;
            Utilities.EventInvoker.Invoke(this.StateChanged, this, "ImageHandle.StateChanged");
        }

        /// <summary>参照を返す。実際の破棄はシーンの切り替え時にまとめて行う。</summary>
        public void Dispose()
        {
            var entry = _entry;
            if (entry == null) {
                return;
            }
            _entry = null;
            entry.StateChanged -= this.OnEntryStateChanged;
            ImageManager.Release(entry);
        }
    }
}
