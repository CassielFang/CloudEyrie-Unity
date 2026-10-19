using UnityEngine;

namespace CloudEyrie
{
    /// <summary>小怪状态（供调试与表现层读取）。</summary>
    public enum EnemyState
    {
        Patrol,
        Discover,
        Chase,
        Attack,
        Dead,
    }

    /// <summary>
    /// 小怪 AI（灰盒最小可用版）：巡逻 → 发现 → 追击 → 攻击 → 死亡，
    /// 另有一层「受击硬直」叠加态，优先级高于上面全部。
    /// 对应 Cocos 侧 <c>enemy/EnemyAI.ts</c>。
    ///
    /// ⚠️ <b>它不是行为树，是手写扁平状态机</b> —— 设计文档里画的是行为树，落地的是 FSM。
    /// 全私有、无基类，<b>Boss 无法继承，只能另写并抄它的模式</b>。
    ///
    /// 数值全部来自 <see cref="GameConfig"/>，本组件不暴露调参面板，
    /// 挂到敌人节点即可，要求同节点有：
    /// <list type="bullet">
    ///   <item><see cref="Rigidbody2D"/>（水平移动）</item>
    ///   <item><see cref="Damageable"/>（挂它的 OnDeath / OnDamaged 回调）</item>
    ///   <item>可选 Art 子节点（立绘，用于朝向翻转 / 状态染色 / 死亡淡出）</item>
    /// </list>
    ///
    /// <b>与战斗系统的两处接口</b>
    /// <list type="bullet">
    ///   <item>打玩家：调 <see cref="CombatController.ReceivePlayerDamage"/>，按返回的
    ///     <c>SpiritCost</c> 扣灵炁；<c>PerfectBlocked</c> 为真时自己吃反制。</item>
    ///   <item>挨打：接管 <see cref="Damageable.OnDamaged"/>，由击退距离驱动受击硬直。</item>
    /// </list>
    ///
    /// 战斗状态上报走 <see cref="SpiritEnergySystem.SetCombatSource"/>，
    /// <b>不要用 <c>SetInCombat</c></b> —— 那是全局单布尔，同屏多只敌人会互相覆写。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Damageable))]
    public class EnemyAI : MonoBehaviour
    {
        /// <summary>巡逻点到达判定阈值（像素）</summary>
        private const float PatrolArriveEps = 12f;

        /// <summary>追击脱战余量：目标距离超过「视野 × 此倍数」才放弃追击</summary>
        private const float ChaseLeashMult = 1.3f;

        /// <summary>
        /// 目标脱离后仍继续追击的宽限时长 /秒。
        ///
        /// 有这段宽限，青禾原地起跳 / 小跳不会立刻把小怪甩掉 —— 否则每跳一下它都要重走
        /// 一遍「返回巡逻 → 发现 0.45 秒 → 追击」，看着像失忆。设为 0 就是「一跳就脱战」。
        /// </summary>
        private const float ChaseLostTargetDuration = 1.2f;

        /// <summary>攻击命中允许的目标位移余量（攻击范围 × 此倍数）</summary>
        private const float AttackReachMult = 1.25f;

        /// <summary>远程型保持的距离 = 射程 × 这个比例</summary>
        private const float RangedKeepRatio = 0.72f;

        /// <summary>后退速度相对追击速度的比例</summary>
        private const float RangedBackoffSpeedRatio = 0.8f;

        /// <summary>受击闪色持续时间 /秒</summary>
        private const float HitFlashDuration = 0.12f;

        /// <summary>死亡表现的缩小比例（终点缩到基准缩放的这么大）</summary>
        private const float DeathShrink = 0.6f;

        // 灰盒状态染色（正式美术接入后连同 RefreshTint 一起删掉）。
        // 提成静态字段而不是实例字段：所有敌人共用同一份，Color 是值语义，不会串色。
        private static readonly Color TintPatrol = new Color32(255, 255, 255, 255);   // 原色（不染色）
        private static readonly Color TintDiscover = new Color32(255, 225, 130, 255); // 黄
        private static readonly Color TintChase = new Color32(255, 165, 90, 255);     // 橙
        private static readonly Color TintAttack = new Color32(255, 110, 110, 255);   // 红

        /// <summary>
        /// 受击闪色。**不能取白色** —— <see cref="TintPatrol"/> 就是 (255,255,255)，
        /// 也就是「不染色」，白闪等于没闪，挨打会完全没有视觉反馈。
        /// 这里取一个与上面四种都不撞的冷白。
        /// </summary>
        private static readonly Color TintHit = new Color32(150, 240, 255, 255);

        /// <summary>
        /// 箭的出生点相对本体中心的偏移。
        ///
        /// <c>ArrowDy</c> 是**量出来的**：在打包画布（512）上，搭在弦上的箭位于身体中心
        /// 下方 21px（身体高 335），换算到世界单位约 <b>-13</b>。
        /// 原按节点原点生成，箭会从比弓高的地方冒出来。
        /// ⚠️ <c>ArtScale</c> 一改这个数就要跟着改。
        /// </summary>
        private const float ArrowDx = 55f;
        private const float ArrowDy = -13f;

