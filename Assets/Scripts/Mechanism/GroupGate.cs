using UnityEngine;

namespace CloudEyrie
{
    /// <summary>这道门要等哪一组机关完成。</summary>
    public enum GateCondition
    {
        /// <summary>等一组火盆全部点燃（火境段3）。</summary>
        BraziersLit,

        /// <summary>等一组剑碑按序共鸣完（金境段3）。</summary>
        StelesComplete,
    }

    /// <summary>
    /// 组门：等**某一组机关全部完成**之后才打开的门。
    ///
    /// 火境的「火墙」与金境的「剑气门」是同一个东西 —— 都是"把一组机关做完，门就没了"，
    /// 只是皮肤与触发源不同。所以合成一个组件、用 <see cref="GateCondition"/> 分派，
    /// 而不是各写一份开关碰撞体的逻辑（那种复制最容易出现的后果是
    /// 修了其中一道门的 bug、另一道没修，而且两边表现不一致时很难看出是同源问题）。
    ///
    /// <b>与 <see cref="ElementBarrier"/> 的分工</b>
    ///
    /// 屏障按**玩家形态**开关（灵雾可穿），组门按**机关状态**开关（做完才开）。
    /// 两者都会开关碰撞体，但判据来源完全不同 —— 合成一个的话，
    /// 「什么时候开着」会变成一堆 bool 的组合，读的人要同时在脑子里跑两套逻辑。
    /// </summary>
    public class GroupGate : LevelMechanism
    {
        [Tooltip("这道门等哪一组机关")]
        [SerializeField] private GateCondition condition = GateCondition.BraziersLit;

        [Tooltip("要盯的那组机关的 id（与 Brazier.groupId / SwordStele.groupId 对应）")]
        [SerializeField] private string groupId = "huo_03";

        [Tooltip("真正挡路的碰撞体节点")]
        [SerializeField] private Collider2D blocker;

        [Tooltip("感应范围（比阻挡体宽）。用来决定什么时候检查那一组的状态")]
        [SerializeField] private Collider2D sense;

        [Header("表现")]
        [Tooltip("门的立绘。打开后它会淡出")]
        [SerializeField] private Transform art;

        [SerializeField] private float fadeDuration = 0.7f;

        [Header("引导（分级）")]
        [Tooltip("玩家在门前停留多久后给第一条提示 /秒")]
        [SerializeField] private float hintDelay = 1.2f;

        [Tooltip("之后每条提示的间隔 /秒")]
        [SerializeField] private float hintInterval = 6f;

        [Tooltip("分级提示：先说现象，再说动作，最后说规则。留空 = 不给提示。\n" +
                 "只在玩家**被门挡住**（在感应区里且条件未满足）时才出现，不是全局定时弹窗。")]
        [SerializeField] private string[] lockedHints = new string[0];

        [Tooltip("给提示时把相关机关高亮多久 /秒（火盆会脉冲放大）")]
        [SerializeField] private float hintHighlightSeconds = 2.5f;

        private int _hintIndex;
        private float _hintTimer;
        private bool _open;
        private float _alpha = 1f;
        private SpriteRenderer[] _renderers;
        private Color[] _baseColors;
        private bool _playerInside;

        private void Reset()
        {
            if (sense != null)
            {
                sense.isTrigger = true;
            }
        }

        private void Awake()
        {
            if (sense != null)
            {
                sense.isTrigger = true;
            }

            if (art == null)
            {
                art = transform;
            }

            _renderers = art.GetComponentsInChildren<SpriteRenderer>(true);
            _baseColors = new Color[_renderers.Length];

            for (int i = 0; i < _renderers.Length; i++)
            {
                _baseColors[i] = _renderers[i].color;
            }

            ApplyBlocking(open: false);
        }

        private void Update()
        {
            // 只在玩家靠近时检查 —— 组状态变了要立刻反应，但没必要每帧全图轮询
            if (!_playerInside)
            {
                // 离开门时不重置 _hintIndex：同一道谜题已经提示到第几级就记住，
                // 回头再来不会从"火墙挡住了去路"重新念一遍。
                _hintTimer = 0f;
                return;
            }

            bool done = IsConditionMet();

            if (done != _open)
            {
                _open = done;
                ApplyBlocking(done);

                if (done)
                {
                    GameLog.I($"[GroupGate] 组 {groupId} 已完成，门打开");
                    _hintIndex = lockedHints != null ? lockedHints.Length : 0;
                }
            }

            float target = _open ? 0f : 1f;

            if (Mathf.Abs(_alpha - target) > 0.001f)
            {
                _alpha = Mathf.MoveTowards(_alpha, target, Time.deltaTime / Mathf.Max(0.01f, fadeDuration));
                ApplyAlpha(_alpha);
            }

            if (!_open)
            {
                TickHint();
            }
        }

        /// <summary>
        /// 玩家被门挡住时逐级给提示。
        ///
        /// <b>为什么不按全局时间弹</b>：后台定时器会在玩家还没到门前时就把答案说了；
        /// 这里以「玩家站在门前、门还是关的」为触发条件 —— 这正是"玩家卡住了"的信号。
        /// 提示分三级：现象 → 该做什么 → 规则，每条给完把相关机关脉冲高亮一次。
        /// </summary>
        private void TickHint()
        {
            if (lockedHints == null || _hintIndex >= lockedHints.Length)
            {
                return;
            }

            _hintTimer += Time.deltaTime;
            float wait = _hintIndex == 0 ? hintDelay : Mathf.Max(hintDelay, hintInterval);

            if (_hintTimer < wait)
            {
                return;
            }

            string text = lockedHints[_hintIndex];

            if (!string.IsNullOrEmpty(text))
            {
                // 引导要比普通拾取提示停留更久：玩家得抬头、看清、再决定做什么
                EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = text, Seconds = 5f });
                GameLog.I($"[GroupGate] 引导 {groupId}（第 {_hintIndex + 1} 级）：{text}");
            }

            // 火墙配的是火盆 —— 点名把没点亮的火盆脉冲一下，视线才会落到正确的东西上
            Brazier.HighlightGroup(groupId, hintHighlightSeconds);

            _hintIndex++;
            _hintTimer = 0f;
        }

        private bool IsConditionMet()
        {
            switch (condition)
            {
                case GateCondition.StelesComplete:
                    return SwordStele.GroupComplete(groupId);

                default:
                    return Brazier.GroupAllLit(groupId);
            }
        }

        private void OnTriggerEnter2D(Collider2D other) { Handle(other, true); }
        private void OnTriggerStay2D(Collider2D other) { Handle(other, true); }
        private void OnTriggerExit2D(Collider2D other) { Handle(other, false); }

        private void Handle(Collider2D other, bool inside)
        {
            if (other.GetComponentInParent<QingheController>() == null)
            {
                return;
            }

            _playerInside = inside;

            // 离开后**不重新封上**：火盆与剑碑都是永久性的，回头路该是通的。
            // （与 ElementBarrier 相反 —— 那个按形态开关，人一走就得恢复。）
        }

        private void ApplyBlocking(bool open)
        {
            if (blocker != null)
            {
                blocker.enabled = !open;
            }
        }

        private void ApplyAlpha(float a)
        {
            if (_renderers == null)
            {
                return;
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                Color c = _baseColors[i];
                c.a *= a;
                _renderers[i].color = c;
            }
        }

        protected override void OnActivate(GameObject who)
        {
            // 门由组状态驱动，不直接触发
        }
    }
}
