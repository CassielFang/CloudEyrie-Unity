using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 实体形态（青禾主导）：行走 / 二段跳 / 踏云闪。
    /// 对应 Cocos 侧 <c>character/forms/EntityForm.ts</c>。
    ///
    /// ⚠️ 两条来自实战的约定，改之前务必读：
    ///
    /// <b>1. 起跳时必须主动置 <c>ctx.Grounded = false</c>。</b>
    /// 物理接触回调（END_CONTACT）要等物理步进之后才到，不在此处主动置 false，
    /// 本帧末尾的「着地重置」会把刚加上去的 jumpCount 抹掉 ——
    /// 表现是「跳过一次后踏云闪（要求 jumpCount ≥ 1）不生效」。
    ///
    /// <b>2. 第二跳按倍率缩水。</b>
    /// 原样再加一个 jumpSpeed 会越跳越高，二段跳落点很难控制（实测反馈「跳太高了」）。
    /// </summary>
    public class EntityForm : ICompanionForm
    {
        public CompanionForm Form
        {
            get { return CompanionForm.Entity; }
        }

        private int _jumpCount;
        private float _dashTimer;
        private float _coyoteTimer;
        private float _jumpBufferTimer;
        private bool _jumpCutApplied;

        public void Update(float dt, FormContext ctx)
        {
            Rigidbody2D rigidBody = ctx.Rigidbody;
            MovementParams move = ctx.Move;
            Vector2 velocity = rigidBody.linearVelocity;

            // ---- 跳跃手感计时 ----
            // 土狼时间：离地后一小段时间内仍算「能起跳」。
            _coyoteTimer = ctx.Grounded
                ? move.CoyoteTime
                : Mathf.Max(0f, _coyoteTimer - dt);

            // 跳跃预输入：按下那一帧记下来，落地时还在窗口内就立刻起跳。
            if (ctx.Input.Jump)
            {
                _jumpBufferTimer = move.JumpBufferTime;
            }
            else
            {
                _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - dt);
            }

            // 冲刺（踏云闪：需已起跳）
            if (ctx.Input.Dash && !ctx.IsAttacking && !ctx.IsBlocking
                && _jumpCount >= 1 && !ctx.IsDashing)
            {
                // 灵炁不足就不起冲 —— 与其它动作（轻击 / 重击 / 灵弹）一致：**先查后扣**
                float cost = GameConfig.Current.Spirit.CloudDashCost;

                if (!SpiritEnergySystem.CanConsume(cost))
                {
                    Debug.Log("[Qinghe Controller] 灵炁不足，无法踏云闪");
                    ctx.Input.Dash = false;
                }
                else
                {
                    SpiritEnergySystem.Consume(cost);
                    velocity.x = ctx.Facing * move.DashSpeed;
                    ctx.IsDashing = true;
                    _dashTimer = 0f;
                    ctx.Input.Dash = false;
                    GameLog.V("[Qinghe Controller] start Dash");

                    // 音效放在**扣完灵炁之后** —— 灵炁不足时上面就 return 掉了，
                    // 那种情况不该响「踏云闪」。
                    //
                    // 冲刺特效不在这里生成：形态类拿不到身高与整体缩放（落点算不出来）。
                    // 由 QingheController 盯 ctx.IsDashing 的上升沿统一发 —— 两种形态共用一条。
                    GameAudio.PlaySfx(SfxCue.Dash);
                }
            }

            // 跳跃（一段 + 云跃式二段）
            //
            // 手感三件套：
            //   土狼时间 —— 刚离地也能跳；
            //   跳跃预输入 —— 落地前按下，落地当帧跳；
            //   可变高度 —— 上升中松开跳跃键，直接砍掉一部分竖直速度。
            bool canFirst = _jumpCount == 0 && (_coyoteTimer > 0f || ctx.Grounded);
            bool canSecond = _jumpCount > 0 && _jumpCount < move.MaxJumpCount;
            bool cloudLeaped = false;

            if (_jumpBufferTimer > 0f && (canFirst || canSecond)
                && !ctx.IsDashing && !ctx.IsAttacking && !ctx.IsBlocking)
            {
                if (_jumpCount == 0)
                {
                    // 一段跳：**叠加**。踩在上升气流 / 平台上时能借上力，是有意的。
                    velocity.y += move.JumpSpeed;
                    ctx.Jumped = true;
                }
                else
                {
                    // 二段跳 = **云跃**：竖直速度是固定值（与按下时机无关），
                    // 同时按住方向键会获得一次水平加速 —— 它是一个有方向的空中位移，
                    // 而不是"弱化版的一段跳"。
                    velocity.y = move.JumpSpeed * move.SecondJumpMult;

                    float h = 0f;
                    if (ctx.Input.Left) h -= 1f;
                    if (ctx.Input.Right) h += 1f;

                    if (Mathf.Abs(h) > 0.01f)
                    {
                        velocity.x = h * move.MoveSpeed
                                     * Mathf.Max(1f, move.SecondJumpHorizontalBoost);
                        ctx.Facing = Mathf.Sign(h);
                    }

                    cloudLeaped = true;
                    ctx.DoubleJumped = true;
                }

                _jumpCount += 1;

                // 见类注释 1
                ctx.Grounded = false;
                _coyoteTimer = 0f;
                _jumpBufferTimer = 0f;
                _jumpCutApplied = false;
                ctx.Input.Jump = false;
            }

            // 可变跳跃高度：只对玩家自己起跳生效（jumpCount > 0）。
            // 弹跳花/上升气流是直接给速度的，不该被"松手"砍掉。
            if (!_jumpCutApplied && _jumpCount > 0 && velocity.y > 0f && !ctx.Input.JumpHeld)
            {
                velocity.y *= Mathf.Clamp01(move.JumpCutMultiplier);
                _jumpCutApplied = true;
            }

            // 水平移动
            if (!ctx.IsDashing && !ctx.IsBlocking)
            {
                float h = 0f;
                if (ctx.Input.Left) h -= 1f;
                if (ctx.Input.Right) h += 1f;

                if (h > 0f)
                {
                    ctx.Facing = 1f;
                }
                else if (h < 0f)
                {
                    ctx.Facing = -1f;
                }

                // 云跃本帧已经给了更大的水平速度，别在这里又被普通移动覆盖掉
                if (h != 0f && !cloudLeaped)
                {
                    velocity.x = h * move.MoveSpeed;
                }
                else if (ctx.Grounded)
                {
                    // ⚠️ **松开即停**。
                    //
                    // Cocos 原代码这里是 `if (h !== 0) velocity.x = ...` —— 没按键时**不写**速度，
                    // 靠地面摩擦把它磨停（玩家碰撞体默认摩擦 0.2）。
                    // 但那个前提在迁移后不成立了：Unity 这边给青禾设的是 friction = 0，
                    // 于是没按键时速度永远是上一帧的值 → **角色一直滑、停不下来**。
                    //
                    // 改成显式归零，比依赖摩擦更可控，也不受物理材质配置影响。
                    // 只在**着地时**归零：空中保留惯性，这是横版动作的常规手感。
                    velocity.x = 0f;
                }
            }

            // 冲刺计时
            if (ctx.IsDashing)
            {
                _dashTimer += dt;

                if (_dashTimer >= move.DashDuration)
                {
                    ctx.IsDashing = false;
                    _dashTimer = 0f;
                    velocity.x = 0f;
                    GameLog.V("[Qinghe Controller] Dash end");
                }
            }

            // 着地重置二段跳。
            //
            // ⚠️ 这里**只依赖** ctx.Grounded，不在本地再加判断 —— 因为
            // QingheController 已经把「上升中不算着地」收在 RefreshGrounded 里了
            // （见它的 IsRising）。曾经这里只堵住了「起跳当帧」，而擦地那一两步
            // 会被 OnCollisionStay2D 翻回 true，把 _jumpCount 抹掉 → 能连跳三次。
            // 判定要留在接地那侧，否则两处各判一半，改一处就会漏。
            if (ctx.Grounded)
            {
                _jumpCount = 0;
            }

            // 上升/下落用不同重力倍率：上升更飘、下落更利落。
            // 只对玩家自己起跳生效 —— 弹跳花/上升气流是直接给速度的，
            // 改它们的重力会让已经调好的弹跳高度全部变样。
            if (!ctx.Grounded && !ctx.IsDashing && _jumpCount > 0)
            {
                rigidBody.gravityScale = velocity.y > 0f
                    ? Mathf.Max(0.05f, move.RiseGravityScale)
                    : Mathf.Max(0.05f, move.FallGravityScale);
            }
            else if (!ctx.IsDashing)
            {
                rigidBody.gravityScale = 1f;
            }

            rigidBody.linearVelocity = velocity;
        }

        /// <summary>
        /// 恒为 0 —— 实体形态没有**按秒**消耗。
        ///
        /// 踏云闪的灵炁花费是一次性的，在起冲那一刻扣（见 <see cref="Update"/>），
        /// 不走这条每秒通道。
        ///
        /// 变更记录：Cocos 版里 <c>cloudDashCost</c> 配了但**没接线**（起冲不扣灵炁），
        /// 是本项目已知的待补项。Unity 这一版按团队决定补上了。
        /// </summary>
        public float DrainPerSecond()
        {
            return 0f;
        }
    }
}
