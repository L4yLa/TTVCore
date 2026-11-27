using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TTVCore.UI
{
    /// <summary>
    /// ボタンの長押し。押している間は進み具合（0〜1）を知らせ、Duration 秒押し続けたら Completed を呼ぶ。
    /// 途中で離す、またはポインタが外れたら取り消す。完了した押し下げを離したときのクリックは、IsSwallowedClick で見分ける。
    /// </summary>
    internal sealed class LongPressHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public float Duration = 3f;
        /// <summary>長押しを受け付けるか。false のときは普通のボタンとして動く。</summary>
        public Func<bool> CanStart;
        /// <summary>進み具合（0〜1）。取り消したときは null。</summary>
        public Action<float?> Progress;
        public Action Completed;

        private float _start = -1f;
        private bool _completed;
        private int _swallowFrame = -1;

        /// <summary>このフレームのクリックが、長押しを完了した押し下げを離したものか（そのクリックは無視する）。</summary>
        public bool IsSwallowedClick => _swallowFrame == Time.frameCount;

        public void OnPointerDown(PointerEventData eventData)
        {
            _completed = false;
            if (this.CanStart != null && !this.CanStart()) {
                return;
            }
            _start = Time.unscaledTime;
            this.Progress?.Invoke(0f);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_completed) {
                // 離したときのクリックは同じフレームで届く
                _swallowFrame = Time.frameCount;
                _completed = false;
            }
            this.Cancel();
        }

        public void OnPointerExit(PointerEventData eventData) => this.Cancel();

        private void OnDisable() => this.Cancel();

        private void Cancel()
        {
            if (_start < 0f) {
                return;
            }
            _start = -1f;
            this.Progress?.Invoke(null);
        }

        private void Update()
        {
            if (_start < 0f) {
                return;
            }
            var p = (Time.unscaledTime - _start) / Mathf.Max(0.1f, this.Duration);
            if (p < 1f) {
                this.Progress?.Invoke(p);
                return;
            }
            _start = -1f;
            _completed = true;
            this.Completed?.Invoke();
        }
    }
}
