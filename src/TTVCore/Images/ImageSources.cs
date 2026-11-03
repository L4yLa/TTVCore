using System.Collections.Concurrent;
using TTVCore.Api.V1;
using TTVCore.Catalog;

namespace TTVCore.Images
{
    /// <summary>画像の取得元。キャッシュの扱いが規約で異なる。</summary>
    internal enum ImageOrigin
    {
        Twitch,
        Bttv,
        Ffz,
        Bundled,
    }

    internal sealed class ImageSource
    {
        public string Url;
        public ImageOrigin Origin;
    }

    /// <summary><see cref="ImageKey"/> から取得先を求める。</summary>
    internal static class ImageSources
    {
        private static readonly ConcurrentDictionary<string, ImageSource> s_registered = new ConcurrentDictionary<string, ImageSource>();

        public static void Register(ImageKey key, string url, ImageOrigin origin)
        {
            s_registered[key.Value] = new ImageSource { Url = url, Origin = origin };
        }

        public static ImageSource Resolve(ImageKey key)
        {
            if (s_registered.TryGetValue(key.Value, out var src)) {
                return src;
            }
            if (!ImageKeys.TryParse(key, out var kind, out var parts)) {
                return null;
            }
            if (kind == ImageKeys.TwitchEmote && parts.Length >= 2) {
                return new ImageSource { Url = CatalogStore.TwitchEmoteUrl(parts[0], parts[1] == "a"), Origin = ImageOrigin.Twitch };
            }
            if (kind == ImageKeys.Cheermote && parts.Length >= 2 && int.TryParse(parts[1], out var tier)) {
                var url = CatalogStore.GetCheermoteUrl(parts[0], tier);
                return url == null ? null : new ImageSource { Url = url, Origin = ImageOrigin.Twitch };
            }
            if (kind == ImageKeys.Emoji && parts.Length >= 1) {
                // 同梱の Twemoji。Url はアーカイブ内のファイル名
                return new ImageSource { Url = parts[0], Origin = ImageOrigin.Bundled };
            }
            return null;
        }
    }
}
