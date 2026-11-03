using Newtonsoft.Json.Linq;
using TTVCore.Api.V1;
using TTVCore.Utilities;

namespace TTVCore.Twitch
{
    /// <summary>Twitch の項目の値を API の型に変換する。</summary>
    internal static class TwitchParse
    {
        /// <summary>
        /// "{prefix}_id"、"{prefix}_login"、"{prefix}_name" を読む。
        /// ID がなければ（匿名など）null。
        /// </summary>
        public static UserRef User(JToken obj, string prefix)
        {
            var id = JsonUtil.StrOrNull(JsonUtil.At(obj, prefix + "_id"));
            if (id == null) {
                return null;
            }
            var login = JsonUtil.StrOrNull(JsonUtil.At(obj, prefix + "_login"));
            var name = JsonUtil.StrOrNull(JsonUtil.At(obj, prefix + "_name"));
            return new UserRef(id, login ?? name ?? id, name ?? login ?? id);
        }

        public static SubTier Tier(JToken token)
        {
            switch (JsonUtil.Str(token)) {
                case "1000": return SubTier.Tier1;
                case "2000": return SubTier.Tier2;
                case "3000": return SubTier.Tier3;
                default: return SubTier.Unknown;
            }
        }

        public static RedemptionStatus RedemptionStatus(JToken token)
        {
            switch (JsonUtil.Str(token)) {
                case "unfulfilled": return Api.V1.RedemptionStatus.Unfulfilled;
                case "fulfilled": return Api.V1.RedemptionStatus.Fulfilled;
                case "canceled": return Api.V1.RedemptionStatus.Canceled;
                default: return Api.V1.RedemptionStatus.Unknown;
            }
        }

        public static BitsUseType BitsUseType(JToken token)
        {
            switch (JsonUtil.Str(token)) {
                case "cheer": return Api.V1.BitsUseType.Cheer;
                case "power_up": return Api.V1.BitsUseType.PowerUp;
                case "custom_power_up": return Api.V1.BitsUseType.CustomPowerUp;
                default: return Api.V1.BitsUseType.Unknown;
            }
        }

        public static PowerUpType PowerUpType(JToken token)
        {
            switch (JsonUtil.Str(token)) {
                case "message_effect": return Api.V1.PowerUpType.MessageEffect;
                case "celebration": return Api.V1.PowerUpType.Celebration;
                case "gigantify_an_emote": return Api.V1.PowerUpType.GigantifyAnEmote;
                default: return Api.V1.PowerUpType.Unknown;
            }
        }

        public static HypeTrainType HypeTrainType(JToken token)
        {
            switch (JsonUtil.Str(token)) {
                case "regular": return Api.V1.HypeTrainType.Regular;
                case "golden_kappa": return Api.V1.HypeTrainType.GoldenKappa;
                case "treasure": return Api.V1.HypeTrainType.Treasure;
                default: return Api.V1.HypeTrainType.Unknown;
            }
        }

        public static HypeTrainContributionType ContributionType(JToken token)
        {
            switch (JsonUtil.Str(token)) {
                case "bits": return HypeTrainContributionType.Bits;
                case "subscription": return HypeTrainContributionType.Subscription;
                case "other": return HypeTrainContributionType.Other;
                default: return HypeTrainContributionType.Unknown;
            }
        }
    }
}