        [Header("引用")]
        [Tooltip("玩家目标。留空则运行时自动按 QingheController 查找")]
        [SerializeField] private Transform target;

        [Tooltip("立绘子节点名")]
        [SerializeField] private string artName = "Art";

        [Header("素材")]
        [Tooltip("立绘的天然朝向：1 = 素材画的是朝右，-1 = 朝左。" +
                 "⚠️ 不同敌人可能相反：持弓的阳浊小怪素材是**朝左**画的（-1）")]
        public NaturalFacing artNaturalFacing = NaturalFacing.Right;

        [Tooltip("灰盒状态染色。⚠️ 接入正式逐帧美术后要关掉 —— 它会把整只小怪染成橙/红")]
        public bool tintByState = true;

        /// <summary>
        /// 远程型（持弓的阳浊小怪）：攻击距离取 <c>RangedAttackRange</c>，
        /// 且攻击时**生成一支箭的抛射物**，而不是近身直接结算伤害。
        ///
        /// <b>2026-10-17 由 <c>[NonSerialized]</c> 改成序列化字段。</b>
        /// 原来它只能是运行时赋值（由 LevelDirector 造小怪时设），理由是
        /// 「小怪参数统一走 GameConfig，不给单实例开面板」——
        /// 那是**关卡由代码搭建**时代的约定。
        ///
        /// 关卡改成 prefab 可视化摆放之后，"近战山魈"与"远程弓手"是**两个 prefab**，
        /// 这个区别必须能存在 prefab 上；不然摆出来的近战怪会静默变回远程
        /// （字段没序列化，加载后是 false 以外的默认值，且不报错）。
        ///
        /// 保留 <c>Ranged</c> 这个名字与可写性，老调用点（<c>Ranged = true</c>）不受影响。
        /// </summary>
        [SerializeField] private bool ranged;

        public bool Ranged
        {
            get { return ranged; }
            set { ranged = value; }
        }

        private Rigidbody2D _rigidBody;
        private Damageable _damageable;
        private CombatController _targetCombat;
        private Transform _artNode;
        private SpriteRenderer _artSprite;

        /// <summary>自己 / 目标的碰撞体，只用来取脚底高度（见 <see cref="VerticalOffsetToTarget"/>）</summary>
        private Collider2D _selfCollider;
        private Collider2D _targetCollider;

        private EnemyState _state = EnemyState.Patrol;
        private float _facing = 1f;

        // 巡逻
        private float _spawnX;
        private float _patrolTargetX;
        private float _patrolPauseRemaining;

        // 发现
        private float _discoverRemaining;

        /// <summary>目标脱离视野的累计时长（水平超脱战余量 或 垂直超容差），到阈值就放弃追击</summary>
        private float _lostTargetRemaining;

        // 攻击
        private float _attackElapsed;
        private bool _attackHitDone;

        /// <summary>这次拉弓是否已经放出去过箭（防止动画回调与兜底计时重复放）</summary>
        private bool _arrowReleased;

        /// <summary>补找目标的计时（目标可能后生成）</summary>
        private float _retryTargetTimer;
        private float _cooldownRemaining;

        // 受击硬直
        private float _hitStunRemaining;

        /// <summary>本次硬直的初速：由击退距离换算而来，使整段硬直的位移积分 ≈ 该距离</summary>
        private float _hitStunVx0;

        /// <summary>本次硬直的总时长（进入时快照，避免中途改配置把衰减曲线拉歪）</summary>
        private float _hitStunTotal;

        // 性格：数值完全相同，只错开反应快慢 / 停留长短 / 攻击间隔 / 巡逻偏好（见 Personality）
        /// <summary>个体种子。**由生成方按序号传**，0 表示「用一条兜底序列」。</summary>
        [System.NonSerialized] public int personalitySeed;

        private float _discoverScale = 1f;
        private float _pauseScale = 1f;
        private float _cooldownScale = 1f;

        /// <summary>巡逻中点偏向：正数偏右、负数偏左。让它有「自己的地盘」。</summary>
        private float _patrolBias;

        // 死亡
        private float _deathElapsed;

        /// <summary>立绘基准缩放：取绝对值，符号位留给朝向（见 UpdateDead）</summary>
        private float _artBaseScaleX = 1f;
        private float _artBaseScaleY = 1f;
        private float _hitFlashRemaining;

        // ============================================================ 生命周期

