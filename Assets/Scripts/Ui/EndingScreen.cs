using UnityEngine;
using UnityEngine.UI;

namespace CloudEyrie
{
    /// <summary>
    /// 结束页。对应 Cocos 侧 <c>ui/EndingScreen.ts</c>。
    ///
    /// 首页的「退出」不是直接掐进程，而是切到这一页再退 —— 切场景会把首页和关卡的
    /// 资源、正在跑的 AI 与物理全部卸干净，退出动作落在一个空场景里执行，
    /// 不会在游戏逻辑还在跑的时候把进程掐掉。
    ///
    /// 背景、暗角、光点、字体这些和首页一样，都由基类 <see cref="ScreenStage"/> 提供。
    ///
    /// ⚠️ 退出**延迟两秒**（见 <see cref="HoldSeconds"/>），这里是唯一一处退出调用。
    /// 别再往这里加别的退出路径。
    /// </summary>
    public class EndingScreen : ScreenStage
    {
        /// <summary>
        /// 结束页停留多久再退。
        ///
        /// **不能立刻退** —— 立刻退的话这一页一闪就没了，玩家根本看不到「感谢游玩」。
        /// 留两秒也给标题的淡入留出时间。
        /// </summary>
        private const float HoldSeconds = 2f;

        private float _time;
        private RectTransform _contentRoot;
        private CanvasGroup _contentGroup;
        private CanvasGroup _glowGroup;
        private float _holdTimer;

        private void Awake()
        {
            SetupStage();

            // ⚠️ 光点层要挂在 **Canvas 下**，不是组件节点下 ——
            // uGUI 的可见性由 Canvas 决定，挂在 Canvas 外的 UI 节点根本不渲染。
            RectTransform wispLayer = MakeNode(Canvas.transform, "WispLayer");

            BuildContent();
            BuildWisps(wispLayer);
            LoadFont();
        }

        // ============================================================ 搭建

        private void BuildContent()
        {
            // 直接用基类建好的内容层（它在 Canvas 下、且会按屏幕高度等比缩放）
            _contentRoot = ContentRoot;
            _contentGroup = _contentRoot.gameObject.AddComponent<CanvasGroup>();
            _contentGroup.alpha = 0f;

            // 标题背后的柔光，和首页同一套做法
            Image glow = CloneTemplate("WispTemplate", "EndingGlow", _contentRoot);

            if (glow != null)
            {
                Fit(glow.rectTransform, 1350f, 630f);
                glow.rectTransform.anchoredPosition = new Vector2(0f, 114f);
                glow.color = StageColors.Glow;

                _glowGroup = glow.gameObject.AddComponent<CanvasGroup>();
                _glowGroup.alpha = 140f / 255f;
            }

            RectTransform title = MakeNode(_contentRoot, "Title");
            MakeLabel(title, "TitleText", "感 谢 游 玩", 138, StageColors.Title, 0f);
            title.anchoredPosition = new Vector2(0f, 114f);

            RectTransform rule = MakeNode(_contentRoot, "Rule");
            DrawRule(rule, 255f);
            rule.anchoredPosition = new Vector2(0f, -9f);

            RectTransform sub = MakeNode(_contentRoot, "SubTitle");
            MakeLabel(sub, "SubText", "C L O U D   E Y R I E", 36, StageColors.Sub, ThinOutline);
            sub.anchoredPosition = new Vector2(0f, -78f);

            RectTransform hint = MakeNode(_contentRoot, "Hint");
            MakeLabel(hint, "HintText", "可 以 直 接 关 闭", 33, StageColors.Sub, ThinOutline);
            hint.anchoredPosition = new Vector2(0f, -255f);

            Layout();
        }

        // ============================================================ 每帧

        private void Update()
        {
            float dt = Time.deltaTime;
            _time += dt;
            _holdTimer += dt;

            Layout();
            UpdateBackdrop(_time);
            UpdateWisps(dt, _time);

            if (ready && _contentGroup != null && _contentGroup.alpha < 1f)
            {
                _contentGroup.alpha = Mathf.Min(1f, _contentGroup.alpha + dt * (150f / 255f));
            }

            if (_glowGroup != null)
            {
                _glowGroup.alpha = (120f + Mathf.Round(50f * (0.5f + 0.5f * Mathf.Sin(_time * 1.1f)))) / 255f;
            }

            if (_holdTimer >= HoldSeconds)
            {
                _holdTimer = float.NegativeInfinity;   // 只退一次
                QuitGame();
            }
        }

        /// <summary>
        /// 退出游戏。
        ///
        /// ⚠️ <b>编辑器里必须跳过</b>。Cocos 那边这条是血泪教训（在 Game View 的
        /// Electron <c>&lt;webview&gt;</c> 里调 close 会把预览视图真的销毁、编辑器卡死，
        /// 只能强制重启，踩过两次）。Unity 的 <c>Application.Quit()</c> 在编辑器里是空操作，
        /// 但**没有意义**、也不该依赖它 —— 显式挡掉更清楚。
        /// </summary>
        private void QuitGame()
        {
            if (Application.isEditor)
            {
                GameLog.I("[Ending] 编辑器里不退出（Application.Quit 在编辑器内是空操作）");
                return;
            }

            Application.Quit();
        }

        private void Layout()
        {
            LayoutStage();
        }
    }
}
