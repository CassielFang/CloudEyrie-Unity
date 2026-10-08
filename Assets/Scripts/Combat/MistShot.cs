using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 灵雾灵弹：沿方向直线飞行，命中 <see cref="Damageable"/> 造成伤害后自毁；超时自毁。
    /// 对应 Cocos 侧 <c>combat/MistShot.ts</c>。
    /// 由 <see cref="CombatController"/> 生成，并调用 <see cref="Init"/> 设定参数。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    public class MistShot : MonoBehaviour
    {
        private Rigidbody2D _rigidBody;
        private BoxCollider2D _collider;

        private Vector2 _dir = new Vector2(1f, 0f);
        private float _speed = 10f;
        private float _damage;
        private float _life;
        private float _lifetime = 2f;

        /// <summary>
        /// 本帧已判定撞上的目标，等 <c>Update</c> 里再结算。
        ///
        /// ⚠️ <b>不在这里直接结算 + Destroy</b> —— Cocos 那边 Box2D 不允许在接触回调里
        /// 动刚体（报 <c>Can not active RigidBody in contact listener</c>），
        /// <c>MinionArrow</c> 在同一个坑上栽过。Unity 对在触发器回调里销毁对象是允许的，
        /// 但「记个标记、下一帧结算」这个约定更稳，也统一了两条判定路径，故照抄。
        /// </summary>
        private Damageable _pendingTarget;

        /// <summary>出生时由 <see cref="CombatController"/> 传进来的可受击目标（和近战共用那份缓存）</summary>
        private Damageable[] _targets = new Damageable[0];

        /// <summary>
        /// 矩形重叠判定用的半边长（世界单位）。
        ///
        /// 用的是**美术尺寸**（灵弹显示 28）的一半，比物理体（18）略宽松，手感上更跟手。
        /// 抛射物本身就是个小矩形，直接算重叠既简单又一眼能验。
        /// </summary>
        private const float HitHalf = 14f;

        private void Awake()
        {
            _rigidBody = GetComponent<Rigidbody2D>();
            _collider = GetComponent<BoxCollider2D>();

            if (_rigidBody != null)
            {
                _rigidBody.gravityScale = 0f;
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_pendingTarget != null)
            {
                return;
            }

            Damageable damageable = other.GetComponentInParent<Damageable>();

            if (damageable != null)
            {
                _pendingTarget = damageable;
            }
        }

        /// <summary>
        /// **按矩形重叠判定命中**，不依赖物理接触。
        ///
        /// 为什么接触之外还要留这一条：接触链路要同时穿过「碰撞分组 → 触发器配置 → 层矩阵」
        /// 几道关，**任何一道出错都是静默失败**，而且每一处读回来的值看着都是对的。
        /// （Cocos 版为此白跑过两轮排查，见 <c>MistShot.ts</c> 的注释。）
        ///
        /// 两条路都汇到 <see cref="_pendingTarget"/>，所以不会重复结算。
        /// </summary>
        private Damageable HitTest()
        {
            Vector2 a = transform.position;

            foreach (Damageable t in _targets)
            {
                if (t == null || !t.gameObject.activeInHierarchy)
                {
                    continue;
                }

                // 目标体积取其 BoxCollider2D；拿不到就退回一个球员大小的默认盒子
                BoxCollider2D col = t.GetComponent<BoxCollider2D>();
                bool usable = col != null && col.size.x > 1f && col.size.y > 1f;
                float halfW = usable ? col.size.x * 0.5f : 40f;
                float halfH = usable ? col.size.y * 0.5f : 75f;
                float offY = usable ? col.offset.y : 0f;

                Vector2 p = t.transform.position;

                if (Mathf.Abs(a.x - p.x) > halfW + HitHalf)
                {
                    continue;
                }

                if (Mathf.Abs(a.y - (p.y + offY)) > halfH + HitHalf)
                {
                    continue;
                }

                return t;
            }

            return null;
        }

        public void Init(Vector2 dir, float speed, float damage, float lifetime = 2f,
                         Damageable[] targets = null)
        {
            _dir = dir.normalized;
            _speed = speed;
            _damage = damage;
            _lifetime = lifetime;
            _life = 0f;
            _targets = targets ?? new Damageable[0];
        }

        private void Update()
        {
            // 命中结算放在物理回调之外（见 _pendingTarget 的说明）
            if (_pendingTarget != null)
            {
                Damageable target = _pendingTarget;
                _pendingTarget = null;

                target.TakeDamage(new DamageInfo
                {
                    Amount = _damage,
                    // 击退量走配置（Cocos 那边原先硬编码 0.5，实测推得太远）
                    Knockback = new Vector2(_dir.x * GameConfig.Current.Combat.MistShotKnockback, 0f),
                    SourceForm = CompanionForm.Mist,
                    IsMistShot = true,
                });

                // 命中：形态固定传灵雾（这颗弹本来就是灵雾形态发的），
                // 于是炸开的是**软雾**而不是墨点 —— 与「灵雾=圆、慢、无方向」一致
                InkFx.Burst(transform.position, InkFx.Kind.Impact,
                            CompanionForm.Mist, Vector2.zero, 1.2f);

                Debug.Log($"[MistShot] 命中 {target.name}，伤害 {_damage}");
                Destroy(gameObject);
                return;
            }

            _life += Time.deltaTime;

            if (_life >= _lifetime)
            {
                Destroy(gameObject);
                return;
            }

            if (_rigidBody != null)
            {
                _rigidBody.linearVelocity = new Vector2(_dir.x * _speed, _dir.y * _speed);
            }
            else
            {
                Vector3 p = transform.position;
                transform.position = new Vector3(
                    p.x + _dir.x * _speed * Time.deltaTime,
                    p.y + _dir.y * _speed * Time.deltaTime,
                    p.z);
            }

            // 走完这一步再判重叠：触发器事件是异步来的，这条是每帧自己算的，
            // 两条路谁先发现都行，结算都走上面那段
            Damageable hit = HitTest();

            if (hit != null)
            {
                _pendingTarget = hit;
            }
        }
    }
}