        private void Awake()
        {
            _rigidBody = GetComponent<Rigidbody2D>();

            if (_rigidBody == null)
            {
                Debug.LogError("[EnemyAI] RigidBody2D not found!");
                enabled = false;
                return;
            }

            _damageable = GetComponent<Damageable>();

            if (_damageable == null)
            {
                Debug.LogError("[EnemyAI] Damageable not found!");
                enabled = false;
                return;
            }

            // 刚体不允许休眠（休眠后接触评估会停摆 —— 项目已在攻击命中盒上栽过）
            _rigidBody.sleepMode = RigidbodySleepMode2D.NeverSleep;
            _rigidBody.WakeUp();

            _artNode = transform.Find(artName);
            _artSprite = _artNode != null ? _artNode.GetComponent<SpriteRenderer>() : null;

            // 基准缩放取绝对值：scale.x 的符号位是「朝左/朝右」（见 FacingFlip），
            // 若直接拿来当放大倍率，美术一旦用 scale.x = -1 表达天然朝向，死亡缩小会变成镜像放大
            _artBaseScaleX = _artNode != null ? Mathf.Abs(_artNode.localScale.x) : 1f;
            _artBaseScaleY = _artNode != null ? Mathf.Abs(_artNode.localScale.y) : 1f;

            // 目标：优先 Inspector 指定；留空则自动找青禾
            if (target == null)
            {
                QingheController qinghe = FindAnyObjectByType<QingheController>();

                if (qinghe != null)
                {
                    target = qinghe.transform;
                }
            }

            _targetCombat = target != null ? target.GetComponent<CombatController>() : null;

            // 脚底高度用的碰撞体引用（见 VerticalOffsetToTarget）。
            // 目标可能由生成方后设 —— 这里读不到也不要紧，垂直判定会退回节点原点。
            _selfCollider = GetComponent<Collider2D>();
            _targetCollider = target != null ? target.GetComponent<Collider2D>() : null;

            // 死亡回调：Damageable 掉血到 0 时交给 AI 播放死亡表现
            _damageable.OnDeath = _ => EnterDead();

            // 受击回调：挂上即接管击退（Damageable 不再直接写速度），改成走受击硬直
            _damageable.OnDamaged = (self, info) => EnterHitStun(info.Knockback.x);

            _spawnX = transform.position.x;
            PickPatrolTarget();

            Debug.Log($"[EnemyAI] {name} 启动，目标 {(target != null ? target.name : "未找到（只会巡逻）")}");
        }

        /// <summary>
        /// 性格初始化放 <c>Start</c> 而**不是 Awake**。
        ///
        /// ⚠️ 原因同 <see cref="MinionVisual"/> 的 artScale：生成方是
        /// <c>AddComponent&lt;EnemyAI&gt;()</c> **之后**才赋 <c>personalitySeed</c>，
        /// 而 <c>AddComponent</c> 会**同步跑 Awake** —— 读到的还是默认值 0，
        /// 于是同屏所有敌人算出**一模一样**的性格，正好是这个机制要避免的事
        /// （实测：两只小怪种子分别是 1 和 2，性格参数却完全相同）。
        ///
        /// Unity 保证「所有 Awake 跑完才跑 Start」，放这里就能拿到生成方设的值。
        /// </summary>
        private void Start()
        {
            // 性格：同一只敌人在整局里稳定（种子固定），但个体之间互不相同。
            // 数值一个都不动 —— 只错开时间与相位，见 Personality 的说明。
            Personality persona = new Personality(personalitySeed);
            _discoverScale = persona.Jitter(0.40f);   // 发现反应：快到慢差近一倍
            _pauseScale = persona.Jitter(0.45f);      // 巡逻到点停留
            _cooldownScale = persona.Jitter(0.25f);   // 攻击间隔
            _patrolBias = persona.Signed() * 0.45f;   // 巡逻中点偏向一侧

            // 偏置变了，首个巡逻点要按新偏置重挑一次
            PickPatrolTarget();
        }

        private void OnDestroy()
        {
            if (_damageable != null)
            {
                _damageable.OnDeath = null;
                _damageable.OnDamaged = null;
            }

            // 必须退订战斗来源：否则战斗中切场景 / 销毁敌人后，
            // 灵炁会永久停在战斗恢复档（1/s），而玩家无从察觉
            SpiritEnergySystem.SetCombatSource(this, false);
        }

        /// <summary>
        /// 当前状态。<b>给表现层读的</b>（例如 <see cref="MinionVisual"/> 按状态切序列帧动画）。
        /// 状态机本身仍是私有的，外部不要用它做逻辑判断 —— 那会绕开 AI 的优先级规则。
        /// </summary>
        public EnemyState GetState()
        {
            return _state;
        }

        // ============================================================ 主循环

