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
        [Tooltip("每格画成几×几像素。格子已经很小了，默认 1 就够")]
        [SerializeField] private int pixelsPerCell = 1;

        [Tooltip("边缘噪声幅度（0~1）")]
        [Range(0f, 1f)]
        [SerializeField] private float edgeNoise = 0.35f;

        [Header("颜色")]
        [SerializeField] private Color waterColor = new Color(0.40f, 0.70f, 0.78f, 0.50f);
        [SerializeField] private Color fireColor = new Color(1f, 0.58f, 0.22f, 0.80f);
        [SerializeField] private Color lavaColor = new Color(0.95f, 0.32f, 0.12f, 0.90f);
        [SerializeField] private Color residueColor = new Color(0.16f, 0.15f, 0.15f, 0.30f);

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

            if (_grid != null && _grid.Dirty)
            {
                Repaint();
                _grid.ClearDirty();
            }
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
                        default: continue;
                    }

                    int x0 = cx * pixelsPerCell;
                    int y0 = cy * pixelsPerCell;

                    for (int py = 0; py < pixelsPerCell; py++)
                    {
                        for (int px = 0; px < pixelsPerCell; px++)
                        {
                            int x = x0 + px;
                            int y = y0 + py;
                            int index = y * pw + x;

                            float n = 1f - edgeNoise * 0.5f + _noise[index] * edgeNoise;
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
    }
}
