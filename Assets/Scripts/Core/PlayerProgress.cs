using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 一周目内的收集进度：碎片与五行印记。
    ///
    /// <b>为什么是静态的而不是挂在某个节点上</b>
    ///
    /// 关卡是**可插拔**的：碎片所在的关卡场景会被卸载重载，印记是在不同关卡里拿的。
    /// 进度必须活过场景卸载，所以它不能住在任何场景对象上 ——
    /// 而本作没有「玩家存档」这一层，一周目内的进度用静态最直接。
    ///
    /// ⚠️ **换关时必须显式清空**（见 <see cref="ResetProtocol"/>）——
    /// 静态状态跨场景活着，从标题重新开始时不清就会带着上一次的收集进度。
    /// 这与灵炁值是同一类问题，别只清一半。
    ///
    /// （跨周目的落盘存档还没做：设计上要存「已通关哪些境」，等流程打通用再补。）
    /// </summary>
    public static class PlayerProgress
    {
        private static readonly HashSet<string> Fragments = new HashSet<string>();
        private static readonly HashSet<ElementKind> Seals = new HashSet<ElementKind>();

        /// <summary>已收集的碎片数。</summary>
        public static int FragmentCount
        {
            get { return Fragments.Count; }
        }

        /// <summary>已获得的印记数（五行 + 祭坛，最多 5 枚有效）。</summary>
        public static int SealCount
        {
            get { return Seals.Count; }
        }

        public static bool HasFragment(string id)
        {
            return !string.IsNullOrEmpty(id) && Fragments.Contains(id);
        }

        public static bool HasSeal(ElementKind element)
        {
            return Seals.Contains(element);
        }

        /// <summary>收下一枚碎片。返回 false 表示之前已经拿过（不必再播表现）。</summary>
        public static bool CollectFragment(string id)
        {
            if (string.IsNullOrEmpty(id) || !Fragments.Add(id))
            {
                return false;
            }

            EventBus.Emit(GameEvents.FragmentCollected);
            return true;
        }

        /// <summary>获得一枚印记。返回 false 表示已有。</summary>
        public static bool GrantSeal(ElementKind element)
        {
            if (!Seals.Add(element))
            {
                return false;
            }

            EventBus.Emit(GameEvents.SealGranted);
            return true;
        }

        /// <summary>清空全部进度。由 <see cref="ResetProtocol"/> 在离开关卡时调用。</summary>
        public static void ResetAll()
        {
            Fragments.Clear();
            Seals.Clear();
        }
    }
}
