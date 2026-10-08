using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 阳浊小怪的表现层：按 AI 状态切序列帧动画。
    /// 对应 Cocos 侧 <c>enemy/MinionVisual.ts</c>。
    ///
    /// 美术组给的是**逐帧动画**（视频转的），不是骨骼，所以「走路」「攻击」是两段
    /// 独立的序列，得由状态驱动着切：
    /// <code>
    /// Patrol / Discover / Chase（移动中） → 小怪-行走（循环）
    /// Attack                             → 小怪-攻击（一次性，播完回走路）
    /// Dead                               → 停播（死亡表现交给 EnemyAI 的缩放淡出）
    /// </code>
    ///
    /// 挂在**小怪节点**上（和 <see cref="EnemyAI"/>、<see cref="Damageable"/> 同节点），
    /// Art 子节点会被自动接上 <see cref="SequencePlayer"/>。
    ///
    /// 注意朝向：<see cref="EnemyAI"/> 已经在翻转 Art 节点（<see cref="FacingFlip"/>），
    /// 这里只管播哪一段，**不要再自己翻一次**，否则会翻回去。
    /// </summary>
    [RequireComponent(typeof(EnemyAI))]
    public class MinionVisual : MonoBehaviour
    {
        [Tooltip("立绘节点名")]
        public string artName = "Art";

        [Tooltip("行走动画")]
        public string walkAnimation = "小怪-行走";

        [Tooltip("攻击动画")]
        public string attackAnimation = "小怪-攻击";

        [Tooltip("立绘缩放。乘以打包后的帧尺寸；改动 pack_atlas 的 --scale 后要跟着调")]
        public float artScale = 1f;

        /// <summary>
        /// 走路动画的播放倍速。默认原速。
        ///
        /// 曾试过压到 0.5 去迁就巡逻速度，但那样步子显得拖沓 —— 反馈是「又有点慢了」。
        /// 改成「动画保持原速、把移动速度提上去」来对节奏。
        /// </summary>
        [Tooltip("走路倍速。1 = 原速；和巡逻速度搭配着调")]
        public float walkTimeScale = 1f;

        [Tooltip("人物底边距帧画布底边的占比。小怪-行走 实测 33/256 ≈ 0.129 —— " +
                 "脚底下那截透明留白，不扣掉小怪会**悬空**")]
        public float artBottomPaddingRatio = 0.129f;

        /// <summary>
        /// 攻击动画的播放区间与倍速。
        ///
        /// 美术给的是 97 帧 / 4.04 秒的**完整表演**，逐帧分析（帧间差）得到：
        /// <code>
        /// f2~ 5  起步
        /// f6~42  抬弓 + 拉弓
        /// f43~54 拉满后微调
        /// f55~61 满弓保持（静止）
        /// f62~69 【放箭】
        /// f70~96 箭飞 + 收势
        /// </code>
        ///
        /// <b>整段从头播、不剪</b>（<c>AttackFrom = 2</c>）—— 之前只从 f42「已拉满」起播，
        /// 结果是走路姿势**直接跳到满弓**，硬切很明显。保留抬弓那一段，衔接才自然。
        ///
        /// 但 4 秒太慢，所以用倍速把它压到约 <b>1.5 秒</b>。
        /// </summary>
        /// <summary>
        /// ⚠️ 这三个原本是 <c>const</c>，**改成 Inspector 字段了**。
        ///
        /// 它们是**跟着美术素材走的**（美术重新剪一版攻击动画，帧号就全变），
        /// 而 <c>const</c> 意味着每调一次都要改代码、重编译 —— 正是「调参面板要能调」
        /// 要避免的事。现在选中挂 MinionVisual 的节点就能改。
        ///
        /// 默认值来自对原素材的逐帧分析（帧间差）：f2~5 起步、f6~42 抬弓拉弓、
        /// f43~54 微调、f55~61 满弓保持、**f62 放箭**、f70~96 箭飞收势。
        /// </summary>
        [Tooltip("攻击动画的起始帧（含）")]
        public int attackFrom = 2;

        [Tooltip("攻击动画的结束帧（含）")]
        public int attackTo = 90;

        /// <summary>撒手那一帧：箭在此时生成，和动画严格同步</summary>
        [Tooltip("放箭帧 —— 箭在这一帧生成，与动画严格对齐")]
        public int releaseFrame = 62;

        /// <summary>
        /// 张弓搭箭的倍速**从配置读**（<c>GameConfig.Enemy.RangedAttackTimeScale</c>），
        /// 这样在 Inspector 里就能调，不必改代码重编译。
        /// 惰性读取、不缓存。
        /// </summary>
        private float AttackTimeScale
        {
            get { return GameConfig.Current.Enemy.RangedAttackTimeScale; }
        }

        /// <summary>
        /// 判定「算不算在移动」的速度阈值（米/秒，刚体单位）。
        /// 巡逻速度是 1.44，站住时理论上为 0，留一点余量盖住数值抖动。
        /// </summary>
        private const float MoveEps = 0.05f;

        private EnemyAI _ai;
        private SequencePlayer _player;
        private Rigidbody2D _rigidBody;
        private Transform _artNode;
        private bool _artAligned;
        private bool _attacking;

        /// <summary>当前是否在播走路循环 —— 避免重复触发「释放 + 重载」</summary>
        private bool _walking;

        /// <summary>本次攻击是否已经放过箭（防止同一段动画里重复触发）</summary>
        private bool _released;

        private void Awake()
        {
            _ai = GetComponent<EnemyAI>();
            _rigidBody = GetComponent<Rigidbody2D>();

            if (_ai == null)
            {
                Debug.LogWarning("[MinionVisual] 同节点没有 EnemyAI，动画不会随状态切换");
            }
        }

        /// <summary>
        /// ⚠️ 美术相关的初始化放在 <c>Start</c> 而**不是 Awake**。
        ///
        /// 原因：<see cref="LevelDirector"/> 是这样造小怪的 ——
        /// <code>
        /// MinionVisual mv = n.AddComponent&lt;MinionVisual&gt;();  // ← Awake 在这一刻同步执行
        /// mv.artScale = 0.66f;                                 // ← 赋值发生在 Awake 之后
        /// </code>
        /// <c>AddComponent</c> 会**立刻同步调用 Awake**，所以 Awake 里读到的
        /// <c>artScale</c> 还是字段默认值 1 —— 立绘会比预期大 1.52 倍。
        ///
        /// Unity 保证「所有 Awake 跑完才跑 Start」，放这里就能拿到生成方设的值。
        /// （Cocos 侧记过同源的坑：「子节点必须在依赖它的组件之前建」——
        ///   本质都是「运行时装配的时序必须对」。）
        /// </summary>
        private void Start()
        {
            Transform art = transform.Find(artName);

            if (art == null)
            {
                Debug.LogWarning($"[MinionVisual] 找不到 \"{artName}\" 子节点");
                return;
            }

            art.localScale = new Vector3(artScale, artScale, 1f);
            _artNode = art;

            if (art.GetComponent<SpriteRenderer>() == null)
            {
                art.gameObject.AddComponent<SpriteRenderer>();
            }

            _player = art.GetComponent<SequencePlayer>();

            if (_player == null)
            {
                _player = art.gameObject.AddComponent<SequencePlayer>();
            }

            // 小怪同屏可能好几只，各留 1 页就够
            _player.aheadPages = 0;
            _player.behindPages = 0;

            PlayWalk();
        }

        private void PlayWalk()
        {
            // ⚠️ **已经在走就别重播。**
            //
            // `Play` 内部会 `Stop()` → 全页释放 → 重新异步加载，在 Unity 下是实打实的开销
            // （掉帧的来源之一）。而 `Update` 在每次状态变化时都会调到这里 ——
            // 巡逻↔发现↔追击来回切时就会反复释放重载同一段动画。
            //
            // ⚠️ **判据必须带上 `IsShowing`，只看 `IsPlaying` 不够。**
            //
            // `SyncWalkPlayback` 会在小怪站住时 `Pause()`（那里把 `_playing` 置 false）。
            // 而本函数随后**每帧**都会被执行到 —— 只看 `IsPlaying` 的话，暂停的下一帧就
            // 判定成「没在走」→ 重播 → `Stop()` 释放全部图集页 → 重新 `LoadAsync`、
            // 重新 `Sprite.Create` 出这一段的**每一帧**。站立期间就变成「每帧重建近百个
            // Sprite」，小怪一停就掉帧（巡逻到点停留 0.8s、攻击冷却 2s 都在停顿）。
            //
            // 带上 `IsShowing` 之后：暂停中（显示的就是走路帧）直接返回，交给
            // `SyncWalkPlayback` 在真的动起来时 `Resume()`。两者构成互补的一对。
            if (_walking && _player != null &&
                (_player.IsPlaying || _player.IsShowing(walkAnimation)))
            {
                return;
            }

            _walking = true;
            _attacking = false;

            if (_player != null)
            {
                _player.Play(walkAnimation, true, 0, -1, walkTimeScale);
            }
        }

        /// <summary>
        /// 站着不动时把走路动画**冻住**，要动了再继续。
        ///
        /// 判据取**刚体速度**而不是 AI 状态 —— 巡逻里有一段「到点停留」（<c>PatrolPause</c>）、
        /// 攻击冷却时也会站着，这些都属同一状态但人没动。
        /// 按状态切的话那些时候会原地踏步（实测反馈「没移动的时候就静止」）。
        /// </summary>
        private void SyncWalkPlayback()
        {
            SequencePlayer pl = _player;

            if (pl == null || _attacking)
            {
                return;                       // 攻击中由攻击动画自己管
            }

            bool moving = _rigidBody != null
                ? Mathf.Abs(_rigidBody.linearVelocity.x) > MoveEps
                : true;

            if (moving && !pl.IsPlaying)
            {
                pl.Resume();
            }
            else if (!moving && pl.IsPlaying && pl.IsShowing(walkAnimation))
            {
                // ⚠️ **必须等走路动画真的出了第一帧才允许暂停。**
                //
                // 攻击打完切回走路时，那一页是**异步加载**的，中间有一段窗口屏幕上还是
                // 攻击动画的最后一帧。小怪贴着玩家等攻击冷却时刚体速度为 0，会立刻触发暂停
                // —— 一暂停就再也不去显示走路帧了，**永久冻在攻击姿势上**。
                //
                // （Cocos 那边没这问题：图集页几乎即时可用，暂停时已经是走路帧了。
                //   Unity 的异步加载把这个时间窗暴露了出来。）
                pl.Pause();
            }
        }

        /// <summary>切到攻击动画。**幂等** —— 已经在攻击中就直接返回（见 <see cref="Update"/> 的说明）。</summary>
        private void PlayAttack()
        {
            if (_attacking)
            {
                return;
            }

            _attacking = true;
            _walking = false;
            _released = false;

            if (_player == null)
            {
                return;
            }

            _player.Play(attackAnimation, false, attackFrom, attackTo, AttackTimeScale, () =>
            {
                // 播完回走路；若这期间已死则让死亡逻辑接手
                if (_ai == null || _ai.GetState() != EnemyState.Dead)
                {
                    PlayWalk();
                }
            });
        }

        /// <summary>
        /// 把**人物底边**对齐到碰撞体底边。
        ///
        /// 不这么做小怪会**悬空**：立绘按节点中心摆，而小怪的碰撞体被刻意下移了 21
        /// （Cocos 原设计，见 <c>LevelDirector.MakeMinion</c> 的说明），立绘没跟着挪，
        /// 于是视觉上比落地位置高 20+ 单位。
        ///
        /// 要等画布尺寸就绪才能算，所以放在 <see cref="Update"/> 里做一次性的对齐
        /// （序列帧是异步加载的，<c>Start</c> 时还拿不到尺寸）。
        /// </summary>
        private void AlignArtToGround()
        {
            if (_artAligned || _player == null || _artNode == null)
            {
                return;
            }

            if (!_player.TryGetCanvasSize(out Vector2 size) || size.y <= 0f)
            {
                return;
            }

            BoxCollider2D col = GetComponent<BoxCollider2D>();

            if (col == null)
            {
                _artAligned = true;
                return;
            }

            float canvasH = size.y * artScale;
            float colliderBottom = col.offset.y - col.size.y * 0.5f;

            // 人物底边 = 碰撞体底边；画布底边再往下让出那段透明留白
            float canvasBottom = colliderBottom - artBottomPaddingRatio * canvasH;
            _artNode.localPosition = new Vector3(0f, canvasBottom + canvasH * 0.5f, 0f);

            _artAligned = true;
            Debug.Log($"[MinionVisual] 立绘对齐：画布高 {canvasH:F0} 中心y {_artNode.localPosition.y:F1} " +
                      $"（碰撞体底边 {colliderBottom}）");
        }

        private void Update()
        {
            AlignArtToGround();

            EnemyAI ai = _ai;

            if (ai == null || _player == null)
            {
                return;
            }

            // 到了撒手那一帧才放箭，动画和抛射物严格同步
            if (_attacking && !_released && _player.FrameIndex >= releaseFrame)
            {
                _released = true;
                ai.ReleaseArrow();
            }

            SyncWalkPlayback();

            // ⚠️ **每帧把动画对齐到当前状态，而不是「只在状态变化那一帧切一次」。**
            //
            // 原来这里是 `if (_lastState == s) return;` —— 边沿触发。一旦那次切换以任何方式
            // 没生效（页面异步加载被打断、回调丢失），**就再没有第二次机会**，
            // 小怪会永久停在错的动画上。实测踩到过：打完一击后冻在攻击动画的最后一帧。
            //
            // 改成电平触发后，任何状态漂移都会在**下一帧自动纠正**。
            // 代价几乎为零 —— PlayWalk / PlayAttack 各自幂等，到位就直接返回。
            EnemyState s = ai.GetState();

            if (s == EnemyState.Dead)
            {
                if (_walking || _attacking)
                {
                    _player.Stop();
                    _walking = false;
                    _attacking = false;
                }

                return;
            }

            if (s == EnemyState.Attack)
            {
                PlayAttack();
            }
            else
            {
                // 巡逻 / 发现 / 追击 都走走路循环
                PlayWalk();
            }
        }
    }
}
