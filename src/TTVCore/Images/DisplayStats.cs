using IPA.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TTVCore.Api.V1;
using TTVCore.Configuration;
using UnityEngine;

namespace TTVCore.Images
{
    /// <summary>
    /// 表示の統計。セッションごとに、メッセージに出たエモートの画像キー（種類と ID）と、
    /// そのエモートを含むメッセージの数、使った人数を数える。視聴者の情報は保存しない（人数を数えるためにメモリ上でだけ使う）。
    /// 直近 N 回のセッション分を UserData\TTVCore\stats.json に残す。
    /// メッセージを一定数以上受け取ったゲームの起動を1回のセッションとする。
    /// </summary>
    internal static class DisplayStats
    {
        private const string FileName = "stats.json";
        private const int FileVersion = 2;

        private sealed class EmoteCount
        {
            /// <summary>このエモートを含むメッセージの数。</summary>
            [JsonProperty("m")]
            public int Messages { get; set; }

            /// <summary>このエモートを使った人数。</summary>
            [JsonProperty("u")]
            public int Users { get; set; }
        }

        private sealed class Session
        {
            /// <summary>受け取ったメッセージの数。</summary>
            [JsonProperty("messages")]
            public int Messages { get; set; }

            [JsonProperty("emotes")]
            public Dictionary<string, EmoteCount> Emotes { get; set; } = new Dictionary<string, EmoteCount>();
        }

        private sealed class StatsFile
        {
            [JsonProperty("version")]
            public int Version { get; set; }

            [JsonProperty("sessions")]
            public List<Session> Sessions { get; set; } = new List<Session>();
        }

        private sealed class CurrentEmote
        {
            public int Messages;
            public readonly HashSet<string> Users = new HashSet<string>(StringComparer.Ordinal);
        }

        // 今回のセッション（メインスレッドでだけ触る）
        private static readonly Dictionary<string, CurrentEmote> s_current = new Dictionary<string, CurrentEmote>(StringComparer.Ordinal);
        private static int s_currentMessages;
        private static bool s_changed;
        private static float s_lastSave;

        // 前回までのセッション。起動時に読み、以後は変えない
        private static List<Session> s_past = new List<Session>();

        private static readonly object s_writeLock = new object();
        private static int s_serial;
        private static int s_written;

        private static string FilePath => Path.Combine(UnityGame.UserDataPath, "TTVCore", FileName);

        public static void Load()
        {
            s_past = new List<Session>();
            try {
                if (!File.Exists(FilePath)) {
                    return;
                }
                var file = JsonConvert.DeserializeObject<StatsFile>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (file == null || file.Version != FileVersion) {
                    // 名前で記録していた以前の形式は ID に変換できないため使わない
                    Plugin.Log.Info($"Stats: {FileName} is in an old format and is not used.");
                    return;
                }
                s_past = file.Sessions?.Where(s => s?.Emotes != null).ToList() ?? new List<Session>();
            }
            catch (Exception e) {
                Plugin.Log.Warn($"Stats: failed to read {FileName} ({e.GetType().Name}: {e.Message}).");
                s_past = new List<Session>();
            }
        }

        /// <summary>メッセージに出たエモートを数える。メインスレッドで呼ぶ。</summary>
        public static void Record(ChatMessage message)
        {
            if (message == null) {
                return;
            }
            s_currentMessages++;
            s_changed = true;
            if (message.Fragments == null) {
                return;
            }
            HashSet<string> seen = null;
            foreach (var f in message.Fragments) {
                ImageKey image;
                if (f is EmoteFragment e) {
                    image = e.Image;
                }
                else if (f is ThirdPartyEmoteFragment t) {
                    image = t.Image;
                }
                else {
                    continue;
                }
                if (!IsEmoteKey(image)) {
                    continue;
                }
                seen = seen ?? new HashSet<string>(StringComparer.Ordinal);
                if (!seen.Add(image.Value)) {
                    // 1つのメッセージで同じエモートは1回だけ数える
                    continue;
                }
                if (!s_current.TryGetValue(image.Value, out var c)) {
                    c = new CurrentEmote();
                    s_current[image.Value] = c;
                }
                c.Messages++;
                var userId = message.Sender?.Id;
                if (!string.IsNullOrEmpty(userId)) {
                    c.Users.Add(userId);
                }
            }
        }

