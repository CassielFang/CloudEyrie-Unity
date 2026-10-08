using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 全局流程闸门：演出（过场 / 阶段转场）期间把游戏"停住"。
    ///
    /// <b>为什么用 <c>Time.timeScale = 0</c> 而不是给每个 Update 加开关</b>
    ///
    /// 「动画后面的游戏逻辑停止」要求敌人、Boss、狐火、浊湮、灵炁恢复、玩家物理**全部**停下来。
    /// 逐个加 <c>if (cutscene) return;</c> 要改六七个文件，而且**将来新增任何一个系统都会漏**——
    /// 漏掉的那个会在过场里继续跑，症状是「转场时小怪还在追我」，很难查。
    ///
    /// <c>timeScale = 0</c> 一次性覆盖所有按 <c>Time.deltaTime</c> 推进的逻辑，包括
    /// <c>FixedUpdate</c>/物理（Unity 在 timeScale 为 0 时不再步进物理）。
    ///
    /// 代价是**演出自身也不能用 <c>Time.deltaTime</c>** —— <see cref="StageTransition"/>
    /// 和它那个 <see cref="SequencePlayer"/> 都改用 <c>unscaledDeltaTime</c>（见
    /// <see cref="Push"/> 的调用方）。
    ///
    /// <b>为什么还要单独闸输入</b>
    ///
    /// <c>timeScale = 0</c> 让移动量变成 0，但**攻击、格挡是按键当帧触发的瞬时动作**，
    /// 不依赖 dt。不管的话玩家能在全屏过场里挥剑、把敌人打死。
    /// 所以 <see cref="QingheController"/> 额外看 <see cref="Active"/>。
    ///
    /// 用**计数**而不是布尔：理论上可能嵌套（过场里再触发一次转场），
    /// 布尔会被内层提前清成 false，timeScale 就卡在 0 再也起不来。
    /// </summary>
    public static class GameFlow
    {
        private static int _cutsceneDepth;

        /// <summary>是否正在演出。演出期间玩家不接收输入。</summary>
        public static bool Active
        {
            get { return _cutsceneDepth > 0; }
        }

        /// <summary>进入演出：暂停时间推进。</summary>
        public static void Push()
        {
            _cutsceneDepth++;

            if (_cutsceneDepth == 1)
            {
                Time.timeScale = 0f;
            }
        }

        /// <summary>退出演出：恢复时间推进。</summary>
        public static void Pop()
        {
            if (_cutsceneDepth <= 0)
            {
                _cutsceneDepth = 0;
                return;
            }

            _cutsceneDepth--;

            if (_cutsceneDepth == 0)
            {
                Time.timeScale = 1f;
            }
        }

        /// <summary>
        /// 强制复位。<b>只给防御性场景用</b>（场景卸载、组件被销毁）。
        ///
        /// 演出没正常收尾就把 timeScale 永久留在 0，游戏看起来"死了"——
        /// 这种状态极难从现象反推原因，所以宁可无条件恢复。
        /// </summary>
        public static void ForceReset()
        {
            _cutsceneDepth = 0;
            Time.timeScale = 1f;
        }
    }
}
