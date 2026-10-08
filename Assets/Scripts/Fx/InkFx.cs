using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 战斗特效：程序化的**精灵粒子**（不是 Unity 的 ParticleSystem）。
    ///
    /// <b>为什么不用 ParticleSystem</b>：它要 <c>Shader.Find</c> 粒子材质，
    /// 而运行时 <c>Shader.Find</c> 在构建里可能返回 null（材质会变成粉红块）。
    /// 本作的特效全是几十颗精灵，自己积分位置完全够用，也躲开了那条坑。
    ///
    /// <b>素材</b>：<c>Resources/fx/*.png</c>，目前是程序化生成的占位图
    /// （<c>tools/gen_fx_sprites.py</c>，黑底 + alpha，颜色运行时染）。
    /// **美术到位后直接替换同名文件即可，代码不用动。**
    ///
    /// <b>2026-10-07：按形态分了「形状语言」。</b>
    ///
    /// 原来三种形态的攻击**只有颜色不同、形状一模一样**，都是同一套墨迹。
    /// 玩家反馈「看不出打的是哪一种」—— 颜色区分在快动作里几乎读不出来。
    /// 所以改成各自一套形状：
    /// <code>
    ///   实体：飞白（细长枯笔）+ 晕染   —— 锐、快、有方向感
    ///   灵雾：柔晕 + 雾团，往上飘      —— 圆、慢、不着力
    ///   灵合：光环 + 光点，放射        —— 亮、不接触、像「场」
    /// </code>
    /// 判断依据是**形状**而不是颜色 —— 色盲、截图、小屏都读得出来。
    /// </summary>
    public static class InkFx
    {
        /// <summary>特效类型。</summary>
        public enum Kind
        {
            /// <summary>轻击：一道扫出去的飞白</summary>
            Slash,

            /// <summary>重击：更大更散的一笔</summary>
            HeavySlash,

            /// <summary>命中：向四周炸开</summary>
            Impact,

            /// <summary>灵弹：一小团晕染（出膛 / 命中都走它）</summary>
            Mist,

            /// <summary>冲刺：身后拖一道</summary>
            Dash,

            /// <summary>格挡：身前荡开一圈</summary>
            Block,

            /// <summary>完美格挡：更大更亮的一圈 + 火花</summary>
            PerfectBlock,

            /// <summary>形态切换：从身上炸开一圈 + 上升光点</summary>
            FormSwitch,

            /// <summary>灵炁爆发：灵合的大招（半径 340 的 AOE）</summary>
            MergeBurst,

            /// <summary>轻击三连的**第三段**：收招更重、带击退，视觉上也要是个「收尾」</summary>
            Finisher,

            /// <summary>持续拖尾（灵雾飞行）：小而淡，靠频率而不是体积堆出「一路飘过去」</summary>
            Trail,
        }

        // ---- 墨（实体形态的语言）----
        private static Sprite _dot;
        private static Sprite _bleed;
        private static Sprite _speck;
        private static Sprite[] _streaks;

        // ---- 光（灵雾 / 灵合的语言）----
        private static Sprite _ring;
        private static Sprite _ringFar;
        private static Sprite _ringNear;
        private static Sprite _glow;
        private static Sprite _mote;

        /// <summary>刀锋弧：挥砍的「主笔」（见 tools/gen_fx_sprites.py 的 arc_slash）</summary>
        private static Sprite _arc;

        private static bool _loaded;

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _dot = Resources.Load<Sprite>("fx/ink_dot");
            _bleed = Resources.Load<Sprite>("fx/ink_bleed");
            _speck = Resources.Load<Sprite>("fx/ink_speck");

            _streaks = new[]
            {
                Resources.Load<Sprite>("fx/ink_streak"),
                Resources.Load<Sprite>("fx/ink_streak2"),
                Resources.Load<Sprite>("fx/ink_streak3"),
            };

            _ring = Resources.Load<Sprite>("fx/glow_ring");
            _ringFar = Resources.Load<Sprite>("fx/glow_ring_far");
            _ringNear = Resources.Load<Sprite>("fx/glow_ring_near");
            _glow = Resources.Load<Sprite>("fx/glow_soft");
            _mote = Resources.Load<Sprite>("fx/mote");
            _arc = Resources.Load<Sprite>("fx/arc_slash");

            if (_bleed == null || _streaks[0] == null)
            {
                Debug.LogWarning("[InkFx] fx/ink_* 没读全，攻击特效会残缺");
            }

            if (_ring == null || _mote == null)
            {
                Debug.LogWarning("[InkFx] fx/glow_ring / fx/mote 没读全，灵合与格挡特效会残缺");
            }
        }

        /// <summary>按形态取色。</summary>
        public static Color TintFor(CompanionForm form)
        {
            CombatConfig c = GameConfig.Current.Combat;

            switch (form)
            {
                case CompanionForm.Mist: return c.InkTintMist;
                case CompanionForm.Merge: return c.InkTintMerge;
                default: return c.InkTintEntity;
            }
        }

        /// <summary>
        /// 在某处炸一簇特效。
        /// </summary>
        /// <param name="worldPos">世界坐标</param>
        /// <param name="kind">类型</param>
        /// <param name="form">当前形态 —— **决定形状语言**（不只是颜色），见类注释</param>
        /// <param name="dir">朝向（挥砍类用它铺弧线，冲刺类用它定「背后」）</param>
        /// <param name="scale">
        /// 整体尺寸倍数：**粒子尺寸与铺开半径都乘它**。
        ///
        /// ⚠️ 这个参数原先**根本没被用上** —— 每种笔法各自写死了 1f / 1.7f，
        /// 传进来的值直接被丢掉。之所以必须让它真的生效：青禾会整体缩放，
        /// 墨迹不跟着缩就会**大过人**。
        /// </param>
        public static void Burst(Vector3 worldPos, Kind kind, CompanionForm form,
                                 Vector2 dir, float scale = 1f)
        {
            EnsureLoaded();

            float k = scale > 0f ? scale : 1f;
            Vector2 forward = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector2.right;
            Color tint = TintFor(form);

            switch (kind)
            {
                case Kind.Slash:
                    Attack(worldPos, form, tint, forward, k, false);
                    break;

                case Kind.HeavySlash:
                    Attack(worldPos, form, tint, forward, k, true);
                    break;

                case Kind.Mist:
                    Puff(worldPos, tint, k);
                    break;

                case Kind.Dash:
                    DashTrail(worldPos, form, tint, forward, k);
                    break;

                case Kind.Block:
                    GuardBurst(worldPos, tint, k, false);
                    break;

                case Kind.PerfectBlock:
                    GuardBurst(worldPos, tint, k, true);
                    break;

                case Kind.FormSwitch:
                    SwitchBloom(worldPos, tint, k);
                    break;

                case Kind.MergeBurst:
                    MergeBurst(worldPos, tint);
                    break;

                case Kind.Finisher:
                    Finisher(worldPos, form, tint, forward, k);
                    break;

                case Kind.Trail:
                    Trail(worldPos, form, tint, forward, k);
                    break;

                default:                // Impact
                    ImpactBurst(worldPos, form, tint, k);
                    break;
            }
        }

        // ============================================================ 按形态分派

        /// <summary>
        /// 挥砍：**按形态选形状语言**。
        ///
        /// ⚠️ <b>目前只有实体形态会走到这里。</b> 近战（轻击/重击）的门控是
        /// <c>CombatController</c> 里那句 <c>else if (_currentForm == Entity)</c> ——
        /// 灵雾按 J 是灵弹、灵合按 J 是灵炁爆发，两者都不出近战。
        /// 下面那两个分支是给「以后加了灵雾/灵合的近战」留的，**别当死代码删掉**；
        /// 但也别指望现在能在游戏里看到它们（想验形状语言请走各自的攻击：
        /// 灵雾看灵弹出膛与命中，灵合看灵炁爆发）。
        /// </summary>
        private static void Attack(Vector3 origin, CompanionForm form, Color tint,
                                   Vector2 forward, float scale, bool heavy)
        {
            switch (form)
            {
                case CompanionForm.Mist:
                    MistSweep(origin, tint, forward, scale, heavy);
                    break;

                case CompanionForm.Merge:
                    MergeSweep(origin, tint, forward, scale, heavy);
                    break;

                default:
                    InkSweep(origin, tint, forward, scale, heavy);
                    break;
            }
        }

        /// <summary>命中：按形态换「炸开的东西」。</summary>
        private static void ImpactBurst(Vector3 origin, CompanionForm form, Color tint, float scale)
        {
            switch (form)
            {
                case CompanionForm.Mist:
                    // 灵雾命中：几团软雾散开，不尖锐；外加一颗过曝的芯
                    for (int i = 0; i < 7; i++)
                    {
                        Vector2 v = Random.insideUnitCircle.normalized * Random.Range(40f, 150f) * scale;

                        Spawn(_bleed, at(origin, 26f * scale), v, tint,
                              life: Random.Range(0.24f, 0.42f),
                              size: Random.Range(60f, 125f) * scale,
                              spin: Random.Range(-50f, 50f),
                              sizeEndRatio: 1.6f, alphaEnd: 0f);
                    }

                    Spawn(_glow, origin, Vector2.zero, Hot(tint, 0.8f),
                          life: 0.13f, size: 100f * scale, spin: 0f,
                          sizeEndRatio: 1.9f, alphaEnd: 0f);

                    break;

                case CompanionForm.Merge:
                    // 灵合命中：一圈光 + 几点星芒，完全不接触
                    RingFx(origin, tint, 70f * scale, 1.9f, 0.26f, 0.55f);
                    Motes(origin, tint, 3, 150f * scale, 0.30f, scale);

                    break;

                default:
                    Spatter(origin, tint, 7, scale);
                    break;
            }
        }

        // ============================================================ 实体：墨

        /// <summary>
        /// 一挥：沿一条**弧线**铺开，两层 —— 主体（晕染）+ 飞白。
        ///
        /// 弧线以朝向前方为中心、左右各张 <see cref="SweepHalfAngle"/> 度；
        /// 粒子沿**切线**方向甩出去（而不是沿半径向外），才是「扫」而不是「炸」。
        /// </summary>
        private const float SweepHalfAngle = 42f;

        private static void InkSweep(Vector3 origin, Color tint, Vector2 forward,
                                     float scale, bool heavy)
        {
            int coreCount = heavy ? 11 : 7;
            int streakCount = heavy ? 18 : 11;
            float girth = heavy ? 1.7f : 1f;

            float baseAngle = Mathf.Atan2(forward.y, forward.x);
            float arcRoll = baseAngle * Mathf.Rad2Deg;

            // ---- 主笔：一道扫出去的刀锋弧 ----
            //
            // 原来**只有碎点**（晕染 + 飞白），看得出「有东西扫过」，看不出刀锋的走向。
            // 弧光是整下攻击的主笔，飞白只是它甩出去的碎屑 —— 有了主笔，
            // 眼睛才会跟着一道弧走。
            //
            // 两层：外层本色、内层近白（见 Hot 的说明）。两份参数**只差尺寸与颜色**，
            // 转动角一致，所以内层严丝合缝压在刃口上，不会错开成两道。
            if (_arc != null)
            {
                Vector3 at = origin + (Vector3)(forward * 18f * girth * scale);

                Spawn(_arc, at, Vector2.zero, tint,
                      life: heavy ? 0.34f : 0.28f,
                      size: (heavy ? 360f : 260f) * girth * scale,
                      spin: 0f, sizeEndRatio: 1.22f, alphaEnd: 0f,
                      initialRoll: arcRoll - 5f, rim: true);

                Spawn(_arc, at, Vector2.zero, Hot(tint, 0.78f),
                      life: heavy ? 0.20f : 0.15f,
                      size: (heavy ? 300f : 215f) * girth * scale,
                      spin: 0f, sizeEndRatio: 1.10f, alphaEnd: 0f,
                      initialRoll: arcRoll - 5f);
            }

            // ---- 爆点：起手处的一闪 ----
            Spawn(_glow, origin + (Vector3)(forward * 22f * girth * scale), Vector2.zero,
                  Hot(tint, 0.55f),
                  life: 0.18f, size: (heavy ? 230f : 160f) * girth * scale,
                  spin: 0f, sizeEndRatio: 1.6f, alphaEnd: 0f, rim: true);

            // 主体层：几团晕染铺在弧线上，尺寸从内到外收锋
            for (int i = 0; i < coreCount; i++)
            {
                float t = coreCount > 1 ? (float)i / (coreCount - 1) : 0.5f;
                float ang = baseAngle + Mathf.Lerp(SweepHalfAngle, -SweepHalfAngle, t) * Mathf.Deg2Rad;

                Vector2 radial = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 tangent = new Vector2(-radial.y, radial.x);

                Vector3 pos = origin + (Vector3)(radial * Random.Range(40f, 96f) * girth * scale);

                // 沿切线甩出，速度随位置递减（起势快、收势慢）
                Vector2 v = tangent * Mathf.Lerp(200f, 80f, t) * girth * scale;

                Spawn(_bleed, pos, v, tint,
                      life: Random.Range(0.26f, 0.40f),
                      size: Mathf.Lerp(150f, 60f, t) * girth * scale,
                      spin: Random.Range(-40f, 40f),
                      sizeEndRatio: 0.35f,      // 粗头尖尾
                      alphaEnd: 0.12f);
            }

            // 飞白层：细长枯笔，朝向=切线，随机取一张变体
            for (int i = 0; i < streakCount; i++)
            {
                float t = Random.value;
                float ang = baseAngle + Mathf.Lerp(SweepHalfAngle, -SweepHalfAngle, t) * Mathf.Deg2Rad;

                Vector2 radial = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 tangent = new Vector2(-radial.y, radial.x);

                Vector3 pos = origin + (Vector3)(radial * Random.Range(30f, 110f) * girth * scale);
                Vector2 v = tangent * Random.Range(260f, 460f) * girth * scale;

                // 贴图默认是横向的，所以旋转角直接取切线的角度 +
                // 一点点随机（完全对齐反而像印刷）
                float tilt = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg
                             + Random.Range(-12f, 12f);

                Sprite s = _streaks[Random.Range(0, _streaks.Length)];

                // 飞白分两档颜色：靠近刃口的那几笔提亮（是「芯」甩出来的），
                // 外圈的用本色。同色一片会显得平。
                Color sc = t < 0.5f ? Hot(tint, 0.35f) : tint;

                Spawn(s, pos, v, sc,
                      life: Random.Range(0.16f, 0.30f),
                      size: Random.Range(120f, 240f) * girth * scale,
                      spin: Random.Range(-25f, 25f),
                      sizeEndRatio: 0.25f,
                      alphaEnd: 0.05f,
                      initialRoll: tilt);
            }

            if (heavy)
            {
                // 重击再加一小撮四散的墨点，交代「劈开了」
                Spatter(origin, tint, 5, 1.4f * scale);
            }
        }

        /// <summary>一溅：四散的墨点（命中反馈）。</summary>
        private static void Spatter(Vector3 origin, Color tint, int count, float scale)
        {
            for (int i = 0; i < count; i++)
            {
                Vector2 v = Random.insideUnitCircle.normalized * Random.Range(160f, 420f) * scale;

                Spawn(_speck, origin, v, tint,
                      life: Random.Range(0.16f, 0.30f),
                      size: Random.Range(30f, 70f) * scale,
                      spin: Random.Range(-320f, 320f),
                      sizeEndRatio: 0.3f,
                      alphaEnd: 0f);
            }
        }

        // ============================================================ 灵雾：雾

        /// <summary>
        /// 灵雾的一挥：**没有飞白**。全是圆形的软晕，往上飘、慢慢散开。
        ///
        /// 和实体的区别不在颜色而在**动势** —— 实体是沿切线「扫」出去（有方向），
        /// 灵雾是原地化开、受一点向上的浮力（无方向）。快动作里一眼能分开。
        /// </summary>
        private static void MistSweep(Vector3 origin, Color tint, Vector2 forward,
                                      float scale, bool heavy)
        {
            int count = heavy ? 12 : 7;
            float girth = heavy ? 1.6f : 1f;
            float baseAngle = Mathf.Atan2(forward.y, forward.x);

            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? (float)i / (count - 1) : 0.5f;
                float ang = baseAngle + Mathf.Lerp(SweepHalfAngle, -SweepHalfAngle, t) * Mathf.Deg2Rad;

                Vector2 radial = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector3 pos = origin + (Vector3)(radial * Random.Range(20f, 70f) * girth * scale);

                // 往外一点点 + 明显向上（雾是浮起来的），比实体慢得多
                Vector2 v = radial * Random.Range(30f, 70f) * girth * scale
                            + Vector2.up * Random.Range(45f, 95f) * girth * scale;

                Sprite s = Random.value < 0.7f ? _bleed : _dot;

                Spawn(s, pos, v, tint,
                      life: Random.Range(0.42f, 0.70f),        // 比墨迹长得多
                      size: Mathf.Lerp(120f, 60f, t) * girth * scale,
                      spin: Random.Range(-35f, 35f),
                      sizeEndRatio: 1.9f,                      // **散开**而不是收锋
                      alphaEnd: 0f);
            }
        }

        /// <summary>一晕：极慢的一小团雾（灵弹出膛 / 命中）。</summary>
        private static void Puff(Vector3 origin, Color tint, float scale)
        {
            for (int i = 0; i < 7; i++)
            {
                Vector2 v = Random.insideUnitCircle.normalized * Random.Range(20f, 90f) * scale
                            + Vector2.up * Random.Range(20f, 60f) * scale;

                Spawn(_bleed, at(origin, 22f * scale), v, tint,
                      life: Random.Range(0.30f, 0.56f),
                      size: Random.Range(50f, 110f) * scale,
                      spin: Random.Range(-60f, 60f),
                      sizeEndRatio: 1.6f,
                      alphaEnd: 0f);
            }

            // 出膛的一闪：灵弹离手要有「亮」才像发射
            Spawn(_glow, origin, Vector2.zero, Hot(tint, 0.85f),
                  life: 0.12f, size: 90f * scale, spin: 0f,
                  sizeEndRatio: 2.0f, alphaEnd: 0f);

            // 几颗星芒跟着弹出去
            Motes(origin, Hot(tint, 0.5f), 3, 220f * scale, 0.22f, scale);
        }

        // ============================================================ 灵合：光

        /// <summary>
        /// 灵合的一挥：**完全不出现墨**。一圈光环从身前荡开 + 几点星芒飞出去。
        ///
        /// 设计意图：灵合是「场」不是「招式」—— 它不该有砍击的方向感，
        /// 而是像能量从身上推出去。所以没有切线、没有收锋，只有放射。
        /// </summary>
        private static void MergeSweep(Vector3 origin, Color tint, Vector2 forward,
                                       float scale, bool heavy)
        {
            float girth = heavy ? 1.5f : 1f;

            // 偏移到朝向前方一点，免得光环整个盖住人物
            Vector3 at = origin + (Vector3)(forward * 30f * scale);

            // 主体：一圈快速荡开的光环（sizeEndRatio > 1 = 扩散）
            RingFx(at, tint, 90f * girth * scale, heavy ? 2.4f : 1.9f,
                   heavy ? 0.34f : 0.26f, 0.65f);

            // 底衬：一团柔光，让环看起来是「发光」而不是「一条线」
            Spawn(_glow, at, Vector2.zero, Fade(tint, 0.55f),
                  life: 0.22f,
                  size: 200f * girth * scale,
                  spin: 0f,
                  sizeEndRatio: 1.5f,
                  alphaEnd: 0f);

            // 星芒：沿扇形飞出去（保留一点点方向感，但比墨迹弱）
            Motes(at, tint, heavy ? 6 : 3, (heavy ? 240f : 170f) * scale, 0.38f, scale,
                  forward, SweepHalfAngle);
        }

        /// <summary>
        /// 一圈扩散的光环。
        /// </summary>
        /// <param name="encircle">
        /// <b>true = 套在人身上</b>（格挡圈、换形态、灵合冲刺）：拆成上下两张**半环**，
        /// 上半圈排到人物之后、下半圈排到人物之前 —— 这样才读得出「套着」，
        /// 而不是「一张圆贴纸糊在脸上」。见 <see cref="SortingOrders.FxOrbitFar"/>。
        ///
        /// false = **离身的冲击波**（灵合的挥击、命中）：整环都在最前面，
        /// 因为它本来就该盖住被打中的东西。
        /// </param>
        private static void RingFx(Vector3 pos, Color tint, float size,
                                   float grow, float life, float alpha,
                                   bool encircle = false)
        {
            float roll = Random.Range(-25f, 25f);

            if (!encircle || _ringFar == null || _ringNear == null)
            {
                Spawn(_ring, pos, Vector2.zero, Fade(tint, alpha),
                      life: life, size: size, spin: roll,
                      sizeEndRatio: grow, alphaEnd: 0f, rim: true);
                return;
            }

            // 两张半环：**除层级外参数必须完全一致**（同一个 roll、同一个生长倍率），
            // 否则两半会错开、在中间露出缝。
            Color far = Fade(tint, alpha * 0.85f);   // 远侧压一点，与 FormAura 的远近明暗一致

            Spawn(_ringFar, pos, Vector2.zero, far,
                  life: life, size: size, spin: roll,
                  sizeEndRatio: grow, alphaEnd: 0f,
                  sortingOrder: SortingOrders.FxOrbitFar, rim: true);

            Spawn(_ringNear, pos, Vector2.zero, Fade(tint, alpha),
                  life: life, size: size, spin: roll,
                  sizeEndRatio: grow, alphaEnd: 0f,
                  sortingOrder: SortingOrders.FxOrbitNear, rim: true);
        }

        /// <summary>
        /// 几点星芒。给了 <paramref name="forward"/> 就落在扇形里，否则整圈乱飞。
        /// </summary>
        private static void Motes(Vector3 pos, Color tint, int count, float speed,
                                  float life, float scale,
                                  Vector2 forward = default, float halfAngle = 180f)
        {
            bool aimed = forward.sqrMagnitude > 0.01f;
            float baseAngle = aimed ? Mathf.Atan2(forward.y, forward.x) : 0f;

            for (int i = 0; i < count; i++)
            {
                float ang = aimed
                    ? baseAngle + Random.Range(-halfAngle, halfAngle) * Mathf.Deg2Rad
                    : Random.Range(0f, Mathf.PI * 2f);

                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 v = dir * Random.Range(speed * 0.5f, speed) * scale;

                // 三成星芒提到近白 —— 满屏同色的点看着像贴纸，掺几颗过曝的才像「光」
                Color mc = Random.value < 0.3f ? Hot(tint, 0.7f) : tint;

                Spawn(_mote, at(pos, Random.Range(0f, 45f) * scale), v, mc,
                      life: Random.Range(life * 0.7f, life * 1.25f),
                      size: Random.Range(40f, 82f) * scale,
                      spin: Random.Range(-90f, 90f),
                      sizeEndRatio: 0.35f,
                      alphaEnd: 0f);
            }
        }

        /// <summary>把一个点沿随机方向挪开一点 —— 粒子全从同一点出发会挤成一颗。</summary>
        private static Vector3 at(Vector3 p, float radius)
        {
            return p + (Vector3)(Random.insideUnitCircle * radius);
        }

        // ============================================================ 蓄力

        /// <summary>蓄力粒子的限频累加器（单玩家游戏，用 static 就够，见 <see cref="ChargeTick"/>）</summary>
        private static float _chargeAccum;

        /// <summary>
        /// 蓄力中**每帧**调用：往身上吸的光点，越蓄越密越亮。
        ///
        /// <b>为什么要有它</b>：蓄力重击原来**在蓄力过程中一点表现都没有** ——
        /// 按住了但屏幕上什么都不变，玩家不知道自己蓄上了、也不知道什么时候满。
        /// 这是典型的「状态没有持续反馈」：只有出手那一下有特效，过程是空的。
        ///
        /// ⚠️ 内部**限频**（默认 0.10s 一颗，越蓄越密到 0.045s）——
        /// 每帧生成会一秒几十个 GameObject，而蓄满要好几秒。
        ///
        /// <paramref name="progress"/> 是蓄力进度 0~1，决定密度、亮度与速度。
        /// </summary>
        public static void ChargeTick(Vector3 pos, CompanionForm form, float progress, float dt)
        {
            EnsureLoaded();

            float p = Mathf.Clamp01(progress);
            _chargeAccum += dt;

            float interval = Mathf.Lerp(0.10f, 0.045f, p);

            if (_chargeAccum < interval)
            {
                return;
            }

            _chargeAccum = 0f;

            Color tint = TintFor(form);

            // 从四周往身上收 —— 「聚气」的动势是**向心**的，不是往外炸
            Vector2 from = Random.insideUnitCircle.normalized * Random.Range(90f, 170f);
            Vector3 spawn = pos + (Vector3)from;

            Spawn(_mote, spawn, -from.normalized * Mathf.Lerp(120f, 260f, p),
                  p > 0.75f ? Hot(tint, 0.6f) : tint,
                  life: Mathf.Lerp(0.34f, 0.20f, p),
                  size: Mathf.Lerp(34f, 62f, p),
                  spin: Random.Range(-120f, 120f),
                  sizeEndRatio: 0.35f, alphaEnd: 0f);
        }

        /// <summary>蓄满的那一瞬间：一圈光环炸开 + 一圈星芒（交代「可以松手了」）。</summary>
        public static void ChargeReady(Vector3 pos, CompanionForm form, float scale)
        {
            EnsureLoaded();

            Color tint = TintFor(form);
            _chargeAccum = 0f;

            RingFx(pos, tint, 120f * scale, 1.9f, 0.26f, 0.85f, encircle: true);
            RingFx(pos, Hot(tint, 0.75f), 80f * scale, 1.4f, 0.15f, 0.9f, encircle: true);
            Spawn(_glow, pos, Vector2.zero, Hot(tint, 0.9f),
                  life: 0.16f, size: 180f * scale, spin: 0f,
                  sizeEndRatio: 1.7f, alphaEnd: 0f);
            Motes(pos, Hot(tint, 0.5f), 6, 200f * scale, 0.30f, scale);
        }

        // ============================================================ 冲刺 / 格挡 / 换形态

        /// <summary>
        /// 冲刺拖尾：粒子全部甩在**背后**（<paramref name="forward"/> 的反向），
        /// 所以看起来是「人往前冲、东西被留在后面」。
        /// </summary>
        private static void DashTrail(Vector3 origin, CompanionForm form, Color tint,
                                      Vector2 forward, float scale)
        {
            Vector2 back = -forward;

            // 起手一闪：三种形态共用（冲刺都要有「弹出去」的那一下）
            Spawn(_glow, origin, Vector2.zero, Hot(tint, 0.7f),
                  life: 0.16f, size: 190f * scale, spin: 0f,
                  sizeEndRatio: 1.9f, alphaEnd: 0f);

            switch (form)
            {
                case CompanionForm.Mist:
                    for (int i = 0; i < 6; i++)
                    {
                        Vector3 pos = origin + (Vector3)(back * Random.Range(10f, 70f) * scale
                                                         + Vector2.up * Random.Range(-25f, 45f) * scale);
                        Vector2 v = back * Random.Range(60f, 150f) * scale
                                    + Vector2.up * Random.Range(20f, 70f) * scale;

                        Spawn(_bleed, pos, v, tint,
                              life: Random.Range(0.28f, 0.48f),
                              size: Random.Range(70f, 130f) * scale,
                              spin: Random.Range(-40f, 40f),
                              sizeEndRatio: 1.7f, alphaEnd: 0f);
                    }

                    break;

                case CompanionForm.Merge:
                    RingFx(origin, tint, 80f * scale, 1.7f, 0.24f, 0.5f, encircle: true);
                    Motes(origin, tint, 4, 130f * scale, 0.30f, scale, back, 60f);

                    break;

                default:
                    for (int i = 0; i < 7; i++)
                    {
                        Vector3 pos = origin + (Vector3)(back * Random.Range(0f, 60f) * scale
                                                         + Vector2.up * Random.Range(-20f, 30f) * scale);

                        // 飞白朝后拉长 —— 冲刺的速度感主要靠它
                        float tilt = Mathf.Atan2(back.y, back.x) * Mathf.Rad2Deg
                                     + Random.Range(-14f, 14f);

                        Spawn(_streaks[Random.Range(0, _streaks.Length)], pos,
                              back * Random.Range(180f, 420f) * scale, tint,
                              life: Random.Range(0.14f, 0.26f),
                              size: Random.Range(120f, 220f) * scale,
                              spin: Random.Range(-18f, 18f),
                              sizeEndRatio: 0.25f, alphaEnd: 0f,
                              initialRoll: tilt);
                    }

                    break;
            }
        }

        /// <summary>
        /// 格挡：身前荡开一圈。**在受击那一刻生成**，不是按键那一刻
        /// （见 <see cref="CombatController.ReceivePlayerDamage"/>）——
        /// 环落在「被打到的地方」才读得出来是「这一下被挡住了」。
        ///
        /// 完美格挡用更大更亮的环 + 一圈星芒，和普通格挡拉开档次。
        /// </summary>
        private static void GuardBurst(Vector3 pos, Color tint, float scale, bool perfect)
        {
            if (perfect)
            {
                // 套在身上 —— 前后两半分别在人后/人前。两道环一快一慢，像两次冲击。
                RingFx(pos, tint, 150f * scale, 2.6f, 0.34f, 0.9f, encircle: true);
                RingFx(pos, Hot(tint, 0.5f), 110f * scale, 3.2f, 0.46f, 0.5f, encircle: true);

                Spawn(_glow, pos, Vector2.zero, Fade(tint, 0.6f),
                      life: 0.26f, size: 260f * scale, spin: 0f,
                      sizeEndRatio: 1.4f, alphaEnd: 0f);
                Spawn(_glow, pos, Vector2.zero, Hot(tint, 0.9f),
                      life: 0.13f, size: 140f * scale, spin: 0f,
                      sizeEndRatio: 1.9f, alphaEnd: 0f);

                // 星芒四散 —— 「完美」要有爆点
                Motes(pos, tint, 12, 320f * scale, 0.42f, scale);
                Motes(pos, Hot(tint, 0.65f), 5, 170f * scale, 0.60f, scale);
                Spatter(pos, tint, 6, 1.2f * scale);
            }
            else
            {
                RingFx(pos, tint, 96f * scale, 1.8f, 0.24f, 0.62f, encircle: true);
                Spawn(_glow, pos, Vector2.zero, Hot(tint, 0.6f),
                      life: 0.12f, size: 120f * scale, spin: 0f,
                      sizeEndRatio: 1.8f, alphaEnd: 0f);
                Motes(pos, tint, 3, 150f * scale, 0.26f, scale);
                Motes(pos, Hot(tint, 0.6f), 2, 100f * scale, 0.34f, scale);
            }
        }

        /// <summary>形态切换：从身上炸开一圈 + 上升的星芒，交代「换了一套东西」。</summary>
        private static void SwitchBloom(Vector3 pos, Color tint, float scale)
        {
            // 套在身上 —— 前后两半分别在人后/人前。两道环：外圈本色、内圈过曝
            RingFx(pos, tint, 130f * scale, 2.1f, 0.34f, 0.8f, encircle: true);
            RingFx(pos, Hot(tint, 0.7f), 96f * scale, 1.6f, 0.20f, 0.85f, encircle: true);

            Spawn(_glow, pos, Vector2.zero, Fade(tint, 0.5f),
                  life: 0.30f, size: 220f * scale, spin: 0f,
                  sizeEndRatio: 1.6f, alphaEnd: 0f);
            Spawn(_glow, pos, Vector2.zero, Hot(tint, 0.9f),
                  life: 0.14f, size: 120f * scale, spin: 0f,
                  sizeEndRatio: 2.0f, alphaEnd: 0f);

            // 上升的光点：一部分近白（过曝）、一部分本色，深浅交错
            for (int i = 0; i < 10; i++)
            {
                Vector3 p = at(pos, 80f * scale);
                Vector2 v = Vector2.up * Random.Range(70f, 230f) * scale
                            + Random.insideUnitCircle * 36f * scale;
                Color mc = Random.value < 0.4f ? Hot(tint, 0.75f) : tint;

                Spawn(_mote, p, v, mc,
                      life: Random.Range(0.36f, 0.70f),
                      size: Random.Range(45f, 95f) * scale,
                      spin: Random.Range(-80f, 80f),
                      sizeEndRatio: 0.4f, alphaEnd: 0f);
            }
        }

        /// <summary>
        /// 轻击三连的**第三段**：同一个动作里最重的一下（带击退）。
        ///
        /// 前两段如果和第三段长得一样，玩家感觉不到「这套打完了」——
        /// 而第三段恰恰是**连段里唯一有击退**的一段，是有节奏意义的。
        /// 所以给它一套明显更「收尾」的东西：更大的一挥 + 一圈扩散 + 更多碎屑。
        /// </summary>
        private static void Finisher(Vector3 origin, CompanionForm form, Color tint,
                                     Vector2 forward, float scale)
        {
            // 主笔：比普通重击还大一圈的刀锋弧，两层（外层本色 + 内层过曝）
            if (_arc != null)
            {
                float roll = Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg - 5f;
                Vector3 at = origin + (Vector3)(forward * 20f * scale);

                Spawn(_arc, at, Vector2.zero, tint,
                      life: 0.30f, size: 430f * scale, spin: 0f,
                      sizeEndRatio: 1.30f, alphaEnd: 0f, initialRoll: roll);
                Spawn(_arc, at, Vector2.zero, Hot(tint, 0.8f),
                      life: 0.22f, size: 350f * scale, spin: 0f,
                      sizeEndRatio: 1.12f, alphaEnd: 0f, initialRoll: roll);
            }

            // 一圈扩散：把「这一下把敌人推开了」交代出来（第三段有击退）
            RingFx(origin + (Vector3)(forward * 40f * scale), tint,
                   150f * scale, 1.9f, 0.30f, 0.6f);

            Spawn(_glow, origin + (Vector3)(forward * 40f * scale), Vector2.zero, Hot(tint, 0.6f),
                  life: 0.18f, size: 260f * scale, spin: 0f,
                  sizeEndRatio: 1.8f, alphaEnd: 0f);

            // 碎屑按形态换一套（形状语言不变，只是量更大）
            if (form == CompanionForm.Merge)
            {
                Motes(origin + (Vector3)(forward * 40f * scale), tint, 10, 380f * scale, 0.40f, scale,
                      forward, 70f);
            }
            else
            {
                Spatter(origin + (Vector3)(forward * 40f * scale), tint, 10, 1.6f * scale);
            }
        }

        /// <summary>
        /// 持续拖尾：**小而淡，靠频率堆**。
        ///
        /// 灵雾形态在飞的时候原来只有环绕件能说明「我在灵雾」，而环绕件贴着身体、
        /// 看不出**移动的轨迹**。这条拖尾让「一路飘过去」这件事有痕迹。
        ///
        /// ⚠️ 它的量必须小（2~3 颗、低不透明度）—— 这是每 0.09 秒一次的**持续**发射，
        /// 单次做大了几秒内就糊满屏。
        /// <paramref name="dir"/> 传**速度方向**，粒子往反方向落。
        /// </summary>
        private static void Trail(Vector3 origin, CompanionForm form, Color tint,
                                  Vector2 dir, float scale)
        {
            Vector2 back = -dir;
            Color faint = Fade(tint, 0.58f);   // 浅色压在浅色山水上本来就淡，这里给足

            for (int i = 0; i < 2; i++)
            {
                Vector3 pos = at(origin, 18f * scale);
                Vector2 v = back * Random.Range(20f, 55f) * scale
                            + Vector2.up * Random.Range(14f, 40f) * scale;

                Spawn(_bleed, pos, v, faint,
                      life: Random.Range(0.30f, 0.52f),
                      size: Random.Range(40f, 78f) * scale,
                      spin: Random.Range(-40f, 40f),
                      sizeEndRatio: 1.7f, alphaEnd: 0f);
            }

            if (Random.value < 0.5f)
            {
                Spawn(_mote, at(origin, 22f * scale),
                      back * Random.Range(30f, 70f) * scale + Vector2.up * 30f * scale,
                      Fade(Hot(tint, 0.5f), 0.5f),
                      life: Random.Range(0.24f, 0.40f),
                      size: Random.Range(22f, 40f) * scale,
                      spin: Random.Range(-60f, 60f),
                      sizeEndRatio: 0.4f, alphaEnd: 0f);
            }
        }

        /// <summary>
        /// 灵炁爆发：灵合的大招，半径 <c>MergeBurstRadius</c>(340) 的 AOE。
        ///
        /// <b>这是补上去的</b>：原来这一招**一点特效都没有** —— 一个 340 半径、
        /// 50 伤害的大范围攻击，画面上只是「敌人突然掉血」。灵合的两种攻击里
        /// 净化光环有常驻的环绕件（<see cref="FormAura"/>），爆发却完全无声无息，
        /// 这正是用户说的「看不出来」。
        ///
        /// ⚠️ <b>不吃人物缩放</b>：伤害半径 340 是**固定世界单位**，不随 bodyScale 缩。
        /// 特效要是跟着人缩，小个子放出来就「小小的一个圈、打到一屏外的敌人」——
        /// 判定和观感对不上。所以这里所有尺寸都按 340 来配，不乘 <c>scale</c>。
        /// </summary>
        private static void MergeBurst(Vector3 pos, Color tint)
        {
            // 三层同心的环，**速度递增**（grow 越大扩散越快）：
            // 快的先冲出去、慢的跟在后面，视觉上就是一波接一波，
            // 而不是「一层圈扩一下就没了」。
            // 套在身上的一圈（前后分层），直径 ≈ 伤害半径
            RingFx(pos, tint, 190f, 1.8f, 0.40f, 0.9f, encircle: true);
            // 离身的冲击波
            RingFx(pos, tint, 150f, 2.3f, 0.48f, 0.55f);
            // 拖尾的余波：最慢最淡，把「范围」交代完整
            RingFx(pos, Hot(tint, 0.35f), 120f, 2.9f, 0.60f, 0.32f);

            // 底衬闪光（外层本色 + 内层过曝，两层才有「芯」）
            Spawn(_glow, pos, Vector2.zero, Fade(tint, 0.8f),
                  life: 0.30f, size: 430f, spin: 0f,
                  sizeEndRatio: 1.7f, alphaEnd: 0f);
            Spawn(_glow, pos, Vector2.zero, Hot(tint, 0.85f),
                  life: 0.16f, size: 230f, spin: 0f,
                  sizeEndRatio: 1.9f, alphaEnd: 0f);

            // 大量星芒四散 —— 大招要有「炸开」的量感。分两波速度，外层慢、内层快。
            Motes(pos, tint, 18, 640f, 0.60f, 1f);
            Motes(pos, tint, 12, 320f, 0.85f, 1f);
            Motes(pos, Hot(tint, 0.6f), 6, 180f, 1.0f, 1f);
        }

        /// <summary>同一个颜色、换一个不透明度。</summary>
        private static Color Fade(Color c, float alpha)
        {
            c.a *= Mathf.Clamp01(alpha);
            return c;
        }

        // ============================================================ 对比度：深色垫底
        //
        // ⚠️ **这是「特效看着不够丰富」的真正原因，不是粒子数不够。**
        //
        // 游戏背景是**浅色的水墨山水**，而灵雾 / 灵合的配色是浅青冷 / 浅暖金 ——
        // 浅色特效压在浅色背景上**几乎没有对比度**，画面上就是一层看不清的雾。
        // 实体那套深墨迹之所以看着还行，就因为它本来就深。
        //
        // 这和 UI 文字早就解决过的是同一个问题（见 UiDraw.MakeText 的说明：
        // 「背景是明暗不定的山水画，不加描边时浅色字会糊进云里」）——
        // 文字加了描边，**粒子当初没加**。所以这里补上同一招：
        // 在彩色粒子底下垫一层略大、更深的副本，形成一圈深色晕边。
        //
        // 只给「结构性」的那几样垫（刀锋弧 / 光环 / 闪光 / 大团晕染）——
        // 它们数量少、决定形状读不读得出来。星芒、飞白这类碎屑不垫：
        // 数量多、本身够小，垫了只会把画面糊满，还白搭一倍粒子。

        /// <summary>垫底色。比墨色更冷更深，和三种形态色都拉得开。</summary>
        private static readonly Color RimColor = new Color(0.06f, 0.09f, 0.13f, 0.55f);

        /// <summary>垫底副本比本体大多少 —— 边太窄看不见，太宽就变成一坨黑。</summary>
        private const float RimScale = 1.26f;

        /// <summary>
        /// 把颜色往白里推 —— 给「芯」用。
        ///
        /// 单色特效看着平，是因为**所有粒子一个颜色**。真实的光是有层次的：
        /// 中心过曝发白、边缘才是本色。所以每一簇都至少两层 ——
        /// 内层 <c>Hot(tint, 0.7)</c> 接近白、外层用原色，
        /// 观感立刻从「一堆同色贴图」变成「一团有芯的光」。
        ///
        /// <paramref name="amount"/>：0 = 原色，1 = 纯白。alpha 不动。
        /// </summary>
        private static Color Hot(Color c, float amount)
        {
            float k = Mathf.Clamp01(amount);

            return new Color(Mathf.Lerp(c.r, 1f, k),
                             Mathf.Lerp(c.g, 1f, k),
                             Mathf.Lerp(c.b, 1f, k),
                             c.a);
        }

        /// <summary>同一个颜色，稍微提亮 + 降低不透明度（给外圈的余晖用）。</summary>
        private static Color Cool(Color c, float brighten, float alpha)
        {
            return new Color(Mathf.Clamp01(c.r * brighten),
                             Mathf.Clamp01(c.g * brighten),
                             Mathf.Clamp01(c.b * brighten),
                             c.a * Mathf.Clamp01(alpha));
        }

        // ============================================================ 单颗

        private static void Spawn(Sprite sprite, Vector3 pos, Vector2 velocity, Color tint,
                                  float life, float size, float spin,
                                  float sizeEndRatio, float alphaEnd,
                                  float initialRoll = -1f,
                                  int sortingOrder = SortingOrders.Fx,
                                  bool rim = false)
        {
            if (sprite == null)
            {
                return;
            }

            // 深色垫底：同样的一次运动参数、略大一圈、更低的不透明度，
            // 于是本体周围留出一圈深色晕边（见上面「对比度」那一段的说明）。
            if (rim)
            {
                Spawn(sprite, pos, velocity, RimColor,
                      life: life, size: size * RimScale, spin: spin,
                      sizeEndRatio: sizeEndRatio, alphaEnd: 0f,
                      initialRoll: initialRoll, sortingOrder: sortingOrder - 1);
            }

            GameObject go = new GameObject("FxParticle");
            go.transform.position = pos;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = tint;
            sr.sortingOrder = sortingOrder;

            // 贴图按 1 像素 = 1 世界单位导入，按目标尺寸（取长边）反算缩放
            Vector2 native = sprite.bounds.size;
            float longest = Mathf.Max(native.x, native.y);

            if (longest > 0f)
            {
                float k = size / longest;
                go.transform.localScale = new Vector3(k, k, 1f);
            }

            InkParticle p = go.AddComponent<InkParticle>();
            p.Init(sr, velocity, life,
                   initialRoll >= 0f ? initialRoll : Random.Range(0f, 360f),
                   spin, sizeEndRatio, alphaEnd);
        }
    }

    /// <summary>
    /// 单颗粒子：自己积分位置、自旋、**收锋**，到点自毁。
    ///
    /// 不走刚体 —— 粒子不需要参与任何物理，写速度反而会踩到「`Animated` 刚体忽略速度」
    /// 那类坑（本项目记过）。
    ///
    /// 名字还叫 InkParticle 是历史原因（最初只做墨迹）；现在它也承载光环与光点，
    /// 但改名字要动一堆调用点、收益为零，就留着了。
    /// </summary>
    public class InkParticle : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Vector2 _velocity;
        private float _life;
        private float _elapsed;
        private float _roll;
        private float _spin;
        private float _sizeEndRatio;
        private float _alphaEnd;
        private float _baseScale;
        private float _baseAlpha;

        /// <summary>
        /// 初始化。由 <c>InkFx.Spawn</c> 调用 —— 那一步已经把尺寸折算成
        /// <c>transform.localScale</c> 了，这里把它记成基准，后面按 <c>sizeEndRatio</c> 收。
        /// </summary>
        /// <param name="sizeEndRatio">结束时尺寸相对初始的倍数。&lt;1 = 收锋（墨迹），&gt;1 = 扩散（光环/雾）</param>
        /// <param name="alphaEnd">结束时的**绝对**不透明度（不是相对值）</param>
        public void Init(SpriteRenderer renderer, Vector2 velocity, float life,
                         float roll, float spin, float sizeEndRatio, float alphaEnd)
        {
            _renderer = renderer;
            _velocity = velocity;
            _life = Mathf.Max(0.0001f, life);   // 防止 life=0 时除零
            _roll = roll;
            _spin = spin;
            _sizeEndRatio = sizeEndRatio;
            _alphaEnd = alphaEnd;
            _baseScale = transform.localScale.x;
            _baseAlpha = renderer != null ? renderer.color.a : 1f;

            transform.rotation = Quaternion.Euler(0f, 0f, _roll);
        }

        /// <summary>
        /// 线性推进 <c>t: 0→1</c>：位置积分、自旋、尺寸与透明度插值，到点自毁。
        ///
        /// 用 <see cref="Time.deltaTime"/> 而不是 unscaled —— 演出期间
        /// <c>timeScale = 0</c>，特效跟着冻住是对的（画面本来就定格了）。
        /// </summary>
        private void Update()
        {
            float dt = Time.deltaTime;
            _elapsed += dt;

            float t = Mathf.Clamp01(_elapsed / _life);

            transform.position += (Vector3)(_velocity * dt);

            _roll += _spin * dt;
            transform.rotation = Quaternion.Euler(0f, 0f, _roll);

            float s = Mathf.Lerp(_baseScale, _baseScale * _sizeEndRatio, t);
            transform.localScale = new Vector3(s, s, 1f);

            if (_renderer != null)
            {
                Color c = _renderer.color;
                c.a = Mathf.Lerp(_baseAlpha, _alphaEnd, t);
                _renderer.color = c;
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
