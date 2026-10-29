using System;
using System.Collections;
using UnityEngine;

namespace CloudEyrie.Fx
{
    /// <summary>
    /// 打击顿帧：命中瞬间把时间压到几乎停住一小会儿，再弹回。
    ///
    /// 为什么需要它：这游戏之前打中敌人只有一圈火花，画面没有任何"卡住"的感觉，
    /// 挥剑像划过空气 —— 而顿帧是 2D 动作游戏里最廉价、最有效的手感来源。
    ///
    /// 几个必须注意的点：
    /// <list type="bullet">
    ///   <item>用 <c>Time.unscaledDeltaTime</c> 计时，否则时间被自己压住后永远走不出去；</item>
    ///   <item>不压到 0，压到 0.02 —— 压 0 会让物理/动画全停，某些代码会除零或卡住；</item>
    ///   <item>已经在顿帧时再来一次只延长，不叠加（连续命中不会把时间冻结成几十帧）；</item>
    ///   <item>演出闸门（<see cref="GameFlow"/>/timeScale=0）期间不介入，别和过场抢时间。</item>
    /// </list>
    /// </summary>
    public class HitStop : MonoBehaviour
    {
        private static HitStop _instance;

        private const float FrozenScale = 0.02f;

        private float _remaining;
        private bool _running;
        private float _savedScale = 1f;

        /// <summary>触发一次顿帧（秒）。<paramref name="onStart"/> 在冻结的那一帧回调，用来震屏。</summary>
        public static void Play(float seconds, Action onStart = null)
        {
            if (seconds <= 0f)
            {
                return;
            }

            // 时间本来就被别人停住了（过场/暂停）：不要掺和
            if (Time.timeScale <= 0.01f)
            {
                return;
            }

            if (_instance == null)
            {
                var go = new GameObject("[HitStop]");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<HitStop>();
            }

            _instance.Trigger(seconds, onStart);
        }

        private void Trigger(float seconds, Action onStart)
        {
            onStart?.Invoke();

            if (_running)
            {
                _remaining = Mathf.Max(_remaining, seconds);
                return;
            }

            _savedScale = Time.timeScale;
            _remaining = seconds;
            _running = true;
            Time.timeScale = FrozenScale;
        }

        private void Update()
        {
            if (!_running)
            {
                return;
            }

            _remaining -= Time.unscaledDeltaTime;

            if (_remaining > 0f)
            {
                return;
            }

            _running = false;
            Time.timeScale = _savedScale > 0.01f ? _savedScale : 1f;
        }

        private void OnDestroy()
        {
            if (_running)
            {
                Time.timeScale = 1f;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
