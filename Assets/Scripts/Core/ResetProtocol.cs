using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 关卡装卸时的**静态状态复位协议**。
    ///
    /// <b>为什么需要它</b>
    ///
    /// 本作有若干「跨场景活着」的静态状态（灵炁值、战斗来源集合、演出闸门计数…）。
    /// 这些状态**在换关时不会被自动清掉**，于是会出现一类极难查的 bug：
    /// 症状离现场很远、没有报错、看画面也看不出来。
    ///
    /// 已经发生过 / 已存在隐患的三处：
    /// <list type="bullet">
    ///   <item><c>SpiritEnergySystem._current</c>（灵炁值）换关带走 —— 从标题进关卡就已经是这样。</item>
    ///   <item><c>SpiritEnergySystem</c> 的战斗来源集合换关不清，抓住已销毁的敌人引用；
    ///     而<b>唯一能清它的 <see cref="SpiritEnergySystem.ResetAll"/> 从来没被调用过</b>。</item>
    ///   <item><c>GameFlow._cutsceneDepth</c> / <c>Time.timeScale</c> 没有场景级兜底 ——
    ///     在过场里切关会让 timeScale <b>永久卡在 0</b>，游戏看起来"死了"。</item>
    /// </list>
    ///
    /// <b>顺序本身就是正确性的一部分</b>，别调换。理由逐条写在方法里。
    ///
    /// ⚠️ <b>接线时机</b>：本类先随「关卡模组化」的 P0 阶段落地（只加不接线），
    /// 由 P1 的 <c>LevelHost</c> 在换关流程里调用。它<b>不是死代码</b> ——
    /// 接线的同时会有一条 PlayMode 测试（<c>LevelSwapStateTests</c>）盯着它。
    /// </summary>
    public static class ResetProtocol
    {
        /// <summary>
        /// 卸载旧关卡**之前**调用。顺序有理由，别调换。
        /// </summary>
        public static void BeforeLevelUnload()
        {
            // 1) 时钟必须第一个放回去。
            //    两个理由：后面任何靠 Time.deltaTime 的收尾动作（若 timeScale 还是 0）都不会跑；
            //    而且「在过场里切关」正是会把 timeScale 永久留在 0 的那条路径
            //    （GameFlow.ForceReset 的注释记的也是这件事）。
            GameFlow.ForceReset();

            // 2) 再清灵炁与战斗态。
            //    ResetAll() 是**唯一**会清 CombatSources 的地方 —— 这一步漏掉的话，
            //    旧敌人对象不会被 GC，且灵炁永久停在「战斗中」那档恢复（1/秒）。
            SpiritEnergySystem.ResetAll();

            // 3) 收集进度。碎片与印记也是静态的（要活过场景卸载），
            //    同样必须在离开关卡时清掉 —— 否则从标题重新开始会带着上一次的收集。
            //    ⚠️ 与灵炁值是同一类问题，别只清一半。
            PlayerProgress.ResetAll();

            // 4) 输入层。沿用 GameManager.OnSceneLoaded 已有的做法，
            //    清掉当帧消费标记与改键捕获态，免得新关第一帧读到旧输入。
            InputSystem.Reset();

            // 4) 关卡级静态表（灵泉坐标等）的清理。
            //    ⚠️ 现在由 LevelDirector.BuildLevel 自己清（它每关重建）。
            //    等 P4 把 LevelDirector 退役、换成 SpringRegistry 之后，那一步挪到这里 ——
            //    届时的理由：FogFire 每帧直接读这张表，清晚一帧就会读到上一关的坐标。
        }

        /// <summary>
        /// 新关卡加载完成**之后**调用。
        /// </summary>
        /// <param name="carryPlayerState">
        /// 是否把玩家的灵炁带进本关。
        /// <c>false</c>（默认）= 重置为初始值；<c>true</c> = 保留当前值（用于「Boss 关 → 祭坛」这类连续战斗）。
        /// </param>
        public static void AfterLevelLoad(bool carryPlayerState = false)
        {
            // 双保险：BeforeLevelUnload 已经复位过一次，这里再来一次。
            // 因为「加载」本身可能触发别的东西 Push 演出（转场、开场），
            // 而我们要保证**交给玩家的第一帧**一定是能动的。
            GameFlow.ForceReset();
            Time.timeScale = 1f;

            if (!carryPlayerState)
            {
                SpiritEnergySystem.ResetToInitial();
            }

            // 这一条断言值钱在：它把「timeScale 卡死」从一个"看起来游戏死了、但查不出原因"
            // 的现象，变成一条带调用栈的报错。
            Debug.Assert(Time.timeScale > 0f,
                "[ResetProtocol] 关卡加载完成后 timeScale 仍为 0 —— 演出闸门没有正确收尾。");
        }

        /// <summary>
        /// 回标题 / 回结局这类**离开关卡**的路径。
        ///
        /// 现状：<c>GlobalBackKey</c> 与 <c>BossAI</c> 都是裸调
        /// <c>GameSceneManager.Load(...)</c>，绕过了复位 —— 静态残留会一路带进标题页。
        /// </summary>
        public static void OnLeaveLevel()
        {
            BeforeLevelUnload();
        }
    }
}
