using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 藤蔓种子：踩上去（或攻击一下）之后，藤蔓**逐级生长**成一条通路。
    /// 设计稿木境段1「藤蔓生长」的核心机关，也是全作第一个「场景物件可被触发」的教学。
    ///
    /// <b>平台为什么是「预先摆好的子节点」而不是运行时生成的瓦片</b>
    ///
    /// 两种做法都行，这里选子节点，理由是**可视化编辑**：
    /// 策划在 Scene 视图里直接把每一级平台摆到位（先设成未激活），
    /// 生长顺序就是子节点的顺序 —— 所见即所得，改位置不用改代码、也不用算格子坐标。
    /// 走 Tilemap 写格的话，每级平台得写成「第几格」，改一次要重新数。
    ///
    /// 代价：平台数量固定（摆几个就是几个）。对藤蔓阶梯这种**几十级封顶**的用途足够。
    ///
    /// <b>生长动画</b>：逐级从 0 缩放到原大小，每级之间稍有重叠，
    /// 看起来是"藤蔓一截一截窜上去"而不是"方块一个个弹出来"。
    /// </summary>
    public class VineSeed : LevelMechanism
    {
        [Header("生长目标")]
        [Tooltip("要逐级激活的子节点，**按生长顺序排列**。通常是几个未激活的平台节点")]
        [SerializeField] private Transform[] steps;

        [Header("节奏")]
        [Tooltip("整条藤蔓长完的总时长 /秒（设计稿 1.2）")]
        [SerializeField] private float growDuration = 1.2f;

        [Tooltip("每一级自己的缩放时长 /秒。剩余时间不够时会自动压缩")]
        [SerializeField] private float stepGrowTime = 0.25f;

        [Tooltip("相邻两级之间重叠多少（0 = 上一级长完才长下一级）")]
        [Range(0f, 0.9f)]
        [SerializeField] private float overlap = 0.45f;

        [Header("触发")]
        [Tooltip("是否允许攻击触发（设计稿：踩踏 / 攻击都可以）")]
        [SerializeField] private bool triggerOnAttack = true;

        [Header("表现")]
        [Tooltip("循环播放的生长音效名。留空则不播")]
        [SerializeField] private string growSfx = "spirit";

        private bool _growing;

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
            if (steps == null)
            {
                return;
            }

            // 未触发前所有平台都不可见、也不参与碰撞 ——
            // 忘了在编辑器里关掉的话，玩家会踩在**还没长出来的**藤蔓上（很怪，且不报错）
            foreach (Transform step in steps)
            {
                if (step != null)
                {
                    step.gameObject.SetActive(false);
                }
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (PlayerFrom(other) == null)
            {
                return;
            }

            Activate(other.gameObject);
        }

        /// <summary>
        /// 攻击触发用这个 —— 由攻击的命中盒调 <see cref="LevelMechanism.Activate"/>。
        /// 与踩踏共用同一套去重（<see cref="_growing"/>），所以踩完再打不会重来一遍。
        /// </summary>
        protected override void OnActivate(GameObject who)
        {
            if (_growing)
            {
                return;
            }

            if (steps == null || steps.Length == 0)
            {
                GameLog.W("[VineSeed] 没有配置生长目标（steps 为空），触发后什么都不会发生");
                return;
            }

            StartCoroutine(GrowRoutine());
        }

        private IEnumerator GrowRoutine()
        {
            _growing = true;

            if (!string.IsNullOrEmpty(growSfx))
            {
                GameAudio.PlaySfx(SfxCue.Spirit);
            }

            int count = steps.Length;

            // 每级之间的间隔：总时长里塞下这么多级，再按 overlap 提前启动下一级
            float interval = count > 1
                ? Mathf.Max(0.02f, growDuration / count) * (1f - overlap)
                : 0f;

            for (int i = 0; i < count; i++)
            {
                Transform step = steps[i];

                if (step != null)
                {
                    step.gameObject.SetActive(true);
                    StartCoroutine(PopIn(step));
                }

                if (i < count - 1)
                {
                    yield return new WaitForSeconds(interval);
                }
            }

            // 平台**永久存在**（设计稿明确），所以这里不回收
            GameLog.I($"[VineSeed] 藤蔓长成，共 {count} 级");
        }

        /// <summary>单级从 0 缩放到原大小。用 unscaled 的 realtime 计时，演出期间也能长。</summary>
        private IEnumerator PopIn(Transform step)
        {
            Vector3 target = step.localScale;
            float t = 0f;

            while (t < stepGrowTime)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(0.01f, stepGrowTime));

                // 轻微过冲，像有生命的东西顶出来
                float e = 1f + 0.18f * Mathf.Sin(k * Mathf.PI);
                step.localScale = target * (k * e);

                yield return null;
            }

            step.localScale = target;
        }
    }
}
