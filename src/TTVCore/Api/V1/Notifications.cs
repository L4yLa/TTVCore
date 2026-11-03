using System;
using System.Collections.Generic;

namespace TTVCore.Api.V1
{
    /// <summary>EventSub から受け取る通知の共通部分。</summary>
    public abstract class NotificationBase
    {
        /// <summary>EventSub の message_id。</summary>
        public string MessageId { get; internal set; }

        /// <summary>購読の種類（例: channel.raid）。</summary>
        public string SubscriptionType { get; internal set; }

        /// <summary>Twitch がメッセージを送った時刻（UTC）。メッセージになければ受信した時刻。</summary>
        public DateTimeOffset Timestamp { get; internal set; }

        public string BroadcasterId { get; internal set; }

        /// <summary>受信した event オブジェクトの JSON。日付は文字列のまま。</summary>
        public string RawJson { get; internal set; }
    }

    /// <summary>受信したすべての通知（TTVCore が変換しない種類を含む）。</summary>
    public sealed class RawNotification : NotificationBase
    {
        public string SubscriptionVersion { get; internal set; }
    }

    public enum RedemptionStatus
    {
        Unknown = 0,
        Unfulfilled,
        Fulfilled,
        Canceled,
    }

    /// <summary>カスタム報酬の引き換え（channel.channel_points_custom_reward_redemption.add）。</summary>
    public sealed class ChannelPointsRedemptionNotification : NotificationBase
    {
        public string RedemptionId { get; internal set; }
        public UserRef User { get; internal set; }
        /// <summary>入力文。入力のない報酬では null。</summary>
        public string UserInput { get; internal set; }
        public RedemptionStatus Status { get; internal set; }
        public DateTimeOffset? RedeemedAt { get; internal set; }
        public string RewardId { get; internal set; }
        public string RewardTitle { get; internal set; }
        public int RewardCost { get; internal set; }
        /// <summary>説明がない報酬では null。</summary>
        public string RewardPrompt { get; internal set; }
    }

    /// <summary>配信者のチャンネルへのレイド（channel.raid）。</summary>
    public sealed class RaidNotification : NotificationBase
    {
        public UserRef FromBroadcaster { get; internal set; }
        public int Viewers { get; internal set; }
    }

    public enum BitsUseType
    {
        Unknown = 0,
        Cheer,
        PowerUp,
        CustomPowerUp,
    }

    public enum PowerUpType
    {
        Unknown = 0,
        MessageEffect,
        Celebration,
        GigantifyAnEmote,
    }

    /// <summary>ビッツの使用。Cheer と Power-up（channel.bits.use）。</summary>
    public sealed class BitsUseNotification : NotificationBase
    {
        /// <summary>Twitch が利用者を送らない場合は null。</summary>
        public UserRef User { get; internal set; }
        public int Bits { get; internal set; }
        public BitsUseType Type { get; internal set; }
        /// <summary>メッセージの平文。ない場合は null。</summary>
        public string MessageText { get; internal set; }
        /// <summary>Power-up の種類。<see cref="Type"/> が <see cref="BitsUseType.PowerUp"/> 以外なら null。</summary>
        public PowerUpType? PowerUp { get; internal set; }
        /// <summary><see cref="Type"/> が <see cref="BitsUseType.CustomPowerUp"/> 以外なら null。</summary>
        public string CustomPowerUpTitle { get; internal set; }
        /// <summary>Power-up に結び付いたエモート（セレブレーションなど）の名前。ない場合は null。</summary>
        public string PowerUpEmoteName { get; internal set; }
        /// <summary>Power-up に結び付いたエモートの画像。ない場合は無効な値（<see cref="ImageKey.IsValid"/> が false）。</summary>
        public ImageKey PowerUpEmoteImage { get; internal set; }
    }

    /// <summary>新規サブスク（channel.subscribe）。継続サブスクは含まない。</summary>
    public sealed class SubscribeNotification : NotificationBase
    {
        public UserRef User { get; internal set; }
        public SubTier Tier { get; internal set; }
        /// <summary>ギフトで受け取ったサブスクか。</summary>
        public bool IsGift { get; internal set; }
    }

