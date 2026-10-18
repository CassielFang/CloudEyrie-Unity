using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudEyrie.EditorTools
{
    /// <summary>
    /// 按约定给关卡机关接上美术。
    ///
    /// <b>为什么需要它</b>
    ///
    /// 关卡机关是**手摆进场景**的，而机制脚本里没有任何一个会在运行时自建美术。
    /// 于是「摆了碰撞体、忘了摆美术」成了一种零报错的摆放错误 ——
    /// 表现是玩家被看不见的火喷、踩看不见的岩浆掉灵炁。
    ///
    /// 2026-10-17 实测：木火土金水五境的机关里，灵泉 / 传送门 / 障碍 / 火柱 / 岩浆 /
    /// 流沙 / 火盆 / 剑碑 / 剑轮 …… **一个渲染节点都没有**。
    ///
    /// <b>素材来自两处</b>
    /// <list type="bullet">
    ///   <item>`Assets/Resources/场景小物件/` —— 早就有、一直没接的灵泉石台 / 灵界屏障 / 传送门亮暗</item>
    ///   <item>`Assets/Resources/mechanisms/` —— 美术后来按五行交付的机关立绘，
    ///     由 `tools/import_mechanism_art.py` 从 `美术素材/物件机关/` 搬进来</item>
    /// </list>
    ///
    /// <b>为什么是工具而不是手工改一遍</b>：路线图写明关卡还要重做，每加一关就会重犯同一个错。
    /// 工具可以重复跑，手改不能。幂等：已存在的 Art 节点会被复用而不是再建一个。
    /// </summary>
    public static class MechanismArtWirer
    {
        // ============================================================
        // 素材路径
        // ============================================================

        // 场景小物件（早就有、一直没接线）
        private const string SpringSpritePath = "场景小物件/灵泉石台（回血）";
        private const string BarrierSpritePath = "场景小物件/灵界屏障（门）";
        private const string PortalOnSpritePath = "场景小物件/灵界机关（亮）（传送门）";
        private const string PortalOffSpritePath = "场景小物件/灵界机关（暗）（传送门）";

        // 美术交付的机关立绘（tools/import_mechanism_art.py 搬进来的）
        private const string MechRoot = "mechanisms/";
        private const string BounceFlowerSprite = MechRoot + "bounce_flower";
        private const string SwingVineSprite = MechRoot + "swing_vine";
        private const string VineSeedSprite = MechRoot + "vine_seed";
        private const string FirePillarSprite = MechRoot + "fire_pillar";
        private const string LavaStripSprite = MechRoot + "lava_strip";
        private const string FloatStoneSprite = MechRoot + "float_stone";
        private const string BrazierSprite = MechRoot + "brazier";
        private const string LiftPillarSprite = MechRoot + "lift_pillar";
        private const string PushRockSprite = MechRoot + "push_rock";
        private const string QuicksandSprite = MechRoot + "quicksand";
        private const string FallingRockSprite = MechRoot + "falling_rock";
        private const string SwordSteleSprite = MechRoot + "sword_stele";
        private const string SwordWheelSprite = MechRoot + "sword_wheel";
        private const string MagneticPlatformSprite = MechRoot + "magnetic_platform";
        private const string WaterCurtainSprite = MechRoot + "water_curtain";
        private const string WaterRaftSprite = MechRoot + "water_raft";
        private const string BronzeClockSprite = MechRoot + "bronze_clock";

        // 灵泉石台的内容锚点（照抄旧 LevelDirector 的实测值，换素材必须重新量）
        private const float SpringArtScale = 0.232f;
        private const float SpringContentBottomPx = 444f;

        // 灵界屏障 / 条件门
        //
        // ⚠️ 0.68 时立绘约 722 高，是玩家的三倍，站在门前像一座庙；设计稿的屏障/门只有 ~320 高。
        // 0.42 → 约 446 高，仍是"高门"但不至于把整个画面占满。
        private const float BarrierArtScale = 0.42f;
        private const float BarrierContentBottomPx = 488f;

        /// <summary>内容底边压进地面多少世界单位。</summary>
        private const float GroundSink = 30f;

        private const string PropsLayer = "Props";
        private const string HazardLayer = "Hazard";
        private const string ArtNodeName = "Art";

        private enum Anchor
        {
            /// <summary>美术底边贴碰撞盒底边（立在地上的东西）。</summary>
            Bottom,

            /// <summary>美术顶边贴碰撞盒顶边（站上去的平台、升降柱）。</summary>
            Top,

            /// <summary>美术中心对碰撞盒中心（轮子、漩涡、可推石块）。</summary>
            Center,
        }

        private enum Fit
        {
            Width,
            Height,
        }

        /// <summary>一条「这张图怎么摆」的规格。</summary>
        private struct ArtSpec
        {
            public string path;
            public Anchor anchor;
            public Fit fit;

            /// <summary>拟合后再乘一个系数：美术自带底座/光晕时用它微调观感。</summary>
            public float factor;

            /// <summary>
            /// 画布里**透明留白**的像素数（从画布上沿/下沿到实际内容）。
            ///
            /// 大多数交付图的内容铺满画布（实测 98~100%），留白为 0；
            /// 但浮石底部留着 106px、岩浆条底部留着 44px 的空白 ——
            /// 不扣掉这部分，图会**悬在碰撞盒上方**，看着像浮空。
            ///
            /// ⚠️ 这些数是离线按 alpha &gt; 8 量出来的，**换素材必须重新量**
            /// （与 LevelDirector 里 `SpringContentBottomPx` 是同一类常量）。
            /// </summary>
            public float contentTopPx;
            public float contentBottomPx;

            public string layer;

            public ArtSpec(string path, Anchor anchor, Fit fit, float factor,
                           string layer = PropsLayer,
                           float contentTopPx = 0f, float contentBottomPx = 0f)
            {
                this.path = path;
                this.anchor = anchor;
                this.fit = fit;
                this.factor = factor;
                this.layer = layer;
                this.contentTopPx = contentTopPx;
                this.contentBottomPx = contentBottomPx;
            }
        }

        [MenuItem("CloudEyrie/关卡工具/补机关美术（当前场景）")]
        public static void WireCurrentScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[补美术] 正在播放模式里，不能改场景。先退出 Play。");
                return;
            }

            var report = new List<string>();
            int changed = WireScene(report);

            if (changed > 0)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            Debug.Log(FormatReport("当前场景", report, changed));
        }

        [MenuItem("CloudEyrie/关卡工具/补机关美术（全部关卡场景）")]
        public static void WireAllLevelScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[补美术] 正在播放模式里，不能改场景。先退出 Play。");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            SceneSetup[] restore = EditorSceneManager.GetSceneManagerSetup();
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Levels" });
            var all = new List<string>();

            try
            {
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

                    var report = new List<string>();
                    int changed = WireScene(report);

                    if (changed > 0)
                    {
                        EditorSceneManager.SaveOpenScenes();
                    }

                    all.Add(FormatReport(path, report, changed));
                }
            }
            finally
            {
                if (restore != null && restore.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(restore);
                }
            }

            Debug.Log("===== 补机关美术（全部关卡）=====\n" + string.Join("\n", all));
        }

        // ============================================================
        // 主流程
        // ============================================================

        private static int WireScene(List<string> report)
        {
            string scene = SceneManager.GetActiveScene().name;
            int changed = 0;

            changed += WireCheckpoints(report);
            changed += WirePortals(report);
            changed += WireBarriers(report, scene);
            changed += WireGroupGates(report);
            changed += WireHazards(report);
            changed += WireBounceFlowers(report);
            changed += WireMovingPlatforms(report, scene);
            changed += WireVineSeeds(report);
            changed += WireBraziers(report);
            changed += WireFallingRocks(report);
            changed += WirePushableRocks(report);
            changed += WireQuicksands(report);
            changed += WireSwordSteles(report);
            changed += WireSwordWheels(report);

            return changed;
        }

        // ------------------------------------------------------------
        // 灵泉 / 传送门 / 屏障
        // ------------------------------------------------------------

        private static int WireCheckpoints(List<string> report)
        {
            int changed = 0;

            foreach (Checkpoint cp in FindAll<Checkpoint>())
            {
                Transform art = EnsureArtNode(cp.transform, ArtNodeName);

                if (art == null) { report.Add($"❌ {Path(cp.gameObject)}：建不出 Art"); continue; }

                // 内容底边压地 GrounSink，精灵中心 = 内容底边 + 内容高度/2
                float centerY = SpringContentBottomPx * SpringArtScale - GroundSink;

                bool ok = ApplySprite(art, SpringSpritePath, PropsLayer, SpringArtScale,
                                      new Vector2(0f, centerY), Vector2.zero);

                report.Add(ok ? $"✅ {Path(cp.gameObject)} → 灵泉石台" : $"⚠️ {Path(cp.gameObject)}：灵泉没接上");
                changed++;
            }

            return changed;
        }

        private static int WirePortals(List<string> report)
        {
            int changed = 0;

            foreach (LevelPortal portal in FindAll<LevelPortal>())
            {
                BoxCollider2D box = portal.GetComponent<BoxCollider2D>();

                if (box == null) { report.Add($"❌ {Path(portal.gameObject)}：没有 BoxCollider2D"); continue; }

                Sprite probe = LoadSprite(PortalOnSpritePath, portal.gameObject, report);

                if (probe == null) continue;

                float spriteH = probe.rect.height;
                float scale = spriteH > 0.01f ? box.size.y / spriteH : 1f;
                float bottomY = box.offset.y - box.size.y * 0.5f;
                var pos = new Vector2(box.offset.x, bottomY + spriteH * scale * 0.5f);

                Transform artOn = EnsureArtNode(portal.transform, "ArtOn");
                Transform artOff = EnsureArtNode(portal.transform, "ArtOff");

                bool ok = artOn != null && artOff != null;

                if (ok)
                {
                    ok &= ApplySprite(artOn, PortalOnSpritePath, PropsLayer, scale, pos, Vector2.zero);
                    ok &= ApplySprite(artOff, PortalOffSpritePath, PropsLayer, scale, pos, Vector2.zero);
                    ok &= LinkField(portal, "visualOn", artOn, report, portal.gameObject);
                    ok &= LinkField(portal, "visualOff", artOff, report, portal.gameObject);
                }

                report.Add(ok ? $"✅ {Path(portal.gameObject)} → 传送门 亮/暗" : $"⚠️ {Path(portal.gameObject)}：传送门没接全");
                changed++;
            }

            return changed;
        }

        /// <summary>
        /// 形态屏障。水境的 <c>WaterCurtain</c> 有美术专门交付的「水幕」，
        /// 其余（木/土境的 ElementBarrier）用通用的「灵界屏障（门）」。
        /// </summary>
        private static int WireBarriers(List<string> report, string scene)
        {
            int changed = 0;

            foreach (ElementBarrier barrier in FindAll<ElementBarrier>())
            {
                Transform art = EnsureArtNode(barrier.transform, ArtNodeName);

                if (art == null) { report.Add($"❌ {Path(barrier.gameObject)}：建不出 Art"); continue; }

                bool isCurtain = barrier.gameObject.name.Contains("WaterCurtain");
                bool ok;

                if (isCurtain)
                {
                    ok = ApplyArtSpec(art, barrier.GetComponent<BoxCollider2D>(),
                                      new ArtSpec(WaterCurtainSprite, Anchor.Bottom, Fit.Height, 1.0f));
                }
                else
                {
                    float centerY = BarrierContentBottomPx * BarrierArtScale - GroundSink;
                    ok = ApplySprite(art, BarrierSpritePath, PropsLayer, BarrierArtScale,
                                     new Vector2(0f, centerY), Vector2.zero);
                }

                ok &= LinkField(barrier, "art", art, report, barrier.gameObject);

                report.Add(ok
                    ? $"✅ {Path(barrier.gameObject)} → {(isCurtain ? "水幕" : "灵界屏障")}"
                    : $"⚠️ {Path(barrier.gameObject)}：屏障美术没接上");

                changed++;
            }

            return changed;
        }

        /// <summary>
        /// 条件门（<c>GroupGate</c>）：火境 FireWall、金境 SwordGate。
        ///
        /// ⚠️ 别信场景 YAML 里的 <c>m_EditorClassIdentifier</c> —— 火境那个写着
        /// <c>CloudEyrie.FireWall</c>，那个类早就不存在了（改名前的残留字符串）。
        /// Unity 按 GUID 解析，组件其实是 <c>GroupGate</c>。
        /// </summary>
        private static int WireGroupGates(List<string> report)
        {
            int changed = 0;

            foreach (GroupGate gate in FindAll<GroupGate>())
            {
                Transform art = EnsureArtNode(gate.transform, ArtNodeName);

                if (art == null) { report.Add($"❌ {Path(gate.gameObject)}：建不出 Art"); continue; }

                bool ok;

                if (gate.gameObject.name.Contains("FireWall"))
                {
                    // 火墙不能再用那张**石质水门**的通用屏障图 —— 在火境里是一道
                    // 蓝汪汪的石头拱门，跟"火墙"完全不是一个东西。
                    // 用火柱立绘排成一排：既贴合主题，也直观表达"整面墙都在烧"。
                    ok = WireFireWall(gate, art, report);
                    report.Add(ok ? $"✅ {Path(gate.gameObject)} → 火墙（火柱排列）" : $"⚠️ {Path(gate.gameObject)}：火墙没接上");
                    changed++;
                    continue;
                }

                float centerY = BarrierContentBottomPx * BarrierArtScale - GroundSink;

                ok = ApplySprite(art, BarrierSpritePath, PropsLayer, BarrierArtScale,
                                 new Vector2(0f, centerY), Vector2.zero);
                ok &= LinkField(gate, "art", art, report, gate.gameObject);

                report.Add(ok ? $"✅ {Path(gate.gameObject)} → 灵界屏障/条件门" : $"⚠️ {Path(gate.gameObject)}：条件门没接上");
                changed++;
            }

            return changed;
        }

        /// <summary>
        /// 火墙 = 一排火柱立绘。
        ///
        /// 通用屏障图是**石质水门**（蓝汪汪的瀑布门），放在火境里非常出戏；
        /// 而火墙的语义就是"一整面火"，用已有的火柱图横向排开最直接，
        /// 打开时 GroupGate 会把子节点的 renderer 一起淡出，不需要额外逻辑。
        ///
        /// 高度取 ~300（设计稿火墙 280），玩家跳不过去（跳跃 160），
        /// 但不会像原来的 722 高石门那样顶天立地。
        /// </summary>
        private static bool WireFireWall(GroupGate gate, Transform art, List<string> report)
        {
            Sprite sprite = LoadSprite(FirePillarSprite, gate.gameObject, report);

            if (sprite == null)
            {
                return false;
            }

            BoxCollider2D box = gate.GetComponent<BoxCollider2D>();

            if (box == null)
            {
                report.Add($"❌ {Path(gate.gameObject)}：没有 BoxCollider2D");
                return false;
            }

            // 幂等：清掉 Art 上/下上一轮接的图（通用屏障的那一张也在里面）
            for (int i = art.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(art.GetChild(i).gameObject);
            }

            SpriteRenderer artSr = art.GetComponent<SpriteRenderer>();

            if (artSr != null)
            {
                Object.DestroyImmediate(artSr);
            }

            art.localPosition = Vector3.zero;
            art.localScale = Vector3.one;

            float targetH = 300f;
            float scale = sprite.rect.height > 0.01f ? targetH / sprite.rect.height : 1f;
            float flameW = sprite.rect.width * scale;
            int count = Mathf.Max(3, Mathf.CeilToInt(box.size.x / Mathf.Max(1f, flameW)));
            float step = box.size.x / count;
            float bottom = box.offset.y - box.size.y * 0.5f;
            float left = box.offset.x - box.size.x * 0.5f;

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"Flame_{i}");
                go.transform.SetParent(art, false);
                go.transform.localScale = new Vector3(scale, scale, 1f);
                go.transform.localPosition = new Vector3(
                    left + step * (i + 0.5f),
                    bottom + targetH * 0.5f,
                    0f);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingLayerName = HazardLayer;
                sr.sortingOrder = 0;
            }

            return LinkField(gate, "art", art, report, gate.gameObject);
        }

        // ------------------------------------------------------------
        // 危险地形
        // ------------------------------------------------------------

        /// <summary>
        /// 判据优先用**配置**：
        /// <c>period &gt; 0</c> = 周期性喷发 = 火柱；<c>period == 0 且 spiritDamage &gt; 0</c> = 常驻伤害 = 岩浆。
        /// 其余一律跳过（坑/坠落区本来就该隐形），并且**逐个打印原因**。
        /// 只有 <c>Water_Drift</c> 因配置与 Pit 完全相同而靠名字 —— 明面上说清楚。
        /// </summary>
        private static int WireHazards(List<string> report)
        {
            int changed = 0;

            foreach (Hazard hazard in FindAll<Hazard>())
            {
                var so = new SerializedObject(hazard);
                SerializedProperty periodProp = so.FindProperty("period");
                SerializedProperty damageProp = so.FindProperty("spiritDamage");

                float period = periodProp != null ? periodProp.floatValue : 0f;
                float damage = damageProp != null ? damageProp.floatValue : 0f;

                ArtSpec spec;
                string why;

                if (period > 0.0001f)
                {
                    spec = new ArtSpec(FirePillarSprite, Anchor.Bottom, Fit.Height, 1.0f, HazardLayer);
                    why = $"周期性（period={period}）→ 间歇火柱";
                }
                else if (damage > 0.0001f)
                {
                    // 岩浆条底部有 44px 透明留白（实测），不扣掉会浮在坑底之上
                    spec = new ArtSpec(LavaStripSprite, Anchor.Bottom, Fit.Width, 1.0f, HazardLayer, 0f, 44f);
                    why = $"常驻伤害（spiritDamage={damage}）→ 岩浆";
                }
                else if (hazard.gameObject.name.Contains("Water_Drift"))
                {
                    // 配置与 Pit 完全一样（period=0、spiritDamage=0、有 respawnPoint），
                    // 只能靠名字。而交付美术里**没有**对应的横向"水流"——
                    // 水幕是竖的屏障、水流浮台是平台，都不合用。保留已有的图，并说清楚。
                    report.Add($"・跳过 {Path(hazard.gameObject)}：Water_Drift（配置与 Pit 相同，只能靠名字）" +
                               "—— 交付美术里没有对应的横向水流素材，保留现状");
                    continue;
                }
                else
                {
                    report.Add($"・跳过 {Path(hazard.gameObject)}：period=0 且 spiritDamage=0 → 坑/坠落区，本来就该隐形");
                    continue;
                }

                BoxCollider2D box = hazard.GetComponent<BoxCollider2D>();

                if (box == null) { report.Add($"❌ {Path(hazard.gameObject)}：没有 BoxCollider2D"); continue; }

                Transform art = EnsureArtNode(hazard.transform, ArtNodeName);

                if (art == null) { report.Add($"❌ {Path(hazard.gameObject)}：建不出 Art"); continue; }

                bool ok = ApplyArtSpec(art, box, spec);
                ok &= LinkField(hazard, "visual", art, report, hazard.gameObject);

                report.Add(ok ? $"✅ {Path(hazard.gameObject)} → {why}" : $"⚠️ {Path(hazard.gameObject)}：{why}，但美术没接上");
                changed++;
            }

            return changed;
        }

        // ------------------------------------------------------------
        // 其余机关
        // ------------------------------------------------------------

        private static int WireBounceFlowers(List<string> report)
        {
            int changed = 0;

            foreach (BounceFlower flower in FindAll<BounceFlower>())
            {
                BoxCollider2D box = flower.GetComponent<BoxCollider2D>();

                if (box == null) { report.Add($"❌ {Path(flower.gameObject)}：没有 BoxCollider2D"); continue; }

                Transform art = EnsureArtNode(flower.transform, ArtNodeName);

                if (art == null) continue;

                // 立绘铺满碰撞盒（1.0，不再放大）：弹跳花的碰撞盒由
                // LevelMechanismAligner 缩到立绘本身的尺寸，放大 1.35 会让立绘
                // 超出触发区，看起来像"踩在花上方的空气里被弹起"。
                bool ok = ApplyArtSpec(art, box, new ArtSpec(BounceFlowerSprite, Anchor.Bottom, Fit.Width, 1.0f));
                ok &= LinkField(flower, "art", art, report, flower.gameObject);

                report.Add(ok ? $"✅ {Path(flower.gameObject)} → 弹跳花" : $"⚠️ {Path(flower.gameObject)}：弹跳花没接上");
                changed++;
            }

            return changed;
        }

        /// <summary>
        /// 移动平台。碰撞盒只有顶面那一块（192×34），所以美术**顶边对齐碰撞盒顶边**，
        /// 柱体/浮石往下延伸 —— 玩家站在顶面。
        ///
        /// 美术按机制各画了一张，而 `VinePlatform` / `SwingPlatform` 这两个 prefab
        /// 是跨关复用的，所以只能按**关卡**挑图。
        /// </summary>
        private static int WireMovingPlatforms(List<string> report, string scene)
        {
            // 每境的移动平台是同一个 prefab，但美术各画了一张，所以按关卡挑。
            // 浮石底部有 106px 透明留白（实测），要扣掉，否则浮石会悬在顶面之上。
            ArtSpec? spec = scene switch
            {
                "Mu" => new ArtSpec(SwingVineSprite, Anchor.Top, Fit.Width, 1.0f),
                "Huo" => new ArtSpec(FloatStoneSprite, Anchor.Top, Fit.Width, 1.0f, PropsLayer, 207f, 0f),
                "Tu" => new ArtSpec(LiftPillarSprite, Anchor.Top, Fit.Width, 1.0f),
                "Jin" => new ArtSpec(MagneticPlatformSprite, Anchor.Top, Fit.Width, 1.0f),
                "Shui" => new ArtSpec(WaterRaftSprite, Anchor.Top, Fit.Width, 1.0f),
                _ => null,
            };

            if (spec == null)
            {
                return 0;
            }

            int changed = 0;

            foreach (MovingPlatform platform in FindAll<MovingPlatform>())
            {
                BoxCollider2D box = platform.GetComponent<BoxCollider2D>();

                if (box == null) { report.Add($"❌ {Path(platform.gameObject)}：没有 BoxCollider2D"); continue; }

                Transform art = EnsureArtNode(platform.transform, ArtNodeName);

                if (art == null) continue;

                bool ok = ApplyArtSpec(art, box, spec.Value);

                report.Add(ok ? $"✅ {Path(platform.gameObject)} → {System.IO.Path.GetFileName(spec.Value.path)}" : $"⚠️ {Path(platform.gameObject)}：平台美术没接上");
                changed++;
            }

            return changed;
        }

        private static int WireVineSeeds(List<string> report)
        {
            int changed = 0;

            foreach (VineSeed seed in FindAll<VineSeed>())
            {
                BoxCollider2D box = seed.GetComponent<BoxCollider2D>();

                if (box == null) continue;

                Transform art = EnsureArtNode(seed.transform, ArtNodeName);

                if (art == null) continue;

                bool ok = ApplyArtSpec(art, box, new ArtSpec(VineSeedSprite, Anchor.Bottom, Fit.Height, 1.0f));

                report.Add(ok ? $"✅ {Path(seed.gameObject)} → 藤蔓种子" : $"⚠️ {Path(seed.gameObject)}：藤种没接上");
                changed++;
            }

            return changed;
        }

        /// <summary>
        /// 火盆。美术只有「点燃」一张，所以亮/暗两态用同一张图 ——
        /// 切换逻辑照常工作，只是暂时看不出差别（等美术补待机图）。
        /// </summary>
        private static int WireBraziers(List<string> report)
        {
            int changed = 0;

            foreach (Brazier brazier in FindAll<Brazier>())
            {
                BoxCollider2D box = brazier.GetComponent<BoxCollider2D>();

                if (box == null) continue;

                Transform on = EnsureArtNode(brazier.transform, "ArtOn");
                Transform off = EnsureArtNode(brazier.transform, "ArtOff");

                if (on == null || off == null) continue;

                // 1.5 倍时火盆立绘约 165 高，和玩家差不多高；火盆应该是腰高的。
                // 碰撞盒是 70×110，按 1.0 拟合后立绘正好落在盒子上。
                var spec = new ArtSpec(BrazierSprite, Anchor.Bottom, Fit.Height, 1.0f);

                bool ok = ApplyArtSpec(on, box, spec);
                ok &= ApplyArtSpec(off, box, spec);
                ok &= LinkField(brazier, "visualOn", on, report, brazier.gameObject);
                ok &= LinkField(brazier, "visualOff", off, report, brazier.gameObject);

                report.Add(ok ? $"✅ {Path(brazier.gameObject)} → 火盆" : $"⚠️ {Path(brazier.gameObject)}：火盆没接上");
                changed++;
            }

            return changed;
        }

        /// <summary>
        /// 预警落石。交付的是**石头本身**（带裂纹），要换掉 <c>rock</c> 指向的那个子节点。
        /// ⚠️ `warningMark`（落点预警圈）美术没给单独素材，仍然空着 —— 这条会打印出来。
        /// </summary>
        private static int WireFallingRocks(List<string> report)
        {
            int changed = 0;

            foreach (FallingRock rock in FindAll<FallingRock>())
            {
                var so = new SerializedObject(rock);
                SerializedProperty rockProp = so.FindProperty("rock");
                Transform rockNode = rockProp != null ? rockProp.objectReferenceValue as Transform : null;

                if (rockNode == null)
                {
                    report.Add($"❌ {Path(rock.gameObject)}：rock 字段是空的（落石不会被换图）");
                    continue;
                }

                SpriteRenderer sr = rockNode.GetComponent<SpriteRenderer>();
                Sprite sprite = LoadSprite(FallingRockSprite, rock.gameObject, report);

                if (sr == null || sprite == null) continue;

                // 石头尺寸按碰撞体的宽度定，保证落地时压得住判定
                BoxCollider2D rubble = null;
                SerializedProperty rubbleProp = so.FindProperty("rubbleCollider");
                rubble = rubbleProp != null ? rubbleProp.objectReferenceValue as BoxCollider2D : null;

                float wantW = rubble != null ? rubble.size.x * 1.15f : 130f;
                float scale = sprite.rect.width > 0.01f ? wantW / sprite.rect.width : 1f;

                sr.sprite = sprite;
                sr.sortingLayerName = PropsLayer;
                sr.sortingOrder = 0;

                rockNode.localScale = new Vector3(scale, scale, 1f);
                EditorUtility.SetDirty(sr);

                var warnProp = so.FindProperty("warningMark");
                bool warnEmpty = warnProp == null || warnProp.objectReferenceValue == null;

                report.Add($"✅ {Path(rock.gameObject)} → 预警落石{(warnEmpty ? "（⚠️ warningMark 仍为空：美术未给预警圈素材）" : "")}");
                changed++;
            }

            return changed;
        }

        private static int WirePushableRocks(List<string> report)
        {
            int changed = 0;

            foreach (PushableRock rock in FindAll<PushableRock>())
            {
                BoxCollider2D box = rock.GetComponent<BoxCollider2D>();

                if (box == null) continue;

                Transform art = EnsureArtNode(rock.transform, ArtNodeName);

                if (art == null) continue;

                bool ok = ApplyArtSpec(art, box, new ArtSpec(PushRockSprite, Anchor.Center, Fit.Width, 1.0f));

                // 玄石的**根节点自带一张占位图**（platform_board）。根节点不能缩放
                // （会连碰撞盒一起缩），所以美术挂在子节点上 —— 那就得把根上那张关掉，
                // 否则两块图叠在一起。
                DisableRootRenderer(rock.gameObject);

                report.Add(ok ? $"✅ {Path(rock.gameObject)} → 可推玄石" : $"⚠️ {Path(rock.gameObject)}：玄石没接上");
                changed++;
            }

            return changed;
        }

        private static int WireQuicksands(List<string> report)
        {
            int changed = 0;

            foreach (Quicksand sand in FindAll<Quicksand>())
            {
                BoxCollider2D box = sand.GetComponent<BoxCollider2D>();

                if (box == null) continue;

                Transform art = EnsureArtNode(sand.transform, ArtNodeName);

                if (art == null) continue;

                // 流沙是一张带透视的椭圆漩涡，比碰撞盒高（480 vs 240）。
                // 按**中心**对齐会让漩涡悬到地面线以上；改成底边贴碰撞盒底边才坐得住。
                bool ok = ApplyArtSpec(art, box, new ArtSpec(QuicksandSprite, Anchor.Bottom, Fit.Width, 1.0f));

                report.Add(ok ? $"✅ {Path(sand.gameObject)} → 流沙" : $"⚠️ {Path(sand.gameObject)}：流沙没接上");
                changed++;
            }

            return changed;
        }

        /// <summary>
        /// 剑碑。金境是剑碑，水境的 <c>WaterClock</c> 挂的也是 <c>SwordStele</c> 组件，
        /// 但美术给它单独画了「青铜水钟」，所以按名字分。
        /// </summary>
        private static int WireSwordSteles(List<string> report)
        {
            int changed = 0;

            foreach (SwordStele stele in FindAll<SwordStele>())
            {
                BoxCollider2D box = stele.GetComponent<BoxCollider2D>();

                if (box == null) continue;

                bool isClock = stele.gameObject.name.Contains("WaterClock");
                var spec = new ArtSpec(isClock ? BronzeClockSprite : SwordSteleSprite,
                                       Anchor.Bottom, Fit.Height, 1.0f);

                Transform on = EnsureArtNode(stele.transform, "ArtOn");
                Transform off = EnsureArtNode(stele.transform, "ArtOff");

                if (on == null || off == null) continue;

                bool ok = ApplyArtSpec(on, box, spec);
                ok &= ApplyArtSpec(off, box, spec);
                ok &= LinkField(stele, "visualOn", on, report, stele.gameObject);
                ok &= LinkField(stele, "visualOff", off, report, stele.gameObject);

                // 场景里原本就有 On / Off 两个占位节点（platform_board）——
                // 只接 ArtOn/ArtOff 会让两套图同时渲染。把没被用到的那套关掉。
                DisableOtherRenderers(stele.transform, on, off);

                report.Add(ok ? $"✅ {Path(stele.gameObject)} → {(isClock ? "青铜水钟" : "剑碑")}"
                              : $"⚠️ {Path(stele.gameObject)}：剑碑没接上");
                changed++;
            }

            return changed;
        }

        private static int WireSwordWheels(List<string> report)
        {
            int changed = 0;

            foreach (SwordWheel wheel in FindAll<SwordWheel>())
            {
                Transform art = EnsureArtNode(wheel.transform, ArtNodeName);

                if (art == null) continue;

                // 剑轮没有碰撞盒，按刀长的两倍定直径
                var so = new SerializedObject(wheel);
                SerializedProperty bladeProp = so.FindProperty("bladeLength");
                float blade = bladeProp != null ? bladeProp.floatValue : 130f;
                float diameter = blade * 2.2f;

                Sprite probe = LoadSprite(SwordWheelSprite, wheel.gameObject, report);

                if (probe == null) continue;

                float scale = probe.rect.height > 0.01f ? diameter / probe.rect.height : 1f;

                bool ok = ApplySprite(art, SwordWheelSprite, PropsLayer, scale, Vector2.zero, Vector2.zero);
                ok &= LinkField(wheel, "art", art, report, wheel.gameObject);

                report.Add(ok ? $"✅ {Path(wheel.gameObject)} → 旋转剑轮（直径 {diameter:0}）" : $"⚠️ {Path(wheel.gameObject)}：剑轮没接上");
                changed++;
            }

            return changed;
        }

        // ============================================================
        // 工具函数
        // ============================================================

        /// <summary>按规格（锚点 + 拟合方向 + 系数）摆好一张图。</summary>
        private static bool ApplyArtSpec(Transform art, BoxCollider2D box, ArtSpec spec)
        {
            Sprite sprite = Resources.Load<Sprite>(spec.path);

            if (sprite == null)
            {
                Debug.LogError($"[补美术] Resources.Load<Sprite>(\"{spec.path}\") 返回 null。" +
                               "检查图是否在 Resources 下、是否被导成 Sprite、PPU 是否为 1。");
                return false;
            }

            float refDim = spec.fit == Fit.Width ? sprite.rect.width : sprite.rect.height;
            float target = (spec.fit == Fit.Width ? box.size.x : box.size.y) * spec.factor;
            float scale = refDim > 0.01f ? target / refDim : 1f;
            float artH = sprite.rect.height * scale;

            float boxBottom = box.offset.y - box.size.y * 0.5f;
            float boxTop = box.offset.y + box.size.y * 0.5f;

            // 锚点算的是**内容**的边，不是画布的边 —— 画布上下的透明留白要扣掉
            float y = spec.anchor switch
            {
                Anchor.Bottom => boxBottom + artH * 0.5f - spec.contentBottomPx * scale,
                Anchor.Top => boxTop - artH * 0.5f + spec.contentTopPx * scale,
                _ => box.offset.y,
            };

            return ApplySprite(art, spec.path, spec.layer, scale,
                               new Vector2(box.offset.x, y), Vector2.zero);
        }

        /// <summary>
        /// 关掉根节点自己的 SpriteRenderer。
        /// 有的机关（如可推玄石）把占位图直接挂在根节点上 —— 根节点不能缩放（会连碰撞盒一起缩），
        /// 所以美术只能挂子节点，根上那张必须关掉，否则两层图叠在一起。
        /// </summary>
        private static void DisableRootRenderer(GameObject root)
        {
            SpriteRenderer sr = root.GetComponent<SpriteRenderer>();

            if (sr != null && sr.enabled)
            {
                sr.enabled = false;
                EditorUtility.SetDirty(sr);
            }
        }

        /// <summary>
        /// 关掉「不是我们摆的那几个」直接子节点上的渲染器。
        /// 场景里有的机关（剑碑）本来就带 On/Off 两个占位节点，接上新美术后会同时渲染。
        /// </summary>
        private static void DisableOtherRenderers(Transform root, params Transform[] keep)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);

                if (System.Array.IndexOf(keep, child) >= 0)
                {
                    continue;
                }

                SpriteRenderer sr = child.GetComponent<SpriteRenderer>();

                if (sr != null && sr.enabled)
                {
                    sr.enabled = false;
                    EditorUtility.SetDirty(sr);
                }
            }
        }

        /// <summary>找一个同名子节点复用，没有才新建 —— 保证重复跑不会堆出第二个 Art。</summary>
        private static Transform EnsureArtNode(Transform parent, string name)
        {
            Transform found = parent.Find(name);

            if (found == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                found = go.transform;
            }

            found.gameObject.layer = 0;
            return found;
        }

        /// <summary>设 sprite / 排序层 / 缩放 / 局部位置。返回是否成功（图加载不到就是失败）。</summary>
        private static bool ApplySprite(Transform art, string spritePath, string layerName,
                                        float scale, Vector2 localPos, Vector2 tiledBox)
        {
            Sprite sprite = Resources.Load<Sprite>(spritePath);

            if (sprite == null)
            {
                Debug.LogError($"[补美术] Resources.Load<Sprite>(\"{spritePath}\") 返回 null。");
                return false;
            }

            SpriteRenderer sr = art.GetComponent<SpriteRenderer>();

            if (sr == null)
            {
                sr = art.gameObject.AddComponent<SpriteRenderer>();
            }

            sr.sprite = sprite;
            sr.sortingLayerName = layerName;
            sr.sortingOrder = 0;
            sr.color = Color.white;

            float s = scale;

            if (tiledBox.x > 0.0001f && tiledBox.y > 0.0001f)
            {
                float spriteW = sprite.rect.width;
                float spriteH = sprite.rect.height;
                float shortSide = Mathf.Min(tiledBox.x, tiledBox.y);
                float spriteShort = tiledBox.x <= tiledBox.y ? spriteW : spriteH;

                s = spriteShort > 0.01f ? shortSide / spriteShort : 1f;

                sr.drawMode = SpriteDrawMode.Tiled;
                sr.tileMode = SpriteTileMode.Continuous;
                sr.size = new Vector2(tiledBox.x / s, tiledBox.y / s);
            }
            else
            {
                sr.drawMode = SpriteDrawMode.Simple;
            }

            art.localScale = new Vector3(s, s, 1f);
            art.localPosition = new Vector3(localPos.x, localPos.y, 0f);
            art.localRotation = Quaternion.identity;

            return true;
        }

        private static Sprite LoadSprite(string path, GameObject context, List<string> report)
        {
            Sprite s = Resources.Load<Sprite>(path);

            if (s == null)
            {
                report.Add($"❌ {Path(context)}：找不到图 \"{path}\"");
            }

            return s;
        }

        /// <summary>
        /// 把节点写进组件的私有 <c>[SerializeField]</c>。
        ///
        /// ⚠️ 必须按字段的声明类型转换并回读校验：字段有的是 <c>Transform</c>、
        /// 有的是 <c>GameObject</c>。往后者的字段塞 <c>Transform</c>，Unity
        /// **不抛异常、不报错**，直接把值丢掉 —— 表现是「门一直亮着、暗态盖不上去」。
        /// </summary>
        private static bool LinkField(Component target, string fieldName, Transform value,
                                      List<string> report, GameObject context)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);

            if (prop == null)
            {
                report.Add($"❌ {Path(context)}：{target.GetType().Name} 上没有字段「{fieldName}」");
                return false;
            }

            if (prop.propertyType != SerializedPropertyType.ObjectReference)
            {
                report.Add($"❌ {Path(context)}：字段「{fieldName}」不是对象引用");
                return false;
            }

            System.Reflection.FieldInfo fi = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);

            if (fi == null)
            {
                report.Add($"❌ {Path(context)}：反射不到 {target.GetType().Name}.{fieldName}");
                return false;
            }

            UnityEngine.Object assign = fi.FieldType == typeof(GameObject)
                ? (UnityEngine.Object)value.gameObject
                : (UnityEngine.Object)value;

            prop.objectReferenceValue = assign;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (new SerializedObject(target).FindProperty(fieldName).objectReferenceValue == null)
            {
                report.Add($"❌ {Path(context)}：{target.GetType().Name}.{fieldName}" +
                           $"（声明类型 {fi.FieldType.Name}）赋值被丢弃了，回读是 null");
                return false;
            }

            EditorUtility.SetDirty(target);
            return true;
        }

        private static T[] FindAll<T>() where T : Component
        {
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include);
        }

        private static string FormatReport(string scope, List<string> report, int changed)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"===== 补机关美术：{scope} =====");

            foreach (string line in report)
            {
                sb.AppendLine(line);
            }

            sb.AppendLine(changed > 0 ? $"合计：处理了 {changed} 个机关。" : "合计：这个场景里没有认得出的机关。");
            return sb.ToString();
        }

        private static string Path(GameObject go)
        {
            var sb = new StringBuilder(go.name);
            Transform t = go.transform.parent;

            while (t != null)
            {
                sb.Insert(0, t.name + "/");
                t = t.parent;
            }

            return "[" + sb + "]";
        }
    }
}
