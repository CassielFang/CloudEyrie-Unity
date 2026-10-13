using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace CloudEyrie
{
    /// <summary>
    /// 全局返回键：**任意场景**按 ESC 回首页菜单（<see cref="GameScenes.Title"/>）。
    ///
    /// 由 2026-10-07 的需求而来：「让 esc 在任意位置都可以返回主题菜单页面」。
    ///
    /// <b>为什么要单独一个全局件，而不是塞进各场景的脚本里</b>
    ///
    /// 首页的面板返回归 <see cref="TitleScreen"/>（它按 <c>_stage</c> 分层处理），
    /// 而「关卡 / 结局 / 引导」这几处**互不相关的场景**都要同一件事。
    /// 逐个场景挂就会漏 —— 漏了的场景表现是「在别处按 ESC 没反应」，
    /// 不报错、也很难发现是漏挂。所以做成一个 <c>DontDestroyOnLoad</c> 的常驻件，
    /// 跟 <see cref="ScreenDimmer"/> 同一套办法（那也是一件「哪个场景都得在」的东西），
    /// 也放在 <c>Ui/</c> 下 —— 它读的是 <see cref="ScreenStage.PanelBackKey"/>，
    /// 放 Core 会变成 Core→Ui 的反向依赖。
    ///
    /// <b>三条不处理的例外</b>
    /// <list type="bullet">
    ///   <item><b>已经在首页</b>：那是 <see cref="TitleScreen"/> 的地盘 ——
    ///     面板开着时 ESC 是「关面板」，不该再叠一层「回首页」（那等于原地重载场景）。</item>
    ///   <item><b>演出 / 转场中</b>（<see cref="GameFlow.Active"/>）：那时
    ///     <c>timeScale = 0</c>、画面正在黑，此刻切场景会把转场打断在半路。
    ///     与 <see cref="QingheController"/> 演出期间不收输入是同一条理由。</item>
    ///   <item><b>没在播放时</b>：常驻件在编辑态也可能存在，顺手挡一道。</item>
    /// </list>
    ///
    /// ⚠️ <b>这是「丢进度」的操作</b>：关卡里按 ESC 会直接放弃当前这一局回到首页，
    /// 没有二次确认。这是需求方明确要的行为（图快）；以后若觉得误触严重，
    /// 加确认的地方就在这里。
    /// </summary>
    public static class GlobalBackKey
    {
        /// <summary>
        /// 装一次常驻监听。
        ///
        /// 与 <see cref="ScreenDimmer.Install"/> 同款：<c>AfterSceneLoad</c> 保证
        /// 每个场景起来时它都在，<c>DontDestroyOnLoad</c> 让它跨场景活下来。
        /// 幂等 —— 编辑器里域重载会重跑，靠 <c>_installed</c> 挡住重复建节点。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_installed)
            {
                return;
            }

            _installed = true;

            GameObject go = new GameObject("GlobalBackKey");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Watcher>();
        }

        private static bool _installed;

        /// <summary>真正干活的组件。不对外用 —— 只由 <see cref="Install"/> 挂。</summary>
        private class Watcher : MonoBehaviour
        {
            private void Update()
            {
                if (!Application.isPlaying)
                {
                    return;
                }

                Keyboard kb = Keyboard.current;

                if (kb == null || !kb[ScreenStage.PanelBackKey].wasPressedThisFrame)
                {
                    return;
                }

                // 转场 / 演出中不打断（见类注释）
                if (GameFlow.Active)
                {
                    return;
                }

                // 首页交给 TitleScreen（面板开着时 ESC 是关面板；没开面板时按了也没事）
                if (SceneManager.GetActiveScene().name == GameScenes.Title)
                {
                    return;
                }

                GameLog.I("[GlobalBackKey] ESC → 返回首页（直接落在菜单）");

                // 置位让首页跳过「按任意键继续」。**必须在 Load 之前** ——
                // 切场景是异步的，Load 之后首页的 Awake 可能已经跑过了。
                // 见 TitleScreen.StartAtMenu 的说明。
                TitleScreen.StartAtMenu = true;

                // 走 LevelHost 是为了**经过复位协议** —— 裸 LoadScene 会把灵炁值、
                // 战斗来源集合、timeScale 这些跨场景静态状态一路带进标题页
                if (LevelHost.Instance != null)
                {
                    LevelHost.Instance.ReturnToTitle();
                }
                else
                {
                    // 不在关卡里（已经在标题页了）：没有宿主可走，直接切
                    GameSceneManager.LoadSingle(GameScenes.Title);
                }
            }
        }
    }
}
