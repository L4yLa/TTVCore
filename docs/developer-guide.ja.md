# TTVCore 開発者向けガイド

[English](developer-guide.en.md)

TTVCore の公開 API（`TTVCore.Api.V1`）を使って、Twitch のチャットや通知を扱う MOD を作るためのガイドです。

## 目次

1. [準備](#1-準備)
2. [基本の規則](#2-基本の規則)
3. [接続の状態](#3-接続の状態)
4. [チャットの受信](#4-チャットの受信)
5. [表示用の流れ（タイムライン）](#5-表示用の流れタイムライン)
6. [通知](#6-通知)
7. [チャットの送信](#7-チャットの送信)
8. [画像](#8-画像)
9. [TextMeshPro への表示](#9-textmeshpro-への表示)
10. [エモートとバッジの一覧](#10-エモートとバッジの一覧)
11. [動作の確認](#11-動作の確認)

## 1. 準備

### 参照の追加

TTVCore.dll を参照に追加します。TTVCore はゲームの `Plugins` フォルダに入っているものを使うため、自分の MOD の出力にはコピーしません（`Private` を `False` にする）。

```xml
<Reference Include="TTVCore">
  <HintPath>$(BeatSaberDir)\Plugins\TTVCore.dll</HintPath>
  <Private>False</Private>
</Reference>
```

### manifest.json の依存

`dependsOn` に TTVCore を加えます。

```json
"dependsOn": {
  "TTVCore": "^0.1.0"
}
```

`^0.1.0` は 0.1.0 以上 0.2.0 未満を表します。

## 2. 基本の規則

- **入口は `TTVCoreApi` です。** `Connection`、`Channel`、`Chat`、`Notifications`、`Images`、`Catalog` の6つに分かれています。
- **イベントはすべて Unity のメインスレッドで発生します。** ハンドラーから Unity の API をそのまま呼べます。
- **購読者の例外は他に影響しません。** ハンドラーが例外を出しても TTVCore がログに記録するだけで、他の購読者には通知が届きます。
- **購読は自分で解除します。** MOD の終了時や、表示を破棄するときに `-=` で解除してください。
- **V1 の中では、メンバーの追加だけを行います。** 後から追加されたメンバーを使う場合は、先に `TTVCoreApi.MinorVersion` を確認します。

```csharp
using TTVCore.Api.V1;

if (TTVCoreApi.MinorVersion >= 1) {
    // MinorVersion 1 で追加されたメンバーを使う
}
```

- **未知の値に備えます。** 列挙型の `Unknown` や、知らない種類の値が届くことがあります。その場合は無視するか、文字列を表示してください。

## 3. 接続の状態

`TTVCoreApi.Connection` で認証と EventSub の状態が分かります。

| メンバー | 内容 |
|---|---|
| `AuthState` | `Checking`（保存済みトークンの確認中）、`Unauthenticated`、`WaitingForUser`（認証ページで許可待ち）、`Authenticated` |
| `EventSubState` | `Disconnected`、`Connecting`、`Connected`（購読まで完了） |
| `LoggedInUser` | 認証したアカウント。未認証なら null |
| `CanReadChat` / `CanSendChat` | チャットの受信・送信の権限があるか |
| `AuthStateChanged` / `EventSubStateChanged` | 状態が変わったときのイベント |
| `StartAuthentication()` | 認証ページを開く |

`TTVCoreApi.Channel.Broadcaster` は配信者（認証したアカウント）です。共有チャット中は `SharedChat` に参加チャンネルが入り、変化すると `SharedChatChanged` が発生します（終わったときは null）。

## 4. チャットの受信

`TTVCoreApi.Chat` のイベントは、受信した時点で発生します。

| イベント | 内容 |
|---|---|
| `MessageReceived` | チャットのメッセージ（`ChatMessage`） |
| `NoticeReceived` | サブスクやレイドなど、チャット上の通知（`ChatNotice`） |
| `MessageDeleted` | 1件の削除 |
| `UserMessagesCleared` | 利用者単位の削除（BAN・タイムアウト） |
| `ChatCleared` | チャットの全消去 |

### メッセージの部品（Fragments）

`ChatMessage.Text` は本文の平文です。エモートなどを表示するには、`Fragments` を順に扱います。

| 型 | 内容 |
|---|---|
| `TextFragment` | 文字 |
| `EmoteFragment` | Twitch のエモート。他のチャンネルのエモートも含む |
| `ThirdPartyEmoteFragment` | BTTV / FFZ のエモート |
| `EmojiFragment` | 絵文字 |
| `CheermoteFragment` | Cheer のエモート |
| `MentionFragment` | メンション |
| `GifFragment` | GIF。TTVCore は画像を扱わないため `Text` を表示する |

知らない種類の部品が届いた場合は `Text` を表示してください。部品の種類は V1 の中で増えることがあります。

### 送信者

`ChatMessage.Sender`（`ChatUser`）には、名前、色（`"#RRGGBB"` または null）、バッジ、立場（`Roles`）が入ります。`Roles` はフラグで、`Broadcaster`、`Moderator`、`Vip`、`Subscriber` などを組み合わせて持ちます。

```csharp
if ((message.Sender.Roles & (ChatRoles.Broadcaster | ChatRoles.Moderator)) != 0) {
    // 配信者かモデレーター
}
```

### 共有チャット

共有チャットで他のチャンネルから届いたメッセージは、`Source` に発言元のチャンネルが入ります。自分のチャンネルのメッセージでは null です。

## 5. 表示用の流れ（タイムライン）

チャット欄のように表示する場合は、`Chat.TimelineItemReceived` を使います。

サブスクやレイドなどは、Twitch からチャット上の通知（`ChatNotice`）と EventSub の通知（`NotificationBase`）が別々に届きます。タイムラインでは TTVCore がこれらを1件にまとめるので、同じ出来事が2回表示されません。

| 型 | 内容 |
|---|---|
| `TimelineMessage` | 通常のメッセージ。`Message` にメッセージが入る |
| `TimelineNotification` | 通知。`Kind` が種類 |

`TimelineNotification` の主なメンバー:

| メンバー | 内容 |
|---|---|
| `Kind` | `Sub`、`Resub`、`SubGift`、`CommunitySubGift`、`Raid`、`Cheer`、`PowerUp`、`ChannelPointsRedemption`、`HypeTrain`、`AdBreak`、`ChatNoticeOnly` |
| `Notification` | EventSub の通知。ない場合は null |
| `Notice` | チャット上の通知。ない場合は null |
| `Message` | 添えられたチャットのメッセージ。ない場合は null |
| `GiftNotices` | まとめてギフトの、受け取った人ごとの通知 |
| `IsComplete` | 両方がそろったか。片方が来なかった場合は false |

注意点:

- 片方を待つ間（最大数秒）、その通知は遅れて発生します。そのため、発生順と `Timestamp` の順が一致しないことがあります。
- `Notification` と `Notice` のどちらか、または両方がそろわないことがあります。どちらが null でも表示できるようにしてください。
- 広告（`AdBreak`）は、開始時と終了の目安の時刻の2回届きます。終了の目安のものは `AdBreakNotification.IsEstimatedEnd` が true です。
- 種類が `ChatNoticeType.Unknown` のチャット上の通知は、タイムラインには届きません（`Chat.NoticeReceived` には届きます）。

```csharp
private void OnTimelineItem(TimelineItem item)
{
    switch (item) {
        case TimelineMessage m:
            Show($"{m.Message.Sender.DisplayName}: {m.Message.Text}");
            break;
        case TimelineNotification n when n.Kind == TimelineNotificationKind.Raid:
            var raid = n.Notification as RaidNotification;
            Show(raid != null ? $"{raid.FromBroadcaster.DisplayName} がレイド（{raid.Viewers} 人）" : n.Notice?.SystemMessage);
            break;
    }
}
```

## 6. 通知

`TTVCoreApi.Notifications` は、EventSub の通知を種類別のイベントで届けます。受信した時点で発生し、組にはしません。

| イベント | 型 |
|---|---|
| `ChannelPointsRedeemed` | `ChannelPointsRedemptionNotification` |
| `Raided` | `RaidNotification` |
| `BitsUsed` | `BitsUseNotification`（Cheer と Power-up） |
| `Subscribed` | `SubscribeNotification`（新規のみ） |
| `Resubscribed` | `ResubscribeNotification` |
| `SubscriptionGifted` | `SubscriptionGiftNotification` |
| `HypeTrainBegan` / `HypeTrainProgressed` | `HypeTrainNotification` |
| `HypeTrainEnded` | `HypeTrainEndNotification` |
| `AdBreakBegan` / `AdBreakEnded` | `AdBreakNotification` |
| `NotificationReceived` | `RawNotification`（チャット以外のすべての通知。種類別のイベントより先に発生） |

- Twitch に広告の終了の通知はありません。`AdBreakEnded` は、開始時刻と長さから計算した時刻に発生します。実際に視聴者に出る広告とはずれることがあります。
- TTVCore が型に変換しない種類の通知も、`NotificationReceived` の `RawJson` で受け取れます。チャット（`channel.chat.*`）と共有チャット（`channel.shared_chat.*`）の通知は含みません。これらは `Chat` と `Channel` のイベントで届きます。
- 通知の内容を読めなかった場合、TTVCore は例外を出さずに種類別のイベントを飛ばします（`NotificationReceived` には届きます）。

## 7. チャットの送信

送信には `ChatSender` を作ります。引数の名前はログに残すためのものです。

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

- 配信者のアカウントで送信します。共有チャット中は、参加しているすべてのチャンネルに表示されます。
- **利用者は、他の MOD からの送信を TTVCore の設定で止められます。** 止めている場合、結果は `Disabled` です。事前に `Chat.ThirdPartySendEnabled` で確認できます。
- 送信は TTVCore が1つの列に並べ、30秒あたり20件までに抑えて順に送ります。送信待ちが20件を超えると `RateLimited` になります。
- 500 文字を超える文は送らず、`TooLong` になります。空の文は `Failed` になります。
- `Dropped` は、Twitch が受け付けたものの表示しなかった場合です（AutoMod など）。理由は `DropReasonCode` / `DropReasonMessage` に入ります。

## 8. 画像

エモート、バッジ、絵文字、Cheer の画像は `ImageKey` で指します。`ImageKey` はメッセージの部品や `Catalog` から受け取ります。**中身の形式は公開していないため、保存したり自分で組み立てたりしないでください。**

画像を使うには、`Images.Acquire` で参照（`ImageHandle`）を取ります。

```csharp
var handle = TTVCoreApi.Images.Acquire(emote.Image);
if (handle.State == ImageState.Loading) {
    handle.StateChanged += OnImageReady;
}
else {
    OnImageReady(handle);
}

// 使い終わったら
handle.Dispose();
```

- メソッドはメインスレッドから呼んでください。
- `StateChanged` は、読み込み中だった画像の読み込みが終わったときに1回だけ発生します。すでに `Ready` / `Failed` の場合は発生しないため、先に `State` を確認してください。
- 画像の読み込み、キャッシュ、解放は TTVCore が行います。使い終わったら `Dispose` してください。実際の破棄はシーンの切り替え時にまとめて行います。
- `Sheet` は全フレームを並べたテクスチャで、他の画像と共有していることがあります。表示には `FrameUVs` または `CurrentUV` の範囲だけを使ってください。
- `SharedMaterial` はすべての参照で共有されるため、変更しないでください。
- アニメーションは TTVCore の共通の時計で進みます。`CurrentFrame` / `CurrentUV` を使うと、同じ画像はすべて同期して動きます。

## 9. TextMeshPro への表示

文字とエモートを混ぜて `TextMeshProUGUI` に表示する場合は、TTVCore に任せると画像の読み込み、差し替え、アニメーションまで処理されます。

```csharp
var content = TTVCoreApi.Images.CreateTmpContent()
    .AppendRichText($"<color={color}><b>")
    .AppendText(message.Sender.DisplayName)
    .AppendRichText("</b></color>: ")
    .AppendFragments(message.Fragments)
    .Build();

var binding = TTVCoreApi.Images.Bind(textMeshPro, content);
```

- `AppendText` は視聴者の文字列などに使います。TMP のタグとして解釈されないよう、TTVCore がエスケープします。
- `AppendRichText` は自分の装飾タグに使います。エスケープしません。**視聴者の文字列を渡さないでください。**
- `AppendImage` の `fallbackText` は、読み込み中と失敗時に表示されます。`scale` は文字の大きさに対する倍率です。
- 結び付けている間は、`text.text` を直接変えないでください。内容を変えるときは、`TmpBinding` を `Dispose` してから、新しい内容で `Bind` し直します。
- 画像の読み込みが終わってテキストが作り直されると、`TmpBinding.Updated` が発生します。行数が変わることがあるため、レイアウトの調整はこのイベントで行ってください。
- テキストのオブジェクトが破棄された場合、結び付けは自動で解除されます。

## 10. エモートとバッジの一覧

`TTVCoreApi.Catalog` で、チャンネルで使えるエモートとバッジを調べられます。

| メンバー | 内容 |
|---|---|
| `GetEmotes(broadcasterId)` | チャンネルとグローバルのエモート（Twitch / BTTV / FFZ） |
| `TryFindEmote(broadcasterId, name, out emote)` | 名前から探す。Twitch エモート（チャンネル、グローバルの順）を先に、次に BTTV / FFZ（チャンネル、グローバルの順）を見る |
| `GetBadge(broadcasterId, badge)` | バッジの名前と画像。見つからなければ null |
| `EmotesUpdated` | 一覧が更新された。引数はチャンネルの ID（グローバルなら null） |

一覧は EventSub の接続後に読み込まれます。共有チャット中は、参加チャンネルの一覧も読み込まれます。読み込み前は空のため、`EmotesUpdated` を受けてから使ってください。視聴者が他のチャンネルのエモートを使った場合、そのエモートは一覧にはなく、メッセージの部品（`EmoteFragment`）でだけ分かります。

## 11. 動作の確認

Debug 構成でビルドした TTVCore は、`UserData\TTVCore\inject\` に置いた `*.json` を、受信した EventSub の通知（チャットのメッセージを含む）として処理します。実際の配信で通知を起こさなくても、表示を確かめられます。

- JSON の形式は、EventSub の WebSocket のメッセージ（`{"metadata": ..., "payload": ...}`）か、Twitch の公式リファレンスの例と同じ `{"subscription": ..., "event": ...}` です。例は [test-payloads/](../test-payloads/) にあります。
- 処理したファイルは `inject\done\` に移されます。

Debug 構成でビルドするには、TTVCore の `src\TTVCore` で次を実行します。

```bat
build.bat -Debug
```
