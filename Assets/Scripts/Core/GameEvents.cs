namespace CloudEyrie
{
    /// <summary>
    /// 全部事件名常量。对应 Cocos 侧散落在各处的字符串字面量
    /// （<c>'spirit-energy-changed'</c> 等）—— 集中成一处，避免打错字后静默失效。
    ///
    /// ⚠️ 加事件时两边一起加：这里加常量，并确保有且只有一处 Emit。
    /// </summary>
    public static class GameEvents
    {
        // ---- 全局生命周期 ----
        /// <summary>GameManager 初始化完成。无载荷。</summary>
        public const string GameInitialized = "game-initialized";

        /// <summary>数值配置就绪。载荷：<see cref="GameConfig"/>。</summary>
        public const string GameConfigReady = "game-config-ready";

        // ---- 灵炁 ----
        /// <summary>灵炁值变化。载荷：<see cref="SpiritEnergyChangedEvent"/>。</summary>
        public const string SpiritEnergyChanged = "spirit-energy-changed";

        /// <summary>灵炁归零。无载荷。⚠️ 目前**无任何消费方**（设计上是「倒地回存档点」，未实现）。</summary>
        public const string SpiritEnergyDepleted = "spirit-energy-depleted";

        // ---- 形态 ----
        /// <summary>灵伴形态切换。载荷：<see cref="CompanionFormChangedEvent"/>。</summary>
        public const string CompanionFormChanged = "companion-form-changed";

        // ---- 设置 ----
        /// <summary>音量/键位设置变化。载荷：<see cref="SettingsChangedEvent"/>。</summary>
        public const string SettingsChanged = "settings-changed";

        // ---- 战斗 ----
        /// <summary>敌人死亡。载荷：<see cref="EnemyDiedEvent"/>。</summary>
        public const string EnemyDied = "enemy-died";

        // ---- 界面提示 ----
        /// <summary>请求在屏幕上闪一行短提示。载荷：<see cref="HintEvent"/>。</summary>
        public const string HintRequested = "hint-requested";

        // ---- 收集 ----
        /// <summary>拾得一枚碎片。无载荷（要数字请读 <see cref="PlayerProgress"/>）。</summary>
        public const string FragmentCollected = "fragment-collected";

        /// <summary>获得一枚五行印记。无载荷。</summary>
        public const string SealGranted = "seal-granted";

        // ---- 关卡编排 ----
        /// <summary>
        /// 让 Boss 开打。无载荷。
        ///
        /// 由关卡编排里那条 <c>SegmentCue.emitEvent = "boss-begin"</c> 发出，
        /// <see cref="BossAI"/> 自己订阅。
        ///
        /// <b>为什么不直接调 <c>boss.Begin()</c></b>：数据表表达不了"调用某个组件的某个方法"，
        /// 只能表达"发生了这件事"。让关心它的组件自己订阅，编排侧才不必认识 Boss ——
        /// 新关卡想加一个"进这段就开打"的 Boss，也只要配一条 cue 即可。
        /// </summary>
        public const string BossBegin = "boss-begin";
    }

    /// <summary>
    /// 一条**屏幕短提示**。由玩法侧发出、<see cref="GameHud"/> 负责显示若干秒后淡掉。
    ///
    /// <b>为什么要有这条链路</b>：本作有一串「按了没反应」的拒绝分支
    /// （灵合冷却中 / 灵炁爆发本回合已用过 / 灵炁不足……），原来它们**只打 Debug.Log** ——
    /// 玩家侧完全看不到，只能归结为「有时候行有时候不行」。
    /// 实测已经因此困惑过两轮（「条件满足却放不出爆发」「切回灵合要等很久，是时间限制吗」）。
    ///
    /// 走 EventBus 而不是让玩法侧持 HUD 引用：发出方在 Core/Combat 层，不该认识 UI
    /// （与 <see cref="SettingsChangedEvent"/> 同一条约定）。
    /// </summary>
    public struct HintEvent
    {
        /// <summary>提示文字。**要短** —— 一行、一眼能读完。</summary>
        public string Text;

        /// <summary>显示时长 /秒。≤0 用 HUD 的默认值。</summary>
        public float Seconds;
    }
}
