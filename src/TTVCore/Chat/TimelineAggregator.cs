using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TTVCore.Api.V1;
using TTVCore.EventSub;

namespace TTVCore.Chat
{
    /// <summary>
    /// 同じ出来事について Chat と Notifications に別々に届くものを組にして、表示用の流れに送る。
    /// 先に届いた方を受け取ったら、もう片方を一定時間待つ。片方の購読ができていなければ待たない。
    /// </summary>
    internal sealed class TimelineAggregator
    {
        // もう片方を待つ時間。暫定値
        private static readonly TimeSpan PairWait = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);

        public static TimelineAggregator Instance { get; } = new TimelineAggregator();

        private sealed class Pending
        {
            public TimelineNotificationKind Kind;
            public string Key;
            public DateTime Deadline;
            public NotificationBase Notification;
            public ChatNotice Notice;
            public ChatMessage Message;
            public bool NeedsNotification;
            public bool NeedsChat;
            public string CommunityGiftId;
            public int ExpectedGifts;
            public List<ChatNotice> Gifts;
            public bool Absorb;
        }

        private readonly object _gate = new object();
        private readonly List<Pending> _pending = new List<Pending>();
        private CancellationToken _ct;

        public void Start(CancellationToken ct)
        {
            _ct = ct;
            _ = Task.Run(this.TickLoopAsync);
        }

        // ================= チャットの側 =================

        public void OnMessage(ChatMessage m)
        {
            var userId = m.Sender?.Id;
            if (m.Source == null && userId != null) {
                if (m.Bits.HasValue && m.Bits.Value > 0) {
                    if (this.Match(TimelineNotificationKind.Cheer, $"{userId}:{m.Bits.Value}", message: m)) {
                        return;
                    }
                }
                else if (m.MessageType == ChatMessageType.PowerUpsMessageEffect || m.MessageType == ChatMessageType.PowerUpsGigantifiedEmote) {
                    var type = m.MessageType == ChatMessageType.PowerUpsMessageEffect ? PowerUpType.MessageEffect : PowerUpType.GigantifyAnEmote;
                    if (this.Match(TimelineNotificationKind.PowerUp, $"{userId}:{type}", message: m)) {
                        return;
                    }
                }
                else if (m.CustomRewardId != null) {
                    if (this.Match(TimelineNotificationKind.ChannelPointsRedemption, $"{userId}:{m.CustomRewardId}:{m.Text}", message: m)) {
                        return;
                    }
                }
            }
            Emit(new TimelineMessage { Message = m, Timestamp = m.Timestamp, BroadcasterId = m.BroadcasterId, Source = m.Source });
        }

        public void OnNotice(ChatNotice n)
        {
            // 知らない種類（Twitch 側で増えたものなど）は表示の流れに入れない。NoticeReceived には届く
            if (n.NoticeType == ChatNoticeType.Unknown) {
                return;
            }
            if (n.Source != null || n.IsSharedChat) {
                // 他のチャンネルの出来事には通知の側がない
                EmitSingle(KindOf(n.NoticeType), notice: n);
                return;
            }
            switch (n.NoticeType) {
                case ChatNoticeType.Sub:
                    this.Match(TimelineNotificationKind.Sub, n.Chatter?.Id, notice: n);
                    return;
                case ChatNoticeType.Resub:
                    this.Match(TimelineNotificationKind.Resub, n.Chatter?.Id, notice: n);
                    return;
                case ChatNoticeType.Raid:
                    this.Match(TimelineNotificationKind.Raid, (n.Detail as RaidNoticeDetail)?.Raider?.Id, notice: n);
                    return;
                case ChatNoticeType.CommunitySubGift:
                    this.OnCommunityGiftNotice(n);
                    return;
                case ChatNoticeType.SubGift:
                    this.OnSubGiftNotice(n);
                    return;
                default:
                    EmitSingle(TimelineNotificationKind.ChatNoticeOnly, notice: n);
                    return;
            }
        }

        // ================= 通知の側 =================

