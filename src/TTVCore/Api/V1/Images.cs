using System;

namespace TTVCore.Api.V1
{
    /// <summary>
    /// 画像を指す値。フラグメントや Catalog から受け取る。中身の形式は公開しないため、
    /// 保存したり自分で組み立てたりしないこと。
    /// </summary>
    public readonly struct ImageKey : IEquatable<ImageKey>
    {
        public string Value { get; }

        public bool IsValid => !string.IsNullOrEmpty(this.Value);

        internal ImageKey(string value)
        {
            this.Value = value;
        }

        public bool Equals(ImageKey other) => string.Equals(this.Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ImageKey other && this.Equals(other);

        public override int GetHashCode() => this.Value == null ? 0 : StringComparer.Ordinal.GetHashCode(this.Value);

        public override string ToString() => this.Value ?? "";

        public static bool operator ==(ImageKey a, ImageKey b) => a.Equals(b);

        public static bool operator !=(ImageKey a, ImageKey b) => !a.Equals(b);
    }
}
