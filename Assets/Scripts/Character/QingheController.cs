using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 青禾玩家控制器：实体外壳（物理、碰撞、输入、形态委托）。
    /// 对应 Cocos 侧 <c>character/QingheController.ts</c>。
    ///
    /// 形态差异的移动/消耗已抽到 <c>Forms/</c> 状态类里（状态模式），
    /// 战斗（轻击/蓄力/格挡/灵弹）已抽到 <see cref="CombatController"/>。
    /// 本控制器只负责：读移动输入 → 委托战斗 tick → 委托形态移动 tick → 翻转立绘。
    ///
    /// <b>⚠️ 全项目唯一读 <see cref="InputSystem"/> 的地方。</b>
    /// 形态状态类与战斗控制器一律读 <c>ctx.Input</c> 快照 ——
    /// 它们若绕过快照直接读 <c>InputSystem.WasPressed</c>，就会出现两个真相源，
    /// 边沿语义立刻失效。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    [RequireComponent(typeof(CompanionStateMachine))]
    public class QingheController : MonoBehaviour
    {
        /// <summary>
        /// 青禾立绘的天然朝向：素材画的是**朝右**。
        /// 判据：弓向右侧伸出、握弓的手在右、脸朝右（把原图和镜像并排对比确认过）。
        /// 改美术素材后如果朝向变了，改这一个常量即可。
        /// </summary>
        public const NaturalFacing ArtNaturalFacing = NaturalFacing.Right;

        // ⚠️ 速度单位是**世界单位/秒**（= 像素/秒）。
        // Cocos 侧这些值是「米/秒」（Box2D 单位），要 × PHYSICS_2D_PTM_RATIO(32) 才是像素/秒。
        // 括号里是 Cocos 原值。详见 PhysicsUnits。
        [Header("移动")]
        [SerializeField] private float moveSpeed = 160f;        // Cocos 5 m/s
        [SerializeField] private float jumpSpeed = 320f;        // Cocos 10 m/s

        [Tooltip("二段跳的速度倍率；1 = 和第一跳一样高")]
        [SerializeField] private float secondJumpMult = 0.6f;

        [SerializeField] private int maxJumpCount = 2;

        [Header("跳跃手感")]
        [Tooltip("离开地面后仍允许起跳的宽限 /秒（土狼时间）")]
        [SerializeField] private float coyoteTime = 0.10f;

        [Tooltip("落地前按下跳跃的预输入窗口 /秒（跳跃缓冲）")]
        [SerializeField] private float jumpBufferTime = 0.12f;

        [Tooltip("上升中松开跳跃键时，竖直速度乘以这个系数（可变跳跃高度）")]
        [SerializeField] private float jumpCutMultiplier = 0.45f;

        [Tooltip("上升段的额外重力倍率（<1 = 上升更飘、更好看）")]
        [SerializeField] private float riseGravityScale = 0.9f;

        [Tooltip("下落段的额外重力倍率（>1 = 落地更利落）")]
        [SerializeField] private float fallGravityScale = 1.35f;

        [Tooltip("二段跳时按住方向键给的水平速度倍率（0 = 不给水平加速）")]
        [SerializeField] private float secondJumpHorizontalBoost = 1.25f;

        [Header("踏云闪")]
        [SerializeField] private float dashSpeed = 960f;        // Cocos 30 m/s
        [SerializeField] private float dashDuration = 0.15f;

        [Header("形态速度")]
        [Tooltip("灵雾自由飞行速度")]
        [SerializeField] private float mistFlySpeed = 160f;     // Cocos 5 m/s

        [Tooltip("灵合悬浮速度")]
        [SerializeField] private float mergeMoveSpeed = 240f;   // Cocos 7.5 m/s

        [Header("立绘")]
        [Tooltip("人物在画面上的高度（世界单位）。⚠️ 现场拖这个数就能调大小，" +
                 "不需要去改 Art 节点的 scale")]
        [SerializeField] private float artHeight = 240f;

        [Tooltip("人物占整张立绘画布的高度比。qinghe_portrait.png 实测 1720/2048 ≈ 0.84" +
                 "（画布上下有透明留白）")]
        [SerializeField] private float artFigureRatio = 0.84f;

        [Tooltip("人物底边距画布底边的占比。实测 250/2048 ≈ 0.122 —— 脚底下那截透明留白。" +
                 "不扣掉它人物会「悬空」")]
        [SerializeField] private float artBottomPaddingRatio = 0.122f;

        /// <summary>
        /// 整体尺寸倍数。**立绘和碰撞体一起缩**，位置关系不变。
        ///
        /// ⚠️ 只缩立绘不缩碰撞体的话，会出现「人看着小了、碰撞体还是原来那么大」——
        /// 表现为攻击够不着、跳起来头撞到看不见的东西，而且**肉眼完全看不出来**，
        /// 只能靠手感怀疑。所以这里把基准尺寸在 <c>Awake</c> 里存下来，
        /// 两个一起乘同样的倍数，对齐关系（人物底边 = 碰撞体底边）自动保持。
        /// </summary>
        /// <summary>
        /// 整体尺寸倍数：**立绘与碰撞体一起缩**，位置关系不变。
        ///
        /// ⚠️ 它是「人物多大」的**唯一旋钮**，别去动 <see cref="artHeight"/> 来调大小 ——
        /// `artHeight` 只影响立绘（以及由 <see cref="FigureHeight"/> 推出来的特效落点），
        /// 碰撞体和判定盒走的是 `bodyScale`。只调 artHeight 会让
        /// 「立绘比碰撞体大一圈」，攻击判定和看着的体型对不上。
        ///
        /// 2026-10-08 定为 <b>0.6</b>：目标是「身高 = 小怪可见高的 1.2 倍」。
        /// 小怪立绘画布 169（含透明留白）而**可见本体 119**，
        /// 119 × 1.2 ≈ 143，而 <c>artHeight(240) × 0.6 = 144</c> ✓
        /// </summary>
        [Tooltip("整体尺寸倍数：立绘与碰撞体一起缩，位置关系不变")]
        public float bodyScale = 0.6f;

        /// <summary>
        /// 胸口在身高上的比例（相对脚底）。**特效落点与判定盒高度都以它为准。**
        ///
        /// 只有这一份定义：<see cref="CombatController.ChestHeight"/> 与
        /// <see cref="ChestOffset"/> 都读它。两处各写一个 0.70 的话，
        /// 改了这边忘了那边，症状就是「墨迹从胸口出去、判定盒在腰上」——
        /// 看着打中了却没伤害，而且不报错。
        ///
        /// 取 0.70（上胸/肩）而不是解剖学上的「胸口 0.62」：人物越小出招点越低，
        /// 而 Boss 的受击盒底边比它的美术脚底高 43 单位，留不出余量就会「打不到」。
        /// 详见 <see cref="CombatController.ChestHeight"/> 的说明。
        /// </summary>
        public const float ChestRatio = 0.70f;

        /// <summary>
        /// 胸口相对**节点原点**的高度（世界单位）。
        ///
        /// 给特效落点用（冲刺拖尾、格挡圈、换形态光）。人物底边 = 碰撞体底边，
        /// 所以从碰撞体底边往上量 <see cref="FigureHeight"/> × <see cref="ChestRatio"/>。
        /// </summary>
        public float ChestOffset
        {
            get
            {
                float feet = _collider != null
                    ? _collider.offset.y - _collider.size.y * 0.5f
                    : 0f;

                return feet + FigureHeight * ChestRatio;
            }
        }

        /// <summary>
        /// 所有「按人物尺寸量出来的」战斗数值，是在**这个 bodyScale 下**量/调出来的。
        ///
        /// 命中盒的偏移与尺寸（场景里 <c>AttackHitBox</c> 节点的 80,60 / 90×100）、
        /// 墨迹特效相对人物的前方距离（<c>SlashOffsetX</c> 等）、灵弹出膛点
        /// （<c>MistShotSpawnX</c>）—— 这些都是**手摆出来的世界单位**，不会自己跟着
        /// <see cref="bodyScale"/> 走。
        ///
        /// 所以凡是要跟人物一起缩的地方，一律乘
        /// <see cref="BodyScaleRatio"/>（= 当前 bodyScale / 这个基准），
        /// **不要再散着写 0.8 之类的字面量** —— 那样下次调缩放就会漏掉某一处，
        /// 而且漏了不报错（判定盒偏移 20 单位肉眼根本看不出来，只有打不中才发现）。
        ///
        /// 变更记录：数值最后一次校对是在 0.75；2026-10-07 按反馈整体缩到 0.6。
        /// </summary>
        public const float MeasuredAtBodyScale = 0.75f;

        /// <summary>
        /// 相对于 <see cref="MeasuredAtBodyScale"/> 的比例，供战斗 / 特效换算世界单位用。
        /// 0 或负数会被当成 1（和 <see cref="ApplyBodyScale"/> 的兜底一致）。
        /// </summary>
        public float BodyScaleRatio
        {
            get
            {
                float b = bodyScale > 0f ? bodyScale : 1f;

                return b / MeasuredAtBodyScale;
            }
        }

        /// <summary>
        /// 人物当前的**实际身高**（世界单位，已含 <see cref="bodyScale"/>）。
        ///
        /// 给特效定位用。⚠️ **不要拿碰撞体去推** —— 碰撞体是**刻意小于立绘**的
        /// （30×112.5 的碰撞体配 180 高的人物，只占 62%），按它算出来的「胸口」
        /// 其实在小腿上。实测：按碰撞体 40% 算只有脚底往上 30，而真正的胸口在 ~97。
        ///
        /// <b>为什么要有兜底</b>：<see cref="FitArt"/> 有可能因为拿不到立绘节点而提前返回
        /// （比如换了素材、或节点名变了），那样这个值就一直是 0，所有特效都会落到脚底。
        /// 而身高本身在 <c>artHeight × bodyScale</c> 里**永远算得出来** —— 所以直接用它兜底，
        /// 不依赖 <see cref="FitArt"/> 有没有跑成。
        /// </summary>
        public float FigureHeight
        {
            get
            {
                if (_figureHeight > 1f)
                {
                    return _figureHeight;
                }

                return artHeight * (bodyScale > 0f ? bodyScale : 1f);
            }
        }

        /// <summary><see cref="FitArt"/> 实测出来的身高；没跑成时为 0，由属性兜底</summary>
        private float _figureHeight;

        [Header("场景引用")]
        [Tooltip("地面节点。⚠️ 必须是场景里的那个对象，运行时另造一个它指不到 " +
                 "—— grounded 永远 false、jumpCount 永不重置，表现是「跳两次之后再也跳不起来」")]
        [SerializeField] private GameObject groundNode;

        private Rigidbody2D _rigidBody;
        private BoxCollider2D _collider;
        private CompanionStateMachine _companionStateMachine;
        private CombatController _combat;
        private FormContext _ctx;

        /// <summary>
        /// 主立绘节点（<c>Art</c>）。装了 Spine 的情况下它是 <c>SkeletonAnimation</c> 的宿主。
        /// </summary>
        private Transform _artNode;

        /// <summary>
        /// 静态立绘备用节点（<c>ArtSprite</c>）。
        ///
        /// ⚠️ <b>为什么要有两个节点</b>：Cocos 和 Unity 都**不允许**把骨骼动画组件和
        /// Sprite 渲染器放在同一个节点上 ——
        /// <list type="bullet">
        ///   <item>Cocos：<c>sp.Skeleton</c> 与 <c>cc.Sprite</c> 都是 UIRenderer，直接冲突</item>
        ///   <item>Unity：<c>SkeletonAnimation</c> 需要 <c>MeshFilter</c>，而它和
        ///     <c>SpriteRenderer</c> 互斥（<c>Can't add component 'MeshFilter' ... conflicts</c>）</item>
        /// </list>
        /// 两边的解法一样：**另建一个节点**。<c>QingheController</c> 按名字找立绘，
        /// 所以主节点仍叫 <c>Art</c>，静态那张放 <c>ArtSprite</c>。
        /// （Cocos 的 <c>BossTest.scene</c> 里就是这个结构。）
        /// </summary>
        private Transform _artSpriteNode;

        private CompanionForm _currentForm = CompanionForm.Entity;

        /// <summary>
        /// 当前形态。**给机关用**（金境「剑碑序列」要判断玩家是不是灵合态）。
        ///
        /// 为什么不在机关里也走 <c>gravityScale</c> 那套判据：那个只能区分
        /// 「有重力（实体）/ 无重力（灵雾或灵合）」，**分不出灵雾与灵合**，
        /// 而剑碑要求的是**必须灵合**。两者的差别在这里是玩法本身，不能含糊。
        /// </summary>
        public CompanionForm CurrentForm
        {
            get { return _currentForm; }
        }

        private void Awake()
        {
            _rigidBody = GetComponent<Rigidbody2D>();
            _collider = GetComponent<BoxCollider2D>();

            if (_rigidBody == null)
            {
                Debug.LogError("[Qinghe Controller] RigidBody2D not found!");
                enabled = false;
                return;
            }

            if (_collider == null)
            {
                Debug.LogError("[Qinghe Controller] BoxCollider2D not found!");
                enabled = false;
                return;
            }

            // ⚠️ groundNode 是**遗留字段**：着地判定早就改成按层了
            // （见 IsGroundContact —— 层 == WORLD 且接触法线朝上）。
            // 这里以前报 "groundNode 未设置！着地判定会永远失败"，那句话是**错的**：
            // 它不会失败。关卡模组化后地面在另一个场景，这个引用必然为空，
            // 再报错就纯属刷屏，所以改成只在 Verbose 档提示一句。
            if (groundNode == null)
            {
                GameLog.V("[Qinghe Controller] groundNode 未设置（正常：着地按层判定，不看它）");
            }

            ApplyBodyScale();

            // 构建形态上下文（注入刚体 + 移动参数）
            MovementParams move = new MovementParams
            {
                MoveSpeed = moveSpeed,
                JumpSpeed = jumpSpeed,
                SecondJumpMult = secondJumpMult,
                MaxJumpCount = maxJumpCount,
                DashSpeed = dashSpeed,
                DashDuration = dashDuration,
                MistFlySpeed = mistFlySpeed,
                MergeMoveSpeed = mergeMoveSpeed,
                CoyoteTime = coyoteTime,
                JumpBufferTime = jumpBufferTime,
                JumpCutMultiplier = jumpCutMultiplier,
                RiseGravityScale = riseGravityScale,
                FallGravityScale = fallGravityScale,
                SecondJumpHorizontalBoost = secondJumpHorizontalBoost,
            };

            _ctx = new FormContext(_rigidBody, move);
            _wasGrounded = _ctx.Grounded;

            // 形态状态机 + 战斗控制器（同节点）
            _companionStateMachine = GetComponent<CompanionStateMachine>();
            _combat = GetComponent<CombatController>();

            // 立绘节点（可选的灰盒占位，没有也不影响逻辑）。
            // 主节点叫 Art；静态立绘的备用节点叫 ArtSprite（见字段说明）。
            _artNode = transform.Find("Art");
            _artSpriteNode = transform.Find("ArtSprite");

            EventBus.On<CompanionFormChangedEvent>(GameEvents.CompanionFormChanged, OnFormChanged);
        }

        private void OnDestroy()
        {
            EventBus.Off<CompanionFormChangedEvent>(GameEvents.CompanionFormChanged, OnFormChanged);
        }

        private void Start()
        {
            FitArt();
            TryFitSpineArt();

            // 确保初始物理状态与默认形态一致
            ApplyFormPhysics();
            ApplyFormTint();
        }

        /// <summary>
        /// 按 <see cref="artHeight"/> 摆好立绘：算出缩放、并把**人物底边**对齐到碰撞体底边。
        ///
        /// 两个坑都在这里：
        /// <list type="number">
        ///   <item>Cocos 的 <c>main.scene</c> 里青禾立绘画布是 <b>134×150</b>（= 碰撞体高），
        ///     而 <c>BossTest.scene</c> 里 Spine 版是 <b>370</b> —— 同一个角色差 2.9 倍。
        ///     所以这里不写死，做成可现场调的字段。</item>
        ///   <item>立绘画布下方有透明留白（实测 250/2048），**按画布底边对齐地面会让人物悬空**。
        ///     必须扣掉留白，让**人物底边**落在碰撞体底边上。</item>
        /// </list>
        /// </summary>
        /// <summary>
        /// 哪个立绘节点上挂着静态 Sprite。
        ///
        /// 装了 Spine 之后 <c>Art</c> 上是 <c>SkeletonAnimation</c>（没有 SpriteRenderer），
        /// 静态那张挪去了 <c>ArtSprite</c>。这个方法把「按名字找」这件事收在一处，
        /// 免得 <see cref="FitArt"/> 和 <see cref="ApplyFormTint"/> 各判各的。
        /// </summary>
        private Transform SpriteArtNode()
        {
            if (_artNode != null && _artNode.GetComponent<SpriteRenderer>() != null)
            {
                return _artNode;
            }

            if (_artSpriteNode != null && _artSpriteNode.GetComponent<SpriteRenderer>() != null)
            {
                return _artSpriteNode;
            }

            return null;
        }

        // ============================================================ 骨骼立绘的缩放与落地
        //
        // ⚠️ **这里曾经是个大坑**：青禾画面上渲染的一直是 Spine **骨骼动画**（`Art` 节点），
        // 而 `FitArt()` 只处理**静态 Sprite** 那条路 —— 静态那张在场景里是 `m_Enabled: 0`
        // （装了 Spine 之后就停用了）。于是 `bodyScale` 只缩了碰撞体，**立绘从来没缩过**：
        // 实测 bodyScale=0.4 时 `FigureHeight` 是 96，而画面上的人高 **231**、脚还陷进地面 27。
        //
        // 后果全都指向同一类症状（实测反馈「特效中心点在腰部以下，灵弹也从腰以下发射」）：
        // 出招点 / 判定盒 / 护盾 / 环绕件都是按 `FigureHeight`（96）算的，
        // 落在真实人物（231 高，腰在脚底上方 88.5）的**腰以下**。
        //
        // 所以骨骼立绘必须自己走一遍「按 bodyScale 缩放 + 脚底对齐碰撞体底边」。
        // 静态那张继续走 `FitArt()`，两条路互不干扰。

        /// <summary>骨骼立绘是否已经摆好（量到有效尺寸之后才置真）</summary>
        private bool _spineFitDone;

        /// <summary>已经尝试的帧数。骨骼网格要等它自己建出来才能量包围盒，所以允许重试几帧。</summary>
        private int _spineFitTries;

        /// <summary>最多重试多少帧（约 1 秒）—— 超了就打一条警告，不再空转。</summary>
        private const int SpineFitMaxTries = 60;

        /// <summary>
        /// 把骨骼立绘按 <see cref="bodyScale"/> 缩放，并把**脚底**对齐到碰撞体底边。
        ///
        /// 目标高度取 <c>artHeight × bodyScale</c> —— 与静态立绘那条路**同一个目标**，
        /// 所以 `artHeight` 仍然是「人物在画面上的高度」这一个旋钮，换哪种立绘都成立。
        ///
        /// <b>为什么要先量再算，而不是按倍数乘</b>：Spine 骨架的原生尺寸是它自己定的
        /// （这里实测 231），`artHeight`(240) 只是「想要多高」。两者不是同一个数，
        /// 按倍数乘会一直差一截。这里量一次原生高度再反算缩放，任何骨架都能适配。
        ///
        /// ⚠️ 量的时候先把 `localScale` 归一并把 `localPosition` 归零 ——
        /// 否则量到的是「已经缩过的结果」，重跑一次就会复合缩放。
        /// 包围盒随动画帧小幅变化（举手/换姿势会差一点），所以只量一次、结果缓存住，
        /// 不每帧重算。
        /// </summary>
        private void TryFitSpineArt()
        {
            // 换成帧序列之后，Art 上是 SpriteRenderer + SequencePlayer：
            // 它由 SpriteSequenceView 自己摆，别按骨骼那套「量包围盒再缩」再来一遍。
            if (_artNode != null && _artNode.GetComponent<SpriteSequenceView>() != null)
            {
                _spineFitDone = true;
                return;
            }

            if (_spineFitDone || _artNode == null || _collider == null)
            {
                return;
            }

            Renderer[] rends = _artNode.GetComponentsInChildren<Renderer>();

            if (rends.Length == 0)
            {
                // 没有渲染器 = 这一版立绘不是骨骼（静态那条走 FitArt），不用管
                _spineFitDone = true;
                return;
            }

            // 归零量原生尺寸
            _artNode.localScale = Vector3.one;
            _artNode.localPosition = Vector3.zero;

            Bounds b = rends[0].bounds;

            for (int i = 1; i < rends.Length; i++)
            {
                b.Encapsulate(rends[i].bounds);
            }

            float nativeH = b.size.y;

            if (nativeH <= 1f)
            {
                // 网格还没建出来（Spine 的 bounds 要等它自己生成一次才有效）—— 下一帧再试
                if (++_spineFitTries > SpineFitMaxTries)
                {
                    _spineFitDone = true;
                    Debug.LogWarning("[Qinghe Controller] 骨骼立绘一直量不到尺寸，" +
                                     "立绘不会按 bodyScale 缩放（人物与实际尺寸会对不上）。");
                }

                return;
            }

            float targetH = artHeight * (bodyScale > 0f ? bodyScale : 1f);
            float k = targetH / nativeH;

            _artNode.localScale = new Vector3(k, k, 1f);

            // 脚底对齐碰撞体底边。
            // b.min.y 是「节点在原点、缩放为 1」时的世界底边，减去本人世界 y 就是相对量；
            // 缩放 k 之后它变成 bottomRel × k，于是把节点抬到 (碰撞体底边 − 它) 即可。
            float bottomRel = b.min.y - transform.position.y;
            float colliderBottom = _collider.offset.y - _collider.size.y * 0.5f;

            _artNode.localPosition = new Vector3(0f, colliderBottom - bottomRel * k, 0f);
            _spineFitDone = true;

            GameLog.I($"[Qinghe Controller] 骨骼立绘(bodyScale ×{bodyScale:F2}) 原生高={nativeH:F0} " +
                      $"→ 目标高={targetH:F0} 缩放={k:F4} 中心y={_artNode.localPosition.y:F1}");
        }

        /// <summary>碰撞体的原始尺寸/偏移，用于按 <see cref="bodyScale"/> 等比缩放。</summary>
        private Vector2 _colliderBaseSize;
        private Vector2 _colliderBaseOffset;
        private bool _colliderBaseCaptured;

        /// <summary>冲刺拖尾的上一次状态（用来取上升沿）</summary>
        private bool _wasDashing;

        /// <summary>帧序列立绘（惰性查找；没有视图的功能不受影响）</summary>
        private SpriteSequenceView _sequenceView;

        /// <summary>上一帧的着地状态（用来取落地的那一瞬）</summary>
        private bool _wasGrounded;

        /// <summary>脚底的世界坐标（起跳/落地特效落点）。</summary>
        private Vector3 FxFeetWorld()
        {
            if (_collider != null)
            {
                Bounds b = _collider.bounds;
                return new Vector3(b.center.x, b.min.y, 0f);
            }

            return transform.position;
        }

        /// <summary>胸口的世界坐标（特效落点）。见 <see cref="ChestOffset"/>。</summary>
        private Vector3 FxChestWorld()
        {
            Vector3 o = transform.position;

            return new Vector3(o.x, o.y + ChestOffset, o.z);
        }

        /// <summary>灵雾拖尾的限频累加器</summary>
        private float _mistTrailAccum;

        /// <summary>灵雾拖尾的间隔 /秒。小一点才连成线；太大就成一段一段的。</summary>
        private const float MistTrailInterval = 0.09f;

        /// <summary>判定「真的在动」的速度下限（平方）。低于它就当作悬停，不喷拖尾。</summary>
        private const float MistTrailMinSpeedSq = 90f * 90f;

        /// <summary>
        /// 灵雾形态飞行时的持续拖尾。见 <see cref="InkFx.Kind.Trail"/>。
        ///
        /// 和冲刺拖尾一样放在控制器里（形态类拿不到身高与缩放，算不出落点）。
        /// </summary>
        private void UpdateMistTrail(float dt)
        {
            if (_currentForm != CompanionForm.Mist || _rigidBody == null || _ctx == null)
            {
                _mistTrailAccum = 0f;
                return;
            }

            Vector2 v = _rigidBody.linearVelocity;

            if (v.sqrMagnitude < MistTrailMinSpeedSq)
            {
                _mistTrailAccum = 0f;      // 悬停：不喷，免得原地冒烟
                return;
            }

            _mistTrailAccum += dt;

            if (_mistTrailAccum < MistTrailInterval)
            {
                return;
            }

            _mistTrailAccum = 0f;

            InkFx.Burst(FxChestWorld(), InkFx.Kind.Trail, CompanionForm.Mist,
                        v.normalized, BodyScaleRatio);
        }

        /// <summary>
        /// 按 <see cref="bodyScale"/> 缩碰撞体。
        ///
        /// 基准值在第一次调用时从碰撞体上抓（**必须是原始的、未缩过的**，
        /// 所以在 Awake 里、任何缩放之前调用一次就够）。
        /// </summary>
        private void ApplyBodyScale()
        {
            if (_collider == null)
            {
                return;
            }

            if (!_colliderBaseCaptured)
            {
                _colliderBaseCaptured = true;
                _colliderBaseSize = _collider.size;
                _colliderBaseOffset = _collider.offset;
            }

            float k = bodyScale > 0f ? bodyScale : 1f;

            _collider.size = _colliderBaseSize * k;
            _collider.offset = _colliderBaseOffset * k;

            GameLog.I($"[Qinghe Controller] 整体缩放 ×{k:F2}：碰撞体 {_colliderBaseSize} → {_collider.size}，" +
                      $"偏移 {_colliderBaseOffset} → {_collider.offset}");
        }

        private void FitArt()
        {
            Transform node = SpriteArtNode();

            // 序列帧立绘自己算尺寸（SpriteSequenceView.FitToCollider）：
            // 这里按的是「整张画布的包围盒」，而序列帧的格子 384、人物只占 232 —— 再缩会小一圈。
            if (node != null && node.GetComponent<SpriteSequenceView>() != null)
            {
                return;
            }

            if (node == null || _collider == null)
            {
                // 没有静态立绘是**正常情况**（比如全身改用 Spine 骨骼）。
                // 只有连主节点都没有时才提醒一句。
                if (_artNode == null && _artSpriteNode == null)
                {
                    Debug.LogWarning("[Qinghe Controller] 既没有 Art 也没有 ArtSprite 节点");
                }

                return;
            }

            SpriteRenderer sr = node.GetComponent<SpriteRenderer>();

            if (sr == null || sr.sprite == null)
            {
                return;
            }

            float spriteH = sr.sprite.bounds.size.y;

            if (spriteH <= 0f || artFigureRatio <= 0f)
            {
                return;
            }

            // 希望「人物」达到 artHeight × bodyScale → 反推整张画布该多大。
            // 立绘位置由碰撞体底边反推（见下），所以缩了立绘、碰撞体也跟着缩，
            // 「人物底边 = 碰撞体底边」这层关系自动保持。
            float targetFigureH = artHeight * (bodyScale > 0f ? bodyScale : 1f);
            float canvasTargetH = targetFigureH / artFigureRatio;

            _figureHeight = targetFigureH;
            float k = canvasTargetH / spriteH;

            node.localScale = new Vector3(k, k, 1f);

            // 人物底边 = 碰撞体底边；画布底边还要再往下让出那段透明留白
            float colliderBottom = _collider.offset.y - _collider.size.y * 0.5f;
            float canvasBottom = colliderBottom - artBottomPaddingRatio * canvasTargetH;

            node.localPosition = new Vector3(0f, canvasBottom + canvasTargetH * 0.5f, 0f);

            GameLog.I($"[Qinghe Controller] 立绘({node.name}) 人物高={targetFigureH:F0}(×{bodyScale:F2}) 画布高={canvasTargetH:F0} " +
                      $"缩放={k:F4} 中心y={(canvasBottom + canvasTargetH * 0.5f):F1}");
        }

        /// <summary>
        /// 开场过场的**脚本接管移动**：非零时覆盖玩家的左右输入（-1 左 / +1 右）。
        ///
        /// 为什么不让 <see cref="LevelDirector"/> 直接改 Transform：那样角色是「滑」过去的，
        /// 不走移动代码 —— 朝向、着地判定、形态门控全都绕过，将来接了行走动画也不会播。
        /// 走这里等于「假装玩家一直按着方向键」，与真人操作同一条路径。
        /// </summary>
        [System.NonSerialized] public float ScriptedMoveX;

        private void Update()
        {
            // 骨骼立绘的尺寸要等它自己把网格建出来才量得到，Start 那次不一定赶得上 ——
            // 所以在这里接着试几帧（量到就只跑一次，见 TryFitSpineArt）。
            TryFitSpineArt();

            if (_ctx == null || _companionStateMachine == null)
            {
                return;
            }

            // ⚠️ 快照必须在**这里、最顶端、无条件**重填。
            //
            // 这是全项目唯一读 InputSystem 的地方（见类注释）。
            // 每帧重填能顶替原来「消费后在 update 末尾手动清 jump/dash」那两行：
            // WasPressed 只在按下的那一帧为 true，所以快照天然只有一帧有效。
            //
            // **若把这段挪到 update 末尾，就必须把那两行清位加回来。**
            MoveInput inp = _ctx.Input;

            // 演出期间（全屏转场 / 过场）**完全不接收输入**。
            //
            // timeScale = 0 已经让移动量变成 0，但攻击 / 格挡是**按键当帧触发的瞬时动作**、
            // 不看 dt —— 不管的话玩家能在全屏过场里挥剑。见 GameFlow 的说明。
            if (GameFlow.Active)
            {
                // 清空快照：否则恢复操作的那一帧会读到演出前残留的按下状态
                inp.Left = inp.Right = inp.Up = inp.Down = false;
                inp.Jump = inp.Dash = false;
                inp.JumpHeld = false;
                _ctx.Input = inp;
                return;
            }

            inp.Left = InputSystem.IsDown(GameAction.MoveLeft);
            inp.Right = InputSystem.IsDown(GameAction.MoveRight);
            inp.Up = InputSystem.IsDown(GameAction.MoveUp);
            inp.Down = InputSystem.IsDown(GameAction.MoveDown);
            inp.Jump = InputSystem.WasPressed(GameAction.Jump);
            inp.JumpHeld = InputSystem.IsDown(GameAction.Jump);
            inp.Dash = InputSystem.WasPressed(GameAction.Dash);
            // 开场过场：脚本接管方向（见 ScriptedMoveX 的说明）。
            // 放在填写之后、使用之前，玩家在过场里按方向键也不会打乱编排。
            if (!Mathf.Approximately(ScriptedMoveX, 0f))
            {
                inp.Left = ScriptedMoveX < 0f;
                inp.Right = ScriptedMoveX > 0f;
                inp.Jump = false;
                inp.JumpHeld = false;
                inp.Dash = false;
            }

            _ctx.Input = inp;

            // 战斗先更新（写 IsAttacking/IsBlocking），再委托形态移动读门控 ——
            // 顺序不能反，否则门控会延迟一帧
            if (_combat != null)
            {
                _combat.Tick(Time.deltaTime, _ctx);
            }

            // 委托形态逻辑（移动 + 持续消耗 + 强制切雾）
            _companionStateMachine.Tick(Time.deltaTime, _ctx);

            // ---- 动作特效（跳 / 云跃 / 落地）----
            if (_ctx.Jumped)
            {
                _ctx.Jumped = false;
                InkFx.Burst(FxFeetWorld(), InkFx.Kind.JumpDust, _currentForm,
                            Vector2.zero, BodyScaleRatio);
            }

            if (_ctx.DoubleJumped)
            {
                _ctx.DoubleJumped = false;
                InkFx.Burst(FxFeetWorld(), InkFx.Kind.DoubleJump, _currentForm,
                            Vector2.zero, BodyScaleRatio);
            }

            if (!_wasGrounded && _ctx.Grounded)
            {
                InkFx.Burst(FxFeetWorld(), InkFx.Kind.LandDust, _currentForm,
                            Vector2.zero, BodyScaleRatio);
            }

            _wasGrounded = _ctx.Grounded;

            // 冲刺拖尾：盯 IsDashing 的**上升沿**统一发，两种形态共用一条。
            //
            // ⚠️ 为什么不放在形态类里：那边只有 ctx（刚体 + 移动参数），**拿不到
            // 身高与整体缩放**，落点和粒子尺寸都算不出来。这里两样都有。
            // 上升沿也正好是「真的冲出去了」—— 实体形态灵炁不足时不会置 IsDashing，
            // 所以不会出现「没冲却有拖尾」（和音效放在扣灵炁之后是同一条约定）。
            if (_ctx.IsDashing && !_wasDashing)
            {
                InkFx.Burst(FxChestWorld(), InkFx.Kind.Dash, _currentForm,
                            new Vector2(_ctx.Facing >= 0f ? 1f : -1f, 0f),
                            BodyScaleRatio);

                // 动作跟着同一条上升沿走：拖尾、音效、立绘三者同时发生。
                if (_sequenceView == null)
                {
                    _sequenceView = GetComponentInChildren<SpriteSequenceView>();
                }

                if (_sequenceView != null)
                {
                    _sequenceView.PlayOneShot("青禾-踏云闪");
                }
            }

            _wasDashing = _ctx.IsDashing;

            // 灵雾飞行的**持续拖尾**。
            //
            // 环绕件贴着身体，只能说明「我在灵雾」，说明不了**移动的轨迹**；
            // 这条拖尾让「一路飘过去」有痕迹。限定灵雾形态、且真的在动才发，
            // 悬停不动时不喷（否则原地站着也在冒烟）。
            UpdateMistTrail(Time.deltaTime);

            // 朝向：水平翻转立绘（美术目前只有单侧朝向，先这样表示左右）。
            // 两个立绘节点都要翻 —— 骨骼的网格也在节点局部空间里，翻节点就翻了它。
            FacingFlip.Apply(_artNode, _ctx.Facing, ArtNaturalFacing);
            FacingFlip.Apply(_artSpriteNode, _ctx.Facing, ArtNaturalFacing);
        }

        // ============================================================ 着地判定
        //
        // **按 Layer 判定，不是按节点。**
        //
        // 原写法是 `collision.gameObject == groundNode` —— 只有场景里那一个 Ground
        // 算地面。代价是**任何别的实心表面都不算着地**：站在灵泉石阶上时
        // `EntityForm` 那句「着地就把水平速度归零」不生效，角色会一直滑
        // （实测反馈「灵泉摩擦力不对，踩上去滑」）。
        //
        // 摩擦这条路走不通：青禾自身的物理材质是 friction = 0，而 Box2D 的摩擦是
        // **两边相乘**（sqrt(fA×fB)），所以无论把石阶设成多少，合成结果永远是 0。
        // 真正让角色停下来的一直是那句显式归零 —— 只是它没被触发而已。
        //
        // `groundNode` 保留（场景里存着引用），但不再参与判定，只是没设时给个提示。
        // 代码里原来就留了这条 TODO：「若以后要支持多块地面，把这里换成按 Layer 判定。」

        /// <summary>
        /// 当前托住角色的地面碰撞体。用集合而不是布尔 —— 同时踩着地面和石阶、
        /// 然后离开其中一个时，布尔会被错误地清成 false。
        /// </summary>
        private readonly System.Collections.Generic.HashSet<EntityId> _groundContacts =
            new System.Collections.Generic.HashSet<EntityId>();

        /// <summary>
        /// 这次接触算不算「站在地面上」。
        ///
        /// 两层条件：
        /// <list type="number">
        ///   <item>碰撞体在 <see cref="PhysicsGroups.World"/> 层（地面、机关、石阶…）</item>
        ///   <item>接触法线**朝上** —— 只有从下方托住才算。否则贴着屏障那面墙也会
        ///     算成着地，玩家能在半空中凭空再跳一次。</item>
        /// </list>
        /// </summary>
        private static bool IsGroundContact(Collision2D collision)
        {
            if (collision.gameObject.layer != PhysicsGroups.World)
            {
                return false;
            }

            for (int i = 0; i < collision.contactCount; i++)
            {
                // 法线从「对方」指向青禾，所以站在地面上时 y 为正
                if (collision.GetContact(i).normal.y > 0.5f)
                {
                    return true;
                }
            }

            return false;
        }

        private void TrackGround(Collision2D collision)
        {
            EntityId id = collision.collider.GetEntityId();

            if (IsGroundContact(collision))
            {
                _groundContacts.Add(id);
            }
            else
            {
                _groundContacts.Remove(id);
            }

            RefreshGrounded();
        }

        /// <summary>
        /// 判定「上升中」的速度下限 /世界单位每秒。
        ///
        /// 取 1 而不是 0：静止站立时 Box2D 的求解器会让 <c>linearVelocity.y</c> 在 0 附近
        /// 留一点抖动（含极小的负值），用 0 当阈值会把「站着」也偶尔判成上升。
        /// 一段跳是 320、二段跳是 192，都远在这个阈值之上，不会被误伤。
        /// </summary>
        private const float RisingVelocityEps = 1f;

        /// <summary>
        /// 身体正在上升。
        ///
        /// <b>为什么上升就不算着地</b>：起跳是在 <c>Update</c> 里改的速度，而物理步进在
        /// 下一次 <c>FixedUpdate</c> 才把它积出来 —— 中间那一两步里，**地面的接触还没断**，
        /// <c>OnCollisionStay2D</c> 照常每帧回调。于是刚被起跳置成 false 的
        /// <see cref="FormContext.Grounded"/> 又被这里翻回 true，
        /// <c>EntityForm</c> 末尾那句「着地重置二段跳」就把 <c>_jumpCount</c> 抹了
        /// —— 表现是**凭空多出一条命**、能连跳三次，而且时有时无（取决于那一两步里
        /// 接触有没有断干净）。
        ///
        /// 上升的人本来也不该算「站在地上」，所以这个门既治症也治本。
        /// </summary>
        private bool IsRising()
        {
            return _rigidBody != null && _rigidBody.linearVelocity.y > RisingVelocityEps;
        }

        private void RefreshGrounded()
        {
            if (_ctx == null)
            {
                return;
            }

            bool on = _groundContacts.Count > 0 && !IsRising();

            if (on != _ctx.Grounded)
            {
                _ctx.Grounded = on;
                GameLog.V($"[Qinghe Controller] 着地 {on}");
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            TrackGround(collision);
        }

        /// <summary>
        /// 每帧复核。走进石阶、从地面滑到石阶上这类**不产生 Enter 事件**的情况，
        /// 只有 Stay 能捕捉到 —— 漏了它就会「站在石阶上却不算着地」。
        /// </summary>
        private void OnCollisionStay2D(Collision2D collision)
        {
            TrackGround(collision);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            _groundContacts.Remove(collision.collider.GetEntityId());
            RefreshGrounded();
        }

        // ============================================================ 形态切换响应

        private void OnFormChanged(CompanionFormChangedEvent e)
        {
            _currentForm = e.Current;

            // 切换形态时取消进行中的冲刺，避免状态跨形态泄漏
            if (_ctx != null)
            {
                _ctx.IsDashing = false;
            }

            ApplyFormPhysics();
            ApplyFormTint();
            PlayFormTransition(e);
        }

        /// <summary>
        /// 形态切换的动作：按**进入的形态**选片段。
        /// 化雾 = 实体→灵雾，凝形 = 灵雾→实体，入合 = 任意→灵合。
        /// 灵合直接退回实体/灵雾时没有专属片段，用凝形代（观感上也是"收起来"）。
        /// </summary>
        private void PlayFormTransition(CompanionFormChangedEvent e)
        {
            if (_sequenceView == null)
            {
                _sequenceView = GetComponentInChildren<SpriteSequenceView>();
            }

            if (_sequenceView == null)
            {
                return;
            }

            switch (e.Current)
            {
                case CompanionForm.Mist:
                    _sequenceView.PlayOneShot("青禾-化雾");
                    break;

                case CompanionForm.Merge:
                    _sequenceView.PlayOneShot("青禾-入合");
                    break;

                default:
                    _sequenceView.PlayOneShot("青禾-凝形");
                    break;
            }
        }

        /// <summary>形态切换时调整物理（重力开关）。</summary>
        private void ApplyFormPhysics()
        {
            if (_rigidBody == null)
            {
                return;
            }

            if (_currentForm == CompanionForm.Entity)
            {
                _rigidBody.gravityScale = 1f;

                // 漂浮时刚体可能被 Box2D 自动休眠，需唤醒才会重新受重力下落
                _rigidBody.WakeUp();
            }
            else
            {
                _rigidBody.gravityScale = 0f;

                // 关重力时清掉下落速度，避免残留
                Vector2 v = _rigidBody.linearVelocity;
                _rigidBody.linearVelocity = new Vector2(v.x, 0f);
            }
        }

        /// <summary>
        /// 灰盒视觉：三形态用不同染色区分，否则换形态只有日志、看不出区别。
        /// 正式美术（各形态独立立绘 / 骨骼动画）接入后整个方法可以删掉。
        ///
        /// <b>Unity 侧的一处适配</b>：Cocos 版直接改 <c>Sprite.color</c>；
        /// 这里先找 <see cref="SpriteRenderer"/>，**找不到就静默跳过** ——
        /// 这样青禾立绘无论是静态 Sprite 还是 Spine 骨骼，这段代码都不必改。
        /// </summary>
        private void ApplyFormTint()
        {
            Transform node = SpriteArtNode();

            if (node == null)
            {
                // 全身 Spine 骨骼时这里没有 SpriteRenderer —— 形态区分交给真动画，
                // 染色这套本来也只是灰盒期的临时手段。
                return;
            }

            SpriteRenderer sprite = node.GetComponent<SpriteRenderer>();

            if (sprite == null)
            {
                return;
            }

            switch (_currentForm)
            {
                case CompanionForm.Mist:
                    sprite.color = new Color32(150, 225, 255, 210);   // 青冷半透 —— 灵雾
                    break;
                case CompanionForm.Merge:
                    sprite.color = new Color32(255, 228, 160, 255);   // 暖金 —— 灵合
                    break;
                default:
                    sprite.color = new Color32(255, 255, 255, 255);   // 原色 —— 实体
                    break;
            }
        }

        // ---- 调试 / 测试入口 ----

        public FormContext Context
        {
            get { return _ctx; }
        }

        public CompanionStateMachine StateMachine
        {
            get { return _companionStateMachine; }
        }
    }
}