        /// <summary>メニュー画面にいる間、毎フレーム呼ぶ。前回の保存から一定時間たっていれば保存する。</summary>
        public static void SaveIfDue()
        {
            var interval = Math.Max(1, CoreSettings.Instance.StatsSaveIntervalMinutes) * 60f;
            if (!s_changed || Time.realtimeSinceStartup - s_lastSave < interval) {
                return;
            }
            s_lastSave = Time.realtimeSinceStartup;
            var file = Snapshot();
            if (file == null) {
                return;
            }
            var serial = ++s_serial;
            // JSON への変換と書き込みは別スレッドで行う
            _ = Task.Run(() => Write(file, serial));
        }

        /// <summary>保存する。ゲームの終了時に呼ぶ。</summary>
        public static void Save()
        {
            if (!s_changed) {
                return;
            }
            var file = Snapshot();
            if (file != null) {
                Write(file, ++s_serial);
            }
        }

        /// <summary>今回のセッションを加えた保存内容を作る。今回のセッションが短く記録しないなら null。</summary>
        private static StatsFile Snapshot()
        {
            s_changed = false;
            var sessions = new List<Session>(s_past);
            if (s_currentMessages >= Math.Max(1, CoreSettings.Instance.StatsMinMessages) && s_current.Count > 0) {
                sessions.Add(new Session {
                    Messages = s_currentMessages,
                    Emotes = s_current.ToDictionary(
                        kv => kv.Key,
                        kv => new EmoteCount { Messages = kv.Value.Messages, Users = kv.Value.Users.Count },
                        StringComparer.Ordinal),
                });
            }
            else {
                // 加えるものがなければ書かない
                return null;
            }
            var keep = Math.Max(1, CoreSettings.Instance.StatsSessionCount);
            if (sessions.Count > keep) {
                sessions.RemoveRange(0, sessions.Count - keep);
            }
            return new StatsFile { Version = FileVersion, Sessions = sessions };
        }

        /// <summary>書き込む。後から作った内容を先に書いていれば、古い内容では上書きしない。</summary>
        private static void Write(StatsFile file, int serial)
        {
            lock (s_writeLock) {
                if (serial <= s_written) {
                    return;
                }
                s_written = serial;
                try {
                    var json = JsonConvert.SerializeObject(file, Formatting.Indented);
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.WriteAllText(FilePath, json, new UTF8Encoding(false));
                }
                catch (Exception e) {
                    Plugin.Log.Warn($"Stats: failed to save {FileName} ({e.GetType().Name}: {e.Message}).");
                }
            }
        }

        /// <summary>
        /// パックに入れる候補（画像キー）。前回までの直近 N 回のうち、一定の割合（最低2回）以上のセッションに出たもの。
        /// 使った人数を、古いセッションほど軽くして合計した値が大きい順。同じならメッセージの数の合計が多い順。
        /// 今回のセッションは含めない。
        /// </summary>
        public static List<ImageKey> Candidates()
        {
            var settings = CoreSettings.Instance;
            var keep = Math.Max(1, settings.StatsSessionCount);
            var sessions = s_past.Skip(Math.Max(0, s_past.Count - keep)).ToList();
            if (sessions.Count == 0) {
                return new List<ImageKey>();
            }
            var ratio = Mathf.Clamp01(settings.StatsMinSessionRatio);
            var minSessions = Math.Max(2, (int)Math.Ceiling(sessions.Count * ratio));
            var decay = Mathf.Clamp01(settings.StatsDecay);
            var scores = new Dictionary<string, (int Sessions, double Score, int Messages)>(StringComparer.Ordinal);
            for (var i = 0; i < sessions.Count; i++) {
                // 一番新しいセッションの重みが 1
                var weight = Math.Pow(decay, sessions.Count - 1 - i);
                foreach (var kv in sessions[i].Emotes) {
                    if (kv.Value == null) {
                        continue;
                    }
                    scores.TryGetValue(kv.Key, out var s);
                    scores[kv.Key] = (s.Sessions + 1, s.Score + weight * kv.Value.Users, s.Messages + kv.Value.Messages);
                }
            }
            return scores
                .Where(kv => kv.Value.Sessions >= minSessions)
                .OrderByDescending(kv => kv.Value.Score).ThenByDescending(kv => kv.Value.Messages)
                .Select(kv => new ImageKey(kv.Key))
                .Where(IsEmoteKey)
                .ToList();
        }

        private static bool IsEmoteKey(ImageKey key)
        {
            if (!ImageKeys.TryParse(key, out var kind, out _)) {
                return false;
            }
            return kind == ImageKeys.TwitchEmote || kind == ImageKeys.Bttv || kind == ImageKeys.Ffz;
        }
    }
}
