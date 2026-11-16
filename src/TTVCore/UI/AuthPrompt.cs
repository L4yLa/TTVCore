using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using HMUI;
using System;
using System.Collections.Generic;
using System.Linq;
using TTVCore.Api.V1;
using TTVCore.Auth;
using TTVCore.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TTVCore.UI
{
    /// <summary>認証を始めた理由。案内のダイアログを出すかどうかの判定に使う。</summary>
    internal enum AuthPromptReason
    {
        /// <summary>起動時に有効な認証がない。</summary>
        Startup,
        /// <summary>起動中に認証が切れた。</summary>
        Lost,
        /// <summary>設定画面のボタン、または公開 API から始めた。</summary>
        Manual,
    }

    /// <summary>
    /// 認証の案内のダイアログ。認証ページは PC のブラウザーで開くため、ヘッドセットを着けたままでは気づけない。
    /// 認証ページを開いたときに、メニューでだけダイアログを出す（曲の最中や画面の切り替え中は、メニューに戻ってから出す）。
    /// <list type="bullet">
    /// <item>起動時と、起動中に認証が切れたときは、理由ごとに起動中1回まで。利用者が自分で始めたときは毎回出す。</item>
    /// <item>認証が済んだら自動で閉じる。</item>
    /// <item>「無視」で閉じても認証は止めず、コードの期限まで許可を待つ。</item>
    /// </list>
    /// 状態はメインスレッドからだけ触る。
    /// </summary>
    internal static class AuthPrompt
    {
        private const string MenuScene = "MainMenu";

        private static readonly HashSet<AuthPromptReason> s_shownReasons = new HashSet<AuthPromptReason>();
        private static AuthPromptReason? s_pendingReason;
        private static bool s_closePending;
        private static string s_code;
        private static string s_uri;
        private static DateTime s_expiresUtc;
        private static AuthPromptFlowCoordinator s_flow;
        private static FlowCoordinator s_parent;
        private static GameScenesManager s_scenes;

        /// <summary>表示中のコード。まだなければ null。</summary>
        public static string Code => s_code;

        /// <summary>ダイアログのコードの表示を差し替える。</summary>
        public static event Action CodeChanged;

        public static void Initialize()
        {
            TTVCoreApi.Connection.AuthStateChanged += OnAuthStateChanged;
        }

        /// <summary>認証ページを開いた（どのスレッドから呼んでもよい）。</summary>
        public static void NotifyDeviceCode(string code, string uri, int expiresIn, AuthPromptReason reason)
        {
            var expires = DateTime.UtcNow.AddSeconds(expiresIn > 0 ? expiresIn : 1800);
            MainThread.Post(() => {
                s_code = code;
                s_uri = uri;
                s_expiresUtc = expires;
                try {
                    CodeChanged?.Invoke();
                }
                catch (Exception e) {
                    Plugin.Log.Error($"AuthPrompt: {e}");
                }
                // 表示中なら、コードの差し替えだけで済む
                if (IsShown) {
                    return;
                }
                if (reason != AuthPromptReason.Manual && s_shownReasons.Contains(reason)) {
                    return;
                }
                s_pendingReason = reason;
            });
        }

        /// <summary>認証の待ち受けが終わった（許可、拒否、期限切れ）。どのスレッドから呼んでもよい。</summary>
        public static void NotifyDeviceFlowEnded()
        {
            // 終わった後の「再度開く」は、古いページではなく新しく認証を始める
            MainThread.Post(() => s_uri = null);
        }

        private static bool IsShown => s_flow != null && s_flow.isActivated;

        private static void OnAuthStateChanged(AuthState state)
        {
            if (state != AuthState.Authenticated) {
                return;
            }
            s_pendingReason = null;
            if (IsShown) {
                s_closePending = true;
            }
        }

        /// <summary>メインスレッドで毎フレーム呼ぶ。</summary>
        public static void Update()
        {
            if (s_closePending) {
                if (!IsShown) {
                    s_closePending = false;
                }
                else if (CanClose()) {
                    s_closePending = false;
                    Close();
                }
                return;
            }
            if (s_pendingReason == null || SceneManager.GetActiveScene().name != MenuScene) {
                return;
            }
            FlowCoordinator top;
            try {
                // メインの画面は、子の画面（設定画面など）を出している間は非アクティブになるため、アクティブかどうかは見ない
                var main = BeatSaberUI.MainFlowCoordinator;
                if (main == null) {
                    return;
                }
                top = main.YoungestChildFlowCoordinatorOrSelf();
            }
            catch (Exception) {
                return;
            }
            if (!CanChangeScreens(top)) {
                return;
            }
            var reason = s_pendingReason.Value;
            s_pendingReason = null;
            if (top is AuthPromptFlowCoordinator) {
                return;
            }
            try {
                if (s_flow == null) {
                    s_flow = BeatSaberUI.CreateFlowCoordinator<AuthPromptFlowCoordinator>();
                }
                // メニューで今開いている一番奥の画面の上に重ねる。ボタンを押すまでメニューは操作できない
                top.PresentFlowCoordinator(s_flow);
                s_parent = top;
                s_shownReasons.Add(reason);
                Plugin.Log.Info($"AuthPrompt: shown ({reason}).");
            }
            catch (Exception e) {
                Plugin.Log.Error($"AuthPrompt: failed to show the dialog: {e}");
            }
        }

        /// <summary>画面の切り替えの途中でなければ true。途中で重ねたり閉じたりすると例外になる。</summary>
        private static bool CanChangeScreens(FlowCoordinator flow)
        {
            if (flow == null || !flow.isActivated || flow.isInTransition) {
                return false;
            }
            if (s_scenes == null) {
                s_scenes = Resources.FindObjectsOfTypeAll<GameScenesManager>().FirstOrDefault();
            }
            return s_scenes == null || !s_scenes.isInTransition;
        }

        /// <summary>
        /// ダイアログを閉じられるか。重ねた先の画面は、子の画面を出している間は非アクティブになるため、
        /// アクティブかどうかは見ず、切り替えの途中でないことだけを確かめる。
        /// </summary>
        private static bool CanClose()
        {
            if (s_parent == null || s_parent.isInTransition || s_flow.isInTransition) {
                return false;
            }
            if (s_scenes == null) {
                s_scenes = Resources.FindObjectsOfTypeAll<GameScenesManager>().FirstOrDefault();
            }
            return s_scenes == null || !s_scenes.isInTransition;
        }

        /// <summary>「再度開く」。コードが有効なら同じページを、期限が切れていれば新しく認証を始めて開く。</summary>
        public static void Reopen()
        {
            if (s_uri != null && DateTime.UtcNow < s_expiresUtc) {
                BrowserOpener.Open(s_uri);
                return;
            }
            TokenManager.Instance?.StartAuthentication(AuthPromptReason.Manual);
        }

        /// <summary>「無視」と、認証が済んだとき。認証の待ち受けは止めない。</summary>
        public static void Close()
        {
            if (!IsShown || s_parent == null) {
                return;
            }
            try {
                s_parent.DismissFlowCoordinator(s_flow);
            }
            catch (Exception e) {
                Plugin.Log.Error($"AuthPrompt: failed to close the dialog: {e}");
            }
        }
    }

    internal sealed class AuthPromptFlowCoordinator : FlowCoordinator
    {
        private AuthPromptViewController _view;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation) {
                this.showBackButton = false;
                _view = BeatSaberUI.CreateViewController<AuthPromptViewController>();
            }
            if (addedToHierarchy) {
                // 左右の画面は空にし、ダイアログだけを出す
                this.ProvideInitialViewControllers(_view);
            }
        }
    }

    internal sealed class AuthPromptViewController : BSMLResourceViewController
    {
        public override string ResourceName => "TTVCore.UI.AuthPromptView.bsml";

        public override string Content => Localization.Bsml(base.Content);

        [UIValue("message-text")]
        private string MessageText => Localization.T(
            $"Twitch authorization is required.\nPlease check the browser on your PC and authorize.\nCode: {AuthPrompt.Code ?? "-"}\nThis screen will close automatically when authorization is complete.",
            // 句読点はフォントで正しく表示されないため使わない
            $"Twitch の認証が必要です\nPC のブラウザを確認して認証してください\nコード: {AuthPrompt.Code ?? "-"}\n認証が完了するとこの画面は自動で閉じます");

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            AuthPrompt.CodeChanged += this.OnCodeChanged;
            this.NotifyPropertyChanged(nameof(this.MessageText));
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            AuthPrompt.CodeChanged -= this.OnCodeChanged;
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
        }

        private void OnCodeChanged() => this.NotifyPropertyChanged(nameof(this.MessageText));

        [UIAction("reopen")]
        private void ReopenClicked() => AuthPrompt.Reopen();

        [UIAction("ignore")]
        private void IgnoreClicked() => AuthPrompt.Close();
    }
}
