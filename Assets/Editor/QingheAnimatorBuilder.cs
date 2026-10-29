using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CloudEyrie.EditorTools
{
    /// <summary>
    /// 把 AutoSprite 导入的 8 段青禾动画合成一个 AnimatorController。
    ///
    /// 为什么用代码建而不是手拖：这 8 段的路径是 AutoSprite 的导出约定
    /// （<c>Assets/AutoSprite/Imported/&lt;动作&gt;/AutoSpriteCharacter_AutoSpriteAnim.anim</c>），
    /// 重新导出一次就要重拖一遍；代码建可以一条菜单重新生成。
    /// </summary>
    public static class QingheAnimatorBuilder
    {
        private const string OutputPath = "Assets/Resources/anim/QingheAnimator.controller";
        private const string Root = "Assets/AutoSprite/Imported";

        /// <summary>状态名 → AutoSprite 目录名</summary>
        private static readonly (string state, string folder)[] Clips =
        {
            ("Idle", "青禾_idle_right"),
            ("Walk", "青禾_walk_right"),
            ("Run", "青禾_run_right"),
            ("Jump", "青禾_jump_right"),
            ("QuickStrike", "青禾_Sword_Quick_Strike_right"),
            ("TripleCombo", "青禾_Sword_Triple_Combo_right"),
            ("RisingCrescent", "青禾_Sword_Rising_Crescent_right"),
            ("CycloneSlash", "青禾_Sword_Cyclone_Slash_right"),
        };

        [MenuItem("CloudEyrie/美术工具/重建青禾 Animator")]
        public static void Build()
        {
            AssetDatabase.DeleteAsset(OutputPath);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(OutputPath);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState first = null;

            foreach ((string state, string folder) in Clips)
            {
                string path = $"{Root}/{folder}/AutoSpriteCharacter_AutoSpriteAnim.anim";
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

                if (clip == null)
                {
                    Debug.LogWarning($"[QingheAnimator] 读不到 {path}");
                    continue;
                }

                AnimatorState st = machine.AddState(state);
                st.motion = clip;

                if (first == null)
                {
                    first = st;
                    machine.defaultState = st;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[QingheAnimator] 已生成 {OutputPath}（{Clips.Length} 个状态）");
        }
    }
}
