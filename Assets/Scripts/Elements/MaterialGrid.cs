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

        [Header("范围（世界坐标，左下角 + 尺寸）")]
        [SerializeField] private Vector2 origin = new Vector2(0f, -700f);
        [SerializeField] private int width = 320;
        [SerializeField] private int height = 40;

        private MaterialType[] _cells;
        private byte[] _fireAge;
        private MaterialType[] _next;
        private byte[] _nextFireAge;

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

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    MaterialType m = _cells[i];

                    switch (m)
                    {
                        case MaterialType.Fire:
                            TickFire(x, y, i);
                            break;
                        case MaterialType.Water:
                            TickWater(x, y, i);
                            break;
                        case MaterialType.Lava:
                            TickLava(x, y, i);
                            break;
                    }
                }
            }

            Swap();
        }

        private void TickFire(int x, int y, int i)
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
                            _next[ni] = MaterialType.Fire;
                            _nextFireAge[ni] = 0;
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
