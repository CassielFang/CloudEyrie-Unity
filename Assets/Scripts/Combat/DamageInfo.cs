using UnityEngine;

namespace CloudEyrie
{
    /// <summary>一次伤害结算的数据。对应 Cocos 侧 <c>DamageInfo</c>。</summary>
    public struct DamageInfo
    {
        public float Amount;

        /// <summary>击退向量（世界方向，x 长度即击退距离）</summary>
        public Vector2 Knockback;

        /// <summary>伤害来源形态</summary>
        public CompanionForm SourceForm;

        /// <summary>是否灵弹（后续阴浊显形区分用）</summary>
        public bool IsMistShot;
    }

    /// <summary><see cref="GameEvents.EnemyDied"/> 事件的载荷。</summary>
    public struct EnemyDiedEvent
    {
        public GameObject Node;
        public string Name;
    }

    /// <summary>
    /// 玩家受击结算结果。
    ///
    /// 原先是直接返回数字，但「完美格挡」返回的 0 和「没扣到灵炁」无法区分，
    /// 攻击方分不出该不该反制 —— 故改成结构体。
    /// </summary>
    public struct PlayerDamageResult
    {
        /// <summary>实际应扣除的灵炁量</summary>
        public float SpiritCost;

        /// <summary>是否完美格挡（此时 <see cref="SpiritCost"/> 恒为 0，攻击方应被反制）</summary>
        public bool PerfectBlocked;
    }
}
