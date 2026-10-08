using System;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 可受击组件：敌人 / Boss / 可破坏物统一挂载，只负责「血量 + 受击 + 死亡」。
    /// 对应 Cocos 侧 <c>combat/Damageable.ts</c>。敌人 AI（行为树）**不在此组件内**。
    /// </summary>
    public class Damageable : MonoBehaviour
    {
        [Tooltip("最大生命")]
        [SerializeField] private float maxHp = 100f;

        /// <summary>
        /// 免伤开关。Boss 第一阶段「未识破前青禾的攻击无效」靠它实现。
        ///
        /// 放在这个通用底座上而不是写 Boss 的子类：可受击物都可能需要无敌帧/阶段免伤，
        /// 且 <see cref="CombatController"/> 的命中判定只认 <see cref="Damageable"/>，
        /// 子类会被它按基类拿到。
        /// </summary>
        [NonSerialized] public bool Invulnerable;

        private float _hp;
        private Rigidbody2D _rigidBody;

        /// <summary>死亡前的自定义处理（敌人 AI 可挂上，播放死亡表现后自行停用）。未设置时立即停用节点。</summary>
        public Action<Damageable> OnDeath;

        /// <summary>
        /// 受击回调（仅在未致死时触发）。挂上后**接管击退**：本组件不再覆写水平速度，
        /// 由接收方自己决定击退曲线。敌人 AI 用它驱动受击硬直（含完美格挡反制）。
        /// </summary>
        public Action<Damageable, DamageInfo> OnDamaged;

        private void Awake()
        {
            _hp = maxHp;
            _rigidBody = GetComponent<Rigidbody2D>();
        }

        public float GetHp()
        {
            return _hp;
        }

        public float GetMaxHp()
        {
            return maxHp;
        }

        /// <summary>剩余血量比例 [0,1]，阶段阈值判定与 UI 用</summary>
        public float GetHpRatio()
        {
            return maxHp > 0f ? Mathf.Max(0f, _hp) / maxHp : 0f;
        }

        public bool IsDead()
        {
            return _hp <= 0f;
        }

        /// <summary>
        /// 覆盖血量上限，并把当前血量重置为满。
        /// Boss 这类数值由 <see cref="GameConfig"/> 驱动的对象用（<c>maxHp</c> 只是 Inspector 默认值）。
        /// </summary>
        public void SetMaxHp(float value)
        {
            maxHp = Mathf.Max(1f, value);
            _hp = maxHp;
        }

        public void TakeDamage(DamageInfo info)
        {
            if (!gameObject.activeInHierarchy || _hp <= 0f)
            {
                return;
            }

            if (Invulnerable)
            {
                return;
            }

            _hp -= info.Amount;

            GameLog.V($"[Damageable] {name} 受击 {info.Amount}，剩余 {Mathf.Max(0f, _hp)}/{maxHp}");

            if (_hp <= 0f)
            {
                _hp = 0f;
                Die();
                return;
            }

            // 击退：挂了 OnDamaged 的（敌人 AI）自己接管。
            // 它要的是「整段受击硬直内的位移总量」，而不是这里写死的一次速度覆盖 ——
            // AI 每帧都会写水平速度，继续在这里写等于白写。
            if (OnDamaged != null)
            {
                OnDamaged(this, info);
                return;
            }

            // 未被接管时的默认行为：有刚体则覆盖水平速度（竖直分量保留）
            if (_rigidBody != null && info.Knockback.sqrMagnitude > 0f)
            {
                Vector2 v = _rigidBody.linearVelocity;
                _rigidBody.linearVelocity = new Vector2(info.Knockback.x, v.y);
            }
        }

        private void Die()
        {
            EventBus.Emit(GameEvents.EnemyDied, new EnemyDiedEvent { Node = gameObject, Name = name });

            if (OnDeath != null)
            {
                OnDeath(this);
                return;
            }

            gameObject.SetActive(false);
        }
    }
}
