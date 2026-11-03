using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TTVCore.Twitch;

namespace TTVCore.Auth
{
    internal enum ApiResultKind
    {
        Success,
        /// <summary>サーバーが HTTP エラーを返した。</summary>
        HttpError,
        /// <summary>通信できない、タイムアウト、応答が壊れている。一時的な障害として扱う。</summary>
        NetworkError,
    }

    internal sealed class ApiResult<T> where T : class
    {
        public ApiResultKind Kind { get; private set; }
        public T Value { get; private set; }
        public int StatusCode { get; private set; }
        /// <summary>エラー内容。トークンは含まない。</summary>
        public string Message { get; private set; }

        public static ApiResult<T> Ok(T value) => new ApiResult<T> { Kind = ApiResultKind.Success, Value = value, StatusCode = 200 };
        public static ApiResult<T> Http(int status, string message) => new ApiResult<T> { Kind = ApiResultKind.HttpError, StatusCode = status, Message = message };
        public static ApiResult<T> Network(string message) => new ApiResult<T> { Kind = ApiResultKind.NetworkError, Message = message };
    }

    /// <summary>Twitch の OAuth エンドポイントへの通信。トークンはログに出さない。</summary>
    internal sealed class TwitchAuthClient
    {
        private static readonly HttpClient s_http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public Task<ApiResult<DeviceCodeResponse>> StartDeviceAuthorizationAsync(CancellationToken ct)
        {
            return PostFormAsync<DeviceCodeResponse>(TwitchConstants.DeviceAuthorizeUrl, new[] {
                Pair("client_id", TwitchConstants.ClientId),
                Pair("scopes", TwitchConstants.Scopes),
            }, ct);
        }

        /// <summary>許可される前は HTTP 400 "authorization_pending" が返る。</summary>
        public Task<ApiResult<TokenResponse>> PollDeviceTokenAsync(string deviceCode, CancellationToken ct)
        {
            return PostFormAsync<TokenResponse>(TwitchConstants.TokenUrl, new[] {
                Pair("client_id", TwitchConstants.ClientId),
                Pair("scopes", TwitchConstants.Scopes),
                Pair("device_code", deviceCode),
                Pair("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
            }, ct);
        }

        /// <summary>Public クライアントのため client_secret は送らない。更新用トークンはフォームの送信で URL エンコードされる。</summary>
        public Task<ApiResult<TokenResponse>> RefreshAsync(string refreshToken, CancellationToken ct)
        {
            return PostFormAsync<TokenResponse>(TwitchConstants.TokenUrl, new[] {
                Pair("client_id", TwitchConstants.ClientId),
                Pair("grant_type", "refresh_token"),
                Pair("refresh_token", refreshToken),
            }, ct);
        }

        public Task<ApiResult<ValidateResponse>> ValidateAsync(string accessToken, CancellationToken ct)
        {
            return SendAsync<ValidateResponse>(() => {
                var req = new HttpRequestMessage(HttpMethod.Get, TwitchConstants.ValidateUrl);
                req.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);
                return req;
            }, ct);
        }

        private static KeyValuePair<string, string> Pair(string key, string value) => new KeyValuePair<string, string>(key, value);

        private static Task<ApiResult<T>> PostFormAsync<T>(string url, KeyValuePair<string, string>[] form, CancellationToken ct) where T : class
        {
            return SendAsync<T>(() => new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) }, ct);
        }

        private static async Task<ApiResult<T>> SendAsync<T>(Func<HttpRequestMessage> createRequest, CancellationToken ct) where T : class
        {
            try {
                using (var req = createRequest())
                using (var res = await s_http.SendAsync(req, ct).ConfigureAwait(false)) {
                    var body = res.Content == null ? null : await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) {
                        return ApiResult<T>.Http((int)res.StatusCode, ExtractMessage(body));
                    }
                    T value;
                    try {
                        value = JsonConvert.DeserializeObject<T>(body ?? "");
                    }
                    catch (JsonException e) {
                        return ApiResult<T>.Network($"invalid JSON response ({e.GetType().Name})");
                    }
                    return value == null ? ApiResult<T>.Network("empty response") : ApiResult<T>.Ok(value);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                throw;
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is IOException
                                      || e is WebException || e is SocketException) {
                // ここでの TaskCanceledException は HttpClient のタイムアウト
                return ApiResult<T>.Network($"{e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>エラー応答（{"status":400,"message":"..."} など）から内容を取り出す。</summary>
        internal static string ExtractMessage(string body)
        {
            if (string.IsNullOrEmpty(body)) {
                return "(empty)";
            }
            try {
                var jo = JObject.Parse(body);
                var msg = (string)jo["message"] ?? (string)jo["error_description"] ?? (string)jo["error"];
                if (!string.IsNullOrEmpty(msg)) {
                    return msg;
                }
            }
            catch (JsonException) {
            }
            return body.Length <= 200 ? body : body.Substring(0, 200) + "...";
        }
    }
}
