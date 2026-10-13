using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CloudEyrie
{
    /// <summary>首页的三个阶段。</summary>
    public enum TitleStage
    {
        /// <summary>标题淡入，「按任意键继续」呼吸闪烁</summary>
        Prompt,

        /// <summary>菜单项错峰淡入，上下选择、确认</summary>
        Menu,

        /// <summary>子面板打开，菜单整体压暗</summary>
        Panel,
    }

    /// <summary>
    /// 游戏首页。对应 Cocos 侧 <c>ui/TitleScreen.ts</c>。
    ///
    /// 场景里只放「背景 / 暗角 / 这个组件」，标题、分隔线、菜单、子面板全部在
    /// <c>Awake</c> 里用代码搭 —— 沿用项目里定下的约定（UI 用代码搭，
    /// 不在 Inspector 连引用），改版式只要动文件顶部的常量。
    ///
    /// <b>输入分两条路</b>（与 Cocos 一致）：
    /// <list type="bullet">
    ///   <item><b>Prompt 阶段</b>的「任意键」—— 读**原始按键**（它是系统级语义，
    ///     没有重映射的必要）。</item>
    ///   <item><b>菜单导航</b> —— 走动作层（<see cref="InputSystem"/>），
    ///     这样键位重映射对菜单同样生效。菜单里 <c>MenuConfirm</c> 默认绑了
    ///     回车 / 空格 / J 三个键。</item>
    /// </list>
    /// 子面板的返回键是 **ESC**（<see cref="ScreenStage.PanelBackKey"/>，2026-10-07 由 E 改来）。
    /// E 已交还给普通按键，可以绑动作。
    /// </summary>
    public class TitleScreen : ScreenStage
    {
        // ---- 版式（单位 = 1920×1080 设计稿像素；原点在画布中心，y 向上）----
        //
        // ⚠️ 这些数**不是肉眼量的**，是 tools/ui_slice_locator.py 在设计稿里
        // 逐张匹配切图算出来的（设计稿 1920×1080 与画布参考同一套单位，1:1 可直接用）。
        // 脚本顺带输出「设计稿坐标 → 这里的画布坐标」的换算表，改版式先重跑它。
        //
        // 取整说明：切图宽高常是奇数（菜单按钮 307、徽记 549），中心必然落在 *.5 上。
        // 脚本统一按 floor(v+0.5) 收，所以间距是齐的（菜单实测 99 一整排）。
        // 若用四舍六入五成双，这几个中心会一半上、一半下，间距就散了。

        /// <summary>中央徽记：中心位置与尺寸（切图 549×776，含上下拖出的笔锋）</summary>
        private const float EmblemY = 123f;
        private const float EmblemW = 549f;
        private const float EmblemH = 776f;

        /// <summary>两侧云纹：距画布中心的水平距离与尺寸</summary>
        private const float FlourishY = 173f;
        private const float FlourishX = 623f;
        private const float FlourishW = 594f;
        private const float FlourishH = 124f;

        /// <summary>菜单：首项中心、项间距、单项尺寸（= 切图原生 307×89）</summary>
        private const float MenuTopY = -145f;
        private const float MenuPitch = 99f;
        private const float MenuItemW = 307f;
        private const float MenuItemH = 89f;

        /// <summary>左下角信息按钮（切图原生 80×80）</summary>
        private const float InfoX = -896f;
        private const float InfoY = -476f;
        private const float InfoSize = 80f;

        /// <summary>
        /// 菜单项常态 / 选中态的**不透明度**。
        ///
        /// ⚠️ 美术只给了**一张**常态图，没有选中态切图。所以选中态靠
        /// 「不透明度拉满 + 略微放大 + 背后辉光」表达 —— 与旧版同一套做法。
        /// </summary>
        private const float ItemAlpha = 0.72f;
        private const float ItemSelectedAlpha = 1f;

        /// <summary>
        /// 菜单四项的文案。
        ///
        /// ⚠️ <b>原先用的是美术切图</b>（<c>ui/title_btn_*</c>，文字是烧进图里的）。
        /// 2026-10-07 改成**真字体**了 —— 切图在非 1080 的窗口尺寸下会被
        /// <see cref="CanvasScaler"/> 乘一个小数比例重采样（比如 21:9 时画布高只有 823，
        /// <c>ContentRoot</c> 的缩放就是 0.762），字一下子糊掉；而 Text 是动态字体，
        /// 按最终尺寸重新光栅化，任何分辨率都清楚。
        /// 切图文件仍留在 <c>Resources/ui</c> 里没删（美术可能还要用）。
        ///
        /// 中间的**是全角空格**（U+3000），照设计稿的字距 —— 美术字的两个字之间是拉开的。
        ///
        /// ⚠️ 顺序必须与 <see cref="MenuKinds"/> 一一对应。
        /// 设计稿的菜单是**启境 / 设置 / 成就 / 退出**四项 —— 没有「制作组」，
        /// 那个挪到了左下角的信息按钮（<c>title_btn_info</c>）。
        /// </summary>
        private static readonly string[] MenuLabels =
        {
            "启　境",
            "设　置",
            "成　就",
            "退　出",
        };

        /// <summary>
        /// 菜单文字的字号。
        ///
        /// 对齐的是原来那张切图：307×89 的图里，两个字的墨迹高 75、宽 261（量过）。
        /// ⚠️ 别按「字面高 ≈ 0.85 × 字号」去算（那是黑体一类的字面率）——
        /// 这份毛笔行楷的字面几乎占满 em，84 号实测墨迹就有 90 高，比切图还大一圈、
        /// 行距被挤没了（相邻两项的墨迹几乎贴上）。76 号才对得上。
        /// </summary>
        private const int MenuFontSize = 76;

        /// <summary>每个菜单项对应的子面板；<c>null</c> 表示不是面板（启境 / 退出）。</summary>
        private static readonly SubPanelKind?[] MenuKinds =
            { null, SubPanelKind.Settings, SubPanelKind.Achievements, null };

        private class MenuItem
        {
            public RectTransform Node;

            /// <summary>菜单文字。选中态靠它的 alpha 拉开（见 <see cref="ItemAlpha"/>）</summary>
            public Text Art;

            public CanvasGroup Glow;
            public RectTransform ArtNode;
        }

        private TitleStage _stage = TitleStage.Prompt;

        /// <summary>进入当前阶段后经过的秒数，用于错峰淡入</summary>
        private float _stageTime;
        private float _time;

        private RectTransform _contentRoot;
        private CanvasGroup _contentGroup;
        private RectTransform _titleNode;
        private CanvasGroup _titleGlowGroup;
        private RectTransform _promptNode;
        private CanvasGroup _promptGroup;

        /// <summary>左下角信息按钮。与菜单一起在 Prompt 阶段隐藏</summary>
        private RectTransform _infoNode;

        private readonly System.Collections.Generic.List<MenuItem> _menuItems =
            new System.Collections.Generic.List<MenuItem>();

        private int _selected;

        /// <summary>
        /// 刚进菜单的那一帧不吃菜单按键。
        ///
        /// 必须要有：Prompt 阶段的「任意键」是**当帧**处理的，而动作层的
        /// <c>WasPressed</c> 在同一帧也读得到。玩家按回车进菜单时，这一按会同时把
        /// <c>MenuConfirm</c> 置位 —— 若不挡掉，同一帧就会把第一项也确认了，
        /// 表现为「一按回车直接开始游戏、菜单一闪而过」。
        /// </summary>
        private bool _swallowMenuInput;

        private TitleSubPanels _panels;

        // ============================================================ 生命周期

        /// <summary>
        /// 这次进首页时**跳过「按任意键继续」直接落在菜单**。
        ///
        /// 由 <see cref="GlobalBackKey"/> 在「关卡里按 ESC 回首页」之前置位 ——
        /// 那条路是玩家**主动要回菜单**的，再让他按一次任意键纯属多一道手
        /// （实测反馈「希望游戏中按 esc 进入的也是菜单页面」）。
        ///
        /// ⚠️ 用 static 而不是 PlayerPrefs：它是**一次性的场景切换意图**，
        /// 不该落盘（落盘的话下次冷启动也会跳过开场屏）。同一进程内跨场景足够，
        /// 而且读完就清，不存在「忘复位」的问题。
        /// </summary>
        public static bool StartAtMenu;

        private void Awake()
        {
            SetupStage();

            RectTransform wispLayer = MakeNode(Canvas.transform, "WispLayer");
            BuildContent();
            BuildWisps(wispLayer);
            LoadFont();

            // 菜单项在 BuildContent 里才建出来，所以这一步必须在它之后。
            if (StartAtMenu)
            {
                StartAtMenu = false;
                EnterMenu();

                // ⚠️ EnterMenu 里会把 _swallowMenuInput 置位，那是为了吃掉
                // 「按任意键」的那一下（它同时会把 MenuConfirm 置位，不挡就会
                // 立刻确认掉选中项）。这条路上**没有那一次按键** ——
                // ESC 不在 MenuConfirm 的绑定里 —— 所以不该吞，否则玩家回来后的
                // 第一次按方向键会没反应。
                _swallowMenuInput = false;
            }

            _panels = new TitleSubPanels(Canvas.transform, Deps());

            // 首页就起播 BGM。**曲目与关卡里是同一条**（探索曲），而 AudioManager
            // 对「同一首正在播」是空操作 —— 所以进关卡时不会重头放，是接着放下去的。
            GameAudio.PlayBgm(BgmTrack.Explore);
        }

        private void OnDestroy()
        {
            // 子面板的内容对象往 EventBus 上挂过订阅，而 EventBus 活得比场景久 ——
            // 不摘的话切场景后设置一变还会回调到已销毁的节点上。
            // 顺带兜住「面板还开着就切场景」时没收掉的改键捕获模式。
            _panels?.Dispose();
        }

        // ============================================================ 搭建

        private void BuildContent()
        {
            _contentRoot = ContentRoot;
            _contentGroup = _contentRoot.gameObject.AddComponent<CanvasGroup>();
            _contentGroup.alpha = 0f;

            // ---- 中央徽记（美术字「云岫」+ 笔锋圆环，切图整张）----
            //
            // 换掉了旧版的「标题 Text + 分隔线 + 英文副标题」三件套 ——
            // 设计稿里这三样都没有，标题是**画在徽记里的**。
            _titleNode = MakeNode(_contentRoot, "Emblem");
            _titleNode.sizeDelta = new Vector2(EmblemW, EmblemH);
            _titleNode.anchoredPosition = new Vector2(0f, EmblemY);

            Image emblem = UiDraw.MakeImage(_titleNode, "Art",
                Resources.Load<Sprite>("ui/title_emblem"), Color.white);

            if (emblem != null)
            {
                emblem.rectTransform.sizeDelta = new Vector2(EmblemW, EmblemH);

                // 徽记背后的柔光（沿用旧版的光点模板做法，让画面「呼吸」）
                Image glow = CloneTemplate("WispTemplate", "EmblemGlow", _titleNode);

                if (glow != null)
                {
                    Fit(glow.rectTransform, 1350f, 690f);
                    glow.color = StageColors.Glow;
                    glow.rectTransform.SetSiblingIndex(0);      // 压到徽记之下

                    _titleGlowGroup = glow.gameObject.AddComponent<CanvasGroup>();
                    _titleGlowGroup.alpha = 130f / 255f;
                }
            }
            else
            {
                Debug.LogWarning("[TitleScreen] 读不到 ui/title_emblem");
            }

            // ---- 两侧云纹 ----
            MakeFlourish("ui/title_flourish_l", -FlourishX);
            MakeFlourish("ui/title_flourish_r", FlourishX);

            // ---- 菜单 ----
            //
            // ⚠️ **注册写在 BuildMenuItem 里面**，不是在这里 `_menuItems.Add(BuildMenuItem(...))`。
            //
            // 排查过一例：节点树里 Menu0..Menu3 都建出来了，`_menuItems` 却是空的，
            // 于是 UpdateContent 的淡入循环跑不到、四项菜单永远停在透明度 0（看不见）。
            // 完全查不出「建了节点却没进列表」的原因，所以改成**创建与注册同一个地方** ——
            // 这样两者在结构上不可能再分叉，不管当初那个原因是什么。
            for (int i = 0; i < MenuLabels.Length; i++)
            {
                BuildMenuItem(_contentRoot, i);
            }

            // ---- 左下角信息按钮（设计稿里「制作组」从菜单挪到了这里）----
            BuildInfoButton();

            // 右下角原来有一行「WS 切换　Enter 确定」的操作提示，已去掉 ——
            // 菜单本身用鼠标就能点、用方向键也能走，这行字既没给出新信息、
            // 又和画面右下角的留白抢注意力（反馈原话「好奇怪」）。
            // 注：设计稿右下角画了这行字，属于**有意偏离设计稿**的一处。

            // ---- 按任意键继续 ----
            _promptNode = MakeNode(_contentRoot, "Prompt");
            MakeLabel(_promptNode, "PromptText", "按 任 意 键 继 续", 34, StageColors.Sub, ThinOutline);
            _promptGroup = _promptNode.gameObject.AddComponent<CanvasGroup>();

            Layout();
        }

        private MenuItem BuildMenuItem(RectTransform parent, int index)
        {
            RectTransform node = MakeNode(parent, $"Menu{index}");
            node.sizeDelta = new Vector2(MenuItemW, MenuItemH);
            node.anchoredPosition = new Vector2(0f, MenuTopY - index * MenuPitch);

            // ⚠️ uGUI 的指针事件要求节点上有一个能命中射线的 Graphic。
            // 用一个全透明的 Image 当命中区（不能只靠 Text —— Text 的 raycastTarget
            // 关掉后就没人接事件了）。
            Image hit = node.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;

            // 选中时背后透出的辉光
            Image glow = CloneTemplate("WispTemplate", "ItemGlow", node);
            CanvasGroup glowGroup = null;

            if (glow != null)
            {
                Fit(glow.rectTransform, 480f, 144f);
                glow.color = StageColors.Glow;
                glowGroup = glow.gameObject.AddComponent<CanvasGroup>();
                glowGroup.alpha = 0f;
            }

            // 文字：用游戏字体渲染，不用烧进文字的切图（理由见 MenuLabels 的说明）
            RectTransform artNode = MakeNode(node, "Art");
            Text art = MakeLabel(artNode, "Label", MenuLabels[index], MenuFontSize,
                StageColors.Title, 2f);

            if (art != null)
            {
                art.rectTransform.sizeDelta = new Vector2(MenuItemW, MenuItemH);

                // 常态压到半透明 —— 选中靠「拉满 + 放大 + 辉光」拉开对比（美术只给了常态图）
                Color c = art.color;
                c.a = ItemAlpha;
                art.color = c;
            }

            CanvasGroup artGroup = artNode.gameObject.AddComponent<CanvasGroup>();
            artGroup.alpha = 0f;

            MenuItem item = new MenuItem
            {
                Node = node,
                Art = art,
                Glow = glowGroup,
                ArtNode = artNode,
            };

            // 鼠标：悬停即选中，点击即确认
            EventTrigger trigger = node.gameObject.AddComponent<EventTrigger>();

            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (_stage == TitleStage.Menu)
                {
                    Select(index);
                }
            });
            trigger.triggers.Add(enter);

            EventTrigger.Entry click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(_ =>
            {
                if (_stage == TitleStage.Menu)
                {
                    Select(index);
                    Confirm();
                }
            });
            trigger.triggers.Add(click);

            // ⚠️ Prompt 阶段菜单是隐形的，**节点也必须失活**，光靠透明度 0 不够：
            // 活着的 Image 会把这一片的点击吞掉，表现为「按任意键继续」时
            // 点到**中间那条菜单带**毫无反应、其他地方正常。
            // （Cocos 那边是同一个道理 —— UI 派发器一旦被某节点处理就 break 掉整条链。）
            node.gameObject.SetActive(false);

            // 创建即注册 —— 见 BuildContent 里那句注释
            _menuItems.Add(item);

            return item;
        }

        /// <summary>两侧的云纹装饰。纯装饰，不接任何输入。</summary>
        private void MakeFlourish(string path, float x)
        {
            RectTransform node = MakeNode(_contentRoot, "Flourish");
            node.sizeDelta = new Vector2(FlourishW, FlourishH);
            node.anchoredPosition = new Vector2(x, FlourishY);

            Sprite sprite = Resources.Load<Sprite>(path);
            Image img = UiDraw.MakeImage(node, "Art", sprite, Color.white);

            if (img != null)
            {
                img.rectTransform.sizeDelta = new Vector2(FlourishW, FlourishH);
            }
            else
            {
                Debug.LogWarning($"[TitleScreen] 读不到 {path}");
            }
        }

        /// <summary>
        /// 左下角的信息按钮。
        ///
        /// <b>设计稿里菜单只有四项</b>（启境 / 设置 / 成就 / 退出），旧版的第五项「制作组」
        /// 没有位置了 —— 但制作组名单不能就这么丢掉，所以挂到这颗信息按钮上。
        /// （设计稿只画了一个 `i` 圆钮，没写职责；这是最接近的映射，若美术另有安排再改。）
        /// </summary>
        private void BuildInfoButton()
        {
            RectTransform node = MakeNode(_contentRoot, "InfoButton");
            node.sizeDelta = new Vector2(InfoSize, InfoSize);
            node.anchoredPosition = new Vector2(InfoX, InfoY);

            // 命中区（uGUI 的指针事件要有能接射线的 Graphic）
            Image hit = node.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;

            Image art = UiDraw.MakeImage(node, "Art",
                Resources.Load<Sprite>("ui/title_btn_info"), Color.white);

            if (art != null)
            {
                art.rectTransform.sizeDelta = new Vector2(InfoSize, InfoSize);
            }

            EventTrigger trigger = node.gameObject.AddComponent<EventTrigger>();
            EventTrigger.Entry click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(_ =>
            {
                if (_stage == TitleStage.Menu)
                {
                    OpenPanel(SubPanelKind.Credits);
                }
            });
            trigger.triggers.Add(click);

            _infoNode = node;
        }

        // ============================================================ 布局

        private void Layout()
        {
            LayoutStage();

            Vector2 vis = VisibleSize();

            if (_promptNode != null)
            {
                _promptNode.anchoredPosition = new Vector2(0f, -vis.y * 0.5f + 120f);
            }

            // 信息按钮与菜单同进退（Prompt 阶段是隐形的，节点也要失活，否则会吞点击）
            if (_infoNode != null)
            {
                bool menuOn = _stage != TitleStage.Prompt;
                if (_infoNode.gameObject.activeSelf != menuOn)
                {
                    _infoNode.gameObject.SetActive(menuOn);
                }
            }
        }

        // ============================================================ 每帧

        private void Update()
        {
            float dt = Time.deltaTime;
            _time += dt;
            _stageTime += dt;

            Layout();
            UpdateBackdrop(_time);
            UpdateWisps(dt, _time);

            // 面板的淡入淡出由这里驱动（Cocos 那边是补间，这里每帧插值更可控）
            _panels?.Tick(dt);

            if (!ready)
            {
                return;
            }

            UpdateContent(dt);
            UpdateInput();
        }

        private void UpdateContent(float dt)
        {
            // 入场：整体淡入
            if (_contentGroup != null && _stage != TitleStage.Panel && _contentGroup.alpha < 1f)
            {
                _contentGroup.alpha = Mathf.Min(1f, _contentGroup.alpha + dt * (220f / 255f));
            }

            // 徽记缓慢浮动 + 背后柔光呼吸
            if (_titleNode != null)
            {
                _titleNode.anchoredPosition = new Vector2(
                    _titleNode.anchoredPosition.x,
                    EmblemY + Mathf.Sin(_time * 0.8f) * 9f);
            }

            if (_titleGlowGroup != null)
            {
                _titleGlowGroup.alpha =
                    (110f + Mathf.Round(45f * (0.5f + 0.5f * Mathf.Sin(_time * 1.1f)))) / 255f;
            }

            // 菜单项错峰淡入
            for (int i = 0; i < _menuItems.Count; i++)
            {
                MenuItem item = _menuItems[i];
                bool shown = _stage != TitleStage.Prompt;
                float k = shown
                    ? Mathf.Min(1f, Mathf.Max(0f, (_stageTime - i * 0.08f) / 0.4f))
                    : 0f;
                float eased = 1f - (1f - k) * (1f - k);

                CanvasGroup lg = item.ArtNode.GetComponent<CanvasGroup>();

                if (lg != null)
                {
                    lg.alpha = eased;
                }

                item.ArtNode.anchoredPosition = new Vector2(0f, (1f - eased) * -21f);

                bool isSel = i == _selected;
                float targetScale = isSel ? 1.06f : 1f;
                float cur = item.Node.localScale.x;
                float ns = cur + (targetScale - cur) * Mathf.Min(1f, dt * 14f);
                item.Node.localScale = new Vector3(ns, ns, 1f);

                // 逐帧插值而不是补间：快速连按时补间会互相打断，用插值更跟手
                float kin = Mathf.Min(1f, dt * 12f);

                if (item.Art != null)
                {
                    // 选中态靠不透明度拉开（美术只有常态图，没有选中态切图）
                    Color c = item.Art.color;
                    c.a = Mathf.Lerp(c.a, (isSel ? ItemSelectedAlpha : ItemAlpha) * eased, kin);
                    item.Art.color = c;
                }

                if (item.Glow != null)
                {
                    float target = isSel && shown ? 235f / 255f * eased : 0f;
                    item.Glow.alpha += (target - item.Glow.alpha) * Mathf.Min(1f, dt * 10f);
                }
            }

            // 「按任意键继续」呼吸
            if (_stage == TitleStage.Prompt && _promptGroup != null)
            {
                _promptGroup.alpha = (150f + 95f * (0.5f + 0.5f * Mathf.Sin(_time * 2.2f))) / 255f;
            }
        }

        // ============================================================ 输入

        private void UpdateInput()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;

            bool anyKey = kb != null && kb.anyKey.wasPressedThisFrame;
            bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;

            switch (_stage)
            {
                case TitleStage.Prompt:
                    // 任意键 / 点击推进。这一段读**原始输入**而不是动作层 ——
                    // 「任意键」本来就不是一个游戏动作。
                    if (anyKey || click)
                    {
                        EnterMenu();
                    }

                    break;

                case TitleStage.Menu:
                    if (_swallowMenuInput)
                    {
                        // 刚进菜单的那一帧：挡掉，理由见字段声明处
                        _swallowMenuInput = false;
                        return;
                    }

                    if (InputSystem.WasPressed(GameAction.MenuUp))
                    {
                        Select(_selected - 1);
                    }

                    if (InputSystem.WasPressed(GameAction.MenuDown))
                    {
                        Select(_selected + 1);
                    }

                    if (InputSystem.WasPressed(GameAction.MenuConfirm))
                    {
                        Confirm();
                    }

                    break;

                case TitleStage.Panel:
                    // 兜底自愈：阶段说「在面板里」但面板其实已经关了（状态脱节），
                    // 立刻把阶段收回来。没有这一段的话，首页会停在 Panel 阶段 ——
                    // 那一段只认返回键，菜单键和点击全部无响应（返回箭头踩过一次）。
                    if (_panels == null || !_panels.IsOpen)
                    {
                        ClosePanel();
                        return;
                    }

                    // ⚠️ **所有**按下的键都要先交给面板，不能只转返回键。
                    //
                    // 这里原来只判 `kb[Key.E].wasPressedThisFrame` 然后转发 E ——
                    // 后果是设置页的改键**永远绑不上**：捕获态进去了，
                    // 可玩家按的新键根本到不了 SettingsPanel.HandleKeyDown，
                    // CommitCapture 不被调用，表现就是「键位无法重新设置」。
                    // 那个转发口的契约是「捕获期间吃下所有键」（见它的注释），
                    // 只在调用方筛一个键，契约就废了。
                    if (kb != null)
                    {
                        foreach (UnityEngine.InputSystem.Controls.KeyControl kc in kb.allKeys)
                        {
                            if (kc.wasPressedThisFrame && _panels.HandleKeyDown(kc.keyCode))
                            {
                                return;
                            }
                        }

                        // 没被面板消费（= 不在改键捕获中）才轮到「返回」
                        if (kb[ScreenStage.PanelBackKey].wasPressedThisFrame)
                        {
                            ClosePanel();
                        }
                    }

                    // 面板阶段点面板外返回。Esc 只有键盘有，鼠标用户得给条退路。
                    if (click && _panels != null &&
                        mouse != null && !_panels.HitTest(mouse.position.ReadValue()))
                    {
                        ClosePanel();
                    }

                    break;
            }
        }

        // ============================================================ 阶段切换

        private void EnterMenu()
        {
            _stage = TitleStage.Menu;
            _stageTime = 0f;

            // 菜单项在 Prompt 阶段是失活的（理由见 BuildMenuItem），进菜单才激活。
            foreach (MenuItem item in _menuItems)
            {
                item.Node.gameObject.SetActive(true);
            }

            RequestFullScreen();

            // 按下「任意键」的那一下也会把 MenuConfirm 置位，挡掉下一帧的菜单读取
            _swallowMenuInput = true;

            if (_promptGroup != null)
            {
                _promptGroup.alpha = 0f;
            }

            if (_promptNode != null)
            {
                _promptNode.gameObject.SetActive(false);
            }
        }

        private void Select(int index)
        {
            int n = _menuItems.Count;

            if (n == 0)
            {
                return;
            }

            // 循环选择，上下走到底会绕回去
            _selected = ((index % n) + n) % n;
        }

        /// <summary>
        /// 请求全屏。
        ///
        /// <b>必须落在用户交互里</b>（浏览器规定全屏只能由用户手势触发），
        /// 所以放在 <see cref="EnterMenu"/> —— 它是玩家第一次按键 / 点击的回调。
        ///
        /// Unity 侧的两点差异：
        /// <list type="bullet">
        ///   <item>编辑器里跳过（Game View 不是真窗口，切全屏只会把布局搞乱）。</item>
        ///   <item>WebGL 上 <c>Screen.fullScreen</c> 同样受浏览器手势限制 ——
        ///     Unity 自己会在用户手势时处理，这里调用是安全的。</item>
        /// </list>
        /// </summary>
        private void RequestFullScreen()
        {
            if (Application.isEditor || Screen.fullScreen)
            {
                return;
            }

            Screen.fullScreen = true;
        }

        private void Confirm()
        {
            if (_selected < 0 || _selected >= _menuItems.Count)
            {
                return;
            }

            SubPanelKind? kind = MenuKinds[_selected];

            if (kind.HasValue)
            {
                OpenPanel(kind.Value);
                return;
            }

            if (_selected == 0)
            {
                // 进 Host（关卡宿主），由它再叠加加载第一关 —— 不再是直接进 Main
                GameSceneManager.LoadSingle(GameScenes.Host);
                return;
            }

            Quit();
        }

        private void OpenPanel(SubPanelKind kind)
        {
            if (_panels == null)
            {
                return;
            }

            _stage = TitleStage.Panel;
            _stageTime = 0f;
            _panels.Open(kind);

            if (_contentGroup != null)
            {
                _contentGroup.alpha = 90f / 255f;
            }
        }

        /// <summary>供子面板内容（设置页页首的返回箭头）反过来请求关闭。见基类的说明。</summary>
        protected override void RequestClosePanel()
        {
            ClosePanel();
        }

        /// <summary>
        /// 关面板。**幂等，且不管面板开没开都要把阶段收回来。**
        ///
        /// ⚠️ 这里原来开头是 <c>if (_panels == null || !_panels.IsOpen) return;</c> —— 那一句
        /// 是首页软锁的第二半：面板已经被人关掉、阶段却还停在 <c>Panel</c> 时，
        /// 按 E 走到这里会被这句直接弹出去，<c>_stage = Menu</c> 永远执行不到。
        /// 于是「面板关了但首页不响应任何按键」再也出不来。
        ///
        /// 现在只把「关面板」这一步做成有条件的，**阶段回收无条件执行** ——
        /// 状态脱节时至少有一条路能自愈。
        /// </summary>
        private void ClosePanel()
        {
            if (_panels != null && _panels.IsOpen)
            {
                _panels.Close();
            }

            _stage = TitleStage.Menu;
            _stageTime = 0f;

            if (_contentGroup != null)
            {
                _contentGroup.alpha = 1f;
            }
        }

        /// <summary>
        /// 「退出」不在这里真退，而是切到结束页。
        ///
        /// 好处是切场景会把首页和关卡的资源、正在跑的逻辑全部卸干净，
        /// 退出动作落在一个干净的空场景里执行 —— 不会在游戏逻辑还在跑的时候把进程掐掉。
        /// 真正的退出代码在 <see cref="EndingScreen"/> 里。
        /// </summary>
        private void Quit()
        {
            GameSceneManager.LoadSingle(GameScenes.Ending);
        }
    }
}
