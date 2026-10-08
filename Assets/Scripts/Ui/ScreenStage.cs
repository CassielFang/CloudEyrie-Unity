using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CloudEyrie
{
    /// <summary>
    /// 首页（<c>TitleScreen</c>）和结束页（<c>EndingScreen</c>）共用的「舞台」。
    /// 对应 Cocos 侧 <c>ui/ScreenStage.ts</c>。
    ///
    /// 两页都是**一张满屏长卷 + 暗角 + 居中文字**，差别只在文字内容和交互，
    /// 所以把背景铺满、统一缩放、字体加载、建文本这些重复部分抽到这里，子类只写自己那点内容。
    ///
    /// <b>基类不碰引擎生命周期</b>（不定义 <c>Awake</c> / <c>Update</c>）——
    /// 子类要覆盖这两个方法，基类再定义一遍的话子类就得记得调 <c>base.Awake()</c>，
    /// 漏一次就静默少跑一半初始化。改成让子类在自己的 <c>Awake</c> 里显式调
    /// <see cref="SetupStage"/> / <see cref="LoadFont"/>。
    ///
    /// <b>与 Cocos 的一处结构差异</b>：那边背景与暗角是**场景里的节点**
    /// （贴图不在 resources 下，只能摆好再引用）。Unity 这边贴图已挪进
    /// <c>Resources/ui_title/</c>，所以这里**自己把它们建出来**，不必依赖场景摆位。
    /// </summary>
    public abstract class ScreenStage : MonoBehaviour
    {
        /// <summary>灵炁光点：缓慢上浮、出屏后从底部回绕的小光斑。</summary>
        private class Wisp
        {
            public RectTransform Node;
            public Image Image;
            public float X;
            public float Y;
            public float Vy;
            public float Amp;
            public float Phase;
            public float Sway;
            public float BaseAlpha;
        }

        /// <summary>光点数量。两页共用。</summary>
        public const int WispCount = 18;

        /// <summary>
        /// 背景图的像素尺寸 —— cover 缩放按它算。
        ///
        /// 2026-10-07 换美术：原来是程序化生成的长卷（2200×1000），
        /// 现在是美术给的整屏图 <c>ui/title_bg</c>（1920×1080）。
        /// **换图必须同步改这两个数**，否则 cover 比例会算错、边上露空。
        /// </summary>
        public const float CoverW = 1920f;
        public const float CoverH = 1080f;

        /// <summary>
        /// 背景左右摆动的最大幅度（画布像素）。<see cref="LayoutStage"/> 的 cover 缩放要留出这个余量。
        ///
        /// ⚠️ <b>不留余量就会露黑边</b>：cover 只保证「刚好铺满可视区」，
        /// 一旦平移，对侧立刻空出一条 —— 表现为镜头晃动时四周一圈黑框（实测反馈）。
        /// 所以基础缩放要按「可视区 + 2×摆幅」算，任何时刻都剩至少一个摆幅的余量。
        /// </summary>
        protected const float BackdropSwayX = 90f;

        /// <summary>背景上下摆动的最大幅度。理由同 <see cref="BackdropSwayX"/>。</summary>
        protected const float BackdropSwayY = 27f;

        /// <summary>
        /// 暗角的不透明度。
        ///
        /// 设计稿的背景是**一张成品图**，上面并没有暗角；这里只保留很淡的一点，
        /// 用来压住四角、让居中的文字更容易读。
        ///
        /// 上一版是按暗角原图全量盖的（四角 alpha 151 ≈ 59%），叠在当时那层暮色乘算上，
        /// 整页比游戏里暗一大截。想更贴近设计稿就把这个值往 0 调。
        /// </summary>
        protected const float VignetteAlpha = 0.4f;

        /// <summary>
        /// 子面板里的「返回 / 取消」键。
        ///
        /// ⚠️ <b>2026-10-07 起是 ESC，不再是 E。</b>
        ///
        /// 原来用 E，理由写的是「全屏时 ESC 归操作系统 / 浏览器管（Web 上是退出全屏）」——
        /// 那条理由只对 **Web 构建**成立。按需求改成 ESC（玩家对「ESC 退一层」的
        /// 肌肉记忆更强），E 不再是任何特殊键、可以拿去绑动作。
        /// 真要出 Web 构建，得再权衡一次：那边按 ESC 会先掉出全屏。
        ///
        /// 类型用 <see cref="Key"/>（Input System）而不是 <c>KeyCode</c> ——
        /// 与整个输入层保持一致。它只有这一份定义：面板文案、<see cref="TitleScreen"/>（关面板）、
        /// <c>SettingsPanel</c>（取消改键）三处都读它，各写一份迟早漂移。
        /// </summary>
        public const Key PanelBackKey = Key.Escape;

        /// <summary>
        /// 这个键算不算「返回」。**只有这一处判定**，关面板 / 取消改键都走它 ——
        /// 以后要再加键也是加在这里，别去各调用点写 `key == Key.Xxx`。
        /// </summary>
        public static bool IsBackKey(Key key)
        {
            return key == PanelBackKey;
        }

        /// <summary>
        /// 小字（30~40 号）用的描边宽度。
        ///
        /// ⚠️ 原来是 <c>3</c>，实测糊 —— <see cref="Outline"/> 是把字形往四个方向各复制
        /// 一份再偏移，所以 <c>3</c> 是**每边 3px 的实边**；对 33 号字来说那个厚度
        /// 已经和笔画本身差不多粗了，远看就是一团发虚的墨。
        /// 大字号（标题那种 130+）不受影响，继续用 0 或各自的取值。
        /// </summary>
        public const float ThinOutline = 1.5f;

        /// <summary>
        /// 背景的乘算色。**现在是白的 —— 也就是原样出图，不做任何调色。**
        ///
        /// 以前是暮蓝 <c>(132,158,180)</c>：那会儿背景还是程序化生成的白纸长卷，需要压成暮色。
        /// 2026-10-07 换成美术给的成品图（<c>ui/title_bg</c>）之后，设计稿里这张图
        /// **本来就是没有加滤镜的**，再乘一层暮色就比游戏里暗一大截
        /// （反馈原话「太暗了，和后面进入游戏的不太搭」）。
        ///
        /// 要调明暗只动这一个值：越接近白色越亮，白 = 与设计稿一致。
        /// </summary>
        protected Color bgTint = Color.white;

        /// <summary>字体到位后才置 true。字没到就开动画会先闪一帧系统字体的错字形。</summary>
        protected bool ready;

        protected Canvas Canvas { get; private set; }

        /// <summary>内容层根节点 —— 子类的 UI 都往它下面挂。</summary>
        protected RectTransform ContentRoot { get; private set; }

        private RectTransform _bgNode;
        private RectTransform _vignetteNode;
        private float _bgBaseScale = 1f;
        private float _lastLayoutW;
        private float _lastLayoutH;
        private readonly List<Wisp> _wisps = new List<Wisp>();

        // ============================================================ 初始化

        /// <summary>建画布、背景、暗角、内容层。子类在自己的 <c>Awake</c> 里第一个调它。</summary>
        protected void SetupStage()
        {
            Canvas = UiDraw.EnsureCanvas(transform);
            Transform root = Canvas.transform;

            // ---- 背景（美术组 2026-10-07 新稿：整屏一张 1920×1080）----
            Sprite bgSprite = Resources.Load<Sprite>("ui/title_bg");

            if (bgSprite == null)
            {
                Debug.LogWarning("[ScreenStage] 读不到 ui/title_bg");
            }

            Image bg = UiDraw.MakeImage(root, "BgScroll", bgSprite, bgTint);
            _bgNode = bg.rectTransform;

            // ---- 暗角 ----
            // 只上一层淡的（强度见 VignetteAlpha）。设计稿里没有暗角，全量盖会明显偏暗。
            Sprite vigSprite = Resources.Load<Sprite>("ui_title/title_vignette");
            Image vig = UiDraw.MakeImage(root, "Vignette", vigSprite,
                new Color(1f, 1f, 1f, VignetteAlpha));
            _vignetteNode = vig.rectTransform;

            // ---- 内容层（子类的 UI 挂这里，按屏幕高度等比缩放）----
            ContentRoot = UiDraw.MakeNode(root, "Content");
            UiDraw.Stretch(ContentRoot);
        }

        /// <summary>取游戏字体。失败就保持系统字体 —— 中文仍能显示，只是字形不对。</summary>
        protected void LoadFont()
        {
            // UiDraw.GameFont 自己带缓存与告警，这里只是把「就绪」状态对齐 Cocos 的语义
            Font f = UiDraw.GameFont;

            if (f == null)
            {
                Debug.LogWarning("[ScreenStage] resources/font 加载失败，回退系统字体");
            }

            ready = true;
        }

        // ============================================================ 面板依赖注入

        /// <summary>给子面板内容类用的两个能力（见 <see cref="PanelDeps"/>）。</summary>
        protected PanelDeps Deps()
        {
            return new StagePanelDeps(this);
        }

        private class StagePanelDeps : PanelDeps
        {
            private readonly ScreenStage _stage;

            public StagePanelDeps(ScreenStage stage)
            {
                _stage = stage;
            }

            public Text MakeLabel(Transform parent, string name, string text, int size,
                                  Color color, float outline = 2f)
            {
                return UiDraw.MakeText(parent, name, text, size, color, outline);
            }

            public Image CloneTemplate(string templateName, string name, Transform parent)
            {
                return _stage.CloneTemplate(templateName, name, parent);
            }

            public void ClosePanel()
            {
                _stage.RequestClosePanel();
            }
        }

        /// <summary>
        /// 面板内容请求关闭当前面板。基类默认什么都不做 —— 只有
        /// <see cref="TitleScreen"/> 有子面板，由它覆盖。
        ///
        /// 做成虚方法而不是让内容类直接调 <c>TitleSubPanels.Close()</c>：
        /// 那样 <see cref="TitleScreen"/> 的阶段状态（<c>_stage</c>）会停在 Panel 上，
        /// 面板关掉了但首页还在「面板阶段」，按键和点击全都不响应。
        /// </summary>
        protected virtual void RequestClosePanel()
        {
        }

        /// <summary>
        /// 模板名 → <c>Resources/ui_title/</c> 下的贴图名。
        ///
        /// Cocos 那边这些是场景里的隐藏模板节点；Unity 这边贴图进了 Resources，
        /// 所以这里只是一张名字对照表。
        /// </summary>
        private static readonly Dictionary<string, string> TemplatePaths =
            new Dictionary<string, string>
            {
                { "WispTemplate", "ui_title/title_glow" },
                { "PanelPaperTemplate", "ui_title/panel_paper" },
                { "PanelScrollTemplate", "ui_title/panel_scroll" },
                { "PanelSoftTemplate", "ui_title/title_panel" },
            };

        /// <summary>按模板名取一张贴图并建节点。</summary>
        protected Image CloneTemplate(string templateName, string name, Transform parent)
        {
            if (!TemplatePaths.TryGetValue(templateName, out string path))
            {
                Debug.LogWarning($"[ScreenStage] 没有名为 {templateName} 的模板");
                return null;
            }

            Sprite sprite = Resources.Load<Sprite>(path);

            if (sprite == null)
            {
                Debug.LogWarning($"[ScreenStage] 模板贴图读不到：{path}");
            }

            return UiDraw.MakeImage(parent, name, sprite, Color.white);
        }

        /// <summary>建一个居中文本（子类自己用；面板内容类走 <see cref="Deps"/>）。</summary>
        protected Text MakeLabel(Transform parent, string name, string text, int size,
                                 Color color, float outline = 2f)
        {
            return UiDraw.MakeText(parent, name, text, size, color, outline);
        }

        /// <summary>建一个空 UI 节点。</summary>
        protected RectTransform MakeNode(Transform parent, string name)
        {
            return UiDraw.MakeNode(parent, name);
        }

        // ============================================================ 绘制

        /// <summary>
        /// 一条细横线 + 中间一枚菱形（标题与副标题之间的分隔）。
        /// Cocos 用 <c>Graphics</c> 画；Unity 这边用两个色块 + 一张菱形小图拼。
        /// </summary>
        protected void DrawRule(RectTransform node, float halfLen)
        {
            const float gap = 36f;

            Image left = UiDraw.MakeRect(node, "RuleL", StageColors.Rule, halfLen - gap, 3f);
            left.rectTransform.anchoredPosition = new Vector2(-(gap + (halfLen - gap) * 0.5f), 0f);

            Image right = UiDraw.MakeRect(node, "RuleR", StageColors.Rule, halfLen - gap, 3f);
            right.rectTransform.anchoredPosition = new Vector2(gap + (halfLen - gap) * 0.5f, 0f);

            Image dia = UiDraw.MakeImage(node, "Diamond", UiDraw.Diamond, StageColors.Diamond);
            dia.rectTransform.sizeDelta = new Vector2(28.5f, 28.5f);
            dia.rectTransform.anchoredPosition = Vector2.zero;
        }

        // ============================================================ 灵炁光点

        protected void BuildWisps(RectTransform layer)
        {
            if (layer == null)
            {
                return;
            }

            for (int i = 0; i < WispCount; i++)
            {
                Image node = CloneTemplate("WispTemplate", $"Wisp{i}", layer);

                if (node == null)
                {
                    return;
                }

                float size = 6f + Random.value * 18f;
                Fit(node.rectTransform, size, size);

                node.color = new Color(
                    StageColors.Glow.r, StageColors.Glow.g, StageColors.Glow.b,
                    (120f + Mathf.Floor(Random.value * 110f)) / 255f);

                _wisps.Add(new Wisp
                {
                    Node = node.rectTransform,
                    Image = node,
                    X = (Random.value - 0.5f) * 2100f,
                    Y = (Random.value - 0.5f) * 1350f,
                    Vy = 12f + Random.value * 30f,
                    Amp = 15f + Random.value * 45f,
                    Phase = Random.value * Mathf.PI * 2f,
                    Sway = 0.25f + Random.value * 0.5f,
                    BaseAlpha = 0.35f + Random.value * 0.5f,
                });
            }
        }

        protected void UpdateWisps(float dt, float t)
        {
            float half = VisibleSize().y * 0.5f + 60f;

            foreach (Wisp w in _wisps)
            {
                w.Y += w.Vy * dt;

                if (w.Y > half)
                {
                    w.Y = -half;
                    w.X = (Random.value - 0.5f) * 2100f;
                }

                w.Node.anchoredPosition = new Vector2(
                    w.X + Mathf.Sin(t * w.Sway + w.Phase) * w.Amp, w.Y);

                // 上浮时淡出、回到底部再淡入，避免看到「瞬间消失」
                float edge = Mathf.Min(1f, (half - w.Y) / 330f, (w.Y + half) / 330f);
                Color c = w.Image.color;
                c.a = w.BaseAlpha * Mathf.Max(0f, edge);
                w.Image.color = c;
            }
        }

        // ============================================================ 布局

        /// <summary>每帧调。背景铺满、暗角跟着可视区、内容按高度等比缩放。</summary>
        protected void LayoutStage()
        {
            Vector2 vis = VisibleSize();

            // 尺寸没变就别动 —— 改 sizeDelta 会标脏重排
            if (!Mathf.Approximately(vis.x, _lastLayoutW) || !Mathf.Approximately(vis.y, _lastLayoutH))
            {
                _lastLayoutW = vis.x;
                _lastLayoutH = vis.y;

                // 暗角必须跟着可视区走：非 16:9 时固定 1280×720 会盖不住或压到画面外
                if (_vignetteNode != null)
                {
                    _vignetteNode.sizeDelta = vis;
                }
            }

            if (ContentRoot != null)
            {
                float s = Mathf.Min(1f, vis.y / UiDraw.DesignHeight);
                ContentRoot.localScale = new Vector3(s, s, 1f);
            }

            // 背景 cover：取两个方向缩放比的较大者，超出画布的部分落在屏幕外。
            //
            // ⚠️ 分母是「可视区 + 2×摆幅」而不是可视区本身 —— 见 BackdropSwayX 的说明：
            // 只按可视区算的话，UpdateBackdrop 的平移会从对侧露出一条底（黑边）。
            if (_bgNode != null)
            {
                _bgBaseScale = Mathf.Max(
                    (vis.x + BackdropSwayX * 2f) / CoverW,
                    (vis.y + BackdropSwayY * 2f) / CoverH);
            }
        }

        /// <summary>
        /// 极缓慢的推近 + 平移，让长卷「活」起来；幅度小到不喧宾夺主。
        ///
        /// 摆幅直接取自 <see cref="BackdropSwayX"/> / <see cref="BackdropSwayY"/> ——
        /// 它们是 <see cref="LayoutStage"/> 算留白余量的依据，两边写死的数一旦分家，
        /// 要么白留一段余量、要么又露黑边，所以只此一份。
        /// </summary>
        protected void UpdateBackdrop(float t)
        {
            if (_bgNode == null)
            {
                return;
            }

            float zoom = 1f + 0.03f * (0.5f - 0.5f * Mathf.Cos(t * 0.08f));
            float s = _bgBaseScale * zoom;
            _bgNode.localScale = new Vector3(s, s, 1f);
            _bgNode.anchoredPosition = new Vector2(
                Mathf.Sin(t * 0.13f) * BackdropSwayX,
                Mathf.Sin(t * 0.09f) * BackdropSwayY);
        }

        /// <summary>
        /// 把节点缩放成 w×h 那么大。
        ///
        /// <b>必须按节点当前的 sizeDelta 反算，不能写死贴图边长</b> ——
        /// <c>Image</c> 一旦被赋 sprite 就会把尺寸设成贴图尺寸（图片导入时还会去掉透明边，
        /// 实际尺寸常和源文件不一样），写死就会算错。
        /// </summary>
        protected void Fit(RectTransform rt, float w, float h)
        {
            float bw = rt.sizeDelta.x > 0f ? rt.sizeDelta.x : w;
            float bh = rt.sizeDelta.y > 0f ? rt.sizeDelta.y : h;
            rt.localScale = new Vector3(w / bw, h / bh, 1f);
        }

        protected Vector2 VisibleSize()
        {
            RectTransform rt = Canvas != null ? Canvas.GetComponent<RectTransform>() : null;

            if (rt != null && rt.rect.width > 0f && rt.rect.height > 0f)
            {
                return new Vector2(rt.rect.width, rt.rect.height);
            }

            return new Vector2(Screen.width, Screen.height);
        }
    }
}
