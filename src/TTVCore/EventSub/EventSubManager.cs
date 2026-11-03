using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TTVCore.Api.V1;
using TTVCore.Auth;
using TTVCore.Chat;
#if DEBUG
using TTVCore.Diagnostics;
#endif
using TTVCore.Twitch;
using TTVCore.Utilities;

namespace TTVCore.EventSub
{
    /// <summary>
    /// EventSub WebSocket の接続管理。
    /// <list type="bullet">
    /// <item>認証済みになったら接続し、welcome から10秒以内に購読する。</item>
    /// <item>keepalive_timeout_seconds と猶予を超えて何も届かなければ切断とみなす。</item>
    /// <item>session_reconnect では新しい URL へ接続し、welcome を受けてから旧接続を閉じる。
    /// 購読は引き継がれる。</item>
    /// <item>message_id で重複を除く。</item>
    /// </list>
    /// 接続は1本のみ。トークン、セッション ID、reconnect_url はログに出さない。
    /// </summary>
    internal sealed class EventSubManager
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan WelcomeTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan KeepaliveGrace = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DefaultKeepalive = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan AuthPollInterval = TimeSpan.FromSeconds(1);
        private const int MaxMessageBytes = 1024 * 1024;

        public static EventSubManager Instance { get; private set; }

        private EventSubSubscriber _subscriber;
        private string _webSocketUrl;
        private readonly MessageIdFilter _dedupe = new MessageIdFilter(500);
        private NotificationDispatcher _dispatcher;
        private CancellationTokenSource _cts;
        private volatile EventSubState _state = EventSubState.Disconnected;
        private volatile HashSet<string> _active = new HashSet<string>();

        public EventSubState State => _state;

        /// <summary>現在の接続で購読が有効になっている種類か。</summary>
        public bool IsSubscribed(string subscriptionType) => _state == EventSubState.Connected && _active.Contains(subscriptionType);

        private sealed class Connection : IDisposable
        {
            public ClientWebSocket Socket;
            public string SessionId;
            public TimeSpan Keepalive;

            public void Dispose()
            {
                try {
                    this.Socket?.Dispose();
                }
                catch (Exception) {
                }
            }
        }

