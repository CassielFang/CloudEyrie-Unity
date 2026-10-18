using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace CloudEyrie.EditorTools
{
    /// <summary>设计稿里一个水平元素：x/y 是**左上角**，世界单位。</summary>
    [System.Serializable]
    public struct LayoutItem
    {
        public string kind;
        public float x;
        public float y;
        public float w;
        public float h;

        public LayoutItem(string kind, float x, float y, float w, float h)
        {
            this.kind = kind;
            this.x = x;
            this.y = y;
            this.w = w;
            this.h = h;
        }

        public float CenterX { get { return x + w * 0.5f; } }
        public float CenterY { get { return y + h * 0.5f; } }
    }

    /// <summary>
    /// 按设计稿（`docs/design/五行关卡设计v3.html` 的 SVG 侧视图）重建关卡的地面与平台碰撞体。
    ///
    /// <b>为什么不能继续用手摆的那一整块地面</b>
    ///
    /// 设计稿的地面是**一段段带缺口的**（缺口 = 坑），这是平台跳跃的基本结构；
    /// 而现场景是一整块连续地面 + 几个坠落区。结构不对，后面的机关位置、
    /// 跳跃节奏全都无从谈起。
    ///
    /// 布局数据由 `tools/gen_level_layout.py` 从设计稿生成，**不要手改**
    /// （`Assets/Editor/LevelLayoutData.cs`）。
    ///
    /// ⚠️ 只重建**碰撞体**，素材仍由背景长卷承担 —— 瓦片渲染已经在上一轮关掉了。
    /// </summary>
    public static class LevelLayoutBuilder
    {
        private const string GroundRootName = "GroundSegments";
        private const string PlatformRootName = "PlatformColliders";

        /// <summary>这些 kind 要生成实心碰撞体。</summary>
        private static readonly HashSet<string> SolidKinds = new HashSet<string> { "Ground", "Platform" };

        [MenuItem("CloudEyrie/关卡工具/按设计稿重建地面碰撞（当前场景）")]
        public static void RebuildCurrentScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[重建] 正在播放模式里，不能改场景。先退出 Play。");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            LayoutItem[] layout = LayoutOf(scene.name);

            if (layout == null)
            {
                Debug.LogError($"[重建] 没有「{scene.name}」的布局数据。" +
                               "检查 LevelLayoutData.cs 是否覆盖了这一关。");
                return;
            }

            var report = new List<string>();
            int built = Rebuild(layout, report);

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"===== 重建地面碰撞：{scene.name} =====\n" +
                      string.Join("\n", report) + $"\n合计：{built} 个碰撞体。");
        }

        /// <summary>
        /// 五境一起重建。设计稿数据（`LevelLayoutData.cs`）一改就要跑 ——
        /// 不然旧场景里还留着上一版生成的碰撞体，和新生成的叠在一起。
        /// </summary>
        [MenuItem("CloudEyrie/关卡工具/按设计稿重建地面碰撞（全部关卡）")]
        public static void RebuildAllLevelScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[重建] 正在播放模式里，不能改场景。先退出 Play。");
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
                    Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    LayoutItem[] layout = LayoutOf(scene.name);

                    if (layout == null)
                    {
                        all.Add($"[{scene.name}] 没有布局数据，跳过");
                        continue;
                    }

                    var report = new List<string>();
                    int built = Rebuild(layout, report);

                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    all.Add($"[{scene.name}] {built} 个碰撞体；" + string.Join("；", report));
                }
            }
            finally
            {
                if (restore != null && restore.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(restore);
                }
            }

            Debug.Log("===== 重建地面碰撞（全部关卡）=====\n" + string.Join("\n", all));
        }

        /// <summary>建成一个干净的父节点 + 若干子碰撞体（之前跑过的会被清掉，保证幂等）。</summary>
        private static int Rebuild(LayoutItem[] layout, List<string> report)
        {
            Transform groundRoot = EnsureRoot(GroundRootName);
            Transform platformRoot = EnsureRoot(PlatformRootName);

            ClearChildren(groundRoot);
            ClearChildren(platformRoot);

            // 地面贴图与碰撞体必须**同一份格子**：贴图走 Tilemap（64 一格），
            // 碰撞体是按设计稿的任意宽度生成的。两者各自取整就会出现
            // 「看得到地、踩下去是空的」或者「踩着空气走」。
            // 这里让**地面碰撞体也吸附到格子上**，再由同一组格子去画贴图。
            Tilemap tilemap = FindTilemap();
            float cell = tilemap != null ? tilemap.cellSize.x : 64f;
            float tilemapX = tilemap != null ? tilemap.transform.position.x : 0f;

            var groundBands = new List<GroundBand>();
            int built = 0;

            foreach (LayoutItem item in layout)
            {
                if (!SolidKinds.Contains(item.kind))
                {
                    continue;
                }

                Transform parent = item.kind == "Ground" ? groundRoot : platformRoot;
                var go = new GameObject(item.kind);
                go.transform.SetParent(parent, false);

                float left = item.x;
                float width = item.w;

                if (item.kind == "Ground" && tilemap != null)
                {
                    int c0 = Mathf.RoundToInt((item.x - tilemapX) / cell);
                    int c1 = Mathf.RoundToInt((item.x + item.w - tilemapX) / cell);

                    if (c1 <= c0)
                    {
                        c1 = c0 + 1;
                    }

                    left = tilemapX + c0 * cell;
                    width = (c1 - c0) * cell;

                    // 顶行：格子 r 的顶边 = tilemap.y + (r + 1) × cell
                    int topRow = Mathf.RoundToInt((item.y - tilemap.transform.position.y) / cell) - 1;

                    // ⚠️ 只画**最上面一行**（64 高）作地表。原来画满整段高度（4 行），
                    // 相机一下潜就露出四面"砖墙"的截面，非常粗糙。
                    // 往下的部分交给地下的渐变土幕，坑壁只留一层地表"草皮"。
                    const int rows = 1;
                    groundBands.Add(new GroundBand(c0, c1, topRow, rows));
                }

                // LayoutItem 的 y 是**顶面**（地面顶线 = -295），碰撞体从顶面往下长
                go.transform.position = new Vector3(left + width * 0.5f, item.y - item.h * 0.5f, 0f);

                var box = go.AddComponent<BoxCollider2D>();
                box.size = new Vector2(width, item.h);

                // 归 WORLD 层：留在 Default 层的话 PhysicsGroups 会把游戏层与它断开，
                // 玩家会直接穿过 —— 而且不报错。
                go.layer = PhysicsGroups.World;

                built++;
            }

            PaintGround(tilemap, groundBands, report);
            HideLegacyGroundArt(report);
            EnsureUndergroundBackdrop(layout, report);

            // 旧场景里那块**整条地面**（名字就叫 Ground）会把缺口堵死 —— 关掉它的碰撞体。
            // 只关碰撞体不删节点：万一还要回退，形状数据还在。
            // ⚠️ 不能 GameObject.Find("Ground")：新生成的 7 段也叫 Ground，
            // 会随机命中其中一段。必须找**根节点**上那个。
            GameObject oldGround = null;

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == "Ground" && root.transform.parent == null)
                {
                    oldGround = root;
                    break;
                }
            }

            if (oldGround != null)
            {
                var oldBox = oldGround.GetComponent<BoxCollider2D>();

                if (oldBox != null && oldBox.enabled)
                {
                    oldBox.enabled = false;
                    EditorUtility.SetDirty(oldBox);
                    report.Add("已关闭旧的整体地面碰撞体 [Ground]（缺口改由设计稿的分段地面表达）");
                }
            }

            report.Add($"地面段 {CountKind(layout, "Ground")} 个、平台 {CountKind(layout, "Platform")} 个");

            return built;
        }

        private static int CountKind(LayoutItem[] layout, string kind)
        {
            int n = 0;

            foreach (LayoutItem item in layout)
            {
                if (item.kind == kind) n++;
            }

            return n;
        }

        /// <summary>一段地面在 Tilemap 上占的格子：列 [c0, c1)，从 topRow 往下 rows 行。</summary>
        private struct GroundBand
        {
            public readonly int c0;
            public readonly int c1;
            public readonly int topRow;
            public readonly int rows;

            public GroundBand(int c0, int c1, int topRow, int rows)
            {
                this.c0 = c0;
                this.c1 = c1;
                this.topRow = topRow;
                this.rows = rows;
            }
        }

        /// <summary>
        /// 把地面画进 Tilemap。
        ///
        /// <b>为什么地面贴图要用 Tilemap 而不是每段一张拉伸的精灵</b>：
        /// 地面块是 64×64 的瓦片，横拉会把纹理拉糊。Tilemap 的格子也是 64，
        /// 刚好和碰撞体的吸附单位一致（见 <see cref="Rebuild"/>）。
        /// </summary>
        private static void PaintGround(Tilemap tilemap, List<GroundBand> bands, List<string> report)
        {
            if (tilemap == null)
            {
                report.Add("⚠️ 没找到 Terrain Tilemap —— 地面有碰撞但**没有贴图**");
                return;
            }

            string scene = SceneManager.GetActiveScene().name;
            TileBase tile = LoadTile(scene, "ground");

            if (tile == null)
            {
                report.Add($"⚠️ 找不到 {scene} 的地面 Tile 资产，地面贴图没画");
                return;
            }

            tilemap.ClearAllTiles();

            int cells = 0;

            foreach (GroundBand band in bands)
            {
                for (int c = band.c0; c < band.c1; c++)
                {
                    for (int r = 0; r < band.rows; r++)
                    {
                        tilemap.SetTile(new Vector3Int(c, band.topRow - r, 0), tile);
                        cells++;
                    }
                }
            }

            TilemapRenderer renderer = tilemap.GetComponent<TilemapRenderer>();

            if (renderer != null)
            {
                renderer.enabled = true;
                renderer.sortingLayerName = "Ground";
                renderer.sortingOrder = 0;
                EditorUtility.SetDirty(renderer);
            }

            // 物理交给 GroundSegments 的 BoxCollider2D。旧 Tilemap 的碰撞体如果还开着，
            // 它会按**上一版**的方块继续挡人 —— 玩家踩在没有贴图、也不该有地面的地方。
            var tileCol = tilemap.GetComponent<TilemapCollider2D>();
            var composite = tilemap.GetComponent<CompositeCollider2D>();

            if (tileCol != null && tileCol.enabled)
            {
                tileCol.enabled = false;
                EditorUtility.SetDirty(tileCol);
            }

            if (composite != null && composite.enabled)
            {
                composite.enabled = false;
                EditorUtility.SetDirty(composite);
            }

            report.Add($"地面贴图 {cells} 格（Tilemap 碰撞体已关，碰撞由 GroundSegments 负责）");
        }

        /// <summary>
        /// 关掉旧的灰色地面占位图。
        ///
        /// 上一轮判断「背景长卷自带地面」后关掉了 Tilemap 渲染，但长卷其实是一张
        /// **远景山水**（底部是水/雾），没有玩法层的实心地面；于是场上只剩
        /// <c>GroundArt</c> 这张 1400 宽的石台占位图，其余地方角色是踩在空气上的。
        /// 现在地面贴图由 Tilemap 承担，这张占位图必须让位。
        /// </summary>
        private static void HideLegacyGroundArt(List<string> report)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name != "GroundArt")
                {
                    continue;
                }

                SpriteRenderer sr = root.GetComponent<SpriteRenderer>();

                if (sr != null && sr.enabled)
                {
                    sr.enabled = false;
                    EditorUtility.SetDirty(sr);
                    report.Add("已关闭旧的地面占位图 [GroundArt]");
                }

                return;
            }
        }

        private const string BackdropName = "GroundBackdrop";

        /// <summary>
        /// 地面以下的"土"。
        ///
        /// 相机纵向跟随之后，玩家掉进坑里会看到地面块的下沿和**背景长卷**——
        /// 也就是"天上/水面跑到了地下"，非常出戏。
        /// 这里在地面顶线以下铺一层深色土：坑里看到的是暗色土壁，不是背景。
        /// 岩浆/坠落区在 Hazard 排序层，会盖在它前面，照常可见。
        /// </summary>
        private static void EnsureUndergroundBackdrop(LayoutItem[] layout, List<string> report)
        {
            // 渐变图（顶部透明 → 下部不透明）比纯色块自然得多：
            // 坑口与背景之间是渐变过渡，不会出现一条生硬的"地下线"。
            Sprite sprite = Resources.Load<Sprite>("common/backdrop_gradient");

            if (sprite == null)
            {
                sprite = Resources.Load<Sprite>("common/backdrop");
            }

            if (sprite == null)
            {
                report.Add("⚠️ 找不到 common/backdrop_gradient，地下没有土（相机下潜会看到背景）");
                return;
            }

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float topY = -295f;

            foreach (LayoutItem item in layout)
            {
                if (item.kind != "Ground")
                {
                    continue;
                }

                minX = Mathf.Min(minX, item.x);
                maxX = Mathf.Max(maxX, item.x + item.w);
                topY = item.y;
            }

            if (maxX <= minX)
            {
                return;
            }

            GameObject root = null;

            foreach (GameObject candidate in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (candidate.name == BackdropName)
                {
                    root = candidate;
                    break;
                }
            }

            if (root == null)
            {
                root = new GameObject(BackdropName);
            }

            var sr = root.GetComponent<SpriteRenderer>();

            if (sr == null)
            {
                sr = root.AddComponent<SpriteRenderer>();
            }

            const float depth = 1400f;

            sr.sprite = sprite;
            sr.color = BackdropColor(SceneManager.GetActiveScene().name);
            sr.sortingLayerName = "Default";
            sr.sortingOrder = -150;      // 背景(-200) 之后、地面贴图(Ground 0) 之前
            sr.drawMode = SpriteDrawMode.Simple;

            // 精灵在 PPU=1 下按自身尺寸放大：宽贴满关卡，高铺到 depth
            float spriteW = Mathf.Max(0.001f, sprite.bounds.size.x);
            float spriteH = Mathf.Max(0.001f, sprite.bounds.size.y);

            root.transform.position = new Vector3((minX + maxX) * 0.5f, topY - depth * 0.5f, 0f);
            root.transform.localScale = new Vector3((maxX - minX) / spriteW, depth / spriteH, 1f);

            EditorUtility.SetDirty(sr);
            report.Add($"地下土幕：x {minX:0}..{maxX:0}，深 {depth:0}");
        }

        private static Color BackdropColor(string scene)
        {
            switch (scene)
            {
                case "Huo": return new Color(0.30f, 0.13f, 0.09f);
                case "Tu": return new Color(0.27f, 0.20f, 0.13f);
                case "Jin": return new Color(0.17f, 0.19f, 0.24f);
                case "Shui": return new Color(0.11f, 0.18f, 0.24f);
                default: return new Color(0.13f, 0.20f, 0.16f);   // Mu
            }
        }

        private static Tilemap FindTilemap()
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Tilemap found = root.GetComponentInChildren<Tilemap>(true);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static TileBase LoadTile(string scene, string kind)
        {
            string lower = scene.ToLowerInvariant();
            string path = $"Assets/Levels/{scene}/Tiles/tile_{lower}_{kind}.asset";
            return AssetDatabase.LoadAssetAtPath<TileBase>(path);
        }

        private static Transform EnsureRoot(string name)
        {
            GameObject existing = GameObject.Find(name);

            if (existing != null)
            {
                return existing.transform;
            }

            var go = new GameObject(name);
            return go.transform;
        }

        private static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(root.GetChild(i).gameObject);
            }
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
    }
}
