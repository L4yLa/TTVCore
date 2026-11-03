using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TTVCore.Api.V1;
using TTVCore.Catalog;
using TTVCore.Images;
using TTVCore.Chat;
using TTVCore.Twitch;
using TTVCore.Utilities;

namespace TTVCore.EventSub
{
    /// <summary>
    /// EventSub の通知を API の型に変換してイベントを発生させる。
    /// 入れ子の項目は JSON の null で届くことがあるため、必ず <see cref="JsonUtil.At"/> で読む。
    /// </summary>
    internal sealed class NotificationDispatcher
    {
        // 広告は通常数分以内。値の異常で長時間タスクが残らないよう上限を設ける。
        private static readonly TimeSpan MaxAdEndDelay = TimeSpan.FromHours(1);

        private readonly CancellationToken _ct;

        private sealed class Header
        {
            public string Type;
            public string MessageId;
            public DateTimeOffset Timestamp;
            public string BroadcasterId;
            public string Raw;

            public T Apply<T>(T n) where T : NotificationBase
            {
                n.SubscriptionType = this.Type;
                n.MessageId = this.MessageId;
                n.Timestamp = this.Timestamp;
                n.BroadcasterId = this.BroadcasterId;
                n.RawJson = this.Raw;
                return n;
            }
        }

        public NotificationDispatcher(CancellationToken ct)
        {
            _ct = ct;
        }

        public void Dispatch(JObject message)
        {
            var type = JsonUtil.Str(JsonUtil.At(message, "metadata", "subscription_type"))
                       ?? JsonUtil.Str(JsonUtil.At(message, "payload", "subscription", "type"));
            var version = JsonUtil.Str(JsonUtil.At(message, "metadata", "subscription_version"))
                          ?? JsonUtil.Str(JsonUtil.At(message, "payload", "subscription", "version"));
            var messageId = JsonUtil.Str(JsonUtil.At(message, "metadata", "message_id"));
            var timestamp = JsonUtil.Time(JsonUtil.At(message, "metadata", "message_timestamp")) ?? DateTimeOffset.UtcNow;

            if (!(JsonUtil.At(message, "payload", "event") is JObject ev)) {
                Plugin.Log.Warn($"EventSub: notification without event ({type}).");
                return;
            }

            if (type != null && (type.StartsWith("channel.chat.", StringComparison.Ordinal) || type.StartsWith("channel.shared_chat.", StringComparison.Ordinal))) {
#if DEBUG
                Plugin.Log.Notice($"[DEBUG] EventSub: {type} v{version} {ev.ToString(Formatting.None)}");
#endif
                ChatDispatcher.Dispatch(type, ev, timestamp);
                return;
            }

            var raw = ev.ToString(Formatting.None);
#if DEBUG
            Plugin.Log.Notice($"[DEBUG] EventSub: {type} v{version} {raw}");
#endif
            var h = new Header {
                Type = type,
                MessageId = messageId,
                Timestamp = timestamp,
                BroadcasterId = JsonUtil.StrOrNull(ev["broadcaster_user_id"]) ?? JsonUtil.StrOrNull(ev["to_broadcaster_user_id"]),
                Raw = raw,
            };
            var api = TTVCoreApi.Notifications;

            api.RaiseNotificationReceived(h.Apply(new RawNotification { SubscriptionVersion = version }));

            try {
                switch (type) {
                    case "channel.channel_points_custom_reward_redemption.add":
                        { var n = h.Apply(ParseRedemption(ev)); api.RaiseChannelPointsRedeemed(n); TimelineAggregator.Instance.OnNotification(n); }
                        break;
                    case "channel.raid":
                        { var n = h.Apply(ParseRaid(ev)); api.RaiseRaided(n); TimelineAggregator.Instance.OnNotification(n); }
                        break;
                    case "channel.bits.use":
                        { var n = h.Apply(ParseBitsUse(ev)); api.RaiseBitsUsed(n); TimelineAggregator.Instance.OnNotification(n); }
                        break;
                    case "channel.subscribe":
                        { var n = h.Apply(ParseSubscribe(ev)); api.RaiseSubscribed(n); TimelineAggregator.Instance.OnNotification(n); }
                        break;
                    case "channel.subscription.message":
                        { var n = h.Apply(ParseResubscribe(ev)); api.RaiseResubscribed(n); TimelineAggregator.Instance.OnNotification(n); }
                        break;
                    case "channel.subscription.gift":
                        { var n = h.Apply(ParseGift(ev)); api.RaiseSubscriptionGifted(n); TimelineAggregator.Instance.OnNotification(n); }
                        break;
                    case "channel.hype_train.begin":
                        { var n = h.Apply(ParseHypeTrain(ev)); api.RaiseHypeTrainBegan(n); TimelineAggregator.Instance.OnNotification(n); }
                        break;
                    case "channel.hype_train.progress":
                        { var n = h.Apply(ParseHypeTrain(ev)); api.RaiseHypeTrainProgressed(n); TimelineAggregator.Instance.OnNotification(n); }
                        break;
                    case "channel.hype_train.end":
                        { var n = h.Apply(ParseHypeTrainEnd(ev)); api.RaiseHypeTrainEnded(n); TimelineAggregator.Instance.OnNotification(n); }
                        break;
                    case "channel.ad_break.begin": {
                        var ad = h.Apply(ParseAdBreak(ev, timestamp));
                        api.RaiseAdBreakBegan(ad);
                        TimelineAggregator.Instance.OnNotification(ad);
                        this.ScheduleAdBreakEnd(ad);
                        break;
                    }
                }
            }
            catch (Exception e) {
                // Twitch 側の変更で形が変わったときなど。読めない通知は捨てて続ける
                Plugin.Log.Warn($"EventSub: skipped {type} that could not be read ({e.GetType().Name}: {e.Message}).");
            }
        }

