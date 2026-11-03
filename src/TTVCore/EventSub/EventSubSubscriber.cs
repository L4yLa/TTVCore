using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TTVCore.Helix;

namespace TTVCore.EventSub
{
    /// <summary>EventSub の購読の作成（POST /helix/eventsub/subscriptions）。</summary>
    internal sealed class EventSubSubscriber
    {
        // welcome から10秒以内に購読する必要があるため短くする。
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

        private readonly string _url;

        /// <param name="url">通常は本番の URL。Debug ビルドではテストサーバーを指定できる。</param>
        public EventSubSubscriber(string url)
        {
            _url = url;
        }

        /// <summary>HTTP ステータス（通信できなければ 0）と、失敗時のエラー内容を返す。成功は 202。</summary>
        public async Task<(int Status, string Message)> CreateAsync(
            SubscriptionDefinition def, string sessionId, string broadcasterUserId, string accessToken, CancellationToken ct)
        {
            var body = new JObject {
                ["type"] = def.Type,
                ["version"] = def.Version,
                ["condition"] = def.BuildCondition(broadcasterUserId),
                ["transport"] = new JObject {
                    ["method"] = "websocket",
                    ["session_id"] = sessionId,
                },
            };
            var res = await HelixClient.SendAsync(HttpMethod.Post, _url, accessToken, body, Timeout, ct).ConfigureAwait(false);
            return (res.Status, res.Error);
        }
    }
}
