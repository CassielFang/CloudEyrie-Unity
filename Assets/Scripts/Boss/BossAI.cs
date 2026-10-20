using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>Boss 阶段。名字用设计文档里的正式叫法（幻影 / 狂乱 / 归心）。</summary>
    public enum BossPhase
    {
        /// <summary>P1 幻影（HP 100%→70%）</summary>
        P1,

        /// <summary>P1→P2 全屏转场</summary>
        TransitionP2,

        /// <summary>P2 狂乱（70%→35%）：浊湮领域 + 狐火 + 尾焰弹幕</summary>
        P2,

        /// <summary>P2→P3 全屏转场（莹翳强制灵合）</summary>
        TransitionP3,

        /// <summary>P3 归心（35%→0%）：灵合净化</summary>
        P3,

        /// <summary>哀鸣：HP ≤10%，停止攻击，等着被净化</summary>
        Lament,

        /// <summary>已净化（播最终净化动画 → 结局过场）</summary>
        Defeated,
    }

    /// <summary>
    /// 九尾灵狐·青丘遗灵 —— Boss 主控。
    /// 对应 Cocos 侧 <c>boss/BossP1AI.ts</c>（Unity 侧已扩成三阶段，故改名 <c>BossAI</c>）。
    ///
    /// <b>设计上做过的一处大改（2026-10-07，团队拍板）</b>
    ///
    /// 原设计 P1 是「分裂 3 身 → 灵雾识破真身 → 破防输出」，代码里也实现过
    /// （<c>BossClone</c> + <c>identifyEnabled</c>）。**现已整体删除**，原因：
    /// 美术给的 <c>九尾狐-幻影</c> 那段动画**本身就画着三只狐狸**，幻影是"演"出来的，
    /// 不需要玩法层再叠一层假身。叠上去反而和画面打架 ——
    /// 玩家看到三只，其中两只却打不中，读起来像 bug 而不是机制。
    ///
    /// <b>那九尾狐靠什么出招？</b>
    ///
    /// 美术只给了三段**形态**动画，没有挥击/甩尾的动作帧，所以本体的任何"出招"都看不出来。
    /// 因此它的攻击**全部由召唤物与领域表达**：
    /// <list type="bullet">
    ///   <item><b>狐火</b>（<see cref="FogFire"/>）—— 主要手段，从尾部浊焰弹出、追人、可被实体形态清掉</item>
    ///   <item><b>浊湮领域</b>（<see cref="CorruptionField"/>）—— P2 铺满战场，逼玩家浮空</item>
    /// </list>
    ///
    /// <b>阶段推进</b>：只看血量比例，由 <see cref="BossConfig"/> 的三个阈值驱动，
    /// 每次转场播一段全屏动画（<see cref="StageTransition"/>），转场期间免伤。
    ///
    /// 契约：同节点要有 <see cref="Rigidbody2D"/> + <see cref="Damageable"/>，
    /// 以及一个 Art 子节点（带 <see cref="SpriteRenderer"/>）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Damageable))]
    public class BossAI : MonoBehaviour
    {
        // ============================================================ Inspector

        [Header("立绘")]
        [Tooltip("立绘节点名")]
        public string artName = "Art";

        /// <summary>
        /// 立绘缩放。
        ///
        /// ⚠️ 它乘的是**打包后**的帧尺寸，不是原始视频分辨率。
        /// <c>pack_atlas.py</c> 的 <c>--scale</c> 一改，这里就得跟着成比例改：
        /// 倍率 1.0 时帧是 768²、取 0.6；倍率 0.5 时帧是 384²，同样的大小要填 <b>1.2</b>。
        /// </summary>
        [Tooltip("乘以打包后的帧尺寸；改动 pack_atlas 的 --scale 后要跟着调")]
        public float artScale = 1f;

        // ---- 三形态体型归一 ----
        //
        // 三段形态动画的画布都是 384×384，但**内容尺寸差很多**。实测（帧间中位包围盒）：
        //   幻影 284×284（对角 402）｜ 狂乱 174×309（355）｜ 归心 372×216（430）
        // 同一个 artScale 下显示尺寸会在 251×442 ~ 536×312 之间跳，换阶段时 Boss 忽大忽小。
        // 下面这组倍率按**包围盒对角线**归一到幻影的尺寸。
        //
        // 而且内容在画布里的**位置**也不一样（内容中心相对画布中心：+1 / +12 / +45）——
        // 归心比幻影低 44 画布像素，归一后约 68 世界单位。不补的话换形态时整个 Boss 会下坠。
        // 所以再给一组 y 偏移，把**脚底**对齐到同一水平线（P1/P3 本来就齐，只有 P2 差）。
        //
        // ⚠️ 这几个数是**量出来的**，不是估的。美术换素材后要重新量（跑一次 alpha 包围盒统计）。

        // ⚠️ 下面这六个数的**真值在场景里**（Main.unity 的 Boss 节点上），
        // 这里的默认值只是跟着抄一份，免得「改了代码没生效」时以为是编译问题。
        // 两边不一样时以场景为准 —— MonoBehaviour 的序列化字段在场景里存着，
        // 改 C# 默认值对已存场景**没有任何影响**（改完记得同步场景，或者在这里改完
        // 把场景里的覆盖删掉）。project 里已经因为这条踩过（ScriptableObject 那次的翻版）。

        [Header("三形态体型归一")]
        [Tooltip("P1 幻影的体型倍率（乘在 artScale 上）")]
        public float formScaleP1 = 1f;

        [Tooltip("P2 狂乱的体型倍率")]
        public float formScaleP2 = 1.3f;

        [Tooltip("P3 归心的体型倍率。2026-10-07 按反馈在 1.45 基础上放大 1.2 倍 → 1.74")]
        public float formScaleP3 = 1.74f;

        [Tooltip("P1 立绘的 y 偏移（世界单位），用来对齐脚底")]
        public float formOffsetYP1 = 0f;

        [Tooltip("P2 立绘的 y 偏移。越小越往下")]
        public float formOffsetYP2 = 82f;

        [Tooltip("P3 立绘的 y 偏移。越小越往下。" +
                 "⚠️ 它和体型倍率是**一对**：内容底边在画布中心下方 152px，放大后底边会往下跑，" +
                 "这里不跟着加回去 Boss 就会陷进地里（1.45→1.74 时 114 → 177.5）。" +
                 "2026-10-07 按反馈整体再下移 25 → 152.5")]
        public float formOffsetYP3 = 152.5f;

        [Header("形态动画")]
        [Tooltip("P1 幻影（原名 idleAnimation，场景里已存着值，别改名）")]
        public string idleAnimation = "九尾狐-幻影";

        [Tooltip("P2 狂乱")]
        public string p2Animation = "九尾狐-狂乱";

        [Tooltip("P3 归心")]
        public string p3Animation = "九尾狐-归心";

        [Tooltip("P1→P2 全屏转场")]
        public string transitionAnimation = "九尾狐-幻影狂乱衔接";

        [Tooltip("P2→P3 全屏转场")]
        public string transitionP3Animation = "九尾狐-狂乱归心衔接";

        [Tooltip("被击败后的净化过场")]
        public string purifyAnimation = "九尾狐-最终净化";

        [Header("流程")]
        [Tooltip("开局自动开打。关掉时由 LevelDirector 在玩家踏进战场时调 Begin()")]
        public bool autoStart = true;

        // ============================================================ 状态

        /// <summary>
        /// 狐火从尾部弹出的位置：相对 Boss 节点的偏移。
        ///
        /// 九条尾巴摊开后横跨约 500 单位，所以左右各留一段散布；
        /// y 往下 120 是「尾部浊焰」的高度（Boss 节点在 y=-80，地面在 -295）。
        /// </summary>
        private static float FogFireSpawnSpreadX { get { return GameConfig.Current.Boss.FogFireSpawnSpreadX; } }
        private static float FogFireSpawnDy { get { return GameConfig.Current.Boss.FogFireSpawnDy; } }

        /// <summary>开局到第一波狐火之间的缓冲 /秒 —— 让玩家先看清场地</summary>
        private static float FirstWaveDelay { get { return GameConfig.Current.Boss.FogFireFirstWaveDelay; } }

        private BossPhase _phase = BossPhase.P1;
        private bool _started;
        private float _phaseElapsed;
        private float _waveTimer;

        private Rigidbody2D _rigidBody;
        private Damageable _damageable;
        private Transform _target;
        private CombatController _targetCombat;
        private CompanionStateMachine _targetForms;
        private StageTransition _transition;

        private Transform _artNode;
        private SequencePlayer _player;
        private SpriteRenderer _artSprite;

        /// <summary>Boss 的出生点 x，同时也就是战场中心</summary>
        private float _spawnX;

        /// <summary>狐火性格种子计数 —— 每召一只 +1，保证互不相同</summary>
        private int _fogFireSeed = 1;

        /// <summary>场上活着的狐火</summary>
        private readonly List<FogFire> _fogFires = new List<FogFire>();

        /// <summary>P2 的浊湮领域（P3 开始前收掉）</summary>
        private CorruptionField _corruption;

        // ============================================================ 生命周期

        private void Awake()
        {
            _rigidBody = GetComponent<Rigidbody2D>();

            if (_rigidBody == null)
            {
                Debug.LogError("[Boss] 同节点缺 RigidBody2D");
                enabled = false;
                return;
            }

            _damageable = GetComponent<Damageable>();

            if (_damageable == null)
            {
                Debug.LogError("[Boss] 同节点缺 Damageable");
                enabled = false;
                return;
            }

            // 刚体不允许休眠：休眠后接触评估会停摆，玩家会「打不中」
            _rigidBody.sleepMode = RigidbodySleepMode2D.NeverSleep;
            _rigidBody.WakeUp();

            // 归到 ENEMY 层：玩家砍得到它，但身体不会被它挡住
            // （碰撞矩阵里命中盒那一层只和 ENEMY 碰，见 PhysicsGroups）
            gameObject.layer = PhysicsGroups.Enemy;

            _spawnX = transform.position.x;
            _damageable.SetMaxHp(GameConfig.Current.Boss.MaxHp);

            _damageable.OnDeath = OnBossDefeated;
            _damageable.OnDamaged = (self, info) =>
                GameLog.V($"[Boss] 受击 {info.Amount}，剩余 {_damageable.GetHp():F0}");

            ResolveTarget();
            SetupArt();

            if (autoStart)
            {
                Begin();
            }
            else
            {
                // 不自动开打时，等关卡编排发话。
                // 关卡模组化之前是 LevelDirector 直接调 Begin()；
                // 现在编排是数据，数据表表达不了"调某个方法"，所以走事件。
                EventBus.On<object>(GameEvents.BossBegin, OnBossBeginRequested);
            }
        }

        private void OnBossBeginRequested(object _)
        {
            Begin();
        }

        private void OnDestroy()
        {
            EventBus.Off<object>(GameEvents.BossBegin, OnBossBeginRequested);

            if (_damageable != null)
            {
                _damageable.OnDeath = null;
                _damageable.OnDamaged = null;
            }

            // 必须退订：否则打完 Boss 之后灵炁会永久停在战斗恢复档（1/s），玩家无从察觉
            SpiritEnergySystem.SetCombatSource(this, false);
        }

        /// <summary>
        /// 开打。关卡流程里由 <see cref="LevelDirector"/> 在玩家踏进 Boss 战场时调用；
        /// 场景没接流程时把 <c>autoStart</c> 打开即可。
        /// </summary>
        public void Begin()
        {
            if (_started)
            {
                return;
            }

            _started = true;

            // Boss 战是唯一不限形态的场合：解除每关一次灵雾的限制
            QingheController qinghe = FindAnyObjectByType<QingheController>();
            CompanionStateMachine forms = qinghe != null ? qinghe.GetComponent<CompanionStateMachine>() : null;

            if (forms != null)
            {
                forms.UnlockForms();
            }

            EnterP1();
        }

        public BossPhase GetPhase()
        {
            return _phase;
        }

        /// <summary>是否已开打。HUD 据此决定要不要显示（没进战场前不该有 Boss 血条）。</summary>
        public bool IsEngaged()
        {
            return _started;
        }

        /// <summary>本组件的血量来源（BossHud 用）</summary>
        public Damageable GetDamageable()
        {
            return _damageable;
        }

        /// <summary>场上还活着几只狐火（自测用）。</summary>
        public int AliveFogFireCount
        {
            get
            {
                PruneFogFires();
                return _fogFires.Count;
            }
        }

        private void Update()
        {
            // 没开打就整个不转。
            // ⚠️ 这一句不能省：_phase 的默认值就是 P1，只判 phase 的话状态机会从
            // 第一帧就自己跑起来（autoStart = false 形同虚设）。
            if (!_started)
            {
                return;
            }

            if (_phase == BossPhase.Defeated)
            {
                return;
            }

            if (_damageable == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            _phaseElapsed += dt;

            switch (_phase)
            {
                case BossPhase.P1:
                    TickP1(dt);
                    break;
                case BossPhase.P2:
                    TickP2(dt);
                    break;
                case BossPhase.P3:
                    TickP3(dt);
                    break;
                case BossPhase.Lament:
                    // 停止攻击，等着被净化 —— 什么都不做
                    break;
            }
        }

        // ============================================================ P1 幻影

        private void EnterP1()
        {
            _phase = BossPhase.P1;
            _phaseElapsed = 0f;
            _waveTimer = FirstWaveDelay;

            SpiritEnergySystem.SetCombatSource(this, true);
            SetInvulnerable(false);
            ApplyFormArt();
            PlayArt(idleAnimation, true);

            Debug.Log("[Boss] P1 幻影 —— 开打");
        }

        private void TickP1(float dt)
        {
            TickFogFireWaves(dt, GameConfig.Current.Boss.FogFireWaveInterval);

            if (_damageable.GetHpRatio() <= GameConfig.Current.Boss.Phase2HpRatio)
            {
                EnterTransitionP2();
            }
        }

        // ============================================================ P2 狂乱

        private void EnterTransitionP2()
        {
            _phase = BossPhase.TransitionP2;
            _phaseElapsed = 0f;

            // 阶段转换是这段最该有声音的地方 —— 狐鸣
            GameAudio.PlaySfx(SfxCue.FoxCry);

            SpiritEnergySystem.SetCombatSource(this, false);
            SetInvulnerable(true);          // 转场期间免伤，否则会被一口气打穿一个阶段
            ClearFogFires();
            HaltRigidBody();

            Debug.Log("[Boss] HP 降至 70%，播放转场 → P2 狂乱");

            StageTransition trans = ResolveTransition();

            if (trans == null)
            {
                // 素材/场景缺一环不该把流程卡死 —— 直接进下一阶段，打条日志就够
                Debug.LogWarning("[Boss] 场景里没有 StageTransition，跳过转场直接进 P2");
                EnterP2();
                return;
            }

            trans.Play(transitionAnimation, EnterP2);
        }

        private void EnterP2()
        {
            _phase = BossPhase.P2;
            _phaseElapsed = 0f;
            _waveTimer = 1.5f;

            SpiritEnergySystem.SetCombatSource(this, true);
            SetInvulnerable(false);
            ApplyFormArt();
            PlayArt(p2Animation, true);

            // 浊湮铺满战场：实体形态踩上去持续掉灵炁，浮空（灵雾/灵合）免疫
            _corruption = CorruptionField.Spawn(LevelRoot(), _spawnX);

            Debug.Log("[Boss] P2 狂乱 —— 浊湮领域展开");
        }

        private void TickP2(float dt)
        {
            // P2 的狐火来得更密
            TickFogFireWaves(dt, GameConfig.Current.Boss.FogFireWaveInterval * 0.6f);

            if (_damageable.GetHpRatio() <= GameConfig.Current.Boss.Phase3HpRatio)
            {
                EnterTransitionP3();
            }
        }

        // ============================================================ P3 归心

        private void EnterTransitionP3()
        {
            _phase = BossPhase.TransitionP3;
            _phaseElapsed = 0f;

            GameAudio.PlaySfx(SfxCue.FoxCry);

            SpiritEnergySystem.SetCombatSource(this, false);
            SetInvulnerable(true);
            ClearFogFires();
            HaltRigidBody();

            _corruption?.Dismiss();
            _corruption = null;

            Debug.Log("[Boss] HP 降至 35%，播放转场 → P3 归心（莹翳强制灵合）");

            StageTransition trans = ResolveTransition();

            if (trans == null)
            {
                Debug.LogWarning("[Boss] 场景里没有 StageTransition，跳过转场直接进 P3");
                EnterP3();
                return;
            }

            trans.Play(transitionP3Animation, EnterP3);
        }

        private void EnterP3()
        {
            _phase = BossPhase.P3;
            _phaseElapsed = 0f;

            SpiritEnergySystem.SetCombatSource(this, true);
            SetInvulnerable(false);
            ApplyFormArt();
            PlayArt(p3Animation, true);

            // 剧情强制灵合：莹翳主动飞向青禾。
            // 走 ForceSwitchTo 而不是 TrySwitchTo —— 不能因为玩家当刻灵炁不足 /
            // 正在冷却就失败，否则 Boss 战会卡在 P3 门口，玩家还不知道为什么。
            if (_targetForms != null)
            {
                _targetForms.ForceSwitchTo(CompanionForm.Merge);
            }

            Debug.Log("[Boss] P3 归心 —— 强制灵合，用净化光环与灵炁爆发收尾");
        }

        private void TickP3(float dt)
        {
            // P3 不再召狐火：这一段的设计是「玩家反过来碾压」，
            // 输出手段是灵合净化光环 + 灵炁爆发（都在 CombatController 里）
            if (_damageable.GetHpRatio() <= GameConfig.Current.Boss.LamentHpRatio)
            {
                EnterLament();
            }
        }

        private void EnterLament()
        {
            _phase = BossPhase.Lament;
            _phaseElapsed = 0f;

            SpiritEnergySystem.SetCombatSource(this, false);
            ClearFogFires();

            Debug.Log("[Boss] HP ≤10%，停止攻击 —— 哀鸣，等着被净化");
        }

        // ============================================================ 狐火

        /// <summary>
        /// 每 <paramref name="interval"/> 秒召一波狐火；场上到上限就不再召。
        /// 三条阶段（P1/P2）共用。
        /// </summary>
        private void TickFogFireWaves(float dt, float interval)
        {
            _waveTimer -= dt;

            if (_waveTimer > 0f)
            {
                return;
            }

            _waveTimer = interval;
            SummonFogFireWave();
        }

        private void SummonFogFireWave()
        {
            BossConfig cfg = GameConfig.Current.Boss;

            PruneFogFires();
            int room = cfg.FogFireMaxAlive - _fogFires.Count;

            if (room <= 0)
            {
                return;
            }

            int count = Mathf.Min(cfg.FogFirePerWave, room);
            Transform root = LevelRoot();

            for (int i = 0; i < count; i++)
            {
                float dx = Random.Range(-1f, 1f) * FogFireSpawnSpreadX;
                Vector2 pos = new Vector2(_spawnX + dx, transform.position.y + FogFireSpawnDy);

                // 每只一个种子（计数器递增）：同一局里性格稳定，不同只互不相同
                FogFire fire = FogFire.Spawn(root, pos, _fogFireSeed++);
                fire.OnDied = OnFogFireDied;
                _fogFires.Add(fire);
            }

            Debug.Log($"[Boss] 召出 {count} 只狐火（场上 {_fogFires.Count} 只）");
        }

        private void OnFogFireDied(FogFire fire)
        {
            _fogFires.Remove(fire);
        }

        /// <summary>
        /// 清掉场上全部狐火（转场 / 死亡时用）。
        ///
        /// 逐个 <c>Destroy</c> 而不是直接 <c>Clear</c> —— 留着节点会变成孤儿：
        /// 转场动画期间玩家看不见它们，转场结束却发现自己被一群狐火围着。
        /// </summary>
        private void ClearFogFires()
        {
            foreach (FogFire f in _fogFires)
            {
                if (f != null)
                {
                    f.OnDied = null;
                    Destroy(f.gameObject);
                }
            }

            _fogFires.Clear();
        }

        /// <summary>剔除已被销毁的条目。</summary>
        private void PruneFogFires()
        {
            for (int i = _fogFires.Count - 1; i >= 0; i--)
            {
                if (_fogFires[i] == null)
                {
                    _fogFires.RemoveAt(i);
                }
            }
        }

        // ============================================================ 收尾

        private void OnBossDefeated(Damageable self)
        {
            _phase = BossPhase.Defeated;
            _phaseElapsed = 0f;

            SpiritEnergySystem.SetCombatSource(this, false);
            SetInvulnerable(true);
            ClearFogFires();
            HaltRigidBody();

            _corruption?.Dismiss();
            _corruption = null;

            Debug.Log("[Boss] 被净化 —— 播放最终净化过场");

            // 结局过场走 **StageTransition 全屏播**，而不是在 Boss 立绘上播。
            //
            // 理由：`九尾狐-最终净化` 的素材本来就是**全屏规格**（画布 960×540，
            // 内容占满整幅），在 Boss 那个 553 单位的立绘节点上播等于把它缩到 1/4，
            // 细节全糊了。全屏播才用得上美术画的构图。
            // 顺带白拿两件事：转场期间 GameFlow 会锁输入 + 停游戏逻辑。
            StageTransition trans = ResolveTransition();

            if (trans != null)
            {
                trans.Play(purifyAnimation, GoToEnding);
                return;
            }

            // 场景里没接转场就退回老做法（在立绘上播），至少别把流程卡住
            Debug.LogWarning("[Boss] 场景里没有 StageTransition，净化过场改为在立绘上播");

            if (_player == null)
            {
                GoToEnding();
                return;
            }

            PlayArt(purifyAnimation, false, GoToEnding);
        }

        private void GoToEnding()
        {
            Debug.Log("[Boss] 净化过场结束，进入结局");
            // 走 LevelHost 是为了经过复位协议（与 GlobalBackKey 同一个理由）
            if (LevelHost.Instance != null)
            {
                LevelHost.Instance.GoEnding();
            }
            else
            {
                GameSceneManager.LoadSingle(GameScenes.Ending);
            }
        }

        // ============================================================ 工具

        private void SetInvulnerable(bool on)
        {
            if (_damageable != null)
            {
                _damageable.Invulnerable = on;
            }
        }

        private void HaltRigidBody()
        {
            if (_rigidBody != null)
            {
                _rigidBody.linearVelocity = Vector2.zero;
            }
        }

        /// <summary>
        /// 狐火挂到**关卡根节点**上，不要挂在 Boss 身上 —— Boss 一旦动起来狐火会跟着飘。
        ///
        /// Boss 是场景根节点（没有父节点）时返回 null，<c>SetParent(null)</c> 会把狐火
        /// 放到场景根 —— 那正是我们要的。**别退回 <c>transform</c>**：那就是挂在 Boss 身上了。
        /// </summary>
        private Transform LevelRoot()
        {
            return transform.parent;
        }

        private StageTransition ResolveTransition()
        {
            if (_transition != null)
            {
                return _transition;
            }

            _transition = FindAnyObjectByType<StageTransition>();
            return _transition;
        }

        private void ResolveTarget()
        {
            QingheController qinghe = FindAnyObjectByType<QingheController>();

            if (qinghe == null)
            {
                Debug.LogWarning("[Boss] 找不到玩家，阶段逻辑仍会推进但没有交互对象");
                return;
            }

            _target = qinghe.transform;
            _targetCombat = qinghe.GetComponent<CombatController>();
            _targetForms = qinghe.GetComponent<CompanionStateMachine>();
        }

        /// <summary>
        /// 接上立绘的序列帧播放器。
        ///
        /// ⚠️ 放 <c>Awake</c> 而**不是** <c>Start</c>：<see cref="Begin"/> 可能在
        /// <c>Awake</c> 里被 <c>autoStart</c> 直接调起来（那时就要 <c>Play</c>），
        /// 而 Unity 不保证 Start 在别的组件调 Begin 之前跑过。
        /// 这里只做「找节点 + 挂组件」，不读任何由外部赋的值，所以放 Awake 是安全的。
        /// </summary>
        private void SetupArt()
        {
            _artNode = transform.Find(artName);

            if (_artNode == null)
            {
                Debug.LogWarning($"[Boss] 找不到 \"{artName}\" 子节点，立绘不会显示");
                return;
            }

            _artSprite = _artNode.GetComponent<SpriteRenderer>();

            if (_artSprite == null)
            {
                _artSprite = _artNode.gameObject.AddComponent<SpriteRenderer>();
            }

            _artSprite.sortingOrder = SortingOrders.Boss;

            _player = _artNode.GetComponent<SequencePlayer>();

            if (_player == null)
            {
                _player = _artNode.gameObject.AddComponent<SequencePlayer>();
            }

            // 单只 Boss，窗口不用留太宽
            _player.aheadPages = 1;
            _player.behindPages = 1;

            ApplyFormArt();          // 未开打时也先按 P1 摆好，避免第一帧尺寸不对
        }

        /// <summary>
        /// 按当前阶段摆好立绘的缩放与 y 偏移。
        ///
        /// 三段形态动画的**画布一样、内容不一样**（见 <see cref="formScaleP1"/> 的说明），
        /// 所以每次换形态都要重摆一次；不摆的话 Boss 会在阶段切换时忽大忽小、上下跳。
        /// </summary>
        private void ApplyFormArt()
        {
            if (_artNode == null)
            {
                return;
            }

            float k;
            float dy;

            switch (_phase)
            {
                case BossPhase.P2:
                case BossPhase.TransitionP2:
                    k = formScaleP2;
                    dy = formOffsetYP2;
                    break;

                case BossPhase.P3:
                case BossPhase.TransitionP3:
                case BossPhase.Lament:
                case BossPhase.Defeated:
                    k = formScaleP3;
                    dy = formOffsetYP3;
                    break;

                default:
                    k = formScaleP1;
                    dy = formOffsetYP1;
                    break;
            }

            float s = artScale * k;
            _artNode.localScale = new Vector3(s, s, 1f);
            _artNode.localPosition = new Vector3(0f, dy, 0f);
        }

        private void PlayArt(string name, bool loop, System.Action onComplete = null)
        {
            if (_player == null || string.IsNullOrEmpty(name))
            {
                return;
            }

            _player.Play(name, loop, 0, -1, 1f, onComplete);
        }
    }
}
