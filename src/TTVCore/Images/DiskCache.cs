using IPA.Utilities;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TTVCore.Images
{
    /// <summary>
    /// デコード済みのシートのディスクキャッシュ（UserData\TTVCore\cache\）。
    /// Twitch の画像は規約により 24 時間で消す。FFZ の画像と同梱の絵文字は保存しない。
    /// </summary>
    internal static class DiskCache
    {
        private static readonly TimeSpan TwitchLifetime = TimeSpan.FromHours(24);
        // BTTV の期限と全体の容量の上限。暫定値
        private static readonly TimeSpan BttvLifetime = TimeSpan.FromDays(7);
        private const long MaxBytes = 256L * 1024 * 1024;

        private static string DirectoryPath => Path.Combine(UnityGame.UserDataPath, "TTVCore", "cache");

        public static bool IsCacheable(ImageOrigin origin) => origin == ImageOrigin.Twitch || origin == ImageOrigin.Bttv;

        private static TimeSpan Lifetime(ImageOrigin origin) => origin == ImageOrigin.Twitch ? TwitchLifetime : BttvLifetime;

        private static string PathFor(string key, ImageOrigin origin)
        {
            using (var sha = SHA1.Create()) {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                var name = string.Concat(hash.Select(b => b.ToString("x2")));
                return Path.Combine(DirectoryPath, (origin == ImageOrigin.Twitch ? "t-" : "b-") + name + ".sheet");
            }
        }

        public static SheetData TryLoad(string key, ImageOrigin origin)
        {
            if (!IsCacheable(origin)) {
                return null;
            }
            var path = PathFor(key, origin);
            try {
                var info = new FileInfo(path);
                if (!info.Exists) {
                    return null;
                }
                if (DateTime.UtcNow - info.CreationTimeUtc > Lifetime(origin)) {
                    info.Delete();
                    return null;
                }
                using (var stream = info.OpenRead()) {
                    var sheet = SheetData.Read(stream);
                    if (sheet == null) {
                        info.Delete();
                    }
                    else {
                        File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
                    }
                    return sheet;
                }
            }
            catch (Exception) {
                return null;
            }
        }

        public static void Save(string key, ImageOrigin origin, SheetData sheet)
        {
            if (!IsCacheable(origin)) {
                return;
            }
            var path = PathFor(key, origin);
            var tmp = path + ".tmp";
            try {
                Directory.CreateDirectory(DirectoryPath);
                using (var stream = File.Create(tmp)) {
                    sheet.Write(stream);
                }
                if (File.Exists(path)) {
                    File.Delete(path);
                }
                File.Move(tmp, path);
                File.SetCreationTimeUtc(path, DateTime.UtcNow);
            }
            catch (Exception e) {
                Plugin.Log.Debug($"ImageCache: failed to save ({e.GetType().Name}: {e.Message}).");
                try {
                    File.Delete(tmp);
                }
                catch (Exception) {
                }
            }
        }

        /// <summary>期限切れのものを消し、容量の上限を超えていれば古く使われていないものから消す。</summary>
        public static void Purge()
        {
            try {
                var dir = new DirectoryInfo(DirectoryPath);
                if (!dir.Exists) {
                    return;
                }
                var now = DateTime.UtcNow;
                var files = dir.GetFiles("*.sheet").ToList();
                foreach (var f in files.ToList()) {
                    var lifetime = f.Name.StartsWith("t-", StringComparison.Ordinal) ? TwitchLifetime : BttvLifetime;
                    if (now - f.CreationTimeUtc > lifetime) {
                        f.Delete();
                        files.Remove(f);
                    }
                }
                foreach (var tmp in dir.GetFiles("*.tmp")) {
                    tmp.Delete();
                }
                var total = files.Sum(f => f.Length);
                foreach (var f in files.OrderBy(f => f.LastAccessTimeUtc)) {
                    if (total <= MaxBytes) {
                        break;
                    }
                    total -= f.Length;
                    f.Delete();
                }
            }
            catch (Exception e) {
                Plugin.Log.Warn($"ImageCache: purge failed ({e.GetType().Name}: {e.Message}).");
            }
        }
    }
}
