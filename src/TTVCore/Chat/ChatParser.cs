using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TTVCore.Api.V1;
using TTVCore.Images;
using TTVCore.Twitch;
using TTVCore.Utilities;

namespace TTVCore.Chat
{
    /// <summary>channel.chat.* の event を API の型に変換する。</summary>
    internal static class ChatParser
    {
        private const string ActionPrefix = "\u0001ACTION ";
        private const char ActionSuffix = '\u0001';

        public static ChatMessage ParseMessage(JObject ev, DateTimeOffset timestamp)
        {
            var broadcasterId = JsonUtil.Str(ev["broadcaster_user_id"]);
            var source = ParseSource(ev, broadcasterId);
            var emoteChannelId = source?.Broadcaster.Id ?? broadcasterId;

            var fragments = ParseFragments(JsonUtil.At(ev, "message", "fragments"), emoteChannelId);
            var text = JsonUtil.Str(JsonUtil.At(ev, "message", "text")) ?? "";
            var isAction = false;
            if (text.StartsWith(ActionPrefix, StringComparison.Ordinal) && text.Length > ActionPrefix.Length && text[text.Length - 1] == ActionSuffix) {
                isAction = true;
            }
            // /me の印は fragments からは取り除かれているため、Text は fragments をつなげたものにそろえる
            text = string.Concat(fragments.Select(f => f.Text));

            return new ChatMessage {
                MessageId = JsonUtil.Str(ev["message_id"]),
                Timestamp = timestamp,
                BroadcasterId = broadcasterId,
                Source = source,
                Sender = ParseChatter(ev, broadcasterId),
                Text = text,
                Fragments = fragments,
                MessageType = ParseMessageType(ev["message_type"]),
                Bits = JsonUtil.IntN(JsonUtil.At(ev, "cheer", "bits")),
                Reply = ParseReply(ev["reply"]),
                CustomRewardId = JsonUtil.StrOrNull(ev["channel_points_custom_reward_id"]),
                MentionsBroadcaster = fragments.OfType<MentionFragment>().Any(m => m.User?.Id == broadcasterId),
                IsAction = isAction,
            };
        }

        public static ChatNotice ParseNotice(JObject ev, DateTimeOffset timestamp)
        {
            var broadcasterId = JsonUtil.Str(ev["broadcaster_user_id"]);
            var source = ParseSource(ev, broadcasterId);
            var anonymous = JsonUtil.Bool(ev["chatter_is_anonymous"]);
            var rawType = JsonUtil.Str(ev["notice_type"]) ?? "";
            var shared = rawType.StartsWith("shared_chat_", StringComparison.Ordinal);
            var baseType = shared ? rawType.Substring("shared_chat_".Length) : rawType;
            var detailObject = ev[rawType] as JObject;

            return new ChatNotice {
                MessageId = JsonUtil.Str(ev["message_id"]),
                Timestamp = timestamp,
                BroadcasterId = broadcasterId,
                Source = source,
                Chatter = anonymous ? null : ParseChatter(ev, broadcasterId),
                IsAnonymous = anonymous,
                NoticeType = ParseNoticeType(baseType),
                IsSharedChat = shared,
                SystemMessage = JsonUtil.Str(ev["system_message"]) ?? "",
                Fragments = ParseFragments(JsonUtil.At(ev, "message", "fragments"), source?.Broadcaster.Id ?? broadcasterId),
                Detail = ParseNoticeDetail(baseType, detailObject),
            };
        }

        public static MessageDeletedEvent ParseMessageDelete(JObject ev)
        {
            return new MessageDeletedEvent {
                BroadcasterId = JsonUtil.Str(ev["broadcaster_user_id"]),
                MessageId = JsonUtil.Str(ev["message_id"]),
                TargetUser = TwitchParse.User(ev, "target_user"),
            };
        }

        public static UserMessagesClearedEvent ParseClearUser(JObject ev)
        {
            return new UserMessagesClearedEvent {
                BroadcasterId = JsonUtil.Str(ev["broadcaster_user_id"]),
                TargetUser = TwitchParse.User(ev, "target_user"),
            };
        }

