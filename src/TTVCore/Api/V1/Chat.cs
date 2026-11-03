using System;
using System.Collections.Generic;
using System.Linq;

namespace TTVCore.Api.V1
{
    /// <summary>チャットのメッセージ（channel.chat.message）。</summary>
    public sealed class ChatMessage
    {
        public string MessageId { get; internal set; }
        /// <summary>EventSub の message_timestamp。</summary>
        public DateTimeOffset Timestamp { get; internal set; }
        /// <summary>受信したチャンネル（配信者）。</summary>
        public string BroadcasterId { get; internal set; }
        /// <summary>共有チャットで他のチャンネルから届いた場合のみ。それ以外は null。</summary>
        public SourceChannel Source { get; internal set; }
        public ChatUser Sender { get; internal set; }
        /// <summary>本文全体の平文。<see cref="Fragments"/> を順につなげたものと同じ。</summary>
        public string Text { get; internal set; }
        public IReadOnlyList<ChatFragment> Fragments { get; internal set; } = Array.Empty<ChatFragment>();
        public ChatMessageType MessageType { get; internal set; }
        /// <summary>Cheer のビッツ数。Cheer でなければ null。</summary>
        public int? Bits { get; internal set; }
        /// <summary>返信でなければ null。</summary>
        public ReplyInfo Reply { get; internal set; }
        /// <summary>入力付きのチャンネルポイント報酬のメッセージなら、その報酬の ID。それ以外は null。</summary>
        public string CustomRewardId { get; internal set; }
        /// <summary>配信者へのメンションを含むか。</summary>
        public bool MentionsBroadcaster { get; internal set; }
        /// <summary>/me で送られたか。</summary>
        public bool IsAction { get; internal set; }
    }

    public enum ChatMessageType
    {
        Unknown = 0,
        Text,
        ChannelPointsHighlighted,
        ChannelPointsSubOnly,
        UserIntro,
        PowerUpsMessageEffect,
        PowerUpsGigantifiedEmote,
    }

    /// <summary>共有チャットで、他のチャンネルから届いたメッセージの発言元。</summary>
    public sealed class SourceChannel
    {
        public UserRef Broadcaster { get; internal set; }
        /// <summary>発言元のチャンネルでのメッセージ ID。</summary>
        public string MessageId { get; internal set; }
        /// <summary>発言元のチャンネルでのバッジ。</summary>
        public IReadOnlyList<BadgeRef> Badges { get; internal set; } = Array.Empty<BadgeRef>();
        public bool IsSourceOnly { get; internal set; }
    }

    /// <summary>チャットの送信者。</summary>
    public sealed class ChatUser
    {
        public string Id { get; internal set; }
        public string Login { get; internal set; }
        public string DisplayName { get; internal set; }
        /// <summary>"#RRGGBB"。未設定なら null。</summary>
        public string Color { get; internal set; }
        public IReadOnlyList<BadgeRef> Badges { get; internal set; } = Array.Empty<BadgeRef>();
        /// <summary>バッジから求めた立場。</summary>
        public ChatRoles Roles { get; internal set; }

        public bool HasBadge(string setId) => this.Badges.Any(b => b.SetId == setId);
    }

    [Flags]
    public enum ChatRoles
    {
        None = 0,
        Broadcaster = 1,
        Moderator = 2,
        Vip = 4,
        Subscriber = 8,
        Founder = 16,
        Turbo = 32,
        Staff = 64,
        /// <summary>リードモデレーター。<see cref="Moderator"/> も立つ。</summary>
        LeadModerator = 128,
    }

    public sealed class BadgeRef
    {
        public string SetId { get; internal set; }
        public string Id { get; internal set; }
        /// <summary>サブスクの月数など。ない場合は null。</summary>
        public string Info { get; internal set; }
    }

    public sealed class ReplyInfo
    {
        public string ParentMessageId { get; internal set; }
        public string ParentText { get; internal set; }
        public UserRef ParentUser { get; internal set; }
        public string ThreadMessageId { get; internal set; }
        public UserRef ThreadUser { get; internal set; }
    }

    public enum FragmentKind
    {
        Unknown = 0,
        Text,
        Emote,
        ThirdPartyEmote,
        Emoji,
        Cheermote,
        Mention,
        Gif,
    }

    /// <summary>
    /// メッセージの部品。知らない <see cref="Kind"/>（<see cref="FragmentKind.Unknown"/> を含む）なら
    /// <see cref="Text"/> を表示すること。種類は V1 の中で追加することがある。
    /// </summary>
    public abstract class ChatFragment
    {
        public FragmentKind Kind { get; }
        /// <summary>画像にできない場合に表示する文字列。</summary>
        public string Text { get; }

        private protected ChatFragment(FragmentKind kind, string text)
        {
            this.Kind = kind;
            this.Text = text ?? "";
        }
    }

    public sealed class TextFragment : ChatFragment
    {
        internal TextFragment(string text) : base(FragmentKind.Text, text)
        {
        }
    }

