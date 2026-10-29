using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie.Character
{
    /// <summary>
    /// 青禾的全部表现 = AutoSprite 导入的 8 个 prefab 轮流显隐。
    ///
    /// 为什么不再自己拼 AnimatorController：AutoSprite 导出的每个 prefab 里
    /// **已经带好了自己的一套**（controller / 剪辑 / 枢轴 / 缩放）。
    /// 自己再拼一份等于把素材的这些假设全部推翻重来 —— 之前两版的问题
    /// （悬空、朝向反、切换跳）都出在这里。直接把原 prefab 当"帧容器"用，
    /// 假设就只剩一条：它们是同一角色、朝右。
    ///
    /// 没有对应动作的状态（格挡/受击/落地/灵雾/灵合/踏云闪）→ 显示静息。
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class QingheAnimatorView : MonoBehaviour
    {
        /// <summary>状态名 → Resources/anim 下的 prefab 名（由 AutoSprite 导出件拷贝而来）</summary>
        private static readonly (string state, string resource)[] Sources =
        {
            ("Idle", "anim/qinghe_idle"),
            ("Walk", "anim/qinghe_walk"),
            ("Run", "anim/qinghe_run"),
            ("Jump", "anim/qinghe_jump"),
            ("QuickStrike", "anim/qinghe_quickstrike"),
            ("TripleCombo", "anim/qinghe_triplecombo"),
            ("RisingCrescent", "anim/qinghe_risingcrescent"),
            ("CycloneSlash", "anim/qinghe_cycloneslash"),
        };

        [Tooltip("目标世界高度")]
        [SerializeField] private float figureHeight = 232f;

        [Tooltip("脚底相对碰撞盒底边再抬一点")]
        [SerializeField] private float feetLift = 0f;

        [Tooltip("一次剑招占用的时间（这段时间内不回到移动状态）")]
        [SerializeField] private float actionSeconds = 0.35f;

        private readonly Dictionary<string, GameObject> _instances = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, SpriteRenderer> _renderers = new Dictionary<string, SpriteRenderer>();

        private Rigidbody2D _body;
        private BoxCollider2D _collider;
        private Transform _facingRoot;

        private string _state;
        private string _actionState = "Idle";
        private float _actionUntil;
        private float _facing = 1f;

        private void Start()
        {
            _body = GetComponent<Rigidbody2D>();
            _collider = GetComponent<BoxCollider2D>();

            var root = new GameObject("AnimRoot");
            root.transform.SetParent(transform, false);
            _facingRoot = root.transform;

            // AutoSprite 导出的帧实际**朝左**画（目录名写 _right）。
            // 所以"面向右"= 镜像：root.scaleX = -1。换素材只改这一处。
            _facing = 1f;
            _facingRoot.localScale = new Vector3(-1f, 1f, 1f);

            foreach ((string state, string resource) in Sources)
            {
                GameObject prefab = Resources.Load<GameObject>(resource);

                if (prefab == null)
                {
                    Debug.LogWarning($"[QingheAnimator] 读不到 Resources/{resource}");
                    continue;
                }

                GameObject go = Instantiate(prefab, _facingRoot);
                go.name = state;

                // prefab 自带的相机/碰撞不要，只留渲染
                foreach (Camera cam in go.GetComponentsInChildren<Camera>(true)) Destroy(cam);
                foreach (Collider2D col in go.GetComponentsInChildren<Collider2D>(true)) Destroy(col);

                var sr = go.GetComponentInChildren<SpriteRenderer>(true);

                if (sr != null)
                {
                    sr.sortingOrder = 30;
                    _renderers[state] = sr;
                }

                // AutoSprite 的帧是 1 世界单位 → 放大到角色高度
                go.transform.localScale = new Vector3(figureHeight, figureHeight, 1f);
                go.SetActive(false);
                _instances[state] = go;
            }

            Show("Idle");
        }

        private void LateUpdate()
        {
            if (_facingRoot == null)
            {
                return;
            }

            UpdateFacing();

            string desired = ChooseState();

            if (desired != _state)
            {
                Show(desired);
            }
        }

        private void Show(string state)
        {
            if (!_instances.ContainsKey(state))
            {
                state = "Idle";
            }

            if (!_instances.ContainsKey(state))
            {
                return;
            }

            foreach (var pair in _instances)
            {
                pair.Value.SetActive(pair.Key == state);
            }

            _state = state;

            var animator = _instances[state].GetComponentInChildren<Animator>(true);

            if (animator != null)
            {
                animator.Play(0, 0, 0f);
            }

            AlignFeet(state);
        }

        /// <summary>按包围盒把内容底边对齐到碰撞盒底边（每个动作各对齐一次）</summary>
        private void AlignFeet(string state)
        {
            if (!_renderers.TryGetValue(state, out SpriteRenderer sr) || sr.sprite == null)
            {
                return;
            }

            float colliderBottom = _collider != null ? _collider.bounds.min.y : transform.position.y;
            float delta = colliderBottom + feetLift - sr.bounds.min.y;

            Transform t = sr.transform;
            Vector3 p = t.position;
            t.position = new Vector3(p.x, p.y + delta, p.z);

            _renderers[state] = sr;
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
                _facingRoot.localScale = new Vector3(-want, 1f, 1f);
            }
        }

        private string ChooseState()
        {
            if (Time.time < _actionUntil)
            {
                return _actionState;
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

        /// <summary>战斗侧调用：映射剑招；没有对应动作就保持当前状态</summary>
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
                return;
            }

            _actionState = mapped;
            _actionUntil = Time.time + actionSeconds;
            Show(mapped);
        }
    }
}
