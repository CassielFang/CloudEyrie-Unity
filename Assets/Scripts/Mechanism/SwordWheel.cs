using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 旋转剑轮。设计稿金境段1「剑轮回廊」：
    /// 2-4 柄古剑绕轴**匀速**旋转，剑刃触碰扣 1 格；**空档可安全通过**。
    ///
    /// <b>为什么是匀速、且轴心要有光</b>
    ///
    /// 这一段的玩法是**轨迹预判** —— 玩家要在心里算出"剑什么时候转到别处"。
    /// 匀速是能预判的前提（变速就变成赌）；轴心的符文光是告诉玩家
    /// 「转轴在这里、转速恒定」的视觉提示。两者缺一，这一段就从"看时机"退化成"碰运气"。
    ///
    /// <b>判定为什么用扇形而不是碰撞体</b>
    ///
    /// 每柄剑都挂一个跟着转的碰撞体当然也行，但剑是**细长**的，
    /// 快速旋转时 Box2D 的接触会漏（一帧转过好几个身位）。
    /// 改成每帧按角度算"玩家在不在剑的扫掠范围内"，与转速无关，不会漏。
    /// </summary>
    public class SwordWheel : LevelMechanism
    {
        [Tooltip("几柄剑绕轴分布")]
        [SerializeField] private int swordCount = 3;

        [Tooltip("转一圈的秒数（设计稿 2.5）")]
        [SerializeField] private float period = 2.5f;

        [Tooltip("剑刃长度（世界单位）——判定用的扫掠半径")]
        [SerializeField] private float bladeLength = 130f;

        [Tooltip("命中判定的角宽（度）。剑有厚度，给太窄会变成擦身而过不算数")]
        [SerializeField] private float bladeWidthDeg = 14f;

        [Tooltip("玩家离轴心多近才算被扫到（水平+垂直距离）")]
        [SerializeField] private float hitRange = 150f;

        [SerializeField] private float spiritDamage = 15f;

        [Tooltip("同一次受伤后多久不再受伤 /秒")]
        [SerializeField] private float hitCooldown = 1.2f;

        [Header("表现")]
        [Tooltip("剑轮的立绘节点，会被旋转")]
        [SerializeField] private Transform art;

        [Tooltip("转向：1 = 顺时针，-1 = 逆时针")]
        [SerializeField] private float direction = 1f;

        private float _angle;
        private float _nextHittable;

        private void Reset()
        {
            gameObject.layer = PhysicsGroups.World;
        }

        private void Awake()
        {
            gameObject.layer = PhysicsGroups.World;

            if (art == null)
            {
                art = transform;
            }
        }

        private void Update()
        {
            if (period <= 0.0001f)
            {
                return;
            }

            _angle += direction * (360f / period) * Time.deltaTime;

            if (_angle > 360f) _angle -= 360f;
            if (_angle < -360f) _angle += 360f;

            if (art != null)
            {
                art.localRotation = Quaternion.Euler(0f, 0f, _angle);
            }

            CheckHit();
        }

        private void CheckHit()
        {
            if (Time.time < _nextHittable)
            {
                return;
            }

            var player = FindAnyObjectByType<QingheController>();

            if (player == null)
            {
                return;
            }

            Vector2 delta = (Vector2)player.transform.position + new Vector2(0f, 40f) - (Vector2)transform.position;

            if (delta.magnitude > hitRange)
            {
                return;
            }

            // 玩家相对轴心的角度
            float playerAngle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

            for (int i = 0; i < swordCount; i++)
            {
                float swordAngle = _angle + i * (360f / swordCount);
                float diff = Mathf.DeltaAngle(swordAngle, playerAngle);

                if (Mathf.Abs(diff) <= bladeWidthDeg * 0.5f)
                {
                    Hit(player);
                    return;
                }
            }
        }

        private void Hit(QingheController player)
        {
            _nextHittable = Time.time + hitCooldown;

            SpiritEnergySystem.Damage(spiritDamage);
            GameAudio.PlaySfx(SfxCue.Slash);

            Rigidbody2D body = player.GetComponent<Rigidbody2D>();

            // 被扫中往远离轴心的方向弹开一点，让"被打到"有反馈（不弹开的话
            // 玩家会站在剑轮里连挨几下，看起来像掉血没有原因）
            if (body != null)
            {
                Vector2 away = ((Vector2)player.transform.position - (Vector2)transform.position).normalized;
                body.linearVelocity = away * 260f + new Vector2(0f, 160f);
            }

            Activate(player.gameObject);
        }

        protected override void OnActivate(GameObject who)
        {
            // 剑轮被动生效
        }
    }
}
