using UnityEditor;
using UnityEngine;
using Spine.Unity;

namespace CloudEyrie.EditorTools
{
    /// <summary>
    /// 把青禾的立绘从 Spine 骨骼切成**帧序列**。
    ///
    /// <b>为什么做成菜单而不是手点</b>
    ///
    /// 切换要动的组件是固定的六样（去掉 3 个 + 加上 3 个），但 Unity 不允许
    /// `SkeletonAnimation`（要 `MeshFilter`）与 `SpriteRenderer` 共存 ——
    /// 手点很容易留下 `MeshFilter` 导致加不上 `SpriteRenderer`，报错还很难读。
    ///
    /// <b>前提</b>：`Assets/Resources/video_fx/青禾-*` 已经就位。
    /// 没放素材就切的话，`SpriteSequenceView` 加载 manifest 失败、角色停在最后一帧，
    /// **不报错也不崩** —— 所以这里先检查一遍目录再动手。
    /// </summary>
    public static class SwitchQingheToSequence
    {
        private static readonly string[] RequiredClips =
        {
            "青禾-待机", "青禾-行走", "青禾-奔跑", "青禾-起跳", "青禾-下落",
        };

        [MenuItem("CloudEyrie/角色工具/切换青禾为帧序列", false, 30)]
        public static void Switch()
        {
            QingheController qinghe = Object.FindAnyObjectByType<QingheController>();

            if (qinghe == null)
            {
                EditorUtility.DisplayDialog("切换失败", "当前场景里没有 QingheController。", "好");
                return;
            }

            Transform art = qinghe.transform.Find("Art");

            if (art == null)
            {
                EditorUtility.DisplayDialog("切换失败", "没找到 Qinghe 下的 Art 节点。", "好");
                return;
            }

            string missing = CheckClips();

            if (missing != null)
            {
                EditorUtility.DisplayDialog(
                    "素材还没就位",
                    "缺少这些序列帧目录：\n" + missing +
                    "\n\n先把打包产物拷进 Assets/Resources/video_fx/ 再切换。",
                    "好");
                return;
            }

            int removed = 0;

            // 顺序有讲究：先摘 SkeletonAnimation（它会拖 MeshRenderer），再摘网格
            removed += Remove<SkeletonAnimation>(art.gameObject);
            removed += Remove<SpineCharacterView>(art.gameObject);
            removed += Remove<ClothSway>(art.gameObject);
            removed += Remove<MeshRenderer>(art.gameObject);
            removed += Remove<MeshFilter>(art.gameObject);

            SpriteRenderer sr = art.GetComponent<SpriteRenderer>();

            if (sr == null)
            {
                sr = art.gameObject.AddComponent<SpriteRenderer>();
            }

            sr.sortingLayerName = "Default";
            sr.sortingOrder = 30;

            SequencePlayer player = art.GetComponent<SequencePlayer>();

            if (player == null)
            {
                player = art.gameObject.AddComponent<SequencePlayer>();
            }

            player.basePath = "video_fx";
            player.pixelsPerUnit = 1f;

            SpriteSequenceView view = art.GetComponent<SpriteSequenceView>();

            if (view == null)
            {
                view = art.gameObject.AddComponent<SpriteSequenceView>();
            }

            // 指向控制器，免得它运行时再找一遍
            SerializedObject so = new SerializedObject(view);
            SerializedProperty ownerProp = so.FindProperty("owner");

            if (ownerProp != null)
            {
                ownerProp.objectReferenceValue = qinghe;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(art.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                qinghe.gameObject.scene);
            Debug.Log($"[切换帧序列] 摘掉 {removed} 个组件，加上 SpriteRenderer + SequencePlayer + SpriteSequenceView。" +
                      "记得保存场景并进 Play 验收。");
        }

        private static string CheckClips()
        {
            var missing = new System.Text.StringBuilder();

            foreach (string clip in RequiredClips)
            {
                string dir = System.IO.Path.Combine(
                    Application.dataPath, "Resources", "video_fx", clip);

                if (!System.IO.Directory.Exists(dir))
                {
                    missing.AppendLine("  · " + clip);
                }
            }

            return missing.Length == 0 ? null : missing.ToString();
        }

        private static int Remove<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();

            if (c == null)
            {
                return 0;
            }

            Object.DestroyImmediate(c, true);
            return 1;
        }
    }
}
