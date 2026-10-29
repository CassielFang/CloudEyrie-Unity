using UnityEngine;

namespace CloudEyrie.Elements
{
    /// <summary>
    /// 把 <see cref="MaterialGrid"/> 画成一张贴图。
    ///
    /// 三件事决定它像不像"水墨"：
    /// 1. **每格画成 N×N 像素块**（默认 3×3）——一格一像素会变成生硬的马赛克；
    /// 2. **每像素加一点噪声**，边缘不是直尺切的；
    /// 3. **向空格渗一点 alpha**，火/水的外沿有晕开的感觉。
    /// 双线性过滤负责把最后一点方格感抹平。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class MaterialPainter : MonoBehaviour
    {
        // ⚠️ 必须是 1。
        // 试过提到 2 想让沙丘边缘柔和：格子 1202×140 → 贴图 2404×280（67 万像素），
        // 沙在动时每 tick 都要重传整张图，直接卡死编辑器（第四次了）。
        // 方块感改用「每格随机明暗 + 双线性过滤」缓解，别再把分辨率翻倍。
        [Tooltip("每格画成几×几像素（大于 1 会显著拖慢刷新）")]
        [SerializeField] private int pixelsPerCell = 1;

        [Tooltip("边缘噪声幅度（0~1）")]
        [Range(0f, 1f)]
        [SerializeField] private float edgeNoise = 0.35f;

        [Tooltip("重画间隔（秒）。太小会把编辑器拖死")]
        [SerializeField] private float paintInterval = 0.12f;

        private float _sincePaint;

        [Header("颜色")]
        [SerializeField] private Color waterColor = new Color(0.40f, 0.70f, 0.78f, 0.50f);
        [SerializeField] private Color fireColor = new Color(1f, 0.58f, 0.22f, 0.55f);
        [SerializeField] private Color lavaColor = new Color(0.95f, 0.32f, 0.12f, 0.90f);
        [SerializeField] private Color residueColor = new Color(0.16f, 0.15f, 0.15f, 0.30f);
        [SerializeField] private Color sandColor = new Color(0.70f, 0.58f, 0.34f, 1f);

        private MaterialGrid _grid;
        private SpriteRenderer _renderer;
        private Texture2D _texture;
        private Color32[] _pixels;
        private float[] _noise;
        private bool _built;

        public void Bind(MaterialGrid grid)
        {
            _grid = grid;
            Build();
        }

        private void Build()
        {
            if (_grid == null || _built)
            {
                return;
            }

            _renderer = GetComponent<SpriteRenderer>();
            _renderer.sortingLayerName = "Default";
            _renderer.sortingOrder = SortingOrders.FxOrbitFar;

            int w = _grid.Width * pixelsPerCell;
            int h = _grid.Height * pixelsPerCell;

            _texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            _pixels = new Color32[w * h];
            _noise = new float[w * h];

            // 固定种子的白噪声：每帧重画结果一致，不会闪
            var rng = new System.Random(12345);

            for (int i = 0; i < _noise.Length; i++)
            {
                _noise[i] = (float)rng.NextDouble();
            }

            Sprite sprite = Sprite.Create(
                _texture,
                new Rect(0f, 0f, w, h),
                Vector2.zero,
                1f / (MaterialGrid.CellSize / pixelsPerCell));

            _renderer.sprite = sprite;

            transform.position = _grid.Origin;
            _built = true;
            Repaint();
        }

        private void LateUpdate()
        {
            if (!_built)
            {
                Build();
                return;
            }

            // 节流：格子一变就整张重画 + 上传，格子大（1202×140）时这一步很贵。
            // 一大片油被点着时，每 tick 都在改 → 每帧都在传图 → 编辑器直接卡死（已经卡死过四次）。
            // 0.12 秒一次（约 8Hz）肉眼看不出区别，但把上传次数压到原来的三分之一以下。
            _sincePaint += Time.unscaledDeltaTime;

            if (_grid == null || !_grid.Dirty || _sincePaint < paintInterval)
            {
                return;
            }

            _sincePaint = 0f;
            Repaint();
            _grid.ClearDirty();
        }

        private void Repaint()
        {
            MaterialType[] cells = _grid.Cells;
            int gw = _grid.Width;
            int gh = _grid.Height;
            int pw = gw * pixelsPerCell;

            for (int i = 0; i < _pixels.Length; i++)
            {
                _pixels[i] = new Color32(0, 0, 0, 0);
            }

            for (int cy = 0; cy < gh; cy++)
            {
                for (int cx = 0; cx < gw; cx++)
                {
                    Color baseColor;

                    switch (cells[cy * gw + cx])
                    {
                        case MaterialType.Water: baseColor = waterColor; break;
                        case MaterialType.Fire: baseColor = fireColor; break;
                        case MaterialType.Lava: baseColor = lavaColor; break;
                        case MaterialType.Residue: baseColor = residueColor; break;
                        case MaterialType.Sand: baseColor = sandColor; break;
                        default: continue;
                    }

                    int x0 = cx * pixelsPerCell;
                    int y0 = cy * pixelsPerCell;

                    // 每格一个固定的明暗系数（格坐标哈希，帧间稳定不闪）。
                    // 分辨率只有 1px/格，靠这个把"一整片同色方块"打散成有颗粒的堆。
                    float grain = 0.82f + 0.36f * Hash(cx, cy);

                    for (int py = 0; py < pixelsPerCell; py++)
                    {
                        for (int px = 0; px < pixelsPerCell; px++)
                        {
                            int x = x0 + px;
                            int y = y0 + py;
                            int index = y * pw + x;

                            float n = grain * (1f - edgeNoise * 0.5f + _noise[index] * edgeNoise);
                            Color c = baseColor;
                            c.a *= n;

                            _pixels[index] = c;
                        }
                    }
                }
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        /// <summary>格坐标 → 0..1 的稳定哈希（不用 Random，帧间必须一致，否则整片会闪）</summary>
        private static float Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / 2147483647f;
            }
        }
    }
}
