using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace TTVCore.EventSub
{
    internal sealed class SubscriptionDefinition
    {
        public string Type { get; }
        public string Version { get; }
        private readonly Func<string, JObject> _condition;

        public SubscriptionDefinition(string type, string version, Func<string, JObject> condition)
        {
            this.Type = type;
            this.Version = version;
            _condition = condition;
        }

        /// <summary>配信者のユーザー ID から condition を作る。</summary>
        public JObject BuildCondition(string broadcasterUserId) => _condition(broadcasterUserId);

        public override string ToString() => $"{this.Type} v{this.Version}";
    }

    internal static class SubscriptionDefinitions
    {
        private static JObject Broadcaster(string id) => new JObject { ["broadcaster_user_id"] = id };

        // チャットは配信者自身のアカウントとして読む
        private static JObject ChatAsBroadcaster(string id) => new JObject { ["broadcaster_user_id"] = id, ["user_id"] = id };

        public static readonly IReadOnlyList<SubscriptionDefinition> All = new[] {
            new SubscriptionDefinition("channel.channel_points_custom_reward_redemption.add", "1", Broadcaster),
            // 配信者のチャンネルへのレイド
            new SubscriptionDefinition("channel.raid", "1", id => new JObject { ["to_broadcaster_user_id"] = id }),
            // Cheer と Power-up。channel.cheer は Cheer が重複するため使わない
            new SubscriptionDefinition("channel.bits.use", "1", Broadcaster),
            new SubscriptionDefinition("channel.subscribe", "1", Broadcaster),
            new SubscriptionDefinition("channel.subscription.message", "1", Broadcaster),
            new SubscriptionDefinition("channel.subscription.gift", "1", Broadcaster),
            new SubscriptionDefinition("channel.hype_train.begin", "2", Broadcaster),
            new SubscriptionDefinition("channel.hype_train.progress", "2", Broadcaster),
            new SubscriptionDefinition("channel.hype_train.end", "2", Broadcaster),
            new SubscriptionDefinition("channel.ad_break.begin", "1", Broadcaster),
            new SubscriptionDefinition("channel.chat.message", "1", ChatAsBroadcaster),
            new SubscriptionDefinition("channel.chat.notification", "1", ChatAsBroadcaster),
            new SubscriptionDefinition("channel.chat.message_delete", "1", ChatAsBroadcaster),
            new SubscriptionDefinition("channel.chat.clear_user_messages", "1", ChatAsBroadcaster),
            new SubscriptionDefinition("channel.chat.clear", "1", ChatAsBroadcaster),
            new SubscriptionDefinition("channel.shared_chat.begin", "1", Broadcaster),
            new SubscriptionDefinition("channel.shared_chat.update", "1", Broadcaster),
            new SubscriptionDefinition("channel.shared_chat.end", "1", Broadcaster),
        };
    }
}
