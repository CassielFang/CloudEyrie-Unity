using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CloudEyrie.EditorTools
{
    /// <summary>
    /// 按约定给关卡机关补上表现层。
    ///
    /// <b>为什么需要它</b>
    ///
    /// 关卡模组化之后，机关是**手摆进场景**的，而机制脚本里**没有任何一个会在运行时自建美术**
    /// （<c>Assets/Scripts/Mechanism/*.cs</c> 全部无 <c>AddComponent&lt;SpriteRenderer&gt;</c>）。
    /// 于是「摆了碰撞体、忘了摆美术」成了一种**零报错**的摆放错误 ——
    /// 表现是玩家被看不见的火喷、踩看不见的岩浆掉灵炁。
    ///
    /// 2026-10-17 实测：全部 6 个关卡里，灵泉 / 传送门 / 形态屏障 / 火柱 / 岩浆 / 水流
    /// **一个渲染节点都没有**。而其中灵泉石台、传送门、屏障三样，
    /// **正式美术早就躺在 <c>Assets/Resources/场景小物件/</c> 里**，只是从没接线。
    ///
    /// <b>为什么是工具而不是手工改一遍</b>
    ///
    /// 路线图（<c>docs/plan/路线图.md</c>）写明关卡还要重做，每加一关就会重犯同一个错。
    /// 工具可以重复跑，手改不能。
    ///
    /// <b>规则表在下面集中声明</b>，改表现只需要改那一段。
    /// </summary>
    public static class MechanismArtWirer
    {
        // ============================================================
        // 规则表 —— 要换美术 / 调缩放，只改这一段
        // ============================================================

        // 灵泉石台。三个常量全部照抄旧 LevelDirector 的实测值（那套是从原图 alpha 离线量出来的，
        // 换素材必须重新量 —— 见 LevelDirector.cs 里 SpringContentBottom 的注释）。
        private const string SpringSpritePath = "场景小物件/灵泉石台（回血）";
        private const float SpringArtScale = 0.232f;
        private const float SpringContentBottomPx = 444f;

        // 灵界屏障（门）
        private const string BarrierSpritePath = "场景小物件/灵界屏障（门）";
        private const float BarrierArtScale = 0.68f;
        private const float BarrierContentBottomPx = 488f;

        // 传送门：亮 / 暗两张，接 LevelPortal.visualOn / visualOff
        private const string PortalOnSpritePath = "场景小物件/灵界机关（亮）（传送门）";
        private const string PortalOffSpritePath = "场景小物件/灵界机关（暗）（传送门）";

        // 火柱 / 岩浆 / 水流
        private const string FlameSpritePath = "fx_tex/fx_flame";
        private const string LavaSpritePath = "fx_tex/fx_lava";
        private const string WaterSpritePath = "fx_tex/fx_water";

        /// <summary>美术与地面的关系：内容底边压进地面多少世界单位（旧的 springGroundSink / barrierGroundSink）。</summary>
        private const float GroundSink = 30f;

        /// <summary>排序层名。用名字而不是 uniqueID —— 那个哈希有 4 层是负数，
        /// <c>CloudEyrieProjectSetup.cs</c> 里有专门警告，硬编码会写坏。</summary>
        private const string PropsLayer = "Props";
        private const string HazardLayer = "Hazard";

        /// <summary>机关美术节点的名字。惯例是「根节点 → Art 子节点」，
        /// 与 <c>Assets/Prefabs/Mechanisms/*.prefab</c> 一致，也正好命中
        /// <c>CloudEyrieSceneAudit</c> 的空 sprite 豁免名单。</summary>
        private const string ArtNodeName = "Art";

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
                EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
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

            // 逐关 OpenScene(Single) 会丢掉当前的场景组合，先给人机会存盘
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
                // 无论中途出什么事，都把人的场景组合还原回去
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

        /// <summary>处理当前打开的场景，返回改动的对象数。</summary>
        private static int WireScene(List<string> report)
        {
            int changed = 0;

            // 顺序有意义：一个节点上只会命中一条规则，但按「专有 → 通用」排能避免
            // 以后加规则时互相抢（例如将来给 Hazard 加子类）。
            changed += WireCheckpoints(report);
            changed += WirePortals(report);
            changed += WireBarriers(report);
            changed += WireGroupGates(report);
            changed += WireHazards(report);

            return changed;
        }

        /// <summary>
        /// 灵泉石台。纯装饰 —— <c>Checkpoint</c> 没有美术字段，Art 节点只是挂上去给人看。
        ///
        /// 摆放沿用旧算式：内容底边压进地面 <see cref="GroundSink"/>，
        /// 精灵中心 = 内容底边 + <see cref="SpringContentBottomPx"/> × 缩放。
        /// 节点自身的 y 就当地面线用（灵泉就摆在地面上），所以不写死 −295。
        /// </summary>
        private static int WireCheckpoints(List<string> report)
        {
            int changed = 0;

            foreach (Checkpoint cp in FindAll<Checkpoint>())
            {
                Transform art = EnsureArtNode(cp.transform, ArtNodeName);

                if (art == null)
                {
                    report.Add($"❌ {Path(cp.gameObject)}：建不出 Art 节点");
                    continue;
                }

                float centerY = SpringContentBottomPx * SpringArtScale - GroundSink;

                // ⚠️ Checkpoint **没有美术字段** —— 灵泉的 Art 是纯装饰，
                //    挂上去只为了「看得见」。别顺手给它 LinkField，它真的没有那个字段。
                bool ok = ApplySprite(art, SpringSpritePath, PropsLayer, SpringArtScale,
                                      new Vector2(0f, centerY), Vector2.zero);

                report.Add(ok
                    ? $"✅ {Path(cp.gameObject)} → 灵泉石台（缩放 {SpringArtScale}，y {centerY:0.##}）"
                    : $"⚠️ {Path(cp.gameObject)}：灵泉石台没接上（见上）");

                changed++;
            }

            return changed;
        }

        /// <summary>
        /// 传送门。亮 / 暗两态 —— <c>LevelPortal</c> 的注释早就写明「美术已给亮/暗两张图」，
        /// 字段 <c>visualOn</c> / <c>visualOff</c> 也留好了，只是一直是 0。
        ///
        /// 缩放按「美术高度对齐碰撞盒高度」定，锚点是内容底边贴碰撞盒底边 ——
        /// 这是 <c>Assets/Prefabs/Mechanisms/*.prefab</c> 里既有 Art 节点的通用规律。
        /// </summary>
        private static int WirePortals(List<string> report)
        {
            int changed = 0;

            foreach (LevelPortal portal in FindAll<LevelPortal>())
            {
                // ⚠️ 必须取 BoxCollider2D —— size / offset 是它的成员，
                //    Collider2D 基类上没有（写成基类会 CS1061）
                BoxCollider2D box = portal.GetComponent<BoxCollider2D>();

                if (box == null)
                {
                    report.Add($"❌ {Path(portal.gameObject)}：没有 BoxCollider2D，量不出该摆多大");
                    continue;
                }

                Sprite probe = LoadSprite(PortalOnSpritePath, portal.gameObject, report);

                if (probe == null)
                {
                    continue;
                }

                // 精灵在原图尺寸下的世界高度（PPU=1，所以就是像素数）
                float spriteH = probe.rect.height;

                // 用 box.size（局部）而不是 box.bounds.size（世界 AABB）——
                // offset 和 Art 的 localPosition 都是局部量，混用会在根节点有缩放时算错
                float scale = spriteH > 0.01f ? box.size.y / spriteH : 1f;

                // 碰撞盒底边（局部）→ 精灵中心要抬半个精灵高
                float bottomY = box.offset.y - box.size.y * 0.5f;
                var localPos = new Vector2(box.offset.x, bottomY + spriteH * scale * 0.5f);

                Transform artOn = EnsureArtNode(portal.transform, "ArtOn");
                Transform artOff = EnsureArtNode(portal.transform, "ArtOff");

                bool ok = artOn != null && artOff != null;

                if (ok)
                {
                    ok &= ApplySprite(artOn, PortalOnSpritePath, PropsLayer, scale, localPos, Vector2.zero);
                    ok &= ApplySprite(artOff, PortalOffSpritePath, PropsLayer, scale, localPos, Vector2.zero);
                    ok &= LinkField(portal, "visualOn", artOn, report, portal.gameObject);
                    ok &= LinkField(portal, "visualOff", artOff, report, portal.gameObject);
                }

                report.Add(ok
                    ? $"✅ {Path(portal.gameObject)} → 传送门 亮/暗（缩放 {scale:0.###}）"
                    : $"⚠️ {Path(portal.gameObject)}：传送门美术没接全（见上）");

                changed++;
            }

            return changed;
        }

        /// <summary>
        /// 形态屏障。旧的 LevelDirector 把这张门图沿 X 切成左右两半、靠 sortingOrder
        /// 分前后（一半在玩家身后、一半在身前）—— 那要在运行时 <c>Sprite.Create</c> 切图，
        /// 编辑器工具做不到。这里先摆整张、放 <c>Props</c> 层（玩家身后），
        /// 效果是「站在门前」而不是「穿过门框」。
        /// </summary>
        private static int WireBarriers(List<string> report)
        {
            int changed = 0;

            foreach (ElementBarrier barrier in FindAll<ElementBarrier>())
            {
                Transform art = EnsureArtNode(barrier.transform, ArtNodeName);

                if (art == null)
                {
                    report.Add($"❌ {Path(barrier.gameObject)}：建不出 Art 节点");
                    continue;
                }

                float centerY = BarrierContentBottomPx * BarrierArtScale - GroundSink;

                bool ok = ApplySprite(art, BarrierSpritePath, PropsLayer, BarrierArtScale,
                                      new Vector2(0f, centerY), Vector2.zero);

                // ElementBarrier.art 是它「解锁后压暗」的表现来源，必须接上
                ok &= LinkField(barrier, "art", art, report, barrier.gameObject);

                report.Add(ok
                    ? $"✅ {Path(barrier.gameObject)} → 灵界屏障（缩放 {BarrierArtScale}，y {centerY:0.##}）"
                    : $"⚠️ {Path(barrier.gameObject)}：屏障美术没接上（见上）");

                changed++;
            }

            return changed;
        }

        /// <summary>
        /// 条件门（<c>GroupGate</c>）：集齐条件才开的那种门。火境的 <c>FireWall</c>、
        /// 金境的 <c>SwordGate</c> 都是它 —— 与 <see cref="ElementBarrier"/> 是同一类东西
        /// （一道挡路的门、解锁后压暗淡出），所以用同一张图、同一套摆放。
        ///
        /// ⚠️ <b>别信场景 YAML 里的 <c>m_EditorClassIdentifier</c></b>：火境那个写着
        /// <c>CloudEyrie.FireWall</c>，但那个类**已经不存在了** —— 它是 <c>GroupGate</c> 改名前的残留字符串。
        /// Unity 实际按 GUID（<c>m_Script</c>）解析，所以组件是 <c>GroupGate</c>。
        /// 照着字符串去写规则，这两个门就会被整条漏掉（本轮第一遍就是这么漏的）。
        /// </summary>
        private static int WireGroupGates(List<string> report)
        {
            int changed = 0;

            foreach (GroupGate gate in FindAll<GroupGate>())
            {
                Transform art = EnsureArtNode(gate.transform, ArtNodeName);

                if (art == null)
                {
                    report.Add($"❌ {Path(gate.gameObject)}：建不出 Art 节点");
                    continue;
                }

                float centerY = BarrierContentBottomPx * BarrierArtScale - GroundSink;

                bool ok = ApplySprite(art, BarrierSpritePath, PropsLayer, BarrierArtScale,
                                      new Vector2(0f, centerY), Vector2.zero);

                // GroupGate.art 是它解锁时淡出的来源
                ok &= LinkField(gate, "art", art, report, gate.gameObject);

                report.Add(ok
                    ? $"✅ {Path(gate.gameObject)} → 灵界屏障/条件门（缩放 {BarrierArtScale}，y {centerY:0.##}）"
                    : $"⚠️ {Path(gate.gameObject)}：条件门美术没接上（见上）");

                changed++;
            }

            return changed;
        }

        /// <summary>
        /// 危险地形。这里**故意不认识的就不补** —— 坑和坠落区本来就该隐形。
        ///
        /// 判据优先用**配置**（比名字可靠）：
        /// <list type="bullet">
        ///   <item><c>period &gt; 0</c> = 周期性喷发 = 火柱（<c>FirePillar_*</c> 与 <c>Recap_Huo</c> 都是 <c>period: 4</c>，
        ///     一条规则全覆盖，不必认名字）</item>
        ///   <item><c>period == 0 且 spiritDamage &gt; 0</c> = 常驻伤害 = 岩浆</item>
        ///   <item>其余一律跳过（<c>Pit_*</c> / <c>FallZone_*</c> 是 <c>spiritDamage: 0</c>，本来就是隐形的坑）</item>
        /// </list>
        ///
        /// ⚠️ 只有 <c>Water_Drift</c> 例外：它的配置（<c>period: 0</c>、<c>spiritDamage: 0</c>、有 respawnPoint）
        /// 与 <c>Pit_*</c> **一模一样**，配置上无从区分，只能靠名字。
        /// 所以**每一个被跳过的 Hazard 都会打印名字和原因** —— 名字依赖是明面上的，不是暗的。
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

                string spritePath = null;
                string why;

                if (period > 0.0001f)
                {
                    spritePath = FlameSpritePath;
                    why = $"周期性（period={period}）→ 火柱";
                }
                else if (damage > 0.0001f)
                {
                    spritePath = LavaSpritePath;
                    why = $"常驻伤害（spiritDamage={damage}）→ 岩浆";
                }
                else if (hazard.gameObject.name.Contains("Water_Drift"))
                {
                    // 配置与 Pit 无异，只能认名字。这条是**明知故犯**的例外，所以单独提示
                    spritePath = WaterSpritePath;
                    why = "名字含 Water_Drift → 水流（配置与 Pit 相同，只能靠名字）";
                }
                else
                {
                    report.Add($"・跳过 {Path(hazard.gameObject)}：" +
                               $"period=0 且 spiritDamage=0 → 判为坑/坠落区，本来就该隐形");
                    continue;
                }

                // 同上：size / offset 在 BoxCollider2D 上
                BoxCollider2D box = hazard.GetComponent<BoxCollider2D>();

                if (box == null)
                {
                    report.Add($"❌ {Path(hazard.gameObject)}：没有 BoxCollider2D，量不出该铺多大");
                    continue;
                }

                Transform art = EnsureArtNode(hazard.transform, ArtNodeName);

                if (art == null)
                {
                    report.Add($"❌ {Path(hazard.gameObject)}：建不出 Art 节点");
                    continue;
                }

                // 平铺铺满碰撞盒，节点对齐盒心（瓦片缩放由 ApplySprite 按盒子短边算）
                bool ok = ApplySprite(art, spritePath, HazardLayer, 1f, box.offset, box.size);

                // Hazard.visual 是预警压暗（dimOnWarn）的来源，接上它火柱才会在喷发前变暗
                ok &= LinkField(hazard, "visual", art, report, hazard.gameObject);

                report.Add(ok
                    ? $"✅ {Path(hazard.gameObject)} → {why}（平铺 {box.size.x:0}×{box.size.y:0}）"
                    : $"⚠️ {Path(hazard.gameObject)}：{why}，但美术没接上（见上）");

                changed++;
            }

            return changed;
        }

        // ============================================================
        // 工具函数
        // ============================================================

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

            // 既有 prefab 里的 Art 节点都在 Default 层（它们只有 SpriteRenderer、不参与物理）
            found.gameObject.layer = 0;

            return found;
        }

        /// <summary>设 sprite / 排序层 / 缩放 / 局部位置。返回是否成功（图加载不到就是失败）。</summary>
        /// <param name="tiledBox">
        /// 非零 = 用平铺模式铺满这个尺寸（危险地形）。零 = 普通 Simple 模式。
        ///
        /// <b>瓦片的缩放是算出来的，不是手填的。</b>
        /// 规矩是「<b>一个瓦片正好铺满盒子的短边</b>」，长边方向重复：
        /// 竖着的火柱用盒子宽度定瓦片宽，横着的岩浆带用盒子高度定瓦片高。
        /// 手填瓦片尺寸会在瓦片尺寸除不尽盒子时，在长边方向留下半块瓦片的接缝 ——
        /// 那是可见的硬边，正是当初 `fx_flame` 看起来像块面板的原因之一。
        /// </param>
        private static bool ApplySprite(Transform art, string spritePath, string layerName,
                                        float scale, Vector2 localPos, Vector2 tiledBox)
        {
            Sprite sprite = Resources.Load<Sprite>(spritePath);

            if (sprite == null)
            {
                // 这条必须吵：Resources.Load 拿不到图是**不报错**的，只表现为"那张图不见了"
                Debug.LogError($"[补美术] Resources.Load<Sprite>(\"{spritePath}\") 返回 null。" +
                               "检查图是否在某个 Resources 目录下（CLAUDE.md：放到别处会按 PPU=100 导入、静默返回 null）。");
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
                // PPU=1，所以原图像素数就是世界单位
                float spriteW = sprite.rect.width;
                float spriteH = sprite.rect.height;

                // 「一个瓦片铺满盒子的短边」：横着的带子按高度定、竖着的柱子按宽度定
                float shortSide = Mathf.Min(tiledBox.x, tiledBox.y);
                float spriteShort = tiledBox.x <= tiledBox.y ? spriteW : spriteH;

                s = spriteShort > 0.01f ? shortSide / spriteShort : 1f;

                sr.drawMode = SpriteDrawMode.Tiled;
                sr.tileMode = SpriteTileMode.Continuous;

                // size 是「局部」尺寸，渲染尺寸 = size × localScale，所以要把缩放除回去
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
        /// 走 <c>SerializedObject</c> 是因为这些字段是 private —— 直接赋值编不过。
        ///
        /// ⚠️ <b>必须按字段的声明类型转换，并且回读校验。</b>
        ///
        /// 字段有的是 <c>Transform</c>（<c>Hazard.visual</c> / <c>ElementBarrier.art</c>），
        /// 有的是 <c>GameObject</c>（<c>LevelPortal.visualOn/Off</c>）。
        /// 往后者的字段里塞一个 <c>Transform</c>，Unity **不抛异常、不报错**，
        /// 直接把值丢掉 —— 字段留 null，表现是「门一直亮着、暗态盖不上去」。
        /// 这个坑在 2026-10-17 真的踩了一次，所以这里加了回读。
        /// </summary>
        private static bool LinkField(Component target, string fieldName, Transform value,
                                      List<string> report, GameObject context)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);

            if (prop == null)
            {
                report.Add($"❌ {Path(context)}：{target.GetType().Name} 上没有字段「{fieldName}」" +
                           "（脚本改过名？这条规则要跟着更新）");
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

            // 回读校验 —— 静默失败在这里现形
            var verify = new SerializedObject(target);

            if (verify.FindProperty(fieldName).objectReferenceValue == null)
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
            // Unity 6.6：带 SortMode 的重载已废弃（CS0618），新重载不收排序参数
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

            sb.AppendLine(changed > 0
                ? $"合计：改了 {changed} 处。"
                : "合计：这个场景里没有认得出的机关。");

            return sb.ToString();
        }

        /// <summary>拼层级路径，让日志能直接定位到对象。</summary>
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
