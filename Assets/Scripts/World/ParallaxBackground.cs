using System;
using System.Collections.Generic;
using UnityEngine;
using CloudEyrie.Fx;

namespace CloudEyrie
{
    /// <summary>背景层配置。</summary>
    [Serializable]
    public class LayerSpec
    {
        /// <summary>Resources 下的路径</summary>
        public string path;

        /// <summary>视差系数：1 = 与地面同速（跟着世界走），0 = 钉死在屏幕上（无穷远）</summary>
        public float factor;

        /// <summary>相对画面中心的 y 偏移</summary>
        public float yOffset;

        /// <summary>
        /// 目标世界高度：**不够高就整体放大**（等比，宽度跟着涨）。
        ///
        /// 为什么需要它：本作的背景图都是「一条带 + 上下透明边」的长卷
        /// （远景 1920×1080 里可见的云带只占第 300~816 行）。只按宽度贴合的话
        /// 云带在世界里只有 ~420 单位高，屏幕顶部就露出一条空白 ——
        /// 实测反馈「远景没有铺满」。给一个目标高度让可见带够高。
        ///
        /// 取 <c>max(按宽度算的倍率, 按高度算的倍率)</c>，所以只会放大不会缩小。
        /// </summary>
        public float height;

        /// <summary>
        /// 自身漂移的**振幅**（世界单位），与相机无关，按 <c>driftPeriod</c> 来回摆。
        /// 远景是云，静止的话像贴纸。
        ///
        /// ⚠️ 是**振幅**不是速度：早先写成「每秒位移」并无限累加，云会一直往一边走，
        /// 走上几分钟就把图层整个推出屏幕、露出空白。现在按正弦来回摆，永远有界。
        /// </summary>
        public float drift;

        /// <summary>一次完整来回的秒数</summary>
        public float driftPeriod = 40f;

        /// <summary>
        /// 是否跟随 <see cref="ParallaxBackground.autoPanSpeed"/> 的自动平移
        /// （开场过场 / Boss 战场那份「背景缓慢推移」）。
        ///
        /// **默认关**：只有远景的云需要自己动，中景与山景保持不动。
        /// </summary>
        public bool autoPan;
    }

    /// <summary>沿关卡**散布**的景物（不是铺满一屏的整层）。</summary>
    [Serializable]
    public class ScenerySpec
    {
        /// <summary>Resources 下的路径</summary>
        public string path;

        /// <summary>视差系数：越接近 1 越贴近玩法层</summary>
        public float factor;

        /// <summary>目标高度（世界单位），等比缩放</summary>
        public float height;

        /// <summary>底边对齐的 y</summary>
        public float bottomY;

        /// <summary>摆放位置（关卡 x）</summary>
        public float[] xs;
    }

    /// <summary>
    /// 视差背景：把长卷拆出的远景 / 中景分层，按不同速度滚动。
    /// 对应 Cocos 侧 <c>world/ParallaxBackground.ts</c>。
    ///
    /// 核心一行（见 <see cref="Apply"/>）：<c>x = base + (cameraX − camMin) × (1 − factor)</c>。
    /// 相机本身被 <see cref="CameraFollow"/> 移动 <c>+cameraX</c>，所以：
    /// <list type="bullet">
    ///   <item><c>factor = 1</c> → 节点位移 0 → 和地面一样跟着世界走</item>
    ///   <item><c>factor = 0</c> → 节点位移 = 相机位移 → 两层相消，看上去钉在屏幕上（无穷远）</item>
    /// </list>
    ///
    /// <b>为什么中景要指定宽度</b>
    ///
    /// 中景只有一屏宽，而关卡横向有好几屏。视差系数 0.45 意味着相机走 1 屏它要走 0.45 屏，
    /// 所以至少要 <c>一屏 + 0.45 × 相机行程</c> 才不露边。宽度不够时**拉伸而不是平铺**
    /// —— 中景是一幅完整构图（左草坡、中灵瀑、右山门），平铺会在接缝处把草坡怼到山门上。
    ///
    /// <b>Unity 侧的一处减法</b>
    ///
    /// Cocos 版有个 <c>mergeDefaults()</c>，用来给「场景里存的旧数组」补新字段
    /// （Cocos 的 <c>@property</c> 数组是逐元素整体序列化的，加字段后老场景补不上、
    /// 新字段恒为 <c>undefined</c>）。**Unity 的序列化器会自动用类默认值补齐缺失字段**，
    /// 所以那段整个不需要 —— 这是换引擎白拿的一处简化。
    /// </summary>
    public class ParallaxBackground : MonoBehaviour
    {
        /// <summary>
        /// 默认层配置：从远到近，绘制顺序同数组顺序。
        ///
        /// 远景 <c>height 1350</c> 与 <c>yOffset 120</c> 是一起定的：
        /// <code>
        /// 云带顶边 = 120 + 240×1.25 = 420（屏幕顶 360 之上，留 60 余量）
        /// 云带底边 = 120 − 276×1.25 = −225（中景带顶边 227，两者接得上、不漏）
        /// </code>
        /// 于是 227~420 这一段屏幕只会看到远景 —— 远近层次才分得出来。
        /// （原先 yOffset 70、没有 height：云带只到 266，且 96% 被中景挡住，等于看不见。）
        /// </summary>
        public static readonly LayerSpec[] DefaultLayers =
        {
            new LayerSpec
            {
                path = "背景/远景", factor = 0.15f, yOffset = 120f, height = 1350f,
                drift = 26f, driftPeriod = 36f, autoPan = true,
            },
            new LayerSpec { path = "背景/中景", factor = 0.45f, yOffset = -40f },
        };

