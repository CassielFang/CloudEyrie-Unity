using UnityEngine;
using UnityEngine.UI;

namespace CloudEyrie
{
    /// <summary>
    /// 程序化建 UI 的工具层。
    ///
    /// Cocos 那边 UI 全是代码搭的（`new Node()` + `addComponent(Sprite/Label/Graphics)`），
    /// 因为项目里没有 prefab，而且面板贴图不在 <c>resources/</c> 下、只能靠场景模板节点克隆。
    ///
    /// Unity 这边沿用同样的思路（保持与 Cocos 一一对应、便于对照），但把常用的几件事
    /// 抽到这里：建 Canvas、建纯色块、建文字、生成小图形。
    ///
    /// <b>字体走 Legacy <see cref="Text"/> + 动态 <see cref="Font"/></b>：
    /// Cocos 的 <c>Label.font</c> 就是这个模型，直接吃 <c>font.ttf</c>、不需要生成 SDF 图集。
    /// （TextMeshPro 要额外烘图集，而且你们这份字体缺「炁」字的问题在 TMP 下一样存在。）
    /// </summary>
    public static class UiDraw
    {
        /// <summary>
        /// 设计分辨率。**宽固定 1920**，高随屏幕变（<c>matchWidthOrHeight = 0</c>，即 fitWidth）。
        ///
        /// ⚠️ <b>2026-10-07 从 1280×720 改成 1920×1080。</b>
        /// 原因是美术组交的 UI 设计稿与切图**全部按 1920×1080 出图**。
        /// 用 1280 的话每处切图都要乘一个 2/3 的魔数；换成 1920 之后切图能
        /// **按原生像素直接摆**，以后接新 UI 也能直接丢进来。
        ///
        /// 代价：所有按 720 高写死的 UI 常量都要 ×1.5（画布单位从 720 高变成 1080 高）。
        /// 这次改动的范围见 <c>docs</c> 里的 UI 记录。
        /// </summary>
        public const float DesignWidth = 1920f;
        public const float DesignHeight = 1080f;

        private static Sprite _white;
        private static Font _font;
        private static bool _fontTried;

        /// <summary>1×1 纯白精灵，配合 <see cref="Image.color"/> 当任意颜色的方块/横线用。</summary>
        public static Sprite White
        {
            get
            {
                if (_white == null)
                {
                    Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    tex.SetPixel(0, 0, Color.white);
                    tex.Apply();
                    tex.name = "UiWhite";
                    _white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                    _white.name = "UiWhite";
                }

                return _white;
            }
        }

        /// <summary>
        /// 游戏字体。加载失败返回 null（<see cref="Text"/> 会落回系统字体 ——
        /// 中文仍能显示、只是字形不对）。
        /// </summary>
        public static Font GameFont
        {
            get
            {
                if (!_fontTried)
                {
                    _fontTried = true;
                    _font = Resources.Load<Font>("font");

                    if (_font == null)
                    {
                        Debug.LogWarning("[UiDraw] resources/font 加载失败，回退系统字体");
                    }
                }

                return _font;
            }
        }

        private static Font _systemCjk;
        private static bool _systemCjkTried;

        /// <summary>
        /// 从操作系统取一个中文字体。
        ///
        /// <b>什么时候需要它</b>：<c>font.ttf</c>（毛笔行楷）**缺一些字形**，
        /// 最典型的是「炁」(U+7081) —— 而「灵炁」是游戏的核心名词。
        /// 挂游戏字体时那个字会**静默掉字**（不是显示方框，是直接不见），
        /// 比字形不统一严重得多。
        ///
        /// 已知需要它的地方：制作组名单（「䴰」U+4D30）、HUD 的「灵炁」标签。
        /// 写新文案前先确认字形在不在，不在就走这里。
        ///
        /// ⚠️ <b>平台差异</b>：桌面平台（编辑器 / Windows / macOS / Linux）可用，
        /// 但 <b>WebGL 上不可用</b>（浏览器不给 Unity 枚举系统字体），会返回 null。
        ///
        /// **真正的解法是给 <c>font.ttf</c> 补上缺的字形**（或换一套完整字库），
        /// 那样这个方法整个可以删掉。
        /// </summary>
        public static Font SystemCjkFont
        {
            get
            {
                if (_systemCjkTried)
                {
                    return _systemCjk;
                }

                _systemCjkTried = true;

                string[] candidates =
                {
                    "Microsoft YaHei", "微软雅黑", "SimHei", "黑体",
                    "PingFang SC", "Hiragino Sans GB", "Noto Sans CJK SC", "Source Han Sans SC",
                };

                try
                {
                    _systemCjk = Font.CreateDynamicFontFromOSFont(candidates, 32);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[UiDraw] 取系统中文字体失败：{e.Message}");
                }

                if (_systemCjk == null)
                {
                    Debug.LogWarning("[UiDraw] 系统里没找到中文字体（WebGL 上属正常），" +
                                     "需要它的文案会掉字");
                }

                return _systemCjk;
            }
        }