        public static ChatClearedEvent ParseClear(JObject ev)
        {
            return new ChatClearedEvent { BroadcasterId = JsonUtil.Str(ev["broadcaster_user_id"]) };
        }

        public static SharedChatSession ParseSharedChat(JObject ev)
        {
            var participants = new List<UserRef>();
            if (ev["participants"] is JArray arr) {
                foreach (var p in arr) {
                    var user = TwitchParse.User(p, "broadcaster_user");
                    if (user != null) {
                        participants.Add(user);
                    }
                }
            }
            return new SharedChatSession {
                SessionId = JsonUtil.Str(ev["session_id"]),
                HostBroadcasterId = JsonUtil.Str(ev["host_broadcaster_user_id"]),
                Participants = participants,
            };
        }

        private static SourceChannel ParseSource(JObject ev, string broadcasterId)
        {
            var source = TwitchParse.User(ev, "source_broadcaster_user");
            if (source == null || source.Id == broadcasterId) {
                return null;
            }
            return new SourceChannel {
                Broadcaster = source,
                MessageId = JsonUtil.StrOrNull(ev["source_message_id"]),
                Badges = ParseBadges(ev["source_badges"]),
                IsSourceOnly = JsonUtil.Bool(ev["is_source_only"]),
            };
        }

        private static ChatUser ParseChatter(JObject ev, string broadcasterId)
        {
            var badges = ParseBadges(ev["badges"]);
            var id = JsonUtil.Str(ev["chatter_user_id"]);
            var login = JsonUtil.StrOrNull(ev["chatter_user_login"]);
            var name = JsonUtil.StrOrNull(ev["chatter_user_name"]);
            var roles = RolesFromBadges(badges);
            if (id != null && id == broadcasterId) {
                roles |= ChatRoles.Broadcaster;
            }
            return new ChatUser {
                Id = id,
                Login = login ?? name ?? id,
                DisplayName = name ?? login ?? id,
                Color = JsonUtil.StrOrNull(ev["color"]),
                Badges = badges,
                Roles = roles,
            };
        }

        private static IReadOnlyList<BadgeRef> ParseBadges(JToken token)
        {
            if (!(token is JArray arr) || arr.Count == 0) {
                return Array.Empty<BadgeRef>();
            }
            var list = new List<BadgeRef>(arr.Count);
            foreach (var b in arr) {
                var setId = JsonUtil.StrOrNull(JsonUtil.At(b, "set_id"));
                if (setId == null) {
                    continue;
                }
                list.Add(new BadgeRef {
                    SetId = setId,
                    Id = JsonUtil.Str(JsonUtil.At(b, "id")),
                    Info = JsonUtil.StrOrNull(JsonUtil.At(b, "info")),
                });
            }
            return list;
        }

        private static ChatRoles RolesFromBadges(IReadOnlyList<BadgeRef> badges)
        {
            var roles = ChatRoles.None;
            foreach (var b in badges) {
                switch (b.SetId) {
                    case "broadcaster": roles |= ChatRoles.Broadcaster; break;
                    case "moderator": roles |= ChatRoles.Moderator; break;
                    case "lead_moderator": roles |= ChatRoles.Moderator | ChatRoles.LeadModerator; break;
                    case "vip": roles |= ChatRoles.Vip; break;
                    case "subscriber": roles |= ChatRoles.Subscriber; break;
                    case "founder": roles |= ChatRoles.Founder; break;
                    case "turbo": roles |= ChatRoles.Turbo; break;
                    case "staff": roles |= ChatRoles.Staff; break;
                }
            }
            return roles;
        }

        private static ReplyInfo ParseReply(JToken token)
        {
            if (!(token is JObject r)) {
                return null;
            }
            return new ReplyInfo {
                ParentMessageId = JsonUtil.Str(r["parent_message_id"]),
                ParentText = JsonUtil.Str(r["parent_message_body"]) ?? "",
                ParentUser = TwitchParse.User(r, "parent_user"),
                ThreadMessageId = JsonUtil.Str(r["thread_message_id"]),
                ThreadUser = TwitchParse.User(r, "thread_user"),
            };
        }

