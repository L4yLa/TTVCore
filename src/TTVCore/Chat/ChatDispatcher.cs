using Newtonsoft.Json.Linq;
using System;
using TTVCore.Api.V1;

namespace TTVCore.Chat
{
    /// <summary>channel.chat.* と channel.shared_chat.* の通知を処理する。</summary>
    internal static class ChatDispatcher
    {
        public static void Dispatch(string type, JObject ev, DateTimeOffset timestamp)
        {
            var chat = TTVCoreApi.Chat;
            try {
                switch (type) {
                    case "channel.chat.message": {
                        var m = ChatParser.ParseMessage(ev, timestamp);
                        chat.RaiseMessageReceived(m);
                        TimelineAggregator.Instance.OnMessage(m);
                        break;
                    }
                    case "channel.chat.notification": {
                        var n = ChatParser.ParseNotice(ev, timestamp);
                        chat.RaiseNoticeReceived(n);
                        TimelineAggregator.Instance.OnNotice(n);
                        break;
                    }
                    case "channel.chat.message_delete":
                        chat.RaiseMessageDeleted(ChatParser.ParseMessageDelete(ev));
                        break;
                    case "channel.chat.clear_user_messages":
                        chat.RaiseUserMessagesCleared(ChatParser.ParseClearUser(ev));
                        break;
                    case "channel.chat.clear":
                        chat.RaiseChatCleared(ChatParser.ParseClear(ev));
                        break;
                    case "channel.shared_chat.begin":
                    case "channel.shared_chat.update":
                    case "channel.shared_chat.end":
                        ChannelState.OnSharedChatEvent(type, ev);
                        break;
                }
            }
            catch (Exception e) {
                // Twitch 側の変更で形が変わったときなど。読めない通知は捨てて続ける
                Plugin.Log.Warn($"Chat: skipped {type} that could not be read ({e.GetType().Name}: {e.Message}).");
            }
        }
    }
}
