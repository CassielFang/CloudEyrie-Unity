using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CloudEyrie
{
    public enum SubPanelKind
    {
        Settings,
        Achievements,
        Credits,
    }

    /// <summary>
    /// 首页的三个子面板：设置 / 成就 / 制作组。
    /// 对应 Cocos 侧 <c>ui/TitleSubPanels.ts</c>。
    ///
    /// 按项目的脚本分层约定，这里是个**普通工具类**（不是组件）：由
    /// <see cref="TitleScreen"/> 在 <c>Awake</c> 里 new 出来，面板节点挂在它给的父节点下，
    /// 生命周期跟着首页走，不需要自己注册引擎回调。
    ///
    /// 本类只管**框架**：外壳、标题、开关与淡入淡出、命中判定。
    /// 具体内容归各自的文件 —— 设置页在 <c>SettingsPanel</c>，
    /// 成就与制作组目前仍只有标题。
    ///
    /// ⚠️ <b>2026-10-07 三个页统一成「全屏页」了。</b>
    /// 原先它们是 1000×620 的**宣纸面板**（<c>panel_paper</c> + 柔边底衬 + 九宫格切边），
    /// 只有设置页因为设计稿是全屏的而单独走了一套。现在成就与制作组也并过来，
    /// 三个页共用 <see cref="PanelFrame"/> 的外壳（深青底图 + 页首整屏横线 + 左上返回箭头）。
    /// 原来那套宣纸面板的代码与常量已经删掉；<c>Resources/ui_title/panel_*.png</c>
    /// 三张贴图**还留在库里但已无人引用**（Resources 下的资源都会进包，介意的话可以挪出去）。
    /// </summary>
    public class TitleSubPanels
    {
        /// <summary>
        /// 制作组名单。
        ///
        /// </summary>
        private static readonly string[] Credits =
            { "祁烬", "泠琼", "䴰子", "雨迹", "柒月" };

        /// <summary>
        /// 制作组名单的排布。**这几个数是本页自己定的**，设计稿没画制作组页。
        /// 摆在上半屏、左对齐于中轴。
        /// </summary>
        private const float CreditsSize = 40f;
        private const float CreditsTop = 230f;
        private const float CreditsGap = 58f;

        private const string CreditsNote = "排名不分先后";
        private const float CreditsNoteY = -235f;
        private const float CreditsNoteSize = 24f;

        private RectTransform _root;
        private CanvasGroup _group;
        private RectTransform _body;
        private RectTransform _backing;
        private SettingsPanel _content;
        private bool _opened;

        private readonly Transform _host;
        private readonly PanelDeps _deps;

        public TitleSubPanels(Transform host, PanelDeps deps)
        {
            _host = host;
            _deps = deps;
            GetRoot();
        }

        public bool IsOpen
        {
            get { return _opened; }
        }

        private RectTransform GetRoot()
        {
            if (_root != null)
            {
                return _root;
            }

            _root = UiDraw.MakeNode(_host, "SubPanels");
            UiDraw.Stretch(_root);
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _root.gameObject.SetActive(false);
            return _root;
        }

        // ======================================================== 系统中文字体

        // 系统中文字体走 UiDraw 的共用实现 —— 制作组名单（「䴰」U+4D30）和
        // HUD 的「灵炁」标签（「炁」U+7081）都需要它，两处各写一份迟早漂移。

        // ======================================================== 开关

        public void Open(SubPanelKind kind)
        {
            RectTransform root = GetRoot();

            // 先拆旧内容再销毁节点：内容对象在全局单例上挂过监听（详见 DisposeContent）
            DisposeContent();

            if (_body != null)
            {
                Object.Destroy(_body.gameObject);
            }

            _backing = null;

            _body = UiDraw.MakeNode(root, "Body");
            UiDraw.Stretch(_body);

            switch (kind)
            {
                case SubPanelKind.Settings:
                    BuildSettings();
                    break;
                case SubPanelKind.Achievements:
                    BuildAchievements();
                    break;
                case SubPanelKind.Credits:
                    BuildCredits();
                    break;
            }

            _opened = true;
            root.gameObject.SetActive(true);
            _group.alpha = 0f;
        }

        public void Close()
        {
            if (!_opened)
            {
                return;
            }

            // 关的瞬间就收掉内容，**不能等淡出结束**：这 0.2 秒里内容还挂在
            // 全局单例上（改键捕获模式），拖到之后才清就晚了一拍
            DisposeContent();
            _opened = false;

            if (_root != null)
            {
                _root.gameObject.SetActive(false);
            }
        }

        /// <summary>每帧由 <see cref="TitleScreen"/> 驱动淡入淡出（Cocos 那边用补间）。</summary>
        public void Tick(float dt)
        {
            if (_group == null)
            {
                return;
            }

            float target = _opened ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, dt * 4f);
        }

        /// <summary>点是否落在面板上。落在外面时由 <see cref="TitleScreen"/> 负责关掉面板。</summary>
        public bool HitTest(Vector2 screenPoint)
        {
            if (!_opened || _backing == null)
            {
                return false;
            }

            // ScreenSpaceOverlay 的画布：相机传 null
            return RectTransformUtility.RectangleContainsScreenPoint(_backing, screenPoint, null);
        }

        /// <summary>
        /// 把按键先交给面板内容。返回 true = 已消费，<see cref="TitleScreen"/> 不要再处理。
        ///
        /// 目前只有设置页会消费：改键捕获期间 E 是「取消改键」而不是「退出面板」，
        /// 不先问一声的话，玩家想取消改键会被直接踢出面板。
        /// </summary>
        public bool HandleKeyDown(Key key)
        {
            return _content != null && _content.HandleKeyDown(key);
        }

        /// <summary>
        /// 场景卸载时调（见 <c>TitleScreen.OnDestroy</c>）。
        /// <see cref="EventBus"/> 活得比场景久，内容对象若还挂着订阅，
        /// 切场景后设置一变就会回调到已销毁的节点上。
        /// </summary>
        public void Dispose()
        {
            DisposeContent();
        }

        private void DisposeContent()
        {
            if (_content != null)
            {
                _content.Dispose();
                _content = null;
            }
        }

        // ======================================================== 三个面板

        /// <summary>
        /// 设置页（音画设置 / 键位设置）。
        ///
        /// ⚠️ <b>这一页是**全屏**的，不走 <see cref="AttachPaper"/></b> ——
        /// 设计稿（<c>弹窗-设置-音画设置.png</c>）里它就是一整屏：深青底图铺满 + 顶部一条
        /// 整屏横线 + 四行铺开，不是另外两个页那种 1000×620 的纸面板。
        /// 外壳走 <see cref="PanelFrame"/>（和成就 / 制作组共用），
        /// 页首那两个**页签**归 <see cref="SettingsPanel"/> 自己建。
        /// </summary>
        private void BuildSettings()
        {
            _backing = PanelFrame.MakeBackdrop(_body);
            PanelFrame.MakeRule(_body);
            PanelFrame.MakeArrow(_body, "ui/settings_arrow_l", PanelFrame.BackX, RequestClose);
            PanelFrame.MakeExitHint(_body);

            _content = new SettingsPanel(_body, _deps);
        }

        /// <summary>
        /// 成就页。和设置页同一个全屏外壳（底图 + 顶线 + 返回箭头 + 居中标题）。
        ///
        /// TODO：成就系统尚未实现，这里先只有外壳 + 标题。
        /// </summary>
        private void BuildAchievements()
        {
            BuildFullScreen("成　就");
        }

        private void BuildCredits()
        {
            BuildFullScreen("制 作 组");

            Font sysFont = UiDraw.SystemCjkFont;

            for (int i = 0; i < Credits.Length; i++)
            {
                RectTransform n = UiDraw.MakeNode(_body, $"Name{i}");
                n.anchoredPosition = new Vector2(0f, CreditsTop - i * CreditsGap);
                Text label = _deps.MakeLabel(n, "Label", Credits[i], (int)CreditsSize,
                                             PanelFrame.TextColor, 0f);

                // 有系统字体就用它（避开「䴰」掉字）；没有就退回游戏字体，接受那一个字掉
                if (sysFont != null)
                {
                    label.font = sysFont;
                }
            }

            RectTransform note = UiDraw.MakeNode(_body, "Note");
            note.anchoredPosition = new Vector2(0f, CreditsNoteY);
            _deps.MakeLabel(note, "Label", CreditsNote, (int)CreditsNoteSize,
                            PanelFrame.DimColor, 0f);
        }

        /// <summary>
        /// 全屏页的公共部分：底 + 顶线 + 返回箭头 + 居中标题。
        /// 内容由调用方在 <see cref="_body"/> 下自己摆（顶线以下的区域）。
        /// </summary>
        private void BuildFullScreen(string title)
        {
            _backing = PanelFrame.MakeBackdrop(_body);
            PanelFrame.MakeRule(_body);
            PanelFrame.MakeArrow(_body, "ui/settings_arrow_l", PanelFrame.BackX, RequestClose);

            RectTransform n = UiDraw.MakeNode(_body, "Heading");
            n.anchoredPosition = new Vector2(0f, PanelFrame.TitleY);
            _deps.MakeLabel(n, "Label", title, PanelFrame.TitleSize, PanelFrame.TextColor, 0f);

            PanelFrame.MakeExitHint(_body);
        }

        /// <summary>
        /// 页首返回箭头被点。**必须走宿主（<see cref="TitleScreen"/>），不能直接
        /// <see cref="Close"/>。**
        ///
        /// ⚠️ 这里曾经直接接的是 <c>Close</c>（本类自己的方法），后果是**首页软锁**：
        /// <c>Close()</c> 只把面板收起来，而 <see cref="TitleScreen"/> 的阶段状态还停在
        /// <c>Panel</c> —— 那一段只认 E 和「点面板外」，而底图铺满全屏让后者永远不成立，
        /// 于是菜单键和点击全都不响应。更死的是连 E 也救不回来：
        /// <c>TitleScreen.ClosePanel()</c> 开头有「面板没开就 return」，此时 <c>IsOpen</c>
        /// 已经是 false，那一句直接把阶段回收跳过了。
        ///
        /// <see cref="PanelDeps.ClosePanel"/> 转的是 <c>RequestClosePanel()</c>，
        /// 正是为这件事留的口子（基类注释里写明了，别再绕过去）。
        /// </summary>
        private void RequestClose()
        {
            _deps.ClosePanel();
        }

        // 宣纸面板那一套（柔边底衬 + 九宫格纸纹 + 天头标题 + 地脚「按 E 退出」）
        // 2026-10-07 三页统一成全屏页后已全部删除 —— 见类注释。
    }
}
