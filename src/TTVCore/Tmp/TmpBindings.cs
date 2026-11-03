using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using TTVCore.Api.V1;
using TTVCore.Images;
using UnityEngine;

namespace TTVCore.Tmp
{
    internal sealed class TmpSegment
    {
        public enum SegmentKind { Text, RichText, Image }

        public SegmentKind Kind;
        public string Text;
        public ImageKey Image;
        public float Scale = 1f;

        public static TmpSegment ForText(string text) => new TmpSegment { Kind = SegmentKind.Text, Text = text };
        public static TmpSegment ForRichText(string text) => new TmpSegment { Kind = SegmentKind.RichText, Text = text };
        public static TmpSegment ForImage(ImageKey key, string fallback, float scale) =>
            new TmpSegment { Kind = SegmentKind.Image, Image = key, Text = fallback ?? "", Scale = scale };
    }

    /// <summary>1つの <see cref="TextMeshProUGUI"/> と内容の結び付け。</summary>
    internal sealed class TmpBindingCore
    {
        private readonly TmpContent _content;
        private readonly List<ImageEntry> _entries = new List<ImageEntry>();
        // 本文に埋め込んだスプライトの順番に、どの画像かを並べる
        private readonly List<ImageEntry> _sprites = new List<ImageEntry>();
        private int[] _shownFrames = Array.Empty<int>();
        private bool _disposed;

        public TextMeshProUGUI Text { get; }
        public TmpBinding Owner;
        public bool HasAnimation { get; private set; }
        public bool AllImagesReady => _entries.All(e => e.State != ImageState.Loading);
        public bool RebuildRequested;

        public TmpBindingCore(TextMeshProUGUI text, TmpContent content)
        {
            this.Text = text;
            _content = content;
            foreach (var seg in content.Segments.Where(s => s.Kind == TmpSegment.SegmentKind.Image)) {
                var entry = ImageManager.Acquire(seg.Image);
                if (entry == null) {
                    continue;
                }
                _entries.Add(entry);
                if (entry.State == ImageState.Loading) {
                    entry.StateChanged += this.OnEntryStateChanged;
                }
            }
            text.OnPreRenderText += this.OnPreRenderText;
            this.Rebuild();
        }

        private void OnEntryStateChanged(ImageEntry entry)
        {
            entry.StateChanged -= this.OnEntryStateChanged;
            if (!_disposed) {
                TmpBindings.RequestRebuild(this);
            }
        }

        public void Rebuild()
        {
            if (_disposed || this.Text == null) {
                return;
            }
            var sb = new StringBuilder();
            _sprites.Clear();
            var index = 0;
            foreach (var seg in _content.Segments) {
                switch (seg.Kind) {
                    case TmpSegment.SegmentKind.Text:
                        sb.Append(Escape(seg.Text));
                        break;
                    case TmpSegment.SegmentKind.RichText:
                        sb.Append(seg.Text);
                        break;
                    case TmpSegment.SegmentKind.Image: {
                        var entry = index < _entries.Count && _entries[index].Key == seg.Image ? _entries[index] : null;
                        index++;
                        var asset = entry != null && entry.State == ImageState.Ready ? SpriteSlots.GetAssetName(entry) : null;
                        if (asset == null) {
                            sb.Append(Escape(seg.Text));
                            break;
                        }
                        var scaled = Math.Abs(seg.Scale - 1f) > 0.001f;
                        if (scaled) {
                            sb.Append("<size=").Append((seg.Scale * 100f).ToString("0", CultureInfo.InvariantCulture)).Append("%>");
                        }
                        sb.Append("<sprite=\"").Append(asset).Append("\" index=").Append(entry.CurrentFrame.ToString(CultureInfo.InvariantCulture)).Append('>');
                        if (scaled) {
                            sb.Append("</size>");
                        }
                        _sprites.Add(entry);
                        break;
                    }
                }
            }
            this.HasAnimation = _sprites.Any(e => e.IsAnimated);
            _shownFrames = _sprites.Select(e => e.CurrentFrame).ToArray();
            this.Text.text = sb.ToString();
        }

