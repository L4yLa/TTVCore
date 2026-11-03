using System;
using System.Collections.Generic;
using TMPro;
using TTVCore.Tmp;

namespace TTVCore.Api.V1
{
    /// <summary>TMP に表示する内容（文字と画像の並び）を組み立てる。</summary>
    public sealed class TmpContentBuilder
    {
        private readonly List<TmpSegment> _segments = new List<TmpSegment>();

        internal TmpContentBuilder()
        {
        }

        /// <summary>視聴者の文字列など。TMP のタグとして解釈されないよう TTVCore がエスケープする。</summary>
        public TmpContentBuilder AppendText(string text)
        {
            if (!string.IsNullOrEmpty(text)) {
                _segments.Add(TmpSegment.ForText(text));
            }
            return this;
        }

        /// <summary>利用側の装飾タグ（名前の色、太字など）。エスケープしない。</summary>
        public TmpContentBuilder AppendRichText(string richText)
        {
            if (!string.IsNullOrEmpty(richText)) {
                _segments.Add(TmpSegment.ForRichText(richText));
            }
            return this;
        }

        /// <summary>画像。読み込み中と失敗時は <paramref name="fallbackText"/> を表示する。<paramref name="scale"/> は文字の大きさに対する倍率。</summary>
        public TmpContentBuilder AppendImage(ImageKey image, string fallbackText, float scale = 1f)
        {
            _segments.Add(image.IsValid ? TmpSegment.ForImage(image, fallbackText, scale) : TmpSegment.ForText(fallbackText ?? ""));
            return this;
        }

        /// <summary>画像を持つ部品は画像、ほかは文字として加える。</summary>
        public TmpContentBuilder AppendFragment(ChatFragment fragment, float scale = 1f)
        {
            if (fragment == null) {
                return this;
            }
            if (fragment is EmoteFragment e) {
                return this.AppendImage(e.Image, e.Text, scale);
            }
            if (fragment is ThirdPartyEmoteFragment t) {
                return this.AppendImage(t.Image, t.Text, scale);
            }
            if (fragment is EmojiFragment em) {
                return this.AppendImage(em.Image, em.Text, scale);
            }
            if (fragment is CheermoteFragment c) {
                return this.AppendImage(c.Image, c.Text, scale);
            }
            return this.AppendText(fragment.Text);
        }

        public TmpContentBuilder AppendFragments(IReadOnlyList<ChatFragment> fragments, float scale = 1f)
        {
            if (fragments != null) {
                foreach (var f in fragments) {
                    this.AppendFragment(f, scale);
                }
            }
            return this;
        }

        public TmpContent Build() => new TmpContent(_segments.ToArray());
    }

    /// <summary>組み立てた内容。変更できない。同じ内容を複数のテキストに使ってよい。</summary>
    public sealed class TmpContent
    {
        internal IReadOnlyList<TmpSegment> Segments { get; }

        internal TmpContent(TmpSegment[] segments)
        {
            this.Segments = segments;
        }
    }

    /// <summary><see cref="TextMeshProUGUI"/> と内容の結び付け。</summary>
    public sealed class TmpBinding : IDisposable
    {
        internal TmpBindingCore Core { get; }

        public TextMeshProUGUI Text => this.Core.Text;

        /// <summary>内容に含まれる画像がすべて読み込まれたか（失敗したものを含む）。</summary>
        public bool AllImagesReady => this.Core.AllImagesReady;

        /// <summary>読み込みが終わった画像に差し替えて、テキストを作り直した。行数が変わることがある。</summary>
        public event Action<TmpBinding> Updated;

        internal TmpBinding(TmpBindingCore core)
        {
            this.Core = core;
            core.Owner = this;
        }

        internal void RaiseUpdated() => Utilities.EventInvoker.Invoke(this.Updated, this, "TmpBinding.Updated");

        /// <summary>画像の参照を返し、アニメーションを止める。</summary>
        public void Dispose() => this.Core.Dispose();
    }
}
