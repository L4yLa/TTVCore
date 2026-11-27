using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TTVCore.EventSub;
using TTVCore.Utilities;

namespace TTVCore.Diagnostics
{
    /// <summary>
    /// デバッグ機能のテスト用の通知とチャット。埋め込みの JSON（tools/gen_debug_data.py で作る）を読み、
    /// 置き換えをしてから、受信した EventSub の通知と同じ道（NotificationDispatcher）に流す。
    /// </summary>
    internal static class DebugEvents
    {
        private const string NotificationsResource = "TTVCore.Resources.Debug.notifications.json";
        private const string ChatResource = "TTVCore.Resources.Debug.chat-messages.json";

        // チャットのテストの基準の速さ（1分あたり60件）。倍率を掛ける
        public const double BaseMessagesPerMinute = 60;
        public const int MinMultiplier = 1;
        public const int MaxMultiplier = 10;
        public const int MinChatCount = 1;
        public const int MaxChatCount = 1000;

        private static readonly Regex s_placeholder = new Regex(@"\{\{([a-z_]+)(?::([a-z0-9_]+)|\+(\d+))?\}\}", RegexOptions.Compiled);
        private static readonly System.Random s_random = new System.Random();
        private static readonly object s_gate = new object();

        private static List<NotificationTest> s_notificationTests;
        private static List<Step> s_chatMessages;

        // 直前に流したテストのメッセージ（返信、削除、タイムアウトの相手）
        private static string s_lastMessageId;
        private static string s_lastMessageText;
        private static string s_lastChatterId;
        private static string s_lastChatterLogin;
        private static string s_lastChatterName;

        private static CancellationTokenSource s_cts = new CancellationTokenSource();
        private static CancellationTokenSource s_chatCts;
        private static int s_chatSent;
        private static int s_chatTotal;

        public sealed class NotificationTest
        {
            public string Id;
            public string LabelEn;
            public string LabelJa;
            public List<Step> Steps;

            public string Label => Localization.T(this.LabelEn, this.LabelJa);
        }

        public sealed class Step
        {
            public string Type;
            public string Version;
            public int DelayMs;
            public string EventTemplate;
        }

        /// <summary>チャットのテストの進み具合が変わった（メインスレッドで発生）。</summary>
        public static event Action ChatProgressChanged;

        public static bool ChatRunning
        {
            get {
                lock (s_gate) {
                    return s_chatCts != null;
                }
            }
        }

        public static int ChatSent => s_chatSent;
        public static int ChatTotal => s_chatTotal;

        public static IReadOnlyList<NotificationTest> NotificationTests
        {
            get {
                EnsureLoaded();
                return s_notificationTests;
            }
        }

        private static void EnsureLoaded()
        {
            lock (s_gate) {
                if (s_notificationTests != null) {
                    return;
                }
                s_notificationTests = new List<NotificationTest>();
                s_chatMessages = new List<Step>();
                try {
                    foreach (var t in ReadArray(NotificationsResource)) {
                        s_notificationTests.Add(new NotificationTest {
                            Id = JsonUtil.Str(t["id"]),
                            LabelEn = JsonUtil.Str(t["en"]),
                            LabelJa = JsonUtil.Str(t["ja"]),
                            Steps = (t["steps"] as JArray)?.OfType<JObject>().Select(ToStep).ToList() ?? new List<Step>(),
                        });
                    }
                    foreach (var m in ReadArray(ChatResource)) {
                        s_chatMessages.Add(ToStep(m));
                    }
                }
                catch (Exception e) {
                    Plugin.Log.Error($"Debug mode: failed to read the test data: {e}");
                }
            }
        }

