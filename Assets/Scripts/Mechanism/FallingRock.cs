using System.Collections;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 预警落石。设计稿土境段2「落石走廊」：
    /// 进入触发区 → 地面出现**红色裂纹预警圈**（0.8s）→ 巨石坠落（伤害1格）→
    /// 碎石留 3s 可以踩，然后消散。
    ///
    /// <b>为什么预警必须够长、且看得见</b>
    ///
    /// 这是本作唯一"从天而降"的危险，玩家没有躲避的习惯动作。
    /// 预警不是装饰，是**给玩家的唯一信息**：没有它这就是随机惩罚，
    /// 而随机惩罚在平台跳跃里等于劝退。
    ///
    /// 落地后留碎石而不是立刻消失，是为了让玩家**能踩上去** ——
    /// 段2 的节奏是"用预警间隙冲刺，或停留诱石后踩着碎石过"，
    /// 碎石本身是通路的一部分。
    /// </summary>
    public class FallingRock : LevelMechanism
    {
        [Header("触发")]
        [Tooltip("进入这个区域就会触发。留空则用本节点的碰撞体")]
        [SerializeField] private Collider2D triggerZone;

        [Tooltip("触发一次之后是否还能再触发")]
        [SerializeField] private bool repeatable = true;

        [Tooltip("可重复时的冷却 /秒")]
        [SerializeField] private float cooldown = 2.5f;

        [Header("落石")]
        [Tooltip("石头立绘节点。会从高处落下来")]
        [SerializeField] private Transform rock;

        [Tooltip("石头从触发点上方多高处开始落")]
        [SerializeField] private float dropHeight = 700f;

        [Tooltip("下落速度（世界单位/秒）")]
        [SerializeField] private float fallSpeed = 1400f;

        [Tooltip("预警圈节点（红色裂纹）。留空则跳过预警表现")]
        [SerializeField] private Transform warningMark;

        [Tooltip("预警时长 /秒（设计稿 0.8）")]
        [SerializeField] private float warnDuration = 0.8f;

        [Header("命中")]
        [SerializeField] private float spiritDamage = 15f;

        [Tooltip("落点判定半径")]
        [SerializeField] private float hitRadius = 70f;

        [Header("碎石")]
        [Tooltip("落地后碎石停留多久 /秒（设计稿 3）")]
        [SerializeField] private float rubbleDuration = 3f;

        [Tooltip("碎石的可踩碰撞体。留空则落地后石头直接消失")]
        [SerializeField] private Collider2D rubbleCollider;

        private bool _busy;
        private float _readyAt;

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
            if (triggerZone == null)
            {
                triggerZone = GetComponent<Collider2D>();
            }

            if (triggerZone != null)
            {
                triggerZone.isTrigger = true;
            }

            if (warningMark != null)
            {
                warningMark.gameObject.SetActive(false);
            }

            if (rubbleCollider != null)
            {
                rubbleCollider.enabled = false;
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (PlayerFrom(other) == null || _busy || Time.time < _readyAt)
            {
                return;
            }

            Activate(other.gameObject);
        }

        protected override void OnActivate(GameObject who)
        {
            StartCoroutine(DropRoutine());
        }

        private IEnumerator DropRoutine()
        {
            _busy = true;

            // ---- 1. 预警 ----
            if (warningMark != null)
            {
                warningMark.gameObject.SetActive(true);
            }

            float t = 0f;

            while (t < warnDuration)
            {
                t += Time.deltaTime;

                if (warningMark != null)
                {
                    // 闪一下停一下，比常亮更抓眼
                    float blink = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * 18f));
                    Vector3 s = warningMark.localScale;
                    warningMark.localScale = new Vector3(s.x, s.y, s.z);

                    var sr = warningMark.GetComponent<SpriteRenderer>();
                    if (sr != null) { Color c = sr.color; c.a = blink; sr.color = c; }
                }

                yield return null;
            }

            if (warningMark != null)
            {
                warningMark.gameObject.SetActive(false);
            }

            // ---- 2. 坠落 ----
            Vector3 landPos = transform.position;
            Vector3 startPos = landPos + new Vector3(0f, dropHeight, 0f);

            if (rock != null)
            {
                rock.gameObject.SetActive(true);
                rock.position = startPos;
            }

            while (rock != null && rock.position.y > landPos.y)
            {
                rock.position = Vector3.MoveTowards(rock.position, landPos, fallSpeed * Time.deltaTime);
                yield return null;
            }

            if (rock != null)
            {
                rock.position = landPos;
            }

            // ---- 3. 命中判定 ----
            GameAudio.PlaySfx(SfxCue.Corruption);

            var player = FindAnyObjectByType<QingheController>();

            if (player != null && Vector2.Distance(player.transform.position, landPos) <= hitRadius)
            {
                SpiritEnergySystem.Damage(spiritDamage);
            }

            // ---- 4. 碎石可踩 ----
            if (rubbleCollider != null)
            {
                rubbleCollider.enabled = true;
            }

            yield return new WaitForSeconds(rubbleDuration);

            if (rubbleCollider != null)
            {
                rubbleCollider.enabled = false;
            }

            if (rock != null)
            {
                rock.gameObject.SetActive(false);
            }

            _readyAt = Time.time + (repeatable ? cooldown : float.MaxValue);
            _busy = false;
        }
    }
}