        public static IReadOnlyList<ChatFragment> ParseFragments(JToken token, string emoteChannelId)
        {
            if (!(token is JArray arr) || arr.Count == 0) {
                return Array.Empty<ChatFragment>();
            }
            var list = new List<ChatFragment>(arr.Count);
            foreach (var f in arr) {
                var text = JsonUtil.Str(JsonUtil.At(f, "text")) ?? "";
                switch (JsonUtil.Str(JsonUtil.At(f, "type"))) {
                    case "text":
                        TextExpander.Expand(text, emoteChannelId, list);
                        break;
                    case "emote": {
                        var e = JsonUtil.At(f, "emote");
                        var id = JsonUtil.Str(JsonUtil.At(e, "id"));
                        var formats = JsonUtil.At(e, "format") as JArray;
                        var animated = formats != null && formats.Any(x => JsonUtil.Str(x) == "animated");
                        list.Add(id == null
                            ? (ChatFragment)new TextFragment(text)
                            : new EmoteFragment(text, id, JsonUtil.StrOrNull(JsonUtil.At(e, "emote_set_id")),
                                                JsonUtil.StrOrNull(JsonUtil.At(e, "owner_id")), animated,
                                                ImageKeys.ForTwitchEmote(id, animated)));
                        break;
                    }
                    case "cheermote": {
                        var c = JsonUtil.At(f, "cheermote");
                        var prefix = (JsonUtil.Str(JsonUtil.At(c, "prefix")) ?? "").ToLowerInvariant();
                        var tier = JsonUtil.Int(JsonUtil.At(c, "tier"));
                        list.Add(new CheermoteFragment(text, prefix, JsonUtil.Int(JsonUtil.At(c, "bits")), tier,
                                                       ImageKeys.ForCheermote(prefix, tier)));
                        break;
                    }
                    case "mention": {
                        var user = TwitchParse.User(JsonUtil.At(f, "mention"), "user");
                        list.Add(user == null ? (ChatFragment)new TextFragment(text) : new MentionFragment(text, user));
                        break;
                    }
                    case "gif": {
                        var g = JsonUtil.At(f, "gif");
                        var id = JsonUtil.StrOrNull(JsonUtil.At(g, "id")) ?? JsonUtil.StrOrNull(JsonUtil.At(g, "gif_id"));
                        list.Add(new GifFragment(text, id, JsonUtil.StrOrNull(JsonUtil.At(g, "url"))));
                        break;
                    }
                    default:
                        list.Add(new TextFragment(text));
                        break;
                }
            }
            return list;
        }

        private static ChatMessageType ParseMessageType(JToken token)
        {
            switch (JsonUtil.Str(token)) {
                case "text": return ChatMessageType.Text;
                case "channel_points_highlighted": return ChatMessageType.ChannelPointsHighlighted;
                case "channel_points_sub_only": return ChatMessageType.ChannelPointsSubOnly;
                case "user_intro": return ChatMessageType.UserIntro;
                case "power_ups_message_effect": return ChatMessageType.PowerUpsMessageEffect;
                case "power_ups_gigantified_emote": return ChatMessageType.PowerUpsGigantifiedEmote;
                default: return ChatMessageType.Unknown;
            }
        }

        private static ChatNoticeType ParseNoticeType(string type)
        {
            switch (type) {
                case "sub": return ChatNoticeType.Sub;
                case "resub": return ChatNoticeType.Resub;
                case "sub_gift": return ChatNoticeType.SubGift;
                case "community_sub_gift": return ChatNoticeType.CommunitySubGift;
                case "gift_paid_upgrade": return ChatNoticeType.GiftPaidUpgrade;
                case "prime_paid_upgrade": return ChatNoticeType.PrimePaidUpgrade;
                case "raid": return ChatNoticeType.Raid;
                case "unraid": return ChatNoticeType.Unraid;
                case "pay_it_forward": return ChatNoticeType.PayItForward;
                case "announcement": return ChatNoticeType.Announcement;
                case "bits_badge_tier": return ChatNoticeType.BitsBadgeTier;
                case "charity_donation": return ChatNoticeType.CharityDonation;
                case "watch_streak": return ChatNoticeType.WatchStreak;
                case "modiversary": return ChatNoticeType.Modiversary;
                case "gifted_drops_summary": return ChatNoticeType.GiftedDropsSummary;
                default: return ChatNoticeType.Unknown;
            }
        }

