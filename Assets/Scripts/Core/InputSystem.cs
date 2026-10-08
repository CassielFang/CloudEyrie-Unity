using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CloudEyrie
{
    /// <summary>
    /// 输入动作层 —— 把物理按键翻译成游戏意图。
    /// 对应 Cocos 侧 <c>core/InputSystem.ts</c>（模块级单例 <c>inputSystem</c>）。
    ///
    /// 业务代码读这个，不再读 <c>Key</c>：
    /// <code>
    /// InputSystem.IsDown(GameAction.MoveRight);
    /// InputSystem.WasPressed(GameAction.Jump);
    /// </code>
    ///
    /// **绑定表的真相源是 <see cref="GameSettings"/>**（它负责读档与回落默认值），
    /// 本类只做一层运行时缓存 + 反查索引，收到 <see cref="GameEvents.SettingsChanged"/>
    /// 就重新拉取。<see cref="Rebind"/> 也是写穿到 <see cref="GameSettings"/>，
    /// 由它落盘后广播回来。
    ///
    /// ============================ 与 Cocos 版的关键差异 ============================
    ///
    /// Cocos 版手工维护三个集合（<c>keysDown</c> / <c>keysPressed</c> / <c>keysReleased</c>），
    /// 靠监听 KEY_DOWN / KEY_UP 事件维护，并在 <c>Director.EVENT_END_FRAME</c> 清边沿。
    /// 那一整套复杂度有两个来源：**事件式输入** + **边沿必须手工清**。
    ///
    /// Unity 的 Input System 两者都不需要：
    ///   - <c>Keyboard.current[k].isPressed</c> 是**实时状态**，随查随取；
    ///   - <c>wasPressedThisFrame</c> / <c>wasReleasedThisFrame</c> 是**引擎维护的逐帧边沿**，
    ///     天然满足「同帧所有读者都看到 true、下一帧自动消失」—— 正是 Cocos 花大力气
    ///     用 END_FRAME 清空换来的语义。
    ///
    /// 所以这里**不缓存任何按键状态，一律实时轮询**。收益不只是代码少：
    /// Cocos 那个「按住 D 切场景 → 主角自己往右走」的 bug（缓存集合变脏）**从根上不会发生**。
    /// 同理，「暂停中丢松手边沿」那条已知边界也自动消解。
    ///
    /// ============================ 三条硬规矩（改之前务必读）============================
    ///
    /// **1. <see cref="WasPressed"/> 是「读」不是「消费」。**
    ///    同一帧里多个读者都会看到 true。旧写法（事件里直接改快照 + 消费后清零）是「先读的拿走」。
    ///    目前同一动作只有一个消费者，所以没影响 —— 以后加暂停菜单时要留意，
    ///    需要排他语义就用 <see cref="ConsumePressed"/>。
    ///
    /// **2. 只在 <see cref="QingheController"/> 一处读输入，其余全部读 <c>ctx.Input</c> 快照。**
    ///    形态状态类（<c>EntityForm</c> 等）会改写 <c>ctx.Input.Jump/Dash</c>。
    ///    若它们绕过快照直接读 <see cref="WasPressed"/>，就会出现两个真相源，边沿语义立刻失效。
    ///
    /// **3. 捕获模式（<see cref="SetCaptureMode"/>）用完必须退出。**
    ///    开着的时候整个动作层哑掉，忘记退出的表现是「游戏里按键全没反应」。
    /// </summary>
    public static class InputSystem
    {
        /// <summary>
        /// 绑定表缓存，真相源在 <see cref="GameSettings"/>。
        /// </summary>
        private static Dictionary<GameAction, Binding[]> _bindings;

        /// <summary>
        /// 反查表：一个键可能同时映射多个动作（↑/W 既是 MoveUp 又是 MenuUp），所以是**数组**。
        /// </summary>
        private static readonly Dictionary<Key, List<GameAction>> KeyToActions =
            new Dictionary<Key, List<GameAction>>();

        /// <summary>本帧已被 <see cref="ConsumePressed"/> 吃掉的键 → 被吃掉的帧号。</summary>
        private static readonly Dictionary<Key, int> ConsumedFrame = new Dictionary<Key, int>();

        /// <summary>自检开关。排查键位问题时手动置 true。</summary>
        public const bool DebugValidate = true;

        private static bool _capturing;
        private static bool _initialized;

        /// <summary>
        /// 捕获模式（设置页改键用）。开启期间**动作层停止记录任何按键**。
        ///
        /// 为什么必须挡住：玩家为了把「跳跃」改成 K 而按下的那个 K，如果照记不误，
        /// 同一帧就会被解析成「踏云闪」——「改键的那一下同时触发了另一个动作」。
        ///
        /// ⚠️ **用完必须 <see cref="SetCaptureMode"/>(false)**。开着的时候整个动作层哑掉；
        /// 关闭设置面板却忘了退出捕获，表现就是「游戏里按键全没反应」。
        /// </summary>
        public static bool IsCapturing
        {
            get { return _capturing; }
        }

        // ============================================================ 初始化

        private static void EnsureInit()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            ReloadBindings();

            EventBus.On<SettingsChangedEvent>(GameEvents.SettingsChanged, OnSettingsChanged);

            if (DebugValidate)
            {
                ValidateBindings();
            }
        }

        private static void ReloadBindings()
        {
            _bindings = GameSettings.GetAllBindings();
            RebuildReverseIndex();
        }

        /// <summary>
        /// 必须是静态方法或显式保存的委托 —— <see cref="EventBus"/> 不传 target。
        /// 这里用静态方法，<see cref="Off"/> 时同一个方法组会解析成相等的委托。
        /// </summary>
        private static void OnSettingsChanged(SettingsChangedEvent e)
        {
            if (e.Bindings)
            {
                ReloadBindings();
            }
        }

        private static void RebuildReverseIndex()
        {
            KeyToActions.Clear();

            foreach (GameAction action in InputActions.AllActions)
            {
                if (!_bindings.TryGetValue(action, out Binding[] list) || list == null)
                {
                    continue;
                }

                foreach (Binding b in list)
                {
                    if (b.Kind != BindingKind.Key)
                    {
                        continue;
                    }

                    if (KeyToActions.TryGetValue(b.Key, out List<GameAction> actions))
                    {
                        if (!actions.Contains(action))
                        {
                            actions.Add(action);
                        }
                    }
                    else
                    {
                        KeyToActions[b.Key] = new List<GameAction> { action };
                    }
                }
            }
        }

        // ============================================================ 查询

        /// <summary>动作是否被按住</summary>
        public static bool IsDown(GameAction action)
        {
            EnsureInit();

            if (_capturing || !_bindings.TryGetValue(action, out Binding[] list) || list == null)
            {
                return false;
            }

            foreach (Binding b in list)
            {
                if (b.Kind == BindingKind.Key && IsKeyDown(b.Key))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>动作是否在本帧被按下（边沿）。**是读不是消费**，同帧多个读者都会看到 true。</summary>
        public static bool WasPressed(GameAction action)
        {
            EnsureInit();

            if (_capturing || !_bindings.TryGetValue(action, out Binding[] list) || list == null)
            {
                return false;
            }

            foreach (Binding b in list)
            {
                if (b.Kind == BindingKind.Key && IsKeyPressedThisFrame(b.Key))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>动作是否在本帧被松开（边沿）</summary>
        public static bool WasReleased(GameAction action)
        {
            EnsureInit();

            if (_capturing || !_bindings.TryGetValue(action, out Binding[] list) || list == null)
            {
                return false;
            }

            foreach (Binding b in list)
            {
                if (b.Kind == BindingKind.Key && IsKeyReleasedThisFrame(b.Key))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 读后即清 —— 给「一次性、排他」的语义用（阶段推进之类）。
        /// 普通玩法**不要用**，会抢掉同帧其它消费者的输入。
        ///
        /// 与 Cocos 版一致：清的是**物理键**，不是动作 —— 同一个键绑定的另一个动作
        /// 本帧也会看到 false。
        /// </summary>
        public static bool ConsumePressed(GameAction action)
        {
            if (!WasPressed(action))
            {
                return false;
            }

            if (_bindings.TryGetValue(action, out Binding[] list) && list != null)
            {
                foreach (Binding b in list)
                {
                    if (b.Kind == BindingKind.Key)
                    {
                        ConsumedFrame[b.Key] = Time.frameCount;
                    }
                }
            }

            return true;
        }

        // ---- 底层按键读取（全部实时，无缓存） ----

        private static bool IsKeyDown(Key key)
        {
            Keyboard kb = Keyboard.current;
            return kb != null && kb[key].isPressed;
        }

        private static bool IsKeyPressedThisFrame(Key key)
        {
            // 本帧已被消费的键不再报告边沿
            if (ConsumedFrame.TryGetValue(key, out int frame) && frame == Time.frameCount)
            {
                return false;
            }

            Keyboard kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }

        private static bool IsKeyReleasedThisFrame(Key key)
        {
            Keyboard kb = Keyboard.current;
            return kb != null && kb[key].wasReleasedThisFrame;
        }

        // ============================================================ 绑定

        public static Binding[] GetBindings(GameAction action)
        {
            EnsureInit();
            return _bindings.TryGetValue(action, out Binding[] list) && list != null
                ? list
                : System.Array.Empty<Binding>();
        }

        /// <summary>
        /// 改一条绑定。写穿到 <see cref="GameSettings"/>（由它落盘并广播），本类随即重新拉取。
        /// <paramref name="index"/> 越界返回 false。
        /// </summary>
        public static bool Rebind(GameAction action, int index, Binding binding)
        {
            EnsureInit();
            return GameSettings.SetBinding(action, index, binding);
        }

        /// <summary>全部恢复默认键位</summary>
        public static void ResetBindings()
        {
            EnsureInit();
            GameSettings.ResetBindings();
        }

        // ============================================================ 复位 / 捕获

        /// <summary>
        /// 清空全部输入状态。切场景、失焦时调。
        ///
        /// Unity 侧因为不做状态缓存，「按住键切场景」本来就不会出问题；
        /// 这里保留它是为了**顺手把捕获模式放掉** —— 设置页若因异常没走到 dispose
        /// （切场景、Alt+Tab 导致面板没了），这是最后一道保险，
        /// 捕获模式挂着不放等于整个动作层失灵。
        /// </summary>
        public static void Reset()
        {
            ConsumedFrame.Clear();
            _capturing = false;
        }

        /// <summary>
        /// 进出改键捕获态。**离开设置页时务必调 <c>SetCaptureMode(false)</c>**，
        /// 理由见 <see cref="IsCapturing"/>。
        ///
        /// 与 Cocos 版一致：这里只切开关，不清按键状态 —— Unity 本来就没有可清的缓存，
        /// 而且实时轮询意味着「玩家为了改键按住的那个键」不会残留成幽灵输入。
        /// </summary>
        public static void SetCaptureMode(bool on)
        {
            EnsureInit();
            _capturing = on;
        }

        // ============================================================ 自检

        /// <summary>
        /// 绑定表自检。抓的是那些**不报错、只在特定场景表现为「按键没反应」**的问题：
        /// 动作没有绑定、同一动作内绑定重复、反查表缺项。
        ///
        /// 跨动作重复是**合法的**（↑/W 同时映射 MoveUp 与 MenuUp），只在同一动作内部查重。
        /// </summary>
        public static bool ValidateBindings()
        {
            EnsureInit();

            bool ok = true;

            foreach (GameAction action in InputActions.AllActions)
            {
                if (!_bindings.TryGetValue(action, out Binding[] list) || list == null || list.Length == 0)
                {
                    Debug.LogError($"[InputSystem] 动作「{action}」没有任何绑定，永远不会触发");
                    ok = false;
                    continue;
                }

                for (int i = 0; i < list.Length; i++)
                {
                    for (int j = i + 1; j < list.Length; j++)
                    {
                        if (InputActions.SameBinding(list[i], list[j]))
                        {
                            Debug.LogError(
                                $"[InputSystem] 动作「{action}」内部有重复绑定：" +
                                $"{InputActions.BindingLabel(list[i])}（第 {i} 和 {j} 条）");
                            ok = false;
                        }
                    }

                    if (list[i].Kind == BindingKind.Key &&
                        (!KeyToActions.TryGetValue(list[i].Key, out List<GameAction> back) ||
                         !back.Contains(action)))
                    {
                        Debug.LogError(
                            $"[InputSystem] 反查表缺项：{action} 的 {InputActions.BindingLabel(list[i])}");
                        ok = false;
                    }
                }
            }

            if (ok)
            {
                Debug.Log($"[InputSystem] 绑定表自检通过（{InputActions.AllActions.Length} 个动作）");
            }

            return ok;
        }
    }
}
