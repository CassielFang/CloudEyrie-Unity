using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        /// <summary>建成一个干净的父节点 + 若干子碰撞体（之前跑过的会被清掉，保证幂等）。</summary>
        private static int Rebuild(LayoutItem[] layout, List<string> report)
        {
            Transform groundRoot = EnsureRoot(GroundRootName);
            Transform platformRoot = EnsureRoot(PlatformRootName);

            ClearChildren(groundRoot);
            ClearChildren(platformRoot);

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

                // LayoutItem 的 y 是**顶面**（地面顶线 = -295），碰撞体从顶面往下长
                go.transform.position = new Vector3(item.CenterX, item.y - item.h * 0.5f, 0f);

                var box = go.AddComponent<BoxCollider2D>();
                box.size = new Vector2(item.w, item.h);

                // 归 WORLD 层：留在 Default 层的话 PhysicsGroups 会把游戏层与它断开，
                // 玩家会直接穿过 —— 而且不报错。
                go.layer = PhysicsGroups.World;

                built++;
            }

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
