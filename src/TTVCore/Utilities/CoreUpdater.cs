using TTVCore.Images;
using TTVCore.Tmp;
using UnityEngine;

namespace TTVCore.Utilities
{
    /// <summary>毎フレームの処理（画像の転送、TMP のアニメーションと作り直し）。</summary>
    internal sealed class CoreUpdater : MonoBehaviour
    {
        public static void Create()
        {
            var go = new GameObject("TTVCore");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<CoreUpdater>();
        }

        private void Update()
        {
            ImageManager.Update();
            TmpBindings.Update();
            UI.SettingsMenu.Update();
        }
    }
}
