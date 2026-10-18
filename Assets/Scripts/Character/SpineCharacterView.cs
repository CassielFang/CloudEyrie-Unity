using Spine.Unity;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 用 Spine 骨骼渲染角色立绘时的「播哪条动画」入口。
    ///
    /// <b>为什么需要它</b>：spine-unity **3.8** 的 <c>SkeletonAnimation</c> 只暴露
    /// <c>AnimationName</c> 这个**属性**（setter 内部调 <c>state.SetAnimation</c>），
    /// 而 <c>state</c> 是 <c>Awake</c> 里初始化出来的 ——
    /// **没有 <c>startingAnimation</c> 这种可以在 Inspector 里预先填好的字段**
    /// （那是 4.x 才有的）。所以起播必须在运行时做一次。
    ///
    /// 青禾目前只有一条 <c>idle_breath</c>（待机呼吸）。等美术接入走路 / 跳跃 / 攻击之后，
    /// 形态与动作的切换应该都收敛到这个组件里 —— 它现在只是个起点，
    /// 但**位置是对的**：所有「骨骼播什么」的判断都从这一处走。
    /// </summary>
    [RequireComponent(typeof(SkeletonAnimation))]
    public class SpineCharacterView : MonoBehaviour
    {
        [Tooltip("待机动画")]
        [SerializeField] private string idleAnimation = "idle_breath";

        [Tooltip("在地面移动时的动画")]
        [SerializeField] private string runAnimation = "run";

        [Tooltip("上升 / 起跳时的动画")]
        [SerializeField] private string jumpAnimation = "jump";

        [Tooltip("下落时的动画")]
        [SerializeField] private string fallAnimation = "fall";

        [Tooltip("速度超过它才算『在走』（世界单位/秒）")]
        [SerializeField] private float moveThreshold = 12f;

        [SerializeField] private bool loop = true;

        private SkeletonAnimation _spine;
        private QingheController _player;
        private Rigidbody2D _body;
        private string _current;

        private void Start()
        {
            _spine = GetComponent<SkeletonAnimation>();
            _player = GetComponentInParent<QingheController>();
            _body = _player != null ? _player.GetComponent<Rigidbody2D>() : null;

            if (_spine == null)
            {
                return;
            }

            // Spine 的数据是懒加载的（Awake/OnEnable 里初始化）。
            // 这里等一下让它就绪，再起播 —— 直接设 AnimationName 在未初始化时会静默无效。
            if (_spine.skeletonDataAsset == null)
            {
                Debug.LogWarning("[SpineCharacterView] 没有指定 SkeletonDataAsset，立绘不会显示");
                return;
            }

            Play(idleAnimation, loop);

            GameLog.I($"[SpineCharacterView] 起播 {idleAnimation}（loop={loop}）");
        }

        private void Update()
        {
            if (_spine == null || _player == null || _body == null)
            {
                return;
            }

            FormContext ctx = _player.Context;

            if (ctx == null)
            {
                return;
            }

            // 跑/跳/落的骨骼动作只属于实体形态。灵雾是飞行、灵合是悬浮，
            // 让它们播"两条腿在跑"会非常违和 —— 那两个形态靠 FormAura / 拖尾表达动感。
            if (_player.CurrentForm != CompanionForm.Entity)
            {
                Play(idleAnimation, true);
                return;
            }

            string want;

            if (ctx.Grounded)
            {
                want = Mathf.Abs(_body.linearVelocity.x) > moveThreshold ? runAnimation : idleAnimation;
            }
            else if (_body.linearVelocity.y > 1f)
            {
                want = jumpAnimation;
            }
            else
            {
                want = fallAnimation;
            }

            // 只有循环的动画（跑 / 落 / 待机）循环；起跳是一次性的
            bool wantLoop = want != jumpAnimation;
            Play(want, wantLoop);
        }

        /// <summary>切动画。同名不重播 —— 每帧 SetAnimation 会把动画一直卡在第一帧。</summary>
        private void Play(string name, bool shouldLoop)
        {
            if (string.IsNullOrEmpty(name) || name == _current)
            {
                return;
            }

            _current = name;
            _spine.loop = shouldLoop;
            _spine.AnimationName = name;
        }
    }
}
