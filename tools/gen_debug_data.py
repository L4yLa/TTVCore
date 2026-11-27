#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
デバッグ機能のテスト用データを作る。
  src/TTVCore/Resources/Debug/notifications.json  通知のテスト（ボタンごとに、届く EventSub の通知の並び）
  src/TTVCore/Resources/Debug/chat-messages.json  チャットのテスト用の 100 件

形は EventSub の event（公式リファレンスの項目名）。TTVCore が実行時に次の置き換えをする。
  {{broadcaster_id}} {{broadcaster_login}} {{broadcaster_name}}  ログイン中の配信者（未認証なら仮の値）
  {{message_id}}       event ごとに新しい ID
  {{id:名前}}          1 回のテストの中で同じ ID（まとめてギフトの ID など）
  {{now}} {{now+秒}}   現在時刻（UTC、RFC3339）
  {{last_message_id}} {{last_message_text}} {{last_chatter_id}} {{last_chatter_login}} {{last_chatter_name}}
                       直前に流したテストのメッセージ
利用者は架空のもの。リクエストのコマンド（! で始まる文）は入れない。
使い方: python3 tools/gen_debug_data.py
"""
import json
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "src", "TTVCore", "Resources", "Debug")

B_ID, B_LOGIN, B_NAME = "{{broadcaster_id}}", "{{broadcaster_login}}", "{{broadcaster_name}}"

# 架空の利用者（id, login, 表示名, 色, バッジ）
USERS = [
    ("900000001", "sakura_test", "さくら_test", "#FF69B4", [("subscriber", "12", "14")]),
    ("900000002", "blue_note_t", "BlueNote_T", "#1E90FF", []),
    ("900000003", "mod_kuma", "モデくま", "#2E8B57", [("moderator", "1", ""), ("subscriber", "24", "26")]),
    ("900000004", "vip_neko", "VIPねこ", "#DAA520", [("vip", "1", ""), ("subscriber", "3", "3")]),
    ("900000005", "pixel_rider", "PixelRider", None, [("premium", "1", "")]),
    ("900000006", "night_owl_t", "NightOwl_T", "#8A2BE2", [("subscriber", "0", "1"), ("bits", "1000", "")]),
    ("900000007", "hikari_t", "ひかり_t", "#FF4500", [("founder", "0", "")]),
    ("900000008", "saber_fan", "SaberFan", "#00CED1", [("turbo", "1", "")]),
    ("900000009", "taro_test", "たろう_test", "#9ACD32", []),
    ("900000010", "longname_viewer_test", "LongNameViewer_Test", "#C71585", [("subscriber", "6", "8"), ("bits", "100", "")]),
]
RAIDER = ("900000101", "raider_ch_t", "RaiderCh_T")
GIFTER = ("900000102", "gift_san_t", "ギフトさん_t")
RECIPIENTS = [("900000201", "lucky_one_t", "LuckyOne_T"), ("900000202", "lucky_two_t", "LuckyTwo_T"), ("900000203", "lucky_three_t", "LuckyThree_T")]

# Twitch のグローバルエモート（数値の ID は旧来のもので、CDN の v2 の URL で取得できる）
TWITCH_EMOTES = {
    "Kappa": "25", "Kreygasm": "41", "BibleThump": "86", "ResidentSleeper": "245", "4Head": "354",
    "LUL": "425618", "NotLikeThis": "58765", "DansGame": "33", "SMOrc": "52", "HeyGuys": "30259",
    "VoHiYo": "81274", "SeemsGood": "64138", "Jebaited": "114836", "CoolCat": "58127",
}


def badges(user):
    return [{"set_id": s, "id": i, "info": info} for (s, i, info) in user[4]]


def fragments(text):
    """空白で区切った語から fragments を作る。エモート、Cheer、@メンションを見分け、残りは文字にまとめる。"""
    out = []
    buf = ""
    for tok in re.split(r"(\s+)", text):
        frag = None
        if tok in TWITCH_EMOTES:
            frag = {"type": "emote", "text": tok, "cheermote": None, "mention": None,
                    "emote": {"id": TWITCH_EMOTES[tok], "emote_set_id": "0", "owner_id": "0", "format": ["static"]}}
        elif re.fullmatch(r"Cheer(\d+)", tok):
            bits = int(tok[5:])
            tier = 10000 if bits >= 10000 else 5000 if bits >= 5000 else 1000 if bits >= 1000 else 100 if bits >= 100 else 1
            frag = {"type": "cheermote", "text": tok, "emote": None, "mention": None,
                    "cheermote": {"prefix": "Cheer", "bits": bits, "tier": tier}}
        elif tok.startswith("@") and len(tok) > 1:
            login = tok[1:]
            u = next((x for x in USERS if x[1] == login), None)
            if login == "broadcaster":
                tok = "@" + B_NAME
                mention = {"user_id": B_ID, "user_login": B_LOGIN, "user_name": B_NAME}
            elif u:
                tok = "@" + u[2]
                mention = {"user_id": u[0], "user_login": u[1], "user_name": u[2]}
            else:
                mention = None
            if mention:
                frag = {"type": "mention", "text": tok, "emote": None, "cheermote": None, "mention": mention}
        if frag is None:
            buf += tok
            continue
        if buf:
            out.append({"type": "text", "text": buf, "emote": None, "cheermote": None, "mention": None})
            buf = ""
        out.append(frag)
    if buf:
        out.append({"type": "text", "text": buf, "emote": None, "cheermote": None, "mention": None})
    return out


def chat_event(user, text, message_type="text", bits=None, reply=None, reward=None, action=False):
    frags = fragments(text)
    full = "".join(f["text"] for f in frags)
    return {
        "broadcaster_user_id": B_ID, "broadcaster_user_login": B_LOGIN, "broadcaster_user_name": B_NAME,
        "source_broadcaster_user_id": None, "source_broadcaster_user_login": None, "source_broadcaster_user_name": None,
        "chatter_user_id": user[0], "chatter_user_login": user[1], "chatter_user_name": user[2],
        "message_id": "{{message_id}}",
        "message": {"text": "\u0001ACTION " + full + "\u0001" if action else full, "fragments": frags},
        "message_type": message_type,
        "badges": badges(user) if len(user) > 4 else [],
        "cheer": {"bits": bits} if bits else None,
        "color": user[3] if len(user) > 3 and user[3] else "",
        "reply": reply,
        "channel_points_custom_reward_id": reward,
        "source_message_id": None, "source_badges": None,
    }


def notice_event(user, notice_type, detail, system_message, text="", anonymous=False):
    ev = {
        "broadcaster_user_id": B_ID, "broadcaster_user_login": B_LOGIN, "broadcaster_user_name": B_NAME,
        "source_broadcaster_user_id": None, "source_broadcaster_user_login": None, "source_broadcaster_user_name": None,
        "chatter_user_id": None if anonymous else user[0],
        "chatter_user_login": None if anonymous else user[1],
        "chatter_user_name": None if anonymous else user[2],
        "chatter_is_anonymous": anonymous,
        "color": "" if anonymous or len(user) < 4 else (user[3] or ""),
        "badges": [] if anonymous or len(user) < 5 else badges(user),
        "system_message": system_message,
        "message_id": "{{message_id}}",
        "message": {"text": text, "fragments": fragments(text) if text else []},
        "notice_type": notice_type,
        "source_message_id": None, "source_badges": None,
    }
    ev[notice_type] = detail
    return ev


def user_fields(prefix, u):
    return {prefix + "_id": u[0], prefix + "_login": u[1], prefix + "_name": u[2]}


def bcast(prefix="broadcaster_user"):
    return {prefix + "_id": B_ID, prefix + "_login": B_LOGIN, prefix + "_name": B_NAME}


def step(type_, event, delay=0, version="1"):
    return {"type": type_, "version": version, "delay": delay, "event": event}


# ================= 通知のテスト =================

def notifications():
    s = USERS[0]
    r = USERS[1]
    tests = []

    def add(id_, en, ja, steps):
        tests.append({"id": id_, "en": en, "ja": ja, "steps": steps})

    add("sub", "Sub", "サブスク", [
        step("channel.subscribe", {**user_fields("user", r), **bcast(), "tier": "1000", "is_gift": False}),
        step("channel.chat.notification", notice_event(r, "sub", {"sub_tier": "1000", "is_prime": False, "duration_months": 1},
                                                       f"{r[2]} subscribed at Tier 1."), 300),
    ])
    add("sub_prime", "Sub (Prime)", "サブスク (Prime)", [
        step("channel.subscribe", {**user_fields("user", USERS[4]), **bcast(), "tier": "1000", "is_gift": False}),
        step("channel.chat.notification", notice_event(USERS[4], "sub", {"sub_tier": "1000", "is_prime": True, "duration_months": 1},
                                                       f"{USERS[4][2]} subscribed with Prime."), 300),
    ])
    resub_text = "いつも楽しく見てます！ これからも応援してます SeemsGood"
    add("resub", "Resub", "継続サブスク", [
        step("channel.chat.notification", notice_event(s, "resub", {
            "cumulative_months": 14, "duration_months": 1, "streak_months": 6, "sub_tier": "2000", "is_prime": False,
            "is_gift": False, "gifter_is_anonymous": None, "gifter_user_id": None, "gifter_user_login": None, "gifter_user_name": None,
        }, f"{s[2]} subscribed at Tier 2. They've subscribed for 14 months, currently on a 6 month streak!", resub_text)),
        step("channel.subscription.message", {**user_fields("user", s), **bcast(), "tier": "2000",
                                              "message": {"text": resub_text, "emotes": [{"begin": 30, "end": 38, "id": "64138"}]},
                                              "cumulative_months": 14, "streak_months": 6, "duration_months": 1}, 300),
    ])
    rcp = RECIPIENTS[0]
    add("sub_gift", "Gift sub (1)", "ギフト (1件)", [
        step("channel.chat.notification", notice_event(GIFTER, "sub_gift", {
            "duration_months": 1, "cumulative_total": 5, **user_fields("recipient_user", rcp), "sub_tier": "1000", "community_gift_id": None,
        }, f"{GIFTER[2]} gifted a Tier 1 sub to {rcp[2]}! They have given 5 Gift Subs in the channel!")),
        step("channel.subscribe", {**user_fields("user", rcp), **bcast(), "tier": "1000", "is_gift": True}, 300),
    ])
    community = [step("channel.subscription.gift", {**user_fields("user", GIFTER), **bcast(), "total": 3, "tier": "1000",
                                                     "cumulative_total": 8, "is_anonymous": False})]
    community.append(step("channel.chat.notification", notice_event(GIFTER, "community_sub_gift", {
        "id": "{{id:community}}", "total": 3, "sub_tier": "1000", "cumulative_total": 8,
    }, f"{GIFTER[2]} is gifting 3 Tier 1 Subs to the community! They've gifted a total of 8 in the channel!"), 200))
    for rc in RECIPIENTS:
        community.append(step("channel.chat.notification", notice_event(GIFTER, "sub_gift", {
            "duration_months": 1, "cumulative_total": None, **user_fields("recipient_user", rc), "sub_tier": "1000",
            "community_gift_id": "{{id:community}}",
        }, f"{GIFTER[2]} gifted a Tier 1 sub to {rc[2]}!"), 100))
        community.append(step("channel.subscribe", {**user_fields("user", rc), **bcast(), "tier": "1000", "is_gift": True}, 50))
    add("community_sub_gift", "Community gift (3)", "まとめてギフト (3件)", community)
    add("community_sub_gift_anonymous", "Anonymous gift (3)", "匿名ギフト (3件)", [
        step("channel.subscription.gift", {"user_id": None, "user_login": None, "user_name": None, **bcast(), "total": 3, "tier": "1000",
                                           "cumulative_total": None, "is_anonymous": True}),
        step("channel.chat.notification", notice_event(GIFTER, "community_sub_gift", {
            "id": "{{id:anon}}", "total": 3, "sub_tier": "1000", "cumulative_total": None,
        }, "An anonymous user is gifting 3 Tier 1 Subs to the community!", anonymous=True), 200),
    ] + sum([[step("channel.chat.notification", notice_event(GIFTER, "sub_gift", {
        "duration_months": 1, "cumulative_total": None, **user_fields("recipient_user", rc), "sub_tier": "1000",
        "community_gift_id": "{{id:anon}}"}, f"An anonymous user gifted a Tier 1 sub to {rc[2]}!", anonymous=True), 100),
        step("channel.subscribe", {**user_fields("user", rc), **bcast(), "tier": "1000", "is_gift": True}, 50)] for rc in RECIPIENTS], []))
    add("raid", "Raid", "レイド", [
        step("channel.raid", {**user_fields("from_broadcaster_user", RAIDER), **bcast("to_broadcaster_user"), "viewers": 42}),
        step("channel.chat.notification", notice_event(RAIDER, "raid", {
            **user_fields("user", RAIDER), "viewer_count": 42, "profile_image_url": None,
        }, f"42 raiders from {RAIDER[2]} have joined!"), 300),
    ])
    cheer_user = USERS[5]
    add("cheer", "Cheer", "Cheer", [
        step("channel.bits.use", {**bcast(), **user_fields("user", cheer_user), "bits": 100, "type": "cheer",
                                  "message": {"text": "Cheer100 ナイスプレイ！", "fragments": []}, "power_up": None, "custom_power_up": None}),
        step("channel.chat.message", chat_event(cheer_user, "Cheer100 ナイスプレイ！", bits=100), 200),
    ])
    add("power_up_effect", "Power-up: effect", "Power-up: エフェクト", [
        step("channel.bits.use", {**bcast(), **user_fields("user", USERS[7]), "bits": 30, "type": "power_up",
                                  "message": {"text": "すごい！", "fragments": []},
                                  "power_up": {"type": "message_effect", "emote": None, "message_effect_id": "cosmic-abyss"}, "custom_power_up": None}),
        step("channel.chat.message", chat_event(USERS[7], "すごい！ エフェクト付きで応援！", message_type="power_ups_message_effect"), 200),
    ])
    add("power_up_gigantify", "Power-up: gigantify", "Power-up: 巨大化", [
        step("channel.bits.use", {**bcast(), **user_fields("user", USERS[8]), "bits": 50, "type": "power_up",
                                  "message": {"text": "Kappa", "fragments": []},
                                  "power_up": {"type": "gigantify_an_emote", "emote": {"id": "25", "name": "Kappa"}, "message_effect_id": None},
                                  "custom_power_up": None}),
        step("channel.chat.message", chat_event(USERS[8], "でかい Kappa", message_type="power_ups_gigantified_emote"), 200),
    ])
    add("power_up_celebration", "Power-up: celebration", "Power-up: セレブレーション", [
        step("channel.bits.use", {**bcast(), **user_fields("user", USERS[9]), "bits": 100, "type": "power_up", "message": None,
                                  "power_up": {"type": "celebration", "emote": {"id": "41", "name": "Kreygasm"}, "message_effect_id": None},
                                  "custom_power_up": None}),
    ])
    add("redemption", "Points (with input)", "ポイント (入力あり)", [
        step("channel.channel_points_custom_reward_redemption.add", {
            "id": "{{id:redemption}}", **bcast(), **user_fields("user", USERS[2]), "user_input": "この曲をもう一回！",
            "status": "unfulfilled", "reward": {"id": "{{id:reward}}", "title": "リクエスト", "cost": 500, "prompt": "メッセージを書いてください"},
            "redeemed_at": "{{now}}"}),
        step("channel.chat.message", chat_event(USERS[2], "この曲をもう一回！", reward="{{id:reward}}"), 200),
    ])
    add("redemption_simple", "Points (no input)", "ポイント (入力なし)", [
        step("channel.channel_points_custom_reward_redemption.add", {
            "id": "{{id:redemption}}", **bcast(), **user_fields("user", USERS[3]), "user_input": "",
            "status": "fulfilled", "reward": {"id": "{{id:reward}}", "title": "水分補給", "cost": 100, "prompt": ""},
            "redeemed_at": "{{now}}"}),
    ])
    add("hype_train", "Hype Train", "ハイプトレイン", [
        step("channel.hype_train.begin", {"id": "{{id:train}}", **bcast(), "total": 137, "progress": 137, "goal": 500, "level": 1,
                                          "top_contributions": [], "shared_train_participants": None, "is_shared_train": False,
                                          "type": "regular", "started_at": "{{now}}", "expires_at": "{{now+300}}",
                                          "all_time_high_level": 3, "all_time_high_total": 2500}, version="2"),
        step("channel.hype_train.progress", {"id": "{{id:train}}", **bcast(), "total": 700, "progress": 200, "goal": 800, "level": 2,
                                             "top_contributions": [], "shared_train_participants": None, "is_shared_train": False,
                                             "type": "regular", "started_at": "{{now}}", "expires_at": "{{now+300}}"}, 2000, version="2"),
        step("channel.hype_train.end", {"id": "{{id:train}}", **bcast(), "total": 1200, "level": 2,
                                        "top_contributions": [{**user_fields("user", USERS[5]), "type": "bits", "total": 600},
                                                              {**user_fields("user", USERS[0]), "type": "subscription", "total": 500}],
                                        "shared_train_participants": None, "is_shared_train": False, "type": "regular",
                                        "started_at": "{{now}}", "ended_at": "{{now}}", "cooldown_ends_at": "{{now+3600}}"}, 3000, version="2"),
    ])
    add("ad_break", "Ad break (30 s)", "広告 (30秒)", [
        step("channel.ad_break.begin", {"duration_seconds": 30, "started_at": "{{now}}", "is_automatic": True, **bcast(),
                                        "requester_user_id": B_ID, "requester_user_login": B_LOGIN, "requester_user_name": B_NAME}),
    ])
    # ここからはチャット上の通知だけのもの
    add("announcement", "Announcement", "アナウンス", [
        step("channel.chat.notification", notice_event(USERS[2], "announcement", {"color": "PRIMARY"}, "",
                                                       "今日は 22 時までの配信です よろしくお願いします HeyGuys")),
    ])
    add("bits_badge_tier", "Bits badge tier", "Bits バッジ", [
        step("channel.chat.notification", notice_event(USERS[5], "bits_badge_tier", {"tier": 1000},
                                                       f"{USERS[5][2]} just earned a new 1K Bits badge!")),
    ])
    add("charity_donation", "Charity donation", "チャリティー", [
        step("channel.chat.notification", notice_event(USERS[1], "charity_donation", {
            "charity_name": "Example Charity", "amount": {"value": 500, "decimal_place": 2, "currency": "USD"}},
            f"{USERS[1][2]}: Donated USD 5.00 to support Example Charity")),
    ])
    add("prime_paid_upgrade", "Prime to paid", "Prime から有料へ", [
        step("channel.chat.notification", notice_event(USERS[4], "prime_paid_upgrade", {"sub_tier": "1000"},
                                                       f"{USERS[4][2]} converted from a Prime sub to a Tier 1 sub!")),
    ])
    add("gift_paid_upgrade", "Gift sub continued", "ギフトの継続", [
        step("channel.chat.notification", notice_event(RECIPIENTS[0], "gift_paid_upgrade", {
            "gifter_is_anonymous": False, **user_fields("gifter_user", GIFTER)},
            f"{RECIPIENTS[0][2]} is continuing the Gift Sub they got from {GIFTER[2]}!")),
    ])
    add("pay_it_forward", "Pay it forward", "恩送り", [
        step("channel.chat.notification", notice_event(RECIPIENTS[1], "pay_it_forward", {
            "gifter_is_anonymous": False, **user_fields("gifter_user", GIFTER)},
            f"{RECIPIENTS[1][2]} is paying forward the Gift they got from {GIFTER[2]} to the community!")),
    ])
    add("unraid", "Unraid", "レイドの取り消し", [
        step("channel.chat.notification", notice_event(RAIDER, "unraid", {}, "The raid has been canceled.")),
    ])
    add("watch_streak", "Watch streak", "連続視聴", [
        step("channel.chat.notification", notice_event(USERS[6], "watch_streak", {"streak_count": 10, "channel_points_awarded": 450},
                                                       f"{USERS[6][2]} watched 10 consecutive streams this month and sparked a watch streak!",
                                                       "10 回目！")),
    ])
    add("modiversary", "Modiversary", "モデレーター記念日", [
        step("channel.chat.notification", notice_event(USERS[2], "modiversary", {"months": 24},
                                                       f"{USERS[2][2]} is celebrating 2 years as a moderator!")),
    ])
    add("gifted_drops_summary", "Gifted Drops", "ドロップのギフト", [
        step("channel.chat.notification", notice_event(USERS[3], "gifted_drops_summary", {"recipient_count": 5},
                                                       f"{USERS[3][2]} gifted Drops to 5 viewers!")),
    ])
    return tests


# ================= チャットの 100 件 =================

def chat_messages():
    U = USERS
    plain = [
        "こんばんは！", "初見です よろしくお願いします", "わこつです", "今日も来ました", "この曲好き",
        "ナイス！", "うまい", "おしい！", "フルコン期待", "その譜面むずかしそう",
        "88888888", "草", "wwwww", "今のどうやったの", "手元が見たい",
        "Hello from the US", "nice run!", "GG", "that was clean", "first time here, love the stream",
        "what song is this?", "so smooth", "how many hours have you played?", "lets gooo", "rip combo",
        "お疲れさまです", "おかえりなさい", "飲み物補給してね", "次の曲も楽しみ", "今日は何曲やるの？",
        "音ズレしてない？", "ちょっと休憩しよう", "神プレイ", "鳥肌立った", "アンコール！",
        "BGM 何ですか", "サーベルの色きれい", "その曲の作者さん好き", "明日も配信ありますか", "おやすみなさい",
        "brb", "back", "hype hype hype", "this map is insane", "wow",
    ]
    with_emotes = [
        "Kappa", "おつかれ Kappa", "SeemsGood ナイス", "LUL LUL LUL", "NotLikeThis 落ちた",
        "Kreygasm この曲最高", "BibleThump", "HeyGuys こんにちは", "VoHiYo", "4Head 4Head",
        "ResidentSleeper ...うそうそ", "CoolCat かわいい",
    ]
    third_party = [  # BTTV / FFZ のグローバルエモートの名前（読み込めていれば画像になる。なければ文字のまま）
        "FeelsBirthdayMan", "SourPls SourPls", "ConcernDoge", "CatBag かわいい", "ZreknarF", "monkaS 緊張する", "LilZ", "BeanieHipster",
    ]
    emoji = ["👍", "🎉🎉🎉", "😂 草", "🔥 すごい", "❤️ ありがとう", "👏👏", "🎵 いい曲", "✨ きれい"]
    mentions = ["@broadcaster こんばんは！", "@broadcaster 今の神", "@mod_kuma いつもありがとう", "@sakura_test それな",
                "@broadcaster おすすめの曲ありますか", "@vip_neko わかる"]
    longs = [
        "今日の配信ずっと見てたけど、エキスパートプラスの譜面でもほとんどミスしないのが本当にすごいと思う。どれくらい練習したらそこまでいけるのか気になる",
        "This is a long message to check how the chat wraps text across several lines when someone writes a lot at once, which happens more often than you would think.",
        "長文テスト 長文テスト 長文テスト 長文テスト 長文テスト 長文テスト 長文テスト 長文テスト 長文テスト 長文テスト 長文テスト 長文テスト",
    ]
    out = []

    def msg(u, text, **kw):
        out.append({"type": "channel.chat.message", "version": "1", "event": chat_event(u, text, **kw)})

    for i, t in enumerate(plain):
        msg(U[i % len(U)], t)
    for i, t in enumerate(with_emotes):
        msg(U[(i + 3) % len(U)], t)
    for i, t in enumerate(third_party):
        msg(U[(i + 5) % len(U)], t)
    for i, t in enumerate(emoji):
        msg(U[(i + 1) % len(U)], t)
    for i, t in enumerate(mentions):
        msg(U[(i + 7) % len(U)], t)
    for i, t in enumerate(longs):
        msg(U[(i + 2) % len(U)], t)
    # 返信（直前のテストのメッセージへの返信）
    for i, t in enumerate(["それな", "同じこと思った", "ほんとそれ"]):
        msg(U[(i + 4) % len(U)], t, reply={
            "parent_message_id": "{{last_message_id}}", "parent_message_body": "{{last_message_text}}",
            "parent_user_id": "{{last_chatter_id}}", "parent_user_login": "{{last_chatter_login}}", "parent_user_name": "{{last_chatter_name}}",
            "thread_message_id": "{{last_message_id}}",
            "thread_user_id": "{{last_chatter_id}}", "thread_user_login": "{{last_chatter_login}}", "thread_user_name": "{{last_chatter_name}}",
        })
    # /me
    for i, t in enumerate(["拍手している", "踊っている", "waves"]):
        msg(U[(i + 6) % len(U)], t, action=True)
    # ハイライト、自己紹介
    msg(U[1], "ハイライトのメッセージです", message_type="channel_points_highlighted")
    msg(U[8], "Highlighted message test", message_type="channel_points_highlighted")
    msg(U[4], "はじめまして！ 最近ビートセイバーを始めました", message_type="user_intro")
    # 削除とタイムアウト（直前のテストのメッセージ）
    for _ in range(2):
        out.append({"type": "channel.chat.message_delete", "version": "1", "event": {
            **bcast(), "target_user_id": "{{last_chatter_id}}", "target_user_login": "{{last_chatter_login}}",
            "target_user_name": "{{last_chatter_name}}", "message_id": "{{last_message_id}}"}})
    out.append({"type": "channel.chat.clear_user_messages", "version": "1", "event": {
        **bcast(), "target_user_id": "{{last_chatter_id}}", "target_user_login": "{{last_chatter_login}}",
        "target_user_name": "{{last_chatter_name}}"}})
    # 数をそろえる
    fillers = ["いいね", "おお", "すごい", "nice", "Pog", "やったー", "かっこいい", "楽しい", "あと少し", "がんばれ"]
    i = 0
    while len(out) < 100:
        msg(U[i % len(U)], fillers[i % len(fillers)])
        i += 1
    assert len(out) == 100, len(out)
    for m in out:
        assert not m["event"].get("message", {}).get("text", "").lstrip().startswith("!"), m
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, data in (("notifications.json", notifications()), ("chat-messages.json", chat_messages())):
        with open(os.path.join(OUT, name), "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
            f.write("\n")
        print(name, len(data))


if __name__ == "__main__":
    main()
