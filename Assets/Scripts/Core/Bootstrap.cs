using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 启动场景的入口：初始化完成后切到首页。
    ///
    /// <b>为什么需要它</b>
    ///
    /// Cocos 那边是「启动场景 = Title」，而 <c>GameManager</c> 挂在 <c>main</c> 场景里、
    /// 靠 <c>addPersistRootNode</c> 跨场景常驻 —— 于是有个口头约定：
    /// **Title.scene 故意不放 GameManager**，两边都放会让 main 的副本自杀。
    ///
    /// Unity 这边用**独立启动场景**把这个约定变成结构：<c>Bootstrap</c> 里只放
    /// <see cref="GameManager"/>（它自己 <c>DontDestroyOnLoad</c>），初始化完再切首页。
    /// 于是 Title 与关卡都不必关心 GameManager 的存在 ——
    /// 语义与 Cocos 的「常驻」一致，但**不再需要「哪个场景放、哪个不放」这种口头约定**。
    ///
    /// 顺序是安全的：Unity 保证**所有 Awake 跑完才跑 Start**，
    /// 所以这里进 Start 时 <see cref="GameManager"/> 已经初始化完毕。
    /// </summary>
    public class Bootstrap : MonoBehaviour
    {
        [Tooltip("启动后要进的场景。留空则用 Title")]
        [SerializeField] private string firstScene = GameScenes.Title;

        private void Start()
        {
            string target = string.IsNullOrEmpty(firstScene) ? GameScenes.Title : firstScene;
            GameLog.I($"[Bootstrap] 初始化完成，进入 {target}");
            GameSceneManager.Load(target);
        }
    }
}
