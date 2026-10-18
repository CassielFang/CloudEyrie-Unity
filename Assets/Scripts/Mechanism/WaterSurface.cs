using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 跟着 <see cref="WaterLevel"/> 升降的**水面**。
    ///
    /// 水境原本只有浮台随水位动，画面上却没有水 —— 玩家看到的是"平台自己浮起来"，
    /// 机制完全不可读。这里用工程里已有的 8×8 白图（`common/backdrop`）拉一块半透明水色矩形，
    /// 贴在水位线上，随水位一起升降。
    ///
    /// 为什么不进资产库：水面是一条纯色带，做成贴图反而要为每种宽度导一张。
    /// 运行时用同一张白图缩放，改宽度只动字段。
    /// </summary>
    [RequireComponent(typeof(WaterLevel))]
    public class WaterSurface : MonoBehaviour
    {
        [Tooltip("横向覆盖范围（世界单位），一般取关卡宽度")]
        [SerializeField] private float width = 9600f;

        [Tooltip("水面厚度")]
        [SerializeField] private float thickness = 120f;

        [Tooltip("横向起点（世界单位）。关卡从 0 开始时留 0 即可")]
        [SerializeField] private float leftEdge = 0f;

        [Tooltip("水色（带透明度）")]
        [SerializeField] private Color color = new Color(0.42f, 0.72f, 0.85f, 0.45f);

        [Tooltip("排序层")]
        [SerializeField] private string sortingLayer = "Default";

        [Tooltip("排序序号。要压在地面之下、角色之上时按需调")]
        [SerializeField] private int sortingOrder = -20;

        private WaterLevel _water;
        private Transform _quad;
        private SpriteRenderer _renderer;

        private void Start()
        {
            _water = GetComponent<WaterLevel>();

            Sprite sprite = Resources.Load<Sprite>("common/backdrop");

            if (sprite == null)
            {
                GameLog.W("[WaterSurface] 找不到 common/backdrop，水面不显示");
                enabled = false;
                return;
            }

            var go = new GameObject("WaterSurfaceQuad");
            go.transform.SetParent(transform, false);

            _renderer = go.AddComponent<SpriteRenderer>();
            _renderer.sprite = sprite;
            _renderer.color = color;
            _renderer.sortingLayerName = sortingLayer;
            _renderer.sortingOrder = sortingOrder;

            // 白图原始尺寸 8×8，拉成想要的大小
            Vector2 size = sprite.bounds.size;
            go.transform.localScale = new Vector3(width / size.x, thickness / size.y, 1f);

            _quad = go.transform;
        }

        private void LateUpdate()
        {
            if (_quad == null || _water == null)
            {
                return;
            }

            // 水面顶边 = 当前水位线
            float centerY = _water.CurrentY - thickness * 0.5f;
            _quad.position = new Vector3(leftEdge + width * 0.5f, centerY, 0f);
        }
    }
}
