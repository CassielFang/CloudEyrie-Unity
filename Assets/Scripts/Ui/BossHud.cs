using UnityEngine;
using UnityEngine.UI;

namespace CloudEyrie
{
    /// <summary>
    /// Boss 阶段指示器（美术组 2026-10-07 新稿）：顶部一条横贯的水墨血条 + 下方一行状态。
    ///
    /// <b>构成</b>：
    /// <code>
    /// bar_boss_track  1791×95  墨黑空底
    /// bar_boss_fill   1779×95  青玉填充（受伤后露出黑底）
    /// </code>
    /// 两张切图的内容区间完全重合（按 alpha 量都是 x[22,1767] y[11,82]），
    /// 所以填充只要用同一块 rect + <see cref="Image.type"/> = Filled 裁左边即可，不用另算偏移。
    ///
    /// <b>与旧版的两处取舍</b>
    /// <list type="bullet">
    ///   <item><b>去掉了 70%/35% 的两根阶段刻度线</b> —— 设计稿的血条是一整条石条，没有刻度。
    ///     阶段信息改由下面那行状态文字承担（原本两边就重复表达同一件事）。</item>
    ///   <item><b>名字与状态文字保留</b> —— 新美术没有出文字部件，这两行仍用项目字体渲染，
    ///     压在血条正下方。设计稿上没有，但删掉会丢掉 Boss 名字与「狐火 N」这两个信息。</item>
    /// </list>
    ///
    /// 位置由 <c>tools/ui_slice_locator.py</c> 在设计稿里匹配切图算出：
    /// 血条水平居中（内容 x[91,1837]，中心 964 ≈ 屏宽中心 960），整图落在 y = 19，中心距屏顶 66.5。
    /// </summary>
    public class BossHud : MonoBehaviour
    {
        /// <summary>
        /// 血条中心距屏幕顶边的距离。
        ///
        /// ⚠️ 由 <c>tools/ui_slice_locator.py</c> 在设计稿里匹配切图算出：满血的那条
        /// <c>bar_boss_fill</c> 落在 y = 19、高 95，中心即 66.5。
        /// 上一轮是 245 —— 那是把设计稿里**带「空血条」批注箭头的空状态对照稿**
        /// （落在 y = 184）当成了版面位置，整条因此低了 178px。
        /// 设计稿的约定是：没批注的那份才是真实位置。
        /// </summary>
        private const float BarTopOffset = 66.5f;

        /// <summary>空底尺寸（= 切图原生）</summary>
        private const float BarW = 1791f;

        /// <summary>填充尺寸（= 切图原生）。**不拉成和底一样宽** —— 拉伸会让纹路随血量变形</summary>
        private const float FillW = 1779f;

        /// <summary>填充相对底图要右移多少才能内容对齐（量出来的，别改成 0）</summary>
        private const float FillAlignX = 6f;

        private const float BarH = 95f;

        /// <summary>
        /// 两行文字距屏幕顶边的距离（压在血条下方）。
        ///
        /// 设计稿里没有文字部件，这两行的位置是**跟着血条走的**：
        /// 血条从 245 上移到 66.5（−178.5），文字同步减掉同样的量，
        /// 保持原来与血条底边的间距（名字 27.5px、状态 63.5px）。
        /// </summary>
        private const float NameTopOffset = 141.5f;
        private const float StateTopOffset = 177.5f;

        private const int NameSize = 33;
        private const int StateSize = 27;

        [Tooltip("Boss 节点。留空则运行时按类型查找")]
        [SerializeField] private BossAI boss;

        private Sprite _trackSprite;
        private Sprite _fillSprite;

        private Image _fill;
        private Text _nameText;
        private Text _stateText;

        private readonly System.Collections.Generic.List<GameObject> _parts =
            new System.Collections.Generic.List<GameObject>();

        private void Awake()
        {
            Canvas canvas = UiDraw.EnsureCanvas(transform);

            _trackSprite = Resources.Load<Sprite>("ui/bar_boss_track");
            _fillSprite = Resources.Load<Sprite>("ui/bar_boss_fill");

            if (_trackSprite == null || _fillSprite == null)
            {
                Debug.LogWarning("[BossHud] ui/bar_boss_* 没读到，Boss 血条会残缺");
            }

            Build(canvas.transform);
        }

