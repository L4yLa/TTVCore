using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TTVCore.Api.V1;
using TTVCore.Auth;
using TTVCore.Twitch;

namespace TTVCore.Helix
{
    internal sealed class HelixResponse
    {
        /// <summary>HTTP ステータス。通信できなければ 0。</summary>
        public int Status { get; }
        /// <summary>成功時の応答の本文。失敗時は null。</summary>
        public string Body { get; }
        /// <summary>失敗時のエラー内容。トークンは含まない。</summary>
        public string Error { get; }

        public bool IsSuccess => this.Status >= 200 && this.Status < 300;

        public HelixResponse(int status, string body, string error)
        {
            this.Status = status;
            this.Body = body;
            this.Error = error;
        }
    }

    /// <summary>ユーザーのアクセストークンで Twitch Helix API を呼ぶ。トークンはログに出さない。</summary>
    internal static class HelixClient
    {
        private static readonly HttpClient s_http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

        public static async Task<HelixResponse> SendAsync(
            HttpMethod method, string url, string accessToken, JObject body, TimeSpan? timeout, CancellationToken ct)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                cts.CancelAfter(timeout ?? DefaultTimeout);
                try {
                    using (var req = new HttpRequestMessage(method, url)) {
                        if (body != null) {
                            req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                        }
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                        req.Headers.Add("Client-Id", TwitchConstants.ClientId);
                        using (var res = await s_http.SendAsync(req, cts.Token).ConfigureAwait(false)) {
                            var text = res.Content == null ? null : await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                            var status = (int)res.StatusCode;
                            return res.IsSuccessStatusCode
                                ? new HelixResponse(status, text, null)
                                : new HelixResponse(status, null, TwitchAuthClient.ExtractMessage(text));
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                    throw;
                }
                catch (OperationCanceledException) {
                    return new HelixResponse(0, null, "timed out");
                }
                catch (Exception e) {
                    return new HelixResponse(0, null, $"{e.GetType().Name}: {e.Message}");
                }
            }
        }

        /// <summary>Get Users でユーザー ID から表示名を取得する。取れなければ null。</summary>
        public static async Task<string> GetDisplayNameAsync(string userId, string accessToken, CancellationToken ct)
        {
            var url = TwitchConstants.HelixBaseUrl + "users?id=" + Uri.EscapeDataString(userId);
            var res = await SendAsync(HttpMethod.Get, url, accessToken, null, null, ct).ConfigureAwait(false);
            if (!res.IsSuccess) {
                Plugin.Log.Warn($"Helix: Get Users failed ({res.Status}: {res.Error}).");
                return null;
            }
            try {
                var data = Utilities.JsonUtil.Parse(res.Body)["data"] as JArray;
                return data != null && data.Count > 0 ? Utilities.JsonUtil.StrOrNull(Utilities.JsonUtil.At(data[0], "display_name")) : null;
            }
            catch (JsonException) {
                Plugin.Log.Warn("Helix: Get Users returned invalid JSON.");
                return null;
            }
        }

        /// <summary>Get Users で複数のユーザー ID の情報を取得する（100 件まで）。取れなかった分は含まれない。</summary>
        public static async Task<List<UserRef>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
        {
            var result = new List<UserRef>();
            if (ids.Count == 0) {
                return result;
            }
            var query = string.Join("&", ids.Take(100).Select(id => "id=" + Uri.EscapeDataString(id)));
            var jo = await GetJsonAsync("users?" + query, ct).ConfigureAwait(false);
            if (!(jo?["data"] is JArray data)) {
                return result;
            }
            foreach (var u in data) {
                var id = Utilities.JsonUtil.StrOrNull(Utilities.JsonUtil.At(u, "id"));
                if (id == null) {
                    continue;
                }
                var login = Utilities.JsonUtil.StrOrNull(Utilities.JsonUtil.At(u, "login")) ?? id;
                result.Add(new UserRef(id, login, Utilities.JsonUtil.StrOrNull(Utilities.JsonUtil.At(u, "display_name")) ?? login));
            }
            return result;
        }

        /// <summary>
        /// 認証済みのトークンで GET し、応答の JSON を返す。401 なら1回だけトークンを更新して再試行する。
        /// 失敗したら null（理由はログに出す）。<paramref name="pathAndQuery"/> は helix/ より後ろ。
        /// </summary>
        public static async Task<JObject> GetJsonAsync(string pathAndQuery, CancellationToken ct)
        {
            var tm = TokenManager.Instance;
            var token = tm == null ? null : await tm.GetValidAccessTokenAsync(ct).ConfigureAwait(false);
            if (token == null) {
                return null;
            }
            var url = TwitchConstants.HelixBaseUrl + pathAndQuery;
            var res = await SendAsync(HttpMethod.Get, url, token, null, null, ct).ConfigureAwait(false);
            if (res.Status == 401) {
                token = await tm.HandleUnauthorizedAsync(token, ct).ConfigureAwait(false);
                if (token == null) {
                    return null;
                }
                res = await SendAsync(HttpMethod.Get, url, token, null, null, ct).ConfigureAwait(false);
            }
            var name = pathAndQuery.Split('?')[0];
            if (!res.IsSuccess) {
                Plugin.Log.Warn($"Helix: GET {name} failed ({res.Status}: {res.Error}).");
                return null;
            }
            try {
                return Utilities.JsonUtil.Parse(res.Body);
            }
            catch (JsonException) {
                Plugin.Log.Warn($"Helix: GET {name} returned invalid JSON.");
                return null;
            }
        }
    }
}
