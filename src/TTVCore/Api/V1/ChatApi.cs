using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TTVCore.Chat;
using TTVCore.Configuration;
using TTVCore.Utilities;

namespace TTVCore.Api.V1
{
    /// <summary>
    /// チャットの受信と送信。個別のイベントは受信した時点で発生する。
    /// 表示には、通知とチャットを組にした <see cref="TimelineItemReceived"/> を使う。
    /// </summary>
    public sealed class ChatApi
    {
        internal ChatApi()
        {
        }

        public event Action<ChatMessage> MessageReceived;
        public event Action<ChatNotice> NoticeReceived;
        public event Action<MessageDeletedEvent> MessageDeleted;
        public event Action<UserMessagesClearedEvent> UserMessagesCleared;
        public event Action<ChatClearedEvent> ChatCleared;

        /// <summary>
        /// 表示用の流れ。通常のメッセージと、チャットと通知を組にしたものが届く。
        /// 組になる通知は、もう片方を待つ間だけ遅れて発生する。発生順は <see cref="TimelineItem.Timestamp"/> の順と一致しないことがある。
        /// </summary>
        public event Action<TimelineItem> TimelineItemReceived;

        /// <summary>サードパーティの MOD からの送信を設定で許可しているか。</summary>
        public bool ThirdPartySendEnabled => CoreSettings.Instance.ThirdPartySendEnabled;

        /// <summary>
        /// 送信の窓口を作る。<paramref name="modName"/> はログに残すための名前。
        /// TTVStreamersKit かどうかは、このメソッドを呼んだアセンブリで判定する。
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public ChatSender CreateSender(string modName)
        {
            var caller = Assembly.GetCallingAssembly();
            var assemblyName = caller.GetName().Name;
            var firstParty = assemblyName == "TTVStreamersKit" || caller == typeof(ChatApi).Assembly;
            return new ChatSender(string.IsNullOrEmpty(modName) ? assemblyName : modName, assemblyName, firstParty);
        }

        internal void RaiseMessageReceived(ChatMessage m) =>
            EventInvoker.Post(() => this.MessageReceived, m, "Chat.MessageReceived");

        internal void RaiseNoticeReceived(ChatNotice n) =>
            EventInvoker.Post(() => this.NoticeReceived, n, "Chat.NoticeReceived");

        internal void RaiseMessageDeleted(MessageDeletedEvent e) =>
            EventInvoker.Post(() => this.MessageDeleted, e, "Chat.MessageDeleted");

        internal void RaiseUserMessagesCleared(UserMessagesClearedEvent e) =>
            EventInvoker.Post(() => this.UserMessagesCleared, e, "Chat.UserMessagesCleared");

        internal void RaiseChatCleared(ChatClearedEvent e) =>
            EventInvoker.Post(() => this.ChatCleared, e, "Chat.ChatCleared");

        internal void RaiseTimelineItemReceived(TimelineItem item) =>
            EventInvoker.Post(() => this.TimelineItemReceived, item, "Chat.TimelineItemReceived");
    }

    /// <summary>チャットの送信の窓口。<see cref="ChatApi.CreateSender"/> で作る。</summary>
    public sealed class ChatSender
    {
        private readonly string _assemblyName;
        private readonly bool _firstParty;

        /// <summary>ログに残す名前。</summary>
        public string ModName { get; }

        internal ChatSender(string modName, string assemblyName, bool firstParty)
        {
            this.ModName = modName;
            _assemblyName = assemblyName;
            _firstParty = firstParty;
        }

        /// <summary>
        /// 配信者のアカウントでチャンネルに送信する。500 文字を超える文は送らない。
        /// 共有チャット中は、参加しているすべてのチャンネルに表示される。
        /// </summary>
        public Task<SendResult> SendAsync(string text, string replyParentMessageId = null)
        {
            if (!_firstParty && !CoreSettings.Instance.ThirdPartySendEnabled) {
                Plugin.Log.Info($"Chat: send from {this.ModName} ({_assemblyName}) is disabled by the settings.");
                return Task.FromResult(new SendResult { Status = SendStatus.Disabled });
            }
            return ChatSendQueue.Instance.Enqueue($"{this.ModName} ({_assemblyName})", text, replyParentMessageId);
        }
    }
}
