using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TTVCore.Api.V1;
using TTVCore.Helix;
using TTVCore.Twitch;
using TTVCore.Utilities;

namespace TTVCore.Auth
{
    /// <summary>
    /// 認証とトークンの寿命の管理。
    /// <list type="bullet">
    /// <item>使えるトークンがなければ Device Code Flow を始める。</item>
    /// <item>期限の30分前に更新する。API を呼ぶ前にも期限が近ければ更新する。</item>
    /// <item>起動時と1時間ごとに検証する。</item>
    /// <item>更新用トークンは1回限りのため、更新と保存は <see cref="_lock"/> で順に行う。</item>
    /// </list>
    /// トークンはログに出さない。
    /// </summary>
    internal sealed class TokenManager
    {
        private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan ValidateInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan LoopInterval = TimeSpan.FromSeconds(30);

        private enum RefreshOutcome
        {
            Ok,
            /// <summary>更新を拒否された。トークンを破棄し、認証を始めた。</summary>
            Lost,
            /// <summary>一時的な障害。トークンは保持し、後で再試行する。</summary>
            Transient,
        }

        public static TokenManager Instance { get; private set; }

        private readonly TwitchAuthClient _client = new TwitchAuthClient();
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private readonly string[] _requiredScopes =
            TwitchConstants.Scopes.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        private CancellationTokenSource _cts;

        // 以下は _lock を持っている間だけ変更する
        private TokenData _token;
        private DateTime _nextValidateUtc = DateTime.MaxValue;
        private DateTime _nextRefreshRetryUtc = DateTime.MinValue;

        private volatile AuthState _state = AuthState.Checking;
        private volatile UserRef _user;
        private volatile string[] _grantedScopes = Array.Empty<string>();
        private int _deviceFlowRunning;

        public AuthState State => _state;

        /// <summary>認証したアカウント（配信者）。検証が済むまでは null。</summary>
        public UserRef User => _user;

        public string UserId => _user?.Id;

        public bool HasScope(string scope) => _grantedScopes.Contains(scope);

        public void Start()
        {
            Instance = this;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _ = Task.Run(() => this.RunAsync(ct));
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        /// <summary>認証を始める（認証ページを開く）。認証中なら何もしない。</summary>
        public void StartAuthentication()
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            if (ct.IsCancellationRequested) {
                return;
            }
            if (Interlocked.CompareExchange(ref _deviceFlowRunning, 1, 0) != 0) {
                Plugin.Log.Info("Auth: authorization is already in progress.");
                return;
            }
            _ = Task.Run(() => this.DeviceFlowAsync(ct));
        }

        /// <summary>使えるアクセストークンを返す。期限が近ければ先に更新する。未認証なら null。</summary>
        public async Task<string> GetValidAccessTokenAsync(CancellationToken ct)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try {
                if (_token == null) {
                    return null;
                }
                if (this.NeedsRefresh(DateTime.UtcNow)) {
                    await this.RefreshCoreAsync("before API call", ct).ConfigureAwait(false);
                }
                return _token?.AccessToken;
            }
            finally {
                _lock.Release();
            }
        }

        /// <summary>次の定期処理（30秒以内）で検証させる。</summary>
        public void RequestValidateSoon()
        {
            var ct = _cts?.Token ?? CancellationToken.None;
            _ = Task.Run(async () => {
                try {
                    await _lock.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    return;
                }
                try {
                    if (_token != null) {
                        _nextValidateUtc = DateTime.MinValue;
                    }
                }
                finally {
                    _lock.Release();
                }
            });
        }

