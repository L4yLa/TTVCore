# TTVCore Developer Guide

[日本語](developer-guide.ja.md)

This guide explains how to build mods that handle Twitch chat and notifications using the TTVCore public API (`TTVCore.Api.V1`).

## Contents

1. [Setup](#1-setup)
2. [Basic rules](#2-basic-rules)
3. [Connection state](#3-connection-state)
4. [Receiving chat](#4-receiving-chat)
5. [Display stream (timeline)](#5-display-stream-timeline)
6. [Notifications](#6-notifications)
7. [Sending chat](#7-sending-chat)
8. [Images](#8-images)
9. [Displaying in TextMeshPro](#9-displaying-in-textmeshpro)
10. [Emote and badge lists](#10-emote-and-badge-lists)
11. [Testing](#11-testing)

## 1. Setup

### Adding the reference

Add TTVCore.dll as a reference. The copy in the game's `Plugins` folder is used at runtime, so do not copy it to your mod's output (set `Private` to `False`).

```xml
<Reference Include="TTVCore">
  <HintPath>$(BeatSaberDir)\Plugins\TTVCore.dll</HintPath>
  <Private>False</Private>
</Reference>
```

### Dependency in manifest.json

Add TTVCore to `dependsOn`.

```json
"dependsOn": {
  "TTVCore": ">=0.1.0"
}
```

`>=0.1.0` means 0.1.0 or later. Within V1, members are only added, so the existing members keep working when TTVCore is updated.

- `^0.1.0` means 0.1.0 or later and earlier than 0.2.0, so your mod would not load with TTVCore 0.2.0 or later.
- To use a member added later, raise the lower bound to the version that added it, or check `TTVCoreApi.MinorVersion` (section 2).

## 2. Basic rules

- **The entry point is `TTVCoreApi`.** It is divided into six parts: `Connection`, `Channel`, `Chat`, `Notifications`, `Images` and `Catalog`.
- **All events are raised on the Unity main thread.** Handlers can call Unity APIs directly.
- **Exceptions in subscribers do not affect others.** If a handler throws, TTVCore only logs it, and other subscribers still receive the event.
- **Unsubscribe yourself.** Remove handlers with `-=` when your mod shuts down or when you destroy your display.
- **Within V1, members are only added.** Before using a member added later, check `TTVCoreApi.MinorVersion`.

```csharp
using TTVCore.Api.V1;

if (TTVCoreApi.MinorVersion >= 1) {
    // Use members added in MinorVersion 1
}
```

- **Be prepared for unknown values.** `Unknown` enum values and unfamiliar kinds may arrive. Ignore them or show their text.

## 3. Connection state

`TTVCoreApi.Connection` reports the authentication and EventSub state.

| Member | Description |
|---|---|
| `AuthState` | `Checking` (checking the saved token), `Unauthenticated`, `WaitingForUser` (waiting for approval on the authorization page), `Authenticated` |
| `EventSubState` | `Disconnected`, `Connecting`, `Connected` (subscriptions completed) |
| `LoggedInUser` | The authenticated account, or null if not authenticated |
| `CanReadChat` / `CanSendChat` | Whether permission to read or send chat has been granted |
| `AuthStateChanged` / `EventSubStateChanged` | Raised when the state changes |
| `StartAuthentication()` | Opens the authorization page |

`TTVCoreApi.Channel.Broadcaster` is the broadcaster (the authenticated account). During a shared chat, `SharedChat` holds the participating channels, and `SharedChatChanged` is raised when they change (with null when the session ends).

## 4. Receiving chat

Events on `TTVCoreApi.Chat` are raised as soon as each item is received.

| Event | Description |
|---|---|
| `MessageReceived` | A chat message (`ChatMessage`) |
| `NoticeReceived` | A chat notice such as a sub or raid (`ChatNotice`) |
| `MessageDeleted` | A single message was deleted |
| `UserMessagesCleared` | A user's messages were cleared (ban or timeout) |
| `ChatCleared` | The whole chat was cleared |

### Message fragments

`ChatMessage.Text` is the plain text of the message. To display emotes and similar content, process `Fragments` in order.

| Type | Description |
|---|---|
| `TextFragment` | Text |
| `EmoteFragment` | A Twitch emote, including emotes from other channels |
| `ThirdPartyEmoteFragment` | A BTTV / FFZ emote |
| `EmojiFragment` | An emoji |
| `CheermoteFragment` | A Cheer emote |
| `MentionFragment` | A mention |
| `GifFragment` | A GIF. TTVCore does not handle its image, so show `Text` |

If an unfamiliar fragment type arrives, show its `Text`. Fragment types may be added within V1.

### Sender

`ChatMessage.Sender` (`ChatUser`) holds the name, color (`"#RRGGBB"` or null), badges and roles (`Roles`). `Roles` is a flags enum combining values such as `Broadcaster`, `Moderator`, `Vip` and `Subscriber`.

```csharp
if ((message.Sender.Roles & (ChatRoles.Broadcaster | ChatRoles.Moderator)) != 0) {
    // Broadcaster or moderator
}
```

### Shared chat

For messages that arrive from another channel during a shared chat, `Source` holds the originating channel. It is null for messages from your own channel.

## 5. Display stream (timeline)

To display items like a chat window, use `Chat.TimelineItemReceived`.

For subs, raids and similar events, Twitch sends a chat notice (`ChatNotice`) and an EventSub notification (`NotificationBase`) separately. The timeline combines them into one item, so the same event is not shown twice.

| Type | Description |
|---|---|
| `TimelineMessage` | A normal message. `Message` holds the message |
| `TimelineNotification` | A notification. `Kind` is its kind |

Main members of `TimelineNotification`:

| Member | Description |
|---|---|
| `Kind` | `Sub`, `Resub`, `SubGift`, `CommunitySubGift`, `Raid`, `Cheer`, `PowerUp`, `ChannelPointsRedemption`, `HypeTrain`, `AdBreak`, `ChatNoticeOnly` |
| `Notification` | The EventSub notification, or null |
| `Notice` | The chat notice, or null |
| `Message` | The attached chat message, or null |
| `GiftNotices` | Per-recipient notices for a community gift |
| `IsComplete` | Whether both sides arrived. False if one side did not arrive |

Notes:

- While TTVCore waits for the other side (up to a few seconds), the item is delayed. As a result, the order in which items are raised may differ from the order of `Timestamp`.
- `Notification` or `Notice`, or both sides of a pair, may be missing. Make sure the item can be displayed when either one is null.
- An ad break arrives twice: at the start and at the estimated end. The estimated-end item has `AdBreakNotification.IsEstimatedEnd` set to true.
- Chat notices of type `ChatNoticeType.Unknown` do not appear in the timeline (they still arrive through `Chat.NoticeReceived`).

```csharp
private void OnTimelineItem(TimelineItem item)
{
    switch (item) {
        case TimelineMessage m:
            Show($"{m.Message.Sender.DisplayName}: {m.Message.Text}");
            break;
        case TimelineNotification n when n.Kind == TimelineNotificationKind.Raid:
            var raid = n.Notification as RaidNotification;
            Show(raid != null ? $"{raid.FromBroadcaster.DisplayName} is raiding with {raid.Viewers} viewers" : n.Notice?.SystemMessage);
            break;
    }
}
```

## 6. Notifications

`TTVCoreApi.Notifications` delivers EventSub notifications as separate events by type. They are raised as soon as each notification is received and are not paired.

| Event | Type |
|---|---|
| `ChannelPointsRedeemed` | `ChannelPointsRedemptionNotification` |
| `Raided` | `RaidNotification` |
| `BitsUsed` | `BitsUseNotification` (Cheers and Power-ups) |
| `Subscribed` | `SubscribeNotification` (new subs only) |
| `Resubscribed` | `ResubscribeNotification` |
| `SubscriptionGifted` | `SubscriptionGiftNotification` |
| `HypeTrainBegan` / `HypeTrainProgressed` | `HypeTrainNotification` |
| `HypeTrainEnded` | `HypeTrainEndNotification` |
| `AdBreakBegan` / `AdBreakEnded` | `AdBreakNotification` |
| `NotificationReceived` | `RawNotification` (all notifications except chat, raised before the typed events) |

- Twitch does not send a notification when an ad break ends. `AdBreakEnded` is raised at a time calculated from the start time and duration. It may differ from the ads that viewers actually see.
- Notifications that TTVCore does not convert to a type are still available through `RawJson` in `NotificationReceived`. Chat (`channel.chat.*`) and shared chat (`channel.shared_chat.*`) notifications are not included. They arrive through the `Chat` and `Channel` events.
- If a notification cannot be read, TTVCore skips the typed event without throwing (`NotificationReceived` still receives it).

## 7. Sending chat

Create a `ChatSender` to send messages. The name you pass is used in the log.

```csharp
private readonly ChatSender _sender = TTVCoreApi.Chat.CreateSender("MyMod");

private async void Reply(string text, string parentMessageId)
{
    var result = await _sender.SendAsync(text, parentMessageId);
    if (result.Status != SendStatus.Sent) {
        Plugin.Log.Warn($"Send failed: {result.Status} {result.DropReasonMessage}");
    }
}
```

- Messages are sent from the broadcaster's account. During a shared chat, they appear in all participating channels.
- **Users can block sending from other mods in the TTVCore settings.** When blocked, the result is `Disabled`. You can check this beforehand with `Chat.ThirdPartySendEnabled`.
- TTVCore puts all sends in one queue and sends them in order, at most 20 messages per 30 seconds. When more than 20 messages are waiting, the result is `RateLimited`.
- Text longer than 500 characters is not sent, and the result is `TooLong`. Empty text results in `Failed`.
- `Dropped` means Twitch accepted the message but did not display it (for example, because of AutoMod). The reason is in `DropReasonCode` / `DropReasonMessage`.

## 8. Images

Images of emotes, badges, emoji and Cheers are identified by an `ImageKey`, which you receive from message fragments or from `Catalog`. **Its format is not public, so do not save it or build one yourself.**

To use an image, get a reference (`ImageHandle`) with `Images.Acquire`.

```csharp
var handle = TTVCoreApi.Images.Acquire(emote.Image);
if (handle.State == ImageState.Loading) {
    handle.StateChanged += OnImageReady;
}
else {
    OnImageReady(handle);
}

// When you are done
handle.Dispose();
```

- Call the methods from the main thread.
- `StateChanged` is raised once, when an image that was loading finishes. It is not raised if the image is already `Ready` or `Failed`, so check `State` first.
- TTVCore loads, caches and releases images. Call `Dispose` when you are done. The actual cleanup happens all at once when the scene changes.
- `Sheet` is a texture containing all frames and may be shared with other images. Use only the area given by `FrameUVs` or `CurrentUV`.
- `SharedMaterial` is shared by all references. Do not modify it.
- Animations advance on a common TTVCore clock. With `CurrentFrame` / `CurrentUV`, every instance of the same image stays in sync.

## 9. Displaying in TextMeshPro

To show text mixed with emotes in a `TextMeshProUGUI`, let TTVCore handle it. It takes care of loading images, replacing placeholders and animating.

```csharp
var content = TTVCoreApi.Images.CreateTmpContent()
    .AppendRichText($"<color={color}><b>")
    .AppendText(message.Sender.DisplayName)
    .AppendRichText("</b></color>: ")
    .AppendFragments(message.Fragments)
    .Build();

var binding = TTVCoreApi.Images.Bind(textMeshPro, content);
```

- Use `AppendText` for strings such as viewer text. TTVCore escapes it so that it is not interpreted as TMP tags.
- Use `AppendRichText` for your own formatting tags. It is not escaped. **Never pass viewer text to it.**
- The `fallbackText` of `AppendImage` is shown while loading and on failure. `scale` is relative to the font size.
- While bound, do not change `text.text` directly. To change the content, `Dispose` the `TmpBinding`, then call `Bind` again with the new content.
- `TmpBinding.Updated` is raised when images finish loading and the text is rebuilt. The number of lines may change, so adjust your layout in this event.
- If the text object is destroyed, the binding is released automatically.

## 10. Emote and badge lists

`TTVCoreApi.Catalog` lets you look up the emotes and badges available in a channel.

| Member | Description |
|---|---|
| `GetEmotes(broadcasterId)` | Channel and global emotes (Twitch / BTTV / FFZ) |
| `TryFindEmote(broadcasterId, name, out emote)` | Finds an emote by name. Twitch emotes are checked first (channel, then global), followed by BTTV / FFZ (channel, then global) |
| `GetBadge(broadcasterId, badge)` | The badge's title and image, or null if not found |
| `EmotesUpdated` | Raised when a list is updated. The argument is the channel ID (null for the global list) |

The lists are loaded after EventSub connects. During a shared chat, the lists of participating channels are loaded too. They are empty until loaded, so use them after receiving `EmotesUpdated`. When a viewer uses an emote from another channel, it is not in these lists and appears only as a message fragment (`EmoteFragment`).

## 11. Testing

A Debug build of TTVCore processes `*.json` files placed in `UserData\TTVCore\inject\` as received EventSub notifications (including chat messages). You can check your display without triggering real events on a stream.

- The JSON is either an EventSub WebSocket message (`{"metadata": ..., "payload": ...}`) or `{"subscription": ..., "event": ...}`, the same shape as the examples in the official Twitch reference. Examples are in [test-payloads/](../test-payloads/).
- Processed files are moved to `inject\done\`.

To build in the Debug configuration, run the following in TTVCore's `src\TTVCore`:

```bat
build.bat -Debug
```
