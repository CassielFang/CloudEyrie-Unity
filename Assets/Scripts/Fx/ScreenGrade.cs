using UnityEngine;
using UnityEngine.UI;

namespace CloudEyrie.Fx
{
    /// <summary>
    /// 全屏色调（"滤镜"）：一层低 alpha 的纯色盖在世界之上、HUD 之下。
    ///
    /// 为什么用一层盖色而不是逐个给精灵染色：五境要的是**整体氛围差异**
    /// （木境青绿、火境暖橙、金境冷灰…）。逐个染色要改每一张图、每一个道具，
    /// 漏一个就出戏；一层盖色是所有东西一起偏移，天然统一。
    ///
    /// 代价是高光会被一起压掉，所以 alpha 控制在 0.18 以内，并且乘在
    /// Canvas 的 Screen Space - Overlay 上（不参与世界光照，不会二次衰减）。
    /// </summary>
    public class ScreenGrade : MonoBehaviour
    {
        private static ScreenGrade _instance;

        [Tooltip("最大覆盖强度。超过 0.2 就会明显压掉画面层次")]
        [SerializeField] private float maxAlpha = 0.10f;

        private Image _image;

        public static ScreenGrade Instance { get { return _instance; } }

        private void Awake()
        {
            _instance = this;
            EnsureBuilt();
        }

        private void EnsureBuilt()
        {
            if (_image != null)
            {
                return;
            }

            var canvasGo = new GameObject("GradeCanvas");
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -1;         // 世界之上、HUD（默认 0）之下

            canvasGo.AddComponent<CanvasScaler>();

            var imageGo = new GameObject("Grade");
            imageGo.transform.SetParent(canvasGo.transform, false);

            var rect = imageGo.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _image = imageGo.AddComponent<Image>();
            _image.raycastTarget = false;
            _image.color = new Color(1f, 1f, 1f, 0f);
        }

        /// <summary>按主题上色。density 0 = 完全透明。</summary>
        public void Apply(Color tint, Color fogColor, float density)
        {
            EnsureBuilt();

            Color c = Color.Lerp(tint, fogColor, Mathf.Clamp01(density));
            c.a = Mathf.Clamp01(maxAlpha * Mathf.Clamp01(density));

            _image.color = c;
        }

        public void Clear()
        {
            EnsureBuilt();
            _image.color = new Color(1f, 1f, 1f, 0f);
        }
    }
}
