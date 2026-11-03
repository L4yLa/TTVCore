namespace TTVCore.Twitch
{
    internal static class TwitchConstants
    {
        /// <summary>TTVCore 専用アプリの Client ID（Public クライアント）。公開してよい情報。</summary>
        public const string ClientId = "ypvkycxvq3zvsdnjp0nslqbsef6116";

        public const string ScopeReadChat = "user:read:chat";
        public const string ScopeWriteChat = "user:write:chat";

        /// <summary>認証で要求する権限（空白区切り）。</summary>
        public const string Scopes =
            "channel:read:redemptions bits:read channel:read:subscriptions channel:read:hype_train channel:read:ads "
            + ScopeReadChat + " " + ScopeWriteChat;

        public const string DeviceAuthorizeUrl = "https://id.twitch.tv/oauth2/device";
        public const string TokenUrl = "https://id.twitch.tv/oauth2/token";
        public const string ValidateUrl = "https://id.twitch.tv/oauth2/validate";
        public const string EventSubWebSocketUrl = "wss://eventsub.wss.twitch.tv/ws";
        public const string HelixBaseUrl = "https://api.twitch.tv/helix/";
        public const string EventSubSubscriptionsUrl = HelixBaseUrl + "eventsub/subscriptions";
    }
}