        public void OnNotification(NotificationBase n)
        {
            if (n is SubscribeNotification s) {
                if (s.IsGift) {
                    this.Match(TimelineNotificationKind.SubGift, s.User?.Id, notification: s);
                }
                else {
                    this.Match(TimelineNotificationKind.Sub, s.User?.Id, notification: s);
                }
            }
            else if (n is ResubscribeNotification r) {
                this.Match(TimelineNotificationKind.Resub, r.User?.Id, notification: r);
            }
            else if (n is SubscriptionGiftNotification g) {
                this.Match(TimelineNotificationKind.CommunitySubGift, g.IsAnonymous ? "anonymous" : g.Gifter?.Id, notification: g);
            }
            else if (n is RaidNotification raid) {
                this.Match(TimelineNotificationKind.Raid, raid.FromBroadcaster?.Id, notification: raid);
            }
            else if (n is BitsUseNotification b) {
                if (b.Type == BitsUseType.Cheer) {
                    this.Match(TimelineNotificationKind.Cheer, $"{b.User?.Id}:{b.Bits}", notification: b);
                }
                else if (b.Type == BitsUseType.PowerUp && (b.PowerUp == PowerUpType.MessageEffect || b.PowerUp == PowerUpType.GigantifyAnEmote)) {
                    this.Match(TimelineNotificationKind.PowerUp, $"{b.User?.Id}:{b.PowerUp}", notification: b);
                }
                else if (b.Type == BitsUseType.CustomPowerUp || (b.Type == BitsUseType.PowerUp && b.PowerUp == PowerUpType.Celebration)) {
                    // セレブレーション、カスタム Power-up。チャットの側はない
                    EmitSingle(TimelineNotificationKind.PowerUp, notification: b);
                }
                // 知らない種類のビッツの使い方・Power-up は表示の流れに入れない。BitsUsed には届く
            }
            else if (n is ChannelPointsRedemptionNotification c) {
                if (c.UserInput != null) {
                    this.Match(TimelineNotificationKind.ChannelPointsRedemption, $"{c.User?.Id}:{c.RewardId}:{c.UserInput}", notification: c);
                }
                else {
                    EmitSingle(TimelineNotificationKind.ChannelPointsRedemption, notification: c);
                }
            }
            else if (n is HypeTrainNotification h) {
                // 進行は表示の流れには入れない
                if (h.SubscriptionType == "channel.hype_train.begin") {
                    EmitSingle(TimelineNotificationKind.HypeTrain, notification: h);
                }
            }
            else if (n is HypeTrainEndNotification) {
                EmitSingle(TimelineNotificationKind.HypeTrain, notification: n);
            }
            else if (n is AdBreakNotification) {
                EmitSingle(TimelineNotificationKind.AdBreak, notification: n);
            }
        }

        // ================= ギフト =================

        private void OnCommunityGiftNotice(ChatNotice n)
        {
            var detail = n.Detail as CommunitySubGiftNoticeDetail;
            var key = n.IsAnonymous ? "anonymous" : n.Chatter?.Id;
            lock (_gate) {
                var p = this.Find(TimelineNotificationKind.CommunitySubGift, key, wantsNotice: true)
                        ?? this.Add(TimelineNotificationKind.CommunitySubGift, key, needsNotification: IsActive("channel.subscription.gift"), needsChat: false);
                p.Notice = n;
                p.NeedsChat = false;
                p.CommunityGiftId = detail?.CommunityGiftId;
                p.ExpectedGifts = detail?.Total ?? 0;
                p.Gifts = p.Gifts ?? new List<ChatNotice>();
                // 受け取った人ごとの通知を集めるため、この時点から待ち直す
                p.Deadline = DateTime.UtcNow + PairWait;
                foreach (var orphan in _pending.Where(x => x.Kind == TimelineNotificationKind.SubGift && x.Notice != null
                                                           && (x.Notice.Detail as SubGiftNoticeDetail)?.CommunityGiftId == p.CommunityGiftId
                                                           && p.CommunityGiftId != null).ToList()) {
                    p.Gifts.Add(orphan.Notice);
                    _pending.Remove(orphan);
                }
                this.TryCompleteCommunityGift(p);
            }
        }

