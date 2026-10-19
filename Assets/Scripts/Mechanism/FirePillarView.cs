using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 间歇火柱的表现：把原来的静态长条图换成**循环火焰动画**。
    ///
    /// 原来的 `fire_pillar.png` 是一条 378×793 的静态色块，喷起来像一块发光的板，
    /// 完全没有"火"的动感。这里在 Hazard 上叠一层 <see cref="SequencePlayer"/>，
    /// 播 `Resources/video_fx/火焰-火柱`，并把原图关掉。
    ///
    /// 尺寸：宽度跟碰撞盒走，高度按比例放大到碰撞盒高度的 1.05 倍（略高一点，
    /// 火焰顶端不该被硬切）。宽度还可以单独缩放，避免又变成"很宽的一块"。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class FirePillarView : MonoBehaviour
    {
        [Tooltip("动画片段名（Resources/video_fx 下的目录名）")]
        [SerializeField] private string clipName = "火焰-火柱";

        [Tooltip("火焰宽度相对碰撞盒宽度的倍数。0.9 左右比碰撞盒略窄，看起来才像一股火")]
        [SerializeField] private float widthScale = 0.9f;

        [Tooltip("火焰高度相对碰撞盒高度的倍数")]
        [SerializeField] private float heightScale = 1.05f;

        [Tooltip("排序层")]
        [SerializeField] private string sortingLayer = "Default";

        [Tooltip("排序序号。要压在地面之上、角色之后用 5 左右")]
        [SerializeField] private int sortingOrder = 5;

        private void Start()
        {
            var box = GetComponent<Collider2D>();

            if (box == null)
            {
                return;
            }

            // 关掉原来的静态图（根节点或子节点上的都关），否则两层叠在一起
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
            {
                sr.enabled = false;
            }

            var go = new GameObject("FlameArt");
            go.transform.SetParent(transform, false);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingLayerName = sortingLayer;
            renderer.sortingOrder = sortingOrder;
            _artRenderer = renderer;

            var player = go.AddComponent<SequencePlayer>();
            player.basePath = "video_fx";
            player.pixelsPerUnit = 1f;

            // 先播一次，尺寸等 manifest 异步加载完再摆（见 Update）
            player.Play(clipName, true);

            _box = box;
            _boxExact = box as BoxCollider2D;
            _art = go.transform;
            _canvasReady = false;
        }

        private Collider2D _box;
        private BoxCollider2D _boxExact;
        private Transform _art;
        private SpriteRenderer _artRenderer;
        private bool _canvasReady;

        /// <summary>拼接段（只用来同步显隐）</summary>
        private readonly System.Collections.Generic.List<SpriteRenderer> _segments =
            new System.Collections.Generic.List<SpriteRenderer>();

        /// <summary>
        /// 精灵**内容**底边在画布里的位置（0 = 贴画布底、1 = 顶）。只扫一次。
        ///
        /// 逐列扫描 alpha：火焰底部是实心的，扫到的最高"空行"就是内容起点。
        /// 用精灵自己的 textureRect 扫描，所以对合图（atlas）同样成立。
        /// </summary>
        private static float ContentBottomFraction(SpriteRenderer sr)
        {
            if (sr == null || sr.sprite == null || sr.sprite.texture == null)
            {
                return 0f;
            }

            try
            {
                Sprite sp = sr.sprite;
                Rect r = sp.textureRect;
                Texture2D tex = sp.texture;
                Color32[] px = tex.GetPixels32();
                int texW = tex.width;

                int bottom = -1;

                for (int y = 0; y < (int)r.height && bottom < 0; y++)
                {
                    int py = (int)r.y + y;

                    for (int x = 0; x < (int)r.width; x++)
                    {
                        int index = py * texW + (int)r.x + x;

                        if (index >= 0 && index < px.Length && px[index].a > 16)
                        {
                            bottom = y;
                            break;
                        }
                    }
                }

                if (bottom < 0 || r.height <= 0f)
                {
                    return 0f;
                }

                return bottom / r.height;
            }
            catch
            {
                return 0f;
            }
        }

        private void Update()
        {
            if (_art == null)
            {
                return;
            }

            // 火柱在"喷发间隙"是关掉碰撞盒的（Hazard 的行为），此时 collider.bounds 为 0。
            // 所以尺寸只能读 size/offset —— 用 bounds 会算出 0 缩放（实测踩过）。
            if (!_canvasReady)
            {
                var player = _art.GetComponent<SequencePlayer>();
                Vector2 canvas;

                if (player == null || !player.TryGetCanvasSize(out canvas) || canvas.y <= 0.01f)
                {
                    return;
                }

                Vector2 size = _boxExact != null ? _boxExact.size : (Vector2)_box.bounds.size;
                Vector2 offset = _boxExact != null ? _boxExact.offset : Vector2.zero;
                float sy = _box.transform.lossyScale.y;

                if (size.y <= 0.01f)
                {
                    size = new Vector2(70f, 900f);   // 兜底，别让火焰消失
                }

                // 按**高度**铺满柱子（宽度跟着比例走）。
                // 之前按宽度算：柱宽只有 70，火焰被缩成 63px 高的一小团 ——
                // 而这根火柱在关卡里是要顶到画面顶上的。宽度限制在这里是错的。
                float targetW = size.x * widthScale;
                float targetH = size.y * heightScale;
                float kx = targetH / canvas.y;
                float segH = canvas.y * kx;
                int segments = 1;

                // 底边对齐：对齐的必须是**火焰内容的底边**，不是画布底边。
                // 生成时画布底部留了空白（火焰没有顶到画布最下沿），
                // 按画布对齐就会整根悬空一截（实测反馈"火焰离地了"）。
                float centerY = transform.position.y + offset.y * sy;
                float bottomY = centerY - size.y * sy * 0.5f;

                float contentBottom = ContentBottomFraction(_artRenderer);

                _art.localScale = new Vector3(kx, kx, 1f);
                _art.position = new Vector3(transform.position.x + offset.x * _box.transform.lossyScale.x,
                                            bottomY + segH * 0.5f - contentBottom * segH, 0f);

                for (int i = 1; i < segments; i++)
                {
                    var seg = new GameObject("FlameSeg" + i);
                    seg.transform.SetParent(transform, false);
                    var segSr = seg.AddComponent<SpriteRenderer>();
                    segSr.sortingLayerName = sortingLayer;
                    segSr.sortingOrder = sortingOrder - i;   // 越上面越靠后，层次感
                    segSr.color = new Color32(255, 255, 255, (byte)Mathf.Max(90, 255 - i * 40));

                    var segPlayer = seg.AddComponent<SequencePlayer>();
                    segPlayer.basePath = "video_fx";
                    segPlayer.pixelsPerUnit = 1f;
                    segPlayer.Play(clipName, true);

                    seg.transform.localScale = new Vector3(kx, kx, 1f);
                    seg.transform.position = new Vector3(_art.position.x, bottomY + segH * (i + 0.5f), 0f);
                    _segments.Add(segSr);
                }

                _canvasReady = true;
            }

            // 喷发时才显火：碰撞盒开 = 正在喷
            if (_artRenderer != null && _box != null)
            {
                bool on = _box.enabled;
                _artRenderer.enabled = on;

                for (int i = 0; i < _segments.Count; i++)
                {
                    if (_segments[i] != null)
                    {
                        _segments[i].enabled = on;
                    }
                }
            }
        }
    }
}
