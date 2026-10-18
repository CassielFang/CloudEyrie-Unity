using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 输入快照（<see cref="QingheController"/> 每帧填充；jump/dash 为边沿锁存）。
    ///
    /// ⚠️ 这是 <c>struct</c>，且**必须作为 <see cref="FormContext"/> 的字段**存在
    /// （不能是属性）—— 形态状态类会原地改写 <c>ctx.Input.Jump</c>。
    /// 写成属性的话改写会作用在一份临时副本上，边沿消费静默失效。
    /// </summary>
    public struct MoveInput
    {
        public bool Left;
        public bool Right;
        public bool Up;
        public bool Down;
        public bool Jump;
        public bool Dash;

        /// <summary>跳跃键是否**按住**（不是按下那一帧）。用来做可变跳跃高度。</summary>
        public bool JumpHeld;
    }

    /// <summary>移动参数（由 <see cref="QingheController"/> 的 Inspector 字段填充，注入各形态状态类）。</summary>
    public class MovementParams
    {
        public float MoveSpeed;
        public float JumpSpeed;

        /// <summary>二段跳（第二跳）的速度倍率。<c>1</c> = 和第一跳一样高；调小则二段跳更矮。</summary>
        public float SecondJumpMult;

        public int MaxJumpCount;
        public float DashSpeed;
        public float DashDuration;
        public float MistFlySpeed;
        public float MergeMoveSpeed;

        // ---- 跳跃手感 ----
        /// <summary>离开地面后仍可起跳的宽限 /秒</summary>
        public float CoyoteTime;

        /// <summary>落地前按下跳跃的预输入窗口 /秒</summary>
        public float JumpBufferTime;

        /// <summary>上升中松开跳跃时，竖直速度乘以这个系数（可变跳跃高度）</summary>
        public float JumpCutMultiplier;

        /// <summary>上升段的额外重力倍率（小于 1 = 上升更飘）</summary>
        public float RiseGravityScale;

        /// <summary>下落段的额外重力倍率（大于 1 = 落得更利落）</summary>
        public float FallGravityScale;

        /// <summary>二段跳时，按住方向键给的水平速度倍率（0 = 不给水平加速）</summary>
        public float SecondJumpHorizontalBoost;
    }

    /// <summary>
    /// 形态上下文：状态类共享的可变状态。
    /// 对应 Cocos 侧 <c>FormContext</c>。
    /// </summary>
    public class FormContext
    {
        /// <summary>朝向 1=右 -1=左</summary>
        public float Facing = 1f;

        /// <summary>是否着地</summary>
        public bool Grounded;

        /// <summary>是否冲刺中（状态写，控制器读）</summary>
        public bool IsDashing;

        /// <summary>是否攻击中（战斗控制器写，形态读）</summary>
        public bool IsAttacking;

        /// <summary>是否格挡中（战斗控制器写，形态读）</summary>
        public bool IsBlocking;

        /// <summary>输入快照</summary>
        public MoveInput Input;

        /// <summary>本帧刚起跳（一段）。表现层读完即清。</summary>
        [System.NonSerialized] public bool Jumped;

        /// <summary>
        /// 本帧刚用掉二段跳（由 <see cref="EntityForm"/> 写，QingheController 读完即清）。
        /// 表现层/特效据此播「云跃」；玩法逻辑不要拿它做判断。
        /// </summary>
        [System.NonSerialized] public bool DoubleJumped;

        public readonly Rigidbody2D Rigidbody;
        public readonly MovementParams Move;

        public FormContext(Rigidbody2D rigidbody, MovementParams move)
        {
            Rigidbody = rigidbody;
            Move = move;
        }
    }
}