        private void Build(Transform root)
        {
            // 空底（墨黑）
            MakeImage(root, "BossBarTrack", _trackSprite, BarTopOffset, BarW, BarH, Color.white, 0f);

            // 填充（青玉）：
            // ⚠️ 用**切图原生尺寸**（1779×95），**不要**拉成和底一样宽 —— 拉了就「纹路随血量变化」。
            // 长度变化靠 Filled 裁左边（见下），贴图本身一直按原尺寸贴着，纹路不动。
            //
            // 两张图的内容区间都是 x[22,1767]，但底图 1791 宽、填充 1779 宽 ——
            // 内容在各自的图里偏心量不同（底左右各 22/23，填充 22/12），
            // 所以填充要**右移 6px**才能和底对齐。这个 6 是量出来的，别改成 0。
            _fill = MakeImage(root, "BossBarFill", _fillSprite, BarTopOffset,
                              FillW, BarH, Color.white, FillAlignX);

            if (_fill != null)
            {
                _fill.type = Image.Type.Filled;
                _fill.fillMethod = Image.FillMethod.Horizontal;
                _fill.fillOrigin = (int)Image.OriginHorizontal.Left;
                _fill.fillAmount = 1f;
            }

            _nameText = MakeLabel(root, "BossName", NameTopOffset, NameSize,
                new Color32(240, 230, 210, 255), "九尾灵狐 · 青丘遗灵");

            _stateText = MakeLabel(root, "BossState", StateTopOffset, StateSize,
                new Color32(180, 200, 215, 255), "");
        }

        /// <summary>
        /// 建一张锚在**屏幕顶边**的图。
        /// ⚠️ 锚顶边而不是画布中心 —— 画布是「匹配宽度」缩放的，超宽屏上画布会变矮，
        /// 锚中心的固定 y 会被顶出屏幕（上一轮灵炁条就是栽在这上面）。
        /// </summary>
        private Image MakeImage(Transform parent, string name, Sprite sprite,
                                float topOffset, float w, float h, Color color, float x)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -topOffset);

            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;

            _parts.Add(go);
            return img;
        }

        private Text MakeLabel(Transform parent, string name, float topOffset, int size,
                               Color color, string content)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1400f, size + 8f);
            rt.anchoredPosition = new Vector2(0f, -topOffset);

            Text t = go.GetComponent<Text>();
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;

            Font font = UiDraw.GameFont;

            if (font != null)
            {
                t.font = font;
            }

            _parts.Add(go);
            return t;
        }

        /// <summary>懒查找：Boss 可能比 HUD 晚挂上来。</summary>
        private BossAI ResolveBoss()
        {
            if (boss != null)
            {
                return boss;
            }

            boss = FindAnyObjectByType<BossAI>();
            return boss;
        }

        private void Update()
        {
            BossAI b = ResolveBoss();

            // 没进 Boss 战场之前整组不显示 —— 探索段顶着一条 Boss 血条很出戏
            bool engaged = b != null && b.IsEngaged();

            foreach (GameObject n in _parts)
            {
                if (n != null)
                {
                    n.SetActive(engaged);
                }
            }

            if (!engaged)
            {
                return;
            }

            Damageable dmg = b.GetDamageable();

            if (dmg == null)
            {
                if (_stateText != null)
                {
                    _stateText.text = "";
                }

                return;
            }

            if (_fill != null)
            {
                _fill.fillAmount = dmg.GetHpRatio();
            }

            float hp = Mathf.Max(0f, Mathf.Round(dmg.GetHp()));
            float max = Mathf.Round(dmg.GetMaxHp());
            string phase = PhaseLabel(b.GetPhase());

            // 狐火数量是 P1/P2 唯一需要玩家读的战场信息（它们会围上来）
            int fogFire = b.AliveFogFireCount;
            string extra = fogFire > 0 ? $"　　狐火 {fogFire}" : "";

            if (_stateText != null)
            {
                _stateText.text = $"{hp} / {max}　　{phase}{extra}";
            }
        }

        /// <summary>
        /// 阶段名。
        ///
        /// ⚠️ 用**设计文档里的正式名字**（幻影 / 狂乱 / 归心），不要用代码里的枚举名
        /// （P1 / P2 / P3）—— 团队 2026-10-07 明确「玩家看不懂 P1」。
        /// </summary>
        private static string PhaseLabel(BossPhase phase)
        {
            switch (phase)
            {
                case BossPhase.P1: return "幻影";
                case BossPhase.TransitionP2: return "转场";
                case BossPhase.P2: return "狂乱";
                case BossPhase.TransitionP3: return "转场";
                case BossPhase.P3: return "归心";
                case BossPhase.Lament: return "哀鸣";
                case BossPhase.Defeated: return "已净化";
                default: return "?";
            }
        }
    }
}
