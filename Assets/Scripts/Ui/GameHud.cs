using UnityEngine;
using UnityEngine.UI;

namespace CloudEyrie
{
    /// <summary>
    /// 关卡内 HUD 的**灵炁条**（美术组 2026-10-07 新稿）。
    ///
    /// <b>构成</b>：云纹头冠 + 石质横条，横条里嵌一条青玉色的填充。
    /// 头冠里嵌**太极双鱼**图标；灵炁归零时头冠换回暗版（`…/bar_spirit_track`）。
    ///
    /// <code>
    /// bar_spirit_track   718×211  底：石条 + **暗**头冠
    /// bar_spirit_head_lit 718×211  底：石条 + **亮**头冠（灵炁非空时用它，二选一）
    /// bar_spirit_fill    446×28   填充：青玉条，嵌在石条的横条区间里
    /// bar_spirit_icon    164×164  头冠圆心里的太极双鱼
    /// </code>
    ///
    /// ⚠️ <b>`head_lit` 是整块的替代版，不是叠加层</b> —— 它同样含整条石条，
    /// 叠在 `track` 上面会把填充条整个盖住。所以两者**二选一**（按灵炁是否为空切 sprite）。
    ///
    /// <b>几何是量出来的</b>（在 `bar_spirit_track.png` 内按 alpha 通道量）：
    /// 头冠 x[0,208]、圆心 y≈110（自图顶）；横条区间 x[209,714]、y[75,184]。
    /// 填充条就是塞进那个横条区间里的。
    ///
    /// <b>锚点</b>：整组锚在**屏幕左下角**（锚点与 pivot 都在 (0,0)）。
    /// 上一轮踩过一次坑 —— 锚画布中心的话，超宽屏上画布变矮、整条会跑出屏幕。
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        // ---- 版面（画布单位；画布参考 1920×1080，所以这些数就是 1920×1080 设计稿上的像素）----

        /// <summary>
        /// 整组相对屏幕左下角的偏移。
        ///
        /// ⚠️ 这两个数是 <c>tools/ui_slice_locator.py</c> 在设计稿里**匹配切图**算出来的
        /// （设计稿 1920×1080 与画布参考同一套单位）：亮头冠那条状态栏落在 (27, 861)，
        /// 整组高 211，所以底边距屏幕底 = 1080 − (861 + 211) = 8。
        ///
        /// 上一轮是 (38, 57)，肉眼量偏了 —— 整组因此歪了 11px 右、49px 上。
        /// 设计稿里那条**带「空状态栏」批注箭头的**是美术画的空状态对照稿，不是版面位置，别拿它当基准。
        /// </summary>
        private const float GroupX = 27f;
        private const float GroupBottom = 8f;

        /// <summary>整组尺寸（= 切图原生尺寸）</summary>
        private const float GroupW = 718f;
        private const float GroupH = 211f;

        /// <summary>
        /// 横条区间的竖直中心（自整组底边量）。
        ///
        /// 由填充条的落位反推：设计稿里 <c>bar_spirit_fill</c> 落在 (258, 971)，高 28，
        /// 中心在设计稿 y = 985；整组底边在设计稿 y = 1072，故 1072 − 985 = 87。
        /// （设计稿上灵炁是**没充满**的，所以只能取填充条的**左缘**当地条区间的起点，
        /// 右端量不到 —— 见 <see cref="BarFillLeft"/>。）
        /// </summary>
        private const float BarCenterY = 87f;

        /// <summary>填充条的显示高度（= 切图原生高度）</summary>
        private const float BarFillH = 28f;

        /// <summary>
        /// 填充条的显示宽度 —— **就是切图原生宽度，不许改**。
        ///
        /// ⚠️ 灵炁条的填充**不能用「改 sizeDelta 宽度」来做**：那是把贴图横向拉伸，
        /// 条一长一短，里面的石纹/水纹就跟着变形（实测反馈「纹路在变化」）。
        ///
        /// 正确做法是 <see cref="Image.type"/> = <see cref="Image.Type.Filled"/> +
        /// <c>fillAmount</c>：贴图始终按原尺寸铺在整块 rect 上，只**裁出左边一段**显示。
        /// 长度变化时纹路完全不动，只是被露出来多少的差别。
        /// </summary>
        private const float BarFillW = 446f;

        /// <summary>
        /// 填充条的左缘（在整组内的本地坐标）。
        ///
        /// 设计稿里 <c>bar_spirit_fill</c> 落在 x = 258，整组左缘在 x = 27，故 258 − 27 = 231。
        /// 因为填充是从左往右裁的（fillAmount），这个左缘**就是**石条槽位的起点 ——
        /// 设计稿上灵炁没充满，右端量不出来，但槽位起点是准的。
        /// </summary>
        private const float BarFillLeft = 231f;

