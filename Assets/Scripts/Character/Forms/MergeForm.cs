using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 灵合形态（二人合一）：八向悬浮（快）+ 冲刺。
    /// 对应 Cocos 侧 <c>character/forms/MergeForm.ts</c>。
    ///
    /// <b>这一形态只有「移动」在这里</b>；<b>攻击全在
    /// <see cref="CombatController"/> 里</b>（形态状态类不管战斗，见那边的分层）：
    /// <list type="bullet">
    ///   <item><b>净化光环</b>：自动、持续 —— 半径 <c>MergePurifyRadius</c> 内每秒
    ///     <c>MergePurifyDamagePerSec</c>，每 <c>AuraTick</c> 结算一跳
    ///     （<c>CombatController.TickMergeAura</c>）。</item>
    ///   <item><b>灵炁爆发</b>：按**攻击键**触发的一次性 AOE
    ///     （<c>CombatController.FireMergeBurst</c>），每次灵合限一次。</item>
    /// </list>
    ///
    /// ⚠️ 这里原来写着「净化光环与灵炁爆发尚未实现，配置项无人消费」——
    /// **那句已经过期了**（Unity 侧在 CombatController 里补上了，Cocos 侧也确实缺）。
    /// 看到「配置项存在但没人用」的注释时，先去 `grep` 一遍配置项名再信它。
    ///
    /// 本类只负责：八向悬浮移动、冲刺、以及灵合的**每秒维持消耗**
    /// （<see cref="DrainPerSecond"/> = 净化光环的代价）。
    /// </summary>
    public class MergeForm : ICompanionForm
    {
        public CompanionForm Form
        {
            get { return CompanionForm.Merge; }
        }

        private float _dashTimer;

        public void Update(float dt, FormContext ctx)
        {
            Rigidbody2D rigidBody = ctx.Rigidbody;
            MovementParams move = ctx.Move;
            Vector2 velocity = rigidBody.linearVelocity;

            // 灵合无跳跃，清掉残留
            ctx.Input.Jump = false;

            // 冲刺（无需起跳 —— 与实体形态的踏云闪不同）
            if (ctx.Input.Dash && !ctx.IsAttacking && !ctx.IsDashing)
            {
                velocity.x = ctx.Facing * move.DashSpeed;
                ctx.IsDashing = true;
                _dashTimer = 0f;
                ctx.Input.Dash = false;
                Debug.Log("[Qinghe Controller] start Dash");
                GameAudio.PlaySfx(SfxCue.Dash);
            }

            // 八向悬浮
            if (!ctx.IsDashing)
            {
                float h = 0f;
                float v = 0f;

                if (ctx.Input.Left) h -= 1f;
                if (ctx.Input.Right) h += 1f;
                if (ctx.Input.Up) v += 1f;
                if (ctx.Input.Down) v -= 1f;

                if (h > 0f)
                {
                    ctx.Facing = 1f;
                }
                else if (h < 0f)
                {
                    ctx.Facing = -1f;
                }

                velocity.x = h * move.MergeMoveSpeed;
                velocity.y = v * move.MergeMoveSpeed;
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
                    Debug.Log("[Qinghe Controller] Dash end");
                }
            }

            rigidBody.linearVelocity = velocity;
        }

        public float DrainPerSecond()
        {
            return GameConfig.Current.Spirit.MergePurifyCostPerSec;
        }
    }
}