        /// <summary>
        /// 默认景物：几座山峰错落在中景之后。
        ///
        /// ⚠️ 用的是 <c>山·透明</c> 而不是 <c>山</c> —— **原图是画在白纸上的、没有 alpha**，
        /// 直接贴上来会是一张白卡片盖住画面。<c>山·透明.png</c> 由
        /// <c>art-source/_tools/key_image.py</c> 抠出（不改源文件）。
        /// </summary>
        public static readonly ScenerySpec[] DefaultScenery =
        {
            new ScenerySpec
            {
                path = "背景/山·透明", factor = 0.62f, height = 620f, bottomY = -300f,
                xs = new[] { 520f, 1720f, 2760f },
            },
        };

        [Tooltip("层配置：从远到近；改动需重跑才生效")]
        public LayerSpec[] layers = DefaultLayers;

        [Tooltip("散布景物：沿关卡逐个摆的景物（山峰等）")]
        public ScenerySpec[] scenery = DefaultScenery;

        [Tooltip("自动平移速度：每秒额外漂移的世界单位；Boss 战场用，探索段设 0")]
        public float autoPanSpeed;

        /// <summary>
        /// 自动平移的**硬上限**（世界单位）。
        ///
        /// <see cref="autoPanSpeed"/> 是「推进」——单方向累加；只靠几何算出来的
        /// 安全余量可能有好几千单位（长卷比需要的宽），玩家在开场站着不动时
        /// 背景会一直慢慢往一边滑，看起来就是「飘走」。这里给一个观感上的上限：
        /// 推这么远就停住，既保留开场那一下缓慢推进，又不会真的滑出画面。
        /// </summary>
        public float autoPanLimit = 120f;

        [Tooltip("视口宽度（世界单位）。留 0 则由相机的正交尺寸自动推算")]
        public float viewWidthOverride;

        /// <summary>相机累计位移，由 <see cref="CameraFollow"/> 每帧写入</summary>
        private float _cameraX;

        /// <summary>相机当前的 y。背景跟着它整层平移（不做纵向视差），保证上下跟随时不露边</summary>
        private float _cameraY;

        private float _pan;
        private readonly List<Transform> _built = new List<Transform>();
        private LayerSpec[] _specs = Array.Empty<LayerSpec>();

        /// <summary>散布景物：每个实例记下自己的关卡 x、视差系数与中心 y</summary>
        private class PlacedScenery
        {
            public Transform Node;
            public float BaseX;
            public float Factor;
            public float CenterY;
        }

        private readonly List<PlacedScenery> _placed = new List<PlacedScenery>();

        /// <summary>累计时间（秒），各层拿它算正弦漂移</summary>
        private float _elapsed;

        private float _camMin;
        private float _camMax = 2600f;
        private float _viewW = 1280f;

        public void SetCameraX(float x)
        {
            _cameraX = x;
        }

        /// <summary>
        /// 相机 y。背景层跟着整层平移（纵向不做视差）：
        /// 纵向视差会让画面底部在相机下潜时露出图层以外的空白，而背景长卷
        /// 只比视口高一点点，没有余量。固定相对屏幕 = 永远不飘走、不露边。
        /// </summary>
        public void SetCameraY(float y)
        {
            _cameraY = y;
        }

        public float ViewWidth
        {
            get { return _viewW; }
        }

