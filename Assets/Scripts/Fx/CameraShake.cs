using UnityEngine;

namespace CloudEyrie.Fx
{
    /// <summary>
    /// 轻微震屏。只动相机的**本地偏移**，不碰跟随逻辑 ——
    /// 跟随（<see cref="CameraFollow"/>）每帧写 camera.position，
    /// 这里在 LateUpdate 里再加一个很小的偏移量，两者互不覆盖。
    ///
    /// 幅度用"世界单位"：3 ≈ 屏幕上 4~5 像素，属于"感觉得到但不晕"的量级。
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class CameraShake : MonoBehaviour
    {
        private static CameraShake _instance;

        [Tooltip("偏移的最大幅度上限（世界单位），防止多次叠加把画面甩飞")]
        [SerializeField] private float maxAmplitude = 10f;

        private float _amplitude;
        private float _duration;
        private float _elapsed;
        private Vector3 _baseLocal;

        private void Awake()
        {
            _instance = this;

            // 基准取**本地位置**：相机常挂在 CameraRig 下，跟随逻辑动的是 rig（父级），
            // 直接写 transform.position 会被父级的移动覆盖掉，看不出在抖。
            _baseLocal = transform.localPosition;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>抖一下。<paramref name="amplitude"/> 是初始幅度（世界单位），按时间线性衰减。</summary>
        public static void Shake(float amplitude, float duration)
        {
            if (_instance == null || amplitude <= 0f || duration <= 0f)
            {
                return;
            }

            // 取较大者而不是相加：连续命中不会越抖越夸张
            _instance._amplitude = Mathf.Max(_instance._amplitude, Mathf.Min(amplitude, _instance.maxAmplitude));
            _instance._duration = Mathf.Max(_instance._duration, duration);
            _instance._elapsed = 0f;
        }

        private void LateUpdate()
        {
            if (_amplitude <= 0f)
            {
                transform.localPosition = _baseLocal;
                return;
            }

            _elapsed += Time.unscaledDeltaTime;

            if (_elapsed >= _duration)
            {
                _amplitude = 0f;
                _duration = 0f;
                transform.localPosition = _baseLocal;
                return;
            }

            float k = 1f - _elapsed / _duration;
            float t = Time.unscaledTime * 40f;

            float x = (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f * _amplitude * k;
            float y = (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * _amplitude * k;

            transform.localPosition = _baseLocal + new Vector3(x, y, 0f);
        }
    }
}
