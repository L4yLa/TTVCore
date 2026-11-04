using IPA.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.IO;
using System.Text;
using TTVCore.Utilities;

namespace TTVCore.Configuration
{
    /// <summary>TTVCore の設定（UserData\TTVCore\settings.json）。</summary>
    internal sealed class CoreSettings
    {
        private const string FileName = "settings.json";

        public static CoreSettings Instance { get; private set; } = new CoreSettings();

        /// <summary>サードパーティの MOD からのチャット送信を許可するか。</summary>
        [JsonProperty("third_party_send_enabled")]
        public bool ThirdPartySendEnabled { get; set; } = true;

        /// <summary>UI の言語。</summary>
        [JsonProperty("language")]
        [JsonConverter(typeof(StringEnumConverter))]
        public UiLanguage Language { get; set; } = UiLanguage.System;

        /// <summary>パック（よく使われる画像をまとめた共有テクスチャ）の VRAM の上限（MB）。0 ならパックを作らない。</summary>
        [JsonProperty("pack_vram_limit_mb")]
        public int PackVramLimitMb { get; set; } = 64;

        /// <summary>パックの1ページの大きさ（px、正方形）。1024 か 2048。</summary>
        [JsonProperty("pack_page_size")]
        public int PackPageSize { get; set; } = 2048;

        /// <summary>表示の統計を残すセッションの数（N）。暫定値。</summary>
        [JsonProperty("stats_session_count")]
        public int StatsSessionCount { get; set; } = 20;

        /// <summary>記録のあるセッションのうち、この割合以上（最低2回）に出たエモートをパックの候補にする。暫定値。</summary>
        [JsonProperty("stats_min_session_ratio")]
        public float StatsMinSessionRatio { get; set; } = 0.3f;

        /// <summary>候補の順位付けで、1回前のセッションごとに掛ける重み（0～1）。暫定値。</summary>
        [JsonProperty("stats_decay")]
        public float StatsDecay { get; set; } = 0.85f;

        /// <summary>受け取ったメッセージがこの数より少ないゲームの起動はセッションとして記録しない。暫定値。</summary>
        [JsonProperty("stats_min_messages")]
        public int StatsMinMessages { get; set; } = 30;

        /// <summary>メニュー画面で統計を保存する間隔（分）。ゲームの終了時にも保存する。暫定値。</summary>
        [JsonProperty("stats_save_interval_minutes")]
        public int StatsSaveIntervalMinutes { get; set; } = 10;

        private static string FilePath => Path.Combine(UnityGame.UserDataPath, "TTVCore", FileName);

        public static void Load()
        {
            var path = FilePath;
            if (!File.Exists(path)) {
                Instance = new CoreSettings();
                Save();
                return;
            }
            try {
                Instance = JsonConvert.DeserializeObject<CoreSettings>(File.ReadAllText(path, Encoding.UTF8)) ?? new CoreSettings();
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Settings: failed to read {FileName} ({e.GetType().Name}: {e.Message}). Defaults are used.");
                Instance = new CoreSettings();
            }
        }

        public static void Save()
        {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(Instance, Formatting.Indented), new UTF8Encoding(false));
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Settings: failed to save {FileName} ({e.GetType().Name}: {e.Message}).");
            }
        }
    }
}
