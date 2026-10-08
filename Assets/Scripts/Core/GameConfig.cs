using UnityEngine;

namespace CloudEyrie
{
    // ============================================================
    // 配置结构定义（与 week1 v1 参数表一一对应）
    //
    // 对应 Cocos 侧 core/GameConfig.ts。那里是「@ccclass 组件 + 81 个 @property，
    // onLoad 时把 Inspector 值刷进模块级单例 gameConfig」；Unity 里不需要这层搬运
    // —— ScriptableObject 本身就是存储，Inspector 直接改它。
    //
    // ⚠️ 读取一律走 GameConfig.Current.Xxx.Yyy（惰性），**不要缓存子配置对象**，
    //    否则运行时换配置不会生效（TS 侧同样的约定，见原文件注释）。
    // ============================================================

    /// <summary>灵炁池 + 形态 + 战斗消耗 + 敌人伤害。</summary>
    [System.Serializable]
    public class SpiritConfig
    {
        [Header("灵炁池基础")]
        public float Max = 100f;                    // 灵炁池上限
        public float Initial = 80f;                 // 灵炁初始值（游戏开始）
        public float RegenOutOfCombat = 5f;         // 非战斗恢复速率 /秒
        public float RegenInCombat = 1f;            // 战斗恢复速率 /秒
        public float SpringRestoreRate = 30f;       // 灵泉恢复速率 /秒
        public float SpringRestoreCapRatio = 1f;    // 灵泉恢复上限比率（1 = 100%）

        [Header("形态切换")]
        public float SwitchEntityToMist = 10f;      // 实体 → 灵雾
        public float SwitchMistToEntity = 5f;       // 灵雾 → 实体
        public float SwitchMistToMerge = 30f;       // 灵雾 → 灵合

        /// <summary>
        /// 进入灵合的最低灵炁。
        ///
        /// ⚠️ <b>它必须 ≥ <see cref="SwitchMistToMerge"/> + <see cref="MergeBurstCost"/></b>
        /// （30 + 30 = 60）—— 否则会出现「门进得去、爆发永远放不出」的死区：
        /// 进门要 ≥ 门槛、进门立刻扣掉进门费、落地灵炁 = 门槛 − 30，
        /// 而爆发还要 30。原来门槛是 50，于是在灵炁 50~60 之间进灵合**必然放不出爆发**。
        /// 叠加灵合每秒净扣 15~19 点（净化 20 − 回复 1~5），初始值 80 进场也只有约 1 秒窗口，
        /// 玩家侧的表现就是「满足条件却不行」。
        ///
        /// 2026-10-07 由 50 提到 <b>70</b>：不变式是
        /// <c>门槛 ≥ 进门费 + 爆发费 + 强制切雾阈值</c>（30 + 30 + 10 = 70）——
        /// 意思是「能进灵合 ⟹ 进门后放得出爆发，**而且爆发完不会立刻被踢回灵雾**」。
        /// 取 60（只是 ≥ 进门费+爆发费）会让门槛进场的人爆发完灵炁归零、当场被踢出，
        /// 那不是一条值得保证的不变式。
        ///
        /// 这个不变式由 <see cref="Validate"/> 在启动时自检，改坏了会报错。
        /// </summary>
        public float MergeMinSpirit = 70f;          // 进入灵合的最低灵炁（≥ 进门费 + 爆发费 + 强制切雾阈值）
        public float SwitchMergeToEntity = 15f;     // 灵合 → 实体
        public float SwitchMergeToMist = 10f;       // 灵合 → 灵雾
        public float ForcedMistThreshold = 10f;     // 灵炁低于此值强制切灵雾
        public float SwitchLockDuration = 0.5f;     // 形态切换硬直 /秒

        [Header("战斗消耗")]
        public float LightAttackCost = 8f;          // 轻击（每次）
        public float HeavyAttackCost = 15f;         // 重击（蓄力斩）
        public float CloudDashCost = 12f;           // 踏云闪
        public float MistShotCost = 5f;             // 灵雾普攻 /发
        public float MergePurifyCostPerSec = 20f;   // 灵合净化 /秒
        public float MergeBurstCost = 30f;          // 灵炁爆发
        public float BlockCost = 10f;               // 格挡（完美格挡不扣）

