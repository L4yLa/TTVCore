using System;
using TTVCore.EventSub;
using TTVCore.Utilities;

namespace TTVCore.Api.V1
{
    /// <summary>チャット以外の通知。各イベントは受信した時点で発生する。</summary>
    public sealed class NotificationsApi
    {
        internal NotificationsApi()
        {
        }

        /// <summary>EventSub に接続し、購読が完了しているか。</summary>
        public bool IsConnected => EventSubManager.Instance?.State == EventSubState.Connected;

        public event Action<bool> ConnectionStateChanged;

        /// <summary>すべての通知（変換しない種類を含む）。種類別のイベントより先に発生する。</summary>
        public event Action<RawNotification> NotificationReceived;

        public event Action<ChannelPointsRedemptionNotification> ChannelPointsRedeemed;
        public event Action<RaidNotification> Raided;
        public event Action<BitsUseNotification> BitsUsed;
        public event Action<SubscribeNotification> Subscribed;
        public event Action<ResubscribeNotification> Resubscribed;
        public event Action<SubscriptionGiftNotification> SubscriptionGifted;
        public event Action<HypeTrainNotification> HypeTrainBegan;
        public event Action<HypeTrainNotification> HypeTrainProgressed;
        public event Action<HypeTrainEndNotification> HypeTrainEnded;
        public event Action<AdBreakNotification> AdBreakBegan;

        /// <summary>広告の終了の目安（開始時刻＋長さ）に発生する。</summary>
        public event Action<AdBreakNotification> AdBreakEnded;

        internal void RaiseConnectionStateChanged(bool connected) =>
            EventInvoker.Post(() => this.ConnectionStateChanged, connected, "Notifications.ConnectionStateChanged");

        internal void RaiseNotificationReceived(RawNotification n) =>
            EventInvoker.Post(() => this.NotificationReceived, n, "Notifications.NotificationReceived");

        internal void RaiseChannelPointsRedeemed(ChannelPointsRedemptionNotification n) =>
            EventInvoker.Post(() => this.ChannelPointsRedeemed, n, "Notifications.ChannelPointsRedeemed");

        internal void RaiseRaided(RaidNotification n) =>
            EventInvoker.Post(() => this.Raided, n, "Notifications.Raided");

        internal void RaiseBitsUsed(BitsUseNotification n) =>
            EventInvoker.Post(() => this.BitsUsed, n, "Notifications.BitsUsed");

        internal void RaiseSubscribed(SubscribeNotification n) =>
            EventInvoker.Post(() => this.Subscribed, n, "Notifications.Subscribed");

        internal void RaiseResubscribed(ResubscribeNotification n) =>
            EventInvoker.Post(() => this.Resubscribed, n, "Notifications.Resubscribed");

        internal void RaiseSubscriptionGifted(SubscriptionGiftNotification n) =>
            EventInvoker.Post(() => this.SubscriptionGifted, n, "Notifications.SubscriptionGifted");

        internal void RaiseHypeTrainBegan(HypeTrainNotification n) =>
            EventInvoker.Post(() => this.HypeTrainBegan, n, "Notifications.HypeTrainBegan");

        internal void RaiseHypeTrainProgressed(HypeTrainNotification n) =>
            EventInvoker.Post(() => this.HypeTrainProgressed, n, "Notifications.HypeTrainProgressed");

        internal void RaiseHypeTrainEnded(HypeTrainEndNotification n) =>
            EventInvoker.Post(() => this.HypeTrainEnded, n, "Notifications.HypeTrainEnded");

        internal void RaiseAdBreakBegan(AdBreakNotification n) =>
            EventInvoker.Post(() => this.AdBreakBegan, n, "Notifications.AdBreakBegan");

        internal void RaiseAdBreakEnded(AdBreakNotification n) =>
            EventInvoker.Post(() => this.AdBreakEnded, n, "Notifications.AdBreakEnded");
    }
}
