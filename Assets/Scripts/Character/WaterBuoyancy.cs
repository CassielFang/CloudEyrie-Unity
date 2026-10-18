using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 玩家在水下的浮力：沉到水面以下就被托着往上浮，直到露出水面。
    ///
    /// 设计稿的「涨水」不只是抬高平台，它要能把人托上去、也能把低处通道淹掉。
    /// 这里只做**托举**这一半：水线以上完全不管，水线以下把竖直速度推向一个向上的定值。
    ///
    /// <b>为什么只对实体形态生效</b>：灵雾与灵合本来就无视重力（`ApplyFormPhysics` 关掉了
    /// gravity），再叠一层浮力会让这两种形态在水里乱飘。
    ///
    /// <b>为什么不改 gravityScale</b>：那是形态切换在管的字段，两边都写会互相覆盖，
    /// 表现成"出水后重力没恢复"或"进水后不浮"，且很难定位。
    /// </summary>
    public class WaterBuoyancy : MonoBehaviour
    {
        [Tooltip("水下上升速度（世界单位/秒）。太大像电梯，太小浮不上来")]
        [SerializeField] private float riseSpeed = 90f;

        [Tooltip("把竖直速度推向 riseSpeed 的加速度")]
        [SerializeField] private float riseAccel = 900f;

        private Rigidbody2D _body;
        private QingheController _qinghe;

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _qinghe = GetComponent<QingheController>();
        }

        private void FixedUpdate()
        {
            WaterLevel water = WaterLevel.Instance;

            // 没有水位系统（木/火/土/金）→ 完全不介入
            if (water == null || _body == null)
            {
                return;
            }

            // 只有实体形态踩水；灵雾/灵合本来就无视重力
            if (_qinghe != null && _qinghe.CurrentForm != CompanionForm.Entity)
            {
                return;
            }

            // 露在水面以上就不管
            if (transform.position.y > water.CurrentY)
            {
                return;
            }

            Vector2 v = _body.linearVelocity;
            v.y = Mathf.MoveTowards(v.y, riseSpeed, riseAccel * Time.fixedDeltaTime);
            _body.linearVelocity = v;
        }
    }
}