    /// <summary>継続サブスクのメッセージ（channel.subscription.message）。</summary>
    public sealed class ResubscribeNotification : NotificationBase
    {
        public UserRef User { get; internal set; }
        public SubTier Tier { get; internal set; }
        /// <summary>メッセージがなければ null。</summary>
        public string MessageText { get; internal set; }
        public int CumulativeMonths { get; internal set; }
        /// <summary>本人が公開していなければ null。</summary>
        public int? StreakMonths { get; internal set; }
        public int DurationMonths { get; internal set; }
    }

    /// <summary>サブスクのギフト（channel.subscription.gift）。</summary>
    public sealed class SubscriptionGiftNotification : NotificationBase
    {
        /// <summary>匿名なら null。</summary>
        public UserRef Gifter { get; internal set; }
        public bool IsAnonymous { get; internal set; }
        /// <summary>今回贈った件数。</summary>
        public int Total { get; internal set; }
        public SubTier Tier { get; internal set; }
        /// <summary>このチャンネルで贈った累計。匿名・非公開なら null。</summary>
        public int? CumulativeTotal { get; internal set; }
    }

    public enum HypeTrainType
    {
        Unknown = 0,
        Regular,
        GoldenKappa,
        Treasure,
    }

    /// <summary>ハイプトレインの開始・進行（channel.hype_train.begin / progress v2）。</summary>
    public sealed class HypeTrainNotification : NotificationBase
    {
        public string HypeTrainId { get; internal set; }
        public int Level { get; internal set; }
        public int Total { get; internal set; }
        public int Progress { get; internal set; }
        public int Goal { get; internal set; }
        public HypeTrainType TrainType { get; internal set; }
        public bool IsSharedTrain { get; internal set; }
        public DateTimeOffset? StartedAt { get; internal set; }
        public DateTimeOffset? ExpiresAt { get; internal set; }
    }

    public enum HypeTrainContributionType
    {
        Unknown = 0,
        Bits,
        Subscription,
        Other,
    }

    public sealed class HypeTrainContribution
    {
        public UserRef User { get; internal set; }
        public HypeTrainContributionType Type { get; internal set; }
        /// <summary>ビッツならビッツ数。サブスクならティア 1 / 2 / 3 で 500 / 1000 / 2500。</summary>
        public int Total { get; internal set; }
    }

    /// <summary>ハイプトレインの終了（channel.hype_train.end v2）。</summary>
    public sealed class HypeTrainEndNotification : NotificationBase
    {
        public string HypeTrainId { get; internal set; }
        public int Level { get; internal set; }
        public int Total { get; internal set; }
        public HypeTrainType TrainType { get; internal set; }
        public bool IsSharedTrain { get; internal set; }
        public IReadOnlyList<HypeTrainContribution> TopContributions { get; internal set; } = Array.Empty<HypeTrainContribution>();
        public DateTimeOffset? StartedAt { get; internal set; }
        public DateTimeOffset? EndedAt { get; internal set; }
        public DateTimeOffset? CooldownEndsAt { get; internal set; }
    }

    /// <summary>
    /// 広告（channel.ad_break.begin）。開始時と、計算した終了時刻の両方でこの型を送る。
    /// Twitch に終了の通知はない。終了時に送るものは別のインスタンスで、<see cref="IsEstimatedEnd"/> が true、
    /// <see cref="NotificationBase.Timestamp"/> が終了の目安の時刻になる。
    /// </summary>
    public sealed class AdBreakNotification : NotificationBase
    {
        public int DurationSeconds { get; internal set; }
        public DateTimeOffset StartedAt { get; internal set; }
        /// <summary>StartedAt + DurationSeconds。視聴者に実際に出る広告とはずれることがある。</summary>
        public DateTimeOffset EstimatedEndAt { get; internal set; }
        public bool IsAutomatic { get; internal set; }
        /// <summary>自動の広告なら null。</summary>
        public UserRef Requester { get; internal set; }
        /// <summary>終了の目安の時刻に送ったもの（<c>AdBreakEnded</c> とタイムライン）なら true。開始時は false。</summary>
        public bool IsEstimatedEnd { get; internal set; }
    }
}
