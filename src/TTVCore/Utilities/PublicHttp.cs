using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TTVCore.Utilities
{
    /// <summary>認証の要らない外部の API（BTTV / FFZ など）と画像の取得。</summary>
    internal static class PublicHttp
    {
        private static readonly HttpClient s_http = CreateClient();

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TTVCore");
            return http;
        }

        /// <summary>JSON を取得する。404 は「データなし」として null を返し、ログに出さない。</summary>
        public static async Task<JToken> GetJsonAsync(string url, string label, CancellationToken ct)
        {
            try {
                using (var res = await s_http.GetAsync(url, ct).ConfigureAwait(false)) {
                    if ((int)res.StatusCode == 404) {
                        return null;
                    }
                    if (!res.IsSuccessStatusCode) {
                        Plugin.Log.Warn($"{label}: request failed ({(int)res.StatusCode}).");
                        return null;
                    }
                    var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using (var reader = new JsonTextReader(new System.IO.StringReader(text)) { DateParseHandling = DateParseHandling.None }) {
                        return JToken.Load(reader);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                throw;
            }
            catch (Exception e) {
                Plugin.Log.Warn($"{label}: request failed ({e.GetType().Name}: {e.Message}).");
                return null;
            }
        }

        /// <summary>バイト列を取得する。失敗したら null。</summary>
        public static async Task<byte[]> GetBytesAsync(string url, CancellationToken ct)
        {
            try {
                using (var res = await s_http.GetAsync(url, ct).ConfigureAwait(false)) {
                    if (!res.IsSuccessStatusCode) {
                        return null;
                    }
                    return await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                throw;
            }
            catch (Exception) {
                return null;
            }
        }
    }
}
