using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudEyrie
{
    /// <summary>
    /// 全局初始化入口 + 常驻单例 —— 对应 Cocos 侧 <c>core/GameManager.ts</c>。
    ///
    /// 职责（与 TS 版一致，刻意保持很薄）：
    ///   1. 防重复创建，跨场景常驻
    ///   2. 应用物理分组矩阵
    ///   3. 广播 <see cref="GameEvents.GameConfigReady"/> / <see cref="GameEvents.GameInitialized"/>
    ///   4. 每帧驱动 <see cref="SpiritEnergySystem.Update"/>（全局灵炁自然恢复 / 灵泉恢复）
    ///
    /// <b>Unity 侧的一处结构差异</b>：Cocos 把它挂在 <c>main.scene</c> 的常驻节点上
    /// （且 <c>Title.scene</c> 故意不放假</b>，避免两个副本打架）。Unity 里推荐建一个
    /// 独立的 <c>Bootstrap</c> 场景作为启动场景，只放这一个对象，然后加载 Title。
    /// 这样 Title 与关卡都不必关心它的存在 —— 语义与 TS 版的「常驻」一致，
    /// 但不再需要「哪个场景放、哪个不放」的口头约定。
    ///
    /// ⚠️ TS 版 <c>GameManager</c> 里挂了 EventBus 的冒烟测试代码，
    /// 这里**不要**挂 <c>spirit-energy-changed</c> 的日志监听 —— 灵炁逐帧变化，
    /// 一挂就会把控制台刷满、把战斗日志全淹掉（Cocos 侧踩过）。
    /// </summary>
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        private static GameManager _instance;

        public static GameManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    Debug.LogError("[GameManager] 尚未初始化。请确认启动场景里放了 GameManager。");
                }

                return _instance;
            }
        }

        public static bool HasInstance
        {
            get { return _instance != null; }
        }

        /// <summary>灵炁冒烟测试开关。需要排查灵炁系统时手动置 true。</summary>
        [Header("调试")]
        [Tooltip("启动时跑一遍灵炁自测（消耗/非战斗恢复/战斗恢复/灵泉恢复四档数值）")]
        [SerializeField]
        private bool debugSpiritSelfTest;

        private void Awake()
        {
            // 防止重复创建（Cocos 侧是 onLoad 里判 instance 非空则 destroy）
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // 跨场景常驻（对应 director.addPersistRootNode）
            if (transform.parent != null)
            {
                transform.SetParent(null);
            }

            DontDestroyOnLoad(gameObject);

            // 切场景 / 失焦时复位输入层。
            // 主要目的是放掉设置页可能遗留的**改键捕获模式**（挂着不放 = 整个动作层失灵）。
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.focusChanged += OnFocusChanged;

            Initialize();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            InputSystem.Reset();
        }

        private void OnFocusChanged(bool hasFocus)
        {
            if (!hasFocus)
            {
                InputSystem.Reset();
            }
        }

        private void Initialize()
        {
            Debug.Log("[GameManager] Initializing CloudEyrie...");

            // 物理分组必须在任何刚体生效之前应用
            PhysicsGroups.Apply();

            // 重力必须与速度单位配套改（Cocos 是 -320 px/s²，Unity 默认 -9.81）
            PhysicsUnits.Apply();

            // 配置就绪 —— Unity 里 SO 没有生命周期，改由这里广播
            // （Cocos 侧是 GameConfig 组件的 onLoad 广播）
            EventBus.Emit(GameEvents.GameConfigReady, GameConfig.Current);

            // 配置自检：跑一遍「数值之间的不变式」。
            // 放在这里是因为所有配置已经就位，而问题一旦存在就是**静默**的
            // （典型：灵合门槛 < 进门费 + 爆发费 → 玩家进了灵合却永远放不出爆发）。
            GameConfig.Current.Validate();

            EventBus.Emit(GameEvents.GameInitialized);

            // 灵炁当前值看游戏内 HUD（ui/GameHud）。这里**不要**挂 spirit-energy-changed 监听打日志。
            SpiritEnergySystem.ResetToInitial();

            if (debugSpiritSelfTest)
            {
                RunSpiritSelfTest();
            }
        }

        /// <summary>冒烟测试：验证消耗 / 非战斗恢复 / 战斗恢复 / 灵泉恢复四档数值。</summary>
        private void RunSpiritSelfTest()
        {
            SpiritEnergySystem.ResetToInitial();
            Debug.Log($"[SpiritEnergy][自测] 初始 -> {SpiritEnergySystem.GetCurrent()}");

            SpiritEnergySystem.Consume(30);
            Debug.Log($"[SpiritEnergy][自测] 消耗30 -> {SpiritEnergySystem.GetCurrent()}");

            SpiritEnergySystem.ClearCombatOverride();
            SpiritEnergySystem.SetInCombat(false);
            SpiritEnergySystem.Update(2f);
            Debug.Log($"[SpiritEnergy][自测] 非战斗恢复2秒(+10) -> {SpiritEnergySystem.GetCurrent()}");

            SpiritEnergySystem.SetInCombat(true);
            SpiritEnergySystem.Update(2f);
            Debug.Log($"[SpiritEnergy][自测] 战斗恢复2秒(+2) -> {SpiritEnergySystem.GetCurrent()}");

            SpiritEnergySystem.SetInSpring(true);
            SpiritEnergySystem.Update(1f);
            Debug.Log($"[SpiritEnergy][自测] 灵泉恢复1秒(+30) -> {SpiritEnergySystem.GetCurrent()}");

            SpiritEnergySystem.SetInCombat(false);
            SpiritEnergySystem.SetInSpring(false);
            SpiritEnergySystem.ClearCombatOverride();
            SpiritEnergySystem.ResetToInitial();
            Debug.Log($"[SpiritEnergy][自测] 结束，复位 -> {SpiritEnergySystem.GetCurrent()}");
        }

        /// <summary>
        /// <see cref="Resources.UnloadUnusedAssets"/> 的节流计时。
        ///
        /// 它是一次**全量扫描**（实测可到几十到上百毫秒），逐帧调会把游戏卡成幻灯片；
        /// 但它的语义是安全的 —— 只回收真的没人引用的资源，不像手动
        /// <c>Resources.UnloadAsset</c> 那样会把在用的贴图抽走（那才是卡死的原因，
        /// 见 <see cref="SequencePlayer"/> 里 ReleaseTexRef 的说明）。
        /// </summary>
        private const float CleanupInterval = 12f;

        private float _cleanupCooldown;

        private void Update()
        {
            // 全局灵炁自然恢复 / 灵泉恢复（每帧）
            SpiritEnergySystem.Update(Time.deltaTime);

            // 贴图回收：序列帧播放器放掉了引用就记个标记，这里限流地真正回收。
            //
            // ⚠️ 演出期间**不能**做 —— GameFlow 把 timeScale 置 0，而这次扫描是实打实的
            // 主线程开销，会让全屏转场卡住不动。等演出结束再说。
            _cleanupCooldown = Mathf.Max(0f, _cleanupCooldown - Time.deltaTime);

            if (SequencePlayer.CleanupRequested && _cleanupCooldown <= 0f && !GameFlow.Active)
            {
                SequencePlayer.ClearCleanupRequest();
                _cleanupCooldown = CleanupInterval;
                Resources.UnloadUnusedAssets();
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.focusChanged -= OnFocusChanged;

            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
