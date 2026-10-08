using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 2D 物理分组与碰撞矩阵 —— 对应 Cocos 侧 <c>core/PhysicsGroups.ts</c>。
    ///
    /// <b>为什么需要它</b>
    ///
    /// 玩家和小怪都是 Dynamic 刚体 + 实心碰撞体。若同层，它们会**物理上互相阻挡**：
    /// 小怪追击时把玩家顶开，玩家走进小怪则被卡住（Cocos 侧实测反馈「人物被小怪挤跑」）。
    ///
    /// 常规做法是把敌人碰撞体做成 trigger（彼此穿过不阻挡），但**本作不行**：
    /// <see cref="CombatController"/> 的 AttackHitBox 本身就是 trigger，而 Box2D
    /// **不产生「触发器与触发器」之间的接触** —— 敌人也变 trigger 的话，玩家就再也打不到它。
    ///
    /// 所以改用碰撞矩阵，把「谁挡谁」和「谁能打谁」拆开：
    ///
    /// <code>
    /// | 分组    | 与谁碰撞                                      |
    /// |---------|-----------------------------------------------|
    /// | WORLD   | 只有 WORLD / PLAYER / ENEMY（地面、机关、屏障）|
    /// | PLAYER  | 只有 WORLD + ARROW —— 不挡小怪、也不被小怪挡   |
    /// | ENEMY   | WORLD + HITBOX —— 玩家身体穿得过去，但刀能砍到 |
    /// | HITBOX  | 只有 ENEMY —— 命中盒不参与任何阻挡             |
    /// | ARROW   | 只有 PLAYER —— 箭是 trigger，不会推玩家        |
    /// </code>
    ///
    /// <b>与 Cocos 的机制差异</b>：Cocos 侧是「位掩码矩阵」（<c>m[group] = mask</c>），
    /// 且要求**两个方向都通过**才算接触；Unity 是「层索引 + 对称矩阵」，一个勾选框管双向。
    /// 下面 <see cref="Apply"/> 里把 Cocos 那份矩阵按「双向都过」解算成 Unity 的对称矩阵，
    /// 结果是等价的 —— 逐对写死而不是留默认值，避免工程设置漂移后行为静默改变。
    ///
    /// ⚠️ <b>所有游戏对象都必须挂到这 5 个层之一</b>，别留在 Default。
    /// </summary>
    public static class PhysicsGroups
    {
        // 避开 Unity 内置层（0=Default, 1=TransparentFX, 2=Ignore Raycast, 4=Water, 5=UI）
        public const int World = 8;
        public const int Player = 9;
        public const int Enemy = 10;
        public const int HitBox = 11;
        public const int Arrow = 12;

        /// <summary>层名，用于 <c>TagManager</c> 注册与 Inspector 显示。</summary>
        public static readonly string[] LayerNames = { "WORLD", "PLAYER", "ENEMY", "HITBOX", "ARROW" };

        /// <summary>五个具名层的层索引，按上表顺序。</summary>
        public static readonly int[] GameLayers = { World, Player, Enemy, HitBox, Arrow };

        private static bool _applied;

        /// <summary>
        /// 把碰撞矩阵写进 Unity。幂等，重复调用无副作用。
        ///
        /// 解算依据（Cocos 侧 <c>applyPhysicsGroups()</c>，注意「双向都通过」才接触）：
        /// <code>
        /// m[WORLD]  = WORLD|PLAYER|ENEMY|HITBOX|ARROW
        /// m[PLAYER] = WORLD|ARROW
        /// m[ENEMY]  = WORLD|HITBOX
        /// m[HITBOX] = ENEMY
        /// m[ARROW]  = PLAYER
        /// </code>
        /// 例如 WORLD↔HITBOX：m[WORLD] 含 HITBOX，但 m[HITBOX] **不含** WORLD → 实际不接触。
        /// m[PLAYER] 里的 ARROW 那一位不能省 —— 只写单方向的箭永远碰不到玩家。
        /// </summary>
        public static void Apply()
        {
            if (_applied)
            {
                return;
            }

            _applied = true;

            // 先全部禁掉，再逐条打开 —— 不依赖工程里已存的矩阵状态。
            for (int i = 0; i < GameLayers.Length; i++)
            {
                for (int j = i; j < GameLayers.Length; j++)
                {
                    Physics2D.IgnoreLayerCollision(GameLayers[i], GameLayers[j], true);
                }

                // 与 5 个具名层之外的一切也断开，语义上贴近 Cocos（那里只有这几个组）。
                for (int layer = 0; layer < 32; layer++)
                {
                    if (!IsGameLayer(layer))
                    {
                        Physics2D.IgnoreLayerCollision(GameLayers[i], layer, true);
                    }
                }
            }

            // 逐对打开（含自碰）。
            Allow(World, World);
            Allow(World, Player);
            Allow(World, Enemy);

            Allow(Player, Arrow);

            Allow(Enemy, HitBox);

            Debug.Log("[PhysicsGroups] 碰撞矩阵已应用（WORLD/PLAYER/ENEMY/HITBOX/ARROW）");
        }

        private static void Allow(int a, int b)
        {
            Physics2D.IgnoreLayerCollision(a, b, false);
        }

        private static bool IsGameLayer(int layer)
        {
            for (int i = 0; i < GameLayers.Length; i++)
            {
                if (GameLayers[i] == layer)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
