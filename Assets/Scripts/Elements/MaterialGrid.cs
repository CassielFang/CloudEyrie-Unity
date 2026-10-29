using UnityEngine;

namespace CloudEyrie.Elements
{
    /// <summary>
    /// 一张很粗的"材质格子"：每格 32 单位（=1 米），10Hz 更新。
    ///
    /// 为什么不做 Noita 那样的像素级模拟：那是每帧上百万格、要 GPU 或高度优化的 CPU 内核。
    /// 我们要的是**涌现感**而不是物理精确 —— 火沿草地蔓延、水把火浇灭、岩浆遇水凝成石头，
    /// 这些在 32 单位格子上已经足够读得出来，而且一关只有几百×几十格，10Hz 的代价可以忽略。
    ///
    /// 约定：坐标系原点在左下角，格子 (x,y) 的中心世界坐标是
    /// <c>origin + (x + 0.5, y + 0.5) * cellSize</c>。
    /// </summary>
    public class MaterialGrid : MonoBehaviour
    {
        /// <summary>
        /// 一格多大。8 单位 ≈ 屏幕 12px，是"能看出形状、又不至于像马赛克"的折中。
        /// 32 单位那版实测一格有半个角色高，水潭看起来就是一个个大方块。
        /// </summary>
        public const float CellSize = 8f;

        [Tooltip("每秒钟模拟多少拍。太高没必要，火苗/水流都不会因此更自然")]
        [SerializeField] private float ticksPerSecond = 10f;

        [Tooltip("火每格燃烧多少拍后熄灭（10 拍 = 1 秒）")]
        [SerializeField] private int fireLifetimeTicks = 40;

        /// <summary>
        /// 全网格同时燃烧的格数上限。只挡「蔓延」，不挡「已燃的烧完」。
        ///
        /// 数值来源：一片油（半径 90）大约 1000 格。上限 260 让火势明显收敛，
        /// 又足以让一次点燃看起来"成片"。
        /// </summary>
        [Tooltip("全网格同时燃烧格数上限（防止火势膨胀把编辑器拖死）")]
        [SerializeField] private int maxFireCells = 260;

        [Header("范围（世界坐标，左下角 + 尺寸）")]
        [SerializeField] private Vector2 origin = new Vector2(0f, -700f);
        [SerializeField] private int width = 320;
        [SerializeField] private int height = 40;

        private MaterialType[] _cells;
        private byte[] _fireAge;
        private MaterialType[] _next;
        private byte[] _nextFireAge;

        /// <summary>
        /// 本 tick 内"已经有东西移到这里"的标记。
        ///
        /// 没有它会出经典的重复移动 bug：相邻两格沙同时判断「下方为空」，
        /// 都往同一格写，然后各自把原位清空 —— 一次 tick 净损失一格沙。
        /// 实测沙丘在没有水的情况下从 2121 格掉到 740 格，就是它。
        /// </summary>
        private bool[] _moved;

        private float _tickAccumulator;

        /// <summary>材质有变化时置位，供表现层重画</summary>
        public bool Dirty { get; private set; }

        public int Width { get { return width; } }
        public int Height { get { return height; } }
        public Vector2 Origin { get { return origin; } }
        public MaterialType[] Cells { get { return _cells; } }

        public void Configure(Vector2 worldOrigin, int w, int h)
        {
            origin = worldOrigin;
            width = Mathf.Max(1, w);
            height = Mathf.Max(1, h);
            Allocate();
        }

        private void Awake()
        {
            Allocate();
        }

        private void Allocate()
        {
            int total = width * height;

            if (_cells == null || _cells.Length != total)
            {
                _cells = new MaterialType[total];
                _fireAge = new byte[total];
                _next = new MaterialType[total];
                _nextFireAge = new byte[total];
                _moved = new bool[total];
            }
        }

        // ============================================================ 坐标换算

        public bool WorldToCell(Vector2 world, out int cx, out int cy)
        {
            cx = Mathf.FloorToInt((world.x - origin.x) / CellSize);
            cy = Mathf.FloorToInt((world.y - origin.y) / CellSize);
            return cx >= 0 && cx < width && cy >= 0 && cy < height;
        }

        public Vector2 CellToWorld(int cx, int cy)
        {
            return origin + new Vector2((cx + 0.5f) * CellSize, (cy + 0.5f) * CellSize);
        }

        public MaterialType GetAt(Vector2 world)
        {
            return WorldToCell(world, out int cx, out int cy) ? _cells[cy * width + cx] : MaterialType.Empty;
        }

        // ============================================================ 外部操作