        private void OnSubGiftNotice(ChatNotice n)
        {
            var detail = n.Detail as SubGiftNoticeDetail;
            lock (_gate) {
                if (detail?.CommunityGiftId != null) {
                    var group = _pending.FirstOrDefault(x => x.Kind == TimelineNotificationKind.CommunitySubGift && x.CommunityGiftId == detail.CommunityGiftId);
                    // 受け取った人の channel.subscribe（is_gift）は、まとめてギフトに含まれるので表示しない
                    this.AbsorbGiftedSubscribe(detail.Recipient?.Id);
                    if (group != null) {
                        group.Gifts.Add(n);
                        this.TryCompleteCommunityGift(group);
                    }
                    else {
                        // 見出しの通知より先に届いた。見出しを待つ
                        var p = this.Add(TimelineNotificationKind.SubGift, detail.Recipient?.Id, needsNotification: false, needsChat: false);
                        p.Notice = n;
                        p.Absorb = true;
                    }
                    return;
                }
            }
            this.Match(TimelineNotificationKind.SubGift, detail?.Recipient?.Id, notice: n);
        }

        private void AbsorbGiftedSubscribe(string recipientId)
        {
            var p = this.Find(TimelineNotificationKind.SubGift, recipientId, wantsNotice: true);
            if (p != null && p.Notification != null) {
                _pending.Remove(p);
            }
            else {
                // 後から届く channel.subscribe を捨てるための印
                var mark = this.Add(TimelineNotificationKind.SubGift, recipientId, needsNotification: true, needsChat: false);
                mark.Absorb = true;
            }
        }

        private void TryCompleteCommunityGift(Pending p)
        {
            if (p.Notice != null && !p.NeedsNotification && p.ExpectedGifts > 0 && p.Gifts.Count >= p.ExpectedGifts) {
                _pending.Remove(p);
                Emit(Build(p, complete: true));
            }
        }

        // ================= 組み合わせ =================

        /// <summary>同じ出来事の相手が待っていれば組にして送る。いなければ待つ（相手の購読がなければすぐ送る）。</summary>
        private bool Match(TimelineNotificationKind kind, string key, NotificationBase notification = null, ChatNotice notice = null, ChatMessage message = null)
        {
            var fromNotification = notification != null;
            Pending done = null;
            lock (_gate) {
                var p = this.Find(kind, key, wantsNotice: !fromNotification);
                if (p != null) {
                    if (p.Absorb) {
                        if (fromNotification) {
                            _pending.Remove(p);
                            return true;
                        }
                    }
                    else {
                        if (fromNotification) {
                            p.Notification = notification;
                            p.NeedsNotification = false;
                        }
                        else {
                            p.Notice = notice ?? p.Notice;
                            p.Message = message ?? p.Message;
                            p.NeedsChat = false;
                        }
                        if (kind == TimelineNotificationKind.CommunitySubGift) {
                            this.TryCompleteCommunityGift(p);
                            return true;
                        }
                        if (!p.NeedsChat && !p.NeedsNotification) {
                            _pending.Remove(p);
                            done = p;
                        }
                    }
                }
                if (done == null && (p == null || p.Absorb)) {
                    var counterpartActive = fromNotification ? IsChatActive(kind) : IsNotificationActive(kind);
                    if (key == null || !counterpartActive) {
                        done = new Pending { Kind = kind, Notification = notification, Notice = notice, Message = message };
                    }
                    else {
                        var added = this.Add(kind, key, needsNotification: !fromNotification, needsChat: fromNotification);
                        added.Notification = notification;
                        added.Notice = notice;
                        added.Message = message;
                        if (kind == TimelineNotificationKind.CommunitySubGift) {
                            added.Gifts = new List<ChatNotice>();
                        }
                        return true;
                    }
                }
            }
            if (done != null) {
                Emit(Build(done, complete: !done.NeedsChat && !done.NeedsNotification));
            }
            return true;
        }

        private Pending Find(TimelineNotificationKind kind, string key, bool wantsNotice)
        {
            if (key == null) {
                return null;
            }
            return _pending.FirstOrDefault(p => p.Kind == kind && p.Key == key
                                                && (wantsNotice ? p.NeedsChat || p.Absorb : p.NeedsNotification || p.Absorb));
        }

        private Pending Add(TimelineNotificationKind kind, string key, bool needsNotification, bool needsChat)
        {
            var p = new Pending {
                Kind = kind,
                Key = key,
                Deadline = DateTime.UtcNow + PairWait,
                NeedsNotification = needsNotification,
                NeedsChat = needsChat,
            };
            _pending.Add(p);
            return p;
        }

