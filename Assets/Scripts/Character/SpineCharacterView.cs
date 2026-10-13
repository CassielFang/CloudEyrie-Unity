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
        [Tooltip("起播的动画名。青禾目前只有 idle_breath")]
        [SerializeField] private string idleAnimation = "idle_breath";

        [SerializeField] private bool loop = true;

        private SkeletonAnimation _spine;

        private void Start()
        {
            _spine = GetComponent<SkeletonAnimation>();

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

            if (string.IsNullOrEmpty(idleAnimation))
            {
                return;
            }

            _spine.loop = loop;
            _spine.AnimationName = idleAnimation;

            GameLog.I($"[SpineCharacterView] 起播 {idleAnimation}（loop={loop}）");
        }
    }
}
