using UnityEngine;
using UnityEngine.UI;

namespace CloudEyrie
{
    /// <summary>
    /// 收集品显示：碎片计数 + 五行印记。
    ///
    /// 设计稿：**印记在 HUD 左上以五行图标点亮显示，集齐 5 枚解锁中央祭坛**。
    /// 本组件把这两样放在右上角（左上已被灵炁条占着）。
    ///
    /// <b>为什么不挂在 GameHud 上</b>
    ///
    /// 灵炁条是"每帧都在变"的实时读数，而收集品是"偶尔变一次"的进度显示 ——
    /// 两者的刷新频率差几个数量级。合在一起的话，每帧刷新的那个会顺手把
    /// 进度也刷一遍（白做功），而分开之后各自只在自己变化时更新。
    ///
    /// <b>为什么整组挂 <see cref="ScreenFixed"/></b>
    ///
    /// 相机是直接移动 <c>Canvas/Camera</c> 的（见 <c>CameraFollow</c>），
    /// HUD 会被一起推走 —— 屏幕固定的东西都要反向补偿，这是本工程的既有约定。
    /// </summary>
    public class CollectHud : MonoBehaviour
    {
        private const int TotalFragments = 10;

        /// <summary>五行的显示色，与设计稿的五行主题色一致。</summary>
        private static readonly (ElementKind kind, string name, Color color)[] Seals =
        {
            (ElementKind.Mu,   "木", new Color32(110, 200, 138, 255)),
            (ElementKind.Huo,  "火", new Color32(232, 120, 92,  255)),
            (ElementKind.Tu,   "土", new Color32(216, 176, 106, 255)),
            (ElementKind.Jin,  "金", new Color32(212, 220, 232, 255)),
            (ElementKind.Shui, "水", new Color32(106, 184, 216, 255)),
        };

        /// <summary>未点亮时的印记颜色（压暗，看得出"还没拿到"）。</summary>
        private static readonly Color DimSeal = new Color32(70, 82, 92, 200);

        private const float DotSize = 34f;
        private const float DotGap = 10f;

        private Text _fragmentText;
        private Image[] _sealDots;
        private Text[] _sealLabels;

        private void Start()
        {
            Build();
            Refresh();
        }

        private void OnEnable()
        {
            EventBus.On<object>(GameEvents.FragmentCollected, OnFragment);
            EventBus.On<object>(GameEvents.SealGranted, OnSeal);
        }

        private void OnDisable()
        {
            EventBus.Off<object>(GameEvents.FragmentCollected, OnFragment);
            EventBus.Off<object>(GameEvents.SealGranted, OnSeal);
        }

        private void OnFragment(object _) { Refresh(); }
        private void OnSeal(object _) { Refresh(); }

        private void Build()
        {
            Canvas canvas = UiDraw.EnsureCanvas(transform);

            RectTransform root = UiDraw.MakeNode(canvas.transform, "CollectHud");

            // 屏幕固定：相机移动 Canvas 时反向补偿（本工程的既有约定）
            root.gameObject.AddComponent<ScreenFixed>();

            // 右上角。锚到右上，留出边距
            root.anchorMin = new Vector2(1f, 1f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(1f, 1f);
            root.anchoredPosition = new Vector2(-40f, -36f);
            root.sizeDelta = new Vector2(420f, 90f);

            // 碎片计数
            _fragmentText = UiDraw.MakeText(root, "Fragments", "", 34,
                new Color32(238, 244, 248, 255), 2f);

            RectTransform frt = _fragmentText.rectTransform;
            frt.anchorMin = frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(1f, 1f);
            frt.anchoredPosition = new Vector2(0f, 0f);
            _fragmentText.alignment = TextAnchor.MiddleRight;

            // 五行印记：五个小方块 + 字
            _sealDots = new Image[Seals.Length];
            _sealLabels = new Text[Seals.Length];

            float totalW = Seals.Length * DotSize + (Seals.Length - 1) * DotGap;

            for (int i = 0; i < Seals.Length; i++)
            {
                float x = -totalW + i * (DotSize + DotGap);

                Image dot = UiDraw.MakeRect(root, $"Seal_{Seals[i].name}",
                    DimSeal, DotSize, DotSize);

                RectTransform drt = dot.rectTransform;
                drt.anchorMin = drt.anchorMax = new Vector2(1f, 1f);
                drt.pivot = new Vector2(0f, 1f);
                drt.anchoredPosition = new Vector2(x, -44f);

                _sealDots[i] = dot;

                Text label = UiDraw.MakeText(root, $"SealLabel_{Seals[i].name}",
                    Seals[i].name, 20, new Color32(228, 236, 244, 220), 1.5f);

                RectTransform lrt = label.rectTransform;
                lrt.anchorMin = lrt.anchorMax = new Vector2(1f, 1f);
                lrt.pivot = new Vector2(0.5f, 1f);
                lrt.anchoredPosition = new Vector2(x + DotSize * 0.5f, -74f);
                lrt.sizeDelta = new Vector2(60f, 22f);

                _sealLabels[i] = label;
            }
        }

        private void Refresh()
        {
            if (_fragmentText == null)
            {
                return;
            }

            _fragmentText.text = $"碎片 {PlayerProgress.FragmentCount}/{TotalFragments}";

            for (int i = 0; i < Seals.Length; i++)
            {
                bool has = PlayerProgress.HasSeal(Seals[i].kind);

                if (_sealDots[i] != null)
                {
                    _sealDots[i].color = has ? Seals[i].color : DimSeal;
                }

                if (_sealLabels[i] != null)
                {
                    _sealLabels[i].color = has
                        ? new Color32(255, 250, 236, 255)
                        : new Color32(140, 152, 162, 200);
                }
            }
        }
    }
}