        private void Update()
        {
            float dt = Time.deltaTime;

            // ⚠️ 目标可能是**后生成**的（关卡把玩家和敌人分开建，玩家往往更晚）。
            // Awake 里只找一次的话，target 会永远是 null ——
            // 表现就是所有小怪都在原地巡逻、既不追也不打（实测：四只小怪 hor=Infinity、sight=False）。
            // 所以这里按固定间隔补找。
            if (target == null)
            {
                _retryTargetTimer -= dt;

                if (_retryTargetTimer <= 0f)
                {
                    _retryTargetTimer = 0.5f;
                    QingheController qinghe = FindAnyObjectByType<QingheController>();

                    if (qinghe != null)
                    {
                        target = qinghe.transform;
                        _targetCombat = qinghe.GetComponent<CombatController>();
                        _targetCollider = qinghe.GetComponent<Collider2D>();
                    }
                }
            }

            if (_state == EnemyState.Dead)
            {
                UpdateDead(dt);
                return;
            }

            // 掉进无底坑：直接消失。不处理的话它会一直往下掉（实测掉到 y=-60 万），
            // 而且永远算「活着的敌人」—— 屏障的 requireEnemiesCleared / 清场编排永远不触发。
            if (transform.position.y < -1000f)
            {
                Destroy(gameObject);
                return;
            }

            // 死亡兜底（Damageable 已把血量清零但还没走到 OnDeath 的情况）
            if (_damageable == null || _damageable.GetHp() <= 0f)
            {
                EnterDead();
                return;
            }

            if (_cooldownRemaining > 0f)
            {
                _cooldownRemaining -= dt;
            }

            if (_hitFlashRemaining > 0f)
            {
                _hitFlashRemaining -= dt;
            }

            // 受击硬直优先级高于一切行为：期间不跑状态机、也不写状态机速度 ——
            // 否则每帧的速度覆写会把击退抹掉，打小怪就完全没有受击位移了。
            // 这里只跑击退曲线。
            if (_hitStunRemaining > 0f)
            {
                UpdateHitStun(dt);
                RefreshTint();
                return;
            }

            float dirX = DirXToTarget();

            switch (_state)
            {
                case EnemyState.Patrol:
                    UpdatePatrol(dt);
                    break;
                case EnemyState.Discover:
                    UpdateDiscover(dt, dirX);
                    break;
                case EnemyState.Chase:
                    UpdateChase(dt, dirX);
                    break;
                case EnemyState.Attack:
                    UpdateAttack(dt, dirX);
                    break;
            }

            ApplyFacing();
            RefreshTint();
        }

        // ============================================================ 各状态

        private void UpdatePatrol(float dt)
        {
            EnemyConfig config = GameConfig.Current.Enemy;

            // 垂直容差之外不算「发现」：从它头顶跳过去不会触发仇恨
            if (IsTargetInSight())
            {
                EnterDiscover();
                return;
            }

            _patrolPauseRemaining -= dt;

            if (_patrolPauseRemaining > 0f)
            {
                SetVelocityX(0f);
                return;
            }

            float x = transform.position.x;
            float dx = _patrolTargetX - x;

            if (Mathf.Abs(dx) <= PatrolArriveEps)
            {
                SetVelocityX(0f);
                PickPatrolTarget();
                _patrolPauseRemaining = config.PatrolPause * _pauseScale;
                return;
            }

            float dir = dx > 0f ? 1f : -1f;
            _facing = dir;
            SetVelocityX(dir * config.PatrolSpeed);
        }

        private void UpdateDiscover(float dt, float dirX)
        {
            _discoverRemaining -= dt;
            _facing = dirX;
            SetVelocityX(0f);

            if (IsTargetInAttackRange() && _cooldownRemaining <= 0f)
            {
                EnterAttack();
                return;
            }

            if (_discoverRemaining <= 0f)
            {
                if (IsTargetInSight())
                {
                    SetState(EnemyState.Chase);
                }
                else
                {
                    EnterPatrol();
                }
            }
        }

        private void UpdateChase(float dt, float dirX)
        {
            EnemyConfig config = GameConfig.Current.Enemy;
            _facing = dirX;

            // 远程型要**保持距离**。
            //
            // 之前它和近战共用同一套追击逻辑：一路贴到玩家脸上再放箭，
            // 箭从零距离射出来，玩家既看不到也躲不掉（实测反馈"总是贴身、躲不掉"）。
            // 现在：太近了就后退，进了射程就停下拉弓。
            if (Ranged)
            {
                float dist = HorizontalDistanceToTarget();
                float keep = config.RangedAttackRange * RangedKeepRatio;

                if (dist < keep)
                {
                    // 后退。朝向仍然对着玩家（要射的方向），但移动方向相反
                    _facing = dirX;
                    SetVelocityX(-dirX * config.ChaseSpeed * RangedBackoffSpeedRatio);
                    _lostTargetRemaining = 0f;
                    return;
                }
            }

            if (IsTargetInAttackRange())
            {
                _lostTargetRemaining = 0f;

                if (_cooldownRemaining <= 0f)
                {
                    EnterAttack();
                }
                else
                {
                    // 攻击冷却中，远程型停在原地等，不要贴上来
                    SetVelocityX(0f);
                }

                return;
            }

            // 目标脱离：水平超脱战余量，或垂直超容差（青禾跳起来了 / 上了高台）。
            //
            // 脱战余量要按**有效视野**算，不能用基础 SightRange ——
            // 远程型的射程有 460，拿 140×1.3=182 当脱战线的话，
            // 玩家还在射程内它就放弃追击了。
            bool outOfLeash = HorizontalDistanceToTarget() > EffectiveSightRange() * ChaseLeashMult;
            bool outOfLevel = VerticalOffsetToTarget() > config.VerticalTolerance;

            if (outOfLeash || outOfLevel)
            {
                _lostTargetRemaining += dt;
                SetVelocityX(0f);

                if (_lostTargetRemaining >= ChaseLostTargetDuration)
                {
                    EnterPatrol();
                }

                return;
            }

            // 重新够得着：清零计时，继续追
            _lostTargetRemaining = 0f;
            SetVelocityX(dirX * config.ChaseSpeed);
        }

