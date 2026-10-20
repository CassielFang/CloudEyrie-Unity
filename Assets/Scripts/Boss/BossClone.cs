using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// Boss P1「幻影」的诱饵身位（移植自 Cocos 版 <c>boss/BossClone.ts</c>）。
    ///
    /// 诱饵**只有立绘**：没有碰撞体、没有血量。伤害永远只有真身（挂
    /// <see cref="BossAI"/> 的根节点）一个来源，不必在多份 Damageable 之间搬运。
    /// 代价是刀打在诱饵上会穿过去、没有打击反馈 —— 这与 Cocos 版的行为一致。
    /// </summary>
    public class BossClone : MonoBehaviour
    {
        /// <summary>这一身是不是真身（由 BossAI 每轮重排时指派）</summary>
        public bool isReal;

        /// <summary>是否已被识破（识破后真身显纹路、假身压暗）</summary>
        public bool identified;
    }
}
