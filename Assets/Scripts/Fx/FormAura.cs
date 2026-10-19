using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 三形态的**环绕粒子**：实体形态不带环，灵雾与灵合各绕一圈不同的墨。
    ///
    /// <b>为什么需要它</b>
    ///
    /// 青禾的骨骼动画还没做完，三个形态目前的区别只有**立绘染色**（一身青冷 / 一身暖金）——
    /// 玩家要在战斗里瞬间判断自己是哪个形态，光靠染色太弱了。
    ///
    /// 环绕粒子是最好的补充：它**在角色轮廓之外**、**一直在动**，余光就能看见；
    /// 而且两种形态的粒子在**颜色、半径、转速、疏密**上全不一样，一眼能分辨。
    ///
    /// <b>做法</b>（参考通行做法：Circle 发射器 + Radius Thickness 0 + Orbital Z，
    /// 或者用脚本给切向速度）
    ///
    /// 这里用脚本 —— 与 <see cref="InkFx"/> 同一套代码驱动精灵，**零 shader 依赖**。
    /// 每颗环绕物就是一个 SpriteRenderer，自己维护角度、半径、角速度、上下浮动相位。
    /// 换成 <c>ParticleSystem</c> 的话要 <c>Shader.Find</c> 粒子材质，
    /// 而运行时 <c>Shader.Find</c> 在构建里可能返回 null（见 <see cref="InkFx"/> 的说明）。
    ///
    /// <b>实体形态为什么不挂环</b>：本体就该是「实体」——给它套一圈光反而失掉对比。
    /// 有环=灵体，没环=肉身，这个对比本身就是在讲故事。
    /// </summary>
    public class FormAura : MonoBehaviour
    {
        /// <summary>一颗环绕物。</summary>
        private class Orb
        {
            public Transform Node;
            public SpriteRenderer Renderer;
            public float AngleDeg;
            public float SpeedDeg;      // 每秒转多少度
            public float Radius;
            public float BobPhase;
            public float BobAmp;
            public float BobSpeed;
            public float BaseAlpha;
        }

        [Tooltip("整体尺寸倍数。与角色一起缩放")]
        public float scale = 1f;

        [Tooltip("粒子绘制层级（比人物高一点 = 在人物之上）")]
        /// <summary>
        /// 环绕物的**近侧**层级（下半圈，朝观众那半）。远侧取它 **−2**，
        /// 也就是把 <see cref="SortingOrders.FxOrbitNear"/> / <see cref="SortingOrders.FxOrbitFar"/>
        /// 这一对原样搬过来 —— 挪的时候两层一起走，不会只剩一半。
        /// </summary>
        public int sortingOrder = SortingOrders.FxOrbitNear;

        private readonly List<Orb> _orbs = new List<Orb>();
        private CompanionForm _form = CompanionForm.Entity;
        private bool _subscribed;

        private Sprite _bleed;
        private Sprite _dot;
        private Sprite _speck;
        private Sprite _ringFar;
        private Sprite _ringNear;
        private bool _loaded;

        /// <summary>灵合净化光环的可见圈（前后两半，见 <see cref="BuildPurifyRing"/>）</summary>
        private SpriteRenderer _purifyFar;
        private SpriteRenderer _purifyNear;

        /// <summary>光环呼吸的相位（让圈看起来在「活」，而不是一张静止贴图）</summary>
        private float _purifyPhase;

        /// <summary>
        /// 净化光环圈的基准缩放（= 直径 / 贴图长边）。做呼吸时在它上面乘系数 ——
        /// 单独存着而不是累乘 <c>localScale</c>，见 <see cref="BreatheRing"/>。
        /// 前后两半用的是同一张尺寸的贴图，所以共用一个值。
        /// </summary>
        private float _purifyBaseScale = 1f;

        /// <summary>缓存的玩家控制器 —— 环绕中心要读它的实际身高</summary>
        private QingheController _qinghe;

        // ============================================================ 形态配置

        /// <summary>一种形态的环绕参数。</summary>
        private struct AuraSpec
        {
            public int Count;
            public float Radius;
            public float RadiusJitter;
            public float SpeedDeg;
            public float Size;
            public float BobAmp;
            public float Alpha;
        }

        /// <summary>
        /// 灵雾：**小而密、转得快** —— 像一缕绕身的青烟。半径小，贴着身体。
        /// </summary>
        private static readonly AuraSpec MistSpec = new AuraSpec
        {
            Count = 6,
            Radius = 78f,
            RadiusJitter = 14f,
            SpeedDeg = 130f,
            Size = 46f,
            BobAmp = 22f,
            Alpha = 0.75f,
        };

        /// <summary>
        /// 灵合：**大而疏、转得慢、更亮** —— 像几团沉在身周的灵光。半径大，离身体远。
        /// </summary>
        private static readonly AuraSpec MergeSpec = new AuraSpec
        {
            Count = 7,
            Radius = 118f,
            RadiusJitter = 20f,
            SpeedDeg = 62f,
            Size = 78f,
            BobAmp = 34f,
            Alpha = 0.95f,
        };

        // ============================================================ 生命周期

        private void Awake()
        {
            _qinghe = GetComponent<QingheController>();

            // 改用交付层的实拍特效（fx_tex）：程序生成的墨点/晕染边缘太"塑料"，
            // 放大到环绕件这个尺寸后尤其明显。语义一一对应：
            //   ink_bleed → fx_smoke（晕染/烟团）
            //   ink_dot   → fx_glow（柔光點）
            //   ink_speck → fx_sparkle（星芒）
            //   半环      → fx_ring_white（交付库没有半环，用整环代替，牺牲前后分层）
            _bleed = LoadFx("fx_tex/fx_smoke", "fx/ink_bleed");
            _dot = LoadFx("fx_tex/fx_glow", "fx/ink_dot");
            _speck = LoadFx("fx_tex/fx_sparkle", "fx/ink_speck");
            _ringFar = LoadFx("fx_tex/fx_ring_white", "fx/glow_ring_far");
            _ringNear = LoadFx("fx_tex/fx_ring_white", "fx/glow_ring_near");
            _loaded = true;

            // ⚠️ **整体缩放必须自己接上**。
            //
            // `scale` 是 public 字段、Tooltip 写着「与角色一起缩放」，但全工程
            // **没有一处给它赋值** —— 也就是说它恒为 1，环绕粒子与人物体形脱钩：
            // 青禾缩到 0.4 倍（身高 96）之后，直径 46~78 的粒子相对身体大得离谱。
            //
            // 这里读控制器的缩放比例（相对「量数值那一版」的比例，见
            // QingheController.MeasuredAtBodyScale）—— 沿用那套约定，
            // 环绕参数就仍然是「在 0.75 那版下看到的样子」。
            if (_qinghe != null)
            {
                scale = _qinghe.BodyScaleRatio;
            }

            if (_bleed == null || _dot == null)
            {
                Debug.LogWarning("[FormAura] fx/ink_* 没读全，环绕粒子不会出现");
            }
        }

        /// <summary>优先读交付层贴图，缺失才回落程序生成的占位图。</summary>
        private static Sprite LoadFx(string primary, string fallback)
        {
            Sprite s = Resources.Load<Sprite>(primary);
            return s != null ? s : Resources.Load<Sprite>(fallback);
        }

        private void OnEnable()
        {            if (!_subscribed)
            {
                _subscribed = true;
                EventBus.On<CompanionFormChangedEvent>(GameEvents.CompanionFormChanged, OnFormChanged);
            }

            // 初始形态可能在订阅之前就切过了，主动同步一次
            CompanionStateMachine fsm = GetComponent<CompanionStateMachine>();

            if (fsm != null)
            {
                Rebuild(fsm.GetCurrentForm());
            }
        }

        private void OnDisable()
        {
            if (_subscribed)
            {
                _subscribed = false;
                EventBus.Off<CompanionFormChangedEvent>(GameEvents.CompanionFormChanged, OnFormChanged);
            }

            Clear();
        }

        private void OnFormChanged(CompanionFormChangedEvent e)
        {
            Rebuild(e.Current);
        }

        // ============================================================ 构建

        private void Clear()
        {
            foreach (Orb o in _orbs)
            {
                if (o.Node != null)
                {
                    Destroy(o.Node.gameObject);
                }
            }

            _orbs.Clear();

            if (_purifyFar != null)
            {
                Destroy(_purifyFar.gameObject);
                _purifyFar = null;
            }

            if (_purifyNear != null)
            {
                Destroy(_purifyNear.gameObject);
                _purifyNear = null;
            }
        }

        /// <summary>
        /// 净化光环的可见圈：脚下一圈半透明光环，**直径 = 实际的伤害半径**。
        ///
        /// <b>为什么必须有</b>：净化光环是一个**持续伤害机制**
        /// （<c>CombatController.TickMergeAura</c>，半径 <c>MergePurifyRadius</c>），
        /// 但原来**一点画面都没有** —— 玩家看到的是「进灵合闪一下，之后敌人莫名掉血」。
        /// 设计文档（<c>docs/关于boss战的一些修改.md</c>）把「灵合态范围光圈」
        /// 明确列为必需资产，一直没有实现。
        ///
        /// <b>几个刻意的取舍</b>
        /// <list type="bullet">
        ///   <item><b>直径直接取伤害半径</b>，不另调一个「看着差不多」的数 ——
        ///     光圈和判定不一致时玩家会照着圈外站，却照样掉血。</item>
        ///   <item><b>不吃人物缩放</b>：伤害半径是固定的世界单位，人物缩小了圈也不该缩，
        ///     否则「小圈打大范围」。和灵炁爆发同一条约定。</item>
        ///   <item><b>画在脚下（y ≈ 0）而不是胸口</b>：这是「场」，不是护盾。
        ///     脚下的圈配上前后分层，人物正好站在圈里。</item>
        ///   <item>前后两半用 <see cref="SortingOrders.FxOrbitFar"/> /
        ///     <see cref="SortingOrders.FxOrbitNear"/> 分层，人物把圈劈成前后两半。</item>
        /// </list>
        /// </summary>
        private void BuildPurifyRing(Color tint)
        {
            if (_ringFar == null || _ringNear == null)
            {
                Debug.LogWarning("[FormAura] fx/glow_ring_far / _near 没读到，净化光环的圈不会显示");
                return;
            }

            float diameter = GameConfig.Current.Spirit.MergePurifyRadius * 2f;

            // 不透明度给足：圈是浅色的，压在浅色山水上很容易看不见（同 InkFx 的对比度说明）。
            // 远侧略暗（远处该压一点），近侧更实。
            _purifyFar = MakeRingHalf("PurifyFar", _ringFar, SortingOrders.FxOrbitFar,
                                      diameter, tint, 0.46f);
            _purifyNear = MakeRingHalf("PurifyNear", _ringNear, SortingOrders.FxOrbitNear,
                                       diameter, tint, 0.62f);
        }

        private SpriteRenderer MakeRingHalf(string name, Sprite sprite, int order,
                                            float diameter, Color tint, float alpha)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            // 脚底：节点原点就在脚底附近（见 QingheController 的碰撞体对齐）
            go.transform.localPosition = new Vector3(0f, 4f, 0f);

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;

            Color c = tint;
            c.a = alpha;
            sr.color = c;

            Vector2 native = sprite.bounds.size;
            float longest = Mathf.Max(native.x, native.y);
            _purifyBaseScale = longest > 0f ? diameter / longest : 1f;
            go.transform.localScale = new Vector3(_purifyBaseScale, _purifyBaseScale, 1f);

            return sr;
        }

        private void Rebuild(CompanionForm form)
        {
            _form = form;
            Clear();

            // 实体形态：不挂环。见类注释「实体形态为什么不挂环」。
            if (form == CompanionForm.Entity || !_loaded || _bleed == null)
            {
                return;
            }

            AuraSpec spec = form == CompanionForm.Merge ? MergeSpec : MistSpec;
            Color tint = InkFx.TintFor(form);

            // 灵合额外挂一个「净化光环」的**可见圈** —— 这一招原来只有伤害、没有画面。
            if (form == CompanionForm.Merge)
            {
                BuildPurifyRing(tint);
            }

            for (int i = 0; i < spec.Count; i++)
            {
                GameObject go = new GameObject($"AuraOrb{i}");
                go.transform.SetParent(transform, false);

                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                // 灵合用更实的墨点、灵雾用更虚的晕染 —— 形状本身就是区分
                sr.sprite = form == CompanionForm.Merge ? _dot : _bleed;
                sr.sortingOrder = sortingOrder;

                Color c = tint;
                c.a = spec.Alpha;
                sr.color = c;

                // 贴图按 1 像素 = 1 世界单位导入，按目标尺寸（取长边）反算缩放
                Vector2 native = sr.sprite.bounds.size;
                float longest = Mathf.Max(native.x, native.y);
                float k = longest > 0f ? spec.Size * scale / longest : 1f;
                go.transform.localScale = new Vector3(k, k, 1f);

                _orbs.Add(new Orb
                {
                    Node = go.transform,
                    Renderer = sr,
                    // 均分一圈，再加一点抖动 —— 完全等距看起来像机械表盘
                    AngleDeg = i * (360f / spec.Count) + Random.Range(-18f, 18f),
                    SpeedDeg = spec.SpeedDeg * Random.Range(0.82f, 1.18f),
                    Radius = (spec.Radius + Random.Range(-spec.RadiusJitter, spec.RadiusJitter)) * scale,
                    BobPhase = Random.Range(0f, Mathf.PI * 2f),
                    BobAmp = spec.BobAmp * scale,
                    BobSpeed = Random.Range(0.8f, 1.5f),
                    BaseAlpha = spec.Alpha,
                });
            }
        }

        // ============================================================ 每帧

        private void Update()
        {
            // 净化光环的呼吸：圈在缓慢张缩 + 明暗起伏，看着才像「场」在一个活的状态。
            // 静止的圈会被当成一张贴图；而且这也顺带把「光环正在计时结算」这件事
            // 画出来了 —— 原来是完全看不出来的。
            if (_purifyNear != null || _purifyFar != null)
            {
                _purifyPhase += Time.deltaTime * 1.6f;
                float k = 1f + Mathf.Sin(_purifyPhase) * 0.035f;

                BreatheRing(_purifyFar, k, 0.30f);
                BreatheRing(_purifyNear, k, 0.42f);
            }

            if (_orbs.Count == 0)
            {
                return;
            }

            float dt = Time.deltaTime;
            float t = Time.time;

            foreach (Orb o in _orbs)
            {
                if (o.Node == null)
                {
                    continue;
                }

                o.AngleDeg += o.SpeedDeg * dt;

                float rad = o.AngleDeg * Mathf.Deg2Rad;

                // 横向走一个正圆，纵向压扁 —— 2D 里这样才有「绕到身后又转回来」的错觉
                float x = Mathf.Cos(rad) * o.Radius;
                float y = Mathf.Sin(rad) * o.Radius * 0.42f;

                // 再叠一层慢速上下浮动，免得轨道过于规整
                y += Mathf.Sin(t * o.BobSpeed + o.BobPhase) * o.BobAmp;

                // 半径按角色身高抬到胸口附近，不是绕脚底转
                float centerY = CharacterCenterY();

                o.Node.localPosition = new Vector3(x, centerY + y, 0f);

                if (o.Renderer == null)
                {
                    continue;
                }

                // ---- 前后关系 ----
                //
                // 轨道是横向的正圆、纵向压扁（见上面 y 的 0.42），所以：
                //   **上半圈（sin>0）= 远侧**，在人物身后，该被他挡住；
                //   **下半圈（sin<0）= 近侧**，朝观众，该盖住他。
                // depth：0 = 远 / 1 = 近。
                //
                // ⚠️ 原来只用一个单层 <c>sortingOrder</c>（全在人物之前），
                // 于是整圈都糊在人身上，「绕到身后」只剩亮度暗示 ——
                // 读起来就是一张贴纸。现在**真的分层**：
                // 远侧排到 <see cref="SortingOrders.FxOrbitFar"/>，近侧
                // <see cref="SortingOrders.FxOrbitNear"/>，一圈自然被人物劈成前后两半。
                //
                // ⚠️ 顺带把亮度也**改成与远近一致**（远暗近亮）。原来那行是
                // `(sin+1)*0.5`，等于把「上半圈」当成近侧去提亮 —— 与远近正好相反。
                // 分层之后远侧本来就会被人物遮住，亮度只影响露出来的边缘，
                // 但如果反着来，近侧反而更暗，看着像「贴在人前面的影子」。
                float depth = (1f - Mathf.Sin(rad)) * 0.5f;      // 0=远 1=近

                o.Renderer.sortingOrder = depth >= 0.5f
                    ? sortingOrder
                    : sortingOrder - 2;      // 远侧：见 sortingOrder 的说明

                Color c = o.Renderer.color;
                c.a = o.BaseAlpha * Mathf.Lerp(0.4f, 1f, depth);
                o.Renderer.color = c;
            }
        }

        /// <summary>
        /// 净化光环的一呼一吸：在**基准缩放**上乘一个 ±3.5% 的系数，亮度跟着起伏。
        ///
        /// ⚠️ 不能写成 <c>transform.localScale *= k</c> —— 那样每帧乘一次，
        /// 几秒钟缩放就爆了。所以基准值单独存着（<see cref="_purifyBaseScale"/>），
        /// 每帧从基准重算。
        /// </summary>
        private void BreatheRing(SpriteRenderer sr, float k, float baseAlpha)
        {
            if (sr == null)
            {
                return;
            }

            sr.transform.localScale = new Vector3(_purifyBaseScale * k, _purifyBaseScale * k, 1f);

            Color c = sr.color;
            c.a = baseAlpha * (0.75f + 0.25f * Mathf.Sin(_purifyPhase * 0.8f));
            sr.color = c;
        }

        /// <summary>
        /// 环绕的中心高度：角色节点的原点是**脚底**，所以要抬到胸口附近再绕。
        ///
        /// ⚠️ 用**人物实际身高**推，不要用碰撞体 —— 碰撞体刻意小于立绘
        /// （30×112.5 配 180 高的人物），按它算出来的中心会落在大腿上。
        /// </summary>
        private float CharacterCenterY()
        {
            Collider2D col = GetComponent<Collider2D>();
            float feetY = col is BoxCollider2D box ? box.offset.y - box.size.y * 0.5f : 0f;

            if (_qinghe != null && _qinghe.FigureHeight > 1f)
            {
                return feetY + _qinghe.FigureHeight * 0.58f;
            }

            return feetY + 80f;
        }
    }
}
