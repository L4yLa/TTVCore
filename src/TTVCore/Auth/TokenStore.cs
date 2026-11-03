using IPA.Utilities;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;

namespace TTVCore.Auth
{
    /// <summary>
    /// トークンを設定とは別の UserData\TTVCore\auth.json に平文で保存する。
    /// 一時ファイルに書いてから置き換える。内容はログに出さない。
    /// </summary>
    internal static class TokenStore
    {
        private const string FileName = "auth.json";

        private static string DirectoryPath => Path.Combine(UnityGame.UserDataPath, "TTVCore");
        private static string FilePath => Path.Combine(DirectoryPath, FileName);

        public static TokenData Load()
        {
            var path = FilePath;
            if (!File.Exists(path)) {
                return null;
            }
            try {
                return JsonConvert.DeserializeObject<TokenData>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e) {
                Plugin.Log.Warn($"TokenStore: failed to read {FileName} ({e.GetType().Name}). It will be ignored.");
                return null;
            }
        }

        /// <summary>失敗時は例外を投げる。</summary>
        public static void Save(TokenData data)
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = FilePath;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(data, Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(path)) {
                File.Replace(tmp, path, null);
            }
            else {
                File.Move(tmp, path);
            }
        }

        public static void Delete()
        {
            try {
                var path = FilePath;
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            }
            catch (Exception e) {
                Plugin.Log.Warn($"TokenStore: failed to delete {FileName} ({e.GetType().Name}: {e.Message}).");
            }
        }
    }
}
