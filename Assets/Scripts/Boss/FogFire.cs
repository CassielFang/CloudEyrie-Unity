using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 狐火 —— 九尾狐从尾部浊焰里弹出的小型灵体，Boss 的**主要攻击手段**。
    ///
    /// <b>为什么 Boss 需要它</b>
    ///
    /// 美术只给了九尾狐三段**形态动画**（幻影 / 狂乱 / 归心），**没有任何挥击、甩尾的动作**，
    /// 所以九尾狐的「攻击」只能靠召唤物与弹幕表达 —— 本体做什么动画都看不出在出招。
    /// 狐火正好补这个位置：飘过去咬人，玩家用实体形态清掉。
    ///
    /// <b>素材</b>：<c>狐火-上升 / 转身 / 静息</c> 三段都**是待机变体**（不是生命周期）——
    /// 上升是火苗上飘、静息是平静悬浮、转身是左顾右盼。所以这里只用两段：
    /// 出场播一次 <c>上升</c>，之后循环 <c>静息</c>。
    /// （三段画布 192、页数 2~3，都在 <see cref="SequencePlayer.keepAllPageThreshold"/> 以内，
    ///   切换由驻留槽兜着，来回切不产生任何加载。）
    ///
    /// <b>三处必须照抄既有约定，别自己发挥</b>
    /// <list type="bullet">
    ///   <item><b>刚体要 Dynamic + gravityScale = 0</b>（不是 Kinematic，也不是 Animated）。
    ///     靠写 <c>linearVelocity</c> 移动的就必须是 Dynamic —— <c>Animated</c> 刚体会忽略速度
    ///     （<c>MinionArrow</c> 当年「箭钉在空中」就是这个）。这里要悬浮所以不给重力。</item>
    ///   <item><b>碰撞体必须挂在 ENEMY 层</b>：玩家的命中盒只和 ENEMY 碰（见 <see cref="PhysicsGroups"/>），
    ///     挂错层就永远砍不到它。</item>
    ///   <item><b>接触伤害不能靠物理回调</b>：碰撞矩阵里 PLAYER 与 ENEMY 是**互不接触**的
    ///     （玩家能穿过小怪），所以根本没有碰撞事件。改成每帧自己算矩形重叠 ——
    ///     与 <see cref="MinionArrow"/> 同一条路径。</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    [RequireComponent(typeof(Damageable))]
    public class FogFire : MonoBehaviour
    {
        private const string ArtName = "Art";
        private const string RiseAnimation = "狐火-上升";
        private const string IdleAnimation = "狐火-静息";

        /// <summary>
        /// 素材画的是**朝左**：狐狸的头在左、拖着的焰尾向右。传错整个左右反掉。
        /// </summary>
        private const NaturalFacing ArtNaturalFacing = NaturalFacing.Left;

        /// <summary>
        /// 接触判定用的半边长（世界单位）。**从 GameConfig.Boss 读**，Inspector 里可调 ——
        /// 这几个数直接决定「多近算咬到人」，是最需要试出来的手感值，不该写死在代码里。
        /// </summary>
        private static float HitHalfW { get { return GameConfig.Current.Boss.FogFireHitHalfW; } }
        private static float HitHalfH { get { return GameConfig.Current.Boss.FogFireHitHalfH; } }

        /// <summary>玩家碰撞体的兜底尺寸（拿不到 BoxCollider2D 时用）</summary>
        private const float FallbackPlayerHalfW = 20f;
        private const float FallbackPlayerHalfH = 75f;
        private const float FallbackPlayerOffY = 55f;

        /// <summary>朝向变化超过这个水平距离才跟着翻 —— 免得在玩家正上方来回抖（配置驱动）</summary>
        private static float FacingDeadZone { get { return GameConfig.Current.Boss.FogFireFacingDeadZone; } }

        /// <summary>死亡表现时长 /秒（配置驱动）</summary>
        private static float DeathDuration { get { return GameConfig.Current.Boss.FogFireDeathDuration; } }

        /// <summary>死亡缩到原缩放的这么小（配置驱动）</summary>
        private static float DeathShrink { get { return GameConfig.Current.Boss.FogFireDeathShrink; } }

        /// <summary>被完美格挡反制时弹开的击退初速（配置驱动）</summary>
        private static float RepelSpeed { get { return GameConfig.Current.Boss.FogFireRepelSpeed; } }

        private float _speed;
        private float _damage;
        private float _contactCooldown;

        private Rigidbody2D _rigidBody;
        private Damageable _damageable;
        private Transform _artNode;
        private SequencePlayer _player;

        private Transform _target;
        private CombatController _targetCombat;

        private float _contactTimer;
        private float _repelRemaining;
        private float _deathElapsed;
        private bool _dying;
        private float _facing = -1f;
        private float _baseScale = 1f;

        /// <summary>个体种子。由召唤方按波次给出。</summary>
        [System.NonSerialized] public int personalitySeed;

        /// <summary>悬停距离的个体差异 —— 免得几只狐火贴成同一个坐标</summary>
        private float _standoff = StandoffDistance;

        /// <summary>悬停高度偏移：每只停在玩家不同的高度上，围成一个圈而不是叠成一堆</summary>
        private float _hoverOffsetY;

        /// <summary>上下浮动的相位（每只错开，免得整群同时上下）</summary>
        private float _bobPhase;

        /// <summary>上下浮动速度（弧度/秒）</summary>
        private float _bobSpeed = 1f;

        private float _cooldownScale = 1f;

        /// <summary>死亡回调（Boss 用来维护「场上还有几只」）。</summary>
        public System.Action<FogFire> OnDied;

        /// <summary>是否还活着（Boss 统计用）。</summary>
        public bool IsAlive
        {
            get { return !_dying; }
        }

        // ============================================================ 生成

        /// <summary>
        /// 弹出一只狐火。<paramref name="parent"/> 传关卡根节点（别挂在 Boss 身上，
        /// 否则 Boss 一旦移动狐火会跟着飘）。
        ///
        /// ⚠️ 性格种子**必须从这里传进来**，不能等返回后再由调用方赋 ——
        /// 下面就要用它算 <c>_standoff</c> / <c>_hoverOffsetY</c> 了，
        /// 晚一步赋的话所有狐火都会用同一个种子，变成一模一样（正是要避免的事）。
        /// </summary>
        public static FogFire Spawn(Transform parent, Vector2 worldPos, int personalitySeed)
        {
            BossConfig cfg = GameConfig.Current.Boss;

            GameObject n = new GameObject("狐火");
            n.layer = PhysicsGroups.Enemy;
            n.transform.SetParent(parent, false);
            n.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

            Rigidbody2D rb = n.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.gravityScale = 0f;                      // 悬浮
            rb.freezeRotation = true;
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;

            BoxCollider2D col = n.AddComponent<BoxCollider2D>();
            col.size = new Vector2(HitHalfW * 2f * 0.7f, HitHalfH * 2f * 0.7f);
            col.sharedMaterial = LowFriction();

            Damageable dmg = n.AddComponent<Damageable>();
            dmg.SetMaxHp(cfg.FogFireHp);

            // ⚠️ Art 子节点要在 FogFire 之前建好：组件的 Awake 里 `transform.Find(ArtName)`
            //    是一次性查找，顺序反了就拿不到（EnemyAI 踩过同一个坑）。
            GameObject art = new GameObject(ArtName);
            art.transform.SetParent(n.transform, false);

            SpriteRenderer sr = art.AddComponent<SpriteRenderer>();
            sr.sortingOrder = SortingOrders.Minion + 1;   // 与小怪同档，略高一格

            FogFire fire = n.AddComponent<FogFire>();
            fire._rigidBody = rb;
            fire._damageable = dmg;
            fire._speed = cfg.FogFireSpeed;
            fire._damage = cfg.FogFireDamage;
            fire._contactCooldown = cfg.FogFireContactCooldown;
            fire._baseScale = cfg.FogFireArtScale;

            // 性格：数值（伤害 / 血量 / 速度）一个字不改，只让每只停的位置和咬人节奏不同。
            // 见 Personality 的说明。
            fire.personalitySeed = personalitySeed;
            Personality persona = new Personality(personalitySeed);
            fire._standoff = StandoffDistance * persona.Jitter(0.35f);
            fire._hoverOffsetY = persona.Signed() * 55f;
            fire._cooldownScale = persona.Jitter(0.30f);
            fire._bobPhase = persona.Range(0f, Mathf.PI * 2f);
            fire._bobSpeed = persona.Range(0.7f, 1.5f);

            art.transform.localScale = new Vector3(cfg.FogFireArtScale, cfg.FogFireArtScale, 1f);

            // 目标在这里解析好，别拖到 Update 里每帧查场景
            QingheController qinghe = FindAnyObjectByType<QingheController>();

            if (qinghe != null)
            {
                fire._target = qinghe.transform;
                fire._targetCombat = qinghe.GetComponent<CombatController>();
            }

            return fire;
        }

        private static PhysicsMaterial2D _lowFriction;

        private static PhysicsMaterial2D LowFriction()
        {
            if (_lowFriction == null)
            {
                _lowFriction = new PhysicsMaterial2D("FogFireLowFriction")
                {
                    friction = 0f,
                    bounciness = 0f,
                };
            }

            return _lowFriction;
        }

        // ============================================================ 生命周期

        private void Awake()
        {
            _rigidBody = GetComponent<Rigidbody2D>();
            _damageable = GetComponent<Damageable>();
            _artNode = transform.Find(ArtName);

            if (_damageable != null)
            {
                _damageable.OnDeath = _ => BeginDeath();
                _damageable.OnDamaged = (self, info) => OnHit(info);
            }
        }

        private void Start()
        {
            // ⚠️ 美术相关的初始化放 Start 不放 Awake —— `AddComponent` 会**同步**跑 Awake，
            //    而生成方设 `artScale` 是在 AddComponent **之后**，
            //    Awake 里读到的是字段默认值（MinionVisual 为此栽过一次）。
            if (_artNode == null)
            {
                Debug.LogWarning("[FogFire] 找不到 Art 子节点，不会有立绘");
                return;
            }

            _player = _artNode.GetComponent<SequencePlayer>();

            if (_player == null)
            {
                _player = _artNode.gameObject.AddComponent<SequencePlayer>();
            }

            if (_artNode.GetComponent<SpriteRenderer>() == null)
            {
                _artNode.gameObject.AddComponent<SpriteRenderer>();
            }

            // 出场：上升 播一次 → 静息 循环
            _player.aheadPages = 0;
            _player.behindPages = 0;
            _player.Play(RiseAnimation, false, 0, -1,
                GameConfig.Current.Boss.FogFireRiseTimeScale, PlayIdle);
        }

        private void PlayIdle()
        {
            if (_player != null && !_dying)
            {
                _player.Play(IdleAnimation, true);
            }
        }

        private void OnDestroy()
        {
            if (_damageable != null)
            {
                _damageable.OnDeath = null;
                _damageable.OnDamaged = null;
            }

            OnDied?.Invoke(this);
        }

        // ============================================================ 主循环

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_dying)
            {
                UpdateDeath(dt);
                return;
            }

            if (_contactTimer > 0f)
            {
                _contactTimer -= dt;
            }

            if (_repelRemaining > 0f)
            {
                // 被完美格挡弹开：这段时间不主动移动，让位给击退
                _repelRemaining -= dt;
            }
            else
            {
                MoveTowardsTarget();
            }

            ApplyFacing();
            CheckContactDamage();
        }

        /// <summary>
        /// 追玩家。两个轴都追 —— 狐火是飘的，没有「站在地上」这回事。
        ///
        /// <b>保持一段间距，不要贴到玩家身上</b>：完全贴合的话三只狐火会**完全重叠**
        /// （实测三只都停在同一个坐标），看起来像只有一只，玩家也分不清有几只在啃自己。
        /// 间距取接触判定距离以内，所以仍然咬得到人。
        /// </summary>
        /// <summary>悬停距离（配置驱动）—— 再近就不加速了</summary>
        private static float StandoffDistance { get { return GameConfig.Current.Boss.FogFireStandoff; } }

        /// <summary>
        /// 灵泉的排斥半径：狐火的排斥力到这里衰减到 0。
        /// 取自 <see cref="LevelDirector.SpringAvoidRadius"/>（= 石台半宽 × 倍数，不要在这里写死）。
        ///
        /// <b>为什么要有这条</b>：灵泉是玩家的回复点，而狐火是追着人走的 ——
        /// 玩家往泉边一站，狐火就整群挤上来了，「回灵炁」这件事直接被堵死。
        /// 设计上灵泉应该是安全区。
        ///
        /// 用**转向力**排斥而不是物理墙：狐火是浮空的、碰撞矩阵里也不和 WORLD 碰，
        /// 加碰撞体既要改分组又要和地形纠缠；这里只是把目标方向往外偏一点，
        /// 表现上就是「绕着泉飘」，不会卡在边界上抖。
        ///
        /// <b>⚠️ 软排斥：实际能推进来的深度</b>
        ///
        /// 速度是 <c>(dir + avoid).normalized * speed</c>，<c>dir</c> 是**单位向量**，
        /// 而 <c>avoid</c> 线性衰减。玩家站在泉心时 <c>dir</c> 恰好径向朝里，
        /// 要顶住它得 <c>avoid ≥ 1</c>：
        /// <code>
        ///   push(d) = SpringAvoidStrength * (1 - d / R)
        ///   推不动 ⇔ push &lt; 1  ⇔  d &gt; R * (1 - 1 / SpringAvoidStrength)
        ///                            = 0.375 R     （强度 1.6 时）
        /// </code>
        /// 也就是**排斥只在「0.375 R」以外的环带起作用，圈内是无效的**。
        /// 所以看这个圈大不大，要看 0.375 R 而不是 R 本身。
        ///
        /// 现状（R = 石台半宽 × 2.7）：平衡点 ≈ 石台半宽的 100% ——
        /// 狐火停在石台**轮廓之外**，整座泉是安全区。
        /// 要改请调 <see cref="LevelDirector.springAvoidRadiusMult"/>，别动这里的算式。
        ///
        /// <b>数值是怎么定的（别凭感觉调）</b>：要让狐火停在石台外沿，就得让平衡点
        /// ≥ 石台半宽，即 <c>0.375 × mult ≥ 1</c> → <b>mult ≥ 2.67</b>。
        /// 取 2.7 留一点余量。
        ///
        /// 历史两轮：
        /// <list type="bullet">
        ///   <item>×1.2：平衡点只有半宽的 45% —— 狐火一路钻到泉心。</item>
        ///   <item>×2.0：平衡点 75%，**仍在石台轮廓之内**（半宽 171.6、平衡点 129）
        ///     —— 实测「狐火无法进入灵泉还是没起效」。数值上确实生效了（实测三只
        ///     狐火稳定停在 127~131），但停在石头里面，看起来就是没挡住。</item>
        /// </list>
        /// ⚠️ 这个倍数越大，狐火被推得越远、Boss 战压力越低。调之前先想清楚
        /// 是要「狐火不进石头」还是「留一点压力」。
        /// </summary>
        private static float SpringAvoidRadius { get { return LevelDirector.SpringAvoidRadius; } }

        /// <summary>排斥力的强度（相对朝向目标的单位向量）</summary>
        private const float SpringAvoidStrength = 1.6f;

        /// <summary>上下浮动的振幅（世界单位）。够大才看得出在「飞」而不是在「飘」。</summary>
        private const float BobAmplitude = 62f;

        private void MoveTowardsTarget()
        {
            if (_target == null || _rigidBody == null)
            {
                return;
            }

            // 目标点是「玩家位置 + 各自的高度偏移 + 上下浮动」——
            // 高度偏移让几只狐火停在不同高度上围住玩家（而不是叠在同一个坐标），
            // 浮动项则让它们**一直在上下飞**：只在追人的时候才动纵向的话，
            // 到位之后看着就只是「左右平移」，不像飘着的火（实测反馈「狐火好像就是左右飞」）。
            float bob = Mathf.Sin(Time.time * _bobSpeed + _bobPhase) * BobAmplitude;
            Vector2 goal = new Vector2(_target.position.x,
                                       _target.position.y + _hoverOffsetY + bob);
            Vector2 to = goal - (Vector2)transform.position;
            float dist = to.magnitude;

            // 灵泉排斥：叠一个背离泉水方向的力，狐火就会绕着泉飘，进不到圈里
            Vector2 me = transform.position;
            Vector2 avoid = Vector2.zero;

            foreach (Vector2 spring in LevelDirector.Springs)
            {
                Vector2 away = me - spring;
                float d = away.magnitude;

                if (d < SpringAvoidRadius && d > 0.01f)
                {
                    // 越近推得越狠（线性），到边界刚好为 0 —— 不会有突变
                    float push = (1f - d / SpringAvoidRadius) * SpringAvoidStrength;
                    avoid += away / d * push;
                }
            }

            if (dist <= _standoff && avoid.sqrMagnitude < 0.0001f)
            {
                // 到了就悬停（不是停下 —— 残留速度会让它在玩家身边漂走）
                _rigidBody.linearVelocity = Vector2.zero;
                return;
            }

            Vector2 dir = dist > 0.01f ? to / dist : Vector2.zero;
            _rigidBody.linearVelocity = (dir + avoid).normalized * _speed;
        }

        private void ApplyFacing()
        {
            if (_target == null || _artNode == null)
            {
                return;
            }

            float dx = _target.position.x - transform.position.x;

            if (Mathf.Abs(dx) > FacingDeadZone)
            {
                _facing = dx > 0f ? 1f : -1f;
            }

            FacingFlip.Apply(_artNode, _facing, ArtNaturalFacing);
        }

        /// <summary>
        /// 接触伤害。
        ///
        /// ⚠️ **不用物理回调** —— 碰撞矩阵里 PLAYER 与 ENEMY 互不接触（玩家能穿过小怪），
        /// 所以压根没有 <c>OnCollisionEnter2D</c> 可等。每帧自己算矩形重叠，
        /// 与 <see cref="MinionArrow"/> 同一条路径（那边还多一层理由：
        /// 接触链路要穿过「分组 → 夹具过滤 → 刚体类型」四道关，任何一道静默失败都极难查）。
        /// </summary>
        private void CheckContactDamage()
        {
            if (_contactTimer > 0f || _targetCombat == null || _target == null)
            {
                return;
            }

            Vector3 a = transform.position;
            Vector3 t = _target.position;

            BoxCollider2D playerCol = _targetCombat.GetComponent<BoxCollider2D>();
            bool usable = playerCol != null && playerCol.size.x > 1f && playerCol.size.y > 1f;

            float halfW = usable ? playerCol.size.x * 0.5f : FallbackPlayerHalfW;
            float halfH = usable ? playerCol.size.y * 0.5f : FallbackPlayerHalfH;
            float offY = usable ? playerCol.offset.y : FallbackPlayerOffY;

            if (Mathf.Abs(a.x - t.x) > halfW + HitHalfW)
            {
                return;
            }

            if (Mathf.Abs(a.y - (t.y + offY)) > halfH + HitHalfH)
            {
                return;
            }

            PlayerDamageResult result = _targetCombat.ReceivePlayerDamage(_damage);
            _contactTimer = _contactCooldown * _cooldownScale;

            GameLog.V($"[FogFire] 咬到玩家，扣灵炁 {result.SpiritCost}（完美格挡={result.PerfectBlocked}）");

            if (result.PerfectBlocked)
            {
                // 完美格挡把它弹开并短暂失控 —— 与敌人近战反制同一个语义
                Vector2 away = ((Vector2)transform.position - (Vector2)t).normalized;

                if (away.sqrMagnitude < 0.01f)
                {
                    away = Vector2.right * -_facing;
                }

                _rigidBody.linearVelocity = away * RepelSpeed;
                _repelRemaining = 0.35f;
                return;
            }

            if (result.SpiritCost > 0f)
            {
                SpiritEnergySystem.Damage(result.SpiritCost);
            }
        }

        /// <summary>被玩家打中时的受击反馈（闪白）。</summary>
        private void OnHit(DamageInfo info)
        {
            SpriteRenderer sr = _artNode != null ? _artNode.GetComponent<SpriteRenderer>() : null;

            if (sr != null)
            {
                sr.color = new Color32(190, 250, 255, 255);
            }

            _flashRemaining = 0.1f;
        }

        private float _flashRemaining;

        private void LateUpdate()
        {
            if (_flashRemaining <= 0f)
            {
                return;
            }

            _flashRemaining -= Time.deltaTime;

            if (_flashRemaining <= 0f)
            {
                SpriteRenderer sr = _artNode != null ? _artNode.GetComponent<SpriteRenderer>() : null;

                if (sr != null)
                {
                    sr.color = Color.white;
                }
            }
        }

        // ============================================================ 死亡

        private void BeginDeath()
        {
            if (_dying)
            {
                return;
            }

            _dying = true;
            _deathElapsed = 0f;

            if (_rigidBody != null)
            {
                _rigidBody.linearVelocity = Vector2.zero;
                _rigidBody.simulated = false;
            }

            BoxCollider2D col = GetComponent<BoxCollider2D>();

            if (col != null)
            {
                col.enabled = false;
            }

            _player?.Stop();
        }

        /// <summary>缩小 + 淡出，然后销毁。</summary>
        private void UpdateDeath(float dt)
        {
            _deathElapsed += dt;
            float t = Mathf.Min(1f, _deathElapsed / DeathDuration);

            if (_artNode != null)
            {
                float k = _baseScale * Mathf.Lerp(1f, DeathShrink, t);
                float signX = _artNode.localScale.x < 0f ? -1f : 1f;
                _artNode.localScale = new Vector3(signX * k, k, 1f);
            }

            SpriteRenderer sr = _artNode != null ? _artNode.GetComponent<SpriteRenderer>() : null;

            if (sr != null)
            {
                Color c = sr.color;
                c.a = 1f - t;
                sr.color = c;
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
