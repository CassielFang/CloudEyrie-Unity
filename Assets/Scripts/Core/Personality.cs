namespace CloudEyrie
{
    /// <summary>
    /// 个体的「性格」：**数值完全相同，只错开行为的时间与相位**。
    ///
    /// <b>为什么需要它</b>
    ///
    /// 同屏两只一模一样的小怪会给人一种「复制粘贴」的廉价感 —— 它们同时转身、同时开火、
    /// 停在同一个坐标上，看起来像一个整体在动。
    ///
    /// 但直接给每只改**数值**（血量 / 速度 / 伤害）是另一条更糟的路：战斗会变得不可读，
    /// 玩家没法从外观判断哪只更危险，只会觉得「这游戏在偷偷坑我」。
    ///
    /// 所以走第三条路：数值一个字不改，只让**发现反应快慢、巡逻停留长短、攻击间隔、
    /// 接近时停的位置**各不相同。玩家会感觉「这两只不一样」，但不会觉得不公平。
    ///
    /// <b>种子固定，不是每次刷新随机</b>
    ///
    /// 同一只敌人在整局里性格稳定 —— 玩家能记住「左边那只反应快」，并据此预判。
    /// 每次刷新都换种子的话，行为就退化成噪声，读不出来，反而更像 bug。
    ///
    /// <b>为什么用 <see cref="System.Random"/> 而不是 <c>UnityEngine.Random</c></b>
    ///
    /// 后者是**全局共享状态**：这只敌人多取一个数，那只的序列就整体偏移。
    /// 于是一旦某处改了取数次数，所有敌人的性格全变，而且完全不可复现。
    /// 每个实例持有自己的 <see cref="System.Random"/> 才互不干扰。
    /// </summary>
    public struct Personality
    {
        private System.Random _rng;

        /// <param name="seed">
        /// 个体种子。**必须由生成方显式给出**（关卡按序号传，不要用名字的哈希 ——
        /// .NET 的 <c>string.GetHashCode</c> 每次进程启动都不一样，行为会一局一变、无法复现）。
        /// </param>
        public Personality(int seed)
        {
            // Knuth 乘法散列：相邻的 seed（0,1,2…）会得到彻底不同的序列。
            // 直接用 new Random(seed) 的话，相邻种子开局的头几个数很接近 ——
            // 表现就是「同屏两只敌人的性格几乎一样」，恰好是这里最不想要的。
            unchecked
            {
                _rng = new System.Random((int)((uint)seed * 2654435761u + 1013904223u));
            }
        }

        /// <summary>在 [min, max] 内取一个固定值。</summary>
        public float Range(float min, float max)
        {
            if (_rng == null)
            {
                _rng = new System.Random(1013904223);
            }

            return min + (float)_rng.NextDouble() * (max - min);
        }

        /// <summary>
        /// 围绕 1.0 的抖动倍率，<paramref name="amount"/> 是最大偏离比例。
        /// 例如 <c>Jitter(0.3f)</c> → 落在 [0.7, 1.3]。
        /// </summary>
        public float Jitter(float amount)
        {
            return Range(1f - amount, 1f + amount);
        }

        /// <summary>在 [-1, 1] 内取一个固定值（做方向 / 相位偏移用）。</summary>
        public float Signed()
        {
            return Range(-1f, 1f);
        }
    }
}