    /// <summary>Twitch のエモート。</summary>
    public sealed class EmoteFragment : ChatFragment
    {
        public string EmoteId { get; }
        public string EmoteSetId { get; }
        /// <summary>エモートの所有者（チャンネル）の ID。グローバルなどでは null。</summary>
        public string OwnerId { get; }
        public bool HasAnimated { get; }
        public ImageKey Image { get; }

        internal EmoteFragment(string text, string emoteId, string emoteSetId, string ownerId, bool hasAnimated, ImageKey image)
            : base(FragmentKind.Emote, text)
        {
            this.EmoteId = emoteId;
            this.EmoteSetId = emoteSetId;
            this.OwnerId = ownerId;
            this.HasAnimated = hasAnimated;
            this.Image = image;
        }
    }

    public enum EmoteProvider
    {
        Unknown = 0,
        Twitch,
        Bttv,
        Ffz,
    }

    /// <summary>BTTV / FFZ のエモート。Twitch から届く文字の部品を TTVCore が分割して作る。</summary>
    public sealed class ThirdPartyEmoteFragment : ChatFragment
    {
        public EmoteProvider Provider { get; }
        public string EmoteId { get; }
        public bool IsAnimated { get; }
        public ImageKey Image { get; }

        internal ThirdPartyEmoteFragment(string text, EmoteProvider provider, string emoteId, bool isAnimated, ImageKey image)
            : base(FragmentKind.ThirdPartyEmote, text)
        {
            this.Provider = provider;
            this.EmoteId = emoteId;
            this.IsAnimated = isAnimated;
            this.Image = image;
        }
    }

    /// <summary>絵文字。<see cref="ChatFragment.Text"/> は絵文字の並びそのもの。</summary>
    public sealed class EmojiFragment : ChatFragment
    {
        public ImageKey Image { get; }

        internal EmojiFragment(string text, ImageKey image) : base(FragmentKind.Emoji, text)
        {
            this.Image = image;
        }
    }

    public sealed class CheermoteFragment : ChatFragment
    {
        /// <summary>小文字。</summary>
        public string Prefix { get; }
        public int Bits { get; }
        public int Tier { get; }
        public ImageKey Image { get; }

        internal CheermoteFragment(string text, string prefix, int bits, int tier, ImageKey image)
            : base(FragmentKind.Cheermote, text)
        {
            this.Prefix = prefix;
            this.Bits = bits;
            this.Tier = tier;
            this.Image = image;
        }
    }

    public sealed class MentionFragment : ChatFragment
    {
        public UserRef User { get; }

        internal MentionFragment(string text, UserRef user) : base(FragmentKind.Mention, text)
        {
            this.User = user;
        }
    }

    /// <summary>チャットの GIF。TTVCore は画像を扱わないため、<see cref="ChatFragment.Text"/> を表示すること。</summary>
    public sealed class GifFragment : ChatFragment
    {
        public string GifId { get; }
        public string Url { get; }

        internal GifFragment(string text, string gifId, string url) : base(FragmentKind.Gif, text)
        {
            this.GifId = gifId;
            this.Url = url;
        }
    }

    /// <summary>チャット上の通知（channel.chat.notification）。</summary>
    public sealed class ChatNotice
    {
        public string MessageId { get; internal set; }
        public DateTimeOffset Timestamp { get; internal set; }
        public string BroadcasterId { get; internal set; }
        public SourceChannel Source { get; internal set; }
        /// <summary>匿名なら null。</summary>
        public ChatUser Chatter { get; internal set; }
        public bool IsAnonymous { get; internal set; }
        public ChatNoticeType NoticeType { get; internal set; }
        /// <summary>shared_chat_* として届いたか。</summary>
        public bool IsSharedChat { get; internal set; }
        /// <summary>Twitch がチャット欄に出す文言。</summary>
        public string SystemMessage { get; internal set; }
        /// <summary>利用者が添えたメッセージ。なければ空。</summary>
        public IReadOnlyList<ChatFragment> Fragments { get; internal set; } = Array.Empty<ChatFragment>();
        /// <summary>種類ごとの詳細。対応する詳細がない種類では null。</summary>
        public ChatNoticeDetail Detail { get; internal set; }
    }

    public enum ChatNoticeType
    {
        Unknown = 0,
        Sub,
        Resub,
        SubGift,
        CommunitySubGift,
        GiftPaidUpgrade,
        PrimePaidUpgrade,
        Raid,
        Unraid,
        PayItForward,
        Announcement,
        BitsBadgeTier,
        CharityDonation,
        WatchStreak,
        Modiversary,
        GiftedDropsSummary,
    }

    public abstract class ChatNoticeDetail
    {
        private protected ChatNoticeDetail()
        {
        }
    }

    public sealed class SubNoticeDetail : ChatNoticeDetail
    {
        public SubTier Tier { get; internal set; }
        public bool IsPrime { get; internal set; }
        public int DurationMonths { get; internal set; }
    }

