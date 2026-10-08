using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CloudEyrie
{
    /// <summary>
    /// 设置页的内容：音量 / 键位两个页签。
    /// 对应 Cocos 侧 <c>ui/SettingsPanel.ts</c>。
    ///
    /// 按项目的脚本分层约定，这是个**普通工具类**（不是 MonoBehaviour）：由
    /// <see cref="TitleSubPanels"/> 在 <c>BuildSettings()</c> 里 new 出来，
    /// 节点挂在它给的 Body 下，生命周期跟着面板走，引擎回调一个都不注册。
    ///
    /// 这一页只做一件事：**把已经存在的链路接到界面上**。音量走 <see cref="AudioManager"/>
    /// （落盘与广播由 <see cref="GameSettings"/> 负责），键位走 <see cref="InputSystem"/>。
    /// UI 不直接碰 <see cref="GameSettings"/>，也不缓存任何状态 —— 每次渲染都从单例现读，
    /// 所以切页签整个重建节点也不会丢状态。
    ///
    /// ⚠️ <b>最要命的一点</b>：改键期间 <see cref="InputSystem"/> 处于**捕获模式**，
    /// 此时它不记录任何按键。忘了退出 = 游戏里按键全没反应。
    /// 所以 <see cref="Dispose"/> **必须**被调用（面板关闭 / 重建 / 场景卸载三处，
    /// 见 <see cref="TitleSubPanels"/> 与 <c>TitleScreen.OnDestroy</c>）。
    ///
    /// 接口契约见 <c>docs/设置页接口.md</c>。
    /// </summary>
    public class SettingsPanel
    {
        // ============================================================ 版式
        //
        // 坐标原点在**画布中心**（y 向上），因为本页的容器是全屏的 Body。
        //
        // ⚠️ 下面每一个数都是 tools/ui_slice_locator.py 在
        // 《美术素材/弹窗-设置-音画设置.png》里匹配切图 / 扫描亮线量出来的，
        // 换算公式：cx = 设计稿 x − 960，cy = 540 − 设计稿 y。别再肉眼估。

        private static readonly string[] TabLabels = { "音画设置", "键位设置" };

        /// <summary>
        /// 两个页签的中心 x。设计稿量得「音画设置」x[464,695]、「键位设置」x[1263,1458]。
        /// 取值与 <see cref="PanelFrame.TabX0"/> / <see cref="PanelFrame.TabX1"/> 同源。
        /// </summary>
        private static readonly float[] TabX = { PanelFrame.TabX0, PanelFrame.TabX1 };

        private const float TabY = PanelFrame.TitleY;
        private const int TabSize = PanelFrame.TitleSize;
        private const float TabHitW = 320f;
        private const float TabHitH = 72f;

        /// <summary>
        /// 选中页签底下那条亮线。
        /// 设计稿里那截亮线在 x[328,856]，宽 528 —— 它**比页签文字宽**（文字才 232），
        /// 所以不能照文字宽度画，只跟着页签中心挪。
        /// </summary>
        private const float MarkW = 528f;
        private const float MarkH = 5f;

        /// <summary>前进（换页签）箭头。设计稿里它落在 (1753,40) 中心 (1789,76)。返回箭头归 <see cref="PanelFrame"/>。</summary>
        private const float ForwardX = 829f;

        // ---- 四行 ----
        // 设计稿行中心 y = 295 / 498 / 701 / 904（间距 203）-> cy = 245 / 42 / −161 / −364
        private static readonly float[] VolRowY = { 245f, 42f, -161f, -364f };

        /// <summary>行标题的左缘。设计稿四行文字都从 x≈135 起（左对齐）</summary>
        private const float CaptionX = -825f;
        private const int VolTextSize = 44;

        // ---- 滑条 ----
        // 设计稿里行内那条细线在 x[497,1694] -> cx −463 ~ 734
        private const float TrackLeft = -463f;
        private const float TrackRight = 734f;
        private const float TrackW = TrackRight - TrackLeft;

        /// <summary>
        /// 手柄中心活动范围相对线两端内收的量（= 未选中手柄的半径 110/2）。
        /// 不内收的话，比例拉到 0 或 1 时手柄会有一半悬在线外。
        /// </summary>
        private const float HandleInset = 55f;

        private const float TravelW = TrackW - HandleInset * 2f;
        private const float TrackCenterX = (TrackLeft + TrackRight) * 0.5f;

        /// <summary>手柄切图原生尺寸：未选中 110、选中 230（选中态美术就是画大了一圈）</summary>
        private const float HandleOffSize = 110f;
        private const float HandleOnSize = 230f;

        /// <summary>命中盒比 5px 的线高得多（那么细的线根本抓不住），点与拖都以它为准</summary>
        private const float SliderHitH = 60f;

        /// <summary>左右箭头一次调多少比例</summary>
        private const float StepRatio = 0.05f;

        // 键位页：一趟 10 行（只列游戏内动作）、每行固定 2 个槽位。
        // 槽位列数固定而不是「有几个画几个」—— 方向键有 2 个绑定、其余只有 1 个，
        // 按实际数量画的话行与行参差不齐，读起来不像表格。
        //
        // ⚠️ 尺寸是**照着全屏改过的**：老版这套数是给 620 高的纸面板定的，
        // 挪到 1080 高的全屏上会缩成中间一小坨、四周全是空的。
        // 现在 10 行从 +330 排到 −282，正好落在「顶线以下、页脚以上」这段里。
        private const float KeyRowTop = 330f;
        private const float KeyRowPitch = 68f;
        private const float KeyNameX = -290f;
        private const float KeyNameW = 260f;
        private static readonly float[] SlotX = { 60f, 250f };
        private const float SlotW = 170f;
        private const float SlotH = 34f;
        private const int KeyTextSize = 30;
        private const int SlotTextSize = 26;

        // 页脚。「恢复默认」按钮与改键提示共用这个位置，按状态二选一显示。
        // 本页现在是全屏的，所以贴着屏幕底边摆（设计稿没有页脚，这是本页自己的功能部件）
        private const float FootY = -470f;
        private const float BtnW = 220f;
        private const float BtnH = 38f;
        private const int BtnTextSize = 24;
        private const int FootHintSize = 22;

        private const string CaptureHint = "按下新键完成绑定，Esc 取消";

        // ============================================================ 配色
        //
        // ⚠️ **这一页从「浅底墨字」翻成了「深底浅字」**：老版是白纸面板，新版底图
        // settings_bg 是深青的（设计稿就是深色）。原来那套墨色 (52,76,86) 压上去
        // 会直接糊进背景里看不见。改配色时这一整块要一起看。
        //
        // 一律 outline=0：底图是平的，描边是给「浅色字压在山水的明暗上」用的，
        // 这里加上去只会糊出双层边。

        private static readonly Color ColorText = new Color32(226, 240, 242, 255);
        private static readonly Color ColorAccent = new Color32(150, 230, 245, 255);
        private static readonly Color ColorDim = new Color32(126, 150, 156, 255);

        /// <summary>唯一强调色，只给「正在改键 / 冲突」用</summary>
        private static readonly Color ColorWarn = new Color32(240, 158, 122, 255);

        /// <summary>滑条那条细线的颜色。切图 settings_line 是白的，这里染色压淡一点</summary>
        private static readonly Color ColorTrack = new Color32(214, 232, 234, 150);

        private static readonly Color ColorSlot = new Color32(24, 40, 44, 200);
        private static readonly Color ColorSlotHover = new Color32(40, 62, 68, 220);
        private static readonly Color ColorSlotActive = new Color32(96, 74, 44, 230);
        private static readonly Color ColorRule = new Color32(150, 178, 184, 120);
        private static readonly Color ColorBtnInk = new Color32(30, 50, 56, 235);
        private static readonly Color ColorBtnHover = new Color32(48, 74, 82, 245);
        private static readonly Color ColorBtnEdge = new Color32(110, 150, 158, 235);
        private static readonly Color ColorBtnText = new Color32(232, 244, 246, 255);

        /// <summary>页签常态 / 选中态</summary>
        private static readonly Color ColorTabDim = new Color32(126, 150, 156, 255);
        private static readonly Color ColorTabOn = new Color32(244, 252, 253, 255);

        /// <summary>键位页会列出来的动作。数组顺序即显示顺序。</summary>
        private static readonly GameAction[] GroupGame =
        {
            GameAction.MoveLeft, GameAction.MoveRight, GameAction.MoveUp, GameAction.MoveDown,
            GameAction.Jump, GameAction.Dash, GameAction.Attack, GameAction.Block,
            GameAction.FormSwitch, GameAction.FormMerge,
        };

        /// <summary>
        /// **故意不在设置页列出来**的动作（首页菜单的三个键）。
        /// 单独立一张表是为了让 <see cref="CheckActionGroups"/> 能发现
        /// 「新加了动作却忘了归类」—— 那种情况的表象是「设置页里根本没有这一项」，
        /// 不报错、很难发现。
        /// </summary>
        private static readonly GameAction[] GroupHidden =
        {
            GameAction.MenuUp, GameAction.MenuDown, GameAction.MenuConfirm,
        };

        private class VolumeRow
        {
            public string Caption;
            public Func<float> Get;
            public Action<float> Set;
        }

        /// <summary>
        /// 四行。**顺序与文案照抄设计稿**（主音量 / 背景音量 / 音效音量 / 画面亮度）。
        ///
        /// ⚠️ 「画面亮度」是设计稿里有、而游戏设置里原本**没有**的一项 ——
        /// 这一行落地时顺带在 <see cref="GameSettings"/> 里补了 brightness
        /// （0.35~1，默认 1）与执行者 <see cref="ScreenDimmer"/>。
        /// 它的下限不是 0：纯黑会让玩家以为游戏崩了。
        /// </summary>
        private static readonly VolumeRow[] VolRows =
        {
            new VolumeRow { Caption = "主音量", Get = AudioManager.GetMasterVolume, Set = AudioManager.SetMasterVolume },
            new VolumeRow { Caption = "背景音量", Get = () => AudioManager.GetChannelVolume(AudioBus.Bgm), Set = v => AudioManager.SetChannelVolume(AudioBus.Bgm, v) },
            new VolumeRow { Caption = "音效音量", Get = () => AudioManager.GetChannelVolume(AudioBus.Sfx), Set = v => AudioManager.SetChannelVolume(AudioBus.Sfx, v) },
            new VolumeRow { Caption = "画面亮度", Get = GameSettings.GetBrightness, Set = GameSettings.SetBrightness },
        };

        private enum Tab { Audio, Bindings }

        private class SliderRow
        {
            public Func<float> Get;
            public Action<float> Set;
            public RectTransform Box;

            /// <summary>太极手柄。选中 / 未选中是**两张不同尺寸的切图**，切状态时换 sprite。</summary>
            public Image Handle;

            public Text Caption;
        }

        private class CellRow
        {
            public GameAction Action;
            public int Index;
            public Text Label;
            public Image Slot;
        }

        private class TabEntry
        {
            public Text Label;
        }

        private Tab _tab = Tab.Audio;
        private bool _disposed;

        /// <summary>页签栏（切页签不重建，只换选中态）</summary>
        private readonly List<TabEntry> _tabs = new List<TabEntry>();

        /// <summary>选中页签底下那条亮线。只有一条，切页签时挪位置</summary>
        private Image _mark;

        /// <summary>当前页签的内容根。切页签整个销毁重建 —— 状态全在单例里，重建即恢复</summary>
        private RectTransform _page;

        private GameObject _footBtn;
        private Image _footBtnInk;
        private Text _footHint;

        private readonly List<SliderRow> _sliders = new List<SliderRow>();
        private readonly List<CellRow> _cells = new List<CellRow>();

        /// <summary>
        /// 正在拖的滑条。
        ///
        /// <c>Ratio</c> 是鼠标算出来的**连续**比例，视觉每帧按它画（跟手）；
        /// <c>Written</c> 是上一次落盘用的整数百分比 —— 落盘要限频
        /// （<c>SetVolume</c> 每次都同步写 PlayerPrefs），
        /// 但**视觉不能被这个量化拖慢**，否则把手一格一格跳。
        /// </summary>
        private class DragState
        {
            public SliderRow Row;
            public float Ratio;
            public int Written;
        }

        private DragState _dragging;

        /// <summary>当前被操作的那一行（手柄切「选中」版）。整页重建时置空。</summary>
        private SliderRow _selectedRow;

        private class CaptureState
        {
            public GameAction Action;
            public int Index;
        }

        private CaptureState _capture;

        private readonly RectTransform _body;
        private readonly PanelDeps _deps;

        public SettingsPanel(RectTransform body, PanelDeps deps)
        {
            _body = body;
            _deps = deps;

            BuildTabs();
            SwitchTab(_tab);

            EventBus.On<SettingsChangedEvent>(GameEvents.SettingsChanged, OnSettingsChanged);
        }

        // ======================================================== 对外

        /// <summary>
        /// 键盘事件转发口。返回 true = 本面板已消费，<see cref="TitleScreen"/> 不要再处理
        /// （具体就是别再拿这个返回键去关面板）。
        ///
        /// 捕获中吃下所有键：返回键（见 <see cref="ScreenStage.IsBackKey"/>，现在是 ESC）
        /// 取消改键，其余落绑定。
        /// 非捕获时一律不消费 —— 关面板仍由 <see cref="TitleScreen"/> 处理，语义不变。
        ///
        /// ⚠️ <b>调用方必须把「所有」按下的键都转进来</b>，不能只转返回键 ——
        /// 只转返回键的话捕获进去也永远绑不上（<see cref="TitleScreen"/> 踩过，
        /// 见那边的注释）。这个口子的契约就是「捕获期间吃下所有键」。
        /// </summary>
        public bool HandleKeyDown(Key key)
        {
            if (_disposed || _capture == null)
            {
                return false;
            }

            if (ScreenStage.IsBackKey(key))
            {
                EndCapture();
                RefreshSlots();
                return true;
            }

            CommitCapture(key);
            return true;
        }

        /// <summary>
        /// 拆掉对外部的订阅、退出捕获模式。**幂等**，可以重复调。
        ///
        /// 不销毁任何节点 —— 节点归 <see cref="TitleSubPanels"/> 的 body 管。
        ///
        /// ⚠️ **必须在面板关闭 / 重建 / 场景卸载时调用**：捕获模式没退出的话，
        /// <see cref="InputSystem"/> 会一直不记录按键，表现是「游戏里按键全部没反应」。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            EndCapture();
            _dragging = null;
            EventBus.Off<SettingsChangedEvent>(GameEvents.SettingsChanged, OnSettingsChanged);
        }

        // ======================================================== 页签

        private void BuildTabs()
        {
            for (int i = 0; i < TabLabels.Length; i++)
            {
                RectTransform node = UiDraw.MakeNode(_body, $"Tab{i}");
                node.sizeDelta = new Vector2(TabHitW, TabHitH);
                node.anchoredPosition = new Vector2(TabX[i], TabY);

                // uGUI 的指针事件要求节点上有能命中射线的 Graphic
                Image hit = node.gameObject.AddComponent<Image>();
                hit.color = new Color(1f, 1f, 1f, 0f);
                hit.raycastTarget = true;

                Text label = _deps.MakeLabel(node, "Label", TabLabels[i], TabSize, ColorTabDim, 0f);

                int index = i;
                EventTrigger trigger = node.gameObject.AddComponent<EventTrigger>();
                AddTrigger(trigger, EventTriggerType.PointerClick,
                    _ => SwitchTab(index == 0 ? Tab.Audio : Tab.Bindings));

                _tabs.Add(new TabEntry { Label = label });
            }

            // 页首的整屏横线与左侧返回箭头归外壳（TitleSubPanels / PanelFrame）建。
            // 这里只补**前进**箭头 —— 它是设置页独有的（右 = 换到另一个页签）。
            PanelFrame.MakeArrow(_body, "ui/settings_arrow_r", ForwardX,
                () => SwitchTab(_tab == Tab.Audio ? Tab.Bindings : Tab.Audio));

            // 选中页签底下的亮线。**只有一条**，跟着选中的页签移动 ——
            // 它比页签文字宽得多（设计稿 528 对 232），所以宽度用 MarkW 而不是文字宽。
            _mark = MakeLine(_body, "TabMark", MarkW, MarkH, StageColors.Glow);
            _mark.rectTransform.anchoredPosition = new Vector2(TabX[0], PanelFrame.RuleY);
        }

        private void SwitchTab(Tab tab)
        {
            if (_disposed)
            {
                return;
            }

            // 切页前先收掉未完成的改键：捕获模式挂在输入单例上，不跟着节点销毁
            EndCapture();

            // 拖拽状态同理要清：下面就把整页销毁了，旧的滑条 RectTransform 立刻失效。
            // _selectedRow 也是 —— 它指的是旧的 SliderRow 对象，留着会让 SelectRow 的
            // 「同一个就不重画」判断误判，新页的手柄永远切不到选中态。
            _dragging = null;
            _selectedRow = null;
            _tab = tab;

            if (_page != null)
            {
                UnityEngine.Object.Destroy(_page.gameObject);
            }

            _page = null;
            _footBtn = null;
            _footHint = null;
            _sliders.Clear();
            _cells.Clear();

            for (int i = 0; i < _tabs.Count; i++)
            {
                bool on = (i == 0) == (tab == Tab.Audio);
                _tabs[i].Label.color = on ? ColorTabOn : ColorTabDim;
            }

            // 亮线挪到选中页签底下（它比页签文字宽，所以只居中、不跟着文字宽度变）
            if (_mark != null)
            {
                _mark.rectTransform.anchoredPosition = new Vector2(TabX[tab == Tab.Audio ? 0 : 1], PanelFrame.RuleY);
            }

            RectTransform page = UiDraw.MakeNode(_body, "Page");
            _page = page;
            BuildFoot(page);

            if (tab == Tab.Audio)
            {
                BuildAudioPage(page);
            }
            else
            {
                BuildBindingsPage(page);
            }

            UpdateFoot();
        }

        // ======================================================== 音量页

        private void BuildAudioPage(RectTransform page)
        {
            for (int i = 0; i < VolRows.Length; i++)
            {
                BuildSlider(page, VolRowY[i], VolRows[i]);
            }
        }

        private void BuildSlider(RectTransform page, float y, VolumeRow row)
        {
            // 行标题：左对齐，左缘固定在 CaptionX
            Text caption = ColumnLabel(page, "Caption", row.Caption, VolTextSize,
                ColorText, 330f, TextAnchor.MiddleLeft);
            caption.rectTransform.anchoredPosition = new Vector2(CaptionX, y);

            // 可见的那条细线（切图 settings_line，原生 1920×5）
            Image track = UiDraw.MakeImage(page, "Track", Resources.Load<Sprite>("ui/settings_line"), ColorTrack);
            track.rectTransform.sizeDelta = new Vector2(TrackW, PanelFrame.RuleH);
            track.rectTransform.anchoredPosition = new Vector2(TrackCenterX, y);

            // 命中盒：透明的，只用来接指针 / 算比例。宽度**正好是手柄的活动范围**，
            // 这样 local.x / TravelW + 0.5 就是比例，不必再补偏移。
            RectTransform box = UiDraw.MakeNode(page, "Slider");
            box.sizeDelta = new Vector2(TravelW, SliderHitH);
            box.anchoredPosition = new Vector2(TrackCenterX, y);

            Image boxHit = box.gameObject.AddComponent<Image>();
            boxHit.color = new Color(1f, 1f, 1f, 0f);
            boxHit.raycastTarget = true;

            Image handle = UiDraw.MakeImage(box, "Handle",
                Resources.Load<Sprite>("ui/settings_handle_off"), Color.white);

            if (handle != null && handle.sprite != null)
            {
                handle.rectTransform.sizeDelta = new Vector2(HandleOffSize, HandleOffSize);
            }
            else
            {
                Debug.LogWarning("[SettingsPanel] 读不到 ui/settings_handle_off，滑条手柄会缺失");
            }

            SliderRow r = new SliderRow
            {
                Get = row.Get,
                Set = row.Set,
                Box = box,
                Handle = handle,
                Caption = caption,
            };

            // 两枚步进箭头摆在线的两端之外，间隔按设计稿量得的：
            // 左箭头中心 cx −524（线左端 −463，往左 61）、右箭头中心 cx 829（线右端 734，往右 95）。
            MakeArrowButton(page, "ui/settings_arrow_l", TrackLeft - 61f, y,
                () => StepSlider(r, -StepRatio));
            MakeArrowButton(page, "ui/settings_arrow_r", TrackRight + 95f, y,
                () => StepSlider(r, StepRatio));

            _sliders.Add(r);
            PaintSlider(r, r.Get());

            // ⚠️ 拖动走 uGUI 的 PointerDown/Drag/PointerUp（节点级），不要用全局鼠标轮询 ——
            // 那样会漏掉「按下后拖出滑条范围」的情况。PointerDown 里点哪跳哪，
            // 不必非得抓住把手。
            EventTrigger trigger = box.gameObject.AddComponent<EventTrigger>();

            AddTrigger(trigger, EventTriggerType.PointerDown, e =>
            {
                SelectRow(r);
                _dragging = new DragState
                {
                    Row = r,
                    Ratio = r.Get(),
                    Written = Mathf.RoundToInt(r.Get() * 100f),
                };

                ApplyDrag(e);
            });

            AddTrigger(trigger, EventTriggerType.Drag, ApplyDrag);
            AddTrigger(trigger, EventTriggerType.PointerUp, _ => EndDrag());

            // 鼠标压到这一行就把手柄换成「选中」版（设计稿里选中态就是画大一圈的那张）
            AddTrigger(trigger, EventTriggerType.PointerEnter, _ => SelectRow(r));
        }

        /// <summary>点左右箭头：按固定步长调一格。</summary>
        private void StepSlider(SliderRow row, float delta)
        {
            if (row == null)
            {
                return;
            }

            SelectRow(row);
            row.Set(Mathf.Clamp(row.Get() + delta, 0f, 1f));
            PaintSlider(row, row.Get());
        }

        /// <summary>
        /// 把某一行标成「当前行」。
        ///
        /// 设计稿里只有被操作的那一行手柄是**大一圈、带辉光**的选中版切图
        /// （230×233 对未选中的 110×110，不是同一个形状语言），所以这里连尺寸一起换。
        /// </summary>
        private void SelectRow(SliderRow row)
        {
            if (_selectedRow == row)
            {
                return;
            }

            _selectedRow = row;

            foreach (SliderRow r in _sliders)
            {
                if (r.Handle == null)
                {
                    continue;
                }

                bool on = r == row;
                r.Handle.sprite = Resources.Load<Sprite>(
                    on ? "ui/settings_handle_on" : "ui/settings_handle_off");

                float size = on ? HandleOnSize : HandleOffSize;
                r.Handle.rectTransform.sizeDelta = new Vector2(size, size);
            }
        }

        /// <summary>建一枚可点的箭头。左右箭头离滑条两端的距离按设计稿量得的间隔摆。</summary>
        private Image MakeArrowButton(RectTransform page, string path, float x, float y, Action onClick)
        {
            RectTransform node = UiDraw.MakeNode(page, "Step");
            node.sizeDelta = new Vector2(PanelFrame.ArrowSize, PanelFrame.ArrowSize);
            node.anchoredPosition = new Vector2(x, y);

            Image hit = node.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;

            Image art = UiDraw.MakeImage(node, "Art", Resources.Load<Sprite>(path), Color.white);

            if (art != null && art.sprite != null)
            {
                art.rectTransform.sizeDelta = new Vector2(PanelFrame.ArrowSize, PanelFrame.ArrowSize);
            }

            EventTrigger trigger = node.gameObject.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerClick, _ => onClick());

            return art;
        }

        /// <summary>
        /// 鼠标位置 → 比例。用 <c>ScreenPointToLocalPointInRectangle</c> 而不是直接拿 x 相减：
        /// 面板以后挪了位置或加了缩放，前者自动跟着变。
        /// </summary>
        private void ApplyDrag(BaseEventData data)
        {
            DragState drag = _dragging;

            if (drag == null || drag.Row.Box == null)
            {
                return;
            }

            PointerEventData ped = data as PointerEventData;

            if (ped == null)
            {
                return;
            }

            Vector2 local;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    drag.Row.Box, ped.position, null, out local))
            {
                return;
            }

            // Box 的宽度就是手柄的活动范围，pivot 在中心 —— 所以除以 TravelW 再 +0.5 即是比例
            float ratio = Mathf.Clamp01(local.x / TravelW + 0.5f);
            drag.Ratio = ratio;

            // 视觉按连续值画（跟手）；落盘按整数百分比限频
            PaintSlider(drag.Row, ratio);

            int pct = Mathf.RoundToInt(ratio * 100f);

            if (pct != drag.Written)
            {
                drag.Written = pct;
                drag.Row.Set(pct / 100f);
            }
        }

        /// <summary>松手收尾：补一次精确值（拖动中落盘量化到 1%，这里把最后那零点几补上）。</summary>
        private void EndDrag()
        {
            DragState drag = _dragging;
            _dragging = null;

            if (drag == null)
            {
                return;
            }

            if (drag.Row.Handle != null)
            {
                drag.Row.Set(drag.Ratio);
                PaintSlider(drag.Row, drag.Ratio);
            }
        }

        /// <summary>
        /// 按给定比例摆手柄。**不读存档** —— 拖动中的连续值要能直接画出来。
        ///
        /// 手柄的活动范围是 <see cref="TravelW"/>（比线短 2×<see cref="HandleInset"/>），
        /// 而 <c>Box</c> 的宽度正好就是 TravelW、pivot 在中心，
        /// 所以手柄的局部 x 就是 <c>-TravelW/2 + TravelW*v</c>。
        /// </summary>
        private static void PaintSlider(SliderRow row, float value)
        {
            if (row.Handle == null)
            {
                return;
            }

            float v = Mathf.Clamp01(value);
            row.Handle.rectTransform.anchoredPosition =
                new Vector2(-TravelW * 0.5f + TravelW * v, 0f);
        }

        private void RefreshSliders()
        {
            foreach (SliderRow row in _sliders)
            {
                // 正在拖的那条由 ApplyDrag 逐帧画，别在这里按存档值把它拽回量化后的位置
                if (_dragging != null && _dragging.Row == row)
                {
                    continue;
                }

                if (row.Handle != null)
                {
                    PaintSlider(row, row.Get());
                }
            }
        }

        // ======================================================== 键位页

        private void BuildBindingsPage(RectTransform page)
        {
            CheckActionGroups();

            for (int i = 0; i < GroupGame.Length; i++)
            {
                BuildActionRow(page, GroupGame[i], KeyRowTop - i * KeyRowPitch);
            }

            RefreshSlots();
        }

        private void BuildActionRow(RectTransform page, GameAction action, float y)
        {
            Text name = ColumnLabel(page, "Name", InputActions.ActionLabels[action], KeyTextSize,
                ColorText, KeyNameW, TextAnchor.MiddleLeft);
            name.rectTransform.anchoredPosition = new Vector2(KeyNameX, y);

            Binding[] binds = InputSystem.GetBindings(action);

            for (int i = 0; i < SlotX.Length; i++)
            {
                if (i >= binds.Length)
                {
                    // 这个动作没这么多绑定：留空，保住列位
                    continue;
                }

                // ⚠️ **必须拷一份索引给闭包用**。
                //
                // `for` 的循环变量 `i` 是**整个循环共用一个变量**（和 `foreach` 不同），
                // 而下面 PointerClick 的 lambda 是**点下去那一刻**才读它的 ——
                // 那时循环早就跑完，`i` 已经是 SlotX.Length(2) 了。
                // 于是每次点击都变成 BeginCapture(action, 2) →
                // Rebind(action, 2, …) → SetBinding 越界 → **绑定一个字节都没动**，
                // 而且只留一条 LogWarning，表现就是「键位点了没反应、改不了」。
                //
                // 同一文件 BuildTabs 里写的就是 `int index = i;`，这里漏了一次。
                int slotIndex = i;

                Image slot = MakeBar(page, "Slot", SlotW, SlotH, ColorSlot, 0.5f, clickable: true);
                slot.rectTransform.anchoredPosition = new Vector2(SlotX[i], y);

                Text label = _deps.MakeLabel(slot.rectTransform, "Text", "", SlotTextSize, ColorAccent, 0f);

                CellRow cell = new CellRow { Action = action, Index = slotIndex, Label = label, Slot = slot };
                _cells.Add(cell);

                EventTrigger trigger = slot.gameObject.AddComponent<EventTrigger>();

                AddTrigger(trigger, EventTriggerType.PointerEnter, _ =>
                {
                    if (!IsCapturing(cell))
                    {
                        cell.Slot.color = ColorSlotHover;
                    }
                });

                AddTrigger(trigger, EventTriggerType.PointerExit, _ =>
                {
                    if (!IsCapturing(cell))
                    {
                        cell.Slot.color = ColorSlot;
                    }
                });

                AddTrigger(trigger, EventTriggerType.PointerClick, _ => BeginCapture(action, slotIndex));
            }
        }

        private bool IsCapturing(CellRow cell)
        {
            return _capture != null && _capture.Action == cell.Action && _capture.Index == cell.Index;
        }

        private void RefreshSlots()
        {
            foreach (CellRow cell in _cells)
            {
                if (cell.Label == null || cell.Slot == null)
                {
                    continue;
                }

                bool cap = IsCapturing(cell);
                Binding[] binds = InputSystem.GetBindings(cell.Action);
                Binding b = cell.Index < binds.Length ? binds[cell.Index] : default;

                cell.Label.text = cap ? "按新键…" : (cell.Index < binds.Length ? InputActions.BindingLabel(b) : "");
                cell.Label.color = cap ? ColorWarn : ColorAccent;
                cell.Slot.color = cap ? ColorSlotActive : ColorSlot;
            }
        }

        // ======================================================== 改键捕获

        private void BeginCapture(GameAction action, int index)
        {
            if (_disposed)
            {
                return;
            }

            EndCapture();
            _capture = new CaptureState { Action = action, Index = index };

            // 先挂起动作层：捕获期间按下的键不能同时被解析成游戏动作
            // （玩家为了把「跳跃」改成 K 而按的 K，不能顺手触发「踏云闪」）
            InputSystem.SetCaptureMode(true);
            RefreshSlots();
            UpdateFoot(CaptureHint);
        }

        private void CommitCapture(Key code)
        {
            CaptureState cap = _capture;

            if (cap == null)
            {
                return;
            }

            // 同一动作内部的重复要拦。**跨动作重复是合法的**（↑/W 同时是「向上」与「菜单上移」），
            // 与 InputSystem.ValidateBindings 的口径一致。
            Binding[] binds = InputSystem.GetBindings(cap.Action);
            Binding candidate = Binding.FromKey(code);

            for (int i = 0; i < binds.Length; i++)
            {
                if (i != cap.Index && InputActions.SameBinding(binds[i], candidate))
                {
                    UpdateFoot("该键已用在本动作的另一格，换个键或按 E 取消");
                    return;
                }
            }

            // 顺序要紧：先退捕获再落绑定。Rebind 会广播 settings-changed 并**同步**回调刷新，
            // 若那时还处在捕获态，刷新会把「按新键…」的显示冲掉。
            EndCapture();

            // ⚠️ **返回值要看**。Rebind 走的是 GameSettings.SetBinding，索引越界时
            // 它只 LogWarning 然后返回 false —— 若这里不看，表现就是「点了没反应、
            // 也没有任何反馈」（实测踩过：槽位索引被闭包 fix 之前恒为 2，全都越界）。
            // 所以失败时给出可见提示，别再让它静默。
            if (!InputSystem.Rebind(cap.Action, cap.Index, candidate))
            {
                UpdateFoot("改键失败（槽位越界），换个槽再试");
            }

            RefreshSlots();
        }

        /// <summary>
        /// 退出捕获模式。**必须与 <see cref="BeginCapture"/> 成对**，否则动作层会永久失灵。
        ///
        /// 捕获结束的唯一出口，页脚的「按钮 ↔ 提示」二选一也挂在这里 ——
        /// 不然按 E 取消之后提示会一直占着位置、按钮回不来。
        /// </summary>
        private void EndCapture()
        {
            _capture = null;
            InputSystem.SetCaptureMode(false);
            UpdateFoot();
        }

        // ======================================================== 页脚

        private void BuildFoot(RectTransform page)
        {
            Text hint = ColumnLabel(page, "FootHint", "", FootHintSize, ColorWarn, 800f, TextAnchor.MiddleCenter);
            hint.rectTransform.anchoredPosition = new Vector2(0f, FootY);
            hint.gameObject.SetActive(false);
            _footHint = hint;

            _footBtn = MakeButton(page, 0f, FootY, BtnW, BtnH, "恢复默认", OnReset);
        }

        /// <summary>
        /// 按钮与改键提示二选一（共用 <c>FootY</c>，不占两行）。
        ///
        /// 用状态而不是定时器驱动：提示只在**捕获期间**出现，捕获一结束按钮就回来 ——
        /// 不需要更新循环，也不会有「面板关了计时器还在跑」的问题。
        /// </summary>
        private void UpdateFoot(string message = CaptureHint)
        {
            bool capturing = _capture != null;

            if (_footBtn != null)
            {
                _footBtn.SetActive(!capturing);
            }

            if (_footHint != null)
            {
                _footHint.gameObject.SetActive(capturing);

                if (capturing)
                {
                    _footHint.text = message;
                }
            }
        }

        private void OnReset()
        {
            // 捕获中不让点：这时玩家的注意力在「按哪个键」上，误点会把刚改的键位全清掉
            if (_capture != null)
            {
                return;
            }

            if (_tab == Tab.Audio)
            {
                // ⚠️ **两样都要复**。这一页有四行（主/背景/音效音量 + 画面亮度），
                // 而 AudioManager.ResetAudio 按名字只管三条音量 ——
                // 只调它的话亮度纹丝不动，玩家点「恢复默认」会以为按钮坏了
                // （实测反馈「恢复默认对亮度无效」）。
                //
                // 亮度不在 AudioManager 的范畴里（它不是音频），所以直接走 GameSettings ——
                // 上面 VolRows 表里那一行的 Get/Set 本来也就是这么接的。
                AudioManager.ResetAudio();
                GameSettings.ResetBrightness();
                RefreshSliders();
                return;
            }

            // 清掉覆盖项即可，读的时候自然回落到默认表
            InputSystem.ResetBindings();
            RefreshSlots();
        }

        // ======================================================== 零件

        /// <summary>
        /// 纯色横条。pivot 取 (anchorX, 0.5) —— <c>anchorX = 0</c> 时锚点在左端，
        /// 改宽度即向右生长。
        ///
        /// ⚠️ <b><paramref name="clickable"/> 默认 false，这是全项目的约定</b>：
        /// <see cref="UiDraw.MakeRect"/> 出来的 Graphic 一律 <c>raycastTarget = false</c>
        /// （视觉件默认不吃点击）。
        ///
        /// 要拿它当按钮 / 键位槽就**必须显式打开**，否则挂了 <c>EventTrigger</c> 也永远收不到
        /// 事件：整条链（横条 → 内层 → 文字）上没有一个能吃射线的 Graphic，
        /// 点下去连「谁被点到了」都不存在。**不报错、不警告、日志也没有**，
        /// 表现只是「点了没反应」—— 实测「恢复默认」按钮与键位槽都栽在这上面。
        /// </summary>
        private static Image MakeBar(RectTransform parent, string name, float w, float h,
                                     Color color, float anchorX, bool clickable = false)
        {
            Image img = UiDraw.MakeRect(parent, name, color, w, h);
            img.rectTransform.pivot = new Vector2(anchorX, 0.5f);
            img.raycastTarget = clickable;
            return img;
        }

        /// <summary>固定颜色的实心横线。</summary>
        private static Image MakeLine(RectTransform parent, string name, float w, float h, Color color)
        {
            return UiDraw.MakeRect(parent, name, color, w, h);
        }

        /// <summary>
        /// 定宽文本。<see cref="UiDraw.MakeText"/> 出的是「节点贴着文字」的居中文本 ——
        /// 排成列时每行字数不同、左边会参差不齐；要排成表格就得自己定宽 + 设对齐。
        /// </summary>
        private static Text ColumnLabel(RectTransform parent, string name, string text, int size,
                                        Color color, float width, TextAnchor align)
        {
            Text label = UiDraw.MakeText(parent, name, text, size, color, 0f);
            label.alignment = align;
            label.rectTransform.sizeDelta = new Vector2(width, Mathf.Round(size * 1.4f));

            // pivot 跟着对齐走：左对齐取左端、右对齐取右端
            label.rectTransform.pivot = new Vector2(
                align == TextAnchor.MiddleRight ? 1f : (align == TextAnchor.MiddleCenter ? 0.5f : 0f),
                0.5f);

            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }

        /// <summary>双层条做的按钮：外层亮边 + 内层深墨底 + 浅色字。</summary>
        private GameObject MakeButton(RectTransform parent, float x, float y, float w, float h,
                                      string text, Action onClick)
        {
            // 只有外层（edge）吃点击 —— 它才是挂 EventTrigger 的那个
            Image edge = MakeBar(parent, "ButtonEdge", w + 4f, h + 4f, ColorBtnEdge, 0.5f, clickable: true);
            edge.rectTransform.anchoredPosition = new Vector2(x, y);

            Image ink = MakeBar(edge.rectTransform, "ButtonInk", w, h, ColorBtnInk, 0.5f);
            ink.rectTransform.anchoredPosition = Vector2.zero;

            _deps.MakeLabel(ink.rectTransform, "Label", text, BtnTextSize, ColorBtnText, 0f);

            EventTrigger trigger = edge.gameObject.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerEnter, _ => ink.color = ColorBtnHover);
            AddTrigger(trigger, EventTriggerType.PointerExit, _ => ink.color = ColorBtnInk);
            AddTrigger(trigger, EventTriggerType.PointerClick, _ => onClick());

            return edge.gameObject;
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type,
                                       UnityEngine.Events.UnityAction<BaseEventData> action)
        {
            EventTrigger.Entry entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(action);
            trigger.triggers.Add(entry);
        }

        // ======================================================== 数据自检

        /// <summary>
        /// 键位页的分组是手写的（顺序即版面顺序），这里盯两眼漏：
        /// <list type="number">
        ///   <item>显示表 + 隐藏表合起来必须覆盖全部动作 —— <see cref="InputActions"/> 里
        ///     加了新动作却忘了归类的话，表现是「设置页里根本没有这一项」，不报错、很难发现。</item>
        ///   <item>动作的绑定数不能超过版面的槽位列数 —— 超了会被静默吃掉一条绑定。</item>
        /// </list>
        /// </summary>
        private static void CheckActionGroups()
        {
            foreach (GameAction action in InputActions.AllActions)
            {
                if (Array.IndexOf(GroupGame, action) < 0 && Array.IndexOf(GroupHidden, action) < 0)
                {
                    Debug.LogError($"[SettingsPanel] 动作「{action}」没有归类，设置页里会看不到它");
                }
            }

            foreach (GameAction action in GroupGame)
            {
                int count = InputSystem.GetBindings(action).Length;

                if (count > SlotX.Length)
                {
                    Debug.LogError($"[SettingsPanel] 动作「{action}」有 {count} 个绑定，" +
                                   $"超过了版面的 {SlotX.Length} 个槽位，多的会被吃掉");
                }
            }
        }

        // ======================================================== 回调

        private void OnSettingsChanged(SettingsChangedEvent e)
        {
            if (_disposed)
            {
                return;
            }

            if (e.Audio)
            {
                RefreshSliders();
            }

            if (e.Bindings)
            {
                RefreshSlots();
            }
        }
    }
}
