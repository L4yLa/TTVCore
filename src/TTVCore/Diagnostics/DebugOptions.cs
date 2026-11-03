#if DEBUG
using IPA.Utilities;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Text;

namespace TTVCore.Diagnostics
{
    /// <summary>
    /// Debug ビルドのみ。テスト用の設定（UserData\TTVCore\debug.json）。
    /// <code>
    /// {
    ///   "mock_websocket_url": "ws://127.0.0.1:8080/ws",
    ///   "mock_subscriptions_url": "http://127.0.0.1:8080/eventsub/subscriptions"
    /// }
    /// </code>
    /// 両方が指定されていれば、EventSub の接続先を Twitch CLI のテストサーバーに切り替える。
    /// テストサーバーにもアクセストークンを送るため、localhost のみ受け付ける。
    /// </summary>
    internal static class DebugOptions
    {
        private const string FileName = "debug.json";

        public static string MockWebSocketUrl { get; private set; }
        public static string MockSubscriptionsUrl { get; private set; }

        public static bool UseMock => MockWebSocketUrl != null && MockSubscriptionsUrl != null;

        public static string BaseDirectory => Path.Combine(UnityGame.UserDataPath, "TTVCore");

        public static void Load()
        {
            var path = Path.Combine(BaseDirectory, FileName);
            if (!File.Exists(path)) {
                return;
            }
            try {
                var jo = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                var ws = LocalOnly((string)jo["mock_websocket_url"], "mock_websocket_url");
                var subs = LocalOnly((string)jo["mock_subscriptions_url"], "mock_subscriptions_url");
                if (ws != null && subs != null) {
                    MockWebSocketUrl = ws;
                    MockSubscriptionsUrl = subs;
                    Plugin.Log.Notice($"[DEBUG] Mock mode: websocket={ws}, subscriptions={subs}");
                }
            }
            catch (Exception e) {
                Plugin.Log.Warn($"[DEBUG] failed to read {FileName} ({e.GetType().Name}: {e.Message}).");
            }
        }

        private static string LocalOnly(string url, string key)
        {
            if (string.IsNullOrEmpty(url)) {
                return null;
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || !(uri.Host == "127.0.0.1" || uri.Host == "localhost" || uri.Host == "[::1]")) {
                Plugin.Log.Warn($"[DEBUG] {key} must point to localhost. Ignored.");
                return null;
            }
            return url;
        }
    }
}
#endif
