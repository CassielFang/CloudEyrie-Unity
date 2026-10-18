using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudEyrie.EditorTools
{
    /// <summary>
    /// 按设计稿（`docs/design/五行关卡设计v3.html` 的 SVG 侧视图）把**机关**挪到位。
    ///
    /// <b>为什么和 <see cref="LevelLayoutBuilder"/> 分开</b>
    ///
    /// 那个工具只处理 `Ground` / `Platform`（几何），而且是从零生成碰撞体；
    /// 这个工具处理的是**场景里已经摆好的对象**（敌人、种子、弹跳花、火盆……），
    /// 只改它们的 `Transform.position`。两件事的失败方式不同：前者错了是"地形不对"，
    /// 后者错了是"机关跑到坑里 / 敌人堆在一起"，所以分开跑、分开看报告。
    ///
    /// <b>为什么要有"预演"</b>
    ///
    /// 设计稿的侧视图是**示意图**，不是施工图：
    /// <list type="bullet">
    ///   <item>同一种颜色在不同境代表不同机关，`Platform` 一个 kind 里混着
    ///     静态平台、移动平台、藤蔓阶梯、甚至水面的长条；</item>
    ///   <item>段 0 的教学敌人、部分机关在图上根本没画；</item>
    ///   <item>数量对不上是常态（木境 5 个敌人、设计稿画了 4 个）。</item>
    /// </list>
    /// 所以这里不做"按顺序硬套"，而是先出一张**对比表**：能对上的逐个列出
    /// 「旧坐标 → 新坐标」，对不上的单独列出来，**不静默丢弃、也不重复使用**。
    /// 预演跑通、确认无误，再跑应用。
    ///
    /// <b>坐标</b>
    ///
    /// `LayoutItem` 的 `x/y` 是**左上角**、`y` 是顶边（地面顶线 = -295），单位是世界单位。
    /// 每个 kind 的对齐方式不同（见 `Align` 枚举）：站在地上的东西只挪 x，
    /// 平台对齐**顶面**，碎片/剑轮对齐**中心**，火柱对齐**底边**。
    /// </summary>
    public static class LevelMechanismAligner
    {
        private const string PlatformRootName = "PlatformColliders";

        /// <summary>段 0 的教学敌人：设计稿侧视图没画，不参与匹配（否则会被拉到几千米外）。</summary>
        private const float TutorialSegmentRight = 900f;

        private enum Align
        {
            /// <summary>只挪 x，y 保持不动（站在地面上的东西）。</summary>
            XOnly,

            /// <summary>碰撞盒中心对齐设计稿图形中心（弹跳花：图里画的就是花盘）。</summary>
            Center,

            /// <summary>碰撞盒底边对齐图形底边（火柱：往地里坐一点）。</summary>
            Bottom,

            /// <summary>碰撞盒顶边对齐图形顶边（平台：玩家站的那一面）。</summary>
            Top,

            /// <summary>把对象原点直接放到图形中心（碎片 / 剑轮：原点就是中心）。</summary>
            TransformCenter,
        }

        private struct Foot
        {
            public Vector2 center;
            public float minY;
            public float maxY;
            public Vector3 pos;
        }

        private sealed class Move
        {
            public Transform target;
            public Vector3 to;
            public string kind;
            public string note;

            /// <summary>平台类的设计稿条目：应用时要把同位置的静态碰撞体删掉（避免和移动平台重叠）。</summary>
            public bool dropStaticCollider;
            public LayoutItem slot;
        }

        /// <summary>
        /// 弹跳花的碰撞盒/立绘整修。弹跳花的触发盒原来是 110×160，而立绘只有约 70 高、
        /// 底边对齐碰撞盒底边 —— 玩家会在**花朵上方 ~90 单位的空气**里被弹起。
        /// 这里把碰撞盒缩到立绘本身的尺寸，让「看到的花」就是「踩到的花」。
        /// </summary>
        private sealed class BounceFix
        {
            public BounceFlower flower;
            public Vector2 boxSize;
            public Vector2 boxOffset;
            public Transform art;
            public float artScale;
            public float artLocalY;
        }

        private sealed class Plan
        {
            public readonly List<Move> moves = new List<Move>();
            public readonly List<Transform> blockerFixes = new List<Transform>();
            public readonly List<BounceFix> bounceFixes = new List<BounceFix>();
            public readonly List<string> notes = new List<string>();
        }

        private sealed class Ctx
        {
            public string scene;
            public LayoutItem[] layout;
            public Plan plan = new Plan();

            public void Note(string line)
            {
                plan.notes.Add(line);
            }
        }

        // ============================================================
        // 菜单
        // ============================================================

        [MenuItem("CloudEyrie/关卡工具/按设计稿重排机关/预演（当前场景）")]
        public static void PreviewCurrent() { Run(apply: false, allScenes: false); }

        [MenuItem("CloudEyrie/关卡工具/按设计稿重排机关/应用（当前场景）")]
        public static void ApplyCurrent() { Run(apply: true, allScenes: false); }

        [MenuItem("CloudEyrie/关卡工具/按设计稿重排机关/预演（全部关卡）")]
        public static void PreviewAll() { Run(apply: false, allScenes: true); }

        [MenuItem("CloudEyrie/关卡工具/按设计稿重排机关/应用（全部关卡）")]
        public static void ApplyAll() { Run(apply: true, allScenes: true); }

        // ============================================================
        // 主流程
        // ============================================================

        private static void Run(bool apply, bool allScenes)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[重排机关] 正在播放模式里，不能改场景。先退出 Play。");
                return;
            }

            string[] scenes = allScenes
                ? new[] { "Mu", "Huo", "Tu", "Jin", "Shui" }
                : new[] { SceneManager.GetActiveScene().name };

            SceneSetup[] restore = allScenes ? EditorSceneManager.GetSceneManagerSetup() : null;
            var report = new StringBuilder();
            report.AppendLine($"===== 按设计稿重排机关（{(apply ? "应用" : "预演")}）=====");

            try
            {
                foreach (string name in scenes)
                {
                    LayoutItem[] layout = LayoutOf(name);

                    if (layout == null)
                    {
                        report.AppendLine($"[{name}] 没有布局数据（LevelLayoutData.cs），跳过。");
                        continue;
                    }

                    Scene scene = allScenes
                        ? EditorSceneManager.OpenScene(ScenePath(name), OpenSceneMode.Single)
                        : SceneManager.GetActiveScene();

                    Ctx ctx = new Ctx { scene = name, layout = layout };
                    BuildPlan(ctx);

                    report.AppendLine(Format(ctx, apply));

                    if (apply)
                    {
                        Apply(ctx);
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                    }
                }
            }
            finally
            {
                if (restore != null && restore.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(restore);
                }
            }

            if (apply)
            {
                Debug.Log(report.ToString());
            }
            else
            {
                Debug.Log(report.ToString());
            }
        }

        private static void Apply(Ctx ctx)
        {
            foreach (Move move in ctx.plan.moves)
            {
                if (move.target == null)
                {
                    continue;
                }

                move.target.position = move.to;
                EditorUtility.SetDirty(move.target);
            }

            // 阻挡体在根节点里、朝正下方 —— 少数场景把它摆到了局部坐标 (root.x, root.y)，
            // 表现是屏障/门**完全不挡路**（碰撞体在几千单位外，还不报错）。
            foreach (Transform blocker in ctx.plan.blockerFixes)
            {
                if (blocker == null)
                {
                    continue;
                }

                blocker.localPosition = Vector3.zero;
                EditorUtility.SetDirty(blocker);
            }

            foreach (BounceFix fix in ctx.plan.bounceFixes)
            {
                if (fix.flower == null)
                {
                    continue;
                }

                var box = fix.flower.GetComponent<BoxCollider2D>();

                if (box != null)
                {
                    box.size = fix.boxSize;
                    box.offset = fix.boxOffset;
                    EditorUtility.SetDirty(box);
                }

                if (fix.art != null && fix.artScale > 0.0001f)
                {
                    fix.art.localScale = new Vector3(fix.artScale, fix.artScale, 1f);
                    fix.art.localPosition = new Vector3(0f, fix.artLocalY, 0f);
                    EditorUtility.SetDirty(fix.art);
                }
            }

            // 移动平台的原点就是它自己的碰撞盒；静态平台碰撞体若留在同一位置，
            // 玩家会站在"不动的影子"上，而平台已经从脚下走开。
            foreach (Move move in ctx.plan.moves)
            {
                if (move.dropStaticCollider)
                {
                    DropStaticCollider(move.slot);
                }
            }
        }

        private static string Format(Ctx ctx, bool apply)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"---- {ctx.scene} ----");

            foreach (Move move in ctx.plan.moves)
            {
                if (move.target == null)
                {
                    sb.AppendLine($"  🗑 {move.note}");
                    continue;
                }

                Vector3 from = move.target.position;
                sb.AppendLine($"  ✅ {move.kind,-12} {move.target.name,-18} " +
                              $"({from.x:0},{from.y:0}) → ({move.to.x:0},{move.to.y:0})" +
                              (string.IsNullOrEmpty(move.note) ? "" : $"   {move.note}"));
            }

            foreach (Transform blocker in ctx.plan.blockerFixes)
            {
                sb.AppendLine($"  🔧 阻挡体 {blocker.name}：局部坐标归零（否则门挡在别处）");
            }

            foreach (BounceFix fix in ctx.plan.bounceFixes)
            {
                sb.AppendLine($"  🔧 弹跳花 {fix.flower.name}：碰撞盒 → {fix.boxSize.x:0}×{fix.boxSize.y:0}" +
                              (fix.art != null ? "，立绘铺满碰撞盒" : "（没有 Art 子节点，立绘未调整）"));
            }

            foreach (string note in ctx.plan.notes)
            {
                sb.AppendLine("  " + note);
            }

            sb.AppendLine(apply
                ? $"  合计：移动 {ctx.plan.moves.Count} 个，修正阻挡体 {ctx.plan.blockerFixes.Count} 个。"
                : $"  合计：将移动 {ctx.plan.moves.Count} 个，修正阻挡体 {ctx.plan.blockerFixes.Count} 个（**未修改场景**）。");

            return sb.ToString();
        }

        // ============================================================
        // 每境的编排
        // ============================================================

        private static void BuildPlan(Ctx ctx)
        {
            switch (ctx.scene)
            {
                case "Mu": PlanMu(ctx); break;
                case "Huo": PlanHuo(ctx); break;
                case "Tu": PlanTu(ctx); break;
                case "Jin": PlanJin(ctx); break;
                case "Shui": PlanShui(ctx); break;
            }

            FixBlockers(ctx);
        }

        private static void PlanMu(Ctx ctx)
        {
            AlignAll<VineSeed>(ctx, "Seed", Align.XOnly);
            AlignBounceFlowers(ctx);
            AlignAll<ElementBarrier>(ctx, "Barrier", Align.XOnly);
            AlignAll<EnemyAI>(ctx, "Enemy", Align.XOnly, skipTutorial: true);
            AlignAll<Collectible>(ctx, "Fragment", Align.TransformCenter, skipTutorial: true);
            AlignAll<Checkpoint>(ctx, "Spring", Align.XOnly);
            AlignAll<LevelPortal>(ctx, "Portal", Align.XOnly);

            // 段 1 的三级藤蔓阶梯（VineSeed 的子节点）+ 段 4 移动藤台 + 固定藤台 + 段 5 隐藏高台。
            List<LayoutItem> plats = Items(ctx, "Platform");
            Transform seed = FindInScene("VineSeed");
            Transform[] steps = seed != null
                ? new[] { seed.Find("VineStep_1"), seed.Find("VineStep_2"), seed.Find("VineStep_3") }
                : new Transform[0];

            if (plats.Count == 6)
            {
                for (int i = 0; i < steps.Length; i++)
                {
                    AlignTransform(ctx, steps[i], plats[i], Align.Top, "Platform", "藤蔓阶梯");
                }

                AlignTransform(ctx, FindInScene("SwingPlatform_A"), plats[3], Align.Top, "Platform", "摆荡藤台");
                AlignTransform(ctx, FindInScene("VinePlatform_Mid"), plats[4], Align.Top, "Platform", "固定藤台");
                AlignTransform(ctx, FindInScene("Ledge_B"), plats[5], Align.Top, "Platform", "隐藏高台");
                DropStaticPlatforms(ctx, plats, "藤蔓阶梯 / 移动藤台");
            }
            else
            {
                ctx.Note($"⚠️ 木境设计稿 Platform 数量是 {plats.Count}，预期 6 —— 平台部分跳过，先核对设计稿。");
            }
        }

        private static void PlanHuo(Ctx ctx)
        {
            List<LayoutItem> pillars = Items(ctx, "FirePillar");
            LayoutItem fireWall = TakeFirst(pillars, i => i.h < 300f);

            if (fireWall.kind != null)
            {
                AlignAll<GroupGate>(ctx, new List<LayoutItem> { fireWall }, Align.XOnly, kindLabel: "FirePillar");
            }

            AlignAll<Hazard>(ctx, pillars, Align.Bottom,
                             filter: h => IsPeriodic(h, 0.0001f), kindLabel: "FirePillar");
            AlignAll<Brazier>(ctx, "Brazier", Align.XOnly);
            AlignAll<EnemyAI>(ctx, "Enemy", Align.XOnly, skipTutorial: true);
            AlignAll<Collectible>(ctx, "Fragment", Align.TransformCenter, skipTutorial: true);
            AlignAll<Checkpoint>(ctx, "Spring", Align.XOnly);
            AlignAll<LevelPortal>(ctx, "Portal", Align.XOnly);

            // 岩浆条是**面积型**安全网（坑底），设计稿画的是示意长条、还横跨了实心地面段，
            // 按它摆会把岩浆挪到地面下面 —— 这里明确保持不动。
            ctx.Note("・保持 Lava 系列：设计稿的岩浆条是示意，按真实坑位摆的安全网不该跟着挪。");

            List<LayoutItem> plats = Items(ctx, "Platform");

            if (plats.Count == 6)
            {
                AlignTransform(ctx, FindInScene("LavaStone_1"), plats[0], Align.Top, "Platform", "沉浮浮石");
                AlignTransform(ctx, FindInScene("LavaStone_2"), plats[1], Align.Top, "Platform", "沉浮浮石");
                AlignTransform(ctx, FindInScene("LavaStone_3"), plats[2], Align.Top, "Platform", "沉浮浮石");
                AlignTransform(ctx, FindInScene("LavaSwing"), plats[3], Align.Top, "Platform", "摆荡浮石");
                AlignTransform(ctx, FindInScene("LavaStone_4"), plats[4], Align.Top, "Platform", "沉浮浮石");
                AlignTransform(ctx, FindInScene("Ledge_B"), plats[5], Align.Top, "Platform", "高台");
                DropStaticPlatforms(ctx, plats, "浮石 / 高台");
            }
            else
            {
                ctx.Note($"⚠️ 火境设计稿 Platform 数量是 {plats.Count}，预期 6 —— 平台部分跳过，先核对设计稿。");
            }
        }

        private static void PlanTu(Ctx ctx)
        {
            AlignAll<FallingRock>(ctx, "RockWarning", Align.XOnly, kindLabel: "RockWarning");
            AlignAll<PushableRock>(ctx, "PushRock", Align.XOnly);
            AlignAll<Quicksand>(ctx, "Quicksand", Align.XOnly);

            // 流沙的复活点 / 玄石的凹槽都不是机关组件，按名字单独跟着走。
            List<LayoutItem> sandItems = Items(ctx, "Quicksand");

            if (sandItems.Count > 0)
            {
                OffsetX(ctx, FindInScene("Quicksand_Respawn"), FindInScene("Quicksand"),
                        sandItems[0], "（配套）", "流沙复活点跟流沙同一位移");
            }

            Transform slot = FindInScene("PushRockSlot");
            LayoutItem groove = NearestItem(Items(ctx, "Platform"), 4472f);

            if (slot != null && groove.kind != null)
            {
                ctx.plan.moves.Add(new Move
                {
                    target = slot,
                    to = new Vector3(groove.CenterX, slot.position.y, slot.position.z),
                    kind = "（配套）",
                    note = "玄石凹槽对齐设计稿的小台",
                });
            }
            AlignAll<Collectible>(ctx, "Fragment", Align.TransformCenter, skipTutorial: true);
            AlignAll<ElementBarrier>(ctx, "Barrier", Align.XOnly);
            AlignAll<EnemyAI>(ctx, "Enemy", Align.XOnly, skipTutorial: true);
            AlignAll<Checkpoint>(ctx, "Spring", Align.XOnly);
            AlignAll<LevelPortal>(ctx, "Portal", Align.XOnly);

            // 升降石柱对齐的是**升起后**的顶面：设计稿画的是最高位。
            List<LayoutItem> plats = Items(ctx, "Platform");

            if (plats.Count == 9)
            {
                AlignStepPillar(ctx, FindInScene("StonePillar_1"), plats[0]);
                AlignStepPillar(ctx, FindInScene("StonePillar_2"), plats[1]);
                AlignStepPillar(ctx, FindInScene("StonePillar_3"), plats[2]);
                AlignStepPillar(ctx, FindInScene("SandPillar_1"), plats[5]);
                AlignStepPillar(ctx, FindInScene("SandPillar_2"), plats[6]);
                AlignStepPillar(ctx, FindInScene("SandPillar_3"), plats[7]);
                AlignTransform(ctx, FindInScene("Ledge"), plats[8], Align.Top, "Platform", "石柱顶");

                var used = new List<LayoutItem> { plats[0], plats[1], plats[2], plats[5], plats[6], plats[7], plats[8] };
                DropStaticPlatforms(ctx, used, "升降石柱 / 高台");

                ctx.Note("・保持两块静态平台：段 3 凹槽旁的小台（4472）与推石后升起的石柱（4706）" +
                         "—— 后者设计稿画成实心柱，但玩法上是「推石后升起」，场景目前是常驻碰撞体，待定。");
            }
            else
            {
                ctx.Note($"⚠️ 土境设计稿 Platform 数量是 {plats.Count}，预期 9 —— 平台部分跳过，先核对设计稿。");
            }
        }

        private static void PlanJin(Ctx ctx)
        {
            // 剑气门在图上画成一根细长的"剑"（SwordWheel 颜色），不是旋转剑轮。
            List<LayoutItem> wheels = Items(ctx, "SwordWheel");
            LayoutItem gate = TakeFirst(wheels, i => i.w < 100f && i.h > 200f);

            if (gate.kind != null)
            {
                AlignAll<GroupGate>(ctx, new List<LayoutItem> { gate }, Align.XOnly, kindLabel: "SwordWheel");
            }

            AlignAll<SwordWheel>(ctx, wheels, Align.TransformCenter, kindLabel: "SwordWheel");
            AlignAll<SwordStele>(ctx, "SwordStele", Align.XOnly);
            AlignAll<EnemyAI>(ctx, "Enemy", Align.XOnly, skipTutorial: true);
            AlignAll<Collectible>(ctx, "Fragment", Align.TransformCenter, skipTutorial: true);
            AlignAll<Checkpoint>(ctx, "Spring", Align.XOnly);
            AlignAll<LevelPortal>(ctx, "Portal", Align.XOnly);

            List<LayoutItem> plats = Items(ctx, "Platform");

            if (plats.Count == 5)
            {
                AlignTransform(ctx, FindInScene("MagneticLift_4400"), plats[0], Align.Top, "Platform", "磁力平台");
                AlignTransform(ctx, FindInScene("MagneticLift_5000"), plats[1], Align.Top, "Platform", "磁力平台");
                AlignTransform(ctx, FindInScene("MagneticRide"), plats[3], Align.Top, "Platform", "磁力横移");
                AlignTransform(ctx, FindInScene("Ledge"), plats[4], Align.Top, "Platform", "磁力高台");

                var used = new List<LayoutItem> { plats[0], plats[1], plats[3], plats[4] };
                DropStaticPlatforms(ctx, used, "磁力平台 / 高台");

                ctx.Note($"・保持静态平台 {plats[2].CenterX:0}（段 2 上崖落点）：设计稿那里就是固定台，没有对应对象。");
            }
            else
            {
                ctx.Note($"⚠️ 金境设计稿 Platform 数量是 {plats.Count}，预期 5 —— 平台部分跳过，先核对设计稿。");
            }
        }

        private static void PlanShui(Ctx ctx)
        {
            // 段 4「五行回廊」的土机关在设计稿里画成金色方柱，颜色与"水钟"相同 ——
            // 它对应的是 Recap_Tu（升降石柱），不是一座水钟。
            List<LayoutItem> clocks = Items(ctx, "WaterClock");
            LayoutItem recapStone = TakeFirst(clocks, i => i.h > 150f);
            AlignAll<SwordStele>(ctx, clocks, Align.XOnly, kindLabel: "WaterClock");

            if (recapStone.kind != null)
            {
                AlignStepPillar(ctx, FindInScene("Recap_Tu"), recapStone);
            }

            AlignAll<LevelPortal>(ctx, "Portal", Align.XOnly);
            AlignBounceFlowers(ctx);
            AlignAll<Hazard>(ctx, "FirePillar", Align.Bottom, filter: h => IsPeriodic(h, 0.0001f));
            AlignAll<SwordWheel>(ctx, "SwordWheel", Align.TransformCenter);
            AlignAll<EnemyAI>(ctx, "Enemy", Align.XOnly, skipTutorial: true);
            AlignAll<Collectible>(ctx, "Fragment", Align.TransformCenter, skipTutorial: true);
            AlignAll<Checkpoint>(ctx, "Spring", Align.XOnly);

            // 水幕屏障：设计稿把它画在 x=5365（场景 5400），只挪 x。
            AlignAll<ElementBarrier>(ctx, "WaterCurtain", Align.XOnly);

            List<LayoutItem> plats = Items(ctx, "Platform");

            if (plats.Count == 7)
            {
                AlignTransform(ctx, FindInScene("DriftRaft_3400"), plats[0], Align.Top, "Platform", "水流浮台");
                AlignTransform(ctx, FindInScene("DriftRaft_3700"), plats[1], Align.Top, "Platform", "水流浮台");
                AlignTransform(ctx, FindInScene("DriftRaft_4000"), plats[2], Align.Top, "Platform", "水流浮台");

                var used = new List<LayoutItem> { plats[0], plats[1], plats[2] };
                DropStaticPlatforms(ctx, used, "水流浮台");

                // 水位浮台：设计稿用**水面**长条表示低水位，浮台停在水面线上。
                // 取 x≈4214 的低水位（h=108）作为停靠点，不用那条更高的水面。
                List<LayoutItem> water = Items(ctx, "Water");
                LayoutItem lowWater = TakeFirst(water, i => i.h > 100f);

                if (lowWater.kind != null && Mathf.Abs(lowWater.CenterX - 4214f) < 50f)
                {
                    AlignTransform(ctx, FindInScene("WaterLift"), WithTop(lowWater, lowWater.y),
                                   Align.Top, "Platform", "水位浮台");
                }
                else
                {
                    ctx.Note("⚠️ 没找到水境段 2 的低水位条目，WaterLift 保持原位。");
                }
            }
            else
            {
                ctx.Note($"⚠️ 水境设计稿 Platform 数量是 {plats.Count}，预期 7 —— 平台部分跳过，先核对设计稿。");
            }

            ctx.Note("・保持 CurtainLedge（水幕内多出来的第三层台）：设计稿只有上下两层，这一块是场景遗留，待定。");
        }

        // ============================================================
        // 通用匹配
        // ============================================================

        private static void AlignAll<T>(Ctx ctx, string kind, Align mode,
                                        bool skipTutorial = false,
                                        Func<T, bool> filter = null,
                                        string kindLabel = null) where T : Component
        {
            AlignAll<T>(ctx, Items(ctx, kind), mode, skipTutorial, filter, kindLabel ?? kind);
        }

        /// <summary>
        /// 把设计稿条目和场景组件做**最小总距离**的一一匹配，再逐个算目标位置。
        ///
        /// 不能"按 x 排序后硬套"：数量对不上时（木境 5 敌 vs 设计稿 4），
        /// 硬套会把段 0 的教学敌人一路拉到段 3。改成最优匹配后，多出来的那个自然落单，
        /// 单独报告而不是被静默丢掉。
        /// </summary>
        private static void AlignAll<T>(Ctx ctx, List<LayoutItem> items, Align mode,
                                        bool skipTutorial = false,
                                        Func<T, bool> filter = null,
                                        string kindLabel = null) where T : Component
        {
            if (kindLabel == null)
            {
                kindLabel = typeof(T).Name;
            }

            var objects = new List<T>();

            foreach (T found in UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include))
            {
                if (filter == null || filter(found))
                {
                    objects.Add(found);
                }
            }

            // 段 0 的教学敌人 / 只在图里出现的起点传送门：不参与匹配。
            var pool = new List<T>();

            foreach (T obj in objects)
            {
                if (skipTutorial && GetFoot(obj).center.x < TutorialSegmentRight)
                {
                    ctx.Note($"・保持 {obj.name}（段 0，设计稿侧视图没画）");
                }
                else
                {
                    pool.Add(obj);
                }
            }

            if (items.Count == 0 || pool.Count == 0)
            {
                foreach (LayoutItem item in items)
                {
                    ctx.Note($"❌ 设计稿有 {kindLabel}@{item.CenterX:0}，场景里没有对应对象");
                }

                foreach (T obj in pool)
                {
                    ctx.Note($"・保持 {obj.name}（设计稿里没有它的位置，原样不动）");
                }

                return;
            }

            List<int> itemIndex = new List<int>();
            List<int> objectIndex = new List<int>();

            Pair(items.ConvertAll(i => i.CenterX), pool.ConvertAll(o => GetFoot(o).center.x),
                 itemIndex, objectIndex);

            var pairedItems = new HashSet<int>();
            var pairedObjects = new HashSet<int>();

            for (int k = 0; k < itemIndex.Count; k++)
            {
                pairedItems.Add(itemIndex[k]);
                pairedObjects.Add(objectIndex[k]);

                AlignTransform(ctx, pool[objectIndex[k]].transform, items[itemIndex[k]], mode,
                               kindLabel, null);
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (!pairedItems.Contains(i))
                {
                    ctx.Note($"❌ 设计稿有 {kindLabel}@{items[i].CenterX:0}，场景里没有对应对象");
                }
            }

            for (int i = 0; i < pool.Count; i++)
            {
                if (!pairedObjects.Contains(i))
                {
                    ctx.Note($"・保持 {pool[i].name}（设计稿里没有它的位置，原样不动）");
                }
            }
        }

        /// <summary>把某一个对象对齐到某一条设计稿条目。</summary>
        private static void AlignTransform(Ctx ctx, Transform target, LayoutItem item, Align mode,
                                           string kind, string note, bool dropStatic = false)
        {
            if (target == null)
            {
                ctx.Note($"❌ 找不到对象（{kind}@{item.CenterX:0}）");
                return;
            }

            Foot foot = GetFoot(target);
            Vector3 to = foot.pos;
            float cx = item.CenterX;
            float cy = item.y - item.h * 0.5f;

            switch (mode)
            {
                case Align.XOnly:
                    to.x += cx - foot.center.x;
                    break;

                case Align.Center:
                    to.x += cx - foot.center.x;
                    to.y += cy - foot.center.y;
                    break;

                case Align.Bottom:
                    to.x += cx - foot.center.x;
                    to.y += (item.y - item.h) - foot.minY;
                    break;

                case Align.Top:
                    to.x += cx - foot.center.x;
                    to.y += item.y - foot.maxY;
                    break;

                case Align.TransformCenter:
                    to = new Vector3(cx, cy, foot.pos.z);
                    break;
            }

            ctx.plan.moves.Add(new Move
            {
                target = target,
                to = to,
                kind = kind,
                note = note,
                dropStaticCollider = dropStatic,
                slot = item,
            });
        }

        // ============================================================
        // 平台专用
        // ============================================================

        /// <summary>
        /// 弹跳花：碰撞盒缩到**立绘本身的尺寸**，再把整朵花按设计稿的底边摆。
        ///
        /// 原来的碰撞盒是 110×160，而立绘（`mechanisms/bounce_flower`，318×149）
        /// 按宽度 1.35 倍拟合后只有约 70 高、底边贴碰撞盒底边 ——
        /// 触发区顶端比花朵高约 90，玩家在花上方**空气里**就被弹起。
        /// 现在：盒宽 = 设计稿花盘宽度，盒高 = 盒宽 × 立绘纵横比，立绘铺满盒子。
        /// </summary>
        private static void AlignBounceFlowers(Ctx ctx)
        {
            List<LayoutItem> items = Items(ctx, "Bounce");
            var flowers = new List<BounceFlower>(
                UnityEngine.Object.FindObjectsByType<BounceFlower>(FindObjectsInactive.Include));

            if (items.Count == 0 || flowers.Count == 0)
            {
                foreach (LayoutItem item in items)
                {
                    ctx.Note($"❌ 设计稿有 Bounce@{item.CenterX:0}，场景里没有对应对象");
                }

                return;
            }

            Sprite sprite = Resources.Load<Sprite>("mechanisms/bounce_flower");
            float aspect = sprite != null && sprite.rect.width > 0.01f
                ? sprite.rect.height / sprite.rect.width
                : 149f / 318f;

            var itemIndex = new List<int>();
            var flowerIndex = new List<int>();

            Pair(items.ConvertAll(i => i.CenterX), flowers.ConvertAll(f => GetFoot(f).center.x),
                 itemIndex, flowerIndex);

            var pairedItems = new HashSet<int>();
            var pairedFlowers = new HashSet<int>();

            for (int k = 0; k < itemIndex.Count; k++)
            {
                LayoutItem item = items[itemIndex[k]];
                BounceFlower flower = flowers[flowerIndex[k]];
                pairedItems.Add(itemIndex[k]);
                pairedFlowers.Add(flowerIndex[k]);

                float w = item.w;
                float h = w * aspect;

                var box = flower.GetComponent<BoxCollider2D>();
                var so = new SerializedObject(flower);
                SerializedProperty artProp = so.FindProperty("art");
                Transform art = artProp != null ? artProp.objectReferenceValue as Transform : null;

                ctx.plan.bounceFixes.Add(new BounceFix
                {
                    flower = flower,
                    boxSize = new Vector2(w, h),
                    boxOffset = new Vector2(0f, h * 0.5f),
                    art = art,
                    artScale = sprite != null ? w / sprite.rect.width : 0f,
                    artLocalY = h * 0.5f,
                });

                // 设计稿图形的底边 = 碰撞盒底边 = 节点 y
                ctx.plan.moves.Add(new Move
                {
                    target = flower.transform,
                    to = new Vector3(item.CenterX, item.y - item.h, flower.transform.position.z),
                    kind = "Bounce",
                    note = $"碰撞盒 {w:0}×{h:0}（与立绘同尺寸）",
                });
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (!pairedItems.Contains(i))
                {
                    ctx.Note($"❌ 设计稿有 Bounce@{items[i].CenterX:0}，场景里没有对应对象");
                }
            }

            for (int i = 0; i < flowers.Count; i++)
            {
                if (!pairedFlowers.Contains(i))
                {
                    ctx.Note($"・保持 {flowers[i].name}（设计稿里没有它的位置）");
                }
            }
        }

        /// <summary>升降石柱对齐"升起后"的顶面：设计稿画的是最高位。</summary>
        private static void AlignStepPillar(Ctx ctx, Transform target, LayoutItem item)
        {
            if (target == null)
            {
                ctx.Note($"❌ 找不到石柱（Platform@{item.CenterX:0}）");
                return;
            }

            var platform = target.GetComponent<MovingPlatform>();
            var so = platform != null ? new SerializedObject(platform) : null;
            SerializedProperty ampProp = so != null ? so.FindProperty("amplitude") : null;
            float amplitude = ampProp != null ? ampProp.floatValue : 0f;

            AlignTransform(ctx, target, WithTop(item, item.y - amplitude), Align.Top,
                           "Platform", $"升起后顶面 {item.y:0}（振幅 {amplitude:0}）");
        }

        /// <summary>
        /// 删掉 `PlatformColliders` 里与设计稿条目重合的**静态**平台碰撞体。
        ///
        /// 地面重建时 `LevelLayoutBuilder` 会给每个 Platform 条目都生成一个静态盒子；
        /// 但其中一部分其实是移动平台 / 藤蔓阶梯。留着的话，玩家能站在
        /// "平台还没来"的位置上，而且会和移动平台的碰撞体重叠。
        /// </summary>
        private static void DropStaticPlatforms(Ctx ctx, List<LayoutItem> items, string why)
        {
            foreach (LayoutItem item in items)
            {
                ctx.plan.moves.Add(new Move
                {
                    target = null,
                    kind = "（静态平台）",
                    to = Vector3.zero,
                    note = $"标记删除 {item.CenterX:0} 的静态碰撞体（{why}）",
                    dropStaticCollider = true,
                    slot = item,
                });
            }
        }

        private static void DropStaticCollider(LayoutItem item)
        {
            Transform root = FindInScene(PlatformRootName);

            if (root == null)
            {
                return;
            }

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                Foot foot = GetFoot(child);

                if (Mathf.Abs(foot.center.x - item.CenterX) < 1.5f &&
                    Mathf.Abs(foot.maxY - item.y) < 1.5f)
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        /// <summary>
        /// 阻挡体（ElementBarrier / GroupGate 的 blocker）必须在根节点正中。
        /// 土 / 金 / 水三境把它摆成了局部坐标 = 根的**世界坐标**，于是阻挡体
        /// 跑到了两倍距离外 —— 门看着在，实际上什么都挡不住，且零报错。
        /// </summary>
        private static void FixBlockers(Ctx ctx)
        {
            foreach (ElementBarrier barrier in UnityEngine.Object.FindObjectsByType<ElementBarrier>(FindObjectsInactive.Include))
            {
                AddBlockerFix(ctx, barrier);
            }

            foreach (GroupGate gate in UnityEngine.Object.FindObjectsByType<GroupGate>(FindObjectsInactive.Include))
            {
                AddBlockerFix(ctx, gate);
            }
        }

        private static void AddBlockerFix(Ctx ctx, Component owner)
        {
            var so = new SerializedObject(owner);
            SerializedProperty prop = so.FindProperty("blocker");

            if (prop == null)
            {
                return;
            }

            UnityEngine.Object reference = prop.objectReferenceValue;

            // 字段声明类型是 Collider2D，不是 Transform —— 直接 `as Transform`
            // 会永远得到 null，然后"看起来这条修复根本没触发"。
            Transform blocker = reference as Transform;

            if (blocker == null && reference is Component)
            {
                blocker = ((Component)reference).transform;
            }

            if (blocker == null || blocker.parent != owner.transform)
            {
                return;
            }

            if (blocker.localPosition.sqrMagnitude > 0.0001f)
            {
                ctx.plan.blockerFixes.Add(blocker);
            }
        }

        // ============================================================
        // 工具
        // ============================================================

        private static List<LayoutItem> Items(Ctx ctx, string kind)
        {
            var list = new List<LayoutItem>();

            foreach (LayoutItem item in ctx.layout)
            {
                if (item.kind == kind)
                {
                    list.Add(item);
                }
            }

            return list;
        }

        /// <summary>从列表里取出第一条满足条件的；列表为空/不满足时返回 kind = null 的空条目。</summary>
        private static LayoutItem TakeFirst(List<LayoutItem> items, Func<LayoutItem, bool> predicate)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (predicate(items[i]))
                {
                    LayoutItem found = items[i];
                    items.RemoveAt(i);
                    return found;
                }
            }

            return new LayoutItem { kind = null };
        }

        private static bool IsPeriodic(Hazard hazard, float threshold)
        {
            var so = new SerializedObject(hazard);
            SerializedProperty period = so.FindProperty("period");
            return period != null && period.floatValue > threshold;
        }

        private static LayoutItem WithTop(LayoutItem item, float top)
        {
            return new LayoutItem(item.kind, item.x, top, item.w, item.h);
        }

        private static LayoutItem NearestItem(List<LayoutItem> items, float x)
        {
            LayoutItem best = new LayoutItem { kind = null };
            float bestDistance = float.MaxValue;

            foreach (LayoutItem item in items)
            {
                float d = Mathf.Abs(item.CenterX - x);

                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = item;
                }
            }

            return best;
        }

        /// <summary>把配套对象（复活点之类）按"主人"的水平位移一起挪。</summary>
        private static void OffsetX(Ctx ctx, Transform follower, Transform leader, LayoutItem item,
                                    string kind, string note)
        {
            if (follower == null || leader == null)
            {
                ctx.Note($"⚠️ {kind} {note}：找不到对象，跳过");
                return;
            }

            float dx = item.CenterX - GetFoot(leader).center.x;
            Vector3 pos = follower.position;

            ctx.plan.moves.Add(new Move
            {
                target = follower,
                to = new Vector3(pos.x + dx, pos.y, pos.z),
                kind = kind,
                note = note,
            });
        }

        private static Foot GetFoot(Component c)
        {
            if (c == null)
            {
                return new Foot();
            }

            var box = c.GetComponent<BoxCollider2D>();

            if (box != null)
            {
                Vector2 center = c.transform.TransformPoint(box.offset);
                Vector2 size = Vector2.Scale(box.size, c.transform.lossyScale);

                return new Foot
                {
                    center = center,
                    minY = center.y - size.y * 0.5f,
                    maxY = center.y + size.y * 0.5f,
                    pos = c.transform.position,
                };
            }

            Vector3 p = c.transform.position;

            return new Foot
            {
                center = p,
                minY = p.y,
                maxY = p.y,
                pos = p,
            };
        }

        private static Transform FindInScene(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Transform found = FindRecursive(root.transform, name);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Transform FindRecursive(Transform t, string name)
        {
            if (t.name == name)
            {
                return t;
            }

            for (int i = 0; i < t.childCount; i++)
            {
                Transform found = FindRecursive(t.GetChild(i), name);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static LayoutItem[] LayoutOf(string sceneName)
        {
            switch (sceneName)
            {
                case "Mu": return LevelLayoutData.Mu;
                case "Huo": return LevelLayoutData.Huo;
                case "Tu": return LevelLayoutData.Tu;
                case "Jin": return LevelLayoutData.Jin;
                case "Shui": return LevelLayoutData.Shui;
                default: return null;
            }
        }

        private static string ScenePath(string sceneName)
        {
            return $"Assets/Levels/{sceneName}/{sceneName}.unity";
        }

        // ============================================================
        // 最小总距离匹配（场景数量通常 ≤ 8，直接状压 DP）
        // ============================================================

        /// <summary>
        /// 在两组一维坐标之间找**总距离平方最小**的一一配对（多出来的一侧落单）。
        /// 输出配对下标；未配对的由调用方报告。
        /// </summary>
        private static void Pair(List<float> items, List<float> objects,
                                 List<int> outItems, List<int> outObjects)
        {
            if (items.Count <= objects.Count)
            {
                PairSmallerFirst(items, objects, outItems, outObjects);
            }
            else
            {
                // 反着配，再把下标换回来
                var a = new List<int>();
                var b = new List<int>();
                PairSmallerFirst(objects, items, a, b);

                for (int i = 0; i < a.Count; i++)
                {
                    outObjects.Add(a[i]);
                    outItems.Add(b[i]);
                }
            }
        }

        /// <summary>n ≤ m：每个 design 条目都必须配一个对象，选总距离最小的一组。</summary>
        private static void PairSmallerFirst(List<float> pick, List<float> pool,
                                             List<int> outPick, List<int> outPool)
        {
            int n = pick.Count;
            int m = pool.Count;
            const double Inf = double.MaxValue;
            var dp = new double[n + 1, 1 << m];

            for (int i = 0; i <= n; i++)
            {
                for (int mask = 0; mask < (1 << m); mask++)
                {
                    dp[i, mask] = Inf;
                }
            }

            dp[0, 0] = 0d;

            for (int i = 0; i < n; i++)
            {
                for (int mask = 0; mask < (1 << m); mask++)
                {
                    if (dp[i, mask] == Inf)
                    {
                        continue;
                    }

                    for (int j = 0; j < m; j++)
                    {
                        if ((mask & (1 << j)) != 0)
                        {
                            continue;
                        }

                        double dx = pick[i] - pool[j];
                        double cost = dp[i, mask] + dx * dx;

                        if (cost < dp[i + 1, mask | (1 << j)])
                        {
                            dp[i + 1, mask | (1 << j)] = cost;
                        }
                    }
                }
            }

            double best = Inf;
            int bestMask = 0;

            for (int mask = 0; mask < (1 << m); mask++)
            {
                if (dp[n, mask] < best)
                {
                    best = dp[n, mask];
                    bestMask = mask;
                }
            }

            if (best == Inf)
            {
                return;
            }

            // 回溯
            int current = bestMask;

            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = 0; j < m; j++)
                {
                    if ((current & (1 << j)) == 0)
                    {
                        continue;
                    }

                    int prev = current & ~(1 << j);
                    double dx = pick[i] - pool[j];

                    if (Math.Abs(dp[i, prev] + dx * dx - dp[i + 1, current]) < 1e-4)
                    {
                        outPick.Insert(0, i);
                        outPool.Insert(0, j);
                        current = prev;
                        break;
                    }
                }
            }
        }
    }
}