        [Header("灵合形态的输出（以前只有消耗、没有伤害）")]
        public float MergePurifyDamagePerSec = 30f; // 净化光环：范围内每秒对目标造成的伤害
        public float MergePurifyRadius = 220f;      // 净化光环半径
        public float MergeBurstDamage = 50f;        // 灵炁爆发：单次 AOE 伤害
        public float MergeBurstRadius = 340f;       // 灵炁爆发半径

        [Header("灵雾形态持续消耗")]
        public float MistBaseDrain = 3f;            // 灵雾基础消耗 /秒
        public float MistFlyDrainExtra = 2f;        // 高速飞行额外消耗 /秒

        /// <summary>穿越灵界屏障 /次。⚠️ **尚未接线** —— 全工程无人读取，穿屏障目前是免费的。</summary>
        public float MistPassObstacleCost = 20f;

        [Header("敌人伤害（扣灵炁）")]
        public float EnemyYangMeleeDamage = 15f;    // 阳浊型·普攻

        /// <summary>阴浊型·远程。⚠️ **尚未接线**（阴浊型敌人未实现）。</summary>
        public float EnemyYinRangedDamage = 10f;

        /// <summary>BOSS·普攻。⚠️ **尚未接线** —— Boss 的伤害走的是别的字段，改这个不会有任何效果。</summary>
        public float BossMeleeDamage = 20f;

        /// <summary>BOSS·特殊技能。⚠️ **尚未接线**，同上。</summary>
        public float BossSkillDamage = 35f;

        public float CorruptionContactDrain = 5f;   // 浊湮接触伤害 /秒

        [Header("灵合冷却与休眠")]
        /// <summary>
        /// 主动离开灵合后的再次灵合冷却 /秒。
        ///
        /// ⚠️ <b>2026-10-08 由 20 降到 5。</b> 20 是**沿用老 GDD 的遗留值**
        /// （原来这一段的标题就写着「v1 未写」），从没按当前设计校对过。
        /// 它和当前节奏严重冲突：灵合自身只持续约 5 秒（净化 20/s、上限 100），
        /// 而冷却 20 秒意味着**形态有 75% 的时间是可望不可即的** ——
        /// 玩家回满灵炁却切不进去，而且拒绝只进日志、屏幕上看不见，
        /// 表现就是「明明条件满足却不行」（实测反馈原话）。
        ///
        /// 真正的资源闸门是 <see cref="MergeMinSpirit"/>（70）：一轮「进门 30 + 爆发 30」
        /// 花掉 60，之后必须回灵泉或脱战回炁才能再来。所以冷却只需要挡住
        /// **瞬间进出刷爆发**，5 秒足够。
        ///
        /// 不变式：**冷却不该长过「回满一次灵炁」所需的时间**，
        /// 否则它就成了隐形的时间闸（由 <see cref="Validate"/> 检查）。
        /// </summary>
        public float MergeCooldown = 5f;

        /// <summary>
        /// ⚠️ <b>2026-10-08 置 0（停用）。</b> 两个原因：
        /// <list type="number">
        ///   <item>它和 <see cref="MergeCooldown"/> 在同一处一起置位、门控又是「或」判断，
        ///     所以两者里**只有大的那个起作用**。原本 20 vs 10，它从不生效；
        ///     而一旦把冷却降到 5，它就会**顶替**冷却变成真正的闸 —— 降冷却的改动会被它悄悄吃掉。</item>
        ///   <item>老 GDD 说它表示「结束后 10 秒无法召唤灵伴」，而**灵伴本体的召唤没有实现**，
        ///     这条语义目前是空的。</item>
        /// </list>
        /// 留 0 而不是删字段：保住资产里的位置与语义备注。**要重新启用得先实现「召唤灵伴」。**
        /// </summary>
        public float MergeSleepDuration = 0f;
    }

    /// <summary>灵脉潮汐。⚠️ 当前**无任何消费方**（潮汐系统未实现，仅 SpiritEnergySystem 提供了入口）。</summary>
    [System.Serializable]
    public class TideConfig
    {
        public float Period = 120f;                     // 灵脉潮汐周期 /秒
        public float HighTideRegenMult = 2.0f;          // 高潮恢复倍率

