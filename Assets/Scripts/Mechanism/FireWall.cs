using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 火墙：同组的火盆**全部点燃**之前一直挡路，全亮后熄灭（可穿过）。
    ///
    /// 设计稿火境段3：「通道被火墙封死，两侧各一座火盆，两座同燃后火墙熄灭」。
    ///
    /// <b>与 <see cref="ElementBarrier"/> 的分工</b>
    ///
    /// 屏障按**玩家形态**开关（灵雾可穿），火墙按**机关状态**开关（点燃才熄）。
    /// 两者都会开关碰撞体，但判据来源完全不同 —— 合成一个组件的话，
    /// 「什么时候开着」会变成一堆 bool 的组合，读的人要同时在脑子里跑两套逻辑。
    /// </summary>
    public class FireWall : LevelMechanism
    {
        [Tooltip("要盯的那组火盆的 id，与 Brazier.groupId 对应")]
        [SerializeField] private string groupId = "huo_03";

        [Tooltip("真正挡路的碰撞体节点")]
        [SerializeField] private Collider2D blocker;

        [Tooltip("感应范围（比阻挡体宽）。用来决定什么时候检查火盆状态")]
        [SerializeField] private Collider2D sense;

        [Header("表现")]
        [Tooltip("火焰立绘。熄灭后它会淡出")]
        [SerializeField] private Transform art;

        [SerializeField] private float fadeDuration = 0.7f;

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
            // 只在玩家靠近时检查 —— 火盆状态变了要立刻反应，但没必要每帧全图轮询
            if (!_playerInside)
            {
                return;
            }

            bool lit = Brazier.GroupAllLit(groupId);

            if (lit != _open)
            {
                _open = lit;
                ApplyBlocking(lit);

                if (lit)
                {
                    GameLog.I($"[FireWall] 组 {groupId} 全部点燃，火墙熄灭");
                }
            }

            // 熄灭过程：火焰淡出
            float target = _open ? 0f : 1f;

            if (Mathf.Abs(_alpha - target) > 0.001f)
            {
                _alpha = Mathf.MoveTowards(_alpha, target, Time.deltaTime / Mathf.Max(0.01f, fadeDuration));
                ApplyAlpha(_alpha);
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

            if (!inside && _open)
            {
                // 离开后重新封上？**不封** —— 火盆是永久点燃的，回头路该是通的。
                // （与 ElementBarrier 不同：那个按形态开关，人一走就得恢复。）
            }
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
            // 火墙不直接触发，由火盆组的状态驱动
        }
    }
}
