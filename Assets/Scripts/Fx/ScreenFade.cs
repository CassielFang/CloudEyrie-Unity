using UnityEngine;
using UnityEngine.UI;

namespace CloudEyrie
{
    /// <summary>
    /// 全屏黑幕淡入淡出。给过场用（开场、结局）。
    ///
    /// <b>为什么不用 <see cref="StageTransition"/> 的遮罩</b>
    ///
    /// 那个是「转场叠层」：绑着一段全屏动画、有压暗/停顿/收尾的固定流程。
    /// 开场和结局只要一块能按任意时序淡入淡出的黑幕，契约对不上，硬套会把
    /// 两件事搅在一起（而且它的遮罩是私有的、外部改不了）。
    ///
    /// <b>自带 Canvas</b>：过场要在**任何** UI 之上，所以这里自建一个
    /// ScreenSpaceOverlay 画布并把排序值拉高 —— 构造上保证「在最上层」，
    /// 不依赖宿主场景里有没有 Canvas、也不用去抢 siblingIndex。
    ///
    /// ⚠️ 淡入淡出用的是 <c>Time.unscaledDeltaTime</c>：过场期间
    /// <see cref="GameFlow"/> 会把 <c>timeScale</c> 置 0，用缩放时钟的话黑幕永远不动。
    /// </summary>
    public class ScreenFade : MonoBehaviour
    {
        [Tooltip("叠层 Canvas 的排序值。要高于游戏内所有 UI")]
        public int sortingOrder = 600;

        private Image _mask;
        private Canvas _canvas;
        private float _alpha;
        private float _from;
        private float _to;
        private float _elapsed;
        private float _duration;
        private bool _running;
        private System.Action _onDone;

        /// <summary>当前是否正在淡。</summary>
        public bool IsFading
        {
            get { return _running; }
        }

        private void Awake()
        {
            Build();
            SetAlpha(0f);
        }

        private void Build()
        {
            GameObject canvasGo = new GameObject("FadeCanvas",
                typeof(RectTransform), typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;

            GameObject maskGo = new GameObject("Mask", typeof(RectTransform), typeof(Image));
            maskGo.transform.SetParent(canvasGo.transform, false);

            RectTransform rt = maskGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _mask = maskGo.GetComponent<Image>();
            _mask.sprite = UiDraw.White;
            _mask.color = new Color(0f, 0f, 0f, 0f);
            _mask.raycastTarget = false;      // 纯视觉，不挡输入
        }

        /// <summary>立刻设为全黑（过场的「从黑开始」）。</summary>
        public void SetBlack()
        {
            _running = false;
            SetAlpha(1f);
        }

        /// <summary>从当前 alpha 淡到目标 alpha。</summary>
        public void FadeTo(float target, float duration, System.Action onDone = null)
        {
            _from = _alpha;
            _to = Mathf.Clamp01(target);
            _duration = Mathf.Max(0.01f, duration);
            _elapsed = 0f;
            _onDone = onDone;

            // 目标与当前一致就别起动画，但仍要回调 —— 否则调用方会永远等下去
            if (Mathf.Approximately(_from, _to))
            {
                SetAlpha(_to);
                _running = false;
                System.Action done = _onDone;
                _onDone = null;
                done?.Invoke();
                return;
            }

            _running = true;
        }

        private void Update()
        {
            if (!_running)
            {
                return;
            }

            _elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Min(1f, _elapsed / _duration);
            SetAlpha(Mathf.Lerp(_from, _to, t));

            if (t >= 1f)
            {
                _running = false;
                System.Action done = _onDone;
                _onDone = null;
                done?.Invoke();
            }
        }

        private void SetAlpha(float a)
        {
            _alpha = Mathf.Clamp01(a);

            if (_mask != null)
            {
                Color c = _mask.color;
                c.a = _alpha;
                _mask.color = c;
            }

            // 全透明时把画布整个关掉：留着一个全屏 quad 也会吃一次 overdraw
            if (_canvas != null)
            {
                _canvas.enabled = _alpha > 0.001f;
            }
        }
    }
}
