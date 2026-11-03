using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TTVCore.Api.V1;
using TTVCore.Catalog;
using TTVCore.Helix;
using TTVCore.Utilities;

namespace TTVCore.Chat
{
    /// <summary>共有チャットの状態と、参加チャンネルごとの一覧の読み込み。</summary>
    internal static class ChannelState
    {
        private static volatile SharedChatSession s_session;
        private static string s_broadcasterId;
        private static CancellationToken s_ct;

        public static SharedChatSession SharedChat => s_session;

        /// <summary>EventSub の接続（再接続を含む）のたびに呼ぶ。</summary>
        public static void OnConnected(string broadcasterId, CancellationToken ct)
        {
            s_broadcasterId = broadcasterId;
            s_ct = ct;
            _ = Task.Run(async () => {
                try {
                    await CatalogStore.EnsureGlobalAsync(ct).ConfigureAwait(false);
                    await CatalogStore.LoadChannelAsync(broadcasterId, includeCheermotes: true, ct).ConfigureAwait(false);
                    await RefreshSharedChatAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                }
                catch (Exception e) {
                    Plugin.Log.Error($"Channel: loading failed unexpectedly: {e}");
                }
            });
        }

        public static void OnSharedChatEvent(string type, JObject ev)
        {
            if (type == "channel.shared_chat.end") {
                SetSession(null);
                return;
            }
            SetSession(ChatParser.ParseSharedChat(ev));
        }

        private static async Task RefreshSharedChatAsync(CancellationToken ct)
        {
            var jo = await HelixClient.GetJsonAsync("shared_chat/session?broadcaster_id=" + Uri.EscapeDataString(s_broadcasterId), ct).ConfigureAwait(false);
            if (jo == null) {
                return;
            }
            var first = (jo["data"] as JArray)?.FirstOrDefault();
            if (first == null) {
                SetSession(null);
                return;
            }
            var ids = (JsonUtil.At(first, "participants") as JArray)?
                .Select(p => JsonUtil.StrOrNull(JsonUtil.At(p, "broadcaster_id")))
                .Where(id => id != null)
                .ToList() ?? new List<string>();
            var users = await HelixClient.GetUsersAsync(ids, ct).ConfigureAwait(false);
            SetSession(new SharedChatSession {
                SessionId = JsonUtil.Str(JsonUtil.At(first, "session_id")),
                HostBroadcasterId = JsonUtil.Str(JsonUtil.At(first, "host_broadcaster_id")),
                Participants = ids.Select(id => users.FirstOrDefault(u => u.Id == id) ?? new UserRef(id, id, id)).ToList(),
            });
        }

        private static void SetSession(SharedChatSession session)
        {
            s_session = session;
            var keep = new HashSet<string> { s_broadcasterId };
            if (session != null) {
                foreach (var p in session.Participants) {
                    keep.Add(p.Id);
                }
            }
            CatalogStore.RetainChannels(keep);
            Plugin.Log.Info(session == null
                ? "Channel: not in a shared chat session."
                : $"Channel: shared chat with {session.Participants.Count} channels.");
            TTVCoreApi.Channel.RaiseSharedChatChanged(session);

            var ct = s_ct;
            foreach (var id in keep.Where(id => id != s_broadcasterId && !CatalogStore.HasChannel(id))) {
                _ = Task.Run(async () => {
                    try {
                        await CatalogStore.LoadChannelAsync(id, includeCheermotes: false, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                    }
                    catch (Exception e) {
                        Plugin.Log.Error($"Channel: loading a shared chat channel failed: {e}");
                    }
                });
            }
        }
    }
}