        /// <summary>API が 401 を返したときに呼ぶ。1回だけ更新し、新しいアクセストークンを返す。失敗したら null。</summary>
        public async Task<string> HandleUnauthorizedAsync(string failedAccessToken, CancellationToken ct)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try {
                if (_token == null) {
                    return null;
                }
                if (_token.AccessToken != failedAccessToken) {
                    return _token.AccessToken;
                }
                var outcome = await this.RefreshCoreAsync("API returned 401", ct).ConfigureAwait(false);
                return outcome == RefreshOutcome.Ok ? _token?.AccessToken : null;
            }
            finally {
                _lock.Release();
            }
        }

        private async Task RunAsync(CancellationToken ct)
        {
            try {
                await this.InitializeAsync(ct).ConfigureAwait(false);
                await this.MaintenanceLoopAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            }
            catch (Exception e) {
                Plugin.Log.Error($"Auth: token manager stopped unexpectedly: {e}");
            }
        }

        private async Task InitializeAsync(CancellationToken ct)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try {
                var stored = TokenStore.Load();
                if (stored == null) {
                    Plugin.Log.Info("Auth: no stored token. Starting authorization.");
                    this.SetState(AuthState.Unauthenticated);
                    this.StartAuthentication();
                    return;
                }
                if (!this.IsUsable(stored, out var reason)) {
                    Plugin.Log.Info($"Auth: stored token cannot be used ({reason}). Starting authorization.");
                    TokenStore.Delete();
                    this.SetState(AuthState.Unauthenticated);
                    this.StartAuthentication();
                    return;
                }

                _token = stored;

#if DEBUG
                Plugin.Log.Notice("[DEBUG] Auth: forcing a refresh at startup.");
                var outcome = await this.RefreshCoreAsync("debug: startup", ct).ConfigureAwait(false);
#else
                var outcome = this.NeedsRefresh(DateTime.UtcNow)
                    ? await this.RefreshCoreAsync("expired at startup", ct).ConfigureAwait(false)
                    : RefreshOutcome.Ok;
#endif
                if (outcome == RefreshOutcome.Lost) {
                    return;
                }
                await this.ValidateCoreAsync(allowRefresh: true, ct).ConfigureAwait(false);
            }
            finally {
                _lock.Release();
            }
        }

        private async Task MaintenanceLoopAsync(CancellationToken ct)
        {
            while (true) {
                await Task.Delay(LoopInterval, ct).ConfigureAwait(false);
                await _lock.WaitAsync(ct).ConfigureAwait(false);
                try {
                    if (_token == null) {
                        continue;
                    }
                    var now = DateTime.UtcNow;
                    if (this.NeedsRefresh(now) && now >= _nextRefreshRetryUtc) {
                        if (await this.RefreshCoreAsync("scheduled", ct).ConfigureAwait(false) == RefreshOutcome.Lost) {
                            continue;
                        }
                    }
                    if (_token != null && now >= _nextValidateUtc) {
                        await this.ValidateCoreAsync(allowRefresh: true, ct).ConfigureAwait(false);
                    }
                }
                finally {
                    _lock.Release();
                }
            }
        }

        private async Task DeviceFlowAsync(CancellationToken ct)
        {
            try {
                this.SetState(AuthState.WaitingForUser);

                var start = await _client.StartDeviceAuthorizationAsync(ct).ConfigureAwait(false);
                if (start.Kind != ApiResultKind.Success) {
                    Plugin.Log.Error($"Auth: failed to start authorization ({start.Kind} {start.StatusCode}: {start.Message}).");
                    this.SetState(AuthState.Unauthenticated);
                    return;
                }
                var device = start.Value;
                if (string.IsNullOrEmpty(device.DeviceCode) || string.IsNullOrEmpty(device.VerificationUri)) {
                    Plugin.Log.Error("Auth: authorization response is missing required fields.");
                    this.SetState(AuthState.Unauthenticated);
                    return;
                }

                BrowserOpener.Open(device.VerificationUri);
                Plugin.Log.Info($"Auth: waiting for the user to authorize (expires in {device.ExpiresIn} s).");

                var interval = TimeSpan.FromSeconds(Math.Max(1, device.Interval));
                var deadline = DateTime.UtcNow.AddSeconds(device.ExpiresIn > 0 ? device.ExpiresIn : 1800);

                while (DateTime.UtcNow < deadline) {
                    await Task.Delay(interval, ct).ConfigureAwait(false);

                    var poll = await _client.PollDeviceTokenAsync(device.DeviceCode, ct).ConfigureAwait(false);
                    if (poll.Kind == ApiResultKind.Success) {
                        await this.CompleteAuthorizationAsync(poll.Value, ct).ConfigureAwait(false);
                        return;
                    }
                    if (poll.Kind == ApiResultKind.NetworkError) {
                        continue;
                    }
                    if (poll.StatusCode == 400 && poll.Message == "authorization_pending") {
                        continue;
                    }
                    if (poll.StatusCode == 400 && poll.Message == "slow_down") {
                        // RFC 8628 に従い間隔を5秒延ばす
                        interval += TimeSpan.FromSeconds(5);
                        continue;
                    }
                    Plugin.Log.Warn($"Auth: authorization ended ({poll.StatusCode}: {poll.Message}).");
                    this.SetState(AuthState.Unauthenticated);
                    return;
                }

                // 期限切れ。設定画面から認証をやり直せる
                Plugin.Log.Info("Auth: the authorization code expired.");
                this.SetState(AuthState.Unauthenticated);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            }
            catch (Exception e) {
                Plugin.Log.Error($"Auth: authorization failed unexpectedly: {e}");
                this.SetState(AuthState.Unauthenticated);
            }
            finally {
                BrowserOpener.CancelPending();
                Interlocked.Exchange(ref _deviceFlowRunning, 0);
            }
        }

        private async Task CompleteAuthorizationAsync(TokenResponse response, CancellationToken ct)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try {
                if (string.IsNullOrEmpty(response.AccessToken) || string.IsNullOrEmpty(response.RefreshToken)) {
                    Plugin.Log.Error("Auth: token response is missing required fields.");
                    this.SetState(AuthState.Unauthenticated);
                    return;
                }
                var token = new TokenData {
                    ClientId = TwitchConstants.ClientId,
                    AccessToken = response.AccessToken,
                    RefreshToken = response.RefreshToken,
                    ExpiresAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + response.ExpiresIn,
                    Scopes = response.GetScopes(),
                };
                this.TrySave(token);
                _token = token;
                Plugin.Log.Info("Auth: authorization completed.");

#if DEBUG
                Plugin.Log.Notice("[DEBUG] Auth: forcing a refresh right after authorization.");
                if (await this.RefreshCoreAsync("debug: after authorization", ct).ConfigureAwait(false) == RefreshOutcome.Lost) {
                    return;
                }
#endif
                await this.ValidateCoreAsync(allowRefresh: true, ct).ConfigureAwait(false);
            }
            finally {
                _lock.Release();
            }
        }

        // 以下のメソッドは _lock を持っている間に呼ぶ

        private async Task<RefreshOutcome> RefreshCoreAsync(string reason, CancellationToken ct)
        {
            var old = _token;
            var result = await _client.RefreshAsync(old.RefreshToken, ct).ConfigureAwait(false);

            if (result.Kind == ApiResultKind.Success) {
                var r = result.Value;
                if (string.IsNullOrEmpty(r.AccessToken) || string.IsNullOrEmpty(r.RefreshToken)) {
                    // 古い更新用トークンは使用済みの可能性が高い
                    this.LoseToken($"refresh response is missing required fields ({reason})");
                    return RefreshOutcome.Lost;
                }
                var token = new TokenData {
                    ClientId = TwitchConstants.ClientId,
                    AccessToken = r.AccessToken,
                    RefreshToken = r.RefreshToken,
                    ExpiresAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + r.ExpiresIn,
                    Scopes = r.GetScopes() ?? old.Scopes,
                    UserId = old.UserId,
                    Login = old.Login,
                    DisplayName = old.DisplayName,
                };
                // 更新用トークンは1回限り。使う前に必ず保存する
                this.TrySave(token);
                _token = token;
                _nextRefreshRetryUtc = DateTime.MinValue;
                Plugin.Log.Info($"Auth: token refreshed ({reason}). Expires in {r.ExpiresIn} s.");
                return RefreshOutcome.Ok;
            }

            if (result.Kind == ApiResultKind.HttpError && (result.StatusCode == 400 || result.StatusCode == 401)) {
                this.LoseToken($"refresh was rejected ({reason}; {result.StatusCode}: {result.Message})");
                return RefreshOutcome.Lost;
            }

            _nextRefreshRetryUtc = DateTime.UtcNow + RetryDelay;
            Plugin.Log.Warn($"Auth: refresh failed temporarily ({reason}; {result.Kind} {result.StatusCode}: {result.Message}). Retrying later.");
            return RefreshOutcome.Transient;
        }

        private async Task ValidateCoreAsync(bool allowRefresh, CancellationToken ct)
        {
            var result = await _client.ValidateAsync(_token.AccessToken, ct).ConfigureAwait(false);
            var now = DateTime.UtcNow;

            if (result.Kind == ApiResultKind.Success) {
                var v = result.Value;
                if (v.ClientId != TwitchConstants.ClientId) {
                    this.LoseToken("token belongs to a different client");
                    return;
                }
                var scopes = v.GetScopes() ?? _token.Scopes ?? Array.Empty<string>();
#if DEBUG
                Plugin.Log.Notice($"[DEBUG] Auth: granted scopes = {string.Join(" ", scopes)}");
                var missing = _requiredScopes.Where(s => !scopes.Contains(s)).ToArray();
                if (missing.Length > 0) {
                    Plugin.Log.Notice($"[DEBUG] Auth: MISSING scopes = {string.Join(" ", missing)}");
                }
#endif
                var userChanged = _token.UserId != v.UserId;
                if (userChanged || _token.Login != v.Login || string.IsNullOrEmpty(_token.DisplayName)) {
                    var displayName = await HelixClient.GetDisplayNameAsync(v.UserId, _token.AccessToken, ct).ConfigureAwait(false);
                    _token.UserId = v.UserId;
                    _token.Login = v.Login;
                    if (displayName != null) {
                        _token.DisplayName = displayName;
                    }
                    else if (userChanged) {
                        _token.DisplayName = null;
                    }
                    this.TrySave(_token);
                }
                _grantedScopes = scopes;
                var current = _user;
                if (current == null || current.Id != v.UserId || current.Login != v.Login
                    || current.DisplayName != (_token.DisplayName ?? v.Login)) {
                    _user = new UserRef(v.UserId, v.Login, _token.DisplayName ?? v.Login);
                }
                _nextValidateUtc = now + ValidateInterval;
                this.SetState(AuthState.Authenticated);
                return;
            }

            if (result.Kind == ApiResultKind.HttpError && result.StatusCode == 401) {
                // トークンは常に有効に保つため、401 は原則として取り消し。念のため1回だけ更新する
                if (allowRefresh) {
                    var outcome = await this.RefreshCoreAsync("validate returned 401", ct).ConfigureAwait(false);
                    if (outcome == RefreshOutcome.Ok) {
                        await this.ValidateCoreAsync(allowRefresh: false, ct).ConfigureAwait(false);
                    }
                    else if (outcome == RefreshOutcome.Transient) {
                        _nextValidateUtc = now + RetryDelay;
                    }
                    return;
                }
                this.LoseToken("validate returned 401 after refresh");
                return;
            }

            _nextValidateUtc = now + RetryDelay;
            Plugin.Log.Warn($"Auth: validate failed temporarily ({result.Kind} {result.StatusCode}: {result.Message}). Retrying later.");
        }

        /// <summary>トークンを破棄し、認証をやり直す。</summary>
        private void LoseToken(string reason)
        {
            Plugin.Log.Warn($"Auth: token is no longer valid ({reason}). Re-authorization is required.");
            _token = null;
            _user = null;
            _grantedScopes = Array.Empty<string>();
            _nextValidateUtc = DateTime.MaxValue;
            TokenStore.Delete();
            this.SetState(AuthState.Unauthenticated);
            this.StartAuthentication();
        }

        private void TrySave(TokenData token)
        {
            try {
                TokenStore.Save(token);
            }
            catch (Exception e) {
                // メモリ上のトークンで動作を続ける。次の起動時は再認証になることがある
                Plugin.Log.Error($"Auth: failed to save the token ({e.GetType().Name}: {e.Message}).");
            }
        }

        private bool NeedsRefresh(DateTime nowUtc) => _token != null && nowUtc >= _token.ExpiresAtUtc - RefreshMargin;

        private bool IsUsable(TokenData token, out string reason)
        {
            if (token.ClientId != TwitchConstants.ClientId) {
                reason = "issued for a different client";
                return false;
            }
            if (string.IsNullOrEmpty(token.AccessToken) || string.IsNullOrEmpty(token.RefreshToken)) {
                reason = "incomplete";
                return false;
            }
            var granted = token.Scopes ?? Array.Empty<string>();
            if (_requiredScopes.Any(s => !granted.Contains(s))) {
                reason = "required scopes changed";
                return false;
            }
            reason = null;
            return true;
        }

        private void SetState(AuthState state)
        {
            if (_state == state) {
                return;
            }
            _state = state;
            Plugin.Log.Info($"Auth: state = {state}");
            TTVCoreApi.Connection.RaiseAuthStateChanged(state);
        }
    }
}
