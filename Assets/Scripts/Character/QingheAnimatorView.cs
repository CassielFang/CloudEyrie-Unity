using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie.Character
{
    /// <summary>
    /// 青禾的表现 = AutoSprite 导出的 8 个 prefab 轮流显隐（旧帧序列已整套删除）。
    ///
    /// <b>尺寸必须按"画面内容"算，不能按画布算 —— 这是这套素材最大的坑。</b>
    /// AutoSprite 每段动画的画布大小不同（移动 640²、快斩 960²、三连与旋风 1536²），
    /// 而切图时每张都按「画布 = 1 世界单位」导入。于是：
    /// <list type="bullet">
    ///   <item>待机的人物占画布 64%（407/640），三连只占 28%（428/1536）；</item>
    ///   <item>按画布等比放，三连里的人物只有待机的一半略多；</item>
    ///   <item>脚底也会因为留白不同而悬空 / 穿地。</item>
    /// </list>
    /// 所以这里记下每段的 <b>alpha 包围盒</b>（内容高、内容底距画布底），
    /// 用一个<b>全局的 像素→世界 比例</b>（由待机定）统一缩放 ——
    /// 只有全局一个比例，跳跃那种「人本来就是蜷起来的」才会自然变矮，
    /// 而不是被强行拉回同一身高。
    ///
    /// 数字来自 `Assets/Editor/QingheAnimMetrics.cs`（菜单「量青禾动画尺寸」）。
    /// 换素材后重跑一次，把新数字贴回下表即可。
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class QingheAnimatorView : MonoBehaviour
    {
        /// <summary>一段动画的取景参数（像素）</summary>
        private struct Frame
        {
            public string Resource;
            public float Cell;       // 画布边长（px）
            public float ContentH;   // 内容高（px）
            public float Bottom;     // 内容底距画布底（px）
        }

        private static readonly Dictionary<string, Frame> Frames = new Dictionary<string, Frame>
        {
            { "Idle", new Frame { Resource = "anim/qinghe_idle", Cell = 640f, ContentH = 407f, Bottom = 112f } },
            { "Walk", new Frame { Resource = "anim/qinghe_walk", Cell = 640f, ContentH = 398f, Bottom = 122f } },
            { "Run", new Frame { Resource = "anim/qinghe_run", Cell = 640f, ContentH = 389f, Bottom = 116f } },
            { "Jump", new Frame { Resource = "anim/qinghe_jump", Cell = 640f, ContentH = 298f, Bottom = 184f } },
            { "QuickStrike", new Frame { Resource = "anim/qinghe_quickstrike", Cell = 960f, ContentH = 424f, Bottom = 158f } },
            { "TripleCombo", new Frame { Resource = "anim/qinghe_triplecombo", Cell = 1536f, ContentH = 428f, Bottom = 162f } },
            { "RisingCrescent", new Frame { Resource = "anim/qinghe_risingcrescent", Cell = 1536f, ContentH = 446f, Bottom = 214f } },
            { "CycloneSlash", new Frame { Resource = "anim/qinghe_cycloneslash", Cell = 1536f, ContentH = 423f, Bottom = 163f } },
        };

        [Tooltip("人物世界高度（按待机的画面内容高折算，不是画布高）")]
        [SerializeField] private float figureHeight = 232f;

        [Tooltip("一次剑招占用的时间，到点回移动状态")]
        [SerializeField] private float actionSeconds = 0.35f;

        private readonly Dictionary<string, GameObject> _instances = new Dictionary<string, GameObject>();

        private Rigidbody2D _body;
        private BoxCollider2D _collider;
        private Transform _facingRoot;

        private string _state;
        private string _actionState = "Idle";
        private float _actionUntil;
        private float _facing = 1f;

        /// <summary>全局 像素 → 世界 比例，由待机的内容高定</summary>
        private float _pxToWorld;

        private void Start()
        {
            _body = GetComponent<Rigidbody2D>();
            _collider = GetComponent<BoxCollider2D>();

            Frame idle = Frames["Idle"];
            _pxToWorld = figureHeight / idle.ContentH;

            // 待机的脚底落在碰撞盒底边，其余动作沿用同一个偏移 ——
            // 这样站/走/跑/跳之间不会因为"内容底"不同而上下跳。
            float idleBottomLocal = (-0.5f * idle.Cell + idle.Bottom) * _pxToWorld;
            float feetOffset = -idleBottomLocal;

            var root = new GameObject("AnimRoot");
            root.transform.SetParent(transform, false);
            _facingRoot = root.transform;

            // AutoSprite 的帧实际朝左画（目录名写 _right）；面向右 = 镜像
            _facing = 1f;
            _facingRoot.localScale = new Vector3(-1f, 1f, 1f);

            foreach (var pair in Frames)
            {
                Frame f = pair.Value;
                GameObject prefab = Resources.Load<GameObject>(f.Resource);

                if (prefab == null)
                {
                    Debug.LogWarning($"[QingheAnimator] 读不到 Resources/{f.Resource}");
                    continue;
                }

                GameObject go = Instantiate(prefab, _facingRoot);
                go.name = pair.Key;

                foreach (Camera cam in go.GetComponentsInChildren<Camera>(true)) Destroy(cam);
                foreach (Collider2D col in go.GetComponentsInChildren<Collider2D>(true)) Destroy(col);

                var sr = go.GetComponentInChildren<SpriteRenderer>(true);

                if (sr != null)
                {
                    sr.sortingOrder = 30;
                }

                // 画布 1 单位 → 该段画布实际占多少世界单位
                float k = f.Cell * _pxToWorld;
                go.transform.localScale = new Vector3(k, k, 1f);
                go.transform.localPosition = new Vector3(0f, feetOffset, 0f);
                go.SetActive(false);
                _instances[pair.Key] = go;
            }

            Show("Idle");
            AlignFeetToCollider();
        }

        /// <summary>
        /// 用待机帧的**内容底边**对齐碰撞盒底边，并把同一个位移套给所有动作。
        ///
        /// 不能直接用 transform 原点：碰撞盒的 offset 不一定在原点
        /// （青禾的碰撞盒底边比节点低 12 单位），只按原点摆会整体陷进地面。
        /// </summary>
        private void AlignFeetToCollider()
        {
            if (_collider == null || !_instances.TryGetValue("Idle", out GameObject idle))
            {
                return;
            }

            var sr = idle.GetComponentInChildren<SpriteRenderer>(true);

            if (sr == null || sr.sprite == null)
            {
                return;
            }

            float contentBottomWorld = sr.bounds.min.y + Frames["Idle"].Bottom * _pxToWorld;
            float delta = _collider.bounds.min.y - contentBottomWorld;

            if (Mathf.Abs(delta) < 0.01f)
            {
                return;
            }

            foreach (var pair in _instances)
            {
                Vector3 p = pair.Value.transform.localPosition;
                pair.Value.transform.localPosition = new Vector3(p.x, p.y + delta, p.z);
            }
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

        /// <summary>战斗侧调用：映射剑招；没有对应动作就保持当前状态（不播）</summary>
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