        private static ChannelPointsRedemptionNotification ParseRedemption(JObject ev)
        {
            return new ChannelPointsRedemptionNotification {
                RedemptionId = JsonUtil.Str(ev["id"]),
                User = TwitchParse.User(ev, "user"),
                UserInput = JsonUtil.StrOrNull(ev["user_input"]),
                Status = TwitchParse.RedemptionStatus(ev["status"]),
                RedeemedAt = JsonUtil.Time(ev["redeemed_at"]),
                RewardId = JsonUtil.Str(JsonUtil.At(ev, "reward", "id")),
                RewardTitle = JsonUtil.Str(JsonUtil.At(ev, "reward", "title")),
                RewardCost = JsonUtil.Int(JsonUtil.At(ev, "reward", "cost")),
                RewardPrompt = JsonUtil.StrOrNull(JsonUtil.At(ev, "reward", "prompt")),
            };
        }

        private static RaidNotification ParseRaid(JObject ev)
        {
            return new RaidNotification {
                FromBroadcaster = TwitchParse.User(ev, "from_broadcaster_user"),
                Viewers = JsonUtil.Int(ev["viewers"]),
            };
        }

        private static BitsUseNotification ParseBitsUse(JObject ev)
        {
            var type = TwitchParse.BitsUseType(ev["type"]);
            return new BitsUseNotification {
                User = TwitchParse.User(ev, "user"),
                Bits = JsonUtil.Int(ev["bits"]),
                Type = type,
                MessageText = JsonUtil.StrOrNull(JsonUtil.At(ev, "message", "text")),
                PowerUp = type == BitsUseType.PowerUp ? TwitchParse.PowerUpType(JsonUtil.At(ev, "power_up", "type")) : (PowerUpType?)null,
                CustomPowerUpTitle = JsonUtil.StrOrNull(JsonUtil.At(ev, "custom_power_up", "title")),
                PowerUpEmoteName = JsonUtil.StrOrNull(JsonUtil.At(ev, "power_up", "emote", "name")),
                PowerUpEmoteImage = PowerUpEmoteImage(ev),
            };
        }

        /// <summary>
        /// Power-up のエモートの画像。一覧にあればその画像（アニメーションの有無が分かる）、
        /// なければ ID から静止画として作る。
        /// </summary>
        private static ImageKey PowerUpEmoteImage(JObject ev)
        {
            var id = JsonUtil.StrOrNull(JsonUtil.At(ev, "power_up", "emote", "id"));
            if (string.IsNullOrEmpty(id)) {
                return default(ImageKey);
            }
            var name = JsonUtil.StrOrNull(JsonUtil.At(ev, "power_up", "emote", "name"));
            if (!string.IsNullOrEmpty(name)) {
                var channel = CatalogStore.GetChannel(JsonUtil.StrOrNull(ev["broadcaster_user_id"]));
                if ((channel.TwitchEmotes.TryGetValue(name, out var e) || CatalogStore.Global.TwitchEmotes.TryGetValue(name, out e))
                    && e.EmoteId == id) {
                    return e.Image;
                }
            }
            return ImageKeys.ForTwitchEmote(id, false);
        }

        private static SubscribeNotification ParseSubscribe(JObject ev)
        {
            return new SubscribeNotification {
                User = TwitchParse.User(ev, "user"),
                Tier = TwitchParse.Tier(ev["tier"]),
                IsGift = JsonUtil.Bool(ev["is_gift"]),
            };
        }

        private static ResubscribeNotification ParseResubscribe(JObject ev)
        {
            return new ResubscribeNotification {
                User = TwitchParse.User(ev, "user"),
                Tier = TwitchParse.Tier(ev["tier"]),
                MessageText = JsonUtil.StrOrNull(JsonUtil.At(ev, "message", "text")),
                CumulativeMonths = JsonUtil.Int(ev["cumulative_months"]),
                StreakMonths = JsonUtil.IntN(ev["streak_months"]),
                DurationMonths = JsonUtil.Int(ev["duration_months"]),
            };
        }