        /// <summary>フレームが切り替わった画像があれば UV を書き換える。メッシュの更新はテキストごとに1回。</summary>
        public void Animate()
        {
            if (!this.HasAnimation || _disposed || this.Text == null || !this.Text.isActiveAndEnabled) {
                return;
            }
            var changed = false;
            for (var i = 0; i < _sprites.Count; i++) {
                var f = _sprites[i].CurrentFrame;
                if (f != _shownFrames[i]) {
                    _shownFrames[i] = f;
                    changed = true;
                }
            }
            if (!changed) {
                return;
            }
            if (this.ApplyUVs(this.Text.textInfo)) {
                this.Text.UpdateVertexData(TMP_VertexDataUpdateFlags.Uv0);
            }
        }

        private void OnPreRenderText(TMP_TextInfo info)
        {
            if (!this.HasAnimation) {
                return;
            }
            for (var i = 0; i < _sprites.Count; i++) {
                _shownFrames[i] = _sprites[i].CurrentFrame;
            }
            this.ApplyUVs(info);
        }

        private bool ApplyUVs(TMP_TextInfo info)
        {
            if (info == null) {
                return false;
            }
            var spriteOrdinal = 0;
            var any = false;
            for (var c = 0; c < info.characterCount && spriteOrdinal < _sprites.Count; c++) {
                var ch = info.characterInfo[c];
                if (ch.elementType != TMP_TextElementType.Sprite) {
                    continue;
                }
                var entry = _sprites[spriteOrdinal];
                var frame = _shownFrames[spriteOrdinal];
                spriteOrdinal++;
                if (!entry.IsAnimated || !ch.isVisible || frame >= entry.FrameUVs.Length) {
                    continue;
                }
                var uv = entry.FrameUVs[frame];
                var uvs = info.meshInfo[ch.materialReferenceIndex].uvs0;
                var v = ch.vertexIndex;
                if (uvs == null || v + 3 >= uvs.Length) {
                    continue;
                }
                uvs[v] = new Vector2(uv.xMin, uv.yMin);
                uvs[v + 1] = new Vector2(uv.xMin, uv.yMax);
                uvs[v + 2] = new Vector2(uv.xMax, uv.yMax);
                uvs[v + 3] = new Vector2(uv.xMax, uv.yMin);
                any = true;
            }
            return any;
        }

        public void Dispose()
        {
            if (_disposed) {
                return;
            }
            _disposed = true;
            if (this.Text != null) {
                this.Text.OnPreRenderText -= this.OnPreRenderText;
            }
            foreach (var e in _entries) {
                e.StateChanged -= this.OnEntryStateChanged;
                ImageManager.Release(e);
            }
            _entries.Clear();
            _sprites.Clear();
            TmpBindings.Remove(this);
        }

        /// <summary>視聴者の文字列の &lt; を TMP のタグとして解釈させない（直後に U+2060 を入れる）。</summary>
        private static string Escape(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<⁠");
    }

    /// <summary>すべての結び付けのアニメーションと作り直しを、毎フレームまとめて行う。</summary>
    internal static class TmpBindings
    {
        // 1フレームに作り直すテキストの数。暫定値
        private const int RebuildsPerFrame = 8;

        private static readonly List<TmpBindingCore> s_all = new List<TmpBindingCore>();
        private static readonly Queue<TmpBindingCore> s_rebuild = new Queue<TmpBindingCore>();

        public static TmpBinding Bind(TextMeshProUGUI text, TmpContent content)
        {
            if (text == null) {
                throw new ArgumentNullException(nameof(text));
            }
            if (content == null) {
                throw new ArgumentNullException(nameof(content));
            }
            var core = new TmpBindingCore(text, content);
            s_all.Add(core);
            return new TmpBinding(core);
        }

        public static void RequestRebuild(TmpBindingCore core)
        {
            if (!core.RebuildRequested) {
                core.RebuildRequested = true;
                s_rebuild.Enqueue(core);
            }
        }

        public static void Remove(TmpBindingCore core) => s_all.Remove(core);

        public static void Update()
        {
            for (var i = 0; i < RebuildsPerFrame && s_rebuild.Count > 0; i++) {
                var core = s_rebuild.Dequeue();
                core.RebuildRequested = false;
                if (!s_all.Contains(core)) {
                    continue;
                }
                core.Rebuild();
                core.Owner?.RaiseUpdated();
            }
            for (var i = s_all.Count - 1; i >= 0; i--) {
                var core = s_all[i];
                if (core.Text == null) {
                    // テキストが破棄された。参照を返す
                    core.Dispose();
                    continue;
                }
                core.Animate();
            }
        }
    }
}
