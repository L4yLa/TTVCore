using System;
using System.Threading;

namespace TTVCore.Utilities
{
    /// <summary>Unity のメインスレッドへ処理を渡す。<see cref="Initialize"/> はメインスレッドで呼ぶ。</summary>
    internal static class MainThread
    {
        private static SynchronizationContext s_context;

        public static void Initialize()
        {
            s_context = SynchronizationContext.Current;
            if (s_context == null) {
                Plugin.Log.Error("MainThread: SynchronizationContext is not available. Main-thread actions (opening the browser, events) will not run.");
            }
        }

        /// <summary>メインスレッドで実行する。例外はログに記録する。</summary>
        public static void Post(Action action)
        {
            if (action == null || s_context == null) {
                return;
            }
            s_context.Post(_ => {
                try {
                    action();
                }
                catch (Exception e) {
                    Plugin.Log.Error($"MainThread: action failed: {e}");
                }
            }, null);
        }
    }
}