        /// <summary>高潮敌人活性倍率。⚠️ **尚未接线** —— 全工程无人读取（潮汐只影响回复速度）。</summary>
        public float HighTideEnemyActivityMult = 1.2f;

        public float LowTideRegenMult = 0.5f;           // 低潮恢复倍率
    }

    /// <summary>青禾的战斗数值。</summary>
    [System.Serializable]
    public class CombatConfig
    {
        [Header("轻击三连")]
        public float BaseLightDamage = 15f;         // 轻击基础伤害（倍率作用于它）
        public float[] ComboDamageMult = { 1.0f, 1.2f, 1.5f };  // 三段伤害倍率（不暴露 Inspector，代码内定）
        public float ComboKnockback = 48f;          // 第三段击退距离（Cocos 原值 1.5 m × 32）
        public float ComboWindow = 0.3f;            // 连击窗口 /秒 —— ⚠️ 从**收招**计时，不是起手

        [Header("蓄力重击")]
        public float LightTapThreshold = 0.2f;      // 点按判定阈值（<此值=轻击，否则蓄力）
        public float HeavyChargeTime = 1.0f;        // 满蓄时长 /秒
        public float HeavyFullDamageMult = 2.5f;    // 满蓄伤害倍率
        public float HeavyHalfDamageMult = 1.5f;    // 半蓄伤害倍率（同时是「这段算重击」的判定界）
        public float HeavyKnockback = 96f;          // 重击击退（Cocos 原值 3.0 m × 32）

        [Header("格挡")]
        public float BlockReduction = 0.7f;         // 格挡减伤比例（0.7 = 70%）
        public float PerfectBlockWindow = 0.2f;     // 完美格挡窗口 /秒

        [Header("灵弹")]
        public float MistShotDamageRatio = 0.6f;    // 灵弹伤害比例（相对轻击）
        public float MistShotKnockback = 4.8f;      // 灵弹击退（作为水平速度直接施加；Cocos 原值 0.15 × 32）
        public float MistShotSpeed = 320f;          // 灵弹飞行速度（Cocos 原值 10 m/s × 32）
        public float MistShotLifetime = 2.0f;       // 灵弹存活 /秒

        [Header("灵弹外观 / 命中反馈")]
        public float MistShotSpawnX = 26f;          // 发射点相对角色（角色原点在脚底）
        public float MistShotSpawnY = 55f;
        public float MistShotSize = 28f;            // 灵弹显示尺寸
        public float MistShotBody = 18f;            // 灵弹物理体尺寸
        public float HitSparkSize = 56f;            // 命中火花显示尺寸（重击会再放大）
        public float AuraTick = 0.25f;              // 灵合净化光环的结算间隔 /秒

        [Header("敌人的箭")]
        public float ArrowHitHalfW = 75f;           // 箭命中判定的半宽
        public float ArrowHitHalfH = 10f;           // 箭命中判定的半高

        /// <summary>
        /// 水墨粒子的三形态配色。
        ///
        /// 青禾的骨骼动画还没做完，**粒子是目前唯一能把三个形态区分开的表现手段** ——
        /// 所以这三个颜色不是纯装饰，是玩法可读性的一部分：
        /// 实体=墨黑（本体的颜色）、灵雾=青冷（灵体）、灵合=暖金（莹翳合体后的灵光）。
        /// </summary>
        [Header("水墨粒子配色（三形态）")]
        public Color InkTintEntity = new Color32(46, 40, 52, 235);
        public Color InkTintMist = new Color32(150, 240, 255, 235);
        public Color InkTintMerge = new Color32(255, 226, 150, 235);
    }

    /// <summary>小怪数值。数值全部来自这里，**不给单实例配**。</summary>
    [System.Serializable]
    public class EnemyConfig
    {
        [Header("移动")]
        // ⚠️ 速度单位是**世界单位/秒**（= 像素/秒），不是 Cocos 配置里的「米/秒」。
        // Cocos 原值 1.44 m/s × PHYSICS_2D_PTM_RATIO(32) = 46.08。见 PhysicsUnits。
        public float PatrolSpeed = 46.08f;          // 巡逻速度（Cocos 原值 1.44 m/s）
        public float ChaseSpeed = 122.88f;          // 追击速度（Cocos 原值 3.84 m/s）
        public float PatrolRange = 140f;            // 巡逻往返范围（相对出生点）
        public float PatrolPause = 0.8f;            // 巡逻点停留 /秒

