using System;

namespace TTVCore.Utilities
{
    internal static class EventInvoker
    {
        /// <summary>購読者を1つずつ呼び、1つの例外が他に影響しないようにする。</summary>
        public static void Invoke<T>(Action<T> handler, T arg, string eventName)
        {
            if (handler == null) {
                return;
            }
            foreach (var d in handler.GetInvocationList()) {
                try {
                    ((Action<T>)d)(arg);
                }
                catch (Exception e) {
                    var owner = d.Method.DeclaringType;
                    Plugin.Log.Error($"{eventName}: a handler in {owner?.Assembly.GetName().Name} ({owner?.FullName}) threw: {e}");
                }
            }
        }

        /// <summary>メインスレッドで呼ぶ。</summary>
        public static void Post<T>(Func<Action<T>> handler, T arg, string eventName)
        {
            MainThread.Post(() => Invoke(handler(), arg, eventName));
        }
    }
}
