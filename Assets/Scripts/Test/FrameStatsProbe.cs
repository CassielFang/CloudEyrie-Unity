using System.Text;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// ⚠️ <b>临时诊断组件，验证完请删掉整个文件。</b>
    ///
    /// 用途：把帧间隔统计写进日志，用来判断「卡顿」是**编辑器噪声**还是**游戏本身的问题**。
    /// 编辑器里量到的是「平均 56 FPS + 每 1.82 秒一次 ~120ms 停顿」；
    /// 在打包后的播放器里跑同一个口径，两者一对比就有结论：
    /// <list type="bullet">
    ///   <item>build 里没有那些尖峰 → 编辑器（或其上的工具）造成的，与游戏无关</item>
    ///   <item>build 里照样有 → 是游戏自己的问题，得往渲染/物理那条线查</item>
    /// </list>
    ///
    /// 挂在常驻的 GameManager 上，统计覆盖整局（首页 + 关卡）。
    /// </summary>
    public class FrameStatsProbe : MonoBehaviour
    {
        /// <summary>每隔这么多秒输出一段统计</summary>
        [SerializeField] private float reportInterval = 10f;

        /// <summary>超过这个毫秒数记一次「尖峰」</summary>
        [SerializeField] private float spikeMs = 30f;

        private float _windowTime;
        private int _windowFrames;
        private float _windowSumMs;
        private float _windowMaxMs;
        private int _spikes;
        private int _bigSpikes;
        private readonly StringBuilder _peaks = new StringBuilder();
        private int _totalWindows;

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;   // 不用 deltaTime：timeScale 会干扰
            float ms = dt * 1000f;

            _windowTime += dt;
            _windowFrames++;
            _windowSumMs += ms;

            if (ms > _windowMaxMs)
            {
                _windowMaxMs = ms;
            }

            if (ms > spikeMs)
            {
                _spikes++;

                if (ms > 50f)
                {
                    _bigSpikes++;
                }

                if (_peaks.Length < 300)
                {
                    _peaks.Append($"{ms:F0}ms@帧{Time.frameCount} ");
                }
            }

            if (_windowTime >= reportInterval)
            {
                _totalWindows++;
                float avg = _windowFrames > 0 ? _windowSumMs / _windowFrames : 0f;

                Debug.Log($"[帧统计#{_totalWindows}] {_windowFrames} 帧 / {_windowTime:F1} 秒  " +
                          $"平均 {avg:F2}ms（{1000f / Mathf.Max(0.01f, avg):F0} FPS）  " +
                          $"最大 {_windowMaxMs:F0}ms  " +
                          $">{spikeMs:F0}ms 的帧 {_spikes} 次（其中 >50ms {_bigSpikes} 次）");

                if (_peaks.Length > 0)
                {
                    Debug.Log($"[帧统计#{_totalWindows}] 尖峰：{_peaks}");
                }
                else
                {
                    Debug.Log($"[帧统计#{_totalWindows}] 尖峰：无");
                }

                _windowTime = 0f;
                _windowFrames = 0;
                _windowSumMs = 0f;
                _windowMaxMs = 0f;
                _spikes = 0;
                _bigSpikes = 0;
                _peaks.Clear();
            }
        }
    }
}
