namespace TTVCore.Api.V1
{
    /// <summary>Twitch の利用者。</summary>
    public sealed class UserRef
    {
        public string Id { get; }
        public string Login { get; }
        public string DisplayName { get; }

        internal UserRef(string id, string login, string displayName)
        {
            this.Id = id;
            this.Login = login;
            this.DisplayName = displayName;
        }
    }

    /// <summary>サブスクのティア。Prime は <see cref="Tier1"/>。</summary>
    public enum SubTier
    {
        Unknown = 0,
        Tier1,
        Tier2,
        Tier3,
    }

    public enum AuthState
    {
        Unknown = 0,
        /// <summary>保存済みトークンの確認中（通信障害で確認できていない間を含む）。</summary>
        Checking,
        Unauthenticated,
        /// <summary>認証ページを開き、許可を待っている。</summary>
        WaitingForUser,
        Authenticated,
    }

    public enum EventSubState
    {
        Unknown = 0,
        Disconnected,
        Connecting,
        /// <summary>接続して購読が完了した。</summary>
        Connected,
    }
}