        private static SubscriptionGiftNotification ParseGift(JObject ev)
        {
            var anonymous = JsonUtil.Bool(ev["is_anonymous"]);
            return new SubscriptionGiftNotification {
                // Twitch CLI は匿名でも仮の名前を入れるため、is_anonymous で判断する。
                Gifter = anonymous ? null : TwitchParse.User(ev, "user"),
                IsAnonymous = anonymous,
                Total = JsonUtil.Int(ev["total"]),
                Tier = TwitchParse.Tier(ev["tier"]),
                CumulativeTotal = anonymous ? null : JsonUtil.IntN(ev["cumulative_total"]),
            };
        }

        private static HypeTrainNotification ParseHypeTrain(JObject ev)
        {
            return new HypeTrainNotification {
                HypeTrainId = JsonUtil.Str(ev["id"]),
                Level = JsonUtil.Int(ev["level"]),
                Total = JsonUtil.Int(ev["total"]),
                Progress = JsonUtil.Int(ev["progress"]),
                Goal = JsonUtil.Int(ev["goal"]),
                TrainType = TwitchParse.HypeTrainType(ev["type"]),
                IsSharedTrain = JsonUtil.Bool(ev["is_shared_train"]),
                StartedAt = JsonUtil.Time(ev["started_at"]),
                ExpiresAt = JsonUtil.Time(ev["expires_at"]),
            };
        }

        private static HypeTrainEndNotification ParseHypeTrainEnd(JObject ev)
        {
            var list = new List<HypeTrainContribution>();
            if (ev["top_contributions"] is JArray arr) {
                foreach (var c in arr) {
                    if (!(c is JObject)) {
                        continue;
                    }
                    list.Add(new HypeTrainContribution {
                        User = TwitchParse.User(c, "user"),
                        Type = TwitchParse.ContributionType(JsonUtil.At(c, "type")),
                        Total = JsonUtil.Int(JsonUtil.At(c, "total")),
                    });
                }
            }
            return new HypeTrainEndNotification {
                HypeTrainId = JsonUtil.Str(ev["id"]),
                Level = JsonUtil.Int(ev["level"]),
                Total = JsonUtil.Int(ev["total"]),
                TrainType = TwitchParse.HypeTrainType(ev["type"]),
                IsSharedTrain = JsonUtil.Bool(ev["is_shared_train"]),
                TopContributions = list,
                StartedAt = JsonUtil.Time(ev["started_at"]),
                EndedAt = JsonUtil.Time(ev["ended_at"]),
                CooldownEndsAt = JsonUtil.Time(ev["cooldown_ends_at"]),
            };
        }

        private static AdBreakNotification ParseAdBreak(JObject ev, DateTimeOffset messageTime)
        {
            var duration = JsonUtil.Int(ev["duration_seconds"]);
            var started = JsonUtil.Time(ev["started_at"]) ?? messageTime;
            return new AdBreakNotification {
                DurationSeconds = duration,
                StartedAt = started,
                EstimatedEndAt = started.AddSeconds(Math.Max(0, duration)),
                IsAutomatic = JsonUtil.Bool(ev["is_automatic"]),
                Requester = TwitchParse.User(ev, "requester_user"),
            };
        }

        private void ScheduleAdBreakEnd(AdBreakNotification ad)
        {
            var delay = ad.EstimatedEndAt - DateTimeOffset.UtcNow;
            if (delay < TimeSpan.Zero) {
                delay = TimeSpan.Zero;
            }
            else if (delay > MaxAdEndDelay) {
                delay = MaxAdEndDelay;
            }
            var ct = _ct;
            _ = Task.Run(async () => {
                try {
                    if (delay > TimeSpan.Zero) {
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    // 終了は開始とは別の出来事として、イベントとタイムラインの両方に送る
                    var end = new AdBreakNotification {
                        MessageId = ad.MessageId,
                        SubscriptionType = ad.SubscriptionType,
                        Timestamp = ad.EstimatedEndAt,
                        BroadcasterId = ad.BroadcasterId,
                        RawJson = ad.RawJson,
                        DurationSeconds = ad.DurationSeconds,
                        StartedAt = ad.StartedAt,
                        EstimatedEndAt = ad.EstimatedEndAt,
                        IsAutomatic = ad.IsAutomatic,
                        Requester = ad.Requester,
                        IsEstimatedEnd = true,
                    };
                    TTVCoreApi.Notifications.RaiseAdBreakEnded(end);
                    TimelineAggregator.Instance.OnNotification(end);
                }
                catch (OperationCanceledException) {
                }
            });
        }
    }
}
