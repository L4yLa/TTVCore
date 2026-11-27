using System;
using TTVCore.Api.V1;
using TTVCore.Auth;

namespace TTVCore.Diagnostics
{
    /// <summary>
    /// デバッグ機能（リリースビルドにも入る）。設定画面の長押しで有効になり、設定画面から出ると解除される。保存しない。
    /// 有効な間は、Twitch から届く本物の通知とチャットを捨て（接続は保つ）、チャットの送信は本物に送らずに成功として返す。
    /// その間に、テスト用の通知とチャット（DebugEvents）を、受信したものとして流せる。
    /// </summary>
    internal static class DebugMode
    {
        // 認証していないときの仮の配信者
        private const string FakeBroadcasterId = "900000000";
        private const string FakeBroadcasterLogin = "ttvcore_debug";
        private const string FakeBroadcasterName = "TTVCoreDebug";

        private static volatile bool s_active;

        public static bool Active => s_active;

        /// <summary>有効・無効が変わった（メインスレッドで発生）。</summary>
        public static event Action Changed;

        public static void Enable()
        {
            if (s_active) {
                return;
            }
            s_active = true;
            Plugin.Log.Notice("Debug mode: on (real notifications and chat are ignored, and chat is not sent).");
            ClearChat();
            Changed?.Invoke();
        }

        public static void Disable()
        {
            if (!s_active) {
                return;
            }
            s_active = false;
            DebugEvents.StopAll();
            Plugin.Log.Notice("Debug mode: off.");
            ClearChat();
            Changed?.Invoke();
        }

        /// <summary>
        /// 有効にしたときと解除したときに、チャットのクリア（Chat.ChatCleared）を送る。
        /// 本物のチャットとテストのチャットが、利用側の表示で混ざらないようにするため。
        /// </summary>
        private static void ClearChat()
        {
            TTVCoreApi.Chat.RaiseChatCleared(new ChatClearedEvent { BroadcasterId = Broadcaster.Id });
        }

        /// <summary>テストに使う配信者。認証していればそのアカウント、していなければ仮の値。</summary>
        public static UserRef Broadcaster
        {
            get {
                var user = TokenManager.Instance?.User;
                return user != null && !string.IsNullOrEmpty(user.Id)
                    ? user
                    : new UserRef(FakeBroadcasterId, FakeBroadcasterLogin, FakeBroadcasterName);
            }
        }
    }
}