        private void UpdateAttack(float dt, float dirX)
        {
            EnemyConfig config = GameConfig.Current.Enemy;
            _attackElapsed += dt;

            // 远程型一旦开始拉弓就**锁死朝向** —— 撒手前还跟着玩家转身，
            // 会出现「箭朝左飞、弓却转向右」的穿帮（射向在 EnterAttack 时已定）。
            if (!Ranged)
            {
                _facing = dirX;
            }

            SetVelocityX(0f);

            // 近战：前摇到点就打一下
            if (!Ranged && !_attackHitDone && _attackElapsed >= config.AttackStartup)
            {
                _attackHitDone = true;

                // 出招瞬间再确认一次：青禾在这段前摇里跳开 / 拉开距离就落空
                if (IsTargetInAttackRange(AttackReachMult))
                {
                    ApplyDamage();
                }
                else
                {
                    GameLog.V("[EnemyAI] 攻击落空（目标已离开范围）");
                }
            }

            // 远程：**兜底放箭**。
            //
            // 原本只靠 MinionVisual 在"放箭那一帧"回调 ReleaseArrow()；
            // 一旦立绘没挂上/动画没跑到那一帧，箭就永远不生成 —— 玩家莫名掉灵炁却看不到箭。
            // 这里按前摇时间兜底一次，保证"只要它拉弓，就一定有一支看得见的箭飞出来"。
            if (Ranged && !_arrowReleased && _attackElapsed >= config.AttackStartup)
            {
                _arrowReleased = true;
                ShootArrow();
            }

            // 远程攻击要等拉弓动画走完，时长比近战长得多（配置里单列）
            float duration = Ranged ? config.RangedAttackDuration : config.AttackDuration;

            if (_attackElapsed >= duration)
            {
                SetState(EnemyState.Chase);
            }
        }

        private void UpdateDead(float dt)
        {
            EnemyConfig config = GameConfig.Current.Enemy;

            if (config.DeathDuration <= 0f)
            {
                gameObject.SetActive(false);
                return;
            }

            _deathElapsed += dt;
            float t = Mathf.Min(1f, _deathElapsed / config.DeathDuration);

            // 缩小 + 淡出
            if (_artNode != null)
            {
                float k = 1f - DeathShrink * t;

                // 保留 scale.x 的符号：否则朝左死亡时，尸体会在断气瞬间翻回朝右
                float signX = _artNode.localScale.x < 0f ? -1f : 1f;
                _artNode.localScale = new Vector3(
                    signX * _artBaseScaleX * k,
                    _artBaseScaleY * k,
                    _artNode.localScale.z);
            }

            if (_artSprite != null)
            {
                Color c = _artSprite.color;
                c.a = 1f - t;
                _artSprite.color = c;
            }

            if (t >= 1f)
            {
                gameObject.SetActive(false);
            }
        }

        // ============================================================ 状态切换

        private void EnterDiscover()
        {
            SetState(EnemyState.Discover);
            _discoverRemaining = GameConfig.Current.Enemy.DiscoverDuration * _discoverScale;
            SetVelocityX(0f);
            _facing = DirXToTarget();

            // 上报「我是战斗威胁来源」而不是直接置战斗位：同屏多只敌人时互不覆写
            SpiritEnergySystem.SetCombatSource(this, true);
            Debug.Log($"[EnemyAI] {name} 发现玩家");
        }

        private void EnterAttack()
        {
            EnemyConfig config = GameConfig.Current.Enemy;
            SetState(EnemyState.Attack);
            _attackElapsed = 0f;
            _attackHitDone = false;
            _arrowReleased = false;

            // ⚠️ 冷却是在 Update 里逐帧递减的，**攻击期间也在走**。
            // 近战动画只有 0.55 秒，所以「时长 < 冷却」时周期自然是冷却；
            // 但远程的拉弓动画长达 1.75 秒、和冷却几乎相等 —— 冷却会在动画结束的同一刻
            // 走完，小怪于是**一直在拉弓**（观感就是「箭永远吊着」）。
            // 远程因此把冷却算在**动画结束之后**。
            _cooldownRemaining = (Ranged
                ? config.RangedAttackDuration + config.AttackCooldown
                : config.AttackCooldown) * _cooldownScale;

            _facing = DirXToTarget();
            SetVelocityX(0f);
            GameLog.V($"[EnemyAI] {name} 攻击");
        }

