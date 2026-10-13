using UnityEngine;
using UnityEngine.SceneManagement;

namespace CloudEyrie
{
    /// <summary>
    /// 对 <see cref="SceneManager"/> 的薄封装
    /// —— 对应 Cocos 侧 <c>core/SceneManager.ts</c>（模块级单例 <c>sceneManager</c>）。
    ///
    /// 类名加了 <c>Game</c> 前缀，是为了避开 <c>UnityEngine.SceneManagement.SceneManager</c>
    /// 这个同名类型 —— 不加的话每个调用点都得写全限定名。
    ///
    /// 场景名常量见 <see cref="GameScenes"/>。
    ///
    /// <b>两种加载模式</b>：
    /// <list type="bullet">
    ///   <item><b>Single</b>（<see cref="LoadSingle"/>）—— 卸载其它一切场景。
    ///     首页 / 结局页用这个。</item>
    ///   <item><b>Additive</b>（<see cref="LoadAdditive"/>）—— 叠在现有场景之上。
    ///     关卡模组化靠它：<c>Host</c> 常驻，关卡场景一层层换。</item>
    /// </list>
    /// </summary>
    public static class GameSceneManager
    {
        /// <summary>单场景替换 —— 会卸载其它一切场景。</summary>
        public static void LoadSingle(string sceneName)
        {
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        /// <summary>
        /// 叠加加载一个场景。
        ///
        /// 返回 <see cref="AsyncOperation"/>，调用方**必须** yield 它 ——
        /// 加载是异步的，等它 <c>isDone</c> 之前新场景里的对象都还不存在，
        /// 直接去找会拿到 null 而且不报错。
        /// </summary>
        public static AsyncOperation LoadAdditive(string sceneName)
        {
            return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        }

        /// <summary>卸载一个叠加场景。已卸载 / 从未加载时返回 null（不报错）。</summary>
        public static AsyncOperation UnloadAdditive(string sceneName)
        {
            UnityEngine.SceneManagement.Scene scene = SceneManager.GetSceneByName(sceneName);

            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            return SceneManager.UnloadSceneAsync(scene);
        }

        public static bool IsLoaded(string sceneName)
        {
            UnityEngine.SceneManagement.Scene scene = SceneManager.GetSceneByName(sceneName);
            return scene.IsValid() && scene.isLoaded;
        }
    }

    /// <summary>
    /// 场景名常量。
    ///
    /// <c>Host</c> 是关卡模组化的宿主场景：玩家 / 相机 / 背景 / HUD / 转场都住在这里，
    /// 跨关卡常驻；关卡场景（<c>Assets/Levels/&lt;Id&gt;/&lt;Id&gt;.unity</c>）叠在它上面。
    /// </summary>
    public static class GameScenes
    {
        /// <summary>首页。</summary>
        public const string Title = "Title";

        /// <summary>关卡宿主：玩家、相机、背景、HUD、转场。跨关卡常驻。</summary>
        public const string Host = "Host";

        /// <summary>结局页。</summary>
        public const string Ending = "Ending";

        /// <summary>
        /// 旧的单场景关卡。**已被 Host + 关卡场景取代**，留着只为回退。
        /// </summary>
        public const string LegacyMain = "Main";
    }
}
