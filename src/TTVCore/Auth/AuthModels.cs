using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace TTVCore.Auth
{
    /// <summary>保存するトークン。内容はログに出さない。</summary>
    internal sealed class TokenData
    {
        [JsonProperty("client_id")]
        public string ClientId { get; set; }

        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        /// <summary>アクセストークンの期限（UTC の UNIX 秒）。</summary>
        [JsonProperty("expires_at")]
        public long ExpiresAtUnix { get; set; }

        [JsonProperty("scopes")]
        public string[] Scopes { get; set; }

        [JsonProperty("user_id")]
        public string UserId { get; set; }

        [JsonProperty("login")]
        public string Login { get; set; }

        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        [JsonIgnore]
        public DateTime ExpiresAtUtc => DateTimeOffset.FromUnixTimeSeconds(this.ExpiresAtUnix).UtcDateTime;
    }

    internal sealed class DeviceCodeResponse
    {
        [JsonProperty("device_code")]
        public string DeviceCode { get; set; }

        [JsonProperty("user_code")]
        public string UserCode { get; set; }

        [JsonProperty("verification_uri")]
        public string VerificationUri { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonProperty("interval")]
        public int Interval { get; set; }
    }

    internal sealed class TokenResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        /// <summary>公式の例では配列。空白区切りの文字列も受け付ける。</summary>
        [JsonProperty("scope")]
        public JToken Scope { get; set; }

        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        public string[] GetScopes() => ScopeUtil.ToArray(this.Scope);
    }

    internal sealed class ValidateResponse
    {
        [JsonProperty("client_id")]
        public string ClientId { get; set; }

        [JsonProperty("login")]
        public string Login { get; set; }

        [JsonProperty("scopes")]
        public JToken Scopes { get; set; }

        [JsonProperty("user_id")]
        public string UserId { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        public string[] GetScopes() => ScopeUtil.ToArray(this.Scopes);
    }

    internal static class ScopeUtil
    {
        public static string[] ToArray(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) {
                return null;
            }
            if (token.Type == JTokenType.Array) {
                return token.Values<string>().Where(s => !string.IsNullOrEmpty(s)).ToArray();
            }
            if (token.Type == JTokenType.String) {
                return ((string)token).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            }
            return null;
        }
    }
}
