using System;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 青禾的**帧序列立绘**：按形态与运动状态选片段，交给 <see cref="SequencePlayer"/> 播。
    ///
    /// <b>为什么不再用 Spine</b>
    ///
    /// 程序化 IK 骨骼动画解决不了长袍遮腿、脚步滑动，而且每加一个动作都要重调一遍 IK 参数。
    /// 现在所有动作由本地 ComfyUI 生成、`tools/pack_seq_atlas.py` 打包成
    /// `Resources/video_fx/青禾-<动作>/`（manifest + page_NN.png），与引擎里既有的
    /// 小怪 / 九尾狐序列帧**同一套格式**，播放器也是同一个。
    ///
    /// <b>它负责什么</b>
    ///
    /// <list type="bullet">
    ///   <item>按 <see cref="CompanionForm"/> 与刚体速度挑片段（待机 / 行走 / 奔跑 / 起跳 / 下落 /
    ///     灵雾飞行 / 灵合悬浮）；</item>
    ///   <item>给战斗与形态切换用的 <see cref="PlayOneShot"/>：一次性播完再回到自动选择；</item>
    ///   <item>尺寸与落地：按 <see cref="charHeightInCell"/> 反算缩放，让**人物**高度等于
    ///     `artHeight × bodyScale`，并把**脚底**对齐碰撞体底边。</item>
    /// </list>
    ///
    /// <b>为什么尺寸要在这里算</b>
    ///
    /// `QingheController.FitArt` 与 `TryFitSpineArt` 都是按「整张画布的包围盒」缩放的，
    /// 而序列帧的画布是 384 的格子、人物只占中间 232 —— 交给它们会把**格子**当成人，
    /// 结果人物只有应有高度的 60%。所以那两个方法在检测到本组件时直接跳过（见控制器里的判断）。
    /// </summary>
    [RequireComponent(typeof(SequencePlayer))]
    public class SpriteSequenceView : MonoBehaviour
    {
        [Header("数据源")]
        [Tooltip("留空则从父节点找 QingheController")]
        [SerializeField] private QingheController owner;

        [Header("片段名（Resources/video_fx 下的目录名）")]
        [SerializeField] private string idleClip = "青禾-待机";
        [SerializeField] private string walkClip = "青禾-行走";
        [SerializeField] private string runClip = "青禾-奔跑";
        [SerializeField] private string jumpClip = "青禾-起跳";
        [SerializeField] private string fallClip = "青禾-下落";
        [SerializeField] private string mistClip = "青禾-灵雾飞行";
        [SerializeField] private string mergeClip = "青禾-灵合悬浮";

        [Header("判定")]
        [Tooltip("低于这个水平速度算站立")]
        [SerializeField] private float walkThreshold = 12f;
        [Tooltip("高于这个水平速度切奔跑")]
        [SerializeField] private float runThreshold = 200f;

        [Header("尺寸与落地")]
        [Tooltip("格子边长（manifest 的 canvas）。打包脚本默认 384")]
        [SerializeField] private float cellSize = 384f;
        [Tooltip("人物在格子里占的高度（像素）。打包脚本按 --char-height 232 摆的")]
        [SerializeField] private float charHeightInCell = 232f;
        [Tooltip("脚底距格子底边的留白（像素），打包脚本 feet-y 默认 canvas-24")]
        [SerializeField] private float feetMargin = 24f;

        private SequencePlayer _player;
        private Rigidbody2D _body;
        private BoxCollider2D _collider;
        private SpriteRenderer _renderer;

        /// <summary>切片段时的交叉淡化时长（秒）。0 = 硬切</summary>
        [SerializeField] private float crossfadeSeconds = 0.1f;

        /// <summary>落地时的过渡片段；留空则不插</summary>
        [SerializeField] private string landClip = "青禾-落地";

        /// <summary>正在淡出的旧画面。切片段时把上一帧复制一份留在这里，淡掉。</summary>
        private readonly System.Collections.Generic.List<SpriteRenderer> _ghosts =
            new System.Collections.Generic.List<SpriteRenderer>();

        /// <summary>上一帧是否着地 —— 用来取「落地」那一下</summary>
        private bool _wasGrounded = true;

        /// <summary>当前正在播的片段名；null = 还没有播过</summary>
        private string _current;

        /// <summary>一次性动作播完之前，不要被自动选择打断</summary>
        private bool _oneShot;

        /// <summary>按住类动作（格挡）的片段名；null = 没有保持</summary>
        private string _holdClip;

        private void Awake()
        {
            _player = GetComponent<SequencePlayer>();
            _renderer = GetComponent<SpriteRenderer>();

            if (owner == null)
            {
                owner = GetComponentInParent<QingheController>();
            }

            if (owner != null)
            {
                _body = owner.GetComponent<Rigidbody2D>();
                _collider = owner.GetComponent<BoxCollider2D>();
            }

            if (_player == null)
            {
                Debug.LogError("[SpriteSequenceView] 缺少 SequencePlayer");
                enabled = false;
            }
        }

        private void Start()
        {
            FitToCollider();
            Play(idleClip, true);
        }

        /// <summary>
        /// 让**人物**（不是格子）达到 `artHeight × bodyScale`，脚底贴碰撞体底边。
        /// 参照量：格子 384、人物 232、脚底距底边 24（都来自打包脚本的默认值）。
        /// </summary>
        private void FitToCollider()
        {
            if (owner == null || _collider == null || charHeightInCell <= 0f)
            {
                return;
            }

            float targetFigureH = owner.FigureHeight;
            float k = targetFigureH / charHeightInCell;

            transform.localScale = new Vector3(k, k, 1f);

            // 精灵 pivot 是格子中心；脚底在中心下方 (cellSize/2 - feetMargin)
            float feetBelowCenter = cellSize * 0.5f - feetMargin;
            float colliderBottom = _collider.offset.y - _collider.size.y * 0.5f;

            transform.localPosition = new Vector3(0f, colliderBottom + feetBelowCenter * k, 0f);

            GameLog.I($"[SpriteSequenceView] 人物高={targetFigureH:F0} 缩放={k:F3} " +
                      $"脚底对齐 y={colliderBottom + feetBelowCenter * k:F1}");
        }

        private void Update()
        {
            FadeGhosts();

            CheckLanding();

            if (_oneShot)
            {
                return;
            }

            // 保持类动作（格挡）优先于自动选择：按住期间一直播这一条，
            // 松手由 ClearHold() 交还给移动状态判断。
            if (_holdClip != null)
            {
                Play(_holdClip, true);
                return;
            }

            // 空中：起跳由 CheckGroundTransitions 以**一次性**动作触发，这里只负责下落循环。
            // 上升阶段起跳动作播完后保持最后一帧（不再重播），落差由落地的过渡吃掉。
            if (owner != null && owner.Context != null && !owner.Context.Grounded)
            {
                if (_body != null && _body.linearVelocity.y < -8f)
                {
                    Play(fallClip, true);
                }

                return;
            }

            Play(ChooseClip(), true);
        }

        /// <summary>
        /// 落地的一瞬插一段「落地」。
        ///
        /// 这是用户提的「跳跃结束之后怎么衔接」的正面解：光靠 jitter 的速度阈值切回待机，
        /// 会从"空中姿势"直接跳到"站着"，落差大。落地动作有屈膝缓冲，正好把落差吃掉。
        /// </summary>
        private void CheckLanding()
        {
            if (owner == null || owner.Context == null)
            {
                return;
            }

            bool grounded = owner.Context.Grounded;

            if (grounded && !_wasGrounded && !string.IsNullOrEmpty(landClip))
            {
                // 落地时先清掉落地瞬间的保持状态（比如空中一直按着格挡）
                _holdClip = null;
                PlayOneShot(landClip);
            }
            else if (!grounded && _wasGrounded && !string.IsNullOrEmpty(jumpClip))
            {
                // 起跳：一次性播完就停在最后一帧，等下落循环接手
                PlayOneShot(jumpClip);
            }

            _wasGrounded = grounded;
        }

        /// <summary>把上一帧复制成一份"残影"，淡出后销毁。</summary>
        private void SpawnGhost()
        {
            if (_renderer == null || _renderer.sprite == null || crossfadeSeconds <= 0f)
            {
                return;
            }

            var go = new GameObject("FxCrossfade");
            go.transform.SetParent(transform.parent, false);
            go.transform.localPosition = transform.localPosition;
            go.transform.localScale = transform.localScale;
            go.transform.localRotation = transform.localRotation;
            go.transform.SetParent(transform.parent, true);

            SpriteRenderer ghost = go.AddComponent<SpriteRenderer>();
            ghost.sprite = _renderer.sprite;
            ghost.color = _renderer.color;
            ghost.sortingLayerName = _renderer.sortingLayerName;
            ghost.sortingOrder = _renderer.sortingOrder - 1;

            _ghosts.Add(ghost);
        }

        private void FadeGhosts()
        {
            for (int i = _ghosts.Count - 1; i >= 0; i--)
            {
                SpriteRenderer g = _ghosts[i];

                if (g == null)
                {
                    _ghosts.RemoveAt(i);
                    continue;
                }

                Color c = g.color;
                c.a -= Time.deltaTime / Mathf.Max(0.01f, crossfadeSeconds);

                if (c.a <= 0f)
                {
                    Destroy(g.gameObject);
                    _ghosts.RemoveAt(i);
                }
                else
                {
                    g.color = c;
                }
            }
        }

        private string ChooseClip()
        {
            if (owner != null)
            {
                if (owner.CurrentForm == CompanionForm.Mist)
                {
                    return mistClip;
                }

                if (owner.CurrentForm == CompanionForm.Merge)
                {
                    return mergeClip;
                }
            }

            if (_body == null)
            {
                return idleClip;
            }

            float speed = Mathf.Abs(_body.linearVelocity.x);

            if (speed >= runThreshold)
            {
                return runClip;
            }

            return speed >= walkThreshold ? walkClip : idleClip;
        }

        private void Play(string clip, bool loop)
        {
            if (string.IsNullOrEmpty(clip) || clip == _current)
            {
                return;
            }

            SpawnGhost();
            _current = clip;
            _player.Play(clip, loop);
        }

        /// <summary>
        /// 播一次动作（轻击 / 重击 / 踏云闪 / 受击 / 形态切换…），播完自动回到状态选择。
        /// <paramref name="onComplete"/> 在播完时调用一次。
        /// </summary>
        public void PlayOneShot(string clip, float timeScale = 1f, Action onComplete = null)
        {
            if (string.IsNullOrEmpty(clip))
            {
                return;
            }

            _holdClip = null;          // 出招打断保持（例如格挡中被击中）
            _oneShot = true;
            SpawnGhost();
            _current = clip;

            _player.Play(clip, false, 0, -1, timeScale, () =>
            {
                _oneShot = false;
                _current = null;          // 让下一帧的自动选择重新起播
                if (onComplete != null)
                {
                    onComplete();
                }
            });
        }

        /// <summary>
        /// 进入「保持」状态：一直循环播这条，直到 <see cref="ClearHold"/>。
        /// 格挡这类按住才成立的动作用它 —— 用 PlayOneShot 会播一遍就回待机，
        /// 表现成「挡了一下就把剑放下」。
        /// </summary>
        public void PlayHold(string clip)
        {
            if (string.IsNullOrEmpty(clip))
            {
                return;
            }

            _oneShot = false;
            _holdClip = clip;
        }

        /// <summary>退出保持，交还给按速度/形态的自动选择。</summary>
        public void ClearHold()
        {
            _holdClip = null;
        }

        /// <summary>给战斗层读的当前片段名（调试用）</summary>
        public string CurrentClip
        {
            get { return _current; }
        }
    }
}
