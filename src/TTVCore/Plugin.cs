using IPA;
using System;
using System.Reflection;
using System.Threading;
using TTVCore.Auth;
using TTVCore.Chat;
using TTVCore.Configuration;
using TTVCore.Emoji;
#if DEBUG
using TTVCore.Diagnostics;
#endif
using TTVCore.EventSub;
using TTVCore.Images;
using TTVCore.Utilities;
using UnityEngine;
using IPALogger = IPA.Logging.Logger;

namespace TTVCore
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal static IPALogger Log { get; private set; }

        private CancellationTokenSource _cts;
        private TokenManager _tokenManager;
        private EventSubManager _eventSubManager;
#if DEBUG
        private DebugInjector _debugInjector;
#endif

        [Init]
        public Plugin(IPALogger logger)
        {
            Log = logger;
        }

        [OnStart]
        public void OnApplicationStart()
        {
            Log.Info($"Runtime: Unity {Application.unityVersion}, CLR {Environment.Version}, Mono {GetMonoVersion()}");

            MainThread.Initialize();
            BrowserOpener.Initialize();
            CoreSettings.Load();
            Utilities.Localization.Apply(CoreSettings.Instance.Language);
            Images.DisplayStats.Load();
            EmojiTable.Load();
#if DEBUG
            DebugOptions.Load();
            DebugEventLogger.Attach();
#endif

            _cts = new CancellationTokenSource();
            ChatSendQueue.Instance.Start(_cts.Token);
            TimelineAggregator.Instance.Start(_cts.Token);
            ImageManager.Initialize(_cts.Token);
            CoreUpdater.Create();
            UI.SettingsMenu.Initialize();

            _tokenManager = new TokenManager();
            _tokenManager.Start();

            _eventSubManager = new EventSubManager();
            _eventSubManager.Start();

#if DEBUG
            _debugInjector = new DebugInjector();
            _debugInjector.Start();
#endif
        }

        [OnExit]
        public void OnApplicationQuit()
        {
#if DEBUG
            _debugInjector?.Stop();
#endif
            _eventSubManager?.Stop();
            _tokenManager?.Stop();
            _cts?.Cancel();
            Images.DisplayStats.Save();
            BrowserOpener.Shutdown();
        }

        private static string GetMonoVersion()
        {
            try {
                var monoRuntime = Type.GetType("Mono.Runtime");
                var getDisplayName = monoRuntime?.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
                return getDisplayName?.Invoke(null, null) as string ?? "(unknown)";
            }
            catch {
                return "(unknown)";
            }
        }
    }
}