        [Header("感知")]
        public float SightRange = 140f;             // 发现玩家距离
        public float VerticalTolerance = 70f;       // 垂直容差：**两者脚底**的 y 差超过它就算「不在同一层」（比脚底不比节点原点，见 EnemyAI.VerticalOffsetToTarget）
        public float DiscoverDuration = 0.45f;      // 发现反应时间 /秒

        [Header("攻击")]
        public float AttackRange = 72f;             // 近战攻击判定距离
        public float AttackStartup = 0.3f;          // 攻击前摇 /秒
        public float AttackDuration = 0.55f;        // 攻击总时长 /秒
        public float AttackCooldown = 2.16f;        // 攻击间隔 /秒

        [Header("远程型（持弓的阳浊小怪）")]
        public float RangedAttackRange = 460f;      // 远程攻击距离
        public float RangedAttackDuration = 1.5f;   // 远程攻击总时长（要和攻击动画对齐）
        public float RangedAttackTimeScale = 2.0f;  // 张弓搭箭的播放倍速（会反过来改变实际时长）
        public float ArrowDamage = 15f;             // 箭的伤害（扣灵炁）
        public float ArrowSpeed = 900f;             // 箭的飞行速度

        [Header("受击与死亡")]
        public float HitStunDuration = 0.3f;        // 受击硬直 /秒（只对带击退的命中生效）
        public float DeathDuration = 0.5f;          // 死亡表现时长 /秒
    }

    /// <summary>
    /// 九尾狐 Boss 数值。
    ///
    /// ⚠️ 别和 <see cref="SpiritConfig.BossMeleeDamage"/> / <see cref="SpiritConfig.BossSkillDamage"/>
    /// 搞混 —— 那两个其实是「Boss 打玩家扣多少灵炁」，不是 Boss 自身属性。
    ///
    /// 取值依据 <c>docs/关于boss战的一些修改.md</c>：总血 1000，P1 300 点 → 70% 处转场。
    /// </summary>
    [System.Serializable]
    public class BossConfig
    {
        public float MaxHp = 1000f;                 // Boss 总血量
        public float Phase2HpRatio = 0.70f;         // 进入 P2 的血量比
        public float Phase3HpRatio = 0.35f;         // 进入 P3 的血量比

        /// <summary>HP 降到这个比例时 Boss 停止攻击、进入「哀鸣」（P3 末段）。</summary>
        public float LamentHpRatio = 0.10f;

        // ---- 狐火：Boss 的核心攻击手段 ----
        //
        // 九尾狐**没有任何挥击/甩尾的骨骼动画**（美术只给了 幻影/狂乱/归心 三段形态动画），
        // 所以它的「攻击」必须靠**召唤物与弹幕**表达，不能靠本体动作。
        // 狐火正好补上这个位置：从尾部浊焰里弹出的小型灵体。

        [Header("狐火")]
        public int FogFirePerWave = 2;              // 每波召唤几只
        public int FogFireMaxAlive = 3;             // 场上同时存活上限（到顶就不召）
        public float FogFireWaveInterval = 7f;      // 召唤间隔 /秒
        public float FogFireSpeed = 105f;           // 追击速度（世界单位/秒）
        public float FogFireDamage = 15f;           // 接触伤害（扣灵炁）
        public float FogFireHp = 40f;               // 狐火血量（活实体形态几下轻击）
        public float FogFireContactCooldown = 1.4f; // 同一只狐火两次接触伤害的最小间隔 /秒
        public float FogFireArtScale = 1f;          // 立绘缩放。乘的是打包后的帧尺寸（192）
        public float FogFireRiseTimeScale = 1.8f;   // 出场那段的播放倍速

        [Header("狐火 · 手感细节")]
        public float FogFireStandoff = 58f;         // 悬停在玩家多远（再近就不加速了）
        public float FogFireHitHalfW = 46f;         // 接触判定的半宽
        public float FogFireHitHalfH = 46f;         // 接触判定的半高
        public float FogFireDeathDuration = 0.35f;  // 死亡表现时长
        public float FogFireDeathShrink = 0.25f;    // 死亡缩到原缩放的多少
        public float FogFireRepelSpeed = 260f;      // 被完美格挡弹开的初速
        public float FogFireFacingDeadZone = 8f;    // 朝向抖动死区