        private async Task TickLoopAsync()
        {
            try {
                while (true) {
                    await Task.Delay(TickInterval, _ct).ConfigureAwait(false);
                    List<Pending> expired;
                    lock (_gate) {
                        var now = DateTime.UtcNow;
                        expired = _pending.Where(p => p.Deadline <= now).ToList();
                        foreach (var p in expired) {
                            _pending.Remove(p);
                        }
                    }
                    foreach (var p in expired) {
                        if (p.Absorb && p.Notice == null) {
                            continue;
                        }
                        Emit(Build(p, complete: !p.NeedsChat && !p.NeedsNotification && !p.Absorb));
                    }
                }
            }
            catch (OperationCanceledException) {
            }
            catch (Exception e) {
                Plugin.Log.Error($"Timeline: stopped unexpectedly: {e}");
            }
        }

        private static TimelineNotification Build(Pending p, bool complete)
        {
            var times = new List<DateTimeOffset>();
            if (p.Notification != null) {
                times.Add(p.Notification.Timestamp);
            }
            if (p.Notice != null) {
                times.Add(p.Notice.Timestamp);
            }
            if (p.Message != null) {
                times.Add(p.Message.Timestamp);
            }
            var kind = p.Kind;
            if (p.Absorb) {
                // 見出しの通知が来なかったまとめてギフトの一部は、個別のギフトとして出す
                kind = TimelineNotificationKind.SubGift;
            }
            return new TimelineNotification {
                Kind = kind,
                Notification = p.Notification,
                Notice = p.Notice,
                Message = p.Message,
                GiftNotices = (IReadOnlyList<ChatNotice>)p.Gifts ?? Array.Empty<ChatNotice>(),
                IsComplete = complete,
                Timestamp = times.Count > 0 ? times.Min() : DateTimeOffset.UtcNow,
                BroadcasterId = p.Notification?.BroadcasterId ?? p.Notice?.BroadcasterId ?? p.Message?.BroadcasterId,
                Source = p.Notice?.Source ?? p.Message?.Source,
            };
        }

        private static void EmitSingle(TimelineNotificationKind kind, NotificationBase notification = null, ChatNotice notice = null)
        {
            Emit(Build(new Pending { Kind = kind, Notification = notification, Notice = notice }, complete: true));
        }

        private static void Emit(TimelineItem item)
        {
            TTVCoreApi.Chat.RaiseTimelineItemReceived(item);
        }

        private static TimelineNotificationKind KindOf(ChatNoticeType type)
        {
            switch (type) {
                case ChatNoticeType.Sub: return TimelineNotificationKind.Sub;
                case ChatNoticeType.Resub: return TimelineNotificationKind.Resub;
                case ChatNoticeType.SubGift: return TimelineNotificationKind.SubGift;
                case ChatNoticeType.CommunitySubGift: return TimelineNotificationKind.CommunitySubGift;
                case ChatNoticeType.Raid: return TimelineNotificationKind.Raid;
                default: return TimelineNotificationKind.ChatNoticeOnly;
            }
        }

        private static bool IsActive(string subscriptionType) => EventSubManager.Instance?.IsSubscribed(subscriptionType) ?? false;

        private static bool IsNotificationActive(TimelineNotificationKind kind)
        {
            switch (kind) {
                case TimelineNotificationKind.Sub:
                case TimelineNotificationKind.SubGift:
                    return IsActive("channel.subscribe");
                case TimelineNotificationKind.Resub:
                    return IsActive("channel.subscription.message");
                case TimelineNotificationKind.CommunitySubGift:
                    return IsActive("channel.subscription.gift");
                case TimelineNotificationKind.Raid:
                    return IsActive("channel.raid");
                case TimelineNotificationKind.Cheer:
                case TimelineNotificationKind.PowerUp:
                    return IsActive("channel.bits.use");
                case TimelineNotificationKind.ChannelPointsRedemption:
                    return IsActive("channel.channel_points_custom_reward_redemption.add");
                default:
                    return false;
            }
        }

        private static bool IsChatActive(TimelineNotificationKind kind)
        {
            switch (kind) {
                case TimelineNotificationKind.Cheer:
                case TimelineNotificationKind.PowerUp:
                case TimelineNotificationKind.ChannelPointsRedemption:
                    return IsActive("channel.chat.message");
                case TimelineNotificationKind.Sub:
                case TimelineNotificationKind.SubGift:
                case TimelineNotificationKind.Resub:
                case TimelineNotificationKind.CommunitySubGift:
                case TimelineNotificationKind.Raid:
                    return IsActive("channel.chat.notification");
                default:
                    return false;
            }
        }
    }
}
