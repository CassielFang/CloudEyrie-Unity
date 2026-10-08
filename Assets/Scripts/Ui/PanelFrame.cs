using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CloudEyrie
{
    /// <summary>
    /// 首页三个全屏子页（设置 / 成就 / 制作组）共用的外壳。
    ///
    /// 三页长得一样：**底图铺满 + 页首一条整屏横线 + 左上返回箭头 + 标题**，
    /// 差别只在标题下面那块内容。所以把这一层抽出来一份，
    /// 免得三处各写一遍底图、各写一遍箭头的坐标。
    ///
    /// <b>几何全部量自</b>《美术素材/弹窗-设置-音画设置.png》（1920×1080）：
    /// 顶线 <c>line/yhsz</c> 落在 (0,146)、返回箭头 <c>btn/left</c> 落在 (95,40) 72×72、
    /// 页签文字中心在 y=78 那一带。换算到画布坐标（原点中心、y 向上）：
    /// <c>cx = 设计稿 x − 960</c>、<c>cy = 540 − 设计稿 y</c>。
    ///
    /// <b>为什么要有</b> <see cref="MakeBackdrop"/> <b>这层不透明底</b>：
    /// <c>settings_bg</c> 自身是半透明的（alpha 204~240）—— 设计稿里它是个「弹窗」，
    /// 本来就该透出后面的**游戏画面**。但这三页都是从**首页**开的，
    /// 透出来的是徽记和四项菜单，叠上去一片乱。垫一层同色不透明块之后观感几乎不变
    /// （底图本身就是一层均匀的深青墨绿滤镜），但把后面挡住了。
    /// </summary>
    public static class PanelFrame
    {
        // ---- 页首横线 ----
        public const float RuleY = 392f;
        public const float RuleW = 1920f;
        public const float RuleH = 5f;

        // ---- 返回箭头（切图原生 72×72）----
        public const float BackX = -829f;
        public const float ArrowY = 464f;
        public const float ArrowSize = 72f;

        /// <summary>单标题的页（成就 / 制作组）标题中心</summary>
        public const float TitleY = 462f;
        public const int TitleSize = 44;

        /// <summary>两个页签的中心 x（设置页用）</summary>
        public const float TabX0 = -380f;
        public const float TabX1 = 401f;

        /// <summary>
        /// 页脚那行「按 E 返回」。
        ///
        /// 摆在设置页「恢复默认」按钮**下面**（按钮在 −470、高 38，即占到 −489），
        /// 所以这里给 −510，留 21px 缝。
        /// </summary>
        public const float ExitHintY = -510f;
        public const int ExitHintSize = 24;

        public static readonly Color BackdropColor = new Color32(35, 47, 45, 255);

        /// <summary>横线的染色。切图是白的，压淡一点免得抢戏</summary>
        public static readonly Color RuleTint = new Color32(214, 232, 234, 150);

        public static readonly Color TextColor = new Color32(226, 240, 242, 255);
        public static readonly Color DimColor = new Color32(126, 150, 156, 255);

        /// <summary>
        /// 铺底：不透明色块 + <c>settings_bg</c>。返回底图节点，
        /// 调用方把它当 <c>_backing</c>（命中判定的边界）。
        /// </summary>
        public static RectTransform MakeBackdrop(RectTransform body)
        {
            UiDraw.MakeFill(body, "Backing", BackdropColor);

            Image bg = UiDraw.MakeImage(body, "Bg",
                Resources.Load<Sprite>("ui/settings_bg"), Color.white);

            if (bg == null || bg.sprite == null)
            {
                Debug.LogWarning("[PanelFrame] 读不到 ui/settings_bg，全屏页会没有底图");
                return null;
            }

            // ⚠️ 拉满父节点而不是给固定尺寸：全屏底要跟着画布缩放，
            // 否则宽高比一变就盖不满
            UiDraw.Stretch(bg.rectTransform);
            return bg.rectTransform;
        }

        /// <summary>
        /// 页脚那行「按 Esc 返回」。
        ///
        /// 三个全屏页共用。**别删** —— 鼠标玩家关面板只能靠页首那枚返回箭头，
        /// 但那枚箭头很容易被当成「上一页」而不是「关闭」；这行字把键位讲明白。
        ///
        /// ⚠️ 文案是**手写的**，没有从 <see cref="ScreenStage.PanelBackKey"/> 生成 ——
        /// 改返回键时这里要一起改（2026-10-07 由 E 改成 Esc 就是这么改的）。
        /// </summary>
        public static void MakeExitHint(RectTransform body)
        {
            RectTransform n = UiDraw.MakeNode(body, "ExitHint");
            n.anchoredPosition = new Vector2(0f, ExitHintY);

            Text t = UiDraw.MakeText(n, "Label", "按 Esc 返 回", ExitHintSize, DimColor,
                                     ScreenStage.ThinOutline);
            t.rectTransform.sizeDelta = new Vector2(600f, ExitHintSize * 1.6f);
        }

        /// <summary>页首那条整屏横线。</summary>
        public static Image MakeRule(RectTransform body)
        {
            Image rule = UiDraw.MakeImage(body, "TopRule",
                Resources.Load<Sprite>("ui/settings_line"), RuleTint);

            rule.rectTransform.sizeDelta = new Vector2(RuleW, RuleH);
            rule.rectTransform.anchoredPosition = new Vector2(0f, RuleY);
            return rule;
        }

        /// <summary>
        /// 页首的一枚箭头（返回 / 前进）。<paramref name="path"/> 取
        /// <c>ui/settings_arrow_l</c> 或 <c>ui/settings_arrow_r</c>。
        ///
        /// ⚠️ 设计稿在箭头旁各写了一枚键位提示（左「Q」右「E」），这里**不照抄** ——
        /// 项目的返回键是 <see cref="ScreenStage.PanelBackKey"/>（E），
        /// 照抄会出现「E 既写在右边箭头上、又真的是返回键」这种自相矛盾的提示。
        /// </summary>
        public static Image MakeArrow(RectTransform body, string path, float x, Action onClick)
        {
            RectTransform node = UiDraw.MakeNode(body, "Arrow");
            node.sizeDelta = new Vector2(ArrowSize, ArrowSize);
            node.anchoredPosition = new Vector2(x, ArrowY);

            // uGUI 的指针事件要求节点上有能命中射线的 Graphic
            Image hit = node.gameObject.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;

            Image art = UiDraw.MakeImage(node, "Art", Resources.Load<Sprite>(path), Color.white);

            if (art == null || art.sprite == null)
            {
                Debug.LogWarning($"[PanelFrame] 读不到 {path}，页首箭头会缺失");
            }
            else
            {
                art.rectTransform.sizeDelta = new Vector2(ArrowSize, ArrowSize);
            }

            if (onClick != null)
            {
                EventTrigger trigger = node.gameObject.AddComponent<EventTrigger>();
                EventTrigger.Entry entry = new EventTrigger.Entry
                {
                    eventID = EventTriggerType.PointerClick,
                };
                entry.callback.AddListener(_ => onClick());
                trigger.triggers.Add(entry);
            }

            return art;
        }
    }
}
