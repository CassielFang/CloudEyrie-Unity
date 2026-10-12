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
    /// （那是 4.x 才有的）。所以起播必须在运行时做一次。所有「骨骼播什么」的判断都从这一处走。
    ///
    /// <b>动画从哪来</b>：走路 / 跳跃 / 下落三条是**程序生成**的，不是手绘 ——
    /// 见 <c>tools/gen_spine_anims.py</c>。骨架绑得很完整（108 根骨骼、四肢各一组 IK），
    /// 所以步态靠驱动 IK 目标的足/手轨迹就能出来，不必手算骨骼旋转。
    /// 美术到位后用真动画替换即可（同一个 skeleton、同一批动画名，本组件不用改）。
    /// </summary>
    [RequireComponent(typeof(SkeletonAnimation))]
    public class SpineCharacterView : MonoBehaviour
    {
        [Header("动画名（必须与 tools/gen_spine_anims.py 生成的一致）")]
        [SerializeField] private string idleAnimation = "idle_breath";
        [SerializeField] private string runAnimation = "run";
        [SerializeField] private string jumpAnimation = "jump";
        [SerializeField] private string fallAnimation = "fall";

        [Header("切换阈值")]
        [Tooltip("水平速度超过它才算「在跑」，避免站着时的微小抖动切到跑步")]
        [SerializeField] private float runSpeedThreshold = 12f;

        [Tooltip("垂直速度超过它才算「在跳 / 在落」。取得比水平阈值大，\n" +
                 "因为走在地面时 y 速度会有物理引擎留下的微小残余")]
        [SerializeField] private float airborneSpeedThreshold = 30f;

        [Header("跑步动画与移速同步")]
        [Tooltip("一个 run 周期（一次完整左右步）角色前进多少个世界单位。\n" +
                 "0 = 不同步，永远按原速播（先这样跑通，量准了再填）")]
        [SerializeField] private float runCycleDistance = 0f;

        private SkeletonAnimation _spine;
        private Rigidbody2D _body;
        private string _current;

        private void Start()
        {
            _spine = GetComponent<SkeletonAnimation>();

            if (_spine == null)
            {
                return;
            }

            // Spine 的数据是懒加载的（Awake/OnEnable 里初始化）。
            // 直接设 AnimationName 在未初始化时会静默无效。
            if (_spine.skeletonDataAsset == null)
            {
                GameLog.W("[SpineCharacterView] 没有指定 SkeletonDataAsset，立绘不会显示");
                return;
            }

            // 玩家刚体在父节点上（本组件挂在 Art 子节点）
            _body = GetComponentInParent<Rigidbody2D>();

            if (_body == null)
            {
                GameLog.W("[SpineCharacterView] 父节点上没有 Rigidbody2D，无法按速度切动画，只播待机");
            }

            _spine.loop = true;
            Play(idleAnimation);
        }

        private void Update()
        {
            if (_spine == null || _body == null)
            {
                return;
            }

            // 演出期间不动 —— 过场里角色不该还在跑
            if (GameFlow.Active)
            {
                return;
            }

            Vector2 v = _body.linearVelocity;
            string want;

            // 先判空中：上升是「跳」，下降是「落」。
            // 注意用 |vy| 而不是 v.y > 0 —— 下落时 v.y 是负的。
            if (Mathf.Abs(v.y) > airborneSpeedThreshold)
            {
                want = v.y > 0f ? jumpAnimation : fallAnimation;
            }
            else if (Mathf.Abs(v.x) > runSpeedThreshold)
            {
                want = runAnimation;
                SyncRunSpeed(Mathf.Abs(v.x));
            }
            else
            {
                want = idleAnimation;
            }

            Play(want);
        }

        /// <summary>
        /// 只在与当前不同时才设 —— <c>AnimationName</c> 的 setter 内部会
        /// <c>SetAnimation</c>，每帧设一次等于每帧从头重播（角色会僵在第一帧）。
        /// </summary>
        private void Play(string name)
        {
            if (string.IsNullOrEmpty(name) || name == _current)
            {
                return;
            }

            _current = name;
            _spine.AnimationName = name;
        }

        /// <summary>
        /// 让跑步周期跟得上实际移速，否则脚会「滑步」。
        ///
        /// 一个周期走 <see cref="runCycleDistance"/> 个世界单位、原时长 <c>T</c> 秒，
        /// 那么实际速度 <c>v</c> 时应当把播放速度乘 <c>v·T / runCycleDistance</c>。
        /// </summary>
        private void SyncRunSpeed(float speed)
        {
            if (runCycleDistance <= 0.01f || _spine.AnimationState == null)
            {
                return;
            }

            Spine.Animation anim = _spine.Skeleton != null && _spine.Skeleton.Data != null
                ? _spine.Skeleton.Data.FindAnimation(runAnimation)
                : null;

            if (anim == null || anim.Duration <= 0f)
            {
                return;
            }

            _spine.timeScale = speed * anim.Duration / runCycleDistance;
        }

        private void OnDisable()
        {
            // 复位，免得下次启用时残留一个被拉快的 timeScale
            if (_spine != null)
            {
                _spine.timeScale = 1f;
            }

            _current = null;
        }
    }
}
