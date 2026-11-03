# TTVCore

Beat Saber 向けの、Twitch と接続するための MOD です。
Twitch の認証、EventSub によるチャットと通知の受信、チャットの送信、エモート・バッジ・絵文字の画像の取得を行います。

本 MOD で取得しているエモートなどについて、別の MOD から取得できる API を用意しています。

## 依存 MOD

- BSIPA
- BeatSaberMarkupLanguage

## 開発用のテストデータ

`test-payloads/` には、通知の受信を試すための JSON の例があります。
Debug 構成でビルドした TTVCore では、`UserData\TTVCore\inject\` に置いた JSON を受信した通知として処理します。

## クレジット

- 絵文字の画像: Twemoji © Twitter, Inc. and other contributors, CC-BY 4.0（[jdecked/twemoji](https://github.com/jdecked/twemoji)）
- 絵文字の並びの表: Unicode `emoji-test.txt`（Unicode License V3）

詳しくは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。

## ライセンス

MIT License（[LICENSE](LICENSE)）

同梱の第三者の素材: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
