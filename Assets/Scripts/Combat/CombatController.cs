using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 战斗控制器（挂在青禾节点）：独占攻击 / 格挡输入，内聚轻击三连 / 蓄力重击 /
    /// 格挡·完美格挡 / 灵雾灵弹 / 灵合光环与爆发。
    /// 对应 Cocos 侧 <c>combat/CombatController.ts</c>。
    ///
    /// 由 <see cref="QingheController"/> 每帧调用 <see cref="Tick"/>
    /// （**先于**形态 tick，避免门控延迟一帧）。
    /// </summary>
    // 注意：**不要**加 [RequireComponent(typeof(Damageable))]。
    // Cocos 侧的青禾节点上没有 Damageable（敌人打玩家走的是 ReceivePlayerDamage，
    // 不是 Damageable 那条链路）—— 加了会平白多出一个没人用的血量组件。
    public class CombatController : MonoBehaviour
    {
        /// <summary>
        /// 挥砍墨迹相对角色的水平偏移（朝向前方）。
        ///
        /// <c>Base</c> 后缀那对是**量出来的原始值**，实际用的是下面按人物缩放换算过的属性
        /// —— 人物缩小后墨迹还炸在原来的距离上，看着就像「特效脱离了刀锋」。
        /// </summary>
        private const float SlashOffsetXBase = 48f;

        /// <summary>重击墨迹更靠前一点（它本身也更大）</summary>
        private const float SlashHeavyOffsetXBase = 66f;

        /// <summary>人物整体缩放相对「量数值那一版」的比例，见 <see cref="QingheController.BodyScaleRatio"/></summary>
        private float BodyScaleK
        {
            get { return _qinghe != null ? _qinghe.BodyScaleRatio : 1f; }
        }

        private float SlashOffsetX
        {
            get { return SlashOffsetXBase * BodyScaleK; }
        }

        private float SlashHeavyOffsetX
        {
            get { return SlashHeavyOffsetXBase * BodyScaleK; }
        }

        /// <summary>
        /// 灵弹发射点相对角色节点的偏移。
        /// 角色节点原点在**脚底**（碰撞体底边），直接拿它当发射点灵弹会从脚底冒出来，
        /// 这里按立绘身高抬到胸口、并朝前让开一点。
        /// </summary>
        /// <summary>
        /// 灵弹出膛点相对角色节点（脚底）的前方距离。
        /// ⚠️ 乘了人物缩放（见 <see cref="BodyScaleK"/>）—— 它和身高是一套量纲，
        /// 人物缩小后出膛点还停在原处，灵弹会像从身后窜出来。
        /// y 不用管：抬到胸口的那个走 <see cref="ChestHeight"/>，本来就按身高算。
        /// </summary>
        private float MistShotSpawnX { get { return GameConfig.Current.Combat.MistShotSpawnX * BodyScaleK; } }
        private static float MistShotSpawnY { get { return GameConfig.Current.Combat.MistShotSpawnY; } }

        /// <summary>灵弹显示尺寸 / 碰撞体尺寸</summary>
        private static float MistShotSize { get { return GameConfig.Current.Combat.MistShotSize; } }
        private static float MistShotBody { get { return GameConfig.Current.Combat.MistShotBody; } }

        /// <summary>命中火花显示尺寸（再按重击倍数放大）</summary>
        private static float HitSparkSize { get { return GameConfig.Current.Combat.HitSparkSize; } }

        /// <summary>光刃素材本身画的是「弧口朝左、弧背鼓向右」—— 即天然朝右</summary>
        private const NaturalFacing SlashNaturalFacing = NaturalFacing.Right;

        /// <summary>净化光环的结算间隔 /秒</summary>
        private static float AuraTick { get { return GameConfig.Current.Combat.AuraTick; } }

        /// <summary>可受击目标的重扫间隔 /秒</summary>
        private const float ScanInterval = 1.0f;

        // ---- 攻击时序 ----
        [Header("战斗时序")]
        [SerializeField] private float attackStartup = 0.1f;
        [SerializeField] private float attackActiveDuration = 0.2f;
        [SerializeField] private float attackRecovery = 0.1f;

        private BoxCollider2D _attackHitBox;
        private AttackHitBox _hitBoxForwarder;

        /// <summary>判定盒中心（相对角色节点）；Awake 折算一次后不再变</summary>
        private Vector3 _attackHitBoxLocalPos;
        private bool _attackHitBoxCenterReady;

        // ---- 灰盒视觉（可选子节点，缺了不影响任何逻辑与判定）----
        private Transform _slashLight;
        private Transform _slashHeavy;
        private Transform _shieldNode;

        /// <summary>本段攻击是否重击（决定显示哪把光刃）</summary>
        private bool _isHeavyAttack;

        private Sprite _mistShotSprite;
        private Sprite _hitSparkSprite;

        /// <summary>缓存的玩家控制器 —— 特效定位要读它的实际身高</summary>
        private QingheController _qinghe;

        private FormContext _ctx;
        private CompanionForm _currentForm = CompanionForm.Entity;

        // ---- 表现层：帧序列 ----
        // 战斗只管时序与判定，动作由 SpriteSequenceView 播。这里缓存一次引用，
        // 没有视图时安静跳过（不要每帧 GetComponent）。
        private SpriteSequenceView _view;
        private bool _viewSearched;

        private static readonly string[] LightClips =
        {
            "青禾-轻击一", "青禾-轻击二", "青禾-轻击三",
        };

        private SpriteSequenceView View
        {
            get
            {
                if (!_viewSearched)
                {
                    _viewSearched = true;
                    _view = GetComponentInChildren<SpriteSequenceView>();
                }

                return _view;
            }
        }

        /// <summary>播一次性动作；没有视图就什么都不做（逻辑不依赖表现）。</summary>
        private void PlayViewClip(string clipName, float timeScale = 1f)
        {
            SpriteSequenceView v = View;

            if (v != null)
            {
                v.PlayOneShot(clipName, timeScale);
            }
        }

        // ---- 输入状态 ----
        private bool _attackHeld;
        private float _chargeTime;
        private bool _blockHeld;
        private float _blockElapsed;

        // ---- 攻击状态机 ----
        private bool _attacking;
        private bool _attackingActive;
        private float _attackTimer;
        private float _pendingDamage;
        private float _pendingKnockback;

        // ---- 轻击连段 ----
        private int _comboIndex;
        private float _comboTimer;

        /// <summary>攻击进行中收到的轻击输入（收招时自动接上，保证连段流畅）</summary>
        private bool _bufferedLight;

        /// <summary>
        /// 本次「按下」发生在攻击过程中 —— 松手时不要再按按住时长去判定轻/重。
        ///
        /// ⚠️ 为什么需要它：<c>_chargeTime</c> **只在按下时清零**，而攻击进行中它仍继续累加。
        /// 于是「攻击中按一下攻击键」只要按住累计超过 <c>LightTapThreshold</c>(0.2s)，
        /// 松手时就会被判成蓄力重击 → 走 `!isLight` 分支 → 因为正在攻击而被**整个丢弃**。
        ///
        /// 表现是：轻点能接上连段，稍微按久一点就**完全没反应**，只能干等这一段打完 ——
        /// 手感上就是「后摇变长了」（实测反馈：把后摇调短反而更明显）。
        /// </summary>
        private bool _swallowAttackRelease;

        // ---- 格挡 ----
        private bool _blocking;

        // ---- 灵合形态 ----
        private float _auraAccum;

        /// <summary>本次灵合是否已用过灵炁爆发</summary>
        private bool _burstUsed;

        /// <summary>
        /// 换形态后「攻击键一直按着」的补发窗口 /秒。
        ///
        /// 灵雾/灵合的攻击挂在按键**上升沿**上，所以按住 J 再切形态原本什么都不会发生
        /// （必须松手再按）。见 <c>Tick</c> 里那段宽限的说明。
        /// </summary>
        private float _formAttackGrace;

        /// <summary>补发窗口时长 /秒。够覆盖「切形态与按键同帧到下一帧」的抖动，又不至于让人觉得是自动放招。</summary>
        private const float FormAttackGraceSeconds = 0.25f;

        /// <summary>按当前形态派发一次**瞬时攻击**（灵雾的灵弹 / 灵合的灵炁爆发）。</summary>
        private void TryFormAttack()
        {
            if (_currentForm == CompanionForm.Mist)
            {
                FireMistShot();
            }
            else if (_currentForm == CompanionForm.Merge)
            {
                FireMergeBurst();
            }
        }

        private Damageable[] _damageables = new Damageable[0];
        private float _scanTimer;

        // ---- 命中去重（每次攻击只结算一次） ----
        private readonly HashSet<Damageable> _hitSet = new HashSet<Damageable>();

        /// <summary>
        /// 复用缓冲区：把命中盒的重叠集合拷进来再遍历。
        /// 见 <see cref="AttackHitBox.CopyOverlapping"/> —— 直接遍历集合会在打死敌人时炸。
        /// 复用是为了不在攻击判定期每帧分配。
        /// </summary>
        private readonly List<Damageable> _overlapBuffer = new List<Damageable>();

        // ============================================================ 生命周期

        private void Awake()
        {
            // 先拿青禾：命中盒 / 特效偏移都要按它的整体缩放换算，下面的折算块要用。
            // （同节点组件的 Awake 顺序按组件顺序走，这里不能假定 QingheController 已经跑过 ——
            //  但 bodyScale 是序列化字段，没跑过 Awake 也读得到正确的值。）
            _qinghe = GetComponent<QingheController>();

            Transform hitBoxNode = transform.Find("AttackHitBox");

            if (hitBoxNode != null)
            {
                _attackHitBox = hitBoxNode.GetComponent<BoxCollider2D>();
                _hitBoxForwarder = hitBoxNode.GetComponent<AttackHitBox>();

                if (_hitBoxForwarder == null)
                {
                    Debug.LogError("[Combat] AttackHitBox 节点上缺少 AttackHitBox 组件，" +
                                   "命中集合不会被维护，攻击将打不到任何敌人。");
                }

                // ⚠️ 命中盒刚体必须是 **Kinematic**。
                //
                // Cocos 那边的对应类型是 `Animated`（夹具跟随节点变换），并且注释里
                // 记着两个致命错误：`Static` 的夹具不跟随节点移动、`Dynamic` 会休眠
                // —— 两者都表现为「攻击时灵时不灵」。
                // Unity 里 `Dynamic` 同样会被物理引擎接管、**不再跟随父节点**，
                // 所以这里强制成 Kinematic（跟随 Transform，且仍产生触发器事件）。
                Rigidbody2D rb = hitBoxNode.GetComponent<Rigidbody2D>();

                if (rb == null)
                {
                    Debug.LogError("[Combat] AttackHitBox 节点上缺少 Rigidbody2D，" +
                                   "触发器事件不会触发。");
                }
                else
                {
                    if (rb.bodyType != RigidbodyType2D.Kinematic)
                    {
                        Debug.LogWarning($"[Combat] AttackHitBox 的刚体类型是 {rb.bodyType}，" +
                                         "已强制改为 Kinematic（Dynamic 会被物理接管、不再跟随玩家）。");
                        rb.bodyType = RigidbodyType2D.Kinematic;
                    }

                    rb.gravityScale = 0f;
                }
            }

            if (_attackHitBox == null)
            {
                Debug.LogError("[Combat] AttackHitBox not found!");
                enabled = false;
                return;
            }

            // 命中盒的实际位置 = 节点位置 + 碰撞体 offset，但**只有节点位置会随朝向翻转**，
            // offset 不会 —— 若 offset 非 0，朝左攻击时判定盒会留在角色身后（够不着敌人）。
            // 这里把 offset 折进 _attackHitBoxLocalPos 并把 offset 归零，从根上避免这个坑。
            // 只折算一次：PlaceAttackHitBox 会移动这个节点，若二次进入 Awake
            // 会把已被移动的位置当成原始位置，判定盒朝向就反了。
            if (!_attackHitBoxCenterReady)
            {
                _attackHitBoxLocalPos = new Vector3(
                    _attackHitBox.transform.localPosition.x + _attackHitBox.offset.x,
                    _attackHitBox.transform.localPosition.y + _attackHitBox.offset.y,
                    0f);

                if (_attackHitBox.offset.x != 0f || _attackHitBox.offset.y != 0f)
                {
                    _attackHitBox.offset = Vector2.zero;
                }

                // ⚠️ **跟着人物一起缩**。
                //
                // 判定盒的偏移和尺寸是场景里手摆的世界单位（80,**111** / 112.5×125），
                // 而青禾的立绘和身体碰撞体会按 bodyScale 缩。两边不同步的话，
                // 人物缩小了、判定盒还停在原地 —— 表现是「看着砍到了却打不中」，
                // 而且**肉眼完全看不出来**（判定盒不可见）。
                //
                // 乘的是「相对量数值那版缩放的比例」，见 QingheController.MeasuredAtBodyScale。
                //
                // ⚠️ **y 必须和 ChestHeight 的比例对得上**（那边现在是身高的 0.70）。
                // 两者是两套数：一个是场景手摆的、一个是按身高算的。不一致的症状是
                // 「墨迹从胸口飞出去、判定盒却在腰上」—— 看着打中了但没伤害。
                // 量数值那版的 0.70 对应 y = −15 + 180×0.70 = 111。
                //
                // 变更记录：60 →（2026-10-07 抬到腰以上）97 →（青禾缩到 0.4、给 Boss 留余量）111。
                float k = _qinghe != null ? _qinghe.BodyScaleRatio : 1f;

                if (!Mathf.Approximately(k, 1f))
                {
                    _attackHitBoxLocalPos *= k;
                    _attackHitBox.size *= k;
                }

                _attackHitBoxCenterReady = true;
            }

            // 碰撞体常开（trigger，不挡路），只用来维护重叠集合
            _attackHitBox.enabled = true;

            // 灰盒视觉节点（可选）
            _slashLight = transform.Find("Slash");
            if (_slashLight != null)
            {
                _slashLight.gameObject.SetActive(false);
            }

            _slashHeavy = transform.Find("SlashHeavy");
            if (_slashHeavy != null)
            {
                _slashHeavy.gameObject.SetActive(false);
            }

            _shieldNode = transform.Find("Shield");
            if (_shieldNode != null)
            {
                _shieldNode.gameObject.SetActive(false);
                FitShield();
            }

            EventBus.On<CompanionFormChangedEvent>(GameEvents.CompanionFormChanged, OnFormChanged);

            // 预加载灰盒特效素材（Cocos 侧从 resources 加载，Unity 侧对应 Resources 目录）
            _mistShotSprite = Resources.Load<Sprite>("mist_shot");
            _hitSparkSprite = Resources.Load<Sprite>("hit_spark");
        }

        private void OnDestroy()
        {
            EventBus.Off<CompanionFormChangedEvent>(GameEvents.CompanionFormChanged, OnFormChanged);
        }

        // ============================================================ 主循环

        /// <summary>每帧由 <see cref="QingheController"/> 驱动（先于形态移动，避免门控延迟一帧）。</summary>
        public void Tick(float dt, FormContext ctx)
        {
            // ⚠️ Awake 里可能因为缺 AttackHitBox 提前 `enabled = false; return;`
            // —— 那样**订阅形态变化的那一句也没跑到**，而 QingheController 是
            // **直接调** Tick 的（`enabled = false` 挡不住外部直调）。
            // 于是 _currentForm 会永远停在初始值 Entity：灵合形态按 J 会走成近战
            // （ResolveAttackRelease），表现是「灵合按攻击键完全没反应」。
            // 这里直接挡掉，宁可什么都不做，也不要带着错的形态状态跑。
            if (_attackHitBox == null)
            {
                return;
            }

            _ctx = ctx;

            // ⚠️ **按键边沿必须在这里、早于下面任何计时积分处理。**
            //
            // Cocos 那边原先是在事件回调里直接改状态（发生在两帧之间）；
            // 若把边沿拖到 chargeTime 积分与 UpdateAttackTiming 之后处理，会有两处偏移：
            //   1) 松手时 chargeTime 比原来多算一个 dt —— lightTapThreshold 只有 0.2s，
            //      阈值漂移约 8%，快速点按更容易被误判成蓄力重击；
            //   2) BeginAttack() 被推迟 —— attackTimer 当帧不再推进，每段攻击都固定多一帧前摇。
            //      attackStartup = 0.1s，等于 +16.7%，连段节奏肉眼可感地变慢。
            //
            // ⚠️ _attackHeld / _blockHeld 必须由 WasPressed/WasReleased 这对**边沿**维护，
            // **不能写成 `= InputSystem.IsDown(...)`**：极快的点按可能「按下+松开」都发生在
            // 同一帧间隙里，采样到的 IsDown 会是 false，这次点按就被整个吞掉了。
            if (InputSystem.WasPressed(GameAction.Attack))
            {
                _attackHeld = true;
                _chargeTime = 0f;
                TryFormAttack();
            }

            // ⚠️ **换形态的宽限**：一进新形态就检查一次「攻击键是否正被按住」。
            //
            // 灵雾/灵合的攻击挂在 WasPressed 的上升沿上，所以「按住 J 再切形态」
            // 不会触发任何东西 —— 玩家必须**松手再按**。而灵合的爆发窗口本来就窄
            // （净扣 15~19/s），这一下很容易就错过了，体感正是「进灵合按 J 没反应」。
            //
            // 只在新形态刚进来的这几帧生效、且只在**键确实按着**时才补发一次，
            // 所以不会造成「切形态自动放招」。
            if (_formAttackGrace > 0f)
            {
                _formAttackGrace -= dt;

                if (_attackHeld)
                {
                    _formAttackGrace = 0f;
                    TryFormAttack();
                }
            }

            if (InputSystem.WasReleased(GameAction.Attack))
            {
                _attackHeld = false;

                if (_swallowAttackRelease)
                {
                    // 这次按下已经在「按下」那一帧缓存过了，不要再分类一次 ——
                    // 按住时长此刻包含攻击期间的累加，分类结果没有意义
                    _swallowAttackRelease = false;
                }
                else if (_currentForm == CompanionForm.Entity)
                {
                    ResolveAttackRelease();
                }
            }

            if (InputSystem.WasPressed(GameAction.Block))
            {
                _blockHeld = true;
                _blockElapsed = 0f;
                GameAudio.PlaySfx(SfxCue.Block);
            }

            if (InputSystem.WasReleased(GameAction.Block))
            {
                _blockHeld = false;
            }

            // 蓄力计时（实体形态按住攻击键）
            if (_attackHeld && _currentForm == CompanionForm.Entity)
            {
                float before = _chargeTime;
                _chargeTime += dt;

                // 蓄力**过程中**的持续反馈：往身上吸的光点，越蓄越密越亮。
                // 原来这一段完全空白 —— 按住了屏幕上什么都不变，
                // 玩家不知道蓄上了没、也不知道什么时候满。
                float full = GameConfig.Current.Combat.HeavyChargeTime;
                InkFx.ChargeTick(SwitchFxPosition(), _currentForm,
                                 full > 0f ? _chargeTime / full : 1f, dt);

                // 刚满蓄的那一帧再给一圈爆开 ——「可以松手了」
                if (before < full && _chargeTime >= full)
                {
                    InkFx.ChargeReady(SwitchFxPosition(), _currentForm, BodyScaleK);
                }
            }

            // 格挡计时（用于完美格挡窗口判定）
            if (_blockHeld)
            {
                _blockElapsed += dt;
            }

            // 连击窗口计时
            if (_comboTimer > 0f)
            {
                _comboTimer -= dt;
            }

            // 攻击时序推进
            if (_attacking)
            {
                UpdateAttackTiming(dt);
            }

            // 灵合形态：净化光环持续对范围内目标造成伤害
            if (_currentForm == CompanionForm.Merge)
            {
                TickMergeAura(dt);
            }

            _blocking = _blockHeld && _currentForm == CompanionForm.Entity;

            // 灰盒视觉：护盾跟随格挡状态
            if (_shieldNode != null)
            {
                _shieldNode.gameObject.SetActive(_blocking);
            }

            // 写回上下文，供形态门控移动/跳跃/冲刺
            ctx.IsAttacking = _attacking;
            ctx.IsBlocking = _blocking;
        }

        // ============================================================ 输入

        private void ResolveAttackRelease()
        {
            bool isLight = _chargeTime < GameConfig.Current.Combat.LightTapThreshold;

            if (!isLight)
            {
                // 重击不缓存：蓄力是「有意识」的动作，攻击中按就直接丢弃
                if (!_attacking)
                {
                    DoHeavyAttack();
                }

                return;
            }

            if (_attacking)
            {
                // 攻击中再点轻击 -> 缓存下来，收招瞬间接上，连段才不会断
                _bufferedLight = true;
                return;
            }

            DoLightAttack();
        }

        // ============================================================ 轻击三连 / 蓄力重击

        private void DoLightAttack()
        {
            if (_attacking)
            {
                return;
            }

            SpiritConfig spirit = GameConfig.Current.Spirit;

            if (!SpiritEnergySystem.CanConsume(spirit.LightAttackCost))
            {
                Debug.Log("[Combat] 灵炁不足，无法轻击");
                return;
            }

            SpiritEnergySystem.Consume(spirit.LightAttackCost);

            CombatConfig combat = GameConfig.Current.Combat;

            if (_comboTimer <= 0f)
            {
                _comboIndex = 0;
            }

            _comboIndex += 1;

            if (_comboIndex > 3)
            {
                _comboIndex = 1;
            }

            _comboTimer = combat.ComboWindow;

            PlayViewClip(LightClips[_comboIndex - 1]);

            float mult = combat.ComboDamageMult[_comboIndex - 1];
            float knockback = _comboIndex == 3 ? combat.ComboKnockback : 0f;
            BeginAttack(mult, knockback);
            GameLog.V($"[Combat] 轻击第{_comboIndex}段 (x{mult})");
        }

        private void DoHeavyAttack()
        {
            if (_attacking)
            {
                return;
            }

            SpiritConfig spirit = GameConfig.Current.Spirit;

            if (!SpiritEnergySystem.CanConsume(spirit.HeavyAttackCost))
            {
                Debug.Log("[Combat] 灵炁不足，无法蓄力重击");
                return;
            }

            SpiritEnergySystem.Consume(spirit.HeavyAttackCost);

            CombatConfig combat = GameConfig.Current.Combat;
            bool isFull = _chargeTime >= combat.HeavyChargeTime;
            float mult = isFull ? combat.HeavyFullDamageMult : combat.HeavyHalfDamageMult;

            _comboIndex = 0;
            _comboTimer = 0f;
            BeginAttack(mult, combat.HeavyKnockback);
            GameLog.V($"[Combat] 蓄力重击 {(isFull ? "满蓄" : "半蓄")} (x{mult})");
        }

        private void BeginAttack(float damageMult, float knockbackDist)
        {
            _attacking = true;
            _attackingActive = false;
            _attackTimer = 0f;
            _hitSet.Clear();

            // 挥击音效挂在这里而不是按键那一帧：按键里还有「缓存连段」那条路，
            // 那条不立刻出招，挂在按键上会响得比动作早。
            GameAudio.PlaySfx(SfxCue.Slash);

            _pendingDamage = GameConfig.Current.Combat.BaseLightDamage * damageMult;
            _pendingKnockback = knockbackDist;

            PlaceAttackHitBox();

            // 灰盒视觉：以重击半蓄倍率为界，决定这一段用哪把光刃
            _isHeavyAttack = damageMult >= GameConfig.Current.Combat.HeavyHalfDamageMult;

            // 水墨粒子：攻击出墨。方向取当前朝向，重击给更大的一簇。
            //
            // 走 InkFx 而不是 Unity 的 ParticleSystem —— 后者要 Shader.Find 粒子材质，
            // 而运行时 Shader.Find 在构建里可能返回 null（见 InkFx 的说明）。
            SpawnAttackInk();

            // 光刃（slash_light / slash_heavy）**不再使用** —— 那两张是灰盒期的硬边
            // 几何图形，压在现在的水墨美术上很出戏。攻击表现已全部交给 InkFx 的墨迹。
            // 节点保留在场景里没删，只是永不显示；确认没问题后可以把它们从场景清掉。
        }

        /// <summary>在身前炸一簇墨，颜色随当前形态。</summary>
        private void SpawnAttackInk()
        {
            if (_ctx == null)
            {
                return;
            }

            float facing = _ctx.Facing >= 0f ? 1f : -1f;

            // ⚠️ 出墨点的高度**从碰撞体算**，不要写死常量。
            //
            // 角色节点的原点在**脚底**，而青禾会随 bodyScale 整体缩放 ——
            // 写死一个 y（原来是 MistShotSpawnY = 55）在人物缩小后就变成从**腰部以下**
            // 出招（实测反馈「攻击特效都是从下半身放出来的」）。
            // 用碰撞体推：碰撞体是脚底对齐的、且跟着一起缩，抬到它的 40% 处就是胸口。
            Vector3 origin = transform.position;
            Vector3 pos = new Vector3(origin.x + facing * SlashOffsetX, origin.y + ChestHeight(), origin.z);

            // 尺寸倍数只传人物缩放。重击的「更大一簇」由 InkFx 的 HeavySlash 分支自己给
            // （粒子数、内部倍率）—— 这里原先还写着 `? 1.8f : 1f`，
            // 但那个参数当时根本没被 InkFx 用上，是个**读起来像在生效的空写入**。
            //
            // ⚠️ 这里传的是**形态本身**而不是 TintFor 出来的颜色：InkFx 按形态选
            // **形状语言**（实体飞白 / 灵雾柔晕 / 灵合光环），不只是换色 —— 见那边的类注释。
            // 传颜色进去的话三种形态又会变成「同一套形状换颜色」，正是要修的问题。
            // 三段轻击里**第三段自带一套收尾**（它是唯一带击退的一段，
            // 前两段和它长得一样的话，玩家感觉不到「这套打完了」）。
            InkFx.Kind kind = _isHeavyAttack
                ? InkFx.Kind.HeavySlash
                : (_comboIndex == 3 ? InkFx.Kind.Finisher : InkFx.Kind.Slash);

            InkFx.Burst(pos, kind, _currentForm, new Vector2(facing, 0f), BodyScaleK);
        }

        /// <summary>
        /// 格挡特效落点：身前一点、胸口高度。
        ///
        /// 不用**来袭方向**是因为 <see cref="ReceivePlayerDamage"/> 收不到方向参数
        /// （敌人 AI 只传伤害数值）。用当前朝向近似 —— 玩家格挡时基本都面朝着敌人，
        /// 偏差可以接受。真要精确就得给接口加一个方向参数，那要动敌人 AI 那条链。
        /// </summary>
        private Vector3 GuardFxPosition()
        {
            float facing = _ctx != null ? _ctx.Facing : 1f;
            Vector3 o = transform.position;

            return new Vector3(o.x + facing * 34f * BodyScaleK, o.y + ChestHeight(), o.z);
        }

        /// <summary>形态切换特效落点：胸口（不偏前，换形态是「从身上」发出来的）。</summary>
        private Vector3 SwitchFxPosition()
        {
            Vector3 o = transform.position;

            return new Vector3(o.x, o.y + ChestHeight(), o.z);
        }

        /// <summary>格挡护盾的基准位置 / 缩放（场景里那组「量数值那版」的值，见 <see cref="FitShield"/>）</summary>
        private Vector3 _shieldBasePos;
        private float _shieldBaseScale = 1f;

        /// <summary>
        /// 摆格挡护盾：**跟着人物一起缩，并抬到身体中线**。
        ///
        /// ⚠️ 护盾节点原来是 <c>localPosition = (0,0)</c>、<c>scale = 1</c> ——
        /// 也就是挂在**节点原点（脚底附近）**、尺寸按贴图原生大小。
        /// 青禾缩到 0.4 倍（身高 96）之后，那张 92×80 的贴图只罩得住
        /// 「脚到膝盖」那一截，躯干和头全在外面（实测反馈「罩不住」）。
        ///
        /// 所以两件事一起做：抬到身体中线、按整体比例放大。两者都乘
        /// <see cref="BodyScaleK"/>，于是以后再调 bodyScale 会自动跟 ——
        /// 场景里存的只是「量数值那一版（bodyScale 0.75）」的基准。
        ///
        /// 层级也从场景的 <see cref="SortingOrders.Fx"/>(60) 压到 59：
        /// 和格挡命中时生成的光环粒子同层会让两者的前后关系不稳定（会闪），
        /// 压一格之后「光环盖在盾上」是确定的。仍然在青禾(30)之前。
        /// </summary>
        private void FitShield()
        {
            if (_shieldNode == null)
            {
                return;
            }

            _shieldBasePos = _shieldNode.localPosition;
            _shieldBaseScale = _shieldNode.localScale.x;

            float k = BodyScaleK;

            _shieldNode.localPosition = _shieldBasePos * k;
            _shieldNode.localScale = new Vector3(_shieldBaseScale * k, _shieldBaseScale * k, 1f);

            SpriteRenderer sr = _shieldNode.GetComponent<SpriteRenderer>();

            if (sr != null)
            {
                sr.sortingOrder = SortingOrders.Fx - 1;
            }
        }

        /// <summary>
        /// 出招高度（相对脚底）：墨迹 / 灵弹都从这里出。
        ///
        /// ⚠️ **要用人物实际身高推，不能用碰撞体。**
        ///
        /// 第一版按碰撞体算，结果特效还是从下半身出 —— 因为**碰撞体是刻意小于立绘的**
        /// （碰撞体只占人物 62%），按它算出来的「胸口」实际落在小腿上
        /// （实测：算出来只有脚底往上 30，真正的胸口在 ~97）。
        ///
        /// 脚底基准取碰撞体底边（立绘就是按它对齐的）。
        ///
        /// <b>比例为什么是 0.70 而不是「胸口的 0.62」</b>
        ///
        /// 2026-10-07 青禾缩到 <c>bodyScale = 0.4</c>（身高 96）后实测发现：
        /// 按 0.62 算，出膛点在 −235.5，而 **Boss 的受击盒底边在 −240**
        /// —— 只探进去 **13.5** 单位。人物越小、出招点越低，对 Boss 的余量就越薄，
        /// 而「打不到敌人」正是这次缩小的原因，所以往上抬到 0.70（上胸/肩）：
        /// 出膛点 −227.8，余量 **21.2**；判定盒对 Boss 的重叠也从 31 涨到 39。
        /// 腰在身高的 0.5 处，0.70 仍然稳稳在腰以上。
        ///
        /// ⚠️ 改这个数要**同时改场景里 <c>AttackHitBox</c> 的 y**（见那里的说明）——
        /// 判定盒和出招点是两套数，不一致就会出现「墨迹从胸口出去、判定盒在腰上」。
        /// </summary>
        private float ChestHeight()
        {
            BoxCollider2D body = GetComponent<BoxCollider2D>();
            float feetY = body != null ? body.offset.y - body.size.y * 0.5f : 0f;

            if (_qinghe != null && _qinghe.FigureHeight > 1f)
            {
                // ⚠️ 比例读 QingheController.ChestRatio，**不要在这里写 0.70** ——
                // 那是「只有一份」的定义，判定盒高度（场景里 AttackHitBox 的 y）
                // 也是按它推的，两处不一致就会出现「墨迹出招了但判定盒在别处」。
                return feetY + _qinghe.FigureHeight * QingheController.ChestRatio;
            }

            return feetY + MistShotSpawnY;   // 兜底：拿不到身高就用配置值
        }

        private void UpdateAttackTiming(float dt)
        {
            _attackTimer += dt;
            float total = attackStartup + attackActiveDuration + attackRecovery;

            if (_attackTimer >= total)
            {
                _attacking = false;
                _attackingActive = false;
                _pendingDamage = 0f;

                // 连击窗口从**收招**开始算，而不是起手 ——
                // 一次攻击总共 0.4s，若从起手算，窗口 0.3s 会在攻击结束前就过期，
                // 连段永远接不上（表现为「怎么按都只有第一段」）。
                _comboTimer = GameConfig.Current.Combat.ComboWindow;

                if (_bufferedLight)
                {
                    _bufferedLight = false;
                    DoLightAttack();
                }
            }
            else if (_attackTimer >= attackStartup + attackActiveDuration)
            {
                _attackingActive = false;
            }
            else if (_attackTimer >= attackStartup)
            {
                _attackingActive = true;
                PlaceAttackHitBox();

                // 判定期每帧扫描一次重叠集合 —— 光靠进入事件会漏掉
                // 「攻击开始时敌人就已经在框内」的情况
                SweepOverlapping();
            }

            // 光刃已停用（见 BeginAttack 的说明）；攻击的可见表现是墨迹
        }

        /// <summary>把命中盒摆到玩家朝向的前方（判定期每帧重摆）。</summary>
        private void PlaceAttackHitBox()
        {
            float facing = _ctx != null ? _ctx.Facing : 1f;

            _attackHitBox.transform.localPosition = new Vector3(
                _attackHitBoxLocalPos.x * facing,
                _attackHitBoxLocalPos.y,
                0f);

            // 光刃摆到角色身前，并随朝向做水平镜像
            PlaceSlash(_slashLight, SlashOffsetX, facing);
            PlaceSlash(_slashHeavy, SlashHeavyOffsetX, facing);
        }

        /// <summary>
        /// 把光刃放到朝向前方，并按朝向水平镜像。
        /// 素材只画了「朝右」那半弧，朝左时若只挪位置不翻转，弧口会背对攻击方向、看着是歪的。
        /// </summary>
        private void PlaceSlash(Transform node, float offsetX, float facing)
        {
            if (node == null)
            {
                return;
            }

            Vector3 p = node.localPosition;
            node.localPosition = new Vector3(offsetX * facing, p.y, 0f);
            FacingFlip.Apply(node, facing, SlashNaturalFacing);
        }

        // ============================================================ 格挡 / 完美格挡

        /// <summary>
        /// 敌人命中玩家时调用。
        /// 完美格挡 → SpiritCost 为 0 且 PerfectBlocked = true；普通格挡 → 减伤后的量；未格挡 → 原值。
        ///
        /// 反制（把攻击者弹开 / 打入硬直）不在这里做 —— 这里拿不到攻击者是谁，
        /// 由调用方（敌人 AI）读 PerfectBlocked 自行执行。
        /// </summary>
        public PlayerDamageResult ReceivePlayerDamage(float amount)
        {
            CombatConfig combat = GameConfig.Current.Combat;

            if (_blocking)
            {
                bool isPerfect = _blockElapsed <= combat.PerfectBlockWindow;

                // ⚠️ 格挡特效在**受击这一刻**生成，不在按格挡键那一刻。
                // 按键时还不知道会不会被打、也不知道从哪来；环落在「被打到的地方」
                // 才读得出来是「这一下挡住了」。位置取身前一点（朝向来袭方向无从得知，
                // 用当前朝向近似）。
                Vector3 guardAt = GuardFxPosition();

                if (isPerfect)
                {
                    Debug.Log("[Combat] 完美格挡！");
                    InkFx.Burst(guardAt, InkFx.Kind.PerfectBlock, CompanionForm.Entity,
                                new Vector2(_ctx != null ? _ctx.Facing : 1f, 0f), BodyScaleK);
                    return new PlayerDamageResult { SpiritCost = 0f, PerfectBlocked = true };
                }

                SpiritEnergySystem.Consume(GameConfig.Current.Spirit.BlockCost);
                float reduced = amount * (1f - combat.BlockReduction);
                InkFx.Burst(guardAt, InkFx.Kind.Block, CompanionForm.Entity,
                            new Vector2(_ctx != null ? _ctx.Facing : 1f, 0f), BodyScaleK);
                Debug.Log($"[Combat] 格挡减伤 {amount} -> {reduced}");
                return new PlayerDamageResult { SpiritCost = reduced, PerfectBlocked = false };
            }

            return new PlayerDamageResult { SpiritCost = amount, PerfectBlocked = false };
        }

        // ============================================================ 灵合形态

        /// <summary>
        /// 净化光环：灵合形态下**持续**对范围内可受击目标造成伤害。
        ///
        /// 按 <see cref="AuraTick"/> 一跳结算，不是每帧 —— 每帧结算会把日志刷爆，
        /// 而且 <see cref="DamageInfo"/> 每帧构造一次纯属浪费。
        /// </summary>
        private void TickMergeAura(float dt)
        {
            RefreshDamageables(dt);
            _auraAccum += dt;

            if (_auraAccum < AuraTick)
            {
                return;
            }

            float step = _auraAccum;
            _auraAccum = 0f;
            SpiritConfig spirit = GameConfig.Current.Spirit;
            DamageNearby(spirit.MergePurifyDamagePerSec * step, spirit.MergePurifyRadius);
        }

        /// <summary>灵炁爆发：一次性 AOE。**每次灵合只能用一次**（进入灵合时重置）。</summary>
        private void FireMergeBurst()
        {
            if (_burstUsed)
            {
                Debug.Log("[Combat] 本次灵合的灵炁爆发已经用过了");

                // 玩家侧要看得见 —— 「每次灵合限一次」是设计，但原来只有日志知道
                EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = "灵炁爆发每次灵合只能用一次" });
                return;
            }

            SpiritConfig spirit = GameConfig.Current.Spirit;

            if (!SpiritEnergySystem.CanConsume(spirit.MergeBurstCost))
            {
                Debug.Log("[Combat] 灵炁不足，无法灵炁爆发");
                EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = $"灵炁不足 {spirit.MergeBurstCost:F0}，无法灵炁爆发" });
                return;
            }

            SpiritEnergySystem.Consume(spirit.MergeBurstCost);
            _burstUsed = true;

            // ⚠️ 音效放在**两道闸之后**：爆不出来就不该有爆炸声。
            // 原来它在这段的最顶上，于是「本次已经用过」和「灵炁不足」两种情况
            // 都会照样响一声 —— 玩家听到爆发音却什么也没发生，只会以为是自己按错了。
            // 与 EntityForm 的踏云闪同一条约定（那边注释里也写了「灵炁不足时上面就 return 掉了」）。
            GameAudio.PlaySfx(SfxCue.Spirit);

            // 爆发特效：**补上去的** —— 原来这一招完全没有视觉，
            // 一个 340 半径的 AOE 只是让敌人「莫名掉血」，是灵合最看不出来的地方。
            // 不吃人物缩放（伤害半径是固定世界单位，见 InkFx.MergeBurst 的说明）。
            InkFx.Burst(new Vector3(transform.position.x, transform.position.y + ChestHeight(), 0f),
                        InkFx.Kind.MergeBurst, CompanionForm.Merge, Vector2.zero);

            RefreshDamageables(99f);          // 强制重扫一次，保证打的是当下的目标
            int hit = DamageNearby(spirit.MergeBurstDamage, spirit.MergeBurstRadius);
            Debug.Log($"[Combat] 灵炁爆发！命中 {hit} 个目标");
        }

        /// <summary>
        /// 对半径内的可受击目标造成伤害。
        ///
        /// 走 <see cref="Damageable.TakeDamage"/>（和玩家普攻同一条链路），
        /// 所以敌人的受击/死亡表现都自动生效。
        /// </summary>
        private int DamageNearby(float amount, float radius)
        {
            if (amount <= 0f)
            {
                return 0;
            }

            Vector3 p = transform.position;
            int hit = 0;

            foreach (Damageable d in _damageables)
            {
                if (d == null || d.IsDead())
                {
                    continue;
                }

                Vector3 wp = d.transform.position;
                float dx = wp.x - p.x;
                float dy = wp.y - p.y;

                if (dx * dx + dy * dy > radius * radius)
                {
                    continue;
                }

                d.TakeDamage(new DamageInfo
                {
                    Amount = amount,
                    Knockback = Vector2.zero,
                    SourceForm = CompanionForm.Merge,
                });

                hit += 1;
            }

            return hit;
        }

        /// <summary>
        /// 缓存场景里的可受击目标。
        ///
        /// 每帧全场景遍历太贵，所以按 <see cref="ScanInterval"/> 扫一次。
        /// <paramref name="forceDt"/> 传大值时立刻重扫（灵炁爆发前要保证打的是当下的目标）。
        /// </summary>
        private void RefreshDamageables(float forceDt)
        {
            _scanTimer -= forceDt;

            if (_scanTimer > 0f && _damageables.Length > 0)
            {
                return;
            }

            _scanTimer = ScanInterval;

            Damageable[] found = Object.FindObjectsByType<Damageable>(FindObjectsInactive.Exclude);
            List<Damageable> outList = new List<Damageable>(found.Length);

            foreach (Damageable d in found)
            {
                // 排除自己（青禾身上也挂了 Damageable）
                if (d != null && d.gameObject != gameObject)
                {
                    outList.Add(d);
                }
            }

            _damageables = outList.ToArray();
        }

        // ============================================================ 灵雾灵弹

        private void FireMistShot()
        {
            SpiritConfig spirit = GameConfig.Current.Spirit;

            // ⚠️ **泡在灵泉里不能发灵弹** —— 这是**平衡约束**，不是手感调整。
            //
            // 灵泉每帧回灵炁（恢复到 SpringRestoreCapRatio 上限），而灵弹正吃着灵炁。
            // 两者叠在一起，泉里的灵雾形态就成了**移动炮台**：消耗被回复抵消掉，
            // 玩家可以站在安全区里无限放风筝。灵泉的设计意图是「停下来回血」，
            // 那就得让「回」和「打」二选一 —— 所以这里把发射这条掐掉。
            //
            // 放在**扣灵炁之前**：被拦下的这一发不该扣钱，与「灵炁不足」那条同序
            // （都是先查后扣）。也不播音效 —— 没打出去就不该有出膛声。
            if (SpiritEnergySystem.IsInSpring())
            {
                Debug.Log("[Combat] 灵泉范围内无法发射灵弹");
                EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = "灵泉范围内无法发射灵弹" });
                return;
            }

            if (!SpiritEnergySystem.CanConsume(spirit.MistShotCost))
            {
                Debug.Log("[Combat] 灵炁不足，无法灵弹");
                return;
            }

            SpiritEnergySystem.Consume(spirit.MistShotCost);

            CombatConfig combat = GameConfig.Current.Combat;
            float facing = _ctx != null ? _ctx.Facing : 1f;

            // 传大 forceDt 强制重扫一次，保证打的是当下的目标（灵弹靠这份列表做重叠判定）
            RefreshDamageables(1e9f);

            SpawnMistShot(
                new Vector2(facing, 0f),
                combat.MistShotSpeed,
                combat.BaseLightDamage * combat.MistShotDamageRatio,
                combat.MistShotLifetime,
                _damageables);

            // 发射的墨团：让灵弹「从手里甩出去」这件事在画面上有交代
            if (_ctx != null)
            {
                Vector3 o = transform.position;
                InkFx.Burst(new Vector3(o.x + facing * MistShotSpawnX, o.y + ChestHeight(), o.z),
                            InkFx.Kind.Mist, CompanionForm.Mist, Vector2.zero, BodyScaleK);
            }

            GameLog.V("[Combat] 灵弹发射");
        }

        private void SpawnMistShot(Vector2 dir, float speed, float damage, float lifetime,
                                   Damageable[] targets)
        {
            // ⚠️ **不要因为 parent 为 null 就整个跳过。**
            //
            // 青禾是**场景根节点**（`transform.parent == null`）。原来这里直接 return，
            // 于是**灵弹从来没有被生成过** —— 但代码读起来是通的、也不报错，
            // 症状只有「灵弹没有」，排查时完全指不到这一行。
            // `SetParent(null)` 的语义本来就是「放到场景根」，是合法的。
            Transform parent = transform.parent;

            GameObject shotNode = BuildFallbackShotNode();
            shotNode.transform.SetParent(parent, true);

            // 角色节点的原点是脚底（碰撞体底边），直接拿它当发射点灵弹会从脚底冒出来。
            //
            // ⚠️ 高度用 <see cref="ChestHeight"/>，**不要用 MistShotSpawnY 那个常量**：
            // 出膛的墨迹（上面那段 InkFx）走的就是 ChestHeight，两边各用一个高度的话，
            // 灵弹会从墨迹下方一小截窜出来，人物一缩放这一截还会变。
            Vector3 origin = transform.position;
            float dirSign = dir.x >= 0f ? 1f : -1f;
            shotNode.transform.position = new Vector3(
                origin.x + dirSign * MistShotSpawnX,
                origin.y + ChestHeight(),
                origin.z);

            MistShot shot = shotNode.GetComponent<MistShot>();
            shot.Init(dir, speed, damage, lifetime, targets);
        }

        /// <summary>灰盒兜底：程序化生成灵弹（精灵 + 刚体 + 触发器 + MistShot）。</summary>
        private GameObject BuildFallbackShotNode()
        {
            GameObject node = new GameObject("MistShot");
            node.layer = PhysicsGroups.HitBox;

            float shotScale = 1f;

            if (_mistShotSprite != null)
            {
                SpriteRenderer sr = node.AddComponent<SpriteRenderer>();
                sr.sprite = _mistShotSprite;
                sr.drawMode = SpriteDrawMode.Simple;

                // ⚠️ **必须显式设绘制层级。**
                //
                // 新建的 SpriteRenderer 的 sortingOrder 默认是 0，而小怪在 10/11、
                // 青禾在 30 —— 灵弹会画在它们**后面**，飞向敌人时几乎看不见
                // （实测反馈「灵弹目前没有」，代码其实是通的，是被盖住了）。
                // `MinionArrow` 是同一个坑，见那里的说明。
                sr.sortingOrder = SortingOrders.Fx;

                // 贴图按「1 像素 = 1 世界单位」导入，直接用就是 40×40；
                // 按设计尺寸 MistShotSize 反算缩放 —— 与 HitSpark 同一套做法。
                // （这个常量原来声明了却从没被引用，所以灵弹一直是原生尺寸。）
                Vector2 spriteSize = _mistShotSprite.bounds.size;

                if (spriteSize.x > 0f)
                {
                    shotScale = MistShotSize / spriteSize.x;
                    node.transform.localScale = Vector3.one * shotScale;
                }
            }

            Rigidbody2D rb = node.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.linearDamping = 0f;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.freezeRotation = true;
            rb.bodyType = RigidbodyType2D.Kinematic;   // 抛射物靠代码写位置/速度，不受物理力

            BoxCollider2D col = node.AddComponent<BoxCollider2D>();

            // 节点带了缩放（见上面的 shotScale），碰撞体的 size 是**本地单位**，
            // 会被一起缩放 —— 所以要除回去，否则物理体比设计值小一截。
            float bodySize = shotScale > 0f ? MistShotBody / shotScale : MistShotBody;
            col.size = new Vector2(bodySize, bodySize);

            // ⚠️ 配法照抄命中盒与敌人的箭：
            //   **isTrigger** —— 只报触发，不产生任何物理推力。
            //   非 trigger 时灵弹会和敌人的 Dynamic 刚体做真物理碰撞，把敌人顶开
            //   （Cocos 那边 `MinionArrow` 当年就因为「非 sensor」被反馈
            //     「不是射在我身上，是把我撞飞了」）。
            //   **层 = HITBOX** —— 碰撞矩阵里 HITBOX 只和 ENEMY 碰：
            //   打得中敌人、穿得过地形、也不会和 PLAYER（自己人）发生任何接触。
            col.isTrigger = true;

            node.AddComponent<MistShot>();
            return node;
        }

        /// <summary>命中火花：短命特效节点，自行淡出自毁。</summary>
        private void SpawnHitSpark(Vector3 worldPos)
        {
            if (_hitSparkSprite == null)
            {
                return;
            }

            // 同 SpawnMistShot：青禾没有父节点，**不能因此跳过**。
            // 这个早退是「打中敌人没有任何反馈」的真正原因。
            Transform parent = transform.parent;

            GameObject n = new GameObject("HitSpark");
            n.transform.SetParent(parent, true);
            n.transform.position = worldPos;

            SpriteRenderer sr = n.AddComponent<SpriteRenderer>();
            sr.sprite = _hitSparkSprite;

            // ⚠️ 同 SpawnMistShot：默认 sortingOrder = 0 会画在小怪(10)、青禾(30) **后面**，
            //    火花等于白生成。
            sr.sortingOrder = SortingOrders.Fx;

            // 让火花的显示尺寸对齐设计值（按精灵自身尺寸反算缩放）。
            //
            // ⚠️ **不要再乘 100。** 那个 `* 100f` 是从 Cocos 搬过来的残留 ——
            // Cocos 那边精灵尺寸按「100 像素 = 1 单位」算，所以要乘回去；
            // 而本项目 `CloudEyrieTextureImporter` 把 Resources 下的图**一律按
            // pixelsPerUnit = 1** 导入（见那个文件的推导），`bounds.size` 已经是世界单位了。
            //
            // 多乘 100 的后果：64×64 的图被放大 87.5 倍 = 5600 世界单位，是屏幕宽度的 4.4 倍，
            // 每次命中**整个屏幕闪白 0.18 秒**（实测反馈「攻击的时候闪白一下有点怪」）。
            // 以前没暴露是因为命中火花压根没被生成过（那个 null-parent 早退）。
            Vector2 spriteSize = sr.sprite.bounds.size;

            if (spriteSize.x > 0f)
            {
                n.transform.localScale = Vector3.one * (HitSparkSize / spriteSize.x);
            }

            HitSpark spark = n.AddComponent<HitSpark>();
            spark.SetScaleMult(_isHeavyAttack ? 1.5f : 1.0f);
        }

        // ============================================================ 命中结算

        /// <summary>
        /// 判定期每帧调用：把「已经在框里」的目标也结算掉。
        ///
        /// 命中盒的碰撞体**常开**（trigger，不挡任何东西），只用它维护「当前和谁重叠」——
        /// 所以敌人走进框内即使不在攻击判定期，集合也是准的（见 <see cref="AttackHitBox"/>）。
        /// </summary>
        private void SweepOverlapping()
        {
            if (_hitBoxForwarder == null)
            {
                return;
            }

            // ⚠️ 先拷再遍历：`TryDamage` 打死敌人会触发 OnTriggerExit2D、改掉源集合，
            // 直接遍历会抛 `Collection was modified`
            _hitBoxForwarder.CopyOverlapping(_overlapBuffer);

            foreach (Damageable d in _overlapBuffer)
            {
                if (d != null)
                {
                    TryDamage(d, d.transform);
                }
            }
        }

        /// <summary>单次结算入口：只在判定期、且本次攻击还没打过这个目标时才生效。</summary>
        private void TryDamage(Damageable damageable, Transform hitNode)
        {
            if (!_attackingActive || _pendingDamage <= 0f)
            {
                return;
            }

            if (_hitSet.Contains(damageable))
            {
                return;
            }

            _hitSet.Add(damageable);

            float facing = _ctx != null ? _ctx.Facing : 1f;

            damageable.TakeDamage(new DamageInfo
            {
                Amount = _pendingDamage,
                Knockback = new Vector2(facing * _pendingKnockback, 0f),
                SourceForm = CompanionForm.Entity,
            });

            SpawnHitSpark(hitNode.position);
            GameLog.V($"[Combat] 命中 {hitNode.name}");
        }

        // ============================================================ 形态切换

        private void OnFormChanged(CompanionFormChangedEvent e)
        {
            _currentForm = e.Current;

            // 每次重新进入灵合，灵炁爆发重置（设计：每次灵合只能用一次）。
            //
            // ⚠️ 判据是「**当前是灵合**」而不是「从非灵合切进灵合」——
            // 强制切换（Boss P3 的「强制灵合」）在玩家**已经在灵合**时也要复位，
            // 那种情况下 Previous == Current == Merge。见
            // CompanionStateMachine.ApplySwitch 里对「重申」的说明。
            if (e.Current == CompanionForm.Merge)
            {
                _burstUsed = false;
                _auraAccum = 0f;
            }

            // 换形态会**取消进行中的攻击**，那两个「待处理的松手」标记也必须清 ——
            // 否则切完之后的第一下平砍会在松手那一帧被吞掉（表现：换形态后第一刀没反应）。
            //
            // ⚠️ 但 `_attackHeld` **不能清**：它表示的「键现在还按着」要跨形态延续 ——
            // 下面的宽限恰恰靠它判断「玩家是不是一直按着攻击键切过来的」。
            _bufferedLight = false;
            _swallowAttackRelease = false;

            // 给新形态一个补发窗口：按着 J 切过来时，新形态的攻击应当立刻响应。
            // 见 Tick 里那段说明。
            _formAttackGrace = FormAttackGraceSeconds;

            // 换形态给一圈光：三种形态的立绘目前只有染色区分（灰盒手段），
            // 快动作里几乎看不出来换了 —— 补一个「炸开一圈」的瞬时反馈，
            // 形状与颜色都跟着新形态走（见 InkFx 的形状语言）。
            InkFx.Burst(SwitchFxPosition(), InkFx.Kind.FormSwitch, e.Current, Vector2.zero, BodyScaleK);

            CancelAttack();
            _attackHeld = false;
            _blockHeld = false;
            _blocking = false;
            _chargeTime = 0f;

            if (_ctx != null)
            {
                _ctx.IsAttacking = false;
                _ctx.IsBlocking = false;
            }
        }

        private void CancelAttack()
        {
            _attacking = false;
            _attackingActive = false;
            _pendingDamage = 0f;
            _bufferedLight = false;

            // 命中盒保持常开，不在这里关（见 SweepOverlapping 的注释）
            if (_slashLight != null)
            {
                _slashLight.gameObject.SetActive(false);
            }

            if (_slashHeavy != null)
            {
                _slashHeavy.gameObject.SetActive(false);
            }
        }

        // ---- 调试 / 测试入口 ----

        public bool IsBlocking
        {
            get { return _blocking; }
        }

        public bool IsAttacking
        {
            get { return _attacking; }
        }
    }
}
