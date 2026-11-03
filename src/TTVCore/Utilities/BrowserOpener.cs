using UnityEngine;
using UnityEngine.SceneManagement;

namespace TTVCore.Utilities
{
    /// <summary>
    /// 認証ページを既定のブラウザーで開く。曲の最中はメニューに戻ってから開く。
    /// URL には認証コードが含まれるためログに出さない。
    /// </summary>
    internal static class BrowserOpener
    {
        private const string GameScene = "GameCore";
        private const string MenuScene = "MainMenu";

        private static string s_pendingUrl;

        public static void Initialize()
        {
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        public static void Shutdown()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            s_pendingUrl = null;
        }

        public static void Open(string url)
        {
            if (string.IsNullOrEmpty(url)) {
                return;
            }
            MainThread.Post(() => {
                if (SceneManager.GetActiveScene().name == GameScene) {
                    s_pendingUrl = url;
                    Plugin.Log.Info("Browser: playing a song; the authorization page will open after returning to the menu.");
                    return;
                }
                s_pendingUrl = null;
                Application.OpenURL(url);
                Plugin.Log.Info("Browser: opened the authorization page.");
            });
        }

        public static void CancelPending()
        {
            MainThread.Post(() => s_pendingUrl = null);
        }

        private static void OnActiveSceneChanged(Scene previous, Scene next)
        {
            if (s_pendingUrl == null || next.name != MenuScene) {
                return;
            }
            var url = s_pendingUrl;
            s_pendingUrl = null;
            Application.OpenURL(url);
            Plugin.Log.Info("Browser: returned to the menu; opened the authorization page.");
        }
    }
}
