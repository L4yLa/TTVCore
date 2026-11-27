using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.ViewControllers;
using HMUI;
using System.Linq;
using TTVCore.Diagnostics;
using TTVCore.Utilities;

namespace TTVCore.UI
{
    /// <summary>デバッグ機能のテストの画面。設定画面の「テスト」から開く。</summary>
    internal sealed class DebugTestViewController : BSMLResourceViewController
    {
        // 起動している間だけ覚えておく（保存しない）
        private static int s_chatCount = 100;
        private static int s_chatSpeed = 1;

        public override string ResourceName => "TTVCore.UI.DebugTestView.bsml";

        public override string Content => Localization.Bsml(base.Content);

        [UIComponent("notification-list")]
        private CustomListTableData _list = null;

        [UIValue("chat-count")]
        private int ChatCount
        {
            get => s_chatCount;
            set => s_chatCount = value;
        }

        [UIValue("chat-speed")]
        private int ChatSpeed
        {
            get => s_chatSpeed;
            set => s_chatSpeed = value;
        }

        [UIAction("format-speed")]
        private string FormatSpeed(int value) =>
            Localization.T($"x{value} ({value * (int)DebugEvents.BaseMessagesPerMinute}/min)", $"x{value} ({value * (int)DebugEvents.BaseMessagesPerMinute} 件/分)");

        [UIValue("chat-button-text")]
        private string ChatButtonText => DebugEvents.ChatRunning ? Localization.T("Stop", "止める") : Localization.T("Start", "流す");

        [UIValue("chat-status")]
        private string ChatStatus => DebugEvents.ChatRunning
            ? Localization.T($"Sending {DebugEvents.ChatSent} / {DebugEvents.ChatTotal}", $"流しています {DebugEvents.ChatSent} / {DebugEvents.ChatTotal}")
            : DebugEvents.ChatTotal > 0
                ? Localization.T($"Sent {DebugEvents.ChatSent} / {DebugEvents.ChatTotal}", $"流しました {DebugEvents.ChatSent} / {DebugEvents.ChatTotal}")
                : "";

        [UIAction("#post-parse")]
        private void PostParse()
        {
            if (_list == null) {
                return;
            }
            _list.Data = DebugEvents.NotificationTests.Select(t => new CustomListTableData.CustomCellInfo(t.Label)).ToList();
            _list.TableView.ReloadData();
        }

        [UIAction("notification-selected")]
        private void NotificationSelected(TableView table, int index)
        {
            var tests = DebugEvents.NotificationTests;
            if (index >= 0 && index < tests.Count) {
                DebugEvents.RunNotification(tests[index].Id);
            }
            // 同じものを続けて選べるよう、選んだ状態を外す
            table.ClearSelection();
        }

        [UIAction("chat-toggle")]
        private void ChatToggle()
        {
            if (DebugEvents.ChatRunning) {
                DebugEvents.StopChat();
            }
            else {
                DebugEvents.StartChat(s_chatCount, s_chatSpeed);
            }
            this.Refresh();
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            DebugEvents.ChatProgressChanged += this.Refresh;
            this.Refresh();
        }

        protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
        {
            DebugEvents.ChatProgressChanged -= this.Refresh;
            base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);
        }

        private void Refresh()
        {
            this.NotifyPropertyChanged(nameof(this.ChatButtonText));
            this.NotifyPropertyChanged(nameof(this.ChatStatus));
        }
    }
}
