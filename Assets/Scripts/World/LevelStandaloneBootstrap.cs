using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 让**关卡场景可以单独按 Play 试玩**。
    ///
    /// <b>为什么需要它</b>
    ///
    /// 关卡模组化之后，玩家 / 相机 / HUD / 背景都住在常驻的 <c>Host</c> 场景里。
    /// 于是「打开某一关的场景、按 Play」——**关卡策划最自然的操作** ——
    /// 会得到一个没有角色、没有相机、什么也动不了的世界，而且不报任何错。
    ///
    /// 这个组件解决的就是这件事：场景里没有宿主，就自己把 <c>Host</c> 加性加载进来。
    ///
    /// <b>为什么是"在关卡场景里补宿主"，而不是"让 Host 去认领关卡"</b>
    ///
    /// 两个方向都能做，差别在**谁在什么时候知道什么**：
    /// 关卡场景是被动的（它不知道自己会不会被别人加载），而 Host 是主动的。
    /// 让关卡在 <c>Awake</c> 里判断「有没有宿主」是最早、最确定的时机 ——
    /// 那时 Host 若在场就已经在了，不在场就肯定是要独立跑。
    /// 反过来让 Host 去猜"现在这个关卡场景是不是别人开的"，判断依据要绕远得多。
    ///
    /// <b>正常流程下它什么都不做</b>：关卡是被 <c>LevelHost</c> 加性加载的，
    /// 加载时宿主必然已经在场，<see cref="Awake"/> 第一句就返回。
    /// </summary>
    public class LevelStandaloneBootstrap : MonoBehaviour
    {
        [Tooltip("独立试玩时加载的宿主场景名")]
        [SerializeField] private string hostScene = GameScenes.Host;

        private void Awake()
        {
            // 宿主已经在场 → 这是正常流程（关卡是被 LevelHost 加载的），不插手
            if (LevelHost.Instance != null)
            {
                return;
            }

            if (GameSceneManager.IsLoaded(hostScene))
            {
                // 宿主场景已加载但实例还没起来（Awake 顺序早于 LevelHost.Start）——
                // 这时候也不该再加载一次，LevelHost 起来后会认领本关
                return;
            }

            GameLog.I($"[Standalone] 没有宿主，加性加载 {hostScene} 以便单独试玩本关");

            // 同步加载：必须在任何 Awake 里就能用上玩家与相机，
            // 走异步的话本关的 Start 会先跑完，那时引用还是空的
            UnityEngine.SceneManagement.SceneManager
                .LoadScene(hostScene, UnityEngine.SceneManagement.LoadSceneMode.Additive);
        }
    }
}
