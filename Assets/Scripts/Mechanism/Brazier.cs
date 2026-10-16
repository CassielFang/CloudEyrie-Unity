using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 火盆：**用攻击点燃**。设计稿火境段3 的教学点 ——
    /// 「攻击不只能打敌人，也能和场景物件互动」。
    ///
    /// <b>为什么挂在 ENEMY 层、靠 Damageable 收攻击</b>
    ///
    /// 玩家的 `AttackHitBox` 是 HITBOX 层的传感器，而碰撞矩阵里
    /// **HITBOX 只和 ENEMY 碰**（见 <see cref="PhysicsGroups"/>）。
    /// 所以"能被砍到的东西"必须归 ENEMY 层 —— 这与 <see cref="Damageable"/> 给敌人用是同一条路。
    /// 把火盆放 WORLD 层的话，玩家能站上去、却**永远砍不亮它**，而且不报错。
    ///
    /// 用 <see cref="Damageable"/> 收攻击而不是自己写重叠检测：命中去重、
    /// 击退、伤害类型这些 <c>CombatController</c> 已经处理好了，不必再来一遍。
    /// </summary>
    [RequireComponent(typeof(Damageable))]
    public class Brazier : LevelMechanism
    {
        [Tooltip("同一组的火盆全部点燃后，同组的火墙才会熄灭")]
        [SerializeField] private string groupId = "huo_03";

        [Tooltip("点燃时的提示。留空则不提示")]
        [SerializeField] private string hint = "火盆已点燃";

        [Header("表现")]
        [Tooltip("未点燃时的立绘节点")]
        [SerializeField] private GameObject visualOff;

        [Tooltip("点燃后的立绘节点（火焰）")]
        [SerializeField] private GameObject visualOn;

        [Tooltip("点燃时闪一下的光晕。留空则跳过")]
        [SerializeField] private Transform glow;

        [SerializeField] private float glowDuration = 0.6f;

        /// <summary>同一组的火盆都在这里登记，火墙据此判断。</summary>
        private static readonly Dictionary<string, List<Brazier>> Groups =
            new Dictionary<string, List<Brazier>>();

        public string GroupId
        {
            get { return groupId; }
        }

        public bool IsLit { get; private set; }

        /// <summary>某一组是不是全亮了。场上一个都没有时返回 false（否则空组会被当成已解锁）。</summary>
        public static bool GroupAllLit(string id)
        {
            if (string.IsNullOrEmpty(id) || !Groups.TryGetValue(id, out List<Brazier> list) || list.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && !list[i].IsLit)
                {
                    return false;
                }
            }

            return true;
        }

        private void Awake()
        {
            gameObject.layer = PhysicsGroups.Enemy;

            if (!Groups.TryGetValue(groupId, out List<Brazier> list))
            {
                list = new List<Brazier>();
                Groups[groupId] = list;
            }

            list.Add(this);

            Refresh();
        }

        private void OnDestroy()
        {
            if (Groups.TryGetValue(groupId, out List<Brazier> list))
            {
                list.Remove(this);
            }
        }

        private void Start()
        {
            var dmg = GetComponent<Damageable>();

            if (dmg != null)
            {
                // 受击即点燃。火盆不会被打死，所以不订阅死亡事件
                dmg.OnDamaged = (self, info) => Light();
            }
        }

        /// <summary>点燃。重复调用无副作用（多段攻击同一帧命中两次也只亮一次）。</summary>
        public void Light()
        {
            if (IsLit)
            {
                return;
            }

            IsLit = true;
            Refresh();

            GameAudio.PlaySfx(SfxCue.Spirit);

            if (!string.IsNullOrEmpty(hint))
            {
                EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = hint });
            }

            if (glow != null)
            {
                StartCoroutine(FlashGlow());
            }

            GameLog.I($"[Brazier] 点燃（组 {groupId}，全亮 = {GroupAllLit(groupId)}）");
        }

        private System.Collections.IEnumerator FlashGlow()
        {
            Vector3 target = glow.localScale;
            float t = 0f;

            while (t < glowDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / glowDuration);

                // 先涨后落，像"轰"地一下点着
                float e = Mathf.Sin(k * Mathf.PI);
                glow.localScale = target * (1f + 0.6f * e);
                yield return null;
            }

            glow.localScale = target;
        }

        private void Refresh()
        {
            if (visualOff != null) visualOff.SetActive(!IsLit);
            if (visualOn != null) visualOn.SetActive(IsLit);
        }

        protected override void OnActivate(GameObject who)
        {
            Light();
        }
    }
}
