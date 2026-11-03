using System;
using TTVCore.Auth;
using TTVCore.EventSub;
using TTVCore.Twitch;
using TTVCore.Utilities;

namespace TTVCore.Api.V1
{
    public sealed class ConnectionApi
    {
        internal ConnectionApi()
        {
        }

        public AuthState AuthState => TokenManager.Instance?.State ?? AuthState.Checking;

        public EventSubState EventSubState => EventSubManager.Instance?.State ?? EventSubState.Disconnected;

        /// <summary>認証したアカウント。未認証なら null。</summary>
        public UserRef LoggedInUser => TokenManager.Instance?.User;

        /// <summary>チャットの受信（user:read:chat）が許可されているか。</summary>
        public bool CanReadChat => TokenManager.Instance?.HasScope(TwitchConstants.ScopeReadChat) ?? false;

        /// <summary>チャットの送信（user:write:chat）が許可されているか。</summary>
        public bool CanSendChat => TokenManager.Instance?.HasScope(TwitchConstants.ScopeWriteChat) ?? false;

        public event Action<AuthState> AuthStateChanged;

        public event Action<EventSubState> EventSubStateChanged;

        /// <summary>認証ページを開く。認証待ちの間は何もしない。</summary>
        public void StartAuthentication()
        {
            TokenManager.Instance?.StartAuthentication();
        }

        internal void RaiseAuthStateChanged(AuthState state) =>
            EventInvoker.Post(() => this.AuthStateChanged, state, "Connection.AuthStateChanged");

        internal void RaiseEventSubStateChanged(EventSubState state) =>
            EventInvoker.Post(() => this.EventSubStateChanged, state, "Connection.EventSubStateChanged");
    }
}