        /// <summary>
        /// 太极双鱼图标的位置（在整组内的本地坐标）。
        ///
        /// 设计稿里 <c>bar_spirit_icon</c> 落在 (60, 886)、边长 164，中心即 (142, 968)；
        /// 换算到整组（左下角为原点）：x = 142 − 27 = 115，y = 1072 − 968 = 104。
        /// 比头冠圆心（本地约 104, 101）略偏右下 —— 头冠是云纹不是正圆，以图标自身落位为准。
        /// </summary>
        private const float HeadCenterX = 115f;
        private const float HeadCenterY = 104f;

        /// <summary>太极双鱼图标的边长</summary>
        private const float IconSize = 164f;

        private Sprite _trackSprite;      // 暗头冠版
        private Sprite _litSprite;        // 亮头冠版
        private Sprite _fillSprite;
        private Sprite _iconSprite;

        private Image _head;
        private Image _fill;
        private Image _icon;

        /// <summary>上一帧的「头冠是否点亮」，只在变化时切 sprite（切 sprite 会重建渲染器）</summary>
        private bool _headLit;

        // ============================================================ 短提示
        //
        // 「按了没反应」的那一串拒绝分支（灵合冷却中 / 爆发本回合已用过 / 灵炁不足）
        // 原来只打 Debug.Log，玩家侧完全看不见 —— 实测因此困惑过两轮
        // （「条件满足却放不出爆发」「切回灵合要等很久，是时间限制吗」）。
        // 这里在灵炁条**上方**闪一行字，几秒后自己淡掉。
        //
        // 位置贴在条上方（条占了屏幕左下角），不挡战斗画面中心。

        /// <summary>提示的默认显示时长 /秒</summary>
        private const float HintSeconds = 2.6f;

        /// <summary>提示文字离灵炁条顶端多高（世界/设计像素）</summary>
        private const float HintY = 24f;

        private const int HintSize = 40;

        private Text _hint;
        private float _hintRemaining;

        /// <summary>提示的半透明衬底；没有提示时隐藏</summary>
        private Image _hintBand;
        private bool _hintSubscribed;

        private void OnEnable()
        {
            if (!_hintSubscribed)
            {
                _hintSubscribed = true;
                EventBus.On<HintEvent>(GameEvents.HintRequested, OnHint);
            }
        }

        private void OnDisable()
        {
            if (_hintSubscribed)
            {
                _hintSubscribed = false;
                EventBus.Off<HintEvent>(GameEvents.HintRequested, OnHint);
            }
        }

        private void OnHint(HintEvent e)
        {
            if (_hint == null || string.IsNullOrEmpty(e.Text))
            {
                return;
            }

            _hint.text = e.Text;
            _hintRemaining = e.Seconds > 0f ? e.Seconds : HintSeconds;
            _hint.color = HintColor;

            // 衬底只在有提示时出现 —— 常驻会在屏幕上留一条灰带（实测踩过）
            if (_hintBand != null)
            {
                _hintBand.enabled = true;
            }
        }

        /// <summary>建那条提示文本（挂在灵炁条整组上方）。</summary>
        private void BuildHint(Transform root)
        {
            RectTransform n = UiDraw.MakeNode(root, "Hint");

            // 屏幕**正下方居中**：原来贴在左下角，和左下角的灵炁条挤在一起，
            // 又小又偏，实际游戏里几乎注意不到。
            n.anchorMin = new Vector2(0.5f, 0f);
            n.anchorMax = new Vector2(0.5f, 0f);
            n.pivot = new Vector2(0.5f, 0f);
            n.anchoredPosition = new Vector2(0f, 148f);
            n.sizeDelta = new Vector2(1400f, HintSize * 2.2f);

            // 半透明深色衬底：提示字在浅色山水上容易糊掉，靠描边不够
            Image band = UiDraw.MakeRect(n, "Band", new Color32(18, 22, 26, 150),
                                         1400f, HintSize * 2.0f);
            _hintBand = band;
            band.enabled = false;

            RectTransform brt = band.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = Vector2.zero;

            _hint = UiDraw.MakeText(n, "Label", "", HintSize, HintColor,
                                    ScreenStage.ThinOutline);
            _hint.rectTransform.sizeDelta = new Vector2(1360f, HintSize * 1.8f);
            _hint.rectTransform.anchorMin = _hint.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _hint.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _hint.rectTransform.anchoredPosition = Vector2.zero;
            _hint.alignment = TextAnchor.MiddleCenter;
            _hint.text = string.Empty;
        }

        private static readonly Color HintColor = new Color32(255, 236, 200, 255);

