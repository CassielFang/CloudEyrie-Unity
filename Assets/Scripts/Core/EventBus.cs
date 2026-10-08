using System;
using System.Collections.Generic;

namespace CloudEyrie
{
    /// <summary>
    /// 发布/订阅事件总线 —— 对应 Cocos 侧 <c>core/EventBus.ts</c>（模块级单例 <c>eventBus</c>）。
    ///
    /// 跨系统解耦统一走它，事件名常量集中在 <see cref="GameEvents"/>。
    ///
    /// ⚠️ <b>订阅必须成对退订</b>。它是静态的、活得比场景久，而订阅方多是普通对象
    /// （没有引擎生命周期，销毁时不会自己找上门）。不摘的回调会在切场景后摸到已销毁的对象
    /// —— 这条在 Cocos 侧真实踩过（设置页 <c>SettingsPanel.dispose()</c> 就是为此存在）。
    /// </summary>
    public static class EventBus
    {
        /// <summary>
        /// 事件名 -> 回调表。用 List 而不是 Dictionary 存回调，是为了保持**插入顺序**
        /// （TS 侧用的 Set 是有序的），同时保留「同一回调重复订阅只留一份」的 Set 语义。
        /// </summary>
        private static readonly Dictionary<string, List<Delegate>> Listeners =
            new Dictionary<string, List<Delegate>>();

        public static void On<T>(string evt, Action<T> callback)
        {
            if (callback == null)
            {
                return;
            }

            if (!Listeners.TryGetValue(evt, out List<Delegate> list))
            {
                list = new List<Delegate>();
                Listeners[evt] = list;
            }

            // List.Contains 走 Delegate 的相等比较 —— 与 TS Set 的去重行为一致。
            // 注意：带闭包的 lambda 每次都是新实例，两边行为相同（都去不掉重）。
            if (!list.Contains(callback))
            {
                list.Add(callback);
            }
        }

        public static void Off<T>(string evt, Action<T> callback)
        {
            if (callback == null)
            {
                return;
            }

            if (!Listeners.TryGetValue(evt, out List<Delegate> list))
            {
                return;
            }

            list.Remove(callback);

            if (list.Count == 0)
            {
                Listeners.Remove(evt);
            }
        }

        /// <summary>
        /// 广播。遍历的是**快照**。
        ///
        /// 与 TS 版的一处刻意的差异：JS 的 Set 允许回调在遍历中退订自己，
        /// C# 的 List 在 foreach 中改会直接抛异常。快照严格更安全，
        /// 而「回调里退订自己」是常见写法（见设置页），所以这里必须快照。
        /// </summary>
        public static void Emit<T>(string evt, T data)
        {
            if (!Listeners.TryGetValue(evt, out List<Delegate> list))
            {
                return;
            }

            Delegate[] snapshot = list.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                ((Action<T>)snapshot[i])(data);
            }
        }

        /// <summary>无载荷事件（等价于 TS 侧 <c>emit('xxx')</c> 不传 data）。</summary>
        public static void Emit(string evt)
        {
            Emit<object>(evt, null);
        }

        public static void Clear()
        {
            Listeners.Clear();
        }
    }
}
