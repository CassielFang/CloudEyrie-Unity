using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 一切场景机关的基类。
    ///
    /// <b>它只统一一件事：谁被触发了。</b>
    ///
    /// 设计稿里五行一共 18 个机关，触发方式看着五花八门（踩踏 / 攻击点燃 / 靠近 /
    /// 站立计时 / 灵合共鸣 / 形态判定 / 自动周期…），但拆开看只有两件事：
    /// <list type="number">
    ///   <item><b>什么时候算被触发</b> —— 这一层交给 Unity 的碰撞回调与
    ///     <see cref="MechanismTrigger"/>，**不要自己造状态机**</item>
    ///   <item><b>触发之后干什么</b> —— 这才是每个机关自己的逻辑，即
    ///     <see cref="OnActivate"/></item>
    /// </list>
    ///
    /// 所以基类只留一个入口，不预设任何"机关生命周期"。
    /// （`docs/archive/` 里那份行为树设计文档就是因为把两层混在一起，最后没落地。）
    ///
    /// <b>摆放要求</b>：机关的 GameObject 必须归 <c>WORLD</c> 层。
    /// 留在 <c>Default</c> 层的话 <see cref="PhysicsGroups.Apply"/> 会把游戏层与它断开 ——
    /// 玩家走过去不会有任何反应，也**不报错**。
    /// 摆完跑一次「CloudEyrie/校验当前场景」能抓出来。
    /// </summary>
    /// <remarks>
    /// <b>基类刻意不提供 <c>oneShot</c> / 冷却 / 状态机。</b>
    /// 试过：加了「只能触发一次」的开关之后，弹跳花（每次踩都要弹）和藤蔓种子
    /// （长完就永久存在）就得靠一个 bool 区分，而**默认值不管取哪边都会让另一边出错**，
    /// 且错的表现是静默的（机关不响应）。这类状态是每个机关自己的事，放在子类里才看得见。
    /// </remarks>
    public abstract class LevelMechanism : MonoBehaviour
    {
        /// <summary>
        /// 触发入口。<paramref name="who"/> 是触发者（一般是玩家）。
        /// 子类自行决定要不要去重 / 加冷却。
        /// </summary>
        public void Activate(GameObject who)
        {
            OnActivate(who);
        }

        /// <summary>子类实现：被触发之后干什么。</summary>
        protected abstract void OnActivate(GameObject who);

        /// <summary>
        /// 从碰撞体反查玩家。
        ///
        /// 用 <c>GetComponentInParent</c> 而不是 <c>GetComponent</c>：
        /// 玩家的碰撞体可能在子节点上（命中盒、骨骼立绘），直接取本节点会拿不到。
        /// </summary>
        protected static QingheController PlayerFrom(Collider2D other)
        {
            return other != null ? other.GetComponentInParent<QingheController>() : null;
        }
    }
}
