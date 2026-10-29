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

        /// <summary>
        /// 是否自动在火柱旁铺油。
        ///
        /// 关掉的原因不是逻辑不通，而是**性能**：油铺好之后火柱一喷就整片烧起来，
        /// 满屏火格子每 tick 都在改，编辑器会在两三个 eval 内卡死。
        /// 等给元素层加上"同时燃烧格数上限"再打开。
        /// </summary>
        private const bool EnableOilPlacement = true;

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
            bool sandBound = false;
            bool oilPlaced = false;
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

                if (n.StartsWith("FirePillar") || n.StartsWith("Flame") || n.StartsWith("FireWall"))
                {
                    var src = hazard.gameObject.AddComponent<ElementSource>();
                    src.Bind(hazard);
                    src.Kind = ElementSource.SourceKind.Fire;
                    src.Radius = 130f;

                    // ⚠️ 油暂时**不自动铺**。
                    //
                    // 「油 + 火」这条链子本身是通的（铺上去、火柱一喷就整片烧起来，
                    // 实测过），但烧起来之后编辑器会卡死在两三个 eval 内 ——
                    // 满屏火格子每 tick 都在改，贴图上传 + 网格 tick 扛不住。
                    // 在给元素层加"最大同时燃烧格数"之前，先关掉自动铺油。
                    // 需要验证时用 ElementSystem.SetMaterial(pos, r, Flammable) 手动铺。
                    if (EnableOilPlacement && n.StartsWith("FirePillar") && !oilPlaced)
                    {
                        oilPlaced = true;
                        TryPlaceOilNear(hazard);
                    }
                }
                else if (n.StartsWith("Lava"))
                {
                    var src = hazard.gameObject.AddComponent<ElementSource>();
                    src.Kind = ElementSource.SourceKind.Lava;
                    src.Radius = 200f;
                }
                else if (n.StartsWith("WaterCurtain") || n.StartsWith("Water_Drift") || n.StartsWith("WaterLevel"))
                {
                    // 水境的三种水：垂帘 / 漂流带 / 水位 —— 都当作水源，遇火会互相熄灭
                    var src = hazard.gameObject.AddComponent<ElementSource>();
                    src.Kind = ElementSource.SourceKind.Water;
                    src.Radius = 160f;
                }
                else if (n.StartsWith("Pit") && !sandBound)
                {
                    // 土境的坑整段在地面以下（-694 以下），坑沿不是能站人的地面 ——
                    // 直接在坑上/坑边铺沙，沙会顺着空洞漏到格子底（实测 rows 0..41）。
                    //
                    // 所以先**探地面**：从格子顶往下打射线，命中 WORLD 层且离地不远的列
                    // 才是"能堆沙"的位置。探不到就不铺 —— 宁可不铺，也不要铺在悬空处。
                    sandBound = true;
                    TryPlaceQuicksand(hazard);
                }
            }

            // 水位不是 Hazard（它带着 WaterLevel + WaterSurface 两个专用组件），
            // 但它是水境最该参与元素交互的东西 —— 单独按名字接一次。
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "WaterLevel" || t.GetComponent<ElementSource>() != null)
                    {
                        continue;
                    }

                    var src = t.gameObject.AddComponent<ElementSource>();
                    src.Kind = ElementSource.SourceKind.Water;
                    src.Radius = 260f;
                }
            }
        }

        /// <summary>
        /// 找一段**连续、平坦、且不被装饰物遮挡**的地面，在上面铺流沙。
        ///
        /// 为什么不是"坑旁边试几个点"：土境的柱子/台座密度很高，按坑偏移探到的点
        /// 经常正好在柱子后面 —— 沙铺出来了，玩家看不见（上一轮就是这个问题）。
        ///
        /// 这里的判据有三条：
        /// <list type="number">
        ///   <item>射线从格子顶打到 WORLD，落点必须在地面高度附近（不是坑底）；</item>
        ///   <item>连续 600 单位都符合 —— 保证是一整段平地，不是柱子顶；</item>
        ///   <item>这段里的最高点与最低点落差 &lt; 12 —— 平地，不是斜坡。</item>
        /// </list>
        /// </summary>
        private void TryPlaceQuicksand(Hazard hazard)
        {
            if (_grid == null)
            {
                return;
            }

            int worldLayer = LayerMask.NameToLayer("WORLD");
            int mask = worldLayer >= 0 ? (1 << worldLayer) : ~0;

            float top = _grid.Origin.y + _grid.Height * MaterialGrid.CellSize - 16f;
            float floorY = -295f;
            float left = _grid.Origin.x + 200f;
            float right = _grid.Origin.x + _grid.Width * MaterialGrid.CellSize - 200f;

            const float step = 40f;
            const int needed = 15;          // 15 × 40 = 600 单位
            const float maxDrop = 12f;

            var run = new System.Collections.Generic.List<Vector2>();
            float runMin = float.MaxValue;
            float runMax = float.MinValue;

            for (float x = left; x <= right; x += step)
            {
                RaycastHit2D[] hits = Physics2D.RaycastAll(new Vector2(x, top), Vector2.down, 4000f, mask);
                float groundY = float.NaN;

                foreach (RaycastHit2D hit in hits)
                {
                    if (hit.collider == null || hit.point.y > floorY + 40f)
                    {
                        continue;
                    }

                    groundY = hit.point.y;
                    break;
                }

                if (float.IsNaN(groundY) || groundY < floorY - 40f)
                {
                    run.Clear();
                    runMin = float.MaxValue;
                    runMax = float.MinValue;
                    continue;
                }

                run.Add(new Vector2(x, groundY));

                if (groundY < runMin)
                {
                    runMin = groundY;
                }

                if (groundY > runMax)
                {
                    runMax = groundY;
                }

                if (run.Count >= needed)
                {
                    if (runMax - runMin > maxDrop)
                    {
                        // 是斜坡，不在这里铺
                        run.RemoveAt(0);
                        continue;
                    }

                    Vector2 mid = run[run.Count / 2];

                    var go = new GameObject("QuicksandSource");
                    go.transform.SetParent(transform, false);
                    go.transform.position = new Vector3(mid.x, mid.y + 150f, 0f);

                    var src = go.AddComponent<ElementSource>();
                    src.Kind = ElementSource.SourceKind.Sand;
                    src.Radius = 120f;

                    GameLog.I($"[ElementSystem] 流沙铺在 x={mid.x:0}（连续平地 {run.Count * step:0} 单位，地面 y={mid.y:0}）");
                    return;
                }
            }

            GameLog.W("[ElementSystem] 找不到连续平地，本关不铺流沙");
        }

        /// <summary>
        /// 在火柱附近找一段平地铺油（可燃物）。
        ///
        /// 只铺在**连续平地**上：油是贴地的，铺在斜坡或坑沿上会顺着格子滑走/漏掉，
        /// 表现成"点不着的一小撮"。判据与流沙同一套。
        /// </summary>
        private void TryPlaceOilNear(Hazard pillar)
        {
            if (_grid == null)
            {
                return;
            }

            int worldLayer = LayerMask.NameToLayer("WORLD");
            int mask = worldLayer >= 0 ? (1 << worldLayer) : ~0;

            float top = _grid.Origin.y + _grid.Height * MaterialGrid.CellSize - 16f;
            float floorY = -295f;
            float center = pillar.transform.position.x;

            const float step = 40f;
            const int needed = 8;           // 8 × 40 = 320 单位
            const float maxDrop = 10f;

            var run = new System.Collections.Generic.List<Vector2>();
            float runMin = float.MaxValue;
            float runMax = float.MinValue;

            for (float off = -600f; off <= 600f; off += step)
            {
                float x = center + off;
                RaycastHit2D[] hits = Physics2D.RaycastAll(new Vector2(x, top), Vector2.down, 4000f, mask);
                float groundY = float.NaN;

                foreach (RaycastHit2D hit in hits)
                {
                    if (hit.collider == null || hit.point.y > floorY + 40f)
                    {
                        continue;
                    }

                    groundY = hit.point.y;
                    break;
                }

                if (float.IsNaN(groundY) || groundY < floorY - 40f)
                {
                    run.Clear();
                    continue;
                }

                run.Add(new Vector2(x, groundY));
                runMin = Mathf.Min(runMin, groundY);
                runMax = Mathf.Max(runMax, groundY);

                if (run.Count < needed)
                {
                    continue;
                }

                if (runMax - runMin > maxDrop)
                {
                    run.RemoveAt(0);
                    continue;
                }

                Vector2 mid = run[run.Count / 2];

                var go = new GameObject("OilSource");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(mid.x, mid.y + 24f, 0f);

                var src = go.AddComponent<ElementSource>();
                src.Kind = ElementSource.SourceKind.Flammable;

                // 90 而不是 150：150 铺出来是一大片，火柱一喷整片烧起来，
                // 满屏都是火色块，把场景糊掉了（实测截图整张发白）。
                src.Radius = 90f;

                GameLog.I($"[ElementSystem] 油铺在 x={mid.x:0}（离火柱 {off:0}，地面 y={mid.y:0}）");
                return;
            }

            GameLog.W("[ElementSystem] 火柱附近找不到平地，本关不铺油");
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

            // 岩浆坑在 WORLD 层以下（火境的 Lava_1/2 在 y=-820），本身是 trigger，
            // 上面的循环把它们排除掉了 —— 结果是岩浆源永远落在格子外、一次都画不上去。
            // 所以只把**名字是 Lava 的机关**并进来，并且限制尺寸：
            // 不加限制的话，一个横跨全关的坑就会把格子撑到几十万格，
            // 贴图刷新直接卡死编辑器（实测卡死过一次）。
            foreach (Hazard hazard in FindObjectsByType<Hazard>(FindObjectsInactive.Exclude))
            {
                if (!hazard.name.StartsWith("Lava"))
                {
                    continue;
                }

                var hc = hazard.GetComponent<Collider2D>();

                if (hc == null || !hc.enabled)
                {
                    continue;
                }

                if (hc.bounds.size.x > 1600f || hc.bounds.size.y > 900f)
                {
                    continue;
                }

                Bounds b = bounds.Value;
                b.Encapsulate(hc.bounds);
                bounds = b;
            }

            Bounds total = bounds.Value;
            total.Expand(new Vector3(2f * MaterialGrid.CellSize, 2f * MaterialGrid.CellSize, 0f));

            int w = Mathf.CeilToInt(total.size.x / MaterialGrid.CellSize);
            int h = Mathf.CeilToInt(total.size.y / MaterialGrid.CellSize);

            // 最后一道保险：格子最多 1400×220（约 31 万格）。超出就按主地面重来，
            // 宁可少模拟一块，也不能把编辑器拖死（已经被拖死过两次）。
            if (w > 1400 || h > 220 || w <= 0 || h <= 0)
            {
                GameLog.W($"[ElementSystem] 格子范围异常 {w}×{h}，回退到 1202×160");
                w = 1202;
                h = 160;
            }

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