        /// <summary>点一处火（半径内的可燃物立刻变成火）</summary>
        public void Ignite(Vector2 world, float radius)
        {
            // 直接点燃也要受上限约束 —— 否则火源每 0.5 秒补一次火，
            // 蔓延被挡住了、总量却还在涨（实测上限 260 时网格里仍有 388 格在烧）。
            if (CountFire() >= maxFireCells)
            {
                return;
            }

            Stamp(world, radius, (m) => m == MaterialType.Flammable ? MaterialType.Fire : m);
            Dirty = true;
        }

        /// <summary>倒一片水（会往下流）</summary>
        public void Pour(Vector2 world, float radius)
        {
            Stamp(world, radius, (m) => m == MaterialType.Empty || m == MaterialType.Flammable
                                        ? MaterialType.Water : m);
            Dirty = true;
        }

        /// <summary>把一片区域标记为可燃（草地/藤蔓/油池）</summary>
        public void MarkFlammable(Vector2 world, float radius)
        {
            Stamp(world, radius, (m) => m == MaterialType.Empty ? MaterialType.Flammable : m);
            Dirty = true;
        }

        private void Stamp(Vector2 world, float radius, System.Func<MaterialType, MaterialType> map)
        {
            int r = Mathf.CeilToInt(radius / CellSize);

            WorldToCell(world, out int cx, out int cy);

            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx;
                    int y = cy + dy;

                    if (x < 0 || x >= width || y < 0 || y >= height)
                    {
                        continue;
                    }

                    if (dx * dx + dy * dy > r * r)
                    {
                        continue;
                    }

                    int i = y * width + x;
                    _cells[i] = map(_cells[i]);

                    // 新点着的火必须从 0 岁开始。
                    // 不清年龄的话，它会继承这一格上一个残留物的旧年龄，下一次 Tick 立刻判"烧完"——
                    // 表现是火一闪就没（实测：源在不停点火，网格里 fire 常年是 0、residue 却一直涨）。
                    if (_cells[i] == MaterialType.Fire)
                    {
                        _fireAge[i] = 0;
                    }
                }
            }
        }

        // ============================================================ 模拟

        private void Update()
        {
            _tickAccumulator += Time.deltaTime;

            float step = 1f / Mathf.Max(1f, ticksPerSecond);

            while (_tickAccumulator >= step)
            {
                _tickAccumulator -= step;
                Tick();
            }
        }

        private void Tick()
        {
            System.Array.Copy(_cells, _next, _cells.Length);
            System.Array.Copy(_fireAge, _nextFireAge, _fireAge.Length);
            System.Array.Clear(_moved, 0, _moved.Length);

            // 先数一遍当前有多少格在烧。
            //
            // 为什么需要上限：油铺开之后火柱一点就是几千格同时在烧，每 tick 都在改
            // → 贴图不停重传 → 编辑器直接卡死（实测四五次）。
            // 上限只挡「蔓延」，不挡「已燃的继续烧完」—— 火势会稳定在一个规模，
            // 不会无限膨胀，也不会看起来"火灭了"。
            bool allowSpread = CountFire() < maxFireCells;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    MaterialType m = _cells[i];

                    switch (m)
                    {
                        case MaterialType.Fire:
                            TickFire(x, y, i, allowSpread);
                            break;
                        case MaterialType.Water:
                            TickWater(x, y, i);
                            break;
                        case MaterialType.Lava:
                            TickLava(x, y, i);
                            break;
                        case MaterialType.Sand:
                            TickSand(x, y, i);
                            break;
                    }
                }
            }

            Swap();
        }

        private int CountFire()
        {
            int n = 0;

            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] == MaterialType.Fire)
                {
                    n++;
                }
            }

            return n;
        }

        private void TickFire(int x, int y, int i, bool allowSpread)
        {
            _nextFireAge[i]++;

            if (_nextFireAge[i] > fireLifetimeTicks)
            {
                _next[i] = MaterialType.Residue;
                _nextFireAge[i] = 0;
                return;
            }

            // 向 8 邻域蔓延：可燃 → 火，水 → 熄灭自己
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    int nx = x + dx;
                    int ny = y + dy;

                    if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    int ni = ny * width + nx;

                    switch (_cells[ni])
                    {
                        case MaterialType.Flammable:
                            // 超过上限就不再蔓延（已经在烧的照常烧完）
                            if (allowSpread)
                            {
                                _next[ni] = MaterialType.Fire;
                                _nextFireAge[ni] = 0;
                            }

                            break;

                        case MaterialType.Water:
                            // 水浇灭火：水变成蒸汽残留，火直接消失
                            _next[i] = MaterialType.Residue;
                            _next[ni] = MaterialType.Residue;
                            return;
                    }
                }
            }
        }

        private void TickWater(int x, int y, int i)
        {
            int below = y - 1;

            if (below < 0)
            {
                return;
            }

            int bi = below * width + x;
            MaterialType bm = _cells[bi];

            // 火 → 浇灭；岩浆 → 黑曜石（水也一起消耗）
            if (bm == MaterialType.Fire)
            {
                _next[bi] = MaterialType.Residue;
                _next[i] = MaterialType.Residue;
                return;
            }

            if (bm == MaterialType.Lava)
            {
                _next[bi] = MaterialType.Solid;
                _next[i] = MaterialType.Residue;
                return;
            }

            if (bm == MaterialType.Empty || bm == MaterialType.Flammable)
            {
                _next[bi] = MaterialType.Water;
                _next[i] = MaterialType.Empty;
                return;
            }

            // 落地了：往两侧找空位（"水往低处流"的最小实现）
            int dir = (x + y) % 2 == 0 ? 1 : -1;

            for (int k = 0; k < 2; k++)
            {
                int nx = x + dir * (k == 0 ? 1 : -1);

                if (nx < 0 || nx >= width)
                {
                    continue;
                }

                int ni = y * width + nx;

                if (_cells[ni] == MaterialType.Empty)
                {
                    _next[ni] = MaterialType.Water;
                    _next[i] = MaterialType.Empty;
                    return;
                }
            }
        }

        private void TickLava(int x, int y, int i)
        {
            // 岩浆点燃相邻可燃物；碰到水就凝固
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    int nx = x + dx;
                    int ny = y + dy;

                    if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    int ni = ny * width + nx;

                    if (_cells[ni] == MaterialType.Flammable)
                    {
                        _next[ni] = MaterialType.Fire;
                        _nextFireAge[ni] = 0;
                    }
                    else if (_cells[ni] == MaterialType.Water)
                    {
                        _next[i] = MaterialType.Solid;
                        _next[ni] = MaterialType.Residue;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// 流沙：竖直下落，落不下去就往左下/右下斜滑，都没有就停住堆起来。
        /// 简单的"沙"行为就够了 —— 土境要的是"地形会变"，不是精确的颗粒物理。
        /// </summary>
        private void TickSand(int x, int y, int i)
        {
            int below = y - 1;

            if (below < 0)
            {
                return;
            }

            int bi = below * width + x;
            MaterialType bm = _cells[bi];

            // 落到水里：**沙自己**变成实心（成泥），水化成残留。
            //
            // 之前写的是「沙变空、水变实心」—— 结果沙一格一格凭空消失，
            // 实测倒下 700 多格沙、实心只多了 14 格，看起来像"沙被水吃掉"。
            // 现在的写法守恒：沙 −1、实心 +1、水 −1，账对得上。
            if (bm == MaterialType.Water)
            {
                _next[i] = MaterialType.Solid;
                _next[bi] = MaterialType.Residue;
                return;
            }

            // 掉进岩浆：被烧成实心（熔渣）
            if (bm == MaterialType.Lava)
            {
                _next[i] = MaterialType.Empty;
                _next[bi] = MaterialType.Solid;
                return;
            }

            if (bm == MaterialType.Empty || bm == MaterialType.Fire || bm == MaterialType.Residue)
            {
                if (_moved[bi])
                {
                    return;
                }

                _next[bi] = MaterialType.Sand;
                _next[i] = MaterialType.Empty;
                _moved[bi] = true;
                _moved[i] = true;
                return;
            }

            // 堆住了：往两侧斜下滑一格
            int dir = (x + y) % 2 == 0 ? 1 : -1;

            for (int k = 0; k < 2; k++)
            {
                int nx = x + dir * (k == 0 ? 1 : -1);

                if (nx < 0 || nx >= width)
                {
                    continue;
                }

                int ni = below * width + nx;

                if (_cells[ni] == MaterialType.Empty && !_moved[ni])
                {
                    _next[ni] = MaterialType.Sand;
                    _next[i] = MaterialType.Empty;
                    _moved[ni] = true;
                    _moved[i] = true;
                    return;
                }
            }
        }

        private void Swap()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] != _next[i] || _fireAge[i] != _nextFireAge[i])
                {
                    Dirty = true;
                    break;
                }
            }

            MaterialType[] tmp = _cells;
            _cells = _next;
            _next = tmp;

            byte[] tmpAge = _fireAge;
            _fireAge = _nextFireAge;
            _nextFireAge = tmpAge;
        }

        public void ClearDirty()
        {
            Dirty = false;
        }

        /// <summary>外部改了 <see cref="Cells"/> 之后要调一次，表现层才知道要重画</summary>
        public void MarkDirty()
        {
            Dirty = true;
        }
    }
}
