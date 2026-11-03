using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using TTVCore.Images;
using UnityEngine;
using UnityEngine.TextCore;

namespace TTVCore.Tmp
{
    /// <summary>
    /// 画像ごとの TMP_SpriteAsset。MaterialReferenceManager に登録したアセットは解除できないため、
    /// ttv-tex-0、ttv-tex-1 … という枠を使い回し、中身（テクスチャ、グリフ、マテリアル）を差し替える。
    /// </summary>
    internal static class SpriteSlots
    {
        private const string Prefix = "ttv-tex-";

        private sealed class Slot
        {
            public string Name;
            public TMP_SpriteAsset Asset;
            public ImageEntry Entry;
        }

        private static readonly Dictionary<ImageEntry, Slot> s_byEntry = new Dictionary<ImageEntry, Slot>();
        private static readonly Stack<Slot> s_free = new Stack<Slot>();
        private static int s_count;
        private static bool s_hooked;

        /// <summary>画像のアセット名を返す（なければ作る）。メインスレッドで呼ぶ。</summary>
        public static string GetAssetName(ImageEntry entry)
        {
            if (!s_hooked) {
                ImageManager.EntryDestroying += Free;
                s_hooked = true;
            }
            if (s_byEntry.TryGetValue(entry, out var slot)) {
                return slot.Name;
            }
            if (entry.Sheet == null || entry.Material == null) {
                return null;
            }
            slot = s_free.Count > 0 ? s_free.Pop() : CreateSlot();
            Fill(slot.Asset, entry);
            slot.Entry = entry;
            s_byEntry[entry] = slot;
            return slot.Name;
        }

        private static Slot CreateSlot()
        {
            var name = Prefix + s_count++;
            var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            asset.name = name;
            asset.hashCode = TMP_TextUtilities.GetSimpleHashCode(name);
            // 古い形式として変換し直されないよう、現行の版を示す
            typeof(TMP_SpriteAsset).GetField("m_Version", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(asset, "1.1.0");
            foreach (var hash in NameHashes(name)) {
                MaterialReferenceManager.AddSpriteAsset(hash, asset);
            }
            return new Slot { Name = name, Asset = asset };
        }

        /// <summary>
        /// &lt;sprite="名前"&gt; の名前から TMP が求めるハッシュ。TMP の版によって大文字小文字の扱いが異なる
        /// （Unity 6 の uGUI 2.0 では区別しない）ため、両版で使われうる求め方のすべてで登録する。
        /// </summary>
        private static IEnumerable<int> NameHashes(string name)
        {
            var hashes = new HashSet<int> { TMP_TextUtilities.GetSimpleHashCode(name) };
            var candidates = new[] {
                typeof(TMP_TextUtilities),
                typeof(TMP_TextUtilities).Assembly.GetType("TMPro.TMP_TextParsingUtilities"),
            };
            foreach (var type in candidates) {
                if (type == null) {
                    continue;
                }
                foreach (var method in new[] { "GetHashCodeCaseInSensitive", "GetSimpleHashCodeLowercase", "GetHashCodeCaseSensitive" }) {
                    var m = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
                    if (m != null && (m.ReturnType == typeof(int) || m.ReturnType == typeof(uint))) {
                        try {
                            var v = m.Invoke(null, new object[] { name });
                            hashes.Add(v is uint u ? unchecked((int)u) : (int)v);
                        }
                        catch (Exception) {
                        }
                    }
                }
            }
#if DEBUG
            Plugin.Log.Notice($"[DEBUG] Tmp: sprite asset {name} hashes = {string.Join(", ", hashes)}");
#endif
            return hashes;
        }

        private static void Fill(TMP_SpriteAsset asset, ImageEntry entry)
        {
            asset.spriteSheet = entry.Sheet;
            asset.material = entry.Material;
            asset.materialHashCode = TMP_TextUtilities.GetSimpleHashCode(entry.Material.name);
            asset.spriteGlyphTable.Clear();
            asset.spriteCharacterTable.Clear();
            var w = entry.Width;
            var h = entry.Height;
            for (var i = 0; i < entry.FrameUVs.Length; i++) {
                var uv = entry.FrameUVs[i];
                var glyph = new TMP_SpriteGlyph {
                    index = (uint)i,
                    // 縦の位置は試作で合わせる。下端を基準線より少し下に置く
                    metrics = new GlyphMetrics(w, h, 0, h * 0.8f, w),
                    glyphRect = new GlyphRect(Mathf.RoundToInt(uv.x * entry.Sheet.width), Mathf.RoundToInt(uv.y * entry.Sheet.height), w, h),
                    scale = 1f,
                    atlasIndex = 0,
                };
                asset.spriteGlyphTable.Add(glyph);
                asset.spriteCharacterTable.Add(new TMP_SpriteCharacter(0, glyph) { name = "f" + i });
            }
            asset.UpdateLookupTables();
        }

        private static void Free(ImageEntry entry)
        {
            if (!s_byEntry.TryGetValue(entry, out var slot)) {
                return;
            }
            s_byEntry.Remove(entry);
            slot.Entry = null;
            slot.Asset.spriteSheet = null;
            slot.Asset.material = null;
            s_free.Push(slot);
        }
    }
}