        [Header("狐火 · 出场位置")]
        public float FogFireSpawnSpreadX = 260f;    // 相对 Boss 的横向散布
        public float FogFireSpawnDy = -120f;        // 相对 Boss 的纵向偏移（尾部浊焰高度）
        public float FogFireFirstWaveDelay = 2.5f;  // 开局到第一波之间给玩家的缓冲

        [Header("浊湮领域（P2）")]
        public float CorruptionHalfWidth = 1400f;   // 领域半宽
        public float CorruptionHalfHeight = 420f;   // 领域半高
        public float CorruptionFadeIn = 1.2f;       // 淡入时长 /秒
    }

    /// <summary>
    /// 全局数值配置（唯一真相源）—— 对应 Cocos 侧挂载在 GameManager 节点上的 <c>GameConfig</c> 组件
    /// 与模块级单例 <c>gameConfig</c>。
    ///
    /// 用法：<c>GameConfig.Current.Spirit.Max</c>。
    ///
    /// 资源位置：<c>Assets/Resources/GameConfig.asset</c>（用 <c>Resources.Load</c> 取，
    /// 所以放在 Resources 下）。找不到时会回落到一份运行时默认实例并打警告，
    /// 保证游戏不会因为少一个资源就整个跑不起来。
    ///
    /// ⚠️ <b>改了下面的字段默认值、却没重建资源 = 白改。</b>
    ///
    /// ScriptableObject 一旦创建，字段值就**冻结在 <c>.asset</c> 里**了。之后改代码里的
    /// <c>= 46.08f</c> 对它毫无影响 —— 运行时读到的仍是资源里的旧值，
    /// 而且**不报任何错**，只表现为「改了没反应」。
    ///
    /// 这和 Cocos 那条「<c>@property</c> 的默认值会冻结进场景」是同一类坑
    /// （见 <c>docs/计划-代码对照.md</c> §8），只是载体从场景换成了 ScriptableObject。
    /// 迁移时就因为这条：小怪速度在代码里改成了 46.08、资源里还是 1.44，
    /// 它以 1.44 单位/秒挪动，看起来像「小怪不会动」。
    ///
    /// <b>改默认值后必须重建资源</b>，或者直接改 Inspector 里的值（那才是资源的正常用法）。
    /// 重建：删掉 <c>Assets/Resources/GameConfig.asset</c>，再
    /// 右键 → Create → CloudEyrie → 游戏数值配置。
    /// </summary>
    [CreateAssetMenu(fileName = "GameConfig", menuName = "CloudEyrie/游戏数值配置")]
    public class GameConfig : ScriptableObject
    {
        public SpiritConfig Spirit = new SpiritConfig();
        public TideConfig Tide = new TideConfig();
        public CombatConfig Combat = new CombatConfig();
        public EnemyConfig Enemy = new EnemyConfig();
        public BossConfig Boss = new BossConfig();

        public const string ResourcePath = "GameConfig";