        private void Awake()
        {
            Canvas canvas = UiDraw.EnsureCanvas(transform);

            _trackSprite = Resources.Load<Sprite>("ui/bar_spirit_track");
            _litSprite = Resources.Load<Sprite>("ui/bar_spirit_head_lit");
            _fillSprite = Resources.Load<Sprite>("ui/bar_spirit_fill");
            _iconSprite = Resources.Load<Sprite>("ui/bar_spirit_icon");

            if (_trackSprite == null || _litSprite == null || _fillSprite == null || _iconSprite == null)
            {
                Debug.LogWarning("[GameHud] ui/bar_spirit_* 没读全，灵炁条会显示不全");
            }

            Build(canvas.transform);
            BuildHint(canvas.transform);
        }

        private void Build(Transform root)
        {
            // 整组：锚屏幕左下角，pivot 也在左下，尺寸 = 切图原生尺寸
            RectTransform group = UiDraw.MakeNode(root, "SpiritBar");
            group.anchorMin = group.anchorMax = Vector2.zero;
            group.pivot = Vector2.zero;
            group.sizeDelta = new Vector2(GroupW, GroupH);
            group.anchoredPosition = new Vector2(GroupX, GroupBottom);

            // 底：石条 + 头冠（暗/亮二选一，每帧按灵炁切）
            //
            // ⚠️ pivot 必须是 (0,0)，**不能给 (0.5,0.5)**。
            // 子节点的锚点被 MakeChild 统一设成 (0,0)（= 整组左下角），
            // 所以 pivot 给中心、anchoredPosition 又给 (0,0) 的话，
            // 整块图是**以整组左下角为中心**摆的 —— 往左下偏出半个身位（359×105.5），
            // 表现为花瓣头冠整块跑到屏幕左外、石条跑到屏幕下外，
            // 只剩太极图和青玉填充条可见（实测：世界左下 (−332,−98)，应为 (27,8)）。
            //
            // ⚠️ **不要**改成「底面永远铺暗版、亮头冠单独盖上去」。试过一版：
            // 那样石条是墨黑的，和左边那颗浅色的花瓣头冠**接不上**，看着像被切成两种材质
            // （反馈原话「和左边双鱼连接的那个条也应该是一致的浅色」）。
            // 亮版整图（浅头冠 + 灰白石条）本来就是一体的，二选一才是对的做法。
            _head = MakeChild(group, "Head", _trackSprite, Vector2.zero,
                              new Vector2(GroupW, GroupH), Vector2.zero);

            // 填充：青玉条，**嵌在石条的横条区间里**。
            // rect 固定成切图原生尺寸，长度靠 Filled 裁左边 —— 不拉伸（见 BarFillW）
            _fill = MakeChild(group, "Fill", _fillSprite,
                              new Vector2(BarFillLeft, BarCenterY),
                              new Vector2(BarFillW, BarFillH),
                              new Vector2(0f, 0.5f));

            if (_fill != null)
            {
                _fill.type = Image.Type.Filled;
                _fill.fillMethod = Image.FillMethod.Horizontal;
                _fill.fillOrigin = (int)Image.OriginHorizontal.Left;
                _fill.fillAmount = 1f;
            }

            // 图标：太极双鱼，压在头冠圆心
            _icon = MakeChild(group, "Icon", _iconSprite,
                              new Vector2(HeadCenterX, HeadCenterY),
                              new Vector2(IconSize, IconSize),
                              new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// 建一张挂在整组里的图。
        /// ⚠️ 尺寸一律用 <c>sizeDelta</c> 显式给 —— 不要让它停在贴图自带尺寸上
        /// （uGUI 的显示尺寸 = sizeDelta × localScale）。
        /// </summary>
        private static Image MakeChild(RectTransform parent, string name, Sprite sprite,
                                       Vector2 pos, Vector2 size, Vector2 pivot)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = Color.white;
            img.raycastTarget = false;
            return img;
        }

        private void Update()
        {
            float ratio = Mathf.Clamp01(SpiritEnergySystem.GetRatio());

            // 填充：改 fillAmount（**不是改 width**）—— 见 BarFillW 的说明
            if (_fill != null)
            {
                _fill.fillAmount = ratio;
            }

            // 头冠：有灵炁才点亮。只在状态翻转时换 sprite —— 每帧赋值会触发渲染器重建
            bool lit = ratio > 0.001f;

            if (lit != _headLit && _head != null)
            {
                _headLit = lit;
                _head.sprite = lit ? _litSprite : _trackSprite;
            }

            // 短提示倒计时 + 淡出（用 unscaled —— 演出里 timeScale=0 时提示也该正常消失）
            if (_hintRemaining > 0f && _hint != null)
            {
                _hintRemaining -= Time.unscaledDeltaTime;

                // 最后 0.5 秒淡出
                Color c = HintColor;
                c.a = HintColor.a * Mathf.Clamp01(_hintRemaining / 0.5f);
                _hint.color = c;

                if (_hintRemaining <= 0f)
                {
                    _hint.text = string.Empty;

                    if (_hintBand != null)
                    {
                        _hintBand.enabled = false;
                    }
                }
            }
        }
    }
}