        private static ChatNoticeDetail ParseNoticeDetail(string type, JObject d)
        {
            if (d == null) {
                return null;
            }
            switch (type) {
                case "sub":
                    return new SubNoticeDetail {
                        Tier = TwitchParse.Tier(d["sub_tier"]),
                        IsPrime = JsonUtil.Bool(d["is_prime"]),
                        DurationMonths = JsonUtil.Int(d["duration_months"]),
                    };
                case "resub": {
                    var anonymous = JsonUtil.Bool(d["gifter_is_anonymous"]);
                    return new ResubNoticeDetail {
                        Tier = TwitchParse.Tier(d["sub_tier"]),
                        IsPrime = JsonUtil.Bool(d["is_prime"]),
                        CumulativeMonths = JsonUtil.Int(d["cumulative_months"]),
                        StreakMonths = JsonUtil.IntN(d["streak_months"]),
                        DurationMonths = JsonUtil.Int(d["duration_months"]),
                        IsGift = JsonUtil.Bool(d["is_gift"]),
                        GifterIsAnonymous = anonymous,
                        Gifter = anonymous ? null : TwitchParse.User(d, "gifter_user"),
                    };
                }
                case "sub_gift":
                    return new SubGiftNoticeDetail {
                        Tier = TwitchParse.Tier(d["sub_tier"]),
                        DurationMonths = JsonUtil.Int(d["duration_months"]),
                        CumulativeTotal = JsonUtil.IntN(d["cumulative_total"]),
                        Recipient = TwitchParse.User(d, "recipient_user"),
                        CommunityGiftId = JsonUtil.StrOrNull(d["community_gift_id"]),
                    };
                case "community_sub_gift":
                    return new CommunitySubGiftNoticeDetail {
                        CommunityGiftId = JsonUtil.Str(d["id"]),
                        Total = JsonUtil.Int(d["total"]),
                        Tier = TwitchParse.Tier(d["sub_tier"]),
                        CumulativeTotal = JsonUtil.IntN(d["cumulative_total"]),
                    };
                case "gift_paid_upgrade":
                case "pay_it_forward": {
                    var anonymous = JsonUtil.Bool(d["gifter_is_anonymous"]);
                    return new GiftPaidUpgradeNoticeDetail {
                        GifterIsAnonymous = anonymous,
                        Gifter = anonymous ? null : TwitchParse.User(d, "gifter_user"),
                    };
                }
                case "prime_paid_upgrade":
                    return new PrimePaidUpgradeNoticeDetail { Tier = TwitchParse.Tier(d["sub_tier"]) };
                case "raid":
                    return new RaidNoticeDetail {
                        Raider = TwitchParse.User(d, "user"),
                        ViewerCount = JsonUtil.Int(d["viewer_count"]),
                        ProfileImageUrl = JsonUtil.StrOrNull(d["profile_image_url"]),
                    };
                case "announcement":
                    return new AnnouncementNoticeDetail { Color = JsonUtil.StrOrNull(d["color"]) };
                case "bits_badge_tier":
                    return new BitsBadgeTierNoticeDetail { Tier = JsonUtil.Int(d["tier"]) };
                case "charity_donation": {
                    var amount = JsonUtil.At(d, "amount");
                    long value = 0;
                    long.TryParse(JsonUtil.Str(JsonUtil.At(amount, "value")), out value);
                    return new CharityDonationNoticeDetail {
                        CharityName = JsonUtil.Str(d["charity_name"]),
                        AmountValue = value,
                        AmountDecimalPlaces = JsonUtil.Int(JsonUtil.At(amount, "decimal_place")),
                        AmountCurrency = JsonUtil.Str(JsonUtil.At(amount, "currency")),
                    };
                }
                default:
                    return null;
            }
        }
    }
}