        private void EnterPatrol()
        {
            SetState(EnemyState.Patrol);
            SpiritEnergySystem.SetCombatSource(this, false);
            PickPatrolTarget();

            // 主动补一次停留计时：从追击切回来时若不设，会立刻往下走、白瞎「停留」参数
            _patrolPauseRemaining = GameConfig.Current.Enemy.PatrolPause * _pauseScale;

            // 脱战计时归零，下一轮仇恨重新起算
            _lostTargetRemaining = 0f;
            Debug.Log($"[EnemyAI] {name} 返回巡逻");
        }

        private void EnterDead()
        {
            if (_state == EnemyState.Dead)
            {
                return;
            }

            SetState(EnemyState.Dead);
            _deathElapsed = 0f;
            SetVelocityX(0f);

            // 关闭物理，避免死亡后继续被打 / 被碰撞
            BoxCollider2D col = GetComponent<BoxCollider2D>();

            if (col != null)
            {
                col.enabled = false;
            }

            if (_rigidBody != null)
            {
                _rigidBody.simulated = false;
            }

            SpiritEnergySystem.SetCombatSource(this, false);
            Debug.Log($"[EnemyAI] {name} 死亡");
        }

        /// <summary>
        /// 进入受击硬直。
        ///
        /// <paramref name="knockbackX"/> 是**击退距离**（与 <c>ComboKnockback</c> /
        /// <c>HeavyKnockback</c> 同义），不是速度：这里换算成初速，硬直期间做线性衰减，
        /// 使位移的时间积分 ≈ 该距离。好处是击退手感不再依赖刚体阻尼。
        ///
        /// 这也是原先「击退失效」的修复：AI 每帧都在写水平速度，
        /// <see cref="Damageable"/> 写进去的击退速度活不过一帧，所以必须由 AI 自己持有这段曲线。
        /// </summary>
        private void EnterHitStun(float knockbackX)
        {
            if (_state == EnemyState.Dead)
            {
                return;
            }

            // 只让「带击退的命中」进硬直。
            // 轻击第 1、2 段在配置里击退为 0（只有第 3 段和重击带击退），
            // 若这些也照打硬直，玩家一路轻击就能把小怪永久锁死。
            // 这两段的受击反馈由闪色提供。
            if (Mathf.Abs(knockbackX) < 1e-6f)
            {
                return;
            }

            float duration = GameConfig.Current.Enemy.HitStunDuration;
            _hitStunRemaining = duration;
            _hitStunTotal = duration;
            _hitStunVx0 = duration > 0f ? (2f * knockbackX) / duration : 0f;

            // 打断进行中的攻击：置位 _attackHitDone，
            // 否则硬直结束后那次「迟到的判定」还会补在玩家身上
            if (_state == EnemyState.Attack)
            {
                _attackHitDone = true;
                SetState(EnemyState.Chase);
            }
        }

        /// <summary>每帧推进硬直：速度从初速线性衰减到 0</summary>
        private void UpdateHitStun(float dt)
        {
            _hitStunRemaining -= dt;
            float k = _hitStunTotal > 0f ? Mathf.Max(0f, _hitStunRemaining) / _hitStunTotal : 0f;
            SetVelocityX(_hitStunVx0 * k);
        }

        private void SetState(EnemyState s)
        {
            _state = s;
        }

        // ============================================================ 战斗结算

        private void ApplyDamage()
        {
            float amount = GameConfig.Current.Spirit.EnemyYangMeleeDamage;

            PlayerDamageResult result = _targetCombat != null
                ? _targetCombat.ReceivePlayerDamage(amount)
                : new PlayerDamageResult { SpiritCost = amount, PerfectBlocked = false };

            _hitFlashRemaining = HitFlashDuration;

            // 完美格挡反制：把小怪朝背离玩家的方向弹开并打入硬直。
            // 力度暂借重击的击退参数，要单独调再拆一个配置项出来。
            if (result.PerfectBlocked)
            {
                EnterHitStun(DirAwayFromTarget() * GameConfig.Current.Combat.HeavyKnockback);
                Debug.Log($"[EnemyAI] {name} 被完美格挡反制");
                return;
            }

            if (result.SpiritCost > 0f)
            {
                SpiritEnergySystem.Damage(result.SpiritCost);
            }

            GameLog.V($"[EnemyAI] 命中玩家，扣灵炁 {result.SpiritCost}（原始 {amount}）");
        }

        /// <summary>
        /// <b>放箭</b>。由 <see cref="MinionVisual"/> 在攻击动画的放箭帧回调 ——
        /// 这样箭的出手时刻和「撒手」那一帧严格对齐，而不是各按各的计时器。
        /// </summary>
        public void ReleaseArrow()
        {
            if (!Ranged || _state == EnemyState.Dead)
            {
                return;
            }

            _hitFlashRemaining = HitFlashDuration;
            _arrowReleased = true;
            ShootArrow();
        }

