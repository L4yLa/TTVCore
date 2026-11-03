"""絵文字の表と画像のまとめファイルを作る。

使い方:
    python build_emoji.py <emoji-test.txt> <twemoji の assets/72x72 フォルダー> <出力先フォルダー>

出力:
    emoji-table.txt  1行に「並び（UTF-16 の16進数を空白区切り）<TAB>画像のファイル名」
    twemoji.bin      画像をまとめたもの（形式は EmojiImages.cs を参照）

画像にするのは fully-qualified と minimally-qualified の並びだけ。数字・#・* で始まる並びは文字のまま表示する。
画像のファイル名は Twemoji の規則に合わせる: ZWJ を含まない並びは U+FE0F を除き、含む並びは残す。
見つからなければ U+FE0F をすべて除いた名前で探し、それでもなければ U+FE0F の有無を無視して同じ並びの画像を使う
（minimally-qualified の並びは、fully-qualified の並びの画像で表示する）。
"""
import os
import struct
import sys


def strip_fe0f(name):
    return "-".join(p for p in name.split("-") if p != "fe0f")


def file_name(cps, available, stripped):
    has_zwj = 0x200D in cps
    primary = cps if has_zwj else [c for c in cps if c != 0xFE0F]
    name = "-".join(format(c, "x") for c in primary)
    if name in available:
        return name
    name = "-".join(format(c, "x") for c in cps if c != 0xFE0F)
    if name in available:
        return name
    return stripped.get(name)


def utf16_units(cps):
    data = "".join(chr(c) for c in cps).encode("utf-16-le")
    return [struct.unpack_from("<H", data, i)[0] for i in range(0, len(data), 2)]


def main():
    test_path, image_dir, out_dir = sys.argv[1:4]
    available = {f[:-4] for f in os.listdir(image_dir) if f.endswith(".png")}
    stripped = {strip_fe0f(n): n for n in available}

    rows = {}
    missing = []
    with open(test_path, encoding="utf-8") as f:
        for line in f:
            line = line.split("#", 1)[0].strip()
            if not line or ";" not in line:
                continue
            seq, status = (part.strip() for part in line.split(";", 1))
            if status not in ("fully-qualified", "minimally-qualified"):
                continue
            cps = [int(x, 16) for x in seq.split()]
            if cps[0] < 0x80:
                continue
            name = file_name(cps, available, stripped)
            if name is None:
                missing.append(seq)
                continue
            rows[" ".join(format(u, "x") for u in utf16_units(cps))] = name

    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, "emoji-table.txt"), "w", encoding="utf-8", newline="\n") as f:
        for key in sorted(rows):
            f.write(f"{key}\t{rows[key]}\n")

    used = sorted(set(rows.values()))
    blobs = []
    for name in used:
        with open(os.path.join(image_dir, name + ".png"), "rb") as f:
            blobs.append((name, f.read()))
    # 形式: "TWEM" + 件数(int32) + 件数分の [名前の長さ(uint8) + 名前(ASCII) + 開始位置(int32) + 長さ(int32)] + 画像の本体
    header_size = 8 + sum(1 + len(n) + 8 for n, _ in blobs)
    offset = header_size
    with open(os.path.join(out_dir, "twemoji.bin"), "wb") as f:
        f.write(b"TWEM")
        f.write(struct.pack("<i", len(blobs)))
        for name, data in blobs:
            encoded = name.encode("ascii")
            f.write(struct.pack("<B", len(encoded)))
            f.write(encoded)
            f.write(struct.pack("<ii", offset, len(data)))
            offset += len(data)
        for _, data in blobs:
            f.write(data)

    print(f"sequences: {len(rows)}, images: {len(used)}, not found: {len(missing)}")
    for seq in missing:
        print("  not found:", seq)


if __name__ == "__main__":
    main()
