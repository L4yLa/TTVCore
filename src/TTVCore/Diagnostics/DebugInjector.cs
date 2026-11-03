#if DEBUG
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TTVCore.EventSub;

namespace TTVCore.Diagnostics
{
    /// <summary>
    /// Debug ビルドのみ。UserData\TTVCore\inject\ に置いた *.json を、受信した通知として処理する。
    /// 形式は WebSocket のメッセージ（{"metadata": ..., "payload": ...}）か、公式リファレンスの例と同じ
    /// {"subscription": ..., "event": ...}（metadata は補う）。処理したファイルは inject\done\ へ移す。
    /// </summary>
    internal sealed class DebugInjector
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private CancellationTokenSource _cts;

        public static string InjectDirectory => Path.Combine(DebugOptions.BaseDirectory, "inject");
        private static string DoneDirectory => Path.Combine(InjectDirectory, "done");

        public void Start()
        {
            try {
                Directory.CreateDirectory(DoneDirectory);
            }
            catch (Exception e) {
                Plugin.Log.Warn($"[DEBUG] Inject: cannot create the folder ({e.GetType().Name}: {e.Message}).");
                return;
            }
            Plugin.Log.Notice($"[DEBUG] Inject: put notification JSON files into {InjectDirectory}");
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _ = Task.Run(() => this.RunAsync(ct));
        }

        public void Stop()
        {
            _cts?.Cancel();
        }

        private async Task RunAsync(CancellationToken ct)
        {
            try {
                while (true) {
                    await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                    string[] files;
                    try {
                        files = Directory.GetFiles(InjectDirectory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
                    }
                    catch (Exception) {
                        continue;
                    }
                    foreach (var file in files) {
                        string text;
                        try {
                            text = File.ReadAllText(file, Encoding.UTF8);
                            var done = Path.Combine(DoneDirectory, Path.GetFileName(file));
                            File.Copy(file, done, overwrite: true);
                            File.Delete(file);
                        }
                        catch (IOException) {
                            // 書き込み中などで開けない。次の回に回す
                            continue;
                        }
                        EventSubManager.Instance?.InjectForDebug(text, Path.GetFileName(file));
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            }
            catch (Exception e) {
                Plugin.Log.Error($"[DEBUG] Inject: stopped unexpectedly: {e}");
            }
        }
    }
}
#endif
