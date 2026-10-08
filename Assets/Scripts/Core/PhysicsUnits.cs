using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// Cocos 物理单位 ↔ Unity 世界单位的换算。
    ///
    /// <b>为什么需要这个文件</b>
    ///
    /// Cocos Creator 的 2D 物理走 Box2D，而 Box2D 期望「米」量级的尺寸。
    /// Cocos 用 <c>PHYSICS_2D_PTM_RATIO = 32</c>（像素/米）在两者之间转换，但**只转位置，不转速度**：
    ///
    /// <code>
    /// // cocos/physics-2d/framework/physics-types.ts
    /// export const PHYSICS_2D_PTM_RATIO = 32;
    ///
    /// // cocos/physics-2d/box2d/rigid-body.ts —— 位置同步要除 PTM
    /// temp.x = pos.x / PHYSICS_2D_PTM_RATIO;
    ///
    /// // 而设置速度是**直接透传**，没有任何换算
    /// setLinearVelocity (v) { this._body.SetLinearVelocity(v); }
    /// </code>
    ///
    /// 所以 Cocos 里 <c>linearVelocity = 5</c> 是 **5 米/秒 = 160 像素/秒**。
    ///
    /// ⚠️ 照搬到 Unity 会**慢 32 倍**（Unity 的 <c>Rigidbody2D.linearVelocity</c> 就是世界单位/秒，
    /// 而本工程 1 世界单位 = 1 像素）。迁移时踩过：角色像在地上爬。
    ///
    /// <b>本工程的约定</b>
    ///
    /// 所有「速度」类数值**已经换算好、直接就是世界单位/秒**（即像素/秒），
    /// 配置里存的就是真实生效值，不再乘 32。每个字段的注释里记着它的 Cocos 原值。
    /// </summary>
    public static class PhysicsUnits
    {
        /// <summary>Cocos 的像素/米换算比（引擎常量）。</summary>
        public const float CocosPtmRatio = 32f;

        /// <summary>
        /// Cocos 物理速度 → Unity 世界单位/秒 的倍率。
        /// 只在需要把 Cocos 原值现场换算时使用；配置里存的是换算后的值。
        /// </summary>
        public const float VelocityScale = CocosPtmRatio;

        /// <summary>
        /// 重力。Cocos 默认是 <c>(0, -10 × 32) = (0, -320)</c> 像素/秒²
        /// （见 <c>physics-system.ts</c> 的 <c>_gravity = new Vec2(0, -10 * PHYSICS_2D_PTM_RATIO)</c>）。
        ///
        /// ⚠️ Unity 的默认重力是 <c>-9.81</c>，**必须改成这个**，否则跳跃高度对不上：
        /// 同样的初速在 -9.81 下会飞 32 倍高。这一条和速度换算是配套的，改一个必须改另一个。
        /// </summary>
        public static readonly Vector2 Gravity = new Vector2(0f, -320f);

        /// <summary>把 Cocos 的物理速度值换算成世界单位/秒。</summary>
        public static float FromCocosVelocity(float cocosValue)
        {
            return cocosValue * VelocityScale;
        }

        /// <summary>
        /// 应用全局物理设置。与 <see cref="PhysicsGroups.Apply"/> 一起由
        /// <see cref="GameManager"/> 在启动时调用。
        /// </summary>
        public static void Apply()
        {
            Physics2D.gravity = Gravity;
            Debug.Log($"[PhysicsUnits] 重力设为 {Gravity}（Cocos 的 -320 px/s²）");
        }
    }
}