        /// <summary>
        /// 跨字段的**不变式自检**。由 <see cref="GameManager"/> 启动时调一次。
        ///
        /// <b>为什么要专门写这个</b>：本作的数值分在五段里（灵炁/潮汐/战斗/敌人/Boss），
        /// 彼此之间有隐含的算术关系，而**破坏这些关系不会报任何错** ——
        /// 只会变成玩家侧一句「有时候按了没反应」。已经栽过一次：
        /// 灵合门槛 50 小于「进门费 30 + 爆发费 30」，于是灵炁 50~60 进门
        /// **必然放不出爆发**，只能靠人工读代码才查得出来。
        ///
        /// 所以把这类关系写成断言，改坏了启动就报错。这属于**编辑期错误**
        /// （数值表写坏了），不是运行时状况，所以不打日志开关、任何构建里都该吵。
        /// </summary>
        public void Validate()
        {
            // 1) 灵合：**门能进 ⟹ 放得出爆发，而且爆发完不会被立刻踢出**
            //
            //    门槛 − 进门费 ≥ 爆发费 + 强制切雾阈值
            //
            // 左边是「进门后手里剩多少」，右边是「一次爆发要多少 + 至少留多少才不会被
            // 强制切回灵雾」。破了这条会出现两种都很难查的表现：
            //   门槛太低 → 进了灵合却永远放不出爆发（玩家说「有时不管用」）
            //   余量太少 → 爆发完灵炁归零，当场被踢回灵雾（玩家说「灵合一进去就没了」）
            float entryCost = Spirit.SwitchMistToMerge;
            float need = Spirit.MergeBurstCost + Spirit.ForcedMistThreshold;
            float have = Spirit.MergeMinSpirit - entryCost;

            if (have < need)
            {
                Debug.LogError(
                    $"[GameConfig] 灵合数值不自洽：门槛 {Spirit.MergeMinSpirit} − 进门费 {entryCost} = {have}，" +
                    $"小于「爆发费 {Spirit.MergeBurstCost} + 强制切雾阈值 {Spirit.ForcedMistThreshold}」= {need}。" +
                    $"请把 MergeMinSpirit 提到 ≥ {entryCost + need}，或下调 MergeBurstCost / ForcedMistThreshold。");
            }

            // 2) 一个常见笔误：初始值高于上限（开局会被静默夹到上限）
            if (Spirit.Initial > Spirit.Max)
            {
                Debug.LogError($"[GameConfig] Spirit.Initial({Spirit.Initial}) > Max({Spirit.Max})，开局会被夹到上限。");
            }

            // 3) 灵合冷却不该长过「回满一次灵炁」—— 否则它就成了**隐形的时间闸**：
            //    玩家回满灵炁却切不进去，而屏幕上看不到任何原因，只会觉得「有时候行有时候不行」。
            //    这正是 20 秒那版踩过的坑（实测反馈「切回灵合要等很久，是时间限制吗」）。
            float recharge = Spirit.RegenOutOfCombat > 0f
                ? Spirit.MergeMinSpirit / Spirit.RegenOutOfCombat
                : float.PositiveInfinity;

            if (Spirit.MergeCooldown > recharge)
            {
                Debug.LogWarning(
                    $"[GameConfig] 灵合冷却 {Spirit.MergeCooldown:F0} 秒 > 脱战回满所需 {recharge:F1} 秒" +
                    "（Max/MergeMinSpirit ÷ RegenOutOfCombat）。冷却会长过资源，玩家会以为是「隐形的时间限制」。");
            }

            // 4) 冷却与休眠取「大的那个」——两个都设时小的那个永远不生效，
            //    容易在调数值时被误判成「改了没用」。
            if (Spirit.MergeSleepDuration > Spirit.MergeCooldown)
            {
                Debug.LogWarning(
                    $"[GameConfig] 灵合休眠 {Spirit.MergeSleepDuration:F0} 秒 > 冷却 {Spirit.MergeCooldown:F0} 秒，" +
                    "此时休眠才是真正的闸（门控是「或」），改 MergeCooldown 不会有任何效果。");
            }
        }

        private static GameConfig _current;

        /// <summary>
        /// 运行时读取入口。惰性加载并缓存**资源引用**（不是配置值）——
        /// 数值本身仍走属性读取，改 asset 立即生效。
        /// </summary>
        public static GameConfig Current
        {
            get
            {
                if (_current == null)
                {
                    _current = Resources.Load<GameConfig>(ResourcePath);

                    if (_current == null)
                    {
                        Debug.LogWarning(
                            "[GameConfig] 未找到 Assets/Resources/GameConfig.asset，" +
                            "本次回落到代码内默认值（数值与 TS 版默认表一致）。" +
                            "请在工程里创建该资源：右键 → Create → CloudEyrie → 游戏数值配置。");
                        _current = CreateInstance<GameConfig>();
                    }
                }

                return _current;
            }
        }

        /// <summary>
        /// 供编辑器/测试注入。运行时不使用。
        /// </summary>
        public static void SetCurrentForTesting(GameConfig config)
        {
            _current = config;
        }

        private void OnValidate()
        {
            // 三段倍率不暴露 Inspector（TS 侧同样写死 [1, 1.2, 1.5]），
            // 但数组在 Inspector 里是可编辑的，这里防一手被误改短。
            if (Combat.ComboDamageMult == null || Combat.ComboDamageMult.Length != 3)
            {
                Combat.ComboDamageMult = new[] { 1.0f, 1.2f, 1.5f };
            }
        }
    }
}
