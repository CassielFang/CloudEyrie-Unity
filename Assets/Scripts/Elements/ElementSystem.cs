using UnityEngine;

namespace CloudEyrie.Elements
{
    /// <summary>
    /// 一关的元素系统入口：建格子、接表现、给实体挂状态、把地面灌成实心格。
    ///
    /// 它自己不做玩法，只负责把「材质格子」和「场景里已有的东西」接起来 ——
    /// 机关想点火/放水就调这里的 <see cref="Ignite"/> / <see cref="Pour"/>。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class ElementSystem : MonoBehaviour
    {
        private static ElementSystem _instance;

        public static ElementSystem Instance { get { return _instance; } }

        [Tooltip("地面以下多深也标记成实心（防止水流穿地）")]
        [SerializeField] private int groundDepthCells = 6;

        private MaterialGrid _grid;

        public MaterialGrid Grid { get { return _grid; } }

        private void Awake()
        {
            _instance = this;

            _grid = GetComponentInChildren<MaterialGrid>();

            if (_grid == null)
            {
                var go = new GameObject("MaterialGrid");
                go.transform.SetParent(transform, false);
                _grid = go.AddComponent<MaterialGrid>();
            }

            FitToScene();

            var painter = GetComponentInChildren<MaterialPainter>();

            if (painter == null)
            {
                var go = new GameObject("MaterialPainter");
                go.transform.SetParent(transform, false);
                go.AddComponent<SpriteRenderer>();
                painter = go.AddComponent<MaterialPainter>();
            }

            painter.Bind(_grid);
        }

        private void Start()
        {
            PaintGroundAsSolid();
            AttachStatusHosts();
            BindSources();
        }

        /// <summary>把场景里已有的火盆 / 火柱 / 岩浆接成元素源</summary>
        private void BindSources()
        {
            foreach (Brazier brazier in FindObjectsByType<Brazier>(FindObjectsInactive.Exclude))
            {
                if (brazier.GetComponent<ElementSource>() != null)
                {
                    continue;
                }

                var src = brazier.gameObject.AddComponent<ElementSource>();
                src.Bind(brazier);
                src.Kind = ElementSource.SourceKind.Fire;
                src.Radius = 120f;
            }

            foreach (Hazard hazard in FindObjectsByType<Hazard>(FindObjectsInactive.Exclude))
            {
                if (hazard.GetComponent<ElementSource>() != null)
                {
                    continue;
                }

                string n = hazard.name;

                if (n.StartsWith("FirePillar"))
                {
                    var src = hazard.gameObject.AddComponent<ElementSource>();
                    src.Bind(hazard);
                    src.Kind = ElementSource.SourceKind.Fire;
                    src.Radius = 130f;
                }
                else if (n.StartsWith("Lava"))
                {
                    var src = hazard.gameObject.AddComponent<ElementSource>();
                    src.Kind = ElementSource.SourceKind.Lava;
                    src.Radius = 200f;
                }
            }
        }

        /// <summary>按场景里 WORLD 层碰撞体的包围盒定格子范围</summary>
        private void FitToScene()
        {
            int worldLayer = LayerMask.NameToLayer("WORLD");
            int mask = worldLayer >= 0 ? (1 << worldLayer) : ~0;

            Bounds? bounds = null;

            foreach (Collider2D col in FindObjectsByType<Collider2D>(FindObjectsInactive.Exclude))
            {
                if (!col.enabled || col.isTrigger)
                {
                    continue;
                }

                if (worldLayer >= 0 && col.gameObject.layer != worldLayer)
                {
                    continue;
                }

                // 忽略背景/巨大的装饰碰撞体
                if (col.bounds.size.x > 4000f && col.bounds.size.y > 800f)
                {
                    continue;
                }

                if (bounds == null)
                {
                    bounds = col.bounds;
                }
                else
                {
                    Bounds b = bounds.Value;
                    b.Encapsulate(col.bounds);
                    bounds = b;
                }
            }

            if (bounds == null)
            {
                _grid.Configure(new Vector2(0f, -700f), 320, 40);
                return;
            }

            Bounds total = bounds.Value;
            total.Expand(new Vector3(2f * MaterialGrid.CellSize, 2f * MaterialGrid.CellSize, 0f));

            int w = Mathf.CeilToInt(total.size.x / MaterialGrid.CellSize);
            int h = Mathf.CeilToInt(total.size.y / MaterialGrid.CellSize);

            _grid.Configure(new Vector2(total.min.x, total.min.y), w, h);
        }

        /// <summary>每列向下打一条射线，射线以下都灌成实心格 —— 水才有地方积。</summary>
        private void PaintGroundAsSolid()
        {
            int worldLayer = LayerMask.NameToLayer("WORLD");
            int mask = worldLayer >= 0 ? (1 << worldLayer) : ~0;

            float top = _grid.Origin.y + _grid.Height * MaterialGrid.CellSize;
            float bottom = _grid.Origin.y;

            for (int x = 0; x < _grid.Width; x++)
            {
                Vector2 origin = new Vector2(_grid.Origin.x + (x + 0.5f) * MaterialGrid.CellSize, top);
                RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, top - bottom, mask);

                if (hit.collider == null)
                {
                    continue;
                }

                float groundY = hit.point.y;
                int startY = Mathf.FloorToInt((groundY - _grid.Origin.y) / MaterialGrid.CellSize);

                for (int dy = 0; dy < groundDepthCells; dy++)
                {
                    int y = startY - dy;

                    if (y < 0 || y >= _grid.Height)
                    {
                        continue;
                    }

                    _grid.Cells[y * _grid.Width + x] = MaterialType.Solid;
                }
            }

            // 地形已定，让表现层重画一次
            _grid.MarkDirty();
        }

        private void AttachStatusHosts()
        {
            foreach (QingheController player in FindObjectsByType<QingheController>(FindObjectsInactive.Exclude))
            {
                if (player.GetComponent<StatusHost>() == null)
                {
                    player.gameObject.AddComponent<StatusHost>();
                }
            }

            foreach (EnemyAI enemy in FindObjectsByType<EnemyAI>(FindObjectsInactive.Exclude))
            {
                if (enemy.GetComponent<StatusHost>() == null)
                {
                    enemy.gameObject.AddComponent<StatusHost>();
                }
            }
        }

        // ============================================================ 对外

        public void Ignite(Vector2 world, float radius)
        {
            if (_grid != null)
            {
                _grid.Ignite(world, radius);
            }
        }

        public void Pour(Vector2 world, float radius)
        {
            if (_grid != null)
            {
                _grid.Pour(world, radius);
            }
        }

        public void MarkFlammable(Vector2 world, float radius)
        {
            if (_grid != null)
            {
                _grid.MarkFlammable(world, radius);
            }
        }

        /// <summary>直接把一片格子刷成某种材质（岩浆用）</summary>
        public void SetMaterial(Vector2 world, float radius, MaterialType material)
        {
            if (_grid == null)
            {
                return;
            }

            int r = Mathf.CeilToInt(radius / MaterialGrid.CellSize);
            _grid.WorldToCell(world, out int cx, out int cy);

            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx;
                    int y = cy + dy;

                    if (x < 0 || x >= _grid.Width || y < 0 || y >= _grid.Height)
                    {
                        continue;
                    }

                    if (dx * dx + dy * dy > r * r)
                    {
                        continue;
                    }

                    _grid.Cells[y * _grid.Width + x] = material;
                }
            }

            _grid.MarkDirty();
        }
    }
}