        /// <summary>
        /// 设定相机行程。**必须在建层之前给到**，因为每层的宽度由它反推：
        /// <code>layerW = viewW + factor × (camMax − camMin)</code>
        ///
        /// 推导：要让相机从关卡头走到尾时，图层正好从「左边缘贴屏幕左边」滑到
        /// 「右边缘贴屏幕右边」，两端都不露空。设行程 T、视口 W、视差系数 f，
        /// 相机位移 T 而图层只位移 (1−f)T，所以图层要比视口多出 f×T 才够滑。
        /// 少给就会在关卡末尾露空，多给则图层被无谓拉大、糊掉。
        /// </summary>
        public void SetRange(float camMin, float camMax, float viewW)
        {
            _camMin = camMin;
            _camMax = camMax;
            _viewW = viewW;

            // 日志打在这里而不是 Layout()：Layout() 在 Awake 里就会跑一次，
            // 那时行程还是默认值，打出来的宽度是错的（Cocos 侧踩过）。
            // 打**实际**尺寸而不是 LayerWidth()：给了 height 的层会被放大，
            // 实际宽度大于 LayerWidth()，只看后者会以为没生效。
            string detail = "";

            for (int i = 0; i < _specs.Length; i++)
            {
                LayerSpec s = _specs[i];
                Transform n = i < _built.Count ? _built[i] : null;
                SpriteRenderer sr = n != null ? n.GetComponent<SpriteRenderer>() : null;

                string actual = sr != null && sr.sprite != null
                    ? $"{sr.sprite.bounds.size.x * n.localScale.x:F0}×{sr.sprite.bounds.size.y * n.localScale.y:F0}"
                    : "?";

                string want = $"需要宽 {LayerWidth(s):F0}{(s.height > 0f ? $" / 高 {s.height}" : "")}";
                detail += $"  {s.path.Split('/')[s.path.Split('/').Length - 1]} f={s.factor} 实际 {actual}（{want}）";
            }

            Debug.Log($"[Parallax] 视口 {_viewW}  行程 [{camMin:F0}, {camMax:F0}]{detail}");
            Layout();
        }

        /// <summary>
        /// 运行时换一套背景规格（换关卡的主题）。
        ///
        /// <b>为什么不是重建背景节点</b>：背景是常驻件（在 Host 场景），
        /// 重建节点就要重新接引用。这里只换规格再 <see cref="Build"/> ——
        /// 而 <c>Build()</c> 本来就是幂等的（先 <c>Destroy</c> 掉上一轮建出来的层再重建），
        /// 所以不需要额外写清理逻辑。
        ///
        /// 顺序上**必须先更新行程再 Build**：层宽是按相机行程反推的
        /// （见 <see cref="SetRange"/> 的推导），行程还是上一关的话，
        /// 层会被按错误的宽度建出来 —— 表现是关卡末尾露空。
        /// </summary>
        public void ApplyTheme(LevelTheme theme, float camMin, float camMax)
        {
            if (theme == null)
            {
                Debug.LogWarning("[Parallax] ApplyTheme 收到空的 LevelTheme —— 背景保持原样。" +
                                 "关卡定义里没配 theme 时就是这个下场。");
                return;
            }

            // ⚠️ layers 与 scenery 的**空值语义不同**，别统一成一样的：
            //   null      = 「没配，用默认」（旧关卡不配主题也能正常显示）
            //   空数组    = 「明确不要」（新美术自带前景，再叠默认景物就重了）
            layers = theme.layers != null && theme.layers.Length > 0 ? theme.layers : DefaultLayers;
            scenery = theme.scenery != null ? theme.scenery : DefaultScenery;

            if (theme.viewWidthOverride > 0f)
            {
                viewWidthOverride = theme.viewWidthOverride;
            }

            _specs = layers;
            _viewW = ResolveViewWidth();
            _camMin = camMin;
            _camMax = camMax;

            // 换关时把开场推进量清零。不清的话上一关攒下的 _pan 会带进下一关，
            // 新关一开场背景就已经偏出去一截。
            _pan = 0f;

            Build();
            ApplyTint(theme);

            Debug.Log($"[Parallax] 换主题「{theme.name}」：{_specs.Length} 层 / {scenery.Length} 组景物，" +
                      $"行程 [{camMin:F0}, {camMax:F0}]");
        }

        /// <summary>
        /// 把主题的着色真正作用到背景层上，并把整屏色调交给 <see cref="ScreenGrade"/>。
        ///
        /// ⚠️ 这两个字段（tint / fogDensity）从引入主题起就**从未被读取过** ——
        /// 六关的 tint 都写在资产里，但代码只用了 layers/scenery，
        /// 表现就是"五行五境看着一个色调"。
        /// </summary>
        private void ApplyTint(LevelTheme theme)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                var sr = transform.GetChild(i).GetComponent<SpriteRenderer>();

