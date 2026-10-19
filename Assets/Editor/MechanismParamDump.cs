using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CloudEyrie.EditorTools
{
    /// <summary>巡检用：把关键机关的运行参数打出来，和设计稿数值对照。</summary>
    public static class MechanismParamDump
    {
        [MenuItem("CloudEyrie/关卡工具/打印机关参数")]
        public static void Dump()
        {
            var sb = new System.Text.StringBuilder();
            string[] levels = { "Mu", "Huo", "Tu", "Jin", "Shui" };
            foreach (var lv in levels)
            {
                var sc = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    "Assets/Levels/" + lv + "/" + lv + ".unity",
                    UnityEditor.SceneManagement.OpenSceneMode.Additive);

                foreach (var root in sc.GetRootGameObjects())
                {
                    foreach (var b in root.GetComponentsInChildren<BounceFlower>(true))
                        sb.AppendLine(lv + " Bounce " + b.name + " " + Fields(b));

                    foreach (var w in root.GetComponentsInChildren<SwordWheel>(true))
                        sb.AppendLine(lv + " Wheel " + w.name + " " + Fields(w));

                    foreach (var h in root.GetComponentsInChildren<Hazard>(true))
                        sb.AppendLine(lv + " Hazard " + h.name + " " + Fields(h));
                }

                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(sc, true);
            }

            Debug.Log(sb.ToString());
        }

        private static string Fields(Object o)
        {
            var so = new SerializedObject(o);
            var sb = new System.Text.StringBuilder();
            var it = so.GetIterator();
            while (it.NextVisible(true))
            {
                if (it.propertyType == SerializedPropertyType.Float && !it.name.StartsWith("m_"))
                    sb.Append(it.name).Append('=').Append(it.floatValue.ToString("0.##")).Append(' ');
            }
            return sb.ToString();
        }
    }
}
