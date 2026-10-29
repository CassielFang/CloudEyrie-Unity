using UnityEngine;

namespace CloudEyrie.Character
{
    /// <summary>
    /// 混合视图：有 AutoSprite 剪辑的状态用 Animator，没有的（格挡/受击/落地/灵雾/灵合/踏云闪）
    /// 回退到原来的帧序列。
    ///
    /// 为什么两种并存而不是二选一：AutoSprite 只导了 8 段（移动 4 + 剑招 4），
    /// 而游戏里有十几种状态。全换会丢一半表现；全不换又浪费新素材。
    ///
    /// 实现上让两者各用一个 SpriteRenderer：
    /// <list type="bullet">
    ///   <item>根节点上的渲染器归 <see cref="SpriteSequenceView"/>（旧）；</item>
    ///   <item>子节点 AnimArt 上的渲染器归 Animator（新）。</item>
    /// </list>
    /// 由这里按状态二选一开/关，同一帧只亮一个，不会出现双重影。
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class HybridCharacterView : MonoBehaviour
    {
        [Tooltip("AutoSprite 合并出来的 AnimatorController")]
        [SerializeField] private RuntimeAnimatorController controller;

        [Tooltip("新动画的子节点名（运行时自动建）")]
        [SerializeField] private string animNodeName = "AnimArt";

        [Tooltip("角色世界高度 —— AutoSprite 的帧是 1 世界单位，按这个放大")]
        [SerializeField] private float figureHeight = 232f;

        [Tooltip("脚底相对碰撞盒底边往上多少（沿用旧序列的设定）")]
        [SerializeField] private float feetMargin = 24f;

        private QingheController _owner;
        private CompanionStateMachine _forms;
        private Rigidbody2D _body;
        private BoxCollider2D _collider;
        private SpriteSequenceView _sequence;
        private Animator _animator;
        private SpriteRenderer _animRenderer;
        private SpriteRenderer _sequenceRenderer;

        private string _currentState;

        private void Start()
        {
            _owner = GetComponent<QingheController>();
            _forms = GetComponent<CompanionStateMachine>();
            _body = GetComponent<Rigidbody2D>();
            _collider = GetComponent<BoxCollider2D>();
            _sequence = GetComponentInChildren<SpriteSequenceView>(true);

            // ⚠️ 序列视图挂在子节点 Art 上，根节点没有 SpriteRenderer。
            // 之前按根节点取会拿到 null，于是新旧两层同时显示（重影）。
            _sequenceRenderer = _sequence != null ? _sequence.GetComponent<SpriteRenderer>() : null;

            if (controller == null)
            {
                controller = Resources.Load<RuntimeAnimatorController>("anim/QingheAnimator");
            }

            Transform node = transform.Find(animNodeName);

            if (node == null)
            {
                var go = new GameObject(animNodeName);
                go.transform.SetParent(transform, false);
                node = go.transform;
            }

            _animRenderer = node.GetComponent<SpriteRenderer>();

            if (_animRenderer == null)
            {
                _animRenderer = node.gameObject.AddComponent<SpriteRenderer>();
            }

            _animator = node.GetComponent<Animator>();

            if (_animator == null)
            {
                _animator = node.gameObject.AddComponent<Animator>();
            }

            _animator.runtimeAnimatorController = controller;

            float k = figureHeight;
            _facingSign = 1f;
            node.localScale = new Vector3(k, k, 1f);

            float colliderBottom = _collider != null ? _collider.offset.y - _collider.size.y * 0.5f : 0f;
            node.localPosition = new Vector3(0f, colliderBottom + figureHeight * 0.5f - feetMargin, 0f);

            _animRenderer.sortingOrder = _sequenceRenderer != null ? _sequenceRenderer.sortingOrder : 30;
            _animRenderer.enabled = false;
        }

        /// <summary>AutoSprite 只有朝右的素材，向左走时靠镜像节点</summary>
        private float _facingSign = 1f;

        private void ApplyFacing()
        {
            if (_body == null || _animRenderer == null)
            {
                return;
            }

            float vx = _body.linearVelocity.x;

            if (Mathf.Abs(vx) > 4f)
            {
                float want = vx > 0f ? 1f : -1f;

                if (!Mathf.Approximately(want, _facingSign))
                {
                    _facingSign = want;
                    Transform node = _animRenderer.transform;
                    node.localScale = new Vector3(figureHeight * _facingSign, figureHeight, 1f);
                }
            }
        }

        private void Update()
        {
            if (_animator == null || _animRenderer == null || controller == null)
            {
                return;
            }

            string desired = ChooseState();
            bool useAnimator = !string.IsNullOrEmpty(desired);

            ApplyFacing();

            if (_sequenceRenderer != null)
            {
                _sequenceRenderer.enabled = !useAnimator;
            }

            _animRenderer.enabled = useAnimator;

            if (!useAnimator || desired == _currentState)
            {
                return;
            }

            _currentState = desired;
            _animator.CrossFade(desired, 0.08f);
        }

        /// <summary>返回 Animator 状态名；返回 null 表示这一步交给旧序列。</summary>
        private string ChooseState()
        {
            if (_owner == null || _sequence == null)
            {
                return null;
            }

            if (_owner.CurrentForm != CompanionForm.Entity)
            {
                return null;
            }

            string clip = _sequence.CurrentClip;

            if (!string.IsNullOrEmpty(clip))
            {
                if (clip.Contains("格挡") || clip.Contains("受击") || clip.Contains("落地") || clip.Contains("踏云闪"))
                {
                    return null;
                }

                if (clip.Contains("轻击一")) return "QuickStrike";
                if (clip.Contains("轻击二")) return "TripleCombo";
                if (clip.Contains("轻击三")) return "RisingCrescent";
                if (clip.Contains("重击")) return "CycloneSlash";
            }

            if (_body == null)
            {
                return "Idle";
            }

            if (!IsGrounded())
            {
                return "Jump";
            }

            float speed = Mathf.Abs(_body.linearVelocity.x);

            if (speed >= 200f)
            {
                return "Run";
            }

            return speed >= 12f ? "Walk" : "Idle";
        }

        private bool IsGrounded()
        {
            if (_collider == null)
            {
                return true;
            }

            int world = LayerMask.NameToLayer("WORLD");
            int mask = world >= 0 ? (1 << world) : ~0;

            return Physics2D.BoxCast(
                (Vector2)transform.position + _collider.offset,
                _collider.size * 0.95f,
                0f,
                Vector2.down,
                4f,
                mask).collider != null;
        }
    }
}