        public void Start()
        {
            Instance = this;
            _webSocketUrl = TwitchConstants.EventSubWebSocketUrl;
            var subscriptionsUrl = TwitchConstants.EventSubSubscriptionsUrl;
#if DEBUG
            if (DebugOptions.UseMock) {
                _webSocketUrl = DebugOptions.MockWebSocketUrl;
                subscriptionsUrl = DebugOptions.MockSubscriptionsUrl;
            }
#endif
            _subscriber = new EventSubSubscriber(subscriptionsUrl);
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _dispatcher = new NotificationDispatcher(ct);
            _ = Task.Run(() => this.RunAsync(ct));
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        private async Task RunAsync(CancellationToken ct)
        {
            var backoff = MinBackoff;
            try {
                while (true) {
                    var userId = await this.WaitForAuthenticatedAsync(ct).ConfigureAwait(false);

                    var established = false;
                    try {
                        established = await this.RunSessionAsync(userId, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                        throw;
                    }
                    catch (Exception e) {
                        Plugin.Log.Warn($"EventSub: session failed ({Describe(e)}).");
                    }
                    finally {
                        this.SetState(EventSubState.Disconnected);
                    }

                    if (established) {
                        backoff = MinBackoff;
                    }
                    Plugin.Log.Info($"EventSub: reconnecting in {backoff.TotalSeconds:0} s.");
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                    if (!established) {
                        backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            }
            catch (Exception e) {
                Plugin.Log.Error($"EventSub: stopped unexpectedly: {e}");
            }
        }

        /// <summary>認証済みになるまで待ち、配信者のユーザー ID を返す。</summary>
        private async Task<string> WaitForAuthenticatedAsync(CancellationToken ct)
        {
            var logged = false;
            while (true) {
                var tm = TokenManager.Instance;
                var userId = tm?.UserId;
                if (tm != null && tm.State == AuthState.Authenticated && !string.IsNullOrEmpty(userId)) {
                    return userId;
                }
                if (!logged) {
                    Plugin.Log.Info("EventSub: waiting for authorization.");
                    logged = true;
                }
                await Task.Delay(AuthPollInterval, ct).ConfigureAwait(false);
            }
        }

        /// <summary>同じアカウントで認証が続いているか。確認中（通信障害を含む）は続いているものとみなす。</summary>
        private static bool IsAuthorizedFor(string userId)
        {
            var tm = TokenManager.Instance;
            if (tm == null) {
                return false;
            }
            var state = tm.State;
            return state != AuthState.Unauthenticated && state != AuthState.WaitingForUser && tm.UserId == userId;
        }

        /// <returns>購読まで完了した（接続が確立した）なら true。</returns>
        private async Task<bool> RunSessionAsync(string userId, CancellationToken ct)
        {
            // welcome 後の10秒を更新に使わないよう、接続前にトークンを確認する。
            var token = await TokenManager.Instance.GetValidAccessTokenAsync(ct).ConfigureAwait(false);
            if (token == null) {
                return false;
            }

            this.SetState(EventSubState.Connecting);
            Connection current = null;
            Task<Connection> pending = null;
            try {
                current = await OpenAsync(new Uri(_webSocketUrl), ct).ConfigureAwait(false);
                Plugin.Log.Info($"EventSub: connected (keepalive {current.Keepalive.TotalSeconds:0} s).");
#if DEBUG
                if (DebugOptions.UseMock) {
                    Plugin.Log.Notice($"[DEBUG] Mock session id = {current.SessionId}");
                }
#endif

                if (!await this.SubscribeAllAsync(current.SessionId, userId, token, ct).ConfigureAwait(false)) {
                    return false;
                }
                this.SetState(EventSubState.Connected);
                ChannelState.OnConnected(userId, ct);

                while (true) {
                    if (!IsAuthorizedFor(userId)) {
                        Plugin.Log.Info("EventSub: authorization changed; closing the connection.");
                        return true;
                    }

                    string text = null;
                    Exception failure = null;
                    try {
                        text = await ReceiveTextAsync(current.Socket, current.Keepalive + KeepaliveGrace, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                        throw;
                    }
                    catch (Exception e) {
                        failure = e;
                    }

                    if (failure != null || text == null) {
                        if (pending != null) {
                            var next = await TakeSwitchAsync(pending).ConfigureAwait(false);
                            pending = null;
                            if (next != null) {
                                current.Dispose();
                                current = next;
                                Plugin.Log.Info("EventSub: switched to the new connection.");
#if DEBUG
                                if (DebugOptions.UseMock) {
                                    Plugin.Log.Notice($"[DEBUG] Mock session id = {current.SessionId}");
                                }
#endif
                                continue;
                            }
                        }
                        if (failure != null) {
                            Plugin.Log.Warn($"EventSub: connection lost ({Describe(failure)}).");
                        }
                        else {
                            Plugin.Log.Warn($"EventSub: closed by server ({(int?)current.Socket.CloseStatus}: {current.Socket.CloseStatusDescription}).");
                        }
                        return true;
                    }

                    pending = this.HandleMessage(text, current, pending, ct);
                }
            }
            finally {
                if (current != null) {
                    await ReplyCloseAsync(current).ConfigureAwait(false);
                    current.Dispose();
                }
                if (pending != null) {
                    _ = pending.ContinueWith(t => {
                        if (t.Status == TaskStatus.RanToCompletion) {
                            t.Result?.Dispose();
                        }
                    }, TaskScheduler.Default);
                }
            }
        }

        /// <summary>受信メッセージを処理する。reconnect を始めた場合は切り替えのタスクを返す。</summary>
        private Task<Connection> HandleMessage(string text, Connection current, Task<Connection> pending, CancellationToken ct)
        {
            JObject jo;
            try {
                jo = JsonUtil.Parse(text);
            }
            catch (JsonException) {
                Plugin.Log.Warn("EventSub: received invalid JSON.");
                return pending;
            }

            var type = JsonUtil.Str(JsonUtil.At(jo, "metadata", "message_type"));
            switch (type) {
                case "session_keepalive":
                    return pending;

                case "notification":
                    if (_dedupe.TryAdd(JsonUtil.Str(JsonUtil.At(jo, "metadata", "message_id")))) {
                        _dispatcher.Dispatch(jo);
                    }
                    return pending;

                case "session_reconnect": {
                    if (pending != null) {
                        return pending;
                    }
                    var url = JsonUtil.Str(JsonUtil.At(jo, "payload", "session", "reconnect_url"));
                    if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
                        Plugin.Log.Warn("EventSub: reconnect requested without a valid URL.");
                        return null;
                    }
                    Plugin.Log.Info("EventSub: server requested a reconnect.");
                    // reconnect_url は変更せずに使う。
                    return SwitchAsync(uri, current, ct);
                }

                case "revocation": {
                    var sub = JsonUtil.At(jo, "payload", "subscription");
                    var status = JsonUtil.Str(JsonUtil.At(sub, "status"));
                    Plugin.Log.Warn($"EventSub: subscription revoked ({JsonUtil.Str(JsonUtil.At(sub, "type"))}: {status}).");
                    if (status == "authorization_revoked") {
                        TokenManager.Instance?.RequestValidateSoon();
                    }
                    return pending;
                }

                default:
                    // 知らない種類のメッセージは何もしない
                    Plugin.Log.Debug($"EventSub: ignored a message of unknown type '{type}'.");
                    return pending;
            }
        }

        private async Task<bool> SubscribeAllAsync(string sessionId, string userId, string token, CancellationToken ct)
        {
            var defs = SubscriptionDefinitions.All;
            var results = await Task.WhenAll(defs.Select(d => _subscriber.CreateAsync(d, sessionId, userId, token, ct))).ConfigureAwait(false);

            var succeeded = 0;
            var active = new HashSet<string>();
            var unauthorized = new List<SubscriptionDefinition>();
            for (var i = 0; i < defs.Count; i++) {
                if (Evaluate(defs[i], results[i], retryOn401: true)) {
                    succeeded++;
                    active.Add(defs[i].Type);
                }
                else if (results[i].Status == 401) {
                    unauthorized.Add(defs[i]);
                }
            }

            if (unauthorized.Count > 0) {
                var newToken = await TokenManager.Instance.HandleUnauthorizedAsync(token, ct).ConfigureAwait(false);
                if (newToken == null) {
                    Plugin.Log.Warn("EventSub: the token was rejected while subscribing.");
                }
                else {
                    var retry = await Task.WhenAll(unauthorized.Select(d => _subscriber.CreateAsync(d, sessionId, userId, newToken, ct))).ConfigureAwait(false);
                    for (var i = 0; i < unauthorized.Count; i++) {
                        if (Evaluate(unauthorized[i], retry[i], retryOn401: false)) {
                            succeeded++;
                            active.Add(unauthorized[i].Type);
                        }
                    }
                }
            }

            _active = active;
            Plugin.Log.Info($"EventSub: subscribed {succeeded}/{defs.Count}.");
            return succeeded > 0;
        }

        /// <returns>購読が有効なら true。</returns>
        private static bool Evaluate(SubscriptionDefinition def, (int Status, string Message) result, bool retryOn401)
        {
            if (result.Status >= 200 && result.Status < 300) {
                return true;
            }
            if (result.Status == 409) {
                Plugin.Log.Info($"EventSub: {def} already exists.");
                return true;
            }
            if (result.Status == 401 && retryOn401) {
                return false;
            }
            Plugin.Log.Warn($"EventSub: failed to subscribe {def} ({result.Status}: {result.Message}).");
            return false;
        }

        /// <summary>接続して welcome を受け取る。</summary>
        private static async Task<Connection> OpenAsync(Uri uri, CancellationToken ct)
        {
            var ws = new ClientWebSocket();
            try {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                    cts.CancelAfter(ConnectTimeout);
                    await ws.ConnectAsync(uri, cts.Token).ConfigureAwait(false);
                }

                var text = await ReceiveTextAsync(ws, WelcomeTimeout, ct).ConfigureAwait(false);
                if (text == null) {
                    throw new InvalidOperationException($"closed before welcome ({(int?)ws.CloseStatus}: {ws.CloseStatusDescription})");
                }
                var jo = JsonUtil.Parse(text);
                var type = JsonUtil.Str(JsonUtil.At(jo, "metadata", "message_type"));
                if (type != "session_welcome") {
                    throw new InvalidOperationException($"first message was '{type}', not session_welcome");
                }
                var session = JsonUtil.At(jo, "payload", "session");
                var sessionId = JsonUtil.Str(JsonUtil.At(session, "id"));
                if (string.IsNullOrEmpty(sessionId)) {
                    throw new InvalidOperationException("welcome without session id");
                }
                var keepalive = JsonUtil.IntN(JsonUtil.At(session, "keepalive_timeout_seconds"));
                return new Connection {
                    Socket = ws,
                    SessionId = sessionId,
                    Keepalive = keepalive.HasValue && keepalive.Value > 0 ? TimeSpan.FromSeconds(keepalive.Value) : DefaultKeepalive,
                };
            }
            catch {
                ws.Dispose();
                throw;
            }
        }

        /// <summary>
        /// reconnect 先へ接続し、welcome を受けたら旧接続へ close を送る。
        /// 旧接続の受信ループは close を受けて終わり、新しい接続へ切り替わる。失敗時は null。
        /// </summary>
        private static async Task<Connection> SwitchAsync(Uri uri, Connection old, CancellationToken ct)
        {
            try {
                var next = await OpenAsync(uri, ct).ConfigureAwait(false);
                Plugin.Log.Info("EventSub: reconnect target is ready.");
                try {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                        cts.CancelAfter(CloseTimeout);
                        await old.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "reconnect", cts.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception) {
                    try {
                        old.Socket.Abort();
                    }
                    catch (Exception) {
                    }
                }
                return next;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                return null;
            }
            catch (Exception e) {
                Plugin.Log.Warn($"EventSub: reconnect failed ({Describe(e)}).");
                return null;
            }
        }

        /// <summary>
        /// サーバーから受けた close に応答する。終了時にも行うため、独自の時間制限で待つ。
        /// </summary>
        private static async Task ReplyCloseAsync(Connection connection)
        {
            try {
                if (connection.Socket?.State != WebSocketState.CloseReceived) {
                    return;
                }
                using (var cts = new CancellationTokenSource(CloseTimeout)) {
                    await connection.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception) {
            }
        }

        private static async Task<Connection> TakeSwitchAsync(Task<Connection> pending)
        {
            try {
                return await pending.ConfigureAwait(false);
            }
            catch (Exception) {
                return null;
            }
        }

        /// <summary>テキストメッセージを1件受信する。close を受けたら null。時間内に届かなければ例外。</summary>
        private static async Task<string> ReceiveTextAsync(ClientWebSocket ws, TimeSpan timeout, CancellationToken ct)
        {
            var buffer = new byte[8192];
            using (var ms = new MemoryStream())
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                cts.CancelAfter(timeout);
                while (true) {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) {
                        return null;
                    }
                    ms.Write(buffer, 0, result.Count);
                    if (ms.Length > MaxMessageBytes) {
                        throw new InvalidDataException("message too large");
                    }
                    if (result.EndOfMessage) {
                        return Encoding.UTF8.GetString(ms.ToArray());
                    }
                }
            }
        }

#if DEBUG
        /// <summary>Debug ビルドのみ。ファイルから読んだ通知を、受信したものとして処理する。</summary>
        internal void InjectForDebug(string text, string source)
        {
            JObject jo;
            try {
                jo = JsonUtil.Parse(text);
            }
            catch (JsonException e) {
                Plugin.Log.Warn($"[DEBUG] Inject: {source} is not valid JSON ({e.Message}).");
                return;
            }

            if (!(jo["metadata"] is JObject)) {
                var sub = jo["subscription"] as JObject;
                var ev = jo["event"] as JObject;
                if (sub == null || ev == null) {
                    Plugin.Log.Warn($"[DEBUG] Inject: {source} needs {{metadata, payload}} or {{subscription, event}}.");
                    return;
                }
                jo = new JObject {
                    ["metadata"] = new JObject {
                        ["message_id"] = Guid.NewGuid().ToString(),
                        ["message_type"] = "notification",
                        ["message_timestamp"] = DateTime.UtcNow.ToString("o"),
                        ["subscription_type"] = sub["type"],
                        ["subscription_version"] = sub["version"],
                    },
                    ["payload"] = new JObject {
                        ["subscription"] = sub,
                        ["event"] = ev,
                    },
                };
            }

            if (JsonUtil.Str(JsonUtil.At(jo, "metadata", "message_type")) != "notification") {
                Plugin.Log.Warn($"[DEBUG] Inject: {source} is not a notification.");
                return;
            }
            if (!_dedupe.TryAdd(JsonUtil.Str(JsonUtil.At(jo, "metadata", "message_id")))) {
                Plugin.Log.Notice($"[DEBUG] Inject: {source} skipped (duplicate message_id).");
                return;
            }
            Plugin.Log.Notice($"[DEBUG] Inject: {source}");
            _dispatcher?.Dispatch(jo);
        }
#endif

        private void SetState(EventSubState state)
        {
            if (_state == state) {
                return;
            }
            var wasConnected = _state == EventSubState.Connected;
            _state = state;
            Plugin.Log.Info($"EventSub: state = {state}");
            TTVCoreApi.Connection.RaiseEventSubStateChanged(state);
            var isConnected = state == EventSubState.Connected;
            if (wasConnected != isConnected) {
                TTVCoreApi.Notifications.RaiseConnectionStateChanged(isConnected);
            }
        }

        private static string Describe(Exception e)
        {
            if (e is OperationCanceledException) {
                return "timed out";
            }
            return $"{e.GetType().Name}: {e.Message}";
        }
    }
}
