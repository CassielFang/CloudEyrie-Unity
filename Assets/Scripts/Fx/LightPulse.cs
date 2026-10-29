using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CloudEyrie.Fx
{
    /// <summary>
    /// 一盏会闪的光。火光不能是恒定的 —— 恒定亮度看起来像一块贴纸。
    ///
    /// 用两个不同频率的正弦叠加 + 一点噪声相位，得到"不规律但连续"的呼吸，
    /// 比纯随机闪烁更像火（纯随机在 60fps 下会抖成频闪）。
    /// </summary>
    [RequireComponent(typeof(Light2D))]
    public class LightPulse : MonoBehaviour
    {
        [SerializeField] private float baseIntensity = 1.4f;
        [SerializeField] private float amplitude = 0.35f;
        [SerializeField] private float speedA = 5.3f;
        [SerializeField] private float speedB = 11.7f;

        [Tooltip("半径也会跟着轻微波动，火苗的照明边缘才不是死圆")]
        [SerializeField] private float radiusWobble = 0.06f;

        private Light2D _light;
        private float _baseRadius;
        private float _phase;

        private void Awake()
        {
            _light = GetComponent<Light2D>();
            _baseRadius = _light.pointLightOuterRadius;
            _phase = Random.value * 10f;
        }

        private void Update()
        {
            if (_light == null)
            {
                return;
            }

            float t = Time.time;

            float wobble = Mathf.Sin((t + _phase) * speedA) * 0.6f
                         + Mathf.Sin((t + _phase) * speedB) * 0.4f;

            _light.intensity = baseIntensity + wobble * amplitude;
            _light.pointLightOuterRadius = _baseRadius * (1f + wobble * radiusWobble);
        }
    }
}
