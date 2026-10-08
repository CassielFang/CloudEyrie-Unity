using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 灵雾形态（莹翳主导）：八向自由飞行。
    /// 对应 Cocos 侧 <c>character/forms/MistForm.ts</c>。
    /// </summary>
    public class MistForm : ICompanionForm
    {
        public CompanionForm Form
        {
            get { return CompanionForm.Mist; }
        }

        /// <summary>是否高速飞行（决定额外消耗）</summary>
        private bool _flying;

        public void Update(float dt, FormContext ctx)
        {
            Rigidbody2D rigidBody = ctx.Rigidbody;
            MovementParams move = ctx.Move;

            // 灵雾无跳跃/冲刺，清掉残留请求
            ctx.Input.Jump = false;
            ctx.Input.Dash = false;

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

            rigidBody.linearVelocity = new Vector2(h * move.MistFlySpeed, v * move.MistFlySpeed);

            _flying = h != 0f || v != 0f;
        }

        public float DrainPerSecond()
        {
            SpiritConfig spirit = GameConfig.Current.Spirit;

            return _flying
                ? spirit.MistBaseDrain + spirit.MistFlyDrainExtra
                : spirit.MistBaseDrain;
        }
    }
}
