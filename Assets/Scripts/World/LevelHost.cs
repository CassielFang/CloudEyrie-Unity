using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudEyrie
{
    /// <summary>
    /// 关卡宿主：住在常驻的 <c>Host</c> 场景里，负责「进哪一关、怎么换」。
    ///
    /// <b>为什么要有它</b>
    ///
    /// 关卡模组化之后，「换关」不再是一次 <c>LoadScene</c> 那么简单 ——
    /// 卸载旧关卡、加载新关卡、把玩家挪到入口点、换相机边界、换背景主题、换 BGM、
    /// 复位一堆跨场景的静态状态，**顺序错了就会出一类查不出来的 bug**
    /// （灵炁值带进下一关、timeScale 永久卡 0、相机从上一关的位置飞过来）。
    /// 把这些收在一个地方，顺序才有唯一的定义。
    ///
    /// <b>为什么这些是常驻件、不放关卡里</b>：玩家 / 相机 / 背景 / HUD / 转场
    /// 每关都一样，而且玩家身上挂着灵炁值这类跨关状态 —— 放关卡场景里就得每关重建一次。
    /// 例外是 <c>Boss</c>：它在 <c>Awake</c> 里把自己的坐标记成"战场中心"
    /// （<c>BossAI._spawnX</c>），提到宿主复用会让它锁死在第一关的坐标。
    /// </summary>
    public class LevelHost : MonoBehaviour
    {
        public static LevelHost Instance { get; private set; }

        [Header("常驻引用")]
        [SerializeField] private CameraFollow cameraFollow;
        [SerializeField] private ParallaxBackground background;
        [SerializeField] private QingheController player;
        [SerializeField] private ScreenFade screenFade;

        [Header("流程")]
        [Tooltip("从标题进游戏时先开哪一关")]
        [SerializeField] private string firstLevelId = "mu";

        [Tooltip("换关时压黑 / 淡入的时长 /秒")]
        [SerializeField] private float fadeSeconds = 0.35f;

        /// <summary>当前关卡定义。尚未进关时为 null。</summary>
        public LevelDefinition Current { get; private set; }

        /// <summary>当前关卡场景名（用于卸载）。</summary>
        public string CurrentSceneName { get; private set; }

        public QingheController Player
        {
            get { return player; }
        }

        public CameraFollow Camera
        {
            get { return cameraFollow; }
        }

        public ParallaxBackground Background
        {
            get { return background; }
        }

        /// <summary>最近踩过的存档点 id（灵泉）。死亡重开回到这里。</summary>
        public string CheckpointId { get; private set; }

        private bool _swapping;

        private void Awake()
        {
            Instance = this;

            if (cameraFollow == null) cameraFollow = FindAnyObjectByType<CameraFollow>();
            if (background == null) background = FindAnyObjectByType<ParallaxBackground>();
            if (player == null) player = FindAnyObjectByType<QingheController>();

            if (cameraFollow == null || player == null)
            {
                Debug.LogError("[LevelHost] Host 场景缺少相机或玩家 —— 关卡无法定位。");
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            LevelCatalog catalog = LevelCatalog.Current;

            if (catalog != null)
            {
                catalog.Validate();
            }

            // 关卡场景**已经加载着**（关卡策划直接 Play 了某个关卡场景，
            // 由 LevelStandaloneBootstrap 把宿主补进来）→ **认领它**，
            // 而不是傻乎乎再去加载第一关 —— 那样会出现两个关卡叠在一起。
            LevelDefinition already = FindLoadedLevel(catalog);

            if (already != null)
            {
                GameLog.I($"[LevelHost] 认领已加载的关卡「{already.displayName}」（独立试玩）");
                StartCoroutine(SwapRoutine(already, null, false, skipLoad: true));
                return;
            }

            EnterLevel(firstLevelId);
        }

        /// <summary>已加载的关卡场景里，有没有哪一个是关卡目录里的关卡。</summary>
        private static LevelDefinition FindLoadedLevel(LevelCatalog catalog)
        {
            if (catalog == null || catalog.levels == null)
            {
                return null;
            }

            for (int i = 0; i < catalog.levels.Length; i++)
            {
                LevelDefinition def = catalog.levels[i];

                if (def != null && !string.IsNullOrEmpty(def.sceneName)
                    && GameSceneManager.IsLoaded(def.sceneName))
                {
                    return def;
                }
            }

            return null;
        }

        // ============================================================
        // 对外入口
        // ============================================================

        /// <summary>进入某一关。重复调用会被忽略（换关中）。</summary>
        public void EnterLevel(string levelId, string entryMarkerId = null, bool carryPlayerState = false)
        {
            if (_swapping)
            {
                return;
            }

            LevelCatalog catalog = LevelCatalog.Current;

            if (catalog == null)
            {
                Debug.LogError("[LevelHost] 没有 LevelCatalog，无法进入关卡。退回标题。");
                ReturnToTitle();
                return;
            }

            LevelDefinition def = catalog.ById(levelId);

            if (def == null)
            {
                Debug.LogError($"[LevelHost] 目录里没有 id 为「{levelId}」的关卡。退回标题。");
                ReturnToTitle();
                return;
            }

            StartCoroutine(SwapRoutine(def, entryMarkerId, carryPlayerState));
        }

        /// <summary>死亡重开：卸载并重新加载本关，落在最近踩过的存档点，灵炁回满。</summary>
        public void RestartCurrent()
        {
            if (Current == null || _swapping)
            {
                return;
            }

            SpiritEnergySystem.ResetToFull();
            StartCoroutine(SwapRoutine(Current, CheckpointId, carryPlayerState: true));
        }

        /// <summary>记录存档点（灵泉踩到时调用）。</summary>
        public void SetCheckpoint(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                CheckpointId = id;
            }
        }

        public void ReturnToTitle()
        {
            // ⚠️ 必须走复位 —— 裸 LoadScene 会把静态残留（灵炁值、战斗来源、timeScale）
            //    一路带进标题页，下次开始游戏时它们还在
            ResetProtocol.OnLeaveLevel();
            GameSceneManager.LoadSingle(GameScenes.Title);
        }

        public void GoEnding()
        {
            ResetProtocol.OnLeaveLevel();
            GameSceneManager.LoadSingle(GameScenes.Ending);
        }

        // ============================================================
        // 换关主流程 —— 顺序本身就是正确性的一部分
        // ============================================================

        /// <param name="skipLoad">
        /// 关卡场景**已经加载着**（独立试玩），跳过卸载与加载两步，
        /// 只做落位 / 换主题 / 注入执行者 / 复位。
        /// 复用一个协程而不是另写一个 AdoptLevel —— 那几段逻辑一模一样，
        /// 复制一份的话以后改落位规则要记得改两处。
        /// </param>
        private IEnumerator SwapRoutine(LevelDefinition def, string entryMarkerId,
                                        bool carryPlayerState, bool skipLoad = false)
        {
            _swapping = true;

            // 1) 先遮住再动。演出闸门同时把物理 / 敌人 / 灵炁推进全冻住，
            //    否则卸载到加载之间那一帧玩家还会继续往下掉。
            GameFlow.Push();

            if (screenFade != null)
            {
                screenFade.SetBlack();
            }

            yield return null;

            // 2) 复位协议（见 ResetProtocol 的注释，顺序有理由）
            ResetProtocol.BeforeLevelUnload();

            if (!skipLoad)
            {
                // 3) 卸旧关
                if (!string.IsNullOrEmpty(CurrentSceneName))
                {
                    AsyncOperation unload = GameSceneManager.UnloadAdditive(CurrentSceneName);

                    if (unload != null)
                    {
                        yield return unload;
                    }
                }

                // 4) 载新关
                AsyncOperation load = GameSceneManager.LoadAdditive(def.sceneName);

                if (load == null)
                {
                    Debug.LogError($"[LevelHost] 加载关卡场景「{def.sceneName}」失败 —— " +
                                   "多半是没在 Build Settings 里注册。");
                    GameFlow.Pop();
                    _swapping = false;
                    yield break;
                }

                yield return load;
            }

            Scene scene = SceneManager.GetSceneByName(def.sceneName);

            // 换到**另一关**时清掉存档点 —— 否则上一关灵泉的 checkpointId 会带进来，
            // 新关还没踩到灵泉就死亡，会被送回一个本关不存在（或不该到）的落点。
            if (!string.IsNullOrEmpty(CurrentSceneName) && CurrentSceneName != def.sceneName)
            {
                CheckpointId = null;
            }

            Current = def;
            CurrentSceneName = def.sceneName;

            // 5) 定位玩家与相机
            PlacePlayerAndCamera(scene, def, entryMarkerId);

            // 6) 换背景主题
            //
            // ⚠️ 这里必须传**相机行程**（关卡两端各让出半个视口），不是关卡两端。
            // 背景层的宽度与基准位置都是按行程反推的；传关卡两端会让 `_camMin`
            // 比真实值小半屏，图层整体偏出去一截，而且和 SetBounds 里已经设好的
            // 行程不一致 —— 表现是换关瞬间背景跳一下、关卡两端露空。
            if (background != null)
            {
                float halfView = cameraFollow != null ? cameraFollow.ViewWidth * 0.5f : 0f;
                background.ApplyTheme(def.theme, def.levelLeft + halfView, def.levelRight - halfView);
            }

            // 7) 把宿主件注入关卡里的执行者，并让它跑第一条编排
            LevelRuntime runtime = FindInScene<LevelRuntime>(scene);

            if (runtime != null)
            {
                runtime.Init(def, cameraFollow, background, player, screenFade);
            }
            else
            {
                Debug.LogWarning($"[LevelHost] 关卡场景「{def.sceneName}」里没有 LevelRuntime —— " +
                                 "这一关不会有任何编排（相机不锁、BGM 不切）。");
            }

            // 8) 收尾
            ResetProtocol.AfterLevelLoad(carryPlayerState);

            if (screenFade != null)
            {
                screenFade.FadeTo(0f, fadeSeconds);
            }

            GameFlow.Pop();
            _swapping = false;

            GameLog.I($"[LevelHost] 进入 {def.displayName}（{def.sceneName}）");
        }

        /// <summary>
        /// 把玩家挪到入口点、把相机立刻对齐过去。
        ///
        /// ⚠️ <b>相机必须瞬移，不能靠跟随追过去</b>：<c>CameraFollow</c> 是指数趋近的，
        /// 不立刻同步的话相机会从上一关的位置一路滑过来，玩家会看到一段穿帮的横向滚动。
        /// 做法是直接把 <c>cameraX</c> 设成目标位置再 <c>SetBounds</c>（后者会顺带夹紧）。
        /// </summary>
        private void PlacePlayerAndCamera(Scene scene, LevelDefinition def, string entryMarkerId)
        {
            Transform marker = null;

            if (!string.IsNullOrEmpty(entryMarkerId))
            {
                marker = FindByName(scene, entryMarkerId);
            }

            if (marker == null && !string.IsNullOrEmpty(def.defaultSpawnMarker))
            {
                marker = FindByName(scene, def.defaultSpawnMarker);
            }

            if (player == null)
            {
                return;
            }

            if (marker != null)
            {
                player.transform.position = marker.position;

                // 落点重置速度 —— 否则会带着上一关的惯性冲出去
                Rigidbody2D body = player.GetComponent<Rigidbody2D>();

                if (body != null)
                {
                    body.linearVelocity = Vector2.zero;
                }
            }
            else
            {
                Debug.LogWarning($"[LevelHost] 关卡「{def.displayName}」里找不到入口点" +
                                 $"「{entryMarkerId ?? def.defaultSpawnMarker}」，玩家停在原地。");
            }

            if (cameraFollow == null)
            {
                return;
            }

            cameraFollow.Unlock();
            cameraFollow.cameraX = player.transform.position.x;
            cameraFollow.SetBounds(def.levelLeft, def.levelRight);

            // 纵向边界：两者相等（默认）就等于关掉纵向跟随，保持旧行为
            cameraFollow.SetVerticalBounds(def.levelBottom, def.levelTop);

            // 立刻把相机纵向对到常规构图。换关/坠落重开时不能保留上一处的 y：
            // 开场 cue 会把相机锁住，锁在一个从坑底带上来的高度上。
            cameraFollow.SnapVerticalToRest();
        }

        // ============================================================
        // 场景内查找（关卡场景是刚加载的，跨场景 FindAnyObjectByType 可能找错副本）
        // ============================================================

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindByName(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindRecursive(root.transform, name);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindRecursive(Transform node, string name)
        {
            if (node.name == name)
            {
                return node;
            }

            for (int i = 0; i < node.childCount; i++)
            {
                Transform found = FindRecursive(node.GetChild(i), name);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
