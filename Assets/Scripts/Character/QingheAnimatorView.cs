using UnityEngine;

namespace CloudEyrie.Character
{
    /// <summary>
    /// 青禾的全部表现交给 AutoSprite 的 Animator（不再混用旧的帧序列）。
    ///
    /// 为什么彻底换：混用两套时，"有剪辑的状态"和"没有剪辑的状态"切换会在
    /// 尺寸、脚底位置、笔触上同时跳一下 —— 观感上比任何一套单独用都差。
    /// 现在的规则很简单：**没有对应动作就退回静息**，宁可不动也不要跳。
    ///
    /// 脚底对齐：AutoSprite 的帧是 1 世界单位、枢轴在中心，画布上下还有留白。
    /// 所以不能按"身高一半"去摆，而是在第一帧真正出图之后**读包围盒**，
    /// 把内容底边挪到碰撞盒底边 —— 换素材、换留白都不用再改常量。
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class QingheAnimatorView : MonoBehaviour
    {
        [Tooltip("AutoSprite 合并出来的 AnimatorController（留空则从 Resources/anim 读）")]
        [SerializeField] private RuntimeAnimatorController controller;

        [Tooltip("目标世界高度（旧序列版是 232）")]
        [SerializeField] private float figureHeight = 232f;

        [Tooltip("脚底相对碰撞盒底边再往上一点，避免穿地")]
        [SerializeField] private float feetLift = 0f;

        private QingheController _owner;
        private Rigidbody2D _body;
        private BoxCollider2D _collider;
        private SpriteSequenceView _sequence;
        private SpriteRenderer _sequenceRenderer;
        private Animator _animator;
        private SpriteRenderer _renderer;

        private string _state = "Idle";
        private bool _feetAligned;
        private float _facing = 1f;

        private void Start()
        {
            _owner = GetComponent<QingheController>();
            _body = GetComponent<Rigidbody2D>();
            _collider = GetComponent<BoxCollider2D>();

            // 旧序列整套停用：渲染器关掉、组件也关掉（不再参与任何表现）。
            // 战斗侧的动作改由 CombatController → PlayAction 直接驱动 Animator。
            SpriteSequenceView oldView = GetComponentInChildren<SpriteSequenceView>(true);

            if (oldView != null)
            {
                SpriteRenderer oldRenderer = oldView.GetComponent<SpriteRenderer>();

                if (oldRenderer != null)
                {
                    oldRenderer.enabled = false;
                }

                oldView.enabled = false;
            }

            if (controller == null)
            {
                controller = Resources.Load<RuntimeAnimatorController>("anim/QingheAnimator");
            }

            Transform node = transform.Find("AnimArt");

            if (node == null)
            {
                var go = new GameObject("AnimArt");
                go.transform.SetParent(transform, false);
                node = go.transform;
            }

            _renderer = node.GetComponent<SpriteRenderer>();

            if (_renderer == null)
            {
                _renderer = node.gameObject.AddComponent<SpriteRenderer>();
            }

            _animator = node.GetComponent<Animator>();

            if (_animator == null)
            {
                _animator = node.gameObject.AddComponent<Animator>();
            }

            _animator.runtimeAnimatorController = controller;

            node.localScale = new Vector3(figureHeight, figureHeight, 1f);
            _renderer.sortingOrder = 30;

            _animator.Play("Idle", 0, 0f);
        }

        private void LateUpdate()
        {
            if (_animator == null || _renderer == null)
            {
                return;
            }

            AlignFeetOnce();
            UpdateFacing();
            UpdateState();
        }

        /// <summary>第一帧出图后按包围盒把脚底对齐到碰撞盒底边（只做一次）</summary>
        private void AlignFeetOnce()
        {
            if (_feetAligned || _renderer.sprite == null)
            {
                return;
            }

            _feetAligned = true;

            float colliderBottom = _collider != null ? _collider.bounds.min.y : transform.position.y;
            float spriteBottom = _renderer.bounds.min.y;
            float delta = colliderBottom + feetLift - spriteBottom;

            Vector3 p = _renderer.transform.position;
            _renderer.transform.position = new Vector3(p.x, p.y + delta, p.z);
        }

        private void UpdateFacing()
        {
            if (_body == null)
            {
                return;
            }

            float vx = _body.linearVelocity.x;

            if (Mathf.Abs(vx) < 4f)
            {
                return;
            }

            float want = vx > 0f ? 1f : -1f;

            if (!Mathf.Approximately(want, _facing))
            {
                _facing = want;
                Vector3 s = _renderer.transform.localScale;
                _renderer.transform.localScale = new Vector3(Mathf.Abs(s.x) * want, s.y, s.z);
            }
        }

        private void UpdateState()
        {
            string desired = ChooseState();

            if (desired == _state)
            {
                return;
            }

            _state = desired;
            _animator.CrossFade(desired, 0.08f);
        }

        /// <summary>
        /// 没有专门动作的状态（格挡/受击/落地/灵雾/灵合/踏云闪/悬停）一律回静息。
        /// 这是有意的：宁可站着不动，也不要在两套美术之间来回跳。
        /// </summary>
        private string ChooseState()
        {
            if (_body == null)
            {
                return "Idle";
            }

            // 出招窗口：由 CombatController 通过 PlayAction 打进来，到点自动回移动状态
            if (Time.time < _actionUntil)
            {
                return _actionState;
            }

            bool grounded = IsGrounded();

            if (!grounded)
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

        /// <summary>攻击/受击时由战斗侧调用，没有对应剪辑就自然回静息</summary>
        public void PlayAction(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
            {
                return;
            }

            string mapped = null;

            if (clipName.Contains("轻击一")) mapped = "QuickStrike";
            else if (clipName.Contains("轻击二")) mapped = "TripleCombo";
            else if (clipName.Contains("轻击三")) mapped = "RisingCrescent";
            else if (clipName.Contains("重击")) mapped = "CycloneSlash";

            if (mapped == null)
            {
                // 没有对应剪辑的动作（格挡/受击/落地/踏云闪）→ 不播，保持静息
                return;
            }

            _state = mapped;
            _actionState = mapped;
            _actionUntil = Time.time + actionSeconds;
            _animator.CrossFade(mapped, 0.06f);
        }

        [Tooltip("一次剑招占用的时间（这段时间内不回到移动状态）")]
        [SerializeField] private float actionSeconds = 0.35f;

        /// <summary>出招窗口：这段时间内不回到移动状态</summary>
        private float _actionUntil;
        private string _actionState = "Idle";
    }
}
