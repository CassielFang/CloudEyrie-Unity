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

            var player = go.AddComponent<SequencePlayer>();
            player.basePath = "video_fx";
            player.pixelsPerUnit = 1f;

            // 先播一次拿到画布尺寸，再按碰撞盒定缩放
            player.Play(clipName, true);

            Vector2 canvas = Vector2.zero;

            if (player.TryGetCanvasSize(out canvas) && canvas.y > 0.01f)
            {
                Bounds b = box.bounds;
                float targetW = b.size.x * widthScale;
                float targetH = b.size.y * heightScale;
                float k = Mathf.Min(targetW / canvas.x, targetH / canvas.y);
                go.transform.localScale = new Vector3(k, k, 1f);

                // 底边贴碰撞盒底边（火焰从地面往上烧）
                float halfH = canvas.y * k * 0.5f;
                go.transform.position = new Vector3(b.center.x, b.min.y + halfH, 0f);
            }
        }
    }
}
