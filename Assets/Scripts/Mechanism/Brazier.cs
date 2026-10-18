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

        [Tooltip("同组里还有没点燃的火盆时的提示。留空则不提示")]
        [SerializeField] private string remainingHint = "还有一座火盆未点燃";

        [Tooltip("玩家靠近未点燃的火盆时，立绘脉冲放大的半径。0 = 不做靠近高亮")]
        [SerializeField] private float highlightRadius = 260f;

        [Tooltip("未点燃时的染色。美术只交付了「已点燃」一张图，必须靠压暗来区分状态，\n" +
                 "否则玩家会以为两座都已经点着了（门却不开）。")]
        [SerializeField] private Color unlitTint = new Color(0.30f, 0.30f, 0.38f, 1f);

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

        /// <summary>
        /// 让某一组**还没点亮**的火盆脉冲高亮一段时间。
        /// 门的提示用到它：文字告诉玩家"攻击火盆"，高亮告诉他们"是这两座"。
        /// </summary>
        public static void HighlightGroup(string id, float seconds)
        {
            if (string.IsNullOrEmpty(id) || !Groups.TryGetValue(id, out List<Brazier> list))
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                {
                    list[i].Highlight(seconds);
                }
            }
        }

        private QingheController _player;
        private Vector3 _visualBaseScale;
        private bool _visualScaleCached;
        private float _highlightUntil;

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

            _player = FindAnyObjectByType<QingheController>();

            if (visualOff != null)
            {
                _visualBaseScale = visualOff.transform.localScale;
                _visualScaleCached = true;
            }
        }

        /// <summary>开始脉冲高亮（门的提示会调）。</summary>
        public void Highlight(float seconds)
        {
            if (IsLit)
            {
                return;
            }

            _highlightUntil = Mathf.Max(_highlightUntil, Time.time + Mathf.Max(0f, seconds));
        }

        /// <summary>
        /// 未点亮时做一点**可交互**的视觉暗示：
        /// 玩家靠近时持续轻微脉冲；被门的提示点名时脉冲更久。
        /// 这是"示能"（affordance）—— 不弹文字，也能看出它可以被打。
        /// </summary>
        private void Update()
        {
            if (visualOff == null || !_visualScaleCached)
            {
                return;
            }

            bool near = highlightRadius > 0.01f
                        && _player != null
                        && Vector2.Distance(_player.transform.position, transform.position) <= highlightRadius;

            bool pulse = !IsLit && (near || Time.time < _highlightUntil);
            float wave = Mathf.Sin(Time.time * 8f);
            float k = pulse ? 1f + 0.06f * wave : 1f;
            visualOff.transform.localScale = _visualBaseScale * k;

            // 靠近时从不点燃的暗色往暖白提一点 —— 比单纯放大更容易被注意到
            if (!IsLit)
            {
                Color tint = pulse
                    ? Color.Lerp(unlitTint, Color.white, 0.30f + 0.12f * wave)
                    : unlitTint;
                ApplyTint(visualOff, tint);
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

            // 点亮一座、同组还有别的没亮 —— 直接告诉玩家"还没完"，
            // 否则很容易以为点燃一座就够了，然后站在门前反复撞。
            // 同一条提示位只发一条：两条一起发的话 HUD 只会显示最后一条。
            bool allLit = GroupAllLit(groupId);
            string message = !allLit ? remainingHint : hint;

            if (!string.IsNullOrEmpty(message))
            {
                EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = message, Seconds = 4f });
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
            if (visualOff != null)
            {
                visualOff.SetActive(!IsLit);
                ApplyTint(visualOff, IsLit ? Color.white : unlitTint);
            }

            if (visualOn != null)
            {
                visualOn.SetActive(IsLit);
                ApplyTint(visualOn, Color.white);
            }
        }

        private static void ApplyTint(GameObject visual, Color color)
        {
            if (visual == null)
            {
                return;
            }

            foreach (SpriteRenderer sr in visual.GetComponentsInChildren<SpriteRenderer>(true))
            {
                sr.color = color;
            }
        }

        protected override void OnActivate(GameObject who)
        {
            Light();
        }
    }
}