        /// <summary>射一支箭。出生点对齐弓的高度，朝**拉弓时锁定的朝向**飞。</summary>
        private void ShootArrow()
        {
            // ⚠️ 这里曾经有一句 `if (parent == null) return;` —— 而本项目的敌人是**场景根节点**
            // （模组化关卡里机关/敌人都是散件，没有统一的父节点），于是这句话让**每一支箭都不生成**。
            // 表现就是玩家一直在掉灵炁、却从来没见过箭。
            // 父节点为空完全合法：MinionArrow.Spawn 里 SetParent(null) 就是挂到场景根。
            Transform parent = transform.parent;

            // 用 _facing（进攻击时锁定）而不是重新算朝向 —— 保证箭和弓同向
            float dir = _facing != 0f ? _facing : 1f;
            EnemyConfig cfg = GameConfig.Current.Enemy;
            Vector3 pos = transform.position;

            // ⚠️ **箭的出生点要跟着立绘的偏移走。**
            //
            // `ArrowDy` 是在 Cocos 那边量出来的（箭在动画里位于身体中心下方 13 单位），
            // 但当时立绘节点在 0 位。Unity 这边为了让小怪**踩在地上**，
            // `MinionVisual` 把立绘往下挪了一截（对齐碰撞体底边）—— 动画里的箭跟着下移了，
            // 而这里还按原来的常量生成，于是箭和弓对不上（差的就是那段偏移）。
            //
            // 直接读立绘的当前偏移，以后立绘怎么调都不会再错位。
            float artOffsetY = _artNode != null ? _artNode.localPosition.y : 0f;

            MinionArrow.Spawn(parent,
                new Vector2(pos.x + dir * ArrowDx, pos.y + ArrowDy + artOffsetY),
                dir, cfg.ArrowDamage, cfg.ArrowSpeed);

            GameLog.V($"[EnemyAI] {name} 射箭（朝 {(dir > 0f ? "右" : "左")}）");
        }

        // ============================================================ 距离判定
        //
        // 距离判定一律**水平 / 垂直分两个轴**，别退回 Vector3.Distance。
        //
        // 本作敌人的刚体 gravityScale = 0，它只沿 X 移动、永远停在同一个 Y 上，
        // 而青禾会跳。两种错误都真出过：
        //   - 混着算（三维距离）：光是两者 35px 的天然高差就白白吃掉 9% 的攻击距离，
        //     青禾一起跳整段滞空都打不到 —— 表现成「贴着你走却不动手」；
        //   - 只算水平、不管 Y：走到另一个极端，跳到它头顶上照样挨打。
        // 所以水平方向用配置里的像素值，垂直方向用 VerticalTolerance
        // 明确表达「算不算在同一层」。

        private float HorizontalDistanceToTarget()
        {
            if (target == null)
            {
                return float.PositiveInfinity;
            }

            return Mathf.Abs(target.position.x - transform.position.x);
        }

        /// <summary>
        /// 与目标的垂直高差绝对值（像素）。**比的是两者的脚底，不是节点原点。**
        ///
        /// ⚠️ 原点会骗人：两个角色的碰撞体**大小和偏移都不一样**
        /// （小怪 80×130 偏移 -21，青禾是随整体缩放变的 30×112.5 偏移 +41.25），
        /// 所以「都站在同一块地上」时两个节点的 y 差着 71px —— 而
        /// <c>VerticalTolerance</c> 是 70。差这 1px，结果就是**站在同一层上也判成不同层**，
        /// 表现是「走进范围里它不动手，非得跳一下才仇恨」（跳起来原点抬高、差值掉到 70 以内）。
        ///
        /// 更糟的是青禾的 <c>bodyScale</c> 一调，这个差值就跟着变（0.75 时 71、1.0 时 66），
        /// 也就是**换个缩放就换一种 bug**。所以这里改成比脚底：
        /// 脚底 = <see cref="Collider2D.bounds"/> 的底边，两人都站在地上时恒等，
        /// 和碰撞体多大、缩放多少都无关。
        ///
        /// 语义也随之更直白：VerticalTolerance 从此就是「脚的落点差多少算同一层」。
        /// 跳起来脚会离开这条线，跳太高照样脱战 —— 那是有意的（见 ChaseLostTargetDuration）。
        /// </summary>
        private float VerticalOffsetToTarget()
        {
            if (target == null)
            {
                return float.PositiveInfinity;
            }

            return Mathf.Abs(FootY(_targetCollider, target) - FootY(_selfCollider, transform));
        }

        /// <summary>
        /// 某个节点的**脚底**世界 y：碰撞体包围盒的底边；没有碰撞体时退回节点原点。
        ///
        /// 用碰撞体而不是立绘：立绘的底边还带着画布透明留白（青禾那张要再扣 12.2%），
        /// 而碰撞体底边本来就是按「踩在地面上」摆的。
        ///
        /// 碰撞体引用由调用方缓存（距离判定每帧要跑好几次，逐次 <c>GetComponent</c> 没必要）；
        /// 但 <c>bounds</c> 是**每帧现读**的 —— 青禾换整体缩放时碰撞体尺寸会变，
        /// 缓存尺寸就会读到旧的。
        /// </summary>
        private static float FootY(Collider2D col, Transform t)
        {
            return col != null ? col.bounds.min.y : t.position.y;
        }

