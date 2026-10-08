using UnityEngine.SceneManagement;

namespace CloudEyrie
{
    /// <summary>
    /// 对 <see cref="SceneManager.LoadScene(string)"/> 的薄封装
    /// —— 对应 Cocos 侧 <c>core/SceneManager.ts</c>（模块级单例 <c>sceneManager</c>）。
    ///
    /// 类名加了 <c>Game</c> 前缀，是为了避开 <c>UnityEngine.SceneManagement.SceneManager</c>
    /// 这个同名类型 —— 不加的话每个调用点都得写全限定名。
    ///
    /// 场景名常量见 <see cref="GameScenes"/>。
    /// </summary>
    public static class GameSceneManager
    {
        public static void Load(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
        }
    }

    /// <summary>
    /// 场景名常量。对应 Cocos 侧两个场景文件（<c>Title.scene</c> / <c>main.scene</c>）。
    /// </summary>
    public static class GameScenes
    {
        /// <summary>首页。</summary>
        public const string Title = "Title";

        /// <summary>关卡（含常驻 GameManager 与 Canvas）。</summary>
        public const string Main = "Main";

        /// <summary>结局页。</summary>
        public const string Ending = "Ending";
    }
}
