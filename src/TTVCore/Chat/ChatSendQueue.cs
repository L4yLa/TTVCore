using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TTVCore.Api.V1;
using TTVCore.Auth;
using TTVCore.Helix;
using TTVCore.Twitch;
using TTVCore.Utilities;

namespace TTVCore.Chat
{
    /// <summary>
    /// チャットの送信を1つの列に並べ、頻度の上限を守って順に送る（Send Chat Message）。
    /// 上限は Twitch の一般の利用者の枠（30秒あたり20件）に合わせた控えめな値。
    /// </summary>
    internal sealed class ChatSendQueue
    {
        private const int MaxLength = 500;
        private const int WindowLimit = 20;
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
        private const int MaxPending = 20;

        public static ChatSendQueue Instance { get; } = new ChatSendQueue();

        private sealed class Item
        {
            public string ModName;
            public string Text;
            public string ReplyParentMessageId;
            public TaskCompletionSource<SendResult> Completion;
        }

        private readonly object _gate = new object();
        private readonly Queue<Item> _pending = new Queue<Item>();
        private readonly Queue<DateTime> _sent = new Queue<DateTime>();
        private bool _running;
        private CancellationToken _ct;

        public void Start(CancellationToken ct)
        {
            _ct = ct;
        }

        public Task<SendResult> Enqueue(string modName, string text, string replyParentMessageId)
        {
            if (string.IsNullOrEmpty(text)) {
                return Task.FromResult(new SendResult { Status = SendStatus.Failed });
            }
            if (text.Length > MaxLength) {
                return Task.FromResult(new SendResult { Status = SendStatus.TooLong });
            }
            var item = new Item {
                ModName = modName,
                Text = text,
                ReplyParentMessageId = replyParentMessageId,
                Completion = new TaskCompletionSource<SendResult>(TaskCreationOptions.RunContinuationsAsynchronously),
            };
            lock (_gate) {
                if (_pending.Count >= MaxPending) {
                    Plugin.Log.Warn($"Chat: send from {modName} rejected; too many messages are waiting.");
                    return Task.FromResult(new SendResult { Status = SendStatus.RateLimited });
                }
                _pending.Enqueue(item);
                if (!_running) {
                    _running = true;
                    _ = Task.Run(this.RunAsync);
                }
            }
            return item.Completion.Task;
        }

        private async Task RunAsync()
        {
            while (true) {
                Item item;
                TimeSpan wait;
                lock (_gate) {
                    if (_pending.Count == 0) {
                        _running = false;
                        return;
                    }
                    var now = DateTime.UtcNow;
                    while (_sent.Count > 0 && now - _sent.Peek() >= Window) {
                        _sent.Dequeue();
                    }
                    if (_sent.Count >= WindowLimit) {
                        wait = Window - (now - _sent.Peek());
                        item = null;
                    }
                    else {
                        wait = TimeSpan.Zero;
                        item = _pending.Dequeue();
                        _sent.Enqueue(now);
                    }
                }
                if (item == null) {
                    try {
                        await Task.Delay(wait, _ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                        this.FailAll();
                        return;
                    }
                    continue;
                }
                SendResult result;
                try {
                    result = await SendAsync(item, _ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    result = new SendResult { Status = SendStatus.Failed };
                }
                catch (Exception e) {
                    Plugin.Log.Error($"Chat: send failed unexpectedly: {e}");
                    result = new SendResult { Status = SendStatus.Failed };
                }
                item.Completion.TrySetResult(result);
            }
        }

        private void FailAll()
        {
            lock (_gate) {
                while (_pending.Count > 0) {
                    _pending.Dequeue().Completion.TrySetResult(new SendResult { Status = SendStatus.Failed });
                }
                _running = false;
            }
        }

        private static async Task<SendResult> SendAsync(Item item, CancellationToken ct)
        {
            var tm = TokenManager.Instance;
            var broadcasterId = tm?.UserId;
            if (tm == null || tm.State != AuthState.Authenticated || broadcasterId == null) {
                return new SendResult { Status = SendStatus.NotAuthenticated };
            }
            if (!tm.HasScope(TwitchConstants.ScopeWriteChat)) {
                return new SendResult { Status = SendStatus.NotPermitted };
            }
            var token = await tm.GetValidAccessTokenAsync(ct).ConfigureAwait(false);
            if (token == null) {
                return new SendResult { Status = SendStatus.NotAuthenticated };
            }

            var body = new JObject {
                ["broadcaster_id"] = broadcasterId,
                ["sender_id"] = broadcasterId,
                ["message"] = item.Text,
            };
            if (!string.IsNullOrEmpty(item.ReplyParentMessageId)) {
                body["reply_parent_message_id"] = item.ReplyParentMessageId;
            }
            var url = TwitchConstants.HelixBaseUrl + "chat/messages";
            var res = await HelixClient.SendAsync(HttpMethod.Post, url, token, body, null, ct).ConfigureAwait(false);
            if (res.Status == 401) {
                var newToken = await tm.HandleUnauthorizedAsync(token, ct).ConfigureAwait(false);
                if (newToken == null) {
                    return new SendResult { Status = SendStatus.NotAuthenticated };
                }
                res = await HelixClient.SendAsync(HttpMethod.Post, url, newToken, body, null, ct).ConfigureAwait(false);
            }

            if (!res.IsSuccess) {
                Plugin.Log.Warn($"Chat: send from {item.ModName} failed ({res.Status}: {res.Error}).");
                switch (res.Status) {
                    case 401:
                        return new SendResult { Status = SendStatus.NotAuthenticated };
                    case 403:
                        return new SendResult { Status = SendStatus.NotPermitted };
                    case 429:
                        return new SendResult { Status = SendStatus.RateLimited };
                    default:
                        return new SendResult { Status = SendStatus.Failed };
                }
            }

            try {
                var data = JsonUtil.Parse(res.Body)["data"] as JArray;
                var first = data != null && data.Count > 0 ? data[0] : null;
                var sent = JsonUtil.Bool(JsonUtil.At(first, "is_sent"));
                var result = new SendResult {
                    Status = sent ? SendStatus.Sent : SendStatus.Dropped,
                    MessageId = JsonUtil.StrOrNull(JsonUtil.At(first, "message_id")),
                    DropReasonCode = JsonUtil.StrOrNull(JsonUtil.At(first, "drop_reason", "code")),
                    DropReasonMessage = JsonUtil.StrOrNull(JsonUtil.At(first, "drop_reason", "message")),
                };
                Plugin.Log.Info(sent
                    ? $"Chat: sent a message from {item.ModName}."
                    : $"Chat: a message from {item.ModName} was dropped ({result.DropReasonCode}).");
                return result;
            }
            catch (JsonException) {
                Plugin.Log.Warn("Chat: Send Chat Message returned invalid JSON.");
                return new SendResult { Status = SendStatus.Failed };
            }
        }
    }
}