        /// <summary>
        /// 目标是否算「在 horizontalRange 内」：水平够近 **且** 高差在垂直容差内。
        /// 发现距离与攻击距离共用这一套判定。
        /// </summary>
        private bool IsTargetInRange(float horizontalRange)
        {
            if (target == null)
            {
                return false;
            }

            if (HorizontalDistanceToTarget() > horizontalRange)
            {
                return false;
            }

            return VerticalOffsetToTarget() <= VerticalToleranceFor(horizontalRange);
        }

        /// <summary>
        /// 垂直容差。远程型要放宽 —— 它站在高台上、玩家在地面打，
        /// 高差很容易超过 70（实测：弓手在土台上，玩家在下面 250，于是它完全不动、一支箭都不射）。
        /// 近战型必须保留原来的小容差，否则它会隔着地面追玩家。
        /// </summary>
        private float VerticalToleranceFor(float horizontalRange)
        {
            float baseTol = GameConfig.Current.Enemy.VerticalTolerance;

            if (!Ranged)
            {
                // 近战也要给一点余量：玩家站在稍高一点的小台阶上（高差 70~110）时，
                // 原来的 70 会让小怪一直贴着转圈却不攻击（实测反馈"非要贴身才打"）。
                return Mathf.Max(baseTol, 110f);
            }

            // 射程内的高差容忍随距离放大，但仍有限度，避免"隔着整张地图锁定"
            return Mathf.Max(baseTol, 220f);
        }

        /// <summary>
        /// 视野距离。远程型的视野要**比射程更远**，两者相等会出问题：
        /// 只要看得见就够得着 → 一进视野就直接开打、永远不走过来。
        /// 留出 1.5 倍余量，它才会「看见 → 走近 → 进入射程再打」。
        /// </summary>
        private float EffectiveSightRange()
        {
            EnemyConfig e = GameConfig.Current.Enemy;

            return Ranged
                ? Mathf.Max(e.SightRange, e.RangedAttackRange * 1.5f)
                : e.SightRange;
        }

        /// <summary>目标是否在视野内（发现 / 脱战判定用）</summary>
        private bool IsTargetInSight()
        {
            return IsTargetInRange(EffectiveSightRange());
        }

        /// <summary>目标是否在攻击距离内（reachMult 供命中那一下放宽用）</summary>
        private bool IsTargetInAttackRange(float reachMult = 1f)
        {
            EnemyConfig e = GameConfig.Current.Enemy;
            float range = Ranged ? e.RangedAttackRange : e.AttackRange;
            return IsTargetInRange(range * reachMult);
        }

        /// <summary>背离目标的方向（完美格挡反制用）</summary>
        private float DirAwayFromTarget()
        {
            if (target == null)
            {
                return -_facing;
            }

            float dx = transform.position.x - target.position.x;

            if (dx != 0f)
            {
                return dx > 0f ? 1f : -1f;
            }

            return -_facing;
        }

        private float DirXToTarget()
        {
            if (target == null)
            {
                return _facing;
            }

            return target.position.x >= transform.position.x ? 1f : -1f;
        }

        private void SetVelocityX(float vx)
        {
            if (_rigidBody == null)
            {
                return;
            }

            Vector2 v = _rigidBody.linearVelocity;
            _rigidBody.linearVelocity = new Vector2(vx, v.y);
        }

        private void PickPatrolTarget()
        {
            float range = GameConfig.Current.Enemy.PatrolRange;
            // 偏置 + 随机：偏置让它有「自己的地盘」（两只不会总在同一段来回），
            // 随机负责每次到点的方向变化。夹在 ±1 内免得越走越偏。
            float t = Mathf.Clamp(Random.Range(-1f, 1f) + _patrolBias, -1f, 1f);
            _patrolTargetX = _spawnX + t * range;
        }

        private void ApplyFacing()
        {
            FacingFlip.Apply(_artNode, _facing, artNaturalFacing);
        }

        /// <summary>
        /// 状态染色（灰盒期用来区分巡逻/发现/追击/攻击）。
        ///
        /// ⚠️ <b>接入正式逐帧美术后要关掉</b>（<c>tintByState = false</c>）——
        /// 它会把整只小怪染成橙/红，真美术上非常难看。
        /// </summary>
        private void RefreshTint()
        {
            if (!tintByState || _artSprite == null)
            {
                return;
            }

            if (_hitFlashRemaining > 0f)
            {
                _artSprite.color = TintHit;
                return;
            }

            switch (_state)
            {
                case EnemyState.Discover:
                    _artSprite.color = TintDiscover;
                    break;
                case EnemyState.Chase:
                    _artSprite.color = TintChase;
                    break;
                case EnemyState.Attack:
                    _artSprite.color = TintAttack;
                    break;
                default:
                    _artSprite.color = TintPatrol;
                    break;
            }
        }
    }
}
