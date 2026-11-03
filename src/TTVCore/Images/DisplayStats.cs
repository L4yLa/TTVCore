using IPA.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TTVCore.Api.V1;
using TTVCore.Configuration;

namespace TTVCore.Images
{
    /// <summary>
    /// 表示の統計。セッションごとに、メッセージに出たエモートの種類と名前、その回数を数える。
    /// エモートの ID と視聴者の情報は記録しない。直近 N 回のセッション分を UserData\TTVCore\stats.json に残す。
    /// 1つ以上数えたゲームの起動を1回のセッションとする。
    /// </summary>
    internal static class DisplayStats
    {
        private const string FileName = "stats.json";

        private sealed class StatsFile
        {
            [JsonProperty("sessions")]
            public List<Dictionary<string, int>> Sessions { get; set; } = new List<Dictionary<string, int>>();
        }

        private static readonly Dictionary<string, int> s_current = new Dictionary<string, int>(StringComparer.Ordinal);
        private static StatsFile s_file = new StatsFile();

        private static string FilePath => Path.Combine(UnityGame.UserDataPath, "TTVCore", FileName);

        public static void Load()
        {
            try {
                if (File.Exists(FilePath)) {
                    s_file = JsonConvert.DeserializeObject<StatsFile>(File.ReadAllText(FilePath, Encoding.UTF8)) ?? new StatsFile();
                }
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Stats: failed to read {FileName} ({e.GetType().Name}: {e.Message}).");
                s_file = new StatsFile();
            }
            s_file.Sessions = s_file.Sessions?.Where(s => s != null).ToList() ?? new List<Dictionary<string, int>>();
        }

        /// <summary>メッセージに出たエモートを数える。メインスレッドで呼ぶ。</summary>
        public static void Record(ChatMessage message)
        {
            if (message?.Fragments == null) {
                return;
            }
            foreach (var f in message.Fragments) {
                string name = null;
                if (f is EmoteFragment) {
                    name = Name(EmoteProvider.Twitch, f.Text);
                }
                else if (f is ThirdPartyEmoteFragment t) {
                    name = Name(t.Provider, f.Text);
                }
                if (name != null) {
                    s_current.TryGetValue(name, out var n);
                    s_current[name] = n + 1;
                }
            }
        }

        /// <summary>今回のセッションを加えて保存する。ゲームの終了時に呼ぶ。</summary>
        public static void Save()
        {
            if (s_current.Count == 0) {
                return;
            }
            s_file.Sessions.Add(new Dictionary<string, int>(s_current));
            var keep = Math.Max(1, CoreSettings.Instance.StatsSessionCount);
            if (s_file.Sessions.Count > keep) {
                s_file.Sessions.RemoveRange(0, s_file.Sessions.Count - keep);
            }
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(s_file, Formatting.Indented), new UTF8Encoding(false));
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Stats: failed to save {FileName} ({e.GetType().Name}: {e.Message}).");
            }
        }

        /// <summary>
        /// パックに入れる候補（種類と名前）。直近 N 回のうち M 回以上のセッションに出たもの。
        /// 出たセッションの数が多い順、同じなら回数の合計が多い順。今回のセッションは含めない。
        /// </summary>
        public static List<(EmoteProvider Provider, string Name)> Candidates()
        {
            var keep = Math.Max(1, CoreSettings.Instance.StatsSessionCount);
            var minSessions = Math.Max(1, CoreSettings.Instance.StatsMinSessions);
            var sessions = s_file.Sessions.Skip(Math.Max(0, s_file.Sessions.Count - keep)).ToList();
            return sessions
                .SelectMany(s => s)
                .GroupBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(g => (Key: g.Key, Sessions: g.Count(), Total: g.Sum(kv => kv.Value)))
                .Where(x => x.Sessions >= minSessions)
                .OrderByDescending(x => x.Sessions).ThenByDescending(x => x.Total)
                .Select(x => Parse(x.Key))
                .Where(x => x.Name != null)
                .ToList();
        }

        private static string Name(EmoteProvider provider, string name)
        {
            if (string.IsNullOrEmpty(name)) {
                return null;
            }
            switch (provider) {
                case EmoteProvider.Twitch: return "tw:" + name;
                case EmoteProvider.Bttv: return "bt:" + name;
                case EmoteProvider.Ffz: return "fz:" + name;
                default: return null;
            }
        }

        private static (EmoteProvider Provider, string Name) Parse(string key)
        {
            if (key.Length < 4 || key[2] != ':') {
                return (EmoteProvider.Unknown, null);
            }
            var name = key.Substring(3);
            switch (key.Substring(0, 2)) {
                case "tw": return (EmoteProvider.Twitch, name);
                case "bt": return (EmoteProvider.Bttv, name);
                case "fz": return (EmoteProvider.Ffz, name);
                default: return (EmoteProvider.Unknown, null);
            }
        }
    }
}
