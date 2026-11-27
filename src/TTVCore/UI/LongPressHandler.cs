using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TTVCore.UI
{
    /// <summary>
    /// ボタンの上に重ねる、長押しの当たり判定。押している間は進み具合（0〜1）を知らせ、Duration 秒押し続けたら Completed を呼ぶ。
    /// 途中で離す、またはポインタが外れたら取り消す。長押しを受け付けないとき（CanStart が false）は、押して離すと Clicked を呼ぶ。
    /// <para>
    /// ゲームの VRInputModule は、押した物がクリックを受け付ける部品（IPointerClickHandler）を持っていると、
    /// 押した同じフレームで離したときの処理まで済ませる（ボタンは押した瞬間に反応する）。そのためボタン自身では長押しを作れない。
    /// この部品はクリックを受け付けないため、本当に離すまで待ってもらえる。押した通知はここで止まるので、下のボタンのクリックは起きない。
    /// </para>
    /// </summary>
    internal sealed class LongPressHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public float Duration = 3f;
        /// <summary>長押しを受け付けるか。false のときは、押して離すと Clicked を呼ぶ。</summary>
        public Func<bool> CanStart;
        /// <summary>進み具合（0〜1）。取り消したときは null。</summary>
        public Action<float?> Progress;
        public Action Completed;
        public Action Clicked;

        private float _start = -1f;
        private bool _pressed;

        /// <summary>ボタンの上に、ボタンと同じ大きさの透明な当たり判定を重ねる。</summary>
        public static LongPressHandler AttachTo(Button button)
        {
            var go = new GameObject("TTVCoreHoldArea", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(button.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            // 一番上に置き、ボタンの中の部品より先にポインタが当たるようにする
            rect.SetAsLastSibling();
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            var image = go.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = true;
            return go.AddComponent<LongPressHandler>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = true;
            if (this.CanStart != null && !this.CanStart()) {
                return;
            }
            _start = Time.unscaledTime;
            this.Progress?.Invoke(0f);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            var wasHolding = _start >= 0f;
            var wasPressed = _pressed;
            _pressed = false;
            this.Cancel();
            // 長押しを受け付けないときの、押して離す操作
            if (wasPressed && !wasHolding && this.CanStart != null && !this.CanStart()) {
                this.Clicked?.Invoke();
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _pressed = false;
            this.Cancel();
        }

        private void OnDisable()
        {
            _pressed = false;
            this.Cancel();
        }

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
            // 完了した押し下げを離しても Clicked は呼ばない（_start を戻し、_pressed も外す）
            _start = -1f;
            _pressed = false;
            this.Completed?.Invoke();
        }
    }
}