        // ============================================================ 画布

        /// <summary>
        /// 确保 <paramref name="host"/> 下有一个按设计分辨率配置好的 Canvas。
        /// 已存在就直接复用。
        /// </summary>
        public static Canvas EnsureCanvas(Transform host)
        {
            Canvas existing = host.GetComponentInChildren<Canvas>(true);

            if (existing != null && existing.isRootCanvas)
            {
                return existing;
            }

            GameObject go = new GameObject("UiCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(host, false);

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(DesignWidth, DesignHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;     // 0 = 以宽为准（fitWidth，与 Cocos 的适配策略一致）

            return canvas;
        }

        // ============================================================ 基本控件

        /// <summary>建一个铺满父节点的纯色块（当背景、遮罩、蒙层用）。</summary>
        public static Image MakeFill(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Stretch(go.GetComponent<RectTransform>());

            Image img = go.GetComponent<Image>();
            img.sprite = White;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>建一个固定尺寸的方块（当横线、分隔线、小色块用）。</summary>
        public static Image MakeRect(Transform parent, string name, Color color, float w, float h)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);

            Image img = go.GetComponent<Image>();
            img.sprite = White;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// 建一段居中文本。描边走 Unity 的 <see cref="Outline"/> 效果 ——
        /// 背景是明暗不定的山水画，不加描边时浅色字会糊进云里（Cocos 那边同款理由）。
        /// </summary>
        public static Text MakeText(Transform parent, string name, string content, int size,
                                    Color color, float outline = 2f)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1200f, size * 1.6f);

            Text t = go.GetComponent<Text>();
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;

            Font font = GameFont;

            if (font != null)
            {
                t.font = font;
            }

            if (outline > 0f)
            {
                Outline o = go.AddComponent<Outline>();
                o.effectColor = StageColors.Outline;
                o.effectDistance = new Vector2(outline, -outline);
                o.useGraphicAlpha = true;
            }

            return t;
        }

        /// <summary>建一个 <see cref="Image"/> 节点（挂贴图用）。</summary>
        public static Image MakeImage(Transform parent, string name, Sprite sprite, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;

            if (sprite != null)
            {
                rt.sizeDelta = sprite.bounds.size;
            }

            return img;
        }

        // ============================================================ 小图形

        private static Sprite _diamond;

        /// <summary>
        /// 一枚菱形（标题与副标题之间的分隔符）。Cocos 那边用 <c>Graphics</c> 画，
        /// Unity 的 uGUI 没有等价物，这里生成一张小贴图缓存起来。
        /// </summary>
        public static Sprite Diamond
        {
            get
            {
                if (_diamond != null)
                {
                    return _diamond;
                }

                const int size = 19;
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                Color32[] px = new Color32[size * size];
                float c = (size - 1) * 0.5f;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        // |dx| + |dy| <= r 即菱形
                        float d = Mathf.Abs(x - c) + Mathf.Abs(y - c);
                        px[y * size + x] = d <= c ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                    }
                }

                tex.SetPixels32(px);
                tex.Apply();
                tex.name = "UiDiamond";
                _diamond = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 1f);
                _diamond.name = "UiDiamond";
                return _diamond;
            }
        }

        // ============================================================ 工具

        /// <summary>把 RectTransform 拉满父节点。</summary>
        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>建一个空的 UI 节点。</summary>
        public static RectTransform MakeNode(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }
    }

    /// <summary>首页 / 结束页共用的配色。对应 Cocos 的 <c>ScreenStage.ts</c> 顶部那组常量。</summary>
    public static class StageColors
    {
        public static readonly Color Title = new Color32(232, 248, 253, 255);
        public static readonly Color Sub = new Color32(152, 196, 212, 255);
        public static readonly Color Outline = new Color32(10, 26, 38, 200);
        public static readonly Color Glow = new Color32(150, 230, 245, 255);
        public static readonly Color Rule = new Color32(150, 210, 225, 200);
        public static readonly Color Diamond = new Color32(198, 242, 252, 240);

        /// <summary>宣纸面板上的墨字。</summary>
        public static readonly Color PanelInk = new Color32(46, 116, 132, 255);

        /// <summary>面板里的次要文字（「按 E 退出」、状态提示），比墨字淡一档。</summary>
        public static readonly Color PanelHint = new Color32(92, 124, 138, 235);
    }
}
