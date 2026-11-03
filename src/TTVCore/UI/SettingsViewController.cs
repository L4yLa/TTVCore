using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using System;
using System.Collections.Generic;
using TMPro;
using TTVCore.Api.V1;
using TTVCore.Configuration;
using TTVCore.Utilities;

namespace TTVCore.UI
{
    /// <summary>TTVCore の設定画面（認証、言語、サードパーティの MOD からの送信）。</summary>
    internal sealed class SettingsViewController : BSMLResourceViewController
    {
        // 今の言語が読めなくても選べるよう、選択肢は言語によらず同じ表記にする
        private const string LanguageSystem = "System / システム";
        private const string LanguageEnglish = "English";
        private const string LanguageJapanese = "日本語";

        public override string ResourceName => "TTVCore.UI.SettingsView.bsml";

        public override string Content => Localization.Bsml(base.Content);

        [UIValue("languages")]
        private readonly List<object> _languages = new List<object> { LanguageSystem, LanguageEnglish, LanguageJapanese };

        [UIValue("language")]
        private string Language
        {
            get {
                switch (CoreSettings.Instance.Language) {
                    case UiLanguage.English: return LanguageEnglish;
                    case UiLanguage.Japanese: return LanguageJapanese;
                    default: return LanguageSystem;
                }
            }
            set {
                var language = value == LanguageEnglish ? UiLanguage.English
                             : value == LanguageJapanese ? UiLanguage.Japanese
                             : UiLanguage.System;
                CoreSettings.Instance.Language = language;
                CoreSettings.Save();
                Localization.Apply(language);
            }
        }

        [UIValue("auth-status")]
        private string AuthStatus
        {
            get {
                var c = TTVCoreApi.Connection;
                switch (c.AuthState) {
                    case AuthState.Authenticated:
                        return $"<color=#7CFC00>{Localization.T("Connected to Twitch", "Twitch に接続中")}  <b>{c.LoggedInUser?.DisplayName}</b></color>";
                    case AuthState.WaitingForUser:
                        return Localization.T("Waiting for authorization in the browser", "ブラウザーでの認証を待っています");
                    case AuthState.Unauthenticated:
                        return Localization.T("Not connected to Twitch", "Twitch に接続していません");
                    default:
                        return Localization.T("Checking the saved authorization", "保存された認証を確認しています");
                }
            }
        }

        [UIValue("auth-button-text")]
        private string AuthButtonText => TTVCoreApi.Connection.AuthState == AuthState.Authenticated
            ? Localization.T("Reauthorize", "再認証")
            : Localization.T("Authorize", "認証");

        [UIValue("auth-button-interactable")]
        private bool AuthButtonInteractable => TTVCoreApi.Connection.AuthState != AuthState.WaitingForUser
                                               && TTVCoreApi.Connection.AuthState != AuthState.Checking;

        [UIValue("pack-vram")]
        private int PackVram
        {
            get => CoreSettings.Instance.PackVramLimitMb;
            set {
                CoreSettings.Instance.PackVramLimitMb = value;
                CoreSettings.Save();
                Images.PackStore.MarkDirty();
            }
        }

        [UIValue("third-party-send")]
        private bool ThirdPartySend
        {
            get => CoreSettings.Instance.ThirdPartySendEnabled;
            set {
                CoreSettings.Instance.ThirdPartySendEnabled = value;
                CoreSettings.Save();
            }
        }

#if DEBUG
        private static readonly bool PreviewEnabled = true;
#else
        private static readonly bool PreviewEnabled = false;
#endif

        /// <summary>Debug ビルドのみ。受信したチャットを TTVCore の TMP 埋め込みで表示し、画像とアニメーションを確かめる。</summary>
        [UIValue("show-preview")]
        private bool ShowPreview => PreviewEnabled;

        [UIComponent("preview-text")]
        private TextMeshProUGUI _previewText = null;

        private readonly List<TimelineItem> _previewItems = new List<TimelineItem>();
        private TmpBinding _previewBinding;

        [UIAction("authenticate")]
        private void Authenticate()
        {
            TTVCoreApi.Connection.StartAuthentication();
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            TTVCoreApi.Connection.AuthStateChanged += this.OnStateChanged;
            if (PreviewEnabled) {
                TTVCoreApi.Chat.TimelineItemReceived += this.OnTimelineItem;
            }
            this.Refresh();
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            TTVCoreApi.Connection.AuthStateChanged -= this.OnStateChanged;
            TTVCoreApi.Chat.TimelineItemReceived -= this.OnTimelineItem;
            _previewBinding?.Dispose();
            _previewBinding = null;
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
        }

        private void OnStateChanged(AuthState _) => this.Refresh();


        private void OnTimelineItem(TimelineItem item)
        {
            if (_previewText == null) {
                return;
            }
            _previewItems.Add(item);
            if (_previewItems.Count > 4) {
                _previewItems.RemoveAt(0);
            }
            var builder = TTVCoreApi.Images.CreateTmpContent();
            foreach (var i in _previewItems) {
                var m = i is TimelineMessage tm ? tm.Message : (i as TimelineNotification)?.Message;
                var notice = (i as TimelineNotification)?.Notice;
                if (i is TimelineNotification tn) {
                    builder.AppendRichText($"<color=#FFD700>[{tn.Kind}]</color> ");
                    if (notice != null) {
                        builder.AppendText(notice.SystemMessage + " ");
                    }
                }
                if (m != null) {
                    var big = m.MessageType == ChatMessageType.PowerUpsGigantifiedEmote;
                    builder.AppendRichText($"<color={m.Sender.Color ?? "#FFFFFF"}><b>").AppendText(m.Sender.DisplayName).AppendRichText("</b></color>: ");
                    for (var f = 0; f < m.Fragments.Count; f++) {
                        // 巨大化エモートは最後のエモートを4倍で表示する
                        var last = big && f == m.Fragments.Count - 1;
                        builder.AppendFragment(m.Fragments[f], last ? 4f : 1f);
                    }
                }
                builder.AppendRichText("\n");
            }
            _previewBinding?.Dispose();
            _previewBinding = TTVCoreApi.Images.Bind(_previewText, builder.Build());
        }

        private void Refresh()
        {
            this.NotifyPropertyChanged(nameof(this.AuthStatus));
            this.NotifyPropertyChanged(nameof(this.AuthButtonText));
            this.NotifyPropertyChanged(nameof(this.AuthButtonInteractable));
        }
    }
}
