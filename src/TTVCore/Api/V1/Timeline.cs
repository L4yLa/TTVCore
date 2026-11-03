using System;
using System.Collections.Generic;

namespace TTVCore.Api.V1
{
    /// <summary>表示用の流れの1件。</summary>
    public abstract class TimelineItem
    {
        /// <summary>組の中で最も早いものの時刻。</summary>
        public DateTimeOffset Timestamp { get; internal set; }
        public string BroadcasterId { get; internal set; }
        /// <summary>共有チャットで他のチャンネルから届いた場合のみ。</summary>
        public SourceChannel Source { get; internal set; }
    }

    /// <summary>通常のチャットのメッセージ。通知と組になったメッセージはこちらでは届かない。</summary>
    public sealed class TimelineMessage : TimelineItem
    {
        public ChatMessage Message { get; internal set; }
    }

    /// <summary>通知。チャットの側と通知の側を組にしたもの、または片方だけのもの。</summary>
    public sealed class TimelineNotification : TimelineItem
    {
        public TimelineNotificationKind Kind { get; internal set; }
        /// <summary>通知の側（Notifications）。ない場合は null。</summary>
        public NotificationBase Notification { get; internal set; }
        /// <summary>チャット上の通知の側。ない場合は null。</summary>
        public ChatNotice Notice { get; internal set; }
        /// <summary>添えられたチャットのメッセージ。ない場合は null。</summary>
        public ChatMessage Message { get; internal set; }
        /// <summary>まとめてギフトの、受け取った人ごとの通知。</summary>
        public IReadOnlyList<ChatNotice> GiftNotices { get; internal set; } = Array.Empty<ChatNotice>();
        /// <summary>組になる出来事で、両方がそろったか。組にならない出来事では true。</summary>
        public bool IsComplete { get; internal set; }
    }

    public enum TimelineNotificationKind
    {
        Unknown = 0,
        Sub,
        Resub,
        SubGift,
        CommunitySubGift,
        Raid,
        Cheer,
        PowerUp,
        ChannelPointsRedemption,
        HypeTrain,
        AdBreak,
        ChatNoticeOnly,
    }
}
