using UnityEngine;

namespace CloudEyrie.Elements
{
    /// <summary>
    /// 把 <see cref="MaterialGrid"/> 画成一张贴图。
    ///
    /// 一格一像素、双线性过滤 —— 出来的就是一层软边的水墨色块，正好配现有画风，
    /// 也不会像方块像素那样和背景长卷打架。整张只有几百×几十像素，重画成本可以忽略。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class MaterialPainter : MonoBehaviour
    {
        [Tooltip("水 / 火 / 岩浆 / 残留 的颜色")]
        [SerializeField] private Color waterColor = new Color(0.42f, 0.72f, 0.78f, 0.55f);
        [SerializeField] private Color fireColor = new Color(1f, 0.62f, 0.25f, 0.85f);
        [SerializeField] private Color lavaColor = new Color(0.95f, 0.35f, 0.15f, 0.9f);
        [SerializeField] private Color residueColor = new Color(0.15f, 0.14f, 0.14f, 0.35f);

        private MaterialGrid _grid;
        private SpriteRenderer _renderer;
        private Texture2D _texture;
        private Color32[] _pixels;
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

            _texture = new Texture2D(_grid.Width, _grid.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            _pixels = new Color32[_grid.Width * _grid.Height];

            // 一格一像素：pixelsPerUnit = 1/CellSize 时贴图 1 像素 = 32 世界单位
            Sprite sprite = Sprite.Create(
                _texture,
                new Rect(0f, 0f, _grid.Width, _grid.Height),
                Vector2.zero,
                1f / MaterialGrid.CellSize);

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

            for (int i = 0; i < cells.Length; i++)
            {
                switch (cells[i])
                {
                    case MaterialType.Water: _pixels[i] = waterColor; break;
                    case MaterialType.Fire: _pixels[i] = fireColor; break;
                    case MaterialType.Lava: _pixels[i] = lavaColor; break;
                    case MaterialType.Residue: _pixels[i] = residueColor; break;
                    default: _pixels[i] = new Color32(0, 0, 0, 0); break;
                }
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }
    }
}
