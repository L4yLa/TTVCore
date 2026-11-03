using System;
using TTVCore.Auth;
using TTVCore.Chat;
using TTVCore.Utilities;

namespace TTVCore.Api.V1
{
    public sealed class ChannelApi
    {
        internal ChannelApi()
        {
        }

        /// <summary>配信者（認証したアカウント）。未認証なら null。</summary>
        public UserRef Broadcaster => TokenManager.Instance?.User;

        /// <summary>共有チャットのセッション。共有チャット中でなければ null。</summary>
        public SharedChatSession SharedChat => ChannelState.SharedChat;

        /// <summary>共有チャットの参加チャンネルが変わった。終わったときは null。</summary>
        public event Action<SharedChatSession> SharedChatChanged;

        internal void RaiseSharedChatChanged(SharedChatSession session) =>
            EventInvoker.Post(() => this.SharedChatChanged, session, "Channel.SharedChatChanged");
    }
}
