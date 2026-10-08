using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 关卡段落（对照 <c>docs/大赛规划v2.0（17天版）.md</c> 的「关卡总览」表）。
    /// 剧情顺序固定，枚举顺序同时也是**推进顺序**。
    /// </summary>
    public enum LevelSegment
    {
        /// <summary>0 开场过场：远景推进 → 谷地被黑雾笼罩 → 狐鸣</summary>
        Opening,

        /// <summary>1 前奏探索：平台跳跃 → 遭遇阳浊小怪 → 战斗教学</summary>
        Explore,

        /// <summary>2 灵雾教学：灵界屏障挡路 → 切灵雾穿墙</summary>
        MistLesson,

        /// <summary>3 过渡段：短暂平静 → 灵泉恢复 → 远处九尾狐轮廓</summary>
        Approach,

        /// <summary>4 Boss P1 幻影</summary>
        BossP1,

        /// <summary>5 Boss P2 狂乱（未实现）</summary>
        BossP2,

        /// <summary>6 Boss P3 归心（未实现）</summary>
        BossP3,

        /// <summary>7 结局过场（未实现）</summary>
        Ending,
    }

    /// <summary>
    /// 关卡导演：按「关卡总览」的段落顺序推进，并搭出关卡几何与道具。
    /// 对应 Cocos 侧 <c>world/LevelDirector.ts</c>。
    ///
    /// <b>为什么有它</b>：之前直接把 Boss 摆在一个光秃秃的竞技场里，
    /// 段落 0–3（开场 / 探索 / 灵雾教学 / 过渡段）整段缺失。
    /// 这里把关卡铺开：段落按玩家 x 推进，每段有自己的相机行为。
    ///
    /// 关卡的几何与道具是**代码搭的**，不是场景里摆的 —— 一是关卡还在反复调，
    /// 摆场景里改一次要动编辑器；二是本项目根本没有 prefab，本来就得程序化建。
    /// </summary>
    public class LevelDirector : MonoBehaviour
    {
        // ---- 关卡横向布局（世界坐标，x 向右） ----
        public const float LevelLeft = 0f;
        public const float LevelRight = 3200f;

        /// <summary>
        /// 地面**节点**的 y（不是顶边）。
        ///
        /// ⚠️ 原注释写的是「地面碰撞体顶边所在的 y」，那是错的：场景里 <c>Ground</c> 的
        /// <c>BoxCollider2D</c> 是 4400×80、节点在 −335、offset 为 0，所以
        /// **顶边其实是 −295**（见 <see cref="GroundTopY"/>）。
        /// 小怪的出生高度等一堆数值是按 −335 这个"节点 y"算的，所以值不动，只纠正注释。
        /// </summary>
        public const float GroundY = -335f;

        /// <summary>
        /// 物理地面的**顶边** y。道具要「放在地上」一律对齐这条线，别用 <see cref="GroundY"/>。
        /// </summary>
        public const float GroundTopY = -295f;

        /// <summary>玩家出生点</summary>
        public const float SpawnX = 200f;

        /// <summary>Boss 战场中心</summary>
        public const float ArenaX = 2600f;

        private struct SegmentEntry
        {
            public LevelSegment Seg;
            public float AtX;
            public string Note;
        }

        /// <summary>各段落的推进阈值：玩家 x 越过就进入该段。</summary>
        private static readonly SegmentEntry[] SegmentOrder =
        {
            new SegmentEntry { Seg = LevelSegment.Opening, AtX = float.NegativeInfinity, Note = "开场过场" },
            new SegmentEntry { Seg = LevelSegment.Explore, AtX = 420f, Note = "前奏探索：阳浊小怪" },
            new SegmentEntry { Seg = LevelSegment.MistLesson, AtX = 1180f, Note = "灵雾教学：灵界屏障" },
            new SegmentEntry { Seg = LevelSegment.Approach, AtX = 1780f, Note = "过渡段：灵泉" },
            new SegmentEntry { Seg = LevelSegment.BossP1, AtX = 2320f, Note = "Boss P1 幻影" },
        };

        [Header("引用")]
        [Tooltip("Boss 节点：进入 Boss 战场时由本组件唤醒它")]
        [SerializeField] private BossAI boss;

        [Tooltip("背景（视差）。与 CameraFollow 用的是同一个")]
        [SerializeField] private ParallaxBackground background;

        [Tooltip("场景里的地面节点（必须已存在，不能运行时新建）")]
        [SerializeField] private GameObject ground;

        [Tooltip("地面美术条。会被横向拉满整关 —— 原图只有一屏宽，不够长会在关卡首尾露空")]
        [SerializeField] private Transform groundArt;

        // ---- 关卡道具的摆放参数 ----
        //
        // ⚠️ 这些原图都是**打印级尺寸**，而且四周有大量透明留白（肉眼看不出来）。
        // 所以摆放不能拍脑袋给 y，要按「内容底边落在哪」反推：
        //   节点 y = GroundTopY + 内容底边距图心(px) × 缩放 − 压进地面多少
        // 下面几个「内容」常量是按 alpha>8 量出来的。**换素材必须重新量。**

        /// <summary>屏障原图内容：底边距图心 488px、高 972px</summary>
        private const float BarrierContentBottom = 488f;

        /// <summary>灵泉原图内容：底边距图心 444px、高 857px</summary>
        private const float SpringContentBottom = 444f;

        [Header("灵界屏障（段落2）")]
        [Tooltip("门的 x")]
        public float barrierX = 1400f;

        [Tooltip("门的缩放。原图 846×1062、内容高 972 —— 0.56 约 545 世界单位高")]
        public float barrierArtScale = 0.56f;

        [Tooltip("门框底边压进地面多少（世界单位）")]
        public float barrierGroundSink = 30f;

        [Tooltip("挡路碰撞体的宽度 = 门左右两半的交接缝")]
        public float barrierSeamWidth = 70f;

        [Tooltip("碰撞体比美术再高出多少 —— 防止二段跳翻过门顶、绕过灵雾教学")]
        public float barrierExtraHeight = 120f;

        [Header("灵泉石台")]
        [Tooltip("灵泉的 x（过渡段）")]
        public float springX = 1950f;

        [Tooltip("灵泉的缩放。原图 1605×1047、内容高 857。" +
                 "⚠️ 改它不算完：走近回灵炁的触发半径、狐火的禁入半径都跟着它算，" +
                 "碰撞体多边形随节点缩放自动跟，但**落地位置**由 SpringContentBottom × 它 反推" +
                 "（见 MakeSpring）。2026-10-07 按反馈在 0.29 基础上缩到 0.8 倍 → 0.232")]
        public float springArtScale = 0.232f;

        [Tooltip("灵泉底座压进地面多少（世界单位）")]
        public float springGroundSink = 30f;


        [Header("流程")]
        [Tooltip("开局自动搭关卡")]
        public bool autoBuild = true;

        // ---- 开场过场（段落0，对照 docs/大赛规划v2.0 的「段落0」表）----
        //
        // 文档写的是 40 秒，分四拍：远景推进(0-12) → 青禾走入(12-24) → 镜头拉远(24-32)
        // → 狐鸣、停步、交还操作(32-40)。
        //
        // ⚠️ **现在撑不满 40 秒**：莹翳立绘、黑雾笼罩、狐鸣音效都还没有，
        //    真正能演的只有「远景推进 + 角色走进来」那一拍。先按 14 秒左右跑，
        //    等美术与音频到位再把 openingDuration 放开到 40。
        [Header("开场过场（段落0）")]
        [Tooltip("开场总时长。文档写 40 秒，但目前内容只能撑到 ~14 秒")]
        public float openingDuration = 14f;

        [Tooltip("从全黑淡入的时长")]
        public float openingFadeIn = 2.5f;

        /// <summary>
        /// 开场是否让青禾「自己走进来」（文档段落0「青禾从左侧走入」）。
        ///
        /// ⚠️ **默认关。** 打开的话角色会在开场里自动向右走 —— 但青禾目前只有一条
        /// 待机动画，没有行走动画，一段静止立绘滑过去看起来就是「莫名其妙自己被推着走」
        /// （实测反馈「角色自动向右移动」）。等行走动画接上、或者开场真做成镜头过场再开。
        /// </summary>
        [Tooltip("开场自动行走（需要行走动画才好看，当前默认关）")]
        public bool openingAutoWalk;

        [Tooltip("自动行走的起止时间（相对开场开始），仅在 openingAutoWalk 打开时有效")]
        public float openingWalkFrom = 3f;
        public float openingWalkTo = 10f;

        [Tooltip("自动行走走到这个 x 就停，免得开场还没完就把玩家送进后面的段落")]
        public float openingWalkMaxX = 560f;

        /// <summary>当前段落</summary>
        [System.NonSerialized] public LevelSegment Segment = LevelSegment.Opening;

        /// <summary>段落变化回调（Boss AI 等挂上来）</summary>
        public System.Action<LevelSegment> OnSegmentChanged;

        private QingheController _player;
        private CameraFollow _camera;
        private float _openTimer;
        private bool _built;

        /// <summary>段落2 的灵界屏障碰撞体</summary>
        private BoxCollider2D _barrierCol;

        /// <summary>全部灵泉的位置（过渡段一座 + Boss 战场一座）</summary>
        private readonly System.Collections.Generic.List<Vector2> _springPositions =
            new System.Collections.Generic.List<Vector2>();

        /// <summary>
        /// 灵泉位置的**静态只读副本**，给不持有 LevelDirector 引用的组件用
        /// （目前是 <see cref="FogFire"/> 的回避逻辑）。
        ///
        /// 每关重建时刷新。**同一时刻只有一个关卡**，所以静态表是安全的 ——
        /// 但切场景时要在 <see cref="BuildLevel"/> 里先清空，否则会残留上一关的坐标。
        /// </summary>
        public static readonly System.Collections.Generic.List<Vector2> Springs =
            new System.Collections.Generic.List<Vector2>();

        // ---- 灵泉的两个半径 ----
        //
        // 两个都**以石台本身为基准**算，不写死、也不靠「标定缩放」那种二次换算：
        //
        //     石台半宽  = SpringOutline 里 |x| 的最大值 × springArtScale
        //     回灵炁半径 = 石台半宽 × SpringTriggerRadiusMult
        //     禁入外沿   = 石台半宽 × springAvoidRadiusMult
        //
        // 之所以锚在**轮廓数据**上而不是某个手填的常量：石台换素材 / 调缩放的次数不会少，
        // 而这两个圈一旦没跟着动，症状都是「看着没碰着泉水，判定却已经算了」——
        // 玩家在泉外面就回灵炁、狐火躲到泉外面去。都不报错，只有对着画面才看得出来。
        // 轮廓变了半宽就变，两个圈自动跟着走。

        /// <summary>
        /// 石台内容半宽（**原图像素**）—— 直接取 <see cref="SpringOutline"/> 里 |x| 的最大值。
        ///
        /// 轮廓本来就是「照着贴图 alpha 量的」，拿它当半宽比再填一个 733 之类的魔数可靠：
        /// 以后重跑轮廓（换素材）时这个值自动更新，两个半径也跟着更新。
        /// </summary>
        private static float SpringOutlineHalfWidthPx()
        {
            float m = 0f;

            for (int i = 0; i < SpringOutline.Length; i++)
            {
                float ax = Mathf.Abs(SpringOutline[i].x);

                if (ax > m)
                {
                    m = ax;
                }
            }

            return m;
        }

        /// <summary>石台在世界里的半宽（= 轮廓半宽 × <see cref="springArtScale"/>）</summary>
        private float SpringHalfWidth
        {
            get { return SpringOutlineHalfWidthPx() * springArtScale; }
        }

        [Tooltip("回灵炁的触发半径 = 石台半宽 × 这个倍数。" +
                 "0.70 ≈ 只认「踩在石台上」，再往外就够不着了")]
        public float springTriggerRadiusMult = 0.70f;

        [Tooltip("狐火禁入圈的外沿 = 石台半宽 × 这个倍数。" +
                 "⚠️ 这是「排斥力衰减到 0」的外圈，狐火实际能贴近到 0.375×它（见 FogFire 的算式）。" +
                 "要让狐火停在石台轮廓之外，这个倍数得 ≥ 1/0.375 = 2.67")]
        public float springAvoidRadiusMult = 2.7f;

        /// <summary>
        /// 灵泉的**禁入半径外沿** —— 狐火（<see cref="FogFire"/>）的排斥力到这里衰减到 0。
        ///
        /// 由 <see cref="BuildLevel"/> 按石台半宽算好发布出去
        /// （FogFire 不持有 LevelDirector 引用，跟 <see cref="Springs"/> 是同一套办法）。
        ///
        /// ⚠️ 这是「排斥力为 0」的外圈，**不等于**狐火实际能贴到多近 ——
        /// 排斥是软的（强度 1.6、线性衰减），玩家的拉力是单位向量，
        /// 所以它实际能推进到 <b>0.375 × 本值</b>处。见 <see cref="FogFire.SpringAvoidRadius"/> 的算式。
        /// </summary>
        public static float SpringAvoidRadius = 260f;

        /// <summary>
        /// 走近灵泉的触发半径（回灵炁的范围）。
        ///
        /// 它原来是个写死的 150：石台缩小之后触发圈还是原来那么大 ——
        /// 表现是「人还站在泉水外面好几步，灵炁就已经在回了」，
        /// 不报错、也不算崩，只是手感不对，很难往「半径没跟着缩」上想。
        /// 现在锚在石台半宽上，缩放怎么变都跟着走。
        /// </summary>
        private float SpringTriggerRadius
        {
            get { return SpringHalfWidth * springTriggerRadiusMult; }
        }

        private bool _inSpring;

        /// <summary>本关建出来的小怪 —— 屏障的解锁条件要看它们死完没有</summary>
        private readonly System.Collections.Generic.List<EnemyAI> _minions =
            new System.Collections.Generic.List<EnemyAI>();

        /// <summary>
        /// 已生成的小怪数，用来给小怪逐只错开绘制层级。
        /// 为什么要错开见 <see cref="SortingOrders.Minion"/> —— 层级相同时重叠会闪。
        /// </summary>
        private int _minionDrawn;

        /// <summary>过场用的全屏黑幕（开场淡入 / 将来结局淡出）</summary>
        private ScreenFade _fade;

        /// <summary>缓存的形态机。⚠️ 别在 Update 里 `GetComponent` —— 那是每帧一次的场景查找</summary>
        private CompanionStateMachine _playerForms;
        private static PhysicsMaterial2D _sharedLowFriction;

        private void Awake()
        {
            _player = FindAnyObjectByType<QingheController>();
            _playerForms = _player != null ? _player.GetComponent<CompanionStateMachine>() : null;
            _camera = GetComponent<CameraFollow>();

            // ⚠️ ParallaxBackground 挂在**背景节点**上，不在本组件所在的节点上
            // （它是景物层，要在小怪/Boss 之下）。早先 Cocos 那边这里写的是
            // `getComponent(ParallaxBackground)` —— 恒为 null，于是 autoPanSpeed 从来没被设过，
            // 开场过场与 Boss 战场的「背景缓慢平移」全程失效（表现就是「远景不动」）。
            if (background == null)
            {
                background = FindAnyObjectByType<ParallaxBackground>();
            }

            if (background == null)
            {
                Debug.LogWarning("[Level] 找不到 ParallaxBackground，背景不会平移");
            }

            if (_player == null)
            {
                Debug.LogWarning("[Level] 找不到玩家，段落推进不会生效");
                return;
            }

            SetupPhysics();

            if (_camera != null)
            {
                _camera.SetBounds(LevelLeft, LevelRight);
            }

            if (autoBuild)
            {
                BuildLevel();
            }

            // 黑幕要在 EnterSegment(Opening) 之前建好 —— 那一步会立刻把它压成全黑
            GameObject fadeGo = new GameObject("ScreenFade");
            fadeGo.transform.SetParent(transform, false);
            _fade = fadeGo.AddComponent<ScreenFade>();

            EnterSegment(LevelSegment.Opening);
        }

        public LevelSegment GetSegment()
        {
            return Segment;
        }

        private void Update()
        {
            if (_player == null)
            {
                return;
            }

            UpdateMistLesson();
            UpdateSpring();

            // 兜底：只要不在开场段，脚本方向就必须是 0。
            // 正常路径由 EnterSegment(Explore) 清掉，但那是「某个分支记得清」式的写法 ——
            // 漏一次玩家就会被永远推着往右走，而这种 bug 从现象几乎指不回原因。
            if (Segment != LevelSegment.Opening && _player != null && _player.ScriptedMoveX != 0f)
            {
                _player.ScriptedMoveX = 0f;
            }

            // 开场过场：分拍推进，期间由脚本接管移动（见 QingheController.ScriptedMoveX）
            if (Segment == LevelSegment.Opening)
            {
                UpdateOpening(Time.deltaTime);
                return;
            }

            if (Segment == LevelSegment.BossP1)
            {
                return;                       // 交给 Boss AI
            }

            float x = _player.transform.position.x;

            // 取「已越过的最后一段」
            LevelSegment want = Segment;

            foreach (SegmentEntry s in SegmentOrder)
            {
                if (x >= s.AtX)
                {
                    want = s.Seg;
                }
            }

            if (want != Segment && IsAfter(want, Segment))
            {
                AdvanceTo(want);
            }
        }

        /// <summary>
        /// 开场过场的分拍推进。
        ///
        /// 走 <see cref="QingheController.ScriptedMoveX"/> 而不是直接改 Transform ——
        /// 角色仍然走正常的移动代码，朝向、着地、形态门控都在，将来接了行走动画也会播。
        /// </summary>
        private void UpdateOpening(float dt)
        {
            _openTimer += dt;

            if (_player != null)
            {
                bool walking = openingAutoWalk
                               && _openTimer >= openingWalkFrom
                               && _openTimer < openingWalkTo
                               && _player.transform.position.x < openingWalkMaxX;

                _player.ScriptedMoveX = walking ? 1f : 0f;
            }

            if (_openTimer >= openingDuration)
            {
                AdvanceTo(LevelSegment.Explore);
            }
        }

        // ============================================================ 物理

        /// <summary>
        /// 配好物理分组：让玩家与小怪**彼此不阻挡**，但命中盒仍能打到小怪。
        /// 详见 <see cref="PhysicsGroups"/>（为什么不能简单地把敌人做成触发器）。
        /// </summary>
        private void SetupPhysics()
        {
            PhysicsGroups.Apply();

            // 玩家身体
            BoxCollider2D body = _player.GetComponent<BoxCollider2D>();

            if (body != null)
            {
                body.gameObject.layer = PhysicsGroups.Player;
            }

            // 攻击命中盒（CombatController 会去找这个子节点）
            Transform hbNode = _player.transform.Find("AttackHitBox");

            if (hbNode != null)
            {
                hbNode.gameObject.layer = PhysicsGroups.HitBox;
            }
            else
            {
                Debug.LogWarning("[Level] 没找到 AttackHitBox，玩家将打不到任何敌人");
            }

            // 三形态的环绕粒子：实体不带环、灵雾与灵合各绕一圈不同的墨。
            // 青禾的骨骼动画还没做完，这是**唯一能在轮廓之外区分形态**的手段。
            if (_player.GetComponent<FormAura>() == null)
            {
                _player.gameObject.AddComponent<FormAura>();
            }

            Debug.Log($"[Level] 物理分组 身体={(body != null ? "PLAYER" : "?")} " +
                      $"命中盒={(hbNode != null ? "HITBOX" : "?")}");
        }

        /// <summary>玩家当前形态（灵雾态能穿过灵界屏障）</summary>
        private CompanionForm? PlayerForm()
        {
            return _playerForms != null ? _playerForms.GetCurrentForm() : (CompanionForm?)null;
        }

        /// <summary>
        /// 段落2：灵界屏障挡路。
        ///
        /// 规则：
        /// <list type="bullet">
        ///   <item><b>场上还有小怪时，屏障谁都拦</b> —— 连灵雾形态也穿不过去。
        ///     必须先把小怪清完，「灵雾穿墙」才解锁。</item>
        ///   <item>小怪清完后：实体态仍是墙、灵雾态放行 —— 这才是「灵雾穿墙」的教学。</item>
        /// </list>
        ///
        /// 屏障碰撞体是运行时建的，位置不动、只切 <c>enabled</c>。
        /// </summary>
        private void UpdateMistLesson()
        {
            if (_player == null || _barrierCol == null)
            {
                return;
            }

            bool mistCanPass = MinionsAllCleared() && PlayerForm() == CompanionForm.Mist;
            _barrierCol.enabled = !mistCanPass;
        }

        /// <summary>场上小怪是否已全部清空（含已被销毁的）。</summary>
        private bool MinionsAllCleared()
        {
            foreach (EnemyAI m in _minions)
            {
                // 已销毁 / 死亡表现播完被 SetActive(false) 的，都算清掉
                if (m == null || !m.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (m.GetState() != EnemyState.Dead)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>段落3：靠近灵泉石台回灵炁。</summary>
        private void UpdateSpring()
        {
            if (_player == null)
            {
                return;
            }

            Vector3 p = _player.transform.position;
            bool near = false;
            float radius = SpringTriggerRadius;
            float radiusSq = radius * radius;

            // 逐座检查：竞技场那座的恢复是 P3 循环的必需品，漏检会让 Boss 战卡死
            foreach (Vector2 s in _springPositions)
            {
                float dx = p.x - s.x;
                float dy = p.y - s.y;

                if (dx * dx + dy * dy <= radiusSq)
                {
                    near = true;
                    break;
                }
            }

            if (near != _inSpring)
            {
                _inSpring = near;
                SpiritEnergySystem.SetInSpring(near);
                Debug.Log($"[Level] 灵泉{(near ? "范围内" : "范围外")}");
            }
        }

        // ============================================================ 段落推进

        /// <summary>只看推进序，防止玩家往回走时倒退段落。</summary>
        private static bool IsAfter(LevelSegment a, LevelSegment b)
        {
            int ia = -1;
            int ib = -1;

            for (int i = 0; i < SegmentOrder.Length; i++)
            {
                if (SegmentOrder[i].Seg == a)
                {
                    ia = i;
                }

                if (SegmentOrder[i].Seg == b)
                {
                    ib = i;
                }
            }

            return ia > ib;
        }

        private void AdvanceTo(LevelSegment seg)
        {
            string note = "";

            foreach (SegmentEntry s in SegmentOrder)
            {
                if (s.Seg == seg)
                {
                    note = " —— " + s.Note;
                    break;
                }
            }

            Debug.Log($"[Level] 进入段落：{seg}{note}");
            EnterSegment(seg);
        }

        private void EnterSegment(LevelSegment seg)
        {
            Segment = seg;

            // 音乐跟着段落走：只有 Boss 段换成 Boss 曲，其余（开场 / 探索 / 教学 / 过渡）
            // 一律回探索曲。AudioManager 对「同一首正在播」是空操作，
            // 所以这几个段落之间来回切不会把音乐打断重来。
            GameAudio.PlayBgm(seg == LevelSegment.BossP1 ? BgmTrack.Boss : BgmTrack.Explore);

            switch (seg)
            {
                case LevelSegment.Opening:
                    // 开场：相机钉在起点，背景缓慢推移（卷轴感）
                    _camera?.Lock();

                    if (background != null)
                    {
                        background.autoPanSpeed = 18f;
                    }

                    // 从全黑淡入 —— 文档「镜头缓慢下推穿过云层」那一拍的廉价等价物，
                    // 但至少不是硬切进来
                    if (_fade != null)
                    {
                        _fade.SetBlack();
                        _fade.FadeTo(0f, openingFadeIn);
                    }

                    break;

                case LevelSegment.Explore:
                    _camera?.Unlock();

                    if (background != null)
                    {
                        background.autoPanSpeed = 0f;
                    }

                    // 交还移动控制权。⚠️ 必须清掉 —— 漏了的话玩家会被一直推着往右走
                    if (_player != null)
                    {
                        _player.ScriptedMoveX = 0f;
                    }

                    break;

                case LevelSegment.BossP1:
                    // Boss 战场是固定场地：锁相机 + 背景重新缓慢平移
                    _camera?.Lock();

                    if (background != null)
                    {
                        background.autoPanSpeed = 12f;
                    }

                    // 唤醒 Boss。BossAI 的 autoStart 要关掉，开打时机由关卡流程决定
                    if (boss != null)
                    {
                        boss.Begin();
                    }
                    else
                    {
                        Debug.LogWarning("[Level] 找不到 BossAI，Boss 不会开打");
                    }

                    break;
            }

            OnSegmentChanged?.Invoke(seg);
        }

        // ============================================================ 搭关卡

        private void BuildLevel()
        {
            if (_built)
            {
                return;
            }

            _built = true;

            // 静态表要跟本关重建 —— 残留上一关的坐标会让狐火在空地上乱躲
            Springs.Clear();

            // 灵泉的两个半径都以石台半宽为基准（见 SpringTriggerRadius / springAvoidRadiusMult）。
            // 石台是运行时按 springArtScale 摆的，所以这两个数也得在这里算、不能写死。
            SpringAvoidRadius = SpringHalfWidth * springAvoidRadiusMult;

            // ⚠️ **先把「在灵泉范围内」这一位清掉**（它住在 SpiritEnergySystem 里，是 static，
            // 会跨场景活着）。
            //
            // 泄漏是怎么发生的：下面 UpdateSpring 的判据是「near 变了才通知」——
            // 而本实例的 _inSpring 每关重建都是 false。玩家要是**在泉里**切了场景，
            // 静态那一位停在 true；新场景第一帧算出 near=false，与实例的 false 相同
            // → **一句通知都不发** → 静态位永远是 true。
            //
            // 后果不是「回炁快一点」那么轻：走灵泉反而成了永久负面状态 ——
            // 全图 30/s 回炁，而且 `FireMistShot` 里那句「泉中禁发灵弹」会**永久禁掉灵弹**
            // （表现：灵雾形态完全打不出子弹），且没有任何自愈路径。
            _inSpring = false;
            SpiritEnergySystem.SetInSpring(false);

            // 地面**不在这里造**，用的是场景里那块 Ground。
            //
            // ⚠️ 为什么必须是场景节点：QingheController.groundNode 是 Inspector 上的
            // **对象引用**，着地判定写死了「碰到的对象 == groundNode」。
            // 运行时新建的地面它指不到 —— grounded 永远是 false、jumpCount 就永远不重置，
            // 表现是「跳两次之后再也跳不起来」。
            if (ground == null)
            {
                GameObject found = GameObject.Find("Ground");

                if (found != null)
                {
                    ground = found;
                }
                else
                {
                    Debug.LogWarning("[Level] 场景里找不到 Ground —— 角色会掉下去、也跳不起来");
                }
            }

            // 地面美术横向拉满整关，**并且挪到和碰撞体同一段区间**。
            //
            // ⚠️ 只调缩放不挪位置会出事：地面美术原本在 x=0，按锚点缩放后覆盖 −1603~+1603；
            // 而地面碰撞体在 x=1600、覆盖 0~3200。两者只在 0~1603 重叠，于是
            //   · 左段 −1603~0：有贴图没碰撞体 → 看着是实的，踩上去掉下去；
            //   · 右段 1603~3200：有碰撞体没贴图 → 悬空行走，看着像地面断了。
            // Cocos 侧实测反馈正是「地面有的地方实有的地方虚、还不连续」。
            if (groundArt != null)
            {
                SpriteRenderer sr = groundArt.GetComponent<SpriteRenderer>();

                if (sr != null && sr.sprite != null)
                {
                    float srcW = sr.sprite.bounds.size.x;

                    if (srcW > 0f)
                    {
                        float k = (LevelRight - LevelLeft) / srcW;
                        Vector3 p = groundArt.position;
                        groundArt.localScale = new Vector3(k, 1f, 1f);
                        groundArt.position = new Vector3(
                            (LevelLeft + LevelRight) / 2f, p.y, p.z);

                        Debug.Log($"[Level] 地面美术 ×{k:F2} 并居中到 x={(LevelLeft + LevelRight) / 2f:F0}" +
                                  $"（原生 {srcW:F0} → {LevelRight - LevelLeft:F0}）");
                    }
                }
            }

            // 段落1 前奏探索的几级石台 —— **暂时不做**。
            // 之前放了三个「只有碰撞体、没有贴图」的平台，角色撞上去就是撞空气，
            // 实测反馈是「莫名其妙看不到的障碍」。平台必须先有美术才有意义。

            // 段落1：两只阳浊小怪。
            //
            // ⚠️ 位置要**离出生点（x=200）比远程视野（690）更远** —— 否则玩家一进场就被
            // 发现、立刻挨一箭（实测反馈「一启动立刻就冲我射一箭」）。
            // 序号同时充当**性格种子**（见 Personality）—— 两只小怪数值完全相同、
            // 但反应快慢 / 停留长短 / 攻击间隔 / 巡逻偏好各不相同。
            _minions.Add(MakeMinion(950f, GroundY + 126f, "小怪A", 1));
            _minions.Add(MakeMinion(1150f, GroundY + 126f, "小怪B", 2));

            // ---- 段落2：灵界屏障 ----
            //
            // 屏障本质是**门**：左半边在角色之后、右半边在角色之前，
            // 角色穿过时才像是「走进门里」。
            //
            // y 由「内容底边落到地面线」反推（见上面 BarrierContentBottom 的说明）。
            float barrierY = GroundTopY + BarrierContentBottom * barrierArtScale - barrierGroundSink;

            MakePropHalf("场景小物件/灵界屏障（门）", barrierX, barrierY, barrierArtScale,
                "Barrier", true, SortingOrders.LevelProp);
            MakePropHalf("场景小物件/灵界屏障（门）", barrierX, barrierY, barrierArtScale,
                "BarrierFront", false, SortingOrders.BarrierFront);

            // ⚠️ 碰撞体挂在**单独一个节点**上，不能挂左半 / 右半。
            //
            // 两半为了让它们分处角色前后，各自被挪开了**半个宽度**。挂在左半节点上的话，
            // 碰撞体中心会跟着跑到门的**左半边** —— 从左边撞上来是「走进门里」的样子，
            // 但从右边过来要先穿进门的右半边 360 单位才被挡住，看着像从门里穿过去
            // （实测反馈「从左到右做的不错，从右到左就一般」）。
            GameObject blocker = new GameObject("BarrierBlocker");

            // ⚠️ **必须归到 WORLD 层。** 留在默认的 Default 层的话，
            // `PhysicsGroups.Apply()` 会把 5 个游戏层与「其它一切层」的碰撞全部断开，
            // 这个屏障对**谁都拦不住** —— 玩家和小怪都能直接穿过去。
            blocker.layer = PhysicsGroups.World;
            blocker.transform.SetParent(transform, false);
            blocker.transform.position = new Vector3(barrierX, barrierY, 0f);

            BoxCollider2D col = blocker.AddComponent<BoxCollider2D>();

            // 尺寸 = 门左右两半的**交接缝**那根长条，不是整扇门的宽度。
            // 缝对齐到门中心后，两半各自露在角色身前/身后，角色能一直走到门框里
            // ——「走进门里」的前后关系才成立。
            // 高度比美术再高出 100：否则门顶留在屏幕内、二段跳能翻过去绕过灵雾教学。
            col.size = new Vector2(barrierSeamWidth,
                                   BarrierContentBottom * barrierArtScale + barrierExtraHeight);
            col.sharedMaterial = LowFrictionMaterial();
            _barrierCol = col;

            // 灵界机关**暂时不放** —— 需求方明确说目前不需要。
            // 「灵雾穿墙」这个教学本身不依赖机关：屏障对实体态是墙、对灵雾态放行就够了。

            // ---- 段落3：灵泉石台（靠近回灵炁） ----
            //
            // ⚠️ **整关只有这一座。**
            //
            // 曾经在 Boss 战场里另加了一座（文档「场地左有一个灵泉石台」），结果两座
            // 相距只有 500 出头、在竞技场锁定视角（视野 [1680, 2960]）里**同屏可见**，
            // 看起来像刷重了。实际上一个位置就够：放在 1950 既在段落3的推进路线上
            // （文档「路旁有灵泉石台」），也落在竞技场视野的左半（文档的「场地左」），
            // P3 那条「灵炁耗尽 → 回灵泉恢复 → 再次灵合」的循环同样跑得通。
            _springPositions.Add(MakeSpring(springX, "Spring").transform.position);
            Springs.Add(_springPositions[_springPositions.Count - 1]);

            Debug.Log($"[Level] 关卡已搭建：灵界屏障({barrierArtScale}×) + 灵泉×{_springPositions.Count} + 两只小怪");
        }

        /// <summary>
        /// 「和地面一样」的物理材质。
        ///
        /// ⚠️ 地面的碰撞体**没有指定材质**（场景里没有 <c>m_Material</c>，工程里也没有
        /// 任何 <c>PhysicsMaterial2D</c> 资源），所以它用的是 Unity 的**默认材质**：
        /// 摩擦 <b>0.4</b>、弹性 0。这里照抄那两个数值，而不是去引用一个并不存在的资源。
        /// </summary>
        private static PhysicsMaterial2D GroundLikeMaterial()
        {
            if (_sharedGroundLike == null)
            {
                _sharedGroundLike = new PhysicsMaterial2D("CloudEyrieGroundLike")
                {
                    friction = 0.4f,
                    bounciness = 0f,
                };
            }

            return _sharedGroundLike;
        }

        private static PhysicsMaterial2D _sharedGroundLike;

        /// <summary>低摩擦材质（Cocos 那边是 <c>col.friction = 0</c>，Unity 走 PhysicsMaterial2D）。</summary>
        private static PhysicsMaterial2D LowFrictionMaterial()
        {
            if (_sharedLowFriction == null)
            {
                _sharedLowFriction = new PhysicsMaterial2D("CloudEyrieLowFriction")
                {
                    friction = 0f,
                    bounciness = 0f,
                };
            }

            return _sharedLowFriction;
        }

        /// <summary>
        /// 造一只阳浊小怪：<see cref="EnemyAI"/> 要求同节点有 <see cref="Rigidbody2D"/> +
        /// <see cref="Damageable"/>，外加一个 Art 子节点给它翻转朝向；
        /// 表现交给 <see cref="MinionVisual"/> 按状态切序列帧。
        /// </summary>
        private EnemyAI MakeMinion(float x, float y, string name, int personalitySeed)
        {
            GameObject n = new GameObject(name);
            n.transform.SetParent(transform, false);
            n.transform.position = new Vector3(x, y, 0f);
            n.layer = PhysicsGroups.Enemy;

            Rigidbody2D rb = n.AddComponent<Rigidbody2D>();

            // ⚠️ **要有重力**（和青禾一致），不能是 0。
            // gravityScale = 0 时 Y 方向一旦被赋过速度就再也回不来 —— 表现是
            // 「小怪走着走着飘起来」（实测反馈）。有重力才会被地面接住。
            rb.gravityScale = 1f;
            rb.freezeRotation = true;
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;

            // ⚠️ 碰撞体**必须比 attackRange(72) 的一半小**。
            // 小怪是「走到攻击距离就停下开打」的，碰撞体一旦宽过两倍攻击距离，
            // 它就会一边「停在攻击距离」一边把玩家顶开 —— 实测「人物被小怪挤跑」。
            BoxCollider2D col = n.AddComponent<BoxCollider2D>();
            col.size = new Vector2(80f, 130f);

            // ⚠️ 碰撞体相对节点**下移 21**，不是 0。
            // 上面那个 y（GroundY+126）是按「脚底正好压在地面美术上沿」实测调出来的，
            // 而它当时靠 gravityScale = 0 悬在半空才成立 —— 碰撞体底边比地面高 21。
            // 现在改成受重力，节点会自己落下来 21 单位，脚就压进地面色带里了。
            // 把碰撞体下移同样的量，落点就回到原来的位置。
            col.offset = new Vector2(0f, -21f);

            PhysicsMaterial2D mat = new PhysicsMaterial2D($"{name}Mat")
            {
                friction = 0.9f,
                bounciness = 0f,
            };
            col.sharedMaterial = mat;

            n.AddComponent<Damageable>();

            // ⚠️ Art 子节点必须在 EnemyAI **之前**建好。
            // EnemyAI.Awake 里 `transform.Find("Art")` 是一次性的，顺序反了就拿不到、
            // 朝向翻转整个失效 —— 表现是「小怪不会转身」（Cocos 侧踩过）。
            GameObject art = new GameObject("Art");
            art.transform.SetParent(n.transform, false);

            SpriteRenderer sr = art.AddComponent<SpriteRenderer>();

            // ⚠️ 逐只错开一层，不能都用 SortingOrders.Minion。
            //
            // 两只小怪层级相同时，Unity 只能按「与相机的距离」排序；它们 z 都是 0、
            // 距离相等 → 排序结果不稳定，重叠时看着像**互相穿透**并随移动闪。
            // 按生成序号各 +1，「谁恒定盖在谁上面」就是确定的（后生成的在上）。
            sr.sortingOrder = SortingOrders.Minion + _minionDrawn++;

            EnemyAI ai = n.AddComponent<EnemyAI>();

            // 远程型：攻击距离取 RangedAttackRange，且攻击时射出箭的抛射物，
            // 而不是近身直接结算 —— 它的攻击动画是**拉弓射箭**
            ai.Ranged = true;

            // 真美术已接入，关掉灰盒的按状态染色（否则攻击时整只小怪变红）
            ai.tintByState = false;

            // 性格种子：让两只「一样强、不一样」—— 见 Personality
            ai.personalitySeed = personalitySeed;

            // 两条动画现在都朝右：行走那条本来就是，攻击那条在流水线里镜像过
            // （美术组交的两条视频左右相反，见 gen_video_fx.py 的说明）
            ai.artNaturalFacing = NaturalFacing.Right;

            // artScale 乘的是**打包后的**帧尺寸（512 × 0.5 = 256），主体约占画布 72%。
            // 原定 1.1（约 200 世界单位高）实测偏大，按反馈缩到 0.6 倍 → 0.66
            MinionVisual mv = n.AddComponent<MinionVisual>();
            mv.artScale = 0.66f;

            return ai;
        }

        /// <summary>
        /// 摆「门」的**半边** —— 左半 / 右半各一个节点，好让它们分处角色的前后两层。
        ///
        /// 做法：加载整图后按纹理的 rect 各取一半。
        ///
        /// <b>⚠️ 坐标约定差异</b>：Cocos 的 <c>SpriteFrame.rect</c> 是**左上原点**，
        /// Unity 的 <see cref="Sprite.rect"/> 是**左下原点**。这里只沿 **X 轴**对半切、
        /// Y 方向取整幅，所以两种约定下 X 的含义一致（都从左边量），**不需要翻转**
        /// —— 但没有翻转的正确性依赖于「Y 取整幅」这个前提，改成上下切时要当心。
        /// </summary>
        private GameObject MakePropHalf(string path, float x, float y, float scale,
                                        string name, bool isLeft, int sortingOrder)
        {
            GameObject n = new GameObject(name);
            n.transform.SetParent(transform, false);
            n.transform.position = new Vector3(x, y, 0f);

            SpriteRenderer sr = n.AddComponent<SpriteRenderer>();
            sr.sortingOrder = sortingOrder;

            Sprite full = Resources.Load<Sprite>(path);

            if (full == null)
            {
                Debug.LogWarning($"[Level] 道具读不到 {path}");
                return n;
            }

            Rect r = full.rect;
            float halfW = r.width / 2f;

            // X 从左边量，两种约定一致；Y 取整幅，同样一致
            Rect halfRect = new Rect(isLeft ? r.x : r.x + halfW, r.y, halfW, r.height);

            // ⚠️ 像素/单位 = 1（不是 Unity 默认的 100）—— 见 CloudEyrieTextureImporter 的推导
            Sprite sub = Sprite.Create(full.texture, halfRect, new Vector2(0.5f, 0.5f), 1f);

            if (sub != null)
            {
                sr.sprite = sub;
            }

            n.transform.localScale = new Vector3(scale, scale, 1f);

            // ⚠️ 两半的分开**靠挪节点**，不要碰 Sprite 的 offset/pivot。
            // 锚点在中心，所以各挪半个宽度即可。
            n.transform.position = new Vector3(
                x + (isLeft ? -1f : 1f) * halfW * scale / 2f, y, 0f);

            return n;
        }

        /// <summary>
        /// 摆一件可运行时加载的道具。
        ///
        /// ⚠️ contentSize 必须设成**原图尺寸**再乘缩放。
        /// Cocos 那边踩过一个坑：`sizeMode = CUSTOM` 下画面尺寸 = contentSize × scale，
        /// contentSize 忘了设就一直是 (1,1)，道具会渲染成约 1 像素、等于看不见
        /// （排查了好几轮的「灵泉/屏障显示不出来」）。Unity 这边靠 SpriteRenderer
        /// 自带的 bounds 避免了这个坑，但**缩放倍率仍然要按原图尺寸算**。
        /// </summary>
        private GameObject MakeProp(string path, float x, float y, float scale, string name)
        {
            GameObject n = new GameObject(name);
            n.transform.SetParent(transform, false);
            n.transform.position = new Vector3(x, y, 0f);

            SpriteRenderer sr = n.AddComponent<SpriteRenderer>();
            sr.sortingOrder = SortingOrders.LevelProp;

            Sprite sprite = Resources.Load<Sprite>(path);

            if (sprite == null)
            {
                Debug.LogWarning($"[Level] 道具读不到 {path}");
                return n;
            }

            sr.sprite = sprite;
            n.transform.localScale = new Vector3(scale, scale, 1f);
            return n;
        }

        /// <summary>
        /// 摆一座灵泉石台：按内容轮廓落地 + 挂一个**贴合石台外轮廓**的碰撞体。
        ///
        /// <b>为什么用烘焙好的多边形，而不是运行时读像素</b>
        ///
        /// 「照着贴图轮廓给碰撞体」最直接的做法是运行时读 alpha、跑 marching squares。
        /// 但那要求贴图是 <c>isReadable</c>，而本项目所有 Resources 美术都按
        /// <c>pixelsPerUnit = 1</c> 导入、**没开 Readable** —— 打开它会让每张贴图多留一份
        /// CPU 副本（背景与序列帧都是几十 MB 级），代价远大于收益。
        ///
        /// 所以轮廓是**离线量好后烘进代码**的（见 <see cref="SpringOutline"/>）。
        ///
        /// ⚠️ **换素材必须重跑轮廓**，否则碰撞体和新图对不上，而且**不会报错**。
        /// </summary>
        private GameObject MakeSpring(float x, string name)
        {
            float nodeY = GroundTopY + SpringContentBottom * springArtScale - springGroundSink;
            GameObject n = MakeProp("场景小物件/灵泉石台（回血）", x, nodeY, springArtScale, name);

            // ⚠️ 必须归到 WORLD 层。碰撞矩阵里 PLAYER 只和 WORLD / ARROW 碰，
            // 留在 Default 层的话 `PhysicsGroups.Apply()` 会把游戏层与「其它一切层」
            // 的碰撞全部断开 —— 石台对玩家完全不存在。
            n.layer = PhysicsGroups.World;

            PolygonCollider2D poly = n.AddComponent<PolygonCollider2D>();
            poly.points = SpringOutline;

            // 和地面一样的摩擦，玩家能稳稳踩在石阶上（原来是 0 摩擦，踩上去会滑）
            poly.sharedMaterial = GroundLikeMaterial();

            return n;
        }

        /// <summary>
        /// 灵泉石台的外轮廓：**相对精灵中心、y 向上，单位 = 原图像素**（与
        /// <see cref="PolygonCollider2D"/> 的本地空间一致，节点缩放在外面套着）。
        ///
        /// 取自 <c>灵泉石台（回血）.png</c> 的 alpha&gt;8 轮廓，1714 点抽稀到 30 点。
        /// 形状是「细颈在上、宽盆在下」—— 所以玩家会被石台挡住、能站在盆沿上。
        ///
        /// ⚠️ **换素材要重新跑一遍**（步骤：读 alpha &gt; 阈值 → 逐行取左右边界 →
        /// 左链自上而下 + 右链自下而上 → 抽稀 → 减去图心、y 取反）。
        /// </summary>
        private static readonly Vector2[] SpringOutline =
        {
            // ⚠️ **只有石阶，不含水**。
            //
            // 泉水那张图上半截是水柱与水花（无实体），拿整幅轮廓去挡人是「水墙」。
            // 所以从「石体起始行」y=532 往下取，其上整段丢弃 —— 那一行是
            // 「行内连续且宽度 > 400」第一次成立的位置，也就是水柱结束、石台开始的地方。
            new Vector2(-449.5f, -8.5f), new Vector2(-468.5f, -41.5f), new Vector2(-546.5f, -74.5f), new Vector2(-513.5f, -107.5f), new Vector2(-570.5f, -140.5f), new Vector2(-617.5f, -173.5f), new Vector2(-652.5f, -206.5f), new Vector2(-686.5f, -239.5f), new Vector2(-726.5f, -272.5f), new Vector2(-633.5f, -305.5f), new Vector2(-578.5f, -338.5f), new Vector2(-429.5f, -371.5f), new Vector2(-382.5f, -404.5f),
            new Vector2(-9.5f, -437.5f), new Vector2(25.5f, -417.5f), new Vector2(402.5f, -384.5f), new Vector2(609.5f, -351.5f), new Vector2(579.5f, -318.5f), new Vector2(673.5f, -285.5f), new Vector2(739.5f, -252.5f), new Vector2(688.5f, -219.5f), new Vector2(633.5f, -186.5f), new Vector2(583.5f, -153.5f), new Vector2(596.5f, -120.5f), new Vector2(586.5f, -87.5f), new Vector2(550.5f, -54.5f), new Vector2(499.5f, -21.5f), new Vector2(500.5f, -8.5f),
        };
    }
}
