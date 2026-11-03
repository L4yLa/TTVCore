using System.Collections.Generic;

namespace TTVCore.EventSub
{
    /// <summary>
    /// 直近の message_id を覚えて重複を除く。session_reconnect の切り替え中に、
    /// 旧接続と新接続の両方で同じ通知を受けた場合にも働く。
    /// </summary>
    internal sealed class MessageIdFilter
    {
        private readonly int _capacity;
        private readonly HashSet<string> _set = new HashSet<string>();
        private readonly Queue<string> _order = new Queue<string>();
        private readonly object _gate = new object();

        public MessageIdFilter(int capacity)
        {
            _capacity = capacity;
        }

        /// <summary>初めて見る ID なら true。ID が空なら判定できないため true。</summary>
        public bool TryAdd(string messageId)
        {
            if (string.IsNullOrEmpty(messageId)) {
                return true;
            }
            lock (_gate) {
                if (!_set.Add(messageId)) {
                    return false;
                }
                _order.Enqueue(messageId);
                while (_order.Count > _capacity) {
                    _set.Remove(_order.Dequeue());
                }
                return true;
            }
        }
    }
}
