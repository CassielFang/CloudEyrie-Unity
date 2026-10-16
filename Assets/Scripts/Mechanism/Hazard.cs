using System.Collections;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 危险地形。一个组件覆盖五境里绝大多数"碰到就疼"的东西：
    /// 岩浆（火）、剑轮（金）、间歇火柱（火）、尖刺…
    ///
    /// <b>惩罚为什么是「扣灵炁 + 弹回本段起点」而不是即死</b>
    ///
    /// 设计稿明确：「落坑/碰岩浆回到本段起点，不回存档点，降低挫败感」。
    /// 这是平台跳跃游戏的常见取舍 —— 惩罚是**位置**而不是**命**，
    /// 玩家会继续尝试而不是放弃。
    ///
    /// <b>周期性危险（火柱）与常驻危险（岩浆）为什么合在一个组件里</b>
    ///
    /// 它们的差别只有「什么时候是危险的」这一个维度：常驻 = 周期为 0。
    /// 拆成两个类的话，预警、伤害、冷却、形态免疫这些逻辑要写两遍，
    /// 改一处漏一处 —— 而伤害这种事漏一处就是"某个 Hazard 打人特别疼"。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Hazard : LevelMechanism
    {
        [Header("伤害")]
        [Tooltip("扣多少灵炁（1 格 = 15，与敌人普攻同档）")]
        [SerializeField] private float spiritDamage = 15f;

        [Tooltip("受伤后的短暂硬直 /秒（0 = 不硬直）")]
        [SerializeField] private float stunDuration = 0.25f;

        [Tooltip("同一次受伤后多久不会再次受伤 /秒")]
        [SerializeField] private float hitCooldown = 0.8f;

        [Header("惩罚落点")]
        [Tooltip("碰到后把玩家送回哪个节点。留空 = 原地不动（只扣灵炁）")]
        [SerializeField] private Transform respawnPoint;

        [Header("形态免疫")]
        [Tooltip("勾上则灵雾/灵合态不受伤（浊湮类地形要它：飞起来就没事）")]
        [SerializeField] private bool floatFormsImmune;

        [Header("周期性（火柱）。period = 0 表示常驻危险")]
        [Tooltip("喷发/危险的周期 /秒。0 = 一直危险")]
        [SerializeField] private float period;

        [Tooltip("一个周期里危险占多久 /秒")]
        [SerializeField] private float activeDuration = 2f;

        [Tooltip("危险开始前多久给预警 /秒")]
        [SerializeField] private float warnLead = 0.5f;

        [Tooltip("相位偏移（0~1），多根火柱靠它错开")]
        [Range(0f, 1f)]
        [SerializeField] private float phase;

        [Header("表现")]
        [Tooltip("危险体本身的渲染节点。会随危险期开关（火柱喷出来/缩回去）")]
        [SerializeField] private Transform visual;

        [Tooltip("预警时是否把危险体压暗一点，当作即将喷发的提示")]
        [SerializeField] private bool dimOnWarn = true;

        private Collider2D _hazardCollider;
        private float _nextHittable;
        private SpriteRenderer[] _visualRenderers;
        private Color[] _visualBaseColors;
        private bool _active = true;

        private void Reset()
        {
            Collider2D col = GetComponent<Collider2D>();

            if (col != null)
            {
                col.isTrigger = true;
            }

            gameObject.layer = PhysicsGroups.World;
        }

        private void Awake()
        {
            _hazardCollider = GetComponent<Collider2D>();

            if (_hazardCollider != null)
            {
                _hazardCollider.isTrigger = true;
            }

            if (visual == null)
            {
                visual = transform;
            }

            _visualRenderers = visual.GetComponentsInChildren<SpriteRenderer>(true);
            _visualBaseColors = new Color[_visualRenderers.Length];

            for (int i = 0; i < _visualRenderers.Length; i++)
            {
                _visualBaseColors[i] = _visualRenderers[i].color;
            }

            // 常驻危险：直接进入危险态，不必等一个周期
            _active = period <= 0.0001f;
            SetActive(_active);
        }

        private void Update()
        {
            if (period <= 0.0001f)
            {
                return;
            }

            float t = Mathf.Repeat(Time.time / period + phase, 1f) * period;
            bool warn = warnLead > 0f && t >= period - activeDuration - warnLead && t < period - activeDuration;
            bool active = t >= period - activeDuration;

            if (active != _active)
            {
                _active = active;
                SetActive(active);
            }

            if (dimOnWarn && _visualRenderers != null && _visualRenderers.Length > 0)
            {
                // 预警时压暗：不用额外做一套预警贴图，也能让人看出来"要来了"
                float k = warn ? 0.55f : (_active ? 1f : 0.25f);

                for (int i = 0; i < _visualRenderers.Length; i++)
                {
                    Color c = _visualBaseColors[i];
                    c.a *= k;
                    _visualRenderers[i].color = c;
                }
            }
        }

        private void SetActive(bool on)
        {
            if (_hazardCollider != null)
            {
                _hazardCollider.enabled = on;
            }
        }

        private void OnTriggerEnter2D(Collider2D other) { TryHit(other); }
        private void OnTriggerStay2D(Collider2D other) { TryHit(other); }

        private void TryHit(Collider2D other)
        {
            if (!_active || Time.time < _nextHittable)
            {
                return;
            }

            QingheController player = PlayerFrom(other);

            if (player == null)
            {
                return;
            }

            Rigidbody2D body = other.attachedRigidbody;

            // 无重力形态 = 灵雾 / 灵合。浊湮类地形对它们无效（飞起来就没事）
            if (floatFormsImmune && body != null && body.gravityScale <= 0f)
            {
                return;
            }

            _nextHittable = Time.time + hitCooldown;

            SpiritEnergySystem.Damage(spiritDamage);
            GameAudio.PlaySfx(SfxCue.Corruption);

            if (respawnPoint != null)
            {
                StartCoroutine(BounceBack(player, body));
            }
            else if (stunDuration > 0f && body != null)
            {
                StartCoroutine(Stun(body));
            }

            Activate(player.gameObject);
        }

        /// <summary>
        /// 送回本段起点。
        ///
        /// 用 <c>MovePosition</c> 而不是直接写 <c>position</c>：
        /// 直接写会让物理引擎在下一帧把角色"拽"回去（它还在按旧位置解算）。
        /// 这里先清速度、再等一帧物理，然后传送 —— 顺序反了会看到一次抖动。
        /// </summary>
        private IEnumerator BounceBack(QingheController player, Rigidbody2D body)
        {
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
            }

            yield return new WaitForFixedUpdate();

            if (player != null && respawnPoint != null)
            {
                player.transform.position = respawnPoint.position;
            }

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
            }
        }

        /// <summary>
        /// 短暂硬直：把水平速度压掉一段时间，但**不动竖直速度** ——
        /// 否则在平台边缘被烧一下会连下坠也停住，看起来像卡在空中。
        /// </summary>
        private IEnumerator Stun(Rigidbody2D body)
        {
            float t = 0f;

            while (t < stunDuration && body != null)
            {
                t += Time.deltaTime;
                body.linearVelocity = new Vector2(body.linearVelocity.x * 0.2f, body.linearVelocity.y);
                yield return null;
            }
        }

        protected override void OnActivate(GameObject who)
        {
            // 刻意**不**弹文字提示：地形伤害是高频的，每碰一次弹一行字会刷屏，
            // 而玩家已经能从灵炁条掉一截 + 音效 + 被弹回去这三件事读出来。
            // 设计稿要的是「柔和的视觉反馈」，不是一条条消息。
        }
    }
}
