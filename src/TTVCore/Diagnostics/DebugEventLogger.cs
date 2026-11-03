#if DEBUG
using System.Linq;
using System.Threading;
using TTVCore.Api.V1;

namespace TTVCore.Diagnostics
{
    /// <summary>Debug ビルドのみ。変換後のイベントの内容と、メインスレッドで発生したかをログに出す。</summary>
    internal static class DebugEventLogger
    {
        private static int s_mainThreadId;

        public static void Attach()
        {
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            var c = TTVCoreApi.Connection;
            var n = TTVCoreApi.Notifications;

            c.AuthStateChanged += s => Log($"AuthStateChanged {s} user={Name(c.LoggedInUser)} id={c.LoggedInUser?.Id ?? "null"} readChat={c.CanReadChat} sendChat={c.CanSendChat}");
            c.EventSubStateChanged += s => Log($"EventSubStateChanged {s}");
            n.ConnectionStateChanged += v => Log($"ConnectionStateChanged connected={v}");
            n.ChannelPointsRedeemed += e => Log(
                $"ChannelPointsRedeemed user={Name(e.User)} reward='{e.RewardTitle}' cost={e.RewardCost} input={Q(e.UserInput)} status={e.Status} at={e.RedeemedAt:O} broadcaster={e.BroadcasterId}");
            n.Raided += e => Log($"Raided from={Name(e.FromBroadcaster)} viewers={e.Viewers} broadcaster={e.BroadcasterId}");
            n.BitsUsed += e => Log(
                $"BitsUsed user={Name(e.User)} bits={e.Bits} type={e.Type} powerUp={(e.PowerUp?.ToString() ?? "null")} custom={Q(e.CustomPowerUpTitle)} message={Q(e.MessageText)}");
            n.Subscribed += e => Log($"Subscribed user={Name(e.User)} tier={e.Tier} isGift={e.IsGift}");
            n.Resubscribed += e => Log(
                $"Resubscribed user={Name(e.User)} tier={e.Tier} months={e.CumulativeMonths} streak={(e.StreakMonths?.ToString() ?? "null")} message={Q(e.MessageText)}");
            n.SubscriptionGifted += e => Log(
                $"SubscriptionGifted gifter={Name(e.Gifter)} anonymous={e.IsAnonymous} total={e.Total} tier={e.Tier} cumulative={(e.CumulativeTotal?.ToString() ?? "null")}");
            n.HypeTrainBegan += e => Log($"HypeTrainBegan level={e.Level} type={e.TrainType} total={e.Total} progress={e.Progress}/{e.Goal}");
            n.HypeTrainProgressed += e => Log($"HypeTrainProgressed level={e.Level} total={e.Total} progress={e.Progress}/{e.Goal}");
            n.HypeTrainEnded += e => Log(
                $"HypeTrainEnded level={e.Level} type={e.TrainType} total={e.Total} top=[{string.Join(", ", e.TopContributions.Select(x => $"{Name(x.User)}:{x.Type}:{x.Total}"))}]");
            n.AdBreakBegan += e => Log($"AdBreakBegan duration={e.DurationSeconds}s started={e.StartedAt:O} estimatedEnd={e.EstimatedEndAt:O} automatic={e.IsAutomatic} requester={Name(e.Requester)}");
            n.AdBreakEnded += e => Log($"AdBreakEnded (estimated) duration={e.DurationSeconds}s");
        }

        private static string Name(UserRef user) => user == null ? "null" : $"{user.DisplayName}({user.Login})";

        private static string Q(string s) => s == null ? "null" : $"'{s}'";

        private static void Log(string text)
        {
            var onMain = Thread.CurrentThread.ManagedThreadId == s_mainThreadId;
            Plugin.Log.Notice($"[DEBUG] Event: {text}{(onMain ? "" : " (NOT ON MAIN THREAD)")}");
        }
    }
}
#endif
