using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CloudEyrie.EditorTools
{
    /// <summary>
    /// 量出 8 段 AutoSprite 动画的**画面内容**尺寸，写成运行时能读的 json。
    ///
    /// 为什么必须量：AutoSprite 的帧画布大小不一（移动 640²、剑招 1024²），
    /// 而导入时每张都按「画布 = 1 世界单位」切 —— 直接用就会：
    /// <list type="bullet">
    ///   <item>不同动作的角色大小差一截；</item>
    ///   <item>画布留白被算进身高，脚离地；</item>
    ///   <item>每帧留白不一样时，播放时会上下抖。</item>
    /// </list>
    /// 所以按 **alpha 包围盒** 量出「内容高 / 内容底距画布底」，
    /// 运行时用同一套比例把内容摆到碰撞盒上，8 段就一致了。
    /// </summary>
    public static class QingheAnimMetrics
    {
        private const string OutPath = "Assets/Resources/anim/qinghe_metrics.json";

        private static readonly (string state, string prefab)[] Sources =
        {
            ("Idle", "Assets/Resources/anim/qinghe_idle.prefab"),
            ("Walk", "Assets/Resources/anim/qinghe_walk.prefab"),
            ("Run", "Assets/Resources/anim/qinghe_run.prefab"),
            ("Jump", "Assets/Resources/anim/qinghe_jump.prefab"),
            ("QuickStrike", "Assets/Resources/anim/qinghe_quickstrike.prefab"),
            ("TripleCombo", "Assets/Resources/anim/qinghe_triplecombo.prefab"),
            ("RisingCrescent", "Assets/Resources/anim/qinghe_risingcrescent.prefab"),
            ("CycloneSlash", "Assets/Resources/anim/qinghe_cycloneslash.prefab"),
        };

        [MenuItem("CloudEyrie/美术工具/量青禾动画尺寸")]
        public static void Measure()
        {
            var json = new StringBuilder();
            json.Append("{\n");

            for (int s = 0; s < Sources.Length; s++)
            {
                (string state, string prefabPath) = Sources[s];
                Stats stats = MeasureState(prefabPath);

                json.Append("  \"").Append(state).Append("\": {");
                json.Append("\"framePx\": ").Append(stats.FramePx).Append(", ");
                json.Append("\"contentH\": ").Append(stats.ContentH.ToString("0.0", CultureInfo.InvariantCulture)).Append(", ");
                json.Append("\"bottomPx\": ").Append(stats.BottomAvg.ToString("0.0", CultureInfo.InvariantCulture)).Append(", ");
                json.Append("\"bottomJitter\": ").Append(stats.BottomJitter.ToString("0.0", CultureInfo.InvariantCulture)).Append(", ");
                json.Append("\"centerX\": ").Append(stats.CenterX.ToString("0.0", CultureInfo.InvariantCulture)).Append(", ");
                json.Append("\"frames\": ").Append(stats.Frames);
                json.Append("}");

                if (s < Sources.Length - 1)
                {
                    json.Append(',');
                }

                json.Append('\n');
            }

            json.Append("}\n");

            File.WriteAllText(OutPath, json.ToString());
            AssetDatabase.ImportAsset(OutPath);
            Debug.Log($"[QingheAnimMetrics] 已写入 {OutPath}\n{json}");
        }

        private struct Stats
        {
            public int FramePx;
            public float ContentH;
            public float BottomAvg;
            public float BottomJitter;
            public float CenterX;
            public int Frames;
        }

        private static Stats MeasureState(string prefabPath)
        {
            var result = new Stats();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
            {
                return result;
            }

            Animator animator = prefab.GetComponentInChildren<Animator>(true);

            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return result;
            }

            AnimationClip clip = animator.runtimeAnimatorController.animationClips[0];
            EditorCurveBinding[] binds = AnimationUtility.GetObjectReferenceCurveBindings(clip);

            if (binds.Length == 0)
            {
                return result;
            }

            ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, binds[0]);

            if (keys.Length == 0)
            {
                return result;
            }

            var first = keys[0].value as Sprite;
            string pngPath = AssetDatabase.GetAssetPath(first.texture);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(pngPath));
            Color32[] px = tex.GetPixels32();

            var bottoms = new List<float>();
            var heights = new List<float>();
            float cxSum = 0f;
            int counted = 0;

            // 每段都抽全部帧（64 帧 × 640² 逐像素也就 2600 万次，一次性工具，慢一点没关系）
            foreach (ObjectReferenceKeyframe key in keys)
            {
                var sp = key.value as Sprite;

                if (sp == null)
                {
                    continue;
                }

                Rect r = sp.rect;
                int x0 = Mathf.RoundToInt(r.x);
                int y0 = Mathf.RoundToInt(r.y);
                int w = Mathf.RoundToInt(r.width);
                int h = Mathf.RoundToInt(r.height);

                int bottom = -1;
                int top = -1;
                long sumX = 0;
                int count = 0;

                for (int y = 0; y < h; y++)
                {
                    int row = (y0 + y) * tex.width;

                    for (int x = 0; x < w; x++)
                    {
                        if (px[row + x0 + x].a < 24)
                        {
                            continue;
                        }

                        if (bottom < 0)
                        {
                            bottom = y;
                        }

                        top = y;
                        sumX += x;
                        count++;
                    }
                }

                if (count == 0)
                {
                    continue;
                }

                bottoms.Add(bottom);
                heights.Add(top - bottom + 1);
                cxSum += (float)sumX / count;
                counted++;
                result.FramePx = Mathf.Max(result.FramePx, w);
            }

            if (counted == 0)
            {
                return result;
            }

            float bMin = float.MaxValue;
            float bMax = float.MinValue;
            float bSum = 0f;
            float hSum = 0f;

            foreach (float b in bottoms)
            {
                bMin = Mathf.Min(bMin, b);
                bMax = Mathf.Max(bMax, b);
                bSum += b;
            }

            foreach (float hh in heights)
            {
                hSum += hh;
            }

            result.Frames = counted;
            result.BottomAvg = bSum / counted;
            result.BottomJitter = bMax - bMin;
            result.ContentH = hSum / counted;
            result.CenterX = cxSum / counted;

            return result;
        }
    }
}
