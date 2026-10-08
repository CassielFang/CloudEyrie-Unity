using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CloudEyrie
{
    /// <summary>
    /// 序列帧播放器：把美术组给的视频转成的 PNG 序列播出来。
    /// 对应 Cocos 侧 <c>fx/SequencePlayer.ts</c>。
    ///
    /// 用法：挂到带 <see cref="SpriteRenderer"/>（或 uGUI <see cref="Image"/>）的节点上，
    /// 然后 <c>Play("九尾狐-幻影")</c>。
    ///
    /// <b>为什么要滑动窗口</b>
    ///
    /// 九段动画合计 15.7 亿像素 ≈ 6.3GB 原始 RGBA，**全量常驻显存不可能**
    /// （狐火一个小怪就有 88M 像素）。所以这里只保留播放头附近的几页，
    /// 播过去的立刻卸载。驻留页数有上界，与动画总长无关。
    ///
    /// <b>与 Cocos 版的差异</b>
    ///
    /// <list type="bullet">
    ///   <item>引用计数（<c>addRef</c>/<c>decRef</c>）换成「放引用 + 由引擎回收」
    ///     —— <b>不手动 <c>Resources.UnloadAsset</c></b>，原因见
    ///     <see cref="ReleaseTexRef"/>；运行时切出来的 <see cref="Sprite"/> 仍然自己销毁。</item>
    ///   <item><b>Y 轴必须翻转</b>：manifest 是左上原点，Unity 的
    ///     <see cref="Sprite.rect"/> 是左下原点。见 <see cref="BuildSubFrames"/>。</item>
    ///   <item>异步加载沿用 <see cref="Resources.LoadAsync"/>，语义与 Cocos 的
    ///     <c>resources.load</c> 回调一致（页没到位就继续显示上一帧，宁可停一帧也不闪空白）。</item>
    /// </list>
    /// </summary>
    public class SequencePlayer : MonoBehaviour
    {
        [Tooltip("Resources 下的子目录，打包器产出所在处")]
        public string basePath = "video_fx";

        [Tooltip("播放头之后再驻留几页")]
        public int aheadPages = 1;

        /// <summary>
        /// 用**不受时间缩放影响**的时钟推进。
        ///
        /// 给演出层用：<see cref="GameFlow"/> 会把 <c>Time.timeScale</c> 置 0 来停住游戏逻辑，
        /// 而全屏转场必须继续播 —— 用 <c>Time.deltaTime</c> 的话它自己也被停住了，
        /// 转场永远播不完、timeScale 永远回不到 1，游戏就"死"在那里。
        /// </summary>
        [Tooltip("忽略 Time.timeScale。演出层的播放器要打开")]
        public bool unscaledTime;

        [Tooltip("播放头之前再驻留几页；全屏那种大页设 0 最省显存")]
        public int behindPages = 1;

        /// <summary>
        /// 起播前预载。
        /// ⚠️ 与 Cocos 版一致：**这个字段目前没有任何消费方**
        /// （TS 里也是声明了没用到，<c>Play</c> 一律先 <c>EnsureWindow</c>）。
        /// 保留是为了对齐接口、方便以后加「延迟预载」。
        /// </summary>
        public bool preloadBeforePlay = true;

        /// <summary>
        /// ⚠️ <b>必须是 1。</b>Cocos 的 UI 世界里 1 个图集像素 = 1 个世界单位
        /// （Sprite 按 <c>contentSize</c> 渲染），所以帧尺寸直接就是世界尺寸。
        /// 用 Unity 默认的 100 会让所有序列帧缩到 1/100，比碰撞体小两个数量级。
        /// 详见 <c>CloudEyrieTextureImporter</c> 里的推导。
        /// </summary>
        [Tooltip("像素/世界单位。⚠️ 必须是 1 —— 帧尺寸即世界尺寸")]
        public float pixelsPerUnit = 1f;

        /// <summary>
        /// 页数不超过这个值的动画**全部驻留，不做滑动窗口**。
        ///
        /// ⚠️ 滑动窗口是为 Cocos 设计的：那里释放/重载几乎是免费的（引用计数 + 引擎缓存）。
        /// Unity 下 <c>Resources.UnloadAsset</c> + <c>LoadAsync</c> 要真的释放并重传纹理，
        /// 是大开销。
        ///
        /// 小怪的走路动画只有 **2 页**，来回循环时**每一圈都在卸载重载**一张 1940² 的贴图，
        /// 表现就是持续卡顿。而帧图集是压缩过的，多留几页的代价可以接受。
        /// </summary>
        [Tooltip("页数 ≤ 此值就全部驻留（避免小动画反复卸载重载）")]
        public int keepAllPageThreshold = 10;

        private SequenceManifest _manifest;

        /// <summary>
        /// <c>_pages</c> / <c>_frames</c> 里**实际装着的**是哪一段动画。
        ///
        /// 为什么不直接用 <see cref="_manifest"/>：<see cref="LoadManifest"/> 在回调
        /// **之前**就把 <c>_manifest</c> 赋成了新那段，而页与帧缓存还是旧那段的。
        /// 两者在「切换动画」这个窗口里是不一致的，判断缓存内容时必须看这个字段。
        /// </summary>
        private SequenceManifest _loaded;

        private SpriteRenderer _renderer;
        private Image _image;

        /// <summary>
        /// 「上一段动画」的驻留槽（只对小动画保留）。
        ///
        /// ⚠️ <b>为什么需要它</b>：<see cref="StartPlay"/> 原来无条件
        /// <c>Stop()</c> → <c>ReleaseAll()</c> —— 把当前动画的**全部图集页卸载、
        /// 全部子精灵销毁**，再重新 <c>Resources.LoadAsync</c> + <c>Sprite.Create</c>。
        ///
        /// 单次切换（两页、91 帧）实测约 <b>60ms</b>。而小怪是
        /// 「走路 ↔ 攻击」**来回切**的：每个攻击周期切两次，于是每 3.66 秒
        /// （= RangedAttackDuration 1.5 + AttackCooldown 2.16）就有一对 ~120ms 的尖峰。
        /// 实测：小怪开着 55~56 FPS、每 1.8 秒一次 120ms 停顿；
        /// 只关掉 <see cref="MinionVisual"/>（AI/物理照跑）→ 立刻回到 60 FPS、最大 50ms。
        ///
        /// 所以把小动画的整份状态留着，切回来时**直接换指针**，零 I/O、零 Sprite.Create。
        /// <see cref="keepAllPageThreshold"/> 那类动画本来就全部常驻，留着不额外占多少显存
        /// （小怪行走 + 攻击 4 页 ≈ 13MB，且两只小怪共享同一批贴图）。
        ///
        /// 只留**一段**（ping-pong）：这类来回切永远是两段交替，一段足够，
        /// 内存上界也就是「两段小动画」。大动画（Boss / 全屏转场，几十页）照旧全放，
        /// 不能让它们常驻。
        /// </summary>
        private ParkedAnim _parked;

        /// <summary>一段动画的完整驻留状态。见 <see cref="_parked"/>。</summary>
        private sealed class ParkedAnim
        {
            public SequenceManifest Manifest;
            public Dictionary<int, Texture2D> Pages;
            public Dictionary<int, ResourceRequest> Loading;
            public Dictionary<int, List<Sprite>> PageSprites;
            public Dictionary<int, Sprite> Frames;
        }

        /// <summary>已加载的页：页号 → 贴图</summary>
        private Dictionary<int, Texture2D> _pages = new Dictionary<int, Texture2D>();

        /// <summary>正在加载中的页请求，防重复请求</summary>
        private Dictionary<int, ResourceRequest> _loading = new Dictionary<int, ResourceRequest>();

        /// <summary>
        /// 加载失败过的页号 —— **只为避免日志被刷爆**，不影响重试：
        /// 失败的页会从 <see cref="_loading"/> 移除，下次 <see cref="EnsureWindow"/> 还会再试。
        /// 可供自测脚本读取（<see cref="FailedPageCount"/>）来确认资源是否完整。
        /// </summary>
        private readonly HashSet<int> _failedPages = new HashSet<int>();

        /// <summary>加载失败过的页数（诊断用）。> 0 说明有帧图集缺失。</summary>
        public int FailedPageCount
        {
            get { return _failedPages.Count; }
        }

        /// <summary>某页切出来的所有子精灵，释放时逐个销毁</summary>
        private Dictionary<int, List<Sprite>> _pageSprites = new Dictionary<int, List<Sprite>>();

        /// <summary>子帧缓存：帧号 → 裁好的精灵；随页释放一起清</summary>
        private Dictionary<int, Sprite> _frames = new Dictionary<int, Sprite>();

        /// <summary>
        /// 当前正显示在渲染器上的精灵。
        /// 释放页时**绝不能销毁它** —— 否则渲染器指向一个已销毁对象，画面会闪一下空白。
        /// </summary>
        private Sprite _currentSprite;

        /// <summary>
        /// **正显示的那张精灵属于哪段动画**。
        ///
        /// 切动画时页面是异步加载的，中间会有一段「新动画还没出第一帧、屏幕上还是旧动画」的窗口。
        /// 调用方（<see cref="MinionVisual"/>）靠 <see cref="IsShowing"/> 判断这个窗口，
        /// 避免在窗口期把动画冻住 —— 那会让角色**永久停在上一段动画的最后一帧**上。
        /// </summary>
        private string _presentedFrom;

        /// <summary>
        /// 屏幕上现在显示的是不是 <paramref name="animationName"/> 这段动画的帧。
        ///
        /// 切换动画后、新动画的第一帧还没加载出来之前会返回 <c>false</c>。
        /// </summary>
        public bool IsShowing(string animationName)
        {
            return _presentedFrom == animationName;
        }

        /// <summary>待回收的页（贴图 + 它切出来的精灵）。见 <see cref="ReleasePage"/>。</summary>
        private class PendingPage
        {
            public Texture2D Texture;
            public readonly List<Sprite> Sprites = new List<Sprite>();
        }

        private readonly List<PendingPage> _pending = new List<PendingPage>();

        // ===================== 全局贴图引用计数 =====================
        //
        // ⚠️ **同一张图集页会被多个播放器共享。**
        //
        // 两只小怪播同一段「小怪-行走」，各自的 SequencePlayer 都会
        // `Resources.LoadAsync` 到**同一个缓存实例**（Unity 对同一路径返回同一指针）。
        // 若其中一个在释放时直接 `Resources.UnloadAsset`，另一个正在用的贴图就被抽走了
        // —— 表现就是小怪**交替闪烁**。
        //
        // 所以卸载必须按**全局**引用计数：只有最后一个引用消失才真正卸载。
        // 引用有两类，都要算：`_pages` 持有的那一份、以及**每个 Sprite 各持一份**。
        // 后者尤其重要：它保证「当前正显示的精灵」能把它那张贴图钉住。

        private static readonly Dictionary<Texture2D, int> SharedTexRefs =
            new Dictionary<Texture2D, int>();

        private static void AddTexRef(Texture2D tex)
        {
            if (tex == null)
            {
                return;
            }

            SharedTexRefs.TryGetValue(tex, out int n);
            SharedTexRefs[tex] = n + 1;
        }

        /// <summary>有贴图不再被引用，等引擎回收。见 <see cref="ReleaseTexRef"/>。</summary>
        public static bool CleanupRequested
        {
            get { return _cleanupRequested; }
        }

        private static bool _cleanupRequested;

        public static void ClearCleanupRequest()
        {
            _cleanupRequested = false;
        }

        /// <summary>
        /// 放掉一份引用。
        ///
        /// ⚠️ <b>不再调用 <c>Resources.UnloadAsset</c> —— 这是修一个能造成「编辑器卡死」的写法。</b>
        ///
        /// Unity 对 <c>Resources.UnloadAsset</c> 的约束是**绝不能卸载仍被引用的资源**，
        /// 违反时是未定义行为：不报错、不抛异常，直接**卡死或崩溃**。
        /// 而这里原本靠一张**手写的静态引用计数表**决定何时卸载 —— 跨播放器共享贴图、
        /// 又要同时数「页持有的那一份」和「每个 Sprite 各持一份」，任何一处漏加漏减
        /// 都会让计数提前归零、把还在渲染的贴图卸掉。
        ///
        /// 症状与实测反馈完全吻合：**只在画面变化时发作**（小怪/狐火出场、攻击、
        /// 频繁切动画），且**打 Boss 时概率最大**（幻影/狂乱/归心/两段转场/最终净化
        /// 六段大动画轮着换，是全项目贴图 churn 最密集的地方）。
        ///
        /// 改用 <see cref="Resources.UnloadUnusedAssets"/>：它的语义就是「只回收真的
        /// 没人引用的」，由引擎去扫**真实**引用（托管 + 原生），不需要我们维护计数，
        /// 也就不存在「数错了」这回事。代价是要挑时机调（它是一次全量扫描），
        /// 见 <see cref="GameManager"/> 里那个限流的调用点。
        /// </summary>
        private static void ReleaseTexRef(Texture2D tex)
        {
            if (tex == null || !SharedTexRefs.TryGetValue(tex, out int n))
            {
                return;
            }

            n -= 1;

            if (n > 0)
            {
                SharedTexRefs[tex] = n;
                return;
            }

            SharedTexRefs.Remove(tex);

            // 只是**记个标记**，真正的回收交给引擎（见方法注释）
            _cleanupRequested = true;
        }

        private bool _playing;
        private bool _loop = true;
        private float _elapsed;
        private int _index;
        private Action _onComplete;

        /// <summary>本次播放的帧区间（含两端）</summary>
        private int _from;
        private int _to;

        private int _pendingFrom;
        private int _pendingTo = -1;

        /// <summary>播放倍速。美术给的动画按「表演」剪，往往比游戏节奏慢，靠它追上</summary>
        private float _playbackScale = 1f;

        // ============================================================ 生命周期

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _image = GetComponent<Image>();

            if (_renderer == null && _image == null)
            {
                Debug.LogWarning("[Sequence] 节点上没有 SpriteRenderer / Image 组件，序列帧没地方画");
            }
        }

        private void OnDestroy()
        {
            ReleaseAll();

            // force：组件都要没了，正显示的那张也一并销毁，否则它会把贴图的
            // 引用计数钉住（SharedTexRefs 是静态表），变成跨场景的泄漏
            ReleaseParked(true);

            // 组件没了，待回收的页也没人再来 Sweep —— 这里一次性清干净，否则精灵和贴图会泄漏
            foreach (PendingPage p in _pending)
            {
                foreach (Sprite s in p.Sprites)
                {
                    if (s != null)
                    {
                        Destroy(s);
                        ReleaseTexRef(p.Texture);
                    }
                }
            }

            _pending.Clear();
        }

        public bool IsPlaying
        {
            get { return _playing; }
        }

        public int FrameCount
        {
            get { return _manifest != null ? _manifest.frames : 0; }
        }

        /// <summary>当前帧号。Boss 分身靠它对齐相位。</summary>
        public int FrameIndex
        {
            get { return _index; }
        }

        /// <summary>当前播放区间的起止帧</summary>
        public void GetFrameRange(out int from, out int to)
        {
            from = _from;
            to = _to;
        }

        /// <summary>帧尺寸（画布尺寸），调用方可据此摆放。未加载时返回 false。</summary>
        public bool TryGetCanvasSize(out Vector2 size)
        {
            if (_manifest == null)
            {
                size = Vector2.zero;
                return false;
            }

            size = new Vector2(_manifest.CanvasWidth, _manifest.CanvasHeight);
            return true;
        }

        // ============================================================ 对外接口

        /// <summary>
        /// 跳到指定帧（负值会绕回）。
        ///
        /// 给「多个实例播同一段动画、必须长得一模一样」用 —— 各实例是异步加载后
        /// 各自起播的，相位天然错开；Boss 的三个分身不同步的话就看不出是同一个 Boss，
        /// 「1 真 2 假」的辨识前提就没了。
        /// </summary>
        public void SetFrame(int frame)
        {
            if (_manifest == null || _manifest.frames <= 0)
            {
                return;
            }

            int n = _manifest.frames;
            _index = ((frame % n) + n) % n;
            _elapsed = 0f;
            Present();
        }

        /// <summary>只把索引读进来，不播。用于提前准备。</summary>
        public void LoadManifest(string name, Action<bool> cb = null)
        {
            string path = $"{basePath}/{name}/manifest";
            TextAsset asset = Resources.Load<TextAsset>(path);

            if (asset == null)
            {
                Debug.LogWarning($"[Sequence] 读不到索引 {path}");
                cb?.Invoke(false);
                return;
            }

            SequenceManifest m;

            try
            {
                m = JsonUtility.FromJson<SequenceManifest>(asset.text);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Sequence] 索引解析失败 {path}：{e.Message}");
                cb?.Invoke(false);
                return;
            }

            if (m == null || m.entries == null || m.pages == null)
            {
                Debug.LogWarning($"[Sequence] 索引格式不对：{path}");
                cb?.Invoke(false);
                return;
            }

            _manifest = m;
            cb?.Invoke(true);
        }

        /// <summary>
        /// 播放。索引没读过会先读一遍。
        ///
        /// <paramref name="from"/> / <paramref name="to"/> 可以只播一段 ——
        /// 美术给的动画往往**不是为游戏节奏剪的**，例如小怪的攻击动画前 2.5 秒都在
        /// 「拉满弓保持」，整段播会看起来像「箭一直吊着不射」。
        ///
        /// <paramref name="onComplete"/> 只在非循环播放结束时调一次。
        /// </summary>
        public void Play(string name, bool loop = true, int from = 0, int to = -1,
                         float timeScale = 1f, Action onComplete = null)
        {
            _loop = loop;

            // ⚠️ 回调**不能在这里设** —— 下面的 StartPlay() 会先调 Stop()，
            // 而 Stop() 里有一句 `_onComplete = null`（那是给外部主动取消用的），
            // 会把刚设好的回调清掉，播完永远不会通知。
            //
            // 症状极隐蔽：MinionVisual 的「攻击播完回走路」没被调用 → attacking
            // 一直是 true → 走路动画从此不再播，小怪卡成一帧静止图。
            // （Cocos 侧为此排查了很久，见原文件注释。）
            Action wantComplete = onComplete;
            _pendingFrom = from;
            _pendingTo = to;
            _playbackScale = Mathf.Max(0.05f, timeScale);

            // 判据用 _loaded（缓存里真的是哪一段），不是 _manifest —— 两者在
            // 「已换 manifest、页还没装载」的窗口里不一致，见 _loaded 的说明。
            if (_loaded != null && _loaded.name == name)
            {
                // **同一段动画：只把播放头拨回去，页面与精灵全都留着。**
                // 原来这里也走 StartPlay → Stop → ReleaseAll，等于每重播一次
                // 就把自己卸载重载一遍 —— 纯浪费（小怪每次攻击都会重播攻击段）。
                BeginRange(wantComplete);
                return;
            }

            // 上一段正好是这段（小怪「走路 ↔ 攻击」的来回切）：把驻留状态直接换回来，
            // 零 I/O、零 Sprite.Create。见 _parked 的说明。
            if (TryUnpark(name, wantComplete))
            {
                return;
            }

            LoadManifest(name, ok =>
            {
                if (ok)
                {
                    StartPlay(wantComplete);
                }
                else
                {
                    _onComplete?.Invoke();
                }
            });
        }

        private void StartPlay(Action wantComplete)
        {
            // ⚠️ **不要在这里 Stop()。**
            //
            // Stop() 会 ReleaseAll()，把上一段整个卸载重载 —— 而这时 `_manifest`
            // 已经被 LoadManifest 换成了**新**那段，页/帧缓存里装着的却还是旧那段，
            // 于是「放掉旧的」和「装载新的」正好是同一件事，白跑一遍。
            // 小怪的走路↔攻击来回切就是在这里每次烧掉约 60ms。
            //
            // 现在改成：小动画驻留（_parked），大动画才真的放掉。
            ParkOrReleaseCurrent();

            BeginRange(wantComplete);
        }

        /// <summary>
        /// 设定播放区间、把播放头拨回起点并起播。
        ///
        /// **不碰页与精灵缓存** —— 调用方负责保证缓存里已经是这一段动画的帧。
        /// </summary>
        private void BeginRange(Action wantComplete)
        {
            _onComplete = wantComplete;
            _loaded = _manifest;             // 缓存现在装的就是这一段

            int last = _manifest != null ? _manifest.frames - 1 : 0;
            _from = Mathf.Max(0, Mathf.Min(_pendingFrom, last));
            _to = _pendingTo < 0 ? last : Mathf.Max(_from, Mathf.Min(_pendingTo, last));
            _index = _from;
            _elapsed = 0f;
            _playing = true;

            // 先把首窗载好再开播，否则第一圈会卡
            EnsureWindow(_from);
            Present();
        }

        // ============================================================ 驻留槽

        /// <summary>
        /// 把「当前这段」交出去：小动画驻留进 <see cref="_parked"/>，大动画真正释放。
        /// 返回时页/帧缓存是空的，等着下一段填进来。
        ///
        /// ⚠️ 判据用 <see cref="_loaded"/> 而**不是** <c>_manifest</c>：
        /// <see cref="LoadManifest"/> 在回调之前就把 <c>_manifest</c> 换成了新那段，
        /// 拿它判断会把旧动画的页按新动画的页数去归类。
        /// </summary>
        private void ParkOrReleaseCurrent()
        {
            if (_loaded == null)
            {
                ReleaseAll();
                return;
            }

            bool small = _loaded.pages != null &&
                         _loaded.pages.Length <= keepAllPageThreshold;

            if (!small)
            {
                // 大动画（几十页的全屏转场 / Boss 段）不能常驻，照旧全放
                _playing = false;
                _onComplete = null;
                ReleaseAll();
                return;
            }

            // 只留一段：旧的驻留先真正放掉
            ReleaseParked(false);

            _parked = new ParkedAnim
            {
                Manifest = _loaded,
                Pages = _pages,
                Loading = _loading,
                PageSprites = _pageSprites,
                Frames = _frames,
            };

            _pages = new Dictionary<int, Texture2D>();
            _loading = new Dictionary<int, ResourceRequest>();
            _pageSprites = new Dictionary<int, List<Sprite>>();
            _frames = new Dictionary<int, Sprite>();
            _loaded = null;
        }

        /// <summary>
        /// 驻留槽里正好是 <paramref name="name"/> 的话，把它换回来（零 I/O）。
        ///
        /// 换回来的同时把当前这段送进驻留槽 —— 于是「走路 ↔ 攻击」这种两段来回切
        /// 可以无限循环而不产生任何加载。
        /// </summary>
        private bool TryUnpark(string name, Action wantComplete)
        {
            if (_parked == null || _parked.Manifest == null || _parked.Manifest.name != name)
            {
                return false;
            }

            ParkedAnim p = _parked;
            _parked = null;                  // 先摘下来，免得被 ParkOrReleaseCurrent 自己覆盖

            ParkOrReleaseCurrent();

            _manifest = p.Manifest;
            _pages = p.Pages;
            _loading = p.Loading;
            _pageSprites = p.PageSprites;
            _frames = p.Frames;

            BeginRange(wantComplete);
            return true;
        }

        /// <summary>
        /// 真正释放驻留槽。
        ///
        /// <paramref name="force"/> 为 true 时连「正显示的那一张」也销毁
        /// （只有 <see cref="OnDestroy"/> 用 —— 那时画面已经不要了，
        ///   留着它会把贴图的引用计数钉住，变成跨场景的泄漏）。
        /// </summary>
        private void ReleaseParked(bool force)
        {
            ParkedAnim p = _parked;
            _parked = null;

            if (p == null)
            {
                return;
            }

            // 先放掉 `Pages` 持有的那一份引用；各精灵自己那一份在下面逐个放
            foreach (Texture2D tex in p.Pages.Values)
            {
                ReleaseTexRef(tex);
            }

            foreach (KeyValuePair<int, List<Sprite>> kv in p.PageSprites)
            {
                p.Pages.TryGetValue(kv.Key, out Texture2D tex);

                foreach (Sprite s in kv.Value)
                {
                    if (s == null || (!force && s == _currentSprite))
                    {
                        continue;            // 正显示的那张不销毁 —— 销毁它会闪一下空白
                    }

                    Destroy(s);
                    ReleaseTexRef(tex);
                }
            }

            p.Pages.Clear();
            p.PageSprites.Clear();
            p.Frames.Clear();
            p.Loading.Clear();
        }

        public void Stop()
        {
            _playing = false;
            _onComplete = null;
            ReleaseAll();
            ReleaseParked(false);
        }

        /// <summary>停在当前帧，不释放页</summary>
        public void Pause()
        {
            _playing = false;
        }

        public void Resume()
        {
            if (_manifest != null)
            {
                _playing = true;
            }
        }

        // ============================================================ 主循环

        private void Update()
        {
            PumpLoading();

            if (!_playing || _manifest == null)
            {
                // 没在播也要回收：停播后待销毁的页得清掉
                SweepPending();
                return;
            }

            float fps = _manifest.fps > 0f ? _manifest.fps : 24f;
            float step = 1f / (fps * _playbackScale);

            // 演出层用未缩放时钟（见 unscaledTime 的说明）
            _elapsed += unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            // while 而不是 if：掉帧时一次补多帧，避免慢机器上动画变慢
            while (_elapsed >= step)
            {
                _elapsed -= step;
                _index += 1;

                if (_index > _to)
                {
                    if (_loop)
                    {
                        _index = _from;
                    }
                    else
                    {
                        _index = _to;
                        _playing = false;
                        Present();
                        Action done = _onComplete;
                        _onComplete = null;
                        done?.Invoke();
                        return;
                    }
                }
            }

            Present();
        }

        /// <summary>推进所有异步页请求（对应 Cocos 的 load 回调）。</summary>
        private void PumpLoading()
        {
            if (_loading.Count == 0)
            {
                return;
            }

            List<int> finished = null;

            foreach (KeyValuePair<int, ResourceRequest> kv in _loading)
            {
                if (!kv.Value.isDone)
                {
                    continue;
                }

                finished ??= new List<int>();
                finished.Add(kv.Key);
            }

            if (finished == null)
            {
                return;
            }

            foreach (int page in finished)
            {
                ResourceRequest req = _loading[page];
                _loading.Remove(page);

                Texture2D tex = req.asset as Texture2D;

                if (tex == null)
                {
                    // ⚠️ 页读不到 = 这一段动画会**永久卡住**（没有帧可显示，也没有重试）。
                    // 原来是只有一条 warning，静默失败、极难排查。
                    // 这里把它记进 _failedPages：一是避免每帧重试把日志刷爆，
                    // 二是留个可查的证据（FailedPageCount 给自测脚本读）。
                    if (_failedPages.Add(page))
                    {
                        Debug.LogError($"[Sequence] 图集页加载失败：{PagePath(page)}（第 {page} 页，" +
                                       $"共 {_manifest?.pages?.Length ?? 0} 页）—— " +
                                       $"这段动画会停在上一帧不再前进");
                    }

                    continue;
                }

                // 加载期间窗口可能已经移走了，那就别留着
                if (_manifest == null || _manifest.entries == null || _manifest.entries.Length == 0)
                {
                    // 同样不直接卸载（见 ReleaseTexRef 的说明），交给引擎回收
                    _cleanupRequested = true;
                    continue;
                }

                FrameEntry cur = _manifest.entries[Mathf.Clamp(_index, 0, _manifest.entries.Length - 1)];
                int last = _manifest.pages.Length - 1;
                int lo = Mathf.Max(0, cur.page - behindPages);
                int hi = Mathf.Min(last, cur.page + aheadPages);

                // 与 EnsureWindow 同一套窗口口径（含「起播页一定在内」），
                // 见那里的说明 —— 不要再按「区间末页」拉右边界。
                int startPage = PageOfFrame(_from);

                if (startPage >= 0 && Mathf.Abs(cur.page - startPage) <= keepAllPageThreshold)
                {
                    lo = Mathf.Min(lo, startPage);
                }

                if (page < lo || page > hi)
                {
                    _cleanupRequested = true;
                    continue;
                }

                _pages[page] = tex;
                AddTexRef(tex);          // `_pages` 持有的那一份
                BuildSubFrames(page, tex);
            }
        }

        // ============================================================ 内部

        private void Present()
        {
            if (_manifest == null || (_renderer == null && _image == null))
            {
                return;
            }

            if (_manifest.entries == null || _index < 0 || _index >= _manifest.entries.Length)
            {
                return;
            }

            FrameEntry e = _manifest.entries[_index];
            EnsureWindow(e.page);

            // ⚠️ **只在真的换了帧时才赋值。**
            //
            // Cocos 那边每帧 `sprite.spriteFrame = sf` 是几乎免费的；Unity 的
            // `SpriteRenderer.sprite` 每次赋值都会触发渲染器重建 —— 每帧无脑赋值是纯开销
            // （小怪多的时候就是可感的掉帧）。
            if (_frames.TryGetValue(_index, out Sprite sf) && sf != _currentSprite)
            {
                _currentSprite = sf;
                _presentedFrom = _manifest != null ? _manifest.name : null;

                if (_renderer != null)
                {
                    _renderer.sprite = sf;
                }
                else
                {
                    _image.sprite = sf;
                }
            }

            // 页还没加载完就继续显示上一帧，宁可停一帧也不闪空白
            SweepPending();
        }

        private int PageOfFrame(int frame)
        {
            if (_manifest == null || _manifest.entries == null ||
                frame < 0 || frame >= _manifest.entries.Length)
            {
                return -1;
            }

            return _manifest.entries[frame].page;
        }

        /// <summary>
        /// 只保留 [当前页 - behind, 当前页 + ahead]，其余释放。
        ///
        /// 另外**永远把起播页拉进窗口**：循环回到 <c>_from</c> 的那一瞬间不该现加载。
        ///
        /// <b>⚠️ 这里曾经还会把窗口右边界拉到「播放区间的末页」，已删除 —— 那是个严重的坑。</b>
        ///
        /// 原写法是 <c>hi = Mathf.Max(hi, PageOfFrame(_to))</c>，本意是「只播一小段时，
        /// 保证那一段的页都驻留」。但 <c>Present()</c> 每帧都会调到这里，而**整段播放**
        /// （<c>to = -1</c>，所有 Boss 形态动画与全屏转场都是这么播的）的 `_to` 就是最后一帧
        /// → 右边界被拉到**最后一页** → 滑动窗口整个失效，动画有多少页就常驻多少页。
        ///
        /// 两段 Boss 转场各有 33 / 30 页（1924×1626），本该只驻留窗口内 2 页，
        /// 被这一行拉成全部加载且永不释放 —— 编辑器直接卡死。
        /// 这个 bug 是既有代码里就有的，只是转场此前从未接进场景，所以一直没被触发。
        ///
        /// 删掉是对的：播放头是**顺序前进**的，窗口跟着它走就够，`aheadPages` 负责提前量；
        /// 真正需要「整段常驻」的小动画本来就由 <see cref="keepAllPageThreshold"/> 兜住了。
        /// </summary>
        private void EnsureWindow(int page)
        {
            if (_manifest == null || _manifest.pages == null)
            {
                return;
            }

            int last = _manifest.pages.Length - 1;

            // 页数少的动画直接全部驻留 —— 见 keepAllPageThreshold 的说明。
            // 这类动画的页数上界很小，全留的显存代价远低于反复卸载重载的开销。
            if (_manifest.pages.Length <= keepAllPageThreshold)
            {
                for (int p = 0; p <= last; p++)
                {
                    LoadPage(p);
                }

                return;
            }

            int lo = Mathf.Max(0, page - behindPages);
            int hi = Mathf.Min(last, page + aheadPages);

            // 起播页也钉住，循环回起点时不用现加载。
            //
            // ⚠️ 但只在**离得近**的时候钉：长动画的起点和播放头可能隔着几十页，
            // 无条件拉过来等于又把整段全载进来（就是上面刚修掉的那个坑的另一种触发方式）。
            // 隔得远就认了那一次加载 —— 长动画本来也不该整段常驻。
            int startPage = PageOfFrame(_from);

            if (startPage >= 0 && Mathf.Abs(page - startPage) <= keepAllPageThreshold)
            {
                lo = Mathf.Min(lo, startPage);
            }

            for (int p = lo; p <= hi; p++)
            {
                LoadPage(p);
            }

            List<int> loaded = null;

            foreach (int p in _pages.Keys)
            {
                if (p < lo || p > hi)
                {
                    loaded ??= new List<int>();
                    loaded.Add(p);
                }
            }

            if (loaded != null)
            {
                foreach (int p in loaded)
                {
                    ReleasePage(p);
                }
            }
        }

        private string PagePath(int page)
        {
            if (_manifest == null || _manifest.pages == null ||
                page < 0 || page >= _manifest.pages.Length)
            {
                return "(越界)";
            }

            // "page_00.png" → "page_00"
            string name = System.IO.Path.GetFileNameWithoutExtension(_manifest.pages[page]);
            return $"{basePath}/{_manifest.name}/{name}";
        }

        private void LoadPage(int page)
        {
            if (_manifest == null || _pages.ContainsKey(page) || _loading.ContainsKey(page))
            {
                return;
            }

            string path = PagePath(page);

            if (path == "(越界)")
            {
                return;
            }

            ResourceRequest req = Resources.LoadAsync<Texture2D>(path);

            if (req == null)
            {
                Debug.LogWarning($"[Sequence] 发起加载失败 {path}");
                return;
            }

            _loading[page] = req;
        }

        /// <summary>
        /// 把这一页里属于本动画的所有帧裁出来缓存好，播放时直接取。
        ///
        /// ⚠️ <b>Y 轴翻转在这里</b>：manifest（<c>pack_atlas.py</c> 的产出）是**左上原点**，
        /// 而 Unity 的 <see cref="Sprite.rect"/> 是**左下原点**。
        /// 不翻的话每段动画都会上下镜像，且不报任何错。
        /// </summary>
        private void BuildSubFrames(int page, Texture2D tex)
        {
            if (_manifest == null || _manifest.entries == null)
            {
                return;
            }

            List<Sprite> created = new List<Sprite>();
            _pageSprites[page] = created;

            // 统一按中心锚点 —— 与 Cocos 版一致（那边构造子帧时也没有覆写锚点）
            Vector2 pivot = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < _manifest.entries.Length; i++)
            {
                FrameEntry e = _manifest.entries[i];

                if (e.page != page || _frames.ContainsKey(i))
                {
                    continue;
                }

                // 左上原点 → 左下原点
                float yBottom = tex.height - (e.y + e.h);

                Rect rect = new Rect(e.x, yBottom, e.w, e.h);
                Sprite sf = Sprite.Create(tex, rect, pivot, pixelsPerUnit);

                if (sf == null)
                {
                    continue;
                }

                sf.name = $"{_manifest.name}_f{i}";

                // 每个精灵各持一份贴图引用 —— 这样「正显示的那一帧」能把它钉住，
                // 别的播放器释放页面也抽不走
                AddTexRef(tex);

                _frames[i] = sf;
                created.Add(sf);
            }
        }

        /// <summary>
        /// 释放一页。
        ///
        /// ⚠️ <b>不立刻销毁精灵</b> —— 这一页切出来的帧里，可能有一张**正显示在屏幕上**。
        /// 立刻 <c>Destroy</c> 会让 <c>SpriteRenderer.sprite</c> 指向一个已销毁对象，
        /// 表现为**画面闪一下空白**。而下一页是**异步加载**的，中间必然存在这个空档。
        ///
        /// （Cocos 版没这个问题：它用 <c>addRef</c>/<c>decRef</c> 引用计数，
        ///   贴图会等到没人引用才真正释放。我第一版照搬了「释放」的结构却换成「立即销毁」，
        ///   语义就变了 —— 症状是小怪一直在闪。）
        ///
        /// 正解是把它们丢进 <see cref="_pending"/>，由 <see cref="SweepPending"/> 择机回收。
        /// </summary>
        private void ReleasePage(int page)
        {
            if (!_pages.TryGetValue(page, out Texture2D tex))
            {
                return;
            }

            _pages.Remove(page);
            ReleaseTexRef(tex);          // 放掉 `_pages` 那一份；各 Sprite 自己的那份还在

            PendingPage pending = new PendingPage { Texture = tex };

            if (_pageSprites.TryGetValue(page, out List<Sprite> sprites))
            {
                pending.Sprites.AddRange(sprites);
                _pageSprites.Remove(page);
            }

            _pending.Add(pending);

            if (_manifest != null && _manifest.entries != null)
            {
                for (int i = 0; i < _manifest.entries.Length; i++)
                {
                    if (_manifest.entries[i].page == page)
                    {
                        _frames.Remove(i);
                    }
                }
            }
        }

        /// <summary>
        /// 回收待销毁的页：跳过**正显示的那一张**，其余销毁；贴图等它的精灵都清空后再卸载。
        /// </summary>
        private void SweepPending()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                PendingPage p = _pending[i];

                for (int j = p.Sprites.Count - 1; j >= 0; j--)
                {
                    Sprite s = p.Sprites[j];

                    if (s == null)
                    {
                        p.Sprites.RemoveAt(j);
                        continue;
                    }

                    // 正显示的那一张留着 —— 销毁它画面会闪
                    if (s == _currentSprite)
                    {
                        continue;
                    }

                    Destroy(s);
                    ReleaseTexRef(p.Texture);   // 销毁一个精灵就放掉它那一份引用
                    p.Sprites.RemoveAt(j);
                }

                // 贴图**什么时候卸载由全局引用计数决定**（最后一个引用消失时自动卸），
                // 这里只负责把空壳摘掉。
                //
                // 不再需要「这一页是不是又被加载回来了」这类判断 —— 计数天然覆盖了它：
                // 只要还有一个 Sprite 指着这张贴图（哪怕是别的播放器的、或者是当前显示的这一帧），
                // 计数就 > 0，谁都卸不掉它。
                if (p.Sprites.Count == 0)
                {
                    _pending.RemoveAt(i);
                }
            }
        }

        private void ReleaseAll()
        {
            List<int> pages = new List<int>(_pages.Keys);

            foreach (int p in pages)
            {
                ReleasePage(p);
            }

            _pages.Clear();
            _pageSprites.Clear();
            _frames.Clear();
            _loaded = null;
        }
    }
}
