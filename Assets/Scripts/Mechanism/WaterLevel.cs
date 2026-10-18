using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 水境「水位」：青铜水钟被灵合敲响后，全局水位上涨并保持 30 秒，再慢慢落回。
    ///
    /// <b>为什么要有全局水位，而不是直接抬某一块水台</b>
    ///
    /// 设计稿写的是「涨水 30s」——它改变的是**整个水面的高度**：浮台随之上浮、
    /// 原本跳不上的高台被水淹到够得着、低处的通道被水封住。这些是同一个数值
    /// 的三种表现，抬单块平台做不出来。
    ///
    /// 现在的实现只做到第一步：水位是一根全局可见的高度，<see cref="WaterFollower"/>
    /// 挂在平台/装饰上跟着升降。水本身的可游/可淹（把玩家浮起来、封通道）
    /// 还没做 —— 那需要水面碰撞与浮力，属于下一步。
    /// </summary>
    public class WaterLevel : MonoBehaviour
    {
        public static WaterLevel Instance { get; private set; }

        [Header("高度")]
        [Tooltip("常态水面 y")]
        [SerializeField] private float baseLevelY = -195f;

        [Tooltip("涨水后抬高多少")]
        [SerializeField] private float raisedHeight = 120f;

        [Header("时序")]
        [Tooltip("上涨耗时 /秒")]
        [SerializeField] private float riseDuration = 2f;

        [Tooltip("保持时间 /秒（设计稿 30s）")]
        [SerializeField] private float holdDuration = 30f;

        [Tooltip("回落耗时 /秒")]
        [SerializeField] private float lowerDuration = 3f;

        /// <summary>0 = 常态，1 = 满水位</summary>
        private float _t;

        /// <summary>0 静止 / 1 上涨 / 2 保持 / 3 回落</summary>
        private int _state;

        private float _timer;

        /// <summary>当前水面世界 y</summary>
        public float CurrentY
        {
            get { return baseLevelY + raisedHeight * Mathf.SmoothStep(0f, 1f, _t); }
        }

        /// <summary>当前水位相对常态的偏移（正数=涨上去了）</summary>
        public float Offset
        {
            get { return CurrentY - baseLevelY; }
        }

        public float BaseY
        {
            get { return baseLevelY; }
        }

        /// <summary>是否已涨到接近满水位（给"必须涨水才够得着"的判定用）</summary>
        public bool IsRaised
        {
            get { return _t > 0.9f; }
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>开始一轮涨水。已经是满水位时只把保持计时清零（再敲一次＝续 30 秒）。</summary>
        public void Raise()
        {
            if (_state == 2)
            {
                _timer = 0f;
                return;
            }

            if (_state == 1)
            {
                return;
            }

            _state = 1;
            _timer = 0f;
            GameLog.I("[WaterLevel] 水钟共鸣，开始涨水");
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            switch (_state)
            {
                case 1:
                    _timer += dt;
                    _t = Mathf.Clamp01(_timer / Mathf.Max(0.01f, riseDuration));

                    if (_t >= 1f)
                    {
                        _state = 2;
                        _timer = 0f;
                    }

                    break;

                case 2:
                    _timer += dt;

                    if (_timer >= holdDuration)
                    {
                        _state = 3;
                        _timer = 0f;
                    }

                    break;

                case 3:
                    _timer += dt;
                    _t = 1f - Mathf.Clamp01(_timer / Mathf.Max(0.01f, lowerDuration));

                    if (_t <= 0f)
                    {
                        _state = 0;
                        _timer = 0f;
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// 跟着全局水位升降的东西：水面浮台、露出水面的石台、水草…
    ///
    /// 记录自己**初始**的 y，之后每帧按水位偏移平移 —— 不累积，
    /// 所以水位回落时它一定回到原位，不会越涨越高。
    /// </summary>
    public class WaterFollower : MonoBehaviour
    {
        [Tooltip("留空则自动找场景里的 WaterLevel")]
        [SerializeField] private WaterLevel water;

        [Tooltip("跟随比例。1 = 完全随水；0.5 = 只抬一半，用来做半浮的观感")]
        [SerializeField] private float followRatio = 1f;

        private float _baseY;

        private void Awake()
        {
            _baseY = transform.position.y;
        }

        private void Start()
        {
            if (water == null)
            {
                water = WaterLevel.Instance;
            }

            if (water == null)
            {
                GameLog.W("[WaterFollower] 场景里没有 WaterLevel，本对象不会跟随水位");
                enabled = false;
            }
        }

        private void Update()
        {
            if (water == null)
            {
                return;
            }

            Vector3 p = transform.position;
            p.y = _baseY + water.Offset * followRatio;
            transform.position = p;
        }
    }
}
