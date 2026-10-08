using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 统一日志出口。
    ///
    /// <b>为什么要有它</b>
    ///
    /// <c>Debug.Log</c> 会抓调用堆栈，在编辑器里**很贵**。本项目从 Cocos 搬过来的日志密度
    /// 不低 —— 每次命中、射箭、受击、落地都打一条 —— 战斗一密集就是可感的掉帧。
    ///
    /// （Cocos 那边也已经吃过这个亏：`GameManager` 曾挂了 `spirit-energy-changed` 监听打日志，
    ///   而灵炁逐帧变化，把控制台刷满。见 <c>docs/计划-代码对照.md</c>。）
    ///
    /// <b>用法</b>
    ///
    /// 把「正常不需要看」的高频日志改成 <see cref="Verbose"/>，默认不输出；
    /// 排查问题时把 <see cref="Level"/> 调高即可，**不用改代码、不用重新编译**。
    /// 错误与警告照常输出。<b>凡是每帧或每次命中都会经过的地方，一律用 Verbose。</b>
    /// </summary>
    public static class GameLog
    {
        public enum Verbosity
        {
            /// <summary>只留警告与错误</summary>
            Quiet = 0,

            /// <summary>重要状态变化（默认）—— 段落推进、形态切换、Boss 阶段…</summary>
            Normal = 1,

            /// <summary>高频日志 —— 命中、射箭、落地、接触…</summary>
            Verbose = 2,
        }

        /// <summary>
        /// 当前日志档位。运行时在 Inspector 里改不了（静态字段），
        /// 但可以在控制台或自测脚本里一行改掉。
        /// </summary>
        public static Verbosity Level = Verbosity.Normal;

        /// <summary>高频日志：默认不输出。</summary>
        public static void V(string message)
        {
            if (Level >= Verbosity.Verbose)
            {
                Debug.Log(message);
            }
        }

        /// <summary>常规日志。</summary>
        public static void I(string message)
        {
            if (Level >= Verbosity.Normal)
            {
                Debug.Log(message);
            }
        }

        /// <summary>警告：始终输出。</summary>
        public static void W(string message)
        {
            Debug.LogWarning(message);
        }

        /// <summary>错误：始终输出。</summary>
        public static void E(string message)
        {
            Debug.LogError(message);
        }
    }
}
