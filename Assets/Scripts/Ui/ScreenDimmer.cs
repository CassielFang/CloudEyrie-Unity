using UnityEngine;
using UnityEngine.UI;

namespace CloudEyrie
{
    /// <summary>
    /// 画面亮度的执行者：盖一层**能盖住一切**的黑罩，按 <see cref="GameSettings.GetBrightness"/>
    /// 调它的不透明度。
    ///
    /// <b>为什么是「压暗」而不是「提亮」</b>：uGUI 上把已经画出来的东西调亮没有正路可走
    /// （要么改材质、要么后期处理，两者都要动渲染管线）。压暗只需要一层黑罩，
    /// 代价是把亮度范围定在「原样 ~ 原样以下」—— 对玩家来说够用，
    /// 而且不会因为没接好后处理而在某些平台上静默失效。
    ///
    /// <b>为什么自带一张 Canvas 而不是用 <see cref="UiDraw.EnsureCanvas"/></b>：
    /// 那张 Canvas 的 sortingOrder 是 100，罩在它下面只能压住 UI、压不住游戏画面里的
    /// SpriteRenderer。要盖住一切就得是一张独立的、层级更高的 Canvas。
    ///
    /// <b>为什么要活过切场景</b>：亮度是全局设置，每个场景都得在。所以整棵 Canvas 走
    /// <c>DontDestroyOnLoad</c>，并在 <c>RuntimeInitializeOnLoadMethod</c> 里建一次。
    ///
    /// 订阅是幂等的：<see cref="EventBus.On{T}"/> 对同一个方法组委托会去重，
    /// 所以就算域重载关掉、<c>Install</c> 被跑第二次也不会重复回调。
    /// </summary>
    public static class ScreenDimmer
    {
        /// <summary>亮度过 600 的 ScreenFade、500 的 StageTransition 之上，罩住它们也是对的。</summary>
        private const int SortingOrder = 1000;

        /// <summary>
        /// 亮度拖到下限 <see cref="GameSettings.MinBrightness"/> 时，这层黑罩的不透明度。
        ///
        /// **不给到 1**：纯黑就等于没画面了，配合亮度的下限（0.35）一起保证「最暗也还看得见」。
        /// </summary>
        private const float MaxDim = 0.65f;

        private static Image _overlay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // 幂等：同一会话里重复订阅会被 EventBus 去重
            EventBus.On<SettingsChangedEvent>(GameEvents.SettingsChanged, OnSettingsChanged);
            Apply();
        }

        /// <summary>按当前设置重画黑罩。设置变了、以及每次安装时调。</summary>
        public static void Apply()
        {
            EnsureOverlay();

            if (_overlay == null)
            {
                return;
            }

            float b = Mathf.Clamp(GameSettings.GetBrightness(),
                                  GameSettings.MinBrightness, 1f);

            // 亮度 1 -> 完全不压暗；亮度 MinBrightness -> MaxDim
            float dim = (1f - b) / (1f - GameSettings.MinBrightness) * MaxDim;
            _overlay.color = new Color(0f, 0f, 0f, Mathf.Clamp01(dim));
        }

        private static void OnSettingsChanged(SettingsChangedEvent e)
        {
            if (e.Visual)
            {
                Apply();
            }
        }

        private static void EnsureOverlay()
        {
            // Unity 的「假 null」：物体被销毁后引用还在，得用 == null 判
            if (_overlay != null)
            {
                return;
            }

            GameObject canvasGo = new GameObject("ScreenDimmerCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Object.DontDestroyOnLoad(canvasGo);

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            // ⚠️ 必须跟着画布缩放，否则宽高比一变就盖不满 —— 这里复用 UI 那套适配参数
            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UiDraw.DesignWidth, UiDraw.DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            GameObject go = new GameObject("Dim", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(canvasGo.transform, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = go.GetComponent<Image>();
            img.sprite = UiDraw.White;
            img.color = new Color(0f, 0f, 0f, 0f);

            // ⚠️ 一定要关掉射线 —— 否则这层罩子会把设置页自己的滑条 / 按钮全部吃掉点击
            img.raycastTarget = false;

            _overlay = img;
        }
    }
}
