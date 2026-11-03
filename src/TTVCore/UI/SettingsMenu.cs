using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.MenuButtons;
using HMUI;
using System;
using TTVCore.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TTVCore.UI
{
    /// <summary>メインメニューの左側の MOD ボタンから開く設定画面。</summary>
    internal static class SettingsMenu
    {
        private const float RetryInterval = 0.5f;

        private static bool s_registered;
        private static bool s_pending;
        private static float s_nextTry;
        private static bool s_rebuild;
        private static MenuButton s_button;
        private static SettingsFlowCoordinator s_flow;

        private static string HoverHint => Localization.T("Twitch connection settings", "Twitch との接続の設定");

        public static void Initialize()
        {
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            Localization.Changed += OnLanguageChanged;
        }

        private static void OnActiveSceneChanged(Scene previous, Scene next)
        {
            if (!s_registered && next.name == "MainMenu") {
                s_pending = true;
                s_nextTry = 0f;
            }
        }

        private static void OnLanguageChanged()
        {
            if (s_button != null) {
                s_button.HoverHint = HoverHint;
            }
            // 設定の値を変えている最中に画面を作り直さないよう、次のフレームで行う
            s_rebuild = true;
        }

        /// <summary>
        /// メインスレッドで毎フレーム呼ぶ。MainMenu に切り替わった直後は BSML の準備ができていないため、
        /// 登録できるまで間隔をあけて試す。
        /// </summary>
        public static void Update()
        {
            if (s_rebuild) {
                s_rebuild = false;
                Rebuild();
            }
            if (!s_pending || Time.realtimeSinceStartup < s_nextTry) {
                return;
            }
            try {
                s_button = new MenuButton("TTVCore", HoverHint, Show);
                MenuButtons.Instance.RegisterButton(s_button);
                s_registered = true;
                s_pending = false;
            }
            catch (InvalidOperationException) {
                s_button = null;
                s_nextTry = Time.realtimeSinceStartup + RetryInterval;
            }
            catch (Exception e) {
                s_button = null;
                s_pending = false;
                Plugin.Log.Error($"UI: failed to add the menu button: {e}");
            }
        }

        /// <summary>言語を変えたら画面を作り直す。開いていれば、作り直した画面を開き直す。</summary>
        private static void Rebuild()
        {
            if (s_flow == null) {
                return;
            }
            var wasOpen = s_flow.isActivated;
            try {
                if (wasOpen) {
                    BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(s_flow, immediately: true);
                }
                UnityEngine.Object.Destroy(s_flow.gameObject);
                s_flow = null;
                if (wasOpen) {
                    Show(true);
                }
            }
            catch (Exception e) {
                Plugin.Log.Error($"UI: failed to rebuild the settings screen: {e}");
            }
        }

        private static void Show() => Show(false);

        private static void Show(bool immediately)
        {
            if (s_flow == null) {
                s_flow = BeatSaberUI.CreateFlowCoordinator<SettingsFlowCoordinator>();
            }
            // MOD のボタンはメインメニューにあるため、メインの FlowCoordinator から開く
            BeatSaberUI.MainFlowCoordinator.PresentFlowCoordinator(s_flow, immediately: immediately);
        }
    }

    internal sealed class SettingsFlowCoordinator : FlowCoordinator
    {
        private SettingsViewController _view;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation) {
                this.SetTitle("TTVCore");
                this.showBackButton = true;
                _view = BeatSaberUI.CreateViewController<SettingsViewController>();
            }
            if (addedToHierarchy) {
                this.ProvideInitialViewControllers(_view);
            }
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this);
        }

        private void OnDestroy()
        {
            // 作り直すときに、古い画面を残さない
            if (_view != null) {
                Destroy(_view.gameObject);
            }
        }
    }
}
