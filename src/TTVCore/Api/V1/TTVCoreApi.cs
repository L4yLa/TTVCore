namespace TTVCore.Api.V1
{
    /// <summary>
    /// TTVCore の公開 API V1 の入口。V1 の中ではメンバーの追加だけを行う。
    /// イベントはすべて Unity のメインスレッドで発生する。購読者が例外を出してもログに記録するだけで、
    /// 他の購読者には影響しない。
    /// </summary>
    public static class TTVCoreApi
    {
        public const int MajorVersion = 1;

        /// <summary>メンバーを追加するたびに増える。後から追加されたメンバーを使う前に確認する。</summary>
        public static int MinorVersion => 0;

        public static ConnectionApi Connection { get; } = new ConnectionApi();

        public static ChannelApi Channel { get; } = new ChannelApi();

        public static ChatApi Chat { get; } = new ChatApi();

        public static NotificationsApi Notifications { get; } = new NotificationsApi();

        public static ImagesApi Images { get; } = new ImagesApi();

        public static CatalogApi Catalog { get; } = new CatalogApi();
    }
}