                if (sr != null)
                {
                    Color baseColor = sr.color;
                    sr.color = new Color(
                        baseColor.r * theme.tint.r,
                        baseColor.g * theme.tint.g,
                        baseColor.b * theme.tint.b,
                        baseColor.a);
                }
            }

            ScreenGrade grade = ScreenGrade.Instance;

            if (grade != null)
            {
                grade.Apply(theme.tint, theme.fogColor, theme.fogDensity);
            }
        }

        private void Awake()
        {
            _viewW = ResolveViewWidth();
            _specs = layers ?? Array.Empty<LayerSpec>();
            Build();
        }

        /// <summary>视口宽度：优先用 Inspector 指定的，否则由正交相机推算。</summary>
        private float ResolveViewWidth()
        {
            if (viewWidthOverride > 0f)
            {
                return viewWidthOverride;
            }

            Camera cam = Camera.main;

            if (cam != null && cam.orthographic)
            {
                return cam.orthographicSize * 2f * cam.aspect;
            }

            return 1280f;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _elapsed += dt;

            if (autoPanSpeed != 0f)
            {
                float limit = MaxPan();
                _pan = Mathf.Max(-limit, Mathf.Min(limit, _pan + autoPanSpeed * dt));
            }

            Apply();
        }

        /// <summary>
        /// 自动平移的**安全上限** —— 再往外推，参与平移的层就会从屏幕边缘露空。
        ///
        /// 为什么非要有：<see cref="LayerWidth"/> 是**刚好**按视差系数算的、没有余量，
        /// 而平移是单向累加的。Boss 战场一直开着 <c>autoPanSpeed</c>，
        /// 不封顶的话打到一分多钟云就滑出屏幕、左边缘露出空白。
        /// </summary>
        private float MaxPan()
        {
            float travel = Mathf.Max(0f, _camMax - _camMin);
            float limit = autoPanLimit;

            for (int i = 0; i < _built.Count; i++)
            {
                LayerSpec spec = i < _specs.Length ? _specs[i] : null;

                if (spec == null || !spec.autoPan || spec.factor >= 1f)
                {
                    continue;
                }

                SpriteRenderer sr = _built[i].GetComponent<SpriteRenderer>();

                float actualW = sr != null && sr.sprite != null
                    ? sr.sprite.bounds.size.x * _built[i].localScale.x
                    : LayerWidth(spec);

                // 图层比「刚好铺满相机行程」多出来的宽度，才是能左右推的余量。
                // 余量按 2 边分、再按 (1−factor) 放大成相机侧的位移。
                //
                // ⚠️ 这里原来写成 `+ factor × travel`，符号反了：把**需要**的宽度
                // 当成了**多余**的宽度，算出来的可推距离比实际余量还大，
                // 于是背景会推到左边缘露出空白（表现就是「飘走」）。
                float extra = actualW - LayerWidth(spec);
                float room = extra > 0f ? extra / (2f * (1f - spec.factor)) : 0f;
                limit = Mathf.Min(limit, room);
            }

            return Mathf.Max(0f, limit);
        }

        /// <summary>某层此刻的漂移位移：按 <c>driftPeriod</c> 来回摆，有界。</summary>
        private float DriftOffset(LayerSpec spec)
        {
            if (spec.drift == 0f)
            {
                return 0f;
            }

            float period = spec.driftPeriod > 0f ? spec.driftPeriod : 40f;
            return spec.drift * Mathf.Sin(_elapsed / period * Mathf.PI * 2f);
        }

        /// <summary>每层的实际宽度（世界单位），由视差系数与相机行程算出。</summary>
        private float LayerWidth(LayerSpec spec)
        {
            float travel = Mathf.Max(0f, _camMax - _camMin);
            return _viewW + spec.factor * travel;
        }

        /// <summary>
        /// 层的摆放。
        /// <code>
        /// L(camX) = camMin − W/2 + layerW/2 + (camX − camMin) × (1 − f)
        /// </code>
        /// 基准项保证 <c>camX = camMin</c> 时图层左边缘正好贴住屏幕左边
        /// （关卡开头看到长卷最左端），增量项就是视差本身。
        /// </summary>
        private void Apply()
        {
            for (int i = 0; i < _built.Count; i++)
            {
                LayerSpec spec = i < _specs.Length ? _specs[i] : null;

                if (spec == null)
                {
                    continue;
                }

                float lw = LayerWidth(spec);
                float baseX = _camMin - _viewW / 2f + lw / 2f;

                // 自动平移只给开了 AutoPan 的层（远景的云），其余层不参与
                float panTerm = spec.autoPan ? _pan : 0f;

                float x = baseX
                          + (_cameraX - _camMin + panTerm) * (1f - spec.factor)
                          + DriftOffset(spec);

                Vector3 p = _built[i].localPosition;
                _built[i].localPosition = new Vector3(x, spec.yOffset + _cameraY, p.z);
            }

            // 散布景物：以**关卡坐标**为基准（BaseX），相机一动就按 (1-factor) 让位。
            // 同样不参与自动平移（山景要稳住，动的只有远景的云）
            foreach (PlacedScenery s in _placed)
            {
                if (s.Node != null)
                {
                    s.Node.localPosition = new Vector3(
                        s.BaseX + _cameraX * (1f - s.Factor), s.CenterY + _cameraY, 0f);
                }
            }
        }

        private void BuildScenery()
        {
            if (scenery == null)
            {
                return;
            }

            for (int si = 0; si < scenery.Length; si++)
            {
                ScenerySpec spec = scenery[si];

                if (spec.xs == null)
                {
                    continue;
                }

                for (int xi = 0; xi < spec.xs.Length; xi++)
                {
                    GameObject go = new GameObject($"Scenery_{si}_{xi}");

                    // 父节点可能带缩放（视差层是缩放的），这里用世界单位摆放
                    go.transform.SetParent(transform, false);

                    SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                    sr.sortingOrder = -100;      // 景物在中景之后

                    PlacedScenery rec = new PlacedScenery
                    {
                        Node = go.transform,
                        BaseX = spec.xs[xi],
                        Factor = spec.factor,
                        CenterY = spec.bottomY,
                    };

                    _placed.Add(rec);

                    Sprite sprite = Resources.Load<Sprite>(spec.path);

                    if (sprite == null)
                    {
                        Debug.LogWarning($"[Parallax] 读不到景物 {spec.path}");
                        continue;
                    }

                    sr.sprite = sprite;

                    // 按目标高度等比缩放
                    float srcH = sprite.bounds.size.y;
                    float k = srcH > 0f ? spec.height / srcH : 1f;
                    go.transform.localScale = new Vector3(k, k, 1f);

                    // 锚点在中心，所以「底边对齐 bottomY」= 节点 y 抬高半个缩放后高度
                    rec.CenterY = spec.bottomY + srcH * k / 2f;
                }
            }
        }

        private void Build()
        {
            // 幂等：先清掉上一轮建出来的层
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            _built.Clear();
            _placed.Clear();

            for (int i = 0; i < _specs.Length; i++)
            {
                LayerSpec spec = _specs[i];
                string leaf = spec.path.Split('/')[spec.path.Split('/').Length - 1];

                GameObject go = new GameObject($"Layer_{leaf}");
                go.transform.SetParent(transform, false);

                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = -200 + i;      // 越靠后的层越靠前（画在上面）

                _built.Add(go.transform);

                Sprite sprite = Resources.Load<Sprite>(spec.path);

                if (sprite == null)
                {
                    Debug.LogWarning($"[Parallax] 读不到背景层 {spec.path}");
                    continue;
                }

                sr.sprite = sprite;
                FitLayer(go.transform, sprite, spec);
            }

            BuildScenery();
            Layout();
        }

        /// <summary>
        /// 把一层拉到目标宽度。
        ///
        /// <c>spec.height</c> 给了目标高度时取**两个要求里更大的那个倍率**（只会放大）。
        /// 放大后图层比 <see cref="LayerWidth"/> 宽 —— 这是安全的：<see cref="Apply"/>
        /// 里的基准项决定的是**图层中心**，更宽只会往两边多探出去，不会露空。
        /// </summary>
        private void FitLayer(Transform n, Sprite sprite, LayerSpec spec)
        {
            float srcW = sprite.bounds.size.x;
            float srcH = sprite.bounds.size.y;

            if (srcW <= 0f)
            {
                return;
            }

            float byWidth = LayerWidth(spec) / srcW;
            float byHeight = spec.height > 0f && srcH > 0f ? spec.height / srcH : 0f;
            float s = Mathf.Max(byWidth, byHeight);

            n.localScale = new Vector3(s, s, 1f);
        }

        /// <summary>行程变了就重新量一遍（相机与视口都要到齐才准）。</summary>
        private void Layout()
        {
            for (int i = 0; i < _specs.Length && i < _built.Count; i++)
            {
                SpriteRenderer sr = _built[i].GetComponent<SpriteRenderer>();

                if (sr != null && sr.sprite != null)
                {
                    FitLayer(_built[i], sr.sprite, _specs[i]);
                }
            }

            Apply();
        }
    }
}