        private static IEnumerable<JObject> ReadArray(string resource)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)) {
                if (stream == null) {
                    throw new InvalidOperationException($"{resource} is not embedded.");
                }
                using (var reader = new StreamReader(stream, Encoding.UTF8)) {
                    var arr = JArray.Parse(reader.ReadToEnd());
                    return arr.OfType<JObject>().ToList();
                }
            }
        }

        private static Step ToStep(JObject s) => new Step {
            Type = JsonUtil.Str(s["type"]),
            Version = JsonUtil.Str(s["version"]) ?? "1",
            DelayMs = JsonUtil.Int(s["delay"]),
            // 置き換えは文字列で行うため、テンプレートは JSON の文字列のまま持つ
            EventTemplate = (s["event"] as JObject)?.ToString(Formatting.None) ?? "{}",
        };

        // ================= 通知のテスト =================

        /// <summary>通知のテストを1回流す。届く順と間隔は、本物の Twitch に合わせてある。</summary>
        public static void RunNotification(string id)
        {
            if (!DebugMode.Active) {
                return;
            }
            var test = NotificationTests.FirstOrDefault(t => t.Id == id);
            if (test == null) {
                return;
            }
            var ct = s_cts.Token;
            var ids = new Dictionary<string, string>();
            _ = Task.Run(async () => {
                try {
                    foreach (var step in test.Steps) {
                        if (step.DelayMs > 0) {
                            await Task.Delay(step.DelayMs, ct).ConfigureAwait(false);
                        }
                        if (!DebugMode.Active || ct.IsCancellationRequested) {
                            return;
                        }
                        Inject(step, ids);
                    }
                }
                catch (OperationCanceledException) {
                }
                catch (Exception e) {
                    Plugin.Log.Error($"Debug mode: notification test {id} failed: {e}");
                }
            });
        }

        // ================= チャットのテスト =================

        /// <summary>
        /// 100 件からランダムに選んで、count 件を流す。速さは 1分あたり 60 件 × multiplier。
        /// 本物のチャットに近づけるため、間隔は指数分布（ポアソン過程）でばらつかせる。
        /// </summary>
        public static void StartChat(int count, int multiplier)
        {
            if (!DebugMode.Active) {
                return;
            }
            EnsureLoaded();
            count = Math.Max(MinChatCount, Math.Min(MaxChatCount, count));
            multiplier = Math.Max(MinMultiplier, Math.Min(MaxMultiplier, multiplier));
            CancellationToken ct;
            lock (s_gate) {
                if (s_chatCts != null || s_chatMessages.Count == 0) {
                    return;
                }
                s_chatCts = CancellationTokenSource.CreateLinkedTokenSource(s_cts.Token);
                ct = s_chatCts.Token;
                s_chatSent = 0;
                s_chatTotal = count;
            }
            RaiseProgress();
            var meanSeconds = 60.0 / (BaseMessagesPerMinute * multiplier);
            _ = Task.Run(async () => {
                try {
                    for (var i = 0; i < count; i++) {
                        double u;
                        lock (s_random) {
                            u = s_random.NextDouble();
                        }
                        // 間隔が 0 にならないよう、平均の 1/20 を下限にする
                        var seconds = Math.Max(meanSeconds / 20, -Math.Log(1 - u) * meanSeconds);
                        await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);
                        if (!DebugMode.Active) {
                            return;
                        }
                        Inject(PickChatMessage(), new Dictionary<string, string>());
                        Interlocked.Increment(ref s_chatSent);
                        RaiseProgress();
                    }
                }
                catch (OperationCanceledException) {
                }
                catch (Exception e) {
                    Plugin.Log.Error($"Debug mode: chat test failed: {e}");
                }
                finally {
                    lock (s_gate) {
                        s_chatCts?.Dispose();
                        s_chatCts = null;
                    }
                    RaiseProgress();
                }
            });
        }

        public static void StopChat()
        {
            lock (s_gate) {
                s_chatCts?.Cancel();
            }
        }

        /// <summary>流しているテストをすべて止める（デバッグ機能を解除したとき）。</summary>
        public static void StopAll()
        {
            CancellationTokenSource old;
            lock (s_gate) {
                old = s_cts;
                s_cts = new CancellationTokenSource();
            }
            old.Cancel();
            old.Dispose();
        }

        /// <summary>削除・タイムアウト・返信は、まだメッセージを流していなければ選ばない。</summary>
        private static Step PickChatMessage()
        {
            lock (s_random) {
                while (true) {
                    var step = s_chatMessages[s_random.Next(s_chatMessages.Count)];
                    if (s_lastMessageId != null || !step.EventTemplate.Contains("{{last_")) {
                        return step;
                    }
                }
            }
        }

        private static void RaiseProgress() => MainThread.Post(() => ChatProgressChanged?.Invoke());

        // ================= 流す =================

        /// <summary>置き換えをして、受信した通知（WebSocket の notification）と同じ形にして流す。</summary>
        private static void Inject(Step step, Dictionary<string, string> ids)
        {
            var broadcaster = DebugMode.Broadcaster;
            var now = DateTimeOffset.UtcNow;
            var messageId = Guid.NewGuid().ToString();
            var json = s_placeholder.Replace(step.EventTemplate, m => {
                var name = m.Groups[1].Value;
                string value;
                switch (name) {
                    case "broadcaster_id": value = broadcaster.Id; break;
                    case "broadcaster_login": value = broadcaster.Login; break;
                    case "broadcaster_name": value = broadcaster.DisplayName; break;
                    case "message_id": value = messageId; break;
                    case "last_message_id": value = s_lastMessageId ?? ""; break;
                    case "last_message_text": value = s_lastMessageText ?? ""; break;
                    case "last_chatter_id": value = s_lastChatterId ?? ""; break;
                    case "last_chatter_login": value = s_lastChatterLogin ?? ""; break;
                    case "last_chatter_name": value = s_lastChatterName ?? ""; break;
                    case "id": {
                        var key = m.Groups[2].Value;
                        if (!ids.TryGetValue(key, out value)) {
                            value = Guid.NewGuid().ToString();
                            ids[key] = value;
                        }
                        break;
                    }
                    case "now": {
                        var add = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                        value = now.AddSeconds(add).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
                        break;
                    }
                    default:
                        return m.Value;
                }
                // テンプレートの中では JSON の文字列の中にあるため、文字列としてエスケープする（前後の引用符は除く）
                var escaped = JsonConvert.ToString(value ?? "");
                return escaped.Substring(1, escaped.Length - 2);
            });

            JObject ev;
            try {
                ev = JsonUtil.Parse(json);
            }
            catch (JsonException e) {
                Plugin.Log.Warn($"Debug mode: test data for {step.Type} is not valid JSON ({e.Message}).");
                return;
            }
            if (step.Type == "channel.chat.message") {
                s_lastMessageId = JsonUtil.Str(ev["message_id"]);
                s_lastMessageText = JsonUtil.Str(JsonUtil.At(ev, "message", "text"));
                s_lastChatterId = JsonUtil.Str(ev["chatter_user_id"]);
                s_lastChatterLogin = JsonUtil.Str(ev["chatter_user_login"]);
                s_lastChatterName = JsonUtil.Str(ev["chatter_user_name"]);
            }
            var message = new JObject {
                ["metadata"] = new JObject {
                    ["message_id"] = Guid.NewGuid().ToString(),
                    ["message_type"] = "notification",
                    ["message_timestamp"] = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture),
                    ["subscription_type"] = step.Type,
                    ["subscription_version"] = step.Version,
                },
                ["payload"] = new JObject {
                    ["subscription"] = new JObject { ["type"] = step.Type, ["version"] = step.Version },
                    ["event"] = ev,
                },
            };
            EventSubManager.Instance?.InjectTest(message);
        }
    }
}
