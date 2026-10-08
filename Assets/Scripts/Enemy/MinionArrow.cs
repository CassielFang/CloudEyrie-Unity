using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 小怪射出的箭 —— 阳浊小怪的**远程攻击**。
    /// 对应 Cocos 侧 <c>enemy/MinionArrow.ts</c>。
    ///
    /// 补的是一个「表现与逻辑对不上」的缺口：小怪的攻击动画明明是**拉弓射箭**，
    /// 而 <see cref="EnemyAI"/> 的攻击曾是近战 —— 在距离内直接扣玩家灵炁、不生成任何东西。
    ///
    /// <b>刚体配法（改之前务必读）</b>
    ///
    /// Cocos 那边这一处踩过两个坑，注释里写得很清楚：
    /// <list type="number">
    ///   <item><c>Animated</c> 刚体 + 靠 <c>linearVelocity</c> 推 → 速度被忽略，箭钉在空中；</item>
    ///   <item>改成 <c>Dynamic</c> + **非 sensor** → 箭会飞了，但和玩家的 Dynamic 刚体做真物理碰撞，
    ///     **把玩家撞飞**（实测反馈「不是射在我身上，是把我撞飞了」）。</item>
    /// </list>
    ///
    /// 正解是两者结合：**跟随变换的刚体 + 触发器**，位置由本组件每帧自己积分 ——
    /// 既不需要质量/速度语义，又不产生任何物理推力。
    ///
    /// Unity 里对应 <see cref="RigidbodyType2D.Kinematic"/>（跟随 Transform，不受物理力）
    /// + <c>isTrigger = true</c>。位置同样**由代码积分**，不写 <c>linearVelocity</c>。
    ///
    /// 命中后走 <see cref="CombatController.ReceivePlayerDamage"/>，和近战同一条链路，
    /// 于是格挡 / 完美格挡 / 受击扣灵炁全部自动生效，不用另写一套。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    public class MinionArrow : MonoBehaviour
    {
        /// <summary>箭的素材（从攻击动画里抽出来的那一帧）</summary>
        private const string ArrowAsset = "小怪-箭";

        /// <summary>素材画的是**朝左**的箭，朝右飞要镜像节点</summary>
        private const float ArrowNaturalDir = -1f;

        /// <summary>箭身的半宽 / 半高，命中判定用（配置驱动）</summary>
        private static float HitHalfW { get { return GameConfig.Current.Combat.ArrowHitHalfW; } }
        private static float HitHalfH { get { return GameConfig.Current.Combat.ArrowHitHalfH; } }

        /// <summary>玩家碰撞体的兜底尺寸（拿不到 BoxCollider2D 时用）。与 Cocos 侧的 40×150 / offset(0,55) 对应</summary>
        private const float FallbackPlayerHalfW = 20f;
        private const float FallbackPlayerHalfH = 75f;
        private const float FallbackPlayerOffY = 55f;

        private float _damage = 15f;
        private float _speed = 900f;
        private float _dir = -1f;
        private float _life = 2f;
        private bool _done;

        private Rigidbody2D _rigidBody;
        private BoxCollider2D _collider;

        /// <summary>目标节点与它的 <see cref="CombatController"/></summary>
        private Transform _target;
        private CombatController _targetCombat;

        /// <summary>本帧已判定命中，等 Update 里结算（与 Cocos 版一致：不在物理回调里动刚体）</summary>
        private bool _pendingHit;

        // ============================================================ 生成

        /// <summary>生成一支箭。<paramref name="parent"/> 用关卡根节点；<paramref name="worldPos"/> 是箭的出生世界坐标。</summary>
        public static MinionArrow Spawn(Transform parent, Vector2 worldPos, float dir,
                                        float damage, float speed)
        {
            GameObject go = new GameObject("MinionArrow");
            go.layer = PhysicsGroups.Arrow;
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;   // 跟随 Transform，不受物理力
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            BoxCollider2D col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(150f, 20f);
            col.isTrigger = true;                      // 只报触发，不阻挡、不推动

            // ⚠️ 必须显式设绘制层级。新建的 SpriteRenderer 默认 sortingOrder = 0，
            // 而小怪在 10、青禾在 30 —— 箭会**画在它们后面**，放箭时看起来像从身体
            // 里穿出来。抛射物归到特效层（与灵弹/刀光同一档）。
            SpriteRenderer arrowSprite = go.AddComponent<SpriteRenderer>();
            arrowSprite.sortingOrder = SortingOrders.Fx;

            MinionArrow arrow = go.AddComponent<MinionArrow>();
            arrow._rigidBody = rb;
            arrow._collider = col;

            // 目标在这里就解析好 —— 别拖到 Update 里每帧查场景
            QingheController qinghe = FindAnyObjectByType<QingheController>();

            if (qinghe != null)
            {
                arrow._target = qinghe.transform;
                arrow._targetCombat = qinghe.GetComponent<CombatController>();
            }

            arrow._damage = damage;
            arrow._speed = speed;
            arrow._dir = dir >= 0f ? 1f : -1f;

            // 素材朝左；要朝右飞就镜像
            float sx = Mathf.Approximately(arrow._dir, ArrowNaturalDir) ? 1f : -1f;
            go.transform.localScale = new Vector3(sx, 1f, 1f);

            arrow.PlayArt();
            return arrow;
        }

        private void PlayArt()
        {
            SequencePlayer player = gameObject.GetComponent<SequencePlayer>();

            if (player == null)
            {
                player = gameObject.AddComponent<SequencePlayer>();
            }

            player.basePath = "video_fx";
            player.aheadPages = 0;
            player.behindPages = 0;

            // 单帧素材：Play 自己会去读 manifest，这里不用先 Load 一遍
            player.Play(ArrowAsset, false);
        }

        // ============================================================ 命中

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_done || _pendingHit)
            {
                return;
            }

            // 诊断：能碰到谁。碰撞分组限定只和 PLAYER 碰，所以这行只在打到玩家时出现。
            GameLog.V($"[MinionArrow] 接触 {other.name}");

            CombatController combat = other.GetComponentInParent<CombatController>();

            if (combat == null)
            {
                return;                    // 只打玩家；撞到别的一律无视
            }

            // 记下标记，交给下一帧的 Update 处理（与 Cocos 版一致）
            _pendingHit = true;
        }

        /// <summary>
        /// **按矩形重叠判定命中**，不依赖物理接触。
        ///
        /// 这是主路径：接触链路要同时穿过「碰撞分组 → 夹具过滤位 → 夹具重建 → 刚体类型」
        /// 四道关，**任何一道出错都是静默失败**，而且每一处读回来的值看着都是对的，
        /// 排查成本极高（Cocos 侧就在这上面反复栽过）。
        ///
        /// 抛射物本来就是个点/小矩形，直接算重叠既简单又能一眼验证。
        /// 碰撞体与分组保留着 —— 将来要做「箭被地形挡下」时还用得上。
        /// </summary>
        private void CheckHitPlayer()
        {
            if (_done || _targetCombat == null || _target == null)
            {
                return;
            }

            Vector3 a = transform.position;
            Vector3 t = _target.position;

            // 优先读玩家自己的碰撞体；拿不到再退回 Cocos 侧的硬编码尺寸
            BoxCollider2D playerCol = _targetCombat.GetComponent<BoxCollider2D>();
            bool usable = playerCol != null && playerCol.size.x > 1f && playerCol.size.y > 1f;

            float halfW = usable ? playerCol.size.x * 0.5f : FallbackPlayerHalfW;
            float halfH = usable ? playerCol.size.y * 0.5f : FallbackPlayerHalfH;
            float offY = usable ? playerCol.offset.y : FallbackPlayerOffY;

            bool overlapX = Mathf.Abs(a.x - t.x) <= halfW + HitHalfW;
            bool overlapY = a.y >= t.y + offY - halfH - HitHalfH
                            && a.y <= t.y + offY + halfH + HitHalfH;

            if (overlapX && overlapY)
            {
                _pendingHit = true;
            }
        }

        /// <summary>真正结算命中。由 <see cref="Update"/> 在物理回调之外调用。</summary>
        private void ResolveHit()
        {
            _done = true;

            CombatController combat = _targetCombat;

            if (combat == null)
            {
                Destroy(gameObject);
                return;
            }

            PlayerDamageResult result = combat.ReceivePlayerDamage(_damage);
            GameLog.V($"[MinionArrow] 命中玩家，扣灵炁 {result.SpiritCost}（完美格挡={result.PerfectBlocked}）");

            if (result.SpiritCost > 0f)
            {
                SpiritEnergySystem.Damage(result.SpiritCost);
            }

            Destroy(gameObject);
        }

        // ============================================================ 主循环

        private void Update()
        {
            float dt = Time.deltaTime;

            // 位置自己积分，不走刚体速度 —— 跟随型刚体本来就不吃 linearVelocity
            Vector3 p = transform.position;
            transform.position = new Vector3(p.x + _dir * _speed * dt, p.y, p.z);

            CheckHitPlayer();

            if (_pendingHit)
            {
                ResolveHit();
                return;
            }

            _life -= dt;

            if (_life <= 0f)
            {
                Destroy(gameObject);
            }
        }
    }
}
