using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CloudEyrie.Fx
{
    /// <summary>
    /// 一关的光照总成：一盏极弱的环境光 + 各处机关的点光。
    ///
    /// 之前整片画面亮度完全均匀 —— 火柱旁边和洞里一样亮，这是"平"的主要原因之一。
    /// 环境光不能省：URP 2D 一旦存在任意 Light2D，没被照到的地方就是纯黑；
    /// 给一盏低强度的 Global 打底，点光才有"照亮"的对比。
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class ElementLights : MonoBehaviour
    {
        [Tooltip("环境光强度。太低会黑成剪影，太高点光就看不出来")]
        [SerializeField] private float ambientIntensity = 0.55f;

        [Header("颜色")]
        [SerializeField] private Color fireColor = new Color(1f, 0.62f, 0.28f);
        [SerializeField] private Color lavaColor = new Color(1f, 0.42f, 0.16f);
        [SerializeField] private Color springColor = new Color(0.45f, 0.85f, 1f);
        [SerializeField] private Color waterColor = new Color(0.35f, 0.6f, 0.9f);

        private void Start()
        {
            CreateAmbient();
            CreateFireLights();
            CreateLavaLights();
            CreateSpringLights();
        }

        private void CreateAmbient()
        {
            var go = new GameObject("Light_Ambient");
            go.transform.SetParent(transform, false);

            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.color = Color.white;
            light.intensity = ambientIntensity;
        }

        private void CreateFireLights()
        {
            foreach (Brazier brazier in FindObjectsByType<Brazier>(FindObjectsInactive.Exclude))
            {
                AddPoint(brazier.transform, fireColor, 2.6f, 320f, "Light_Brazier", true);
            }

            foreach (Hazard hazard in FindObjectsByType<Hazard>(FindObjectsInactive.Exclude))
            {
                if (hazard.name.StartsWith("FirePillar"))
                {
                    AddPoint(hazard.transform, fireColor, 3.2f, 460f, "Light_FirePillar", true);
                }
            }
        }

        private void CreateLavaLights()
        {
            foreach (Hazard hazard in FindObjectsByType<Hazard>(FindObjectsInactive.Exclude))
            {
                if (hazard.name.StartsWith("Lava") || hazard.name.Contains("lava"))
                {
                    AddPoint(hazard.transform, lavaColor, 1.6f, 420f, "Light_Lava", false);
                }
            }
        }

        private void CreateSpringLights()
        {
            foreach (BossSpring spring in FindObjectsByType<BossSpring>(FindObjectsInactive.Exclude))
            {
                AddPoint(spring.transform, springColor, 1.8f, 300f, "Light_Spring", true);
            }

            foreach (Checkpoint spring in FindObjectsByType<Checkpoint>(FindObjectsInactive.Exclude))
            {
                AddPoint(spring.transform, springColor, 1.8f, 300f, "Light_Spring", true);
            }
        }

        private void AddPoint(Transform parent, Color color, float intensity, float radius,
                              string name, bool pulse)
        {
            // 同一个物体只加一盏，避免重复进 Play 时叠光
            Transform existing = parent.Find(name);

            if (existing != null)
            {
                return;
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 60f, 0f);

            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.pointLightInnerRadius = radius * 0.25f;
            light.pointLightOuterRadius = radius;
            light.falloffIntensity = 0.6f;

            if (pulse)
            {
                go.AddComponent<LightPulse>();
            }
        }
    }
}
