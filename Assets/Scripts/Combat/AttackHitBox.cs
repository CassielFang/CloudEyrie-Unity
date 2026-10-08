using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 挂在青禾的 <c>AttackHitBox</c> 子节点上，维护「命中盒当前和谁重叠」这个集合。
    ///
    /// <b>为什么需要这一层</b>
    ///
    /// Cocos 版把接触回调直接注册在 <c>BoxCollider2D</c> 上
    /// （<c>collider.on(Contact2DType.BEGIN_CONTACT, ...)</c>），因为 Cocos 允许
    /// 给任意碰撞体挂回调。Unity 的碰撞/触发消息是**发给 MonoBehaviour 的**
    /// （<c>OnTriggerEnter2D</c> 等），所以命中盒节点上必须有一个组件来接收。
    ///
    /// <b>与 Cocos 版的差异（简化，且是刻意的）</b>
    ///
    /// Cocos 那份代码为「接触事件不生效」付出了很大代价，注释里记了三条同源的坑：
    ///   - <c>RigidBody2D.enabledContactListener</c> 默认 false，运行时建的刚体没人勾选
    ///     → 回调一次都不触发（灵弹「穿过去、无伤害、不自毁」的病根）；
    ///   - 静止约 0.5s 后刚体休眠，Box2D 对双方都休眠的接触对不再评估
    ///     → 「站着不动打不到、一走又能打到」，要用 <c>allowSleep = false</c> 绕；
    ///   - 传感器之间不产生接触 → 敌人不能用传感器。
    ///
    /// **这三条在 Unity 里都不存在**：触发消息只要求「至少一方有 Rigidbody2D」，
    /// 与刚体是否休眠无关，也不需要在组件上勾任何开关。所以这里直接老实收
    /// <c>OnTriggerEnter2D/Exit2D</c> 即可，一行 workaround 都不需要。
    ///
    /// ⚠️ 命中盒的碰撞体要**常开**（<c>isTrigger = true</c>，不要按攻击节奏开关）：
    /// 集合靠 enter/exit 维护，若攻击时才启用碰撞体，那么「开打时目标就已经在框内」
    /// 这种情况不会有 enter 事件，集合是空的、打不到人。
    /// </summary>
    // 用具体类型而不是抽象的 Collider2D —— RequireComponent 要求能实例化的类型，
    // 传抽象基类时 Unity 加不上组件、会报错。
    [RequireComponent(typeof(BoxCollider2D))]
    public class AttackHitBox : MonoBehaviour
    {
        private readonly HashSet<Damageable> _overlapping = new HashSet<Damageable>();

        /// <summary>
        /// 把当前重叠集合**拷贝**到调用方的缓冲区，供其安全遍历。
        ///
        /// ⚠️ <b>必须拷贝，不能直接返回集合作遍历。</b>
        ///
        /// 遍历过程中 `CombatController.TryDamage` 可能**打死敌人** → 敌人
        /// `SetActive(false)` → Unity 触发本组件的 `OnTriggerExit2D` → 集合被改
        /// → 正在跑的枚举器立刻抛
        /// `InvalidOperationException: Collection was modified`。
        ///
        /// （实测踩到：击杀过一个小怪之后就报这个。Cocos 那边没这问题 ——
        ///   它的接触回调是异步派发的，不会在遍历中途插进来。）
        ///
        /// 用调用方传入的缓冲区是为了**避免每帧分配**：攻击判定期每帧都会调一次。
        /// </summary>
        public void CopyOverlapping(List<Damageable> buffer)
        {
            buffer.Clear();

            foreach (Damageable d in _overlapping)
            {
                if (d != null)
                {
                    buffer.Add(d);
                }
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            Damageable damageable = other.GetComponentInParent<Damageable>();

            if (damageable != null)
            {
                _overlapping.Add(damageable);
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            Damageable damageable = other.GetComponentInParent<Damageable>();

            if (damageable != null)
            {
                _overlapping.Remove(damageable);
            }
        }

        private void OnDisable()
        {
            _overlapping.Clear();
        }
    }
}