    public sealed class ResubNoticeDetail : ChatNoticeDetail
    {
        public SubTier Tier { get; internal set; }
        public bool IsPrime { get; internal set; }
        public int CumulativeMonths { get; internal set; }
        public int? StreakMonths { get; internal set; }
        public int DurationMonths { get; internal set; }
        public bool IsGift { get; internal set; }
        public bool GifterIsAnonymous { get; internal set; }
        /// <summary>匿名なら null。</summary>
        public UserRef Gifter { get; internal set; }
    }

    public sealed class SubGiftNoticeDetail : ChatNoticeDetail
    {
        public SubTier Tier { get; internal set; }
        public int DurationMonths { get; internal set; }
        /// <summary>匿名なら null。</summary>
        public int? CumulativeTotal { get; internal set; }
        public UserRef Recipient { get; internal set; }
        /// <summary>まとめてギフトの一部なら、その ID。それ以外は null。</summary>
        public string CommunityGiftId { get; internal set; }
    }

    public sealed class CommunitySubGiftNoticeDetail : ChatNoticeDetail
    {
        public string CommunityGiftId { get; internal set; }
        public int Total { get; internal set; }
        public SubTier Tier { get; internal set; }
        /// <summary>匿名なら null。</summary>
        public int? CumulativeTotal { get; internal set; }
    }

    /// <summary><see cref="ChatNoticeType.GiftPaidUpgrade"/> と <see cref="ChatNoticeType.PayItForward"/> の詳細。</summary>
    public sealed class GiftPaidUpgradeNoticeDetail : ChatNoticeDetail
    {
        public bool GifterIsAnonymous { get; internal set; }
        /// <summary>匿名なら null。</summary>
        public UserRef Gifter { get; internal set; }
    }

    public sealed class PrimePaidUpgradeNoticeDetail : ChatNoticeDetail
    {
        public SubTier Tier { get; internal set; }
    }

    public sealed class RaidNoticeDetail : ChatNoticeDetail
    {
        public UserRef Raider { get; internal set; }
        public int ViewerCount { get; internal set; }
        public string ProfileImageUrl { get; internal set; }
    }

    public sealed class AnnouncementNoticeDetail : ChatNoticeDetail
    {
        /// <summary>Twitch の色名。値の一覧は公式に記載がない。</summary>
        public string Color { get; internal set; }
    }

    public sealed class BitsBadgeTierNoticeDetail : ChatNoticeDetail
    {
        /// <summary>100、1000 など。</summary>
        public int Tier { get; internal set; }
    }

    public sealed class CharityDonationNoticeDetail : ChatNoticeDetail
    {
        public string CharityName { get; internal set; }
        /// <summary>通貨の最小単位での金額（USD ならセント）。</summary>
        public long AmountValue { get; internal set; }
        public int AmountDecimalPlaces { get; internal set; }
        /// <summary>ISO 4217。</summary>
        public string AmountCurrency { get; internal set; }
    }

    public sealed class MessageDeletedEvent
    {
        public string BroadcasterId { get; internal set; }
        public string MessageId { get; internal set; }
        public UserRef TargetUser { get; internal set; }
    }

    /// <summary>利用者単位の削除（BAN・タイムアウト）。</summary>
    public sealed class UserMessagesClearedEvent
    {
        public string BroadcasterId { get; internal set; }
        public UserRef TargetUser { get; internal set; }
    }

    public sealed class ChatClearedEvent
    {
        public string BroadcasterId { get; internal set; }
    }

    /// <summary>共有チャットのセッション。</summary>
    public sealed class SharedChatSession
    {
        public string SessionId { get; internal set; }
        public string HostBroadcasterId { get; internal set; }
        /// <summary>参加しているチャンネル。自分を含む。</summary>
        public IReadOnlyList<UserRef> Participants { get; internal set; } = Array.Empty<UserRef>();
    }

    public sealed class SendResult
    {
        public SendStatus Status { get; internal set; }
        /// <summary>送れた場合のメッセージ ID。</summary>
        public string MessageId { get; internal set; }
        /// <summary>Twitch に破棄された場合の理由。</summary>
        public string DropReasonCode { get; internal set; }
        public string DropReasonMessage { get; internal set; }
    }

    public enum SendStatus
    {
        Unknown = 0,
        Sent,
        /// <summary>Twitch が受け付けたが表示しなかった（AutoMod など）。</summary>
        Dropped,
        /// <summary>サードパーティの MOD からの送信を設定で止めている。</summary>
        Disabled,
        NotAuthenticated,
        /// <summary>送信の権限（user:write:chat）がない、または Twitch に拒否された。</summary>
        NotPermitted,
        /// <summary>500 文字を超えている。</summary>
        TooLong,
        /// <summary>送信待ちが上限を超えた。</summary>
        RateLimited,
        Failed,
    }
}
