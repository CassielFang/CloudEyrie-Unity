using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace CloudEyrie
{
    /// <summary>
    /// 输入动作层的**数据定义**（动作枚举、绑定类型、默认绑定表）
    /// —— 对应 Cocos 侧 <c>core/InputActions.ts</c>。
    ///
    /// 这一层的目的：让业务代码不再认「D 键」，而是认「向右移动」这个**意图**。
    /// 有了它，键位重映射、手柄、触屏才接得上 —— 否则每个组件各判各的 <c>Key</c>，
    /// 想换个键得改三个文件。
    ///
    /// 动作的读写接口在 <see cref="InputSystem"/>。
    /// </summary>
    public enum GameAction
    {
        // ---- 游戏内 ----
        MoveLeft,
        MoveRight,
        MoveUp,
        MoveDown,
        Jump,
        Dash,
        Attack,
        Block,

        /// <summary>实体 ↔ 灵雾</summary>
        FormSwitch,

        /// <summary>灵雾 → 灵合；灵合 → 实体</summary>
        FormMerge,

        // ---- 菜单 ----
        MenuUp,
        MenuDown,
        MenuConfirm,
    }

    public enum BindingKind
    {
        Key,
        Gamepad,
        Mouse,
    }

    /// <summary>
    /// 一条绑定。
    ///
    /// 手柄与鼠标**只是预留**，本轮不实现 —— 留成可辨识的结构是为了以后加手柄时
    /// 只需要在 <see cref="InputSystem"/> 里加一个分支，不用动数据结构，也不用迁移存档格式。
    /// </summary>
    [System.Serializable]
    public struct Binding
    {
        public BindingKind Kind;

        /// <summary>Kind == Key 时有效</summary>
        public Key Key;

        /// <summary>Kind == Gamepad/Mouse 时有效</summary>
        public int Button;

        public static Binding FromKey(Key k)
        {
            return new Binding { Kind = BindingKind.Key, Key = k };
        }

        public static Binding FromGamepad(int button)
        {
            return new Binding { Kind = BindingKind.Gamepad, Button = button };
        }

        public static Binding FromMouse(int button)
        {
            return new Binding { Kind = BindingKind.Mouse, Button = button };
        }
    }

    public static class InputActions
    {
        /// <summary>
        /// 全部动作的列表（遍历用）。**加新动作时这里要一起加**，
        /// <see cref="InputSystem.ValidateBindings"/> 会盯着漏掉的。
        /// </summary>
        public static readonly GameAction[] AllActions =
        {
            GameAction.MoveLeft,
            GameAction.MoveRight,
            GameAction.MoveUp,
            GameAction.MoveDown,
            GameAction.Jump,
            GameAction.Dash,
            GameAction.Attack,
            GameAction.Block,
            GameAction.FormSwitch,
            GameAction.FormMerge,
            GameAction.MenuUp,
            GameAction.MenuDown,
            GameAction.MenuConfirm,
        };

        /// <summary>
        /// 设置页显示用的中文名。填设置页时直接用，**不要在 UI 里再手写一份**。
        /// </summary>
        public static readonly Dictionary<GameAction, string> ActionLabels = new Dictionary<GameAction, string>
        {
            { GameAction.MoveLeft, "向左移动" },
            { GameAction.MoveRight, "向右移动" },
            { GameAction.MoveUp, "向上（飞行）" },
            { GameAction.MoveDown, "向下（飞行）" },
            { GameAction.Jump, "跳　跃" },
            { GameAction.Dash, "踏云闪" },
            { GameAction.Attack, "攻　击" },
            { GameAction.Block, "格　挡" },
            { GameAction.FormSwitch, "形态切换" },
            { GameAction.FormMerge, "灵合归一" },
            { GameAction.MenuUp, "菜单上移" },
            { GameAction.MenuDown, "菜单下移" },
            { GameAction.MenuConfirm, "菜单确认" },
        };

        /// <summary>
        /// 默认绑定表。
        ///
        /// ⚠️ **必须与引入动作层之前的按键逐一对应** —— 这是一次纯重构，默认手感不能变。
        /// 对照表（迁移前的原始代码）：
        /// <code>
        /// QingheController      A/← D/→ W/↑ S/↓ SPACE K
        /// CombatController      J（按下起手/按住蓄力/松开结算） L（按住格挡）
        /// CompanionStateMachine F G
        /// TitleScreen           ↑/W ↓/S ENTER/SPACE/J
        /// </code>
        ///
        /// 几个动作**共用同一个键是正常的**（菜单和游戏在不同场景里消费，不会同时生效）
        /// —— 反向表是一对多的。<see cref="InputSystem.ValidateBindings"/> 只在**同一个动作内部**查重。
        /// </summary>
        public static readonly Dictionary<GameAction, Binding[]> DefaultBindings =
            new Dictionary<GameAction, Binding[]>
            {
                { GameAction.MoveLeft, new[] { Binding.FromKey(Key.A), Binding.FromKey(Key.LeftArrow) } },
                { GameAction.MoveRight, new[] { Binding.FromKey(Key.D), Binding.FromKey(Key.RightArrow) } },
                { GameAction.MoveUp, new[] { Binding.FromKey(Key.W), Binding.FromKey(Key.UpArrow) } },
                { GameAction.MoveDown, new[] { Binding.FromKey(Key.S), Binding.FromKey(Key.DownArrow) } },
                { GameAction.Jump, new[] { Binding.FromKey(Key.Space) } },
                { GameAction.Dash, new[] { Binding.FromKey(Key.K) } },
                { GameAction.Attack, new[] { Binding.FromKey(Key.J) } },
                { GameAction.Block, new[] { Binding.FromKey(Key.L) } },
                { GameAction.FormSwitch, new[] { Binding.FromKey(Key.F) } },
                { GameAction.FormMerge, new[] { Binding.FromKey(Key.G) } },
                { GameAction.MenuUp, new[] { Binding.FromKey(Key.UpArrow), Binding.FromKey(Key.W) } },
                { GameAction.MenuDown, new[] { Binding.FromKey(Key.DownArrow), Binding.FromKey(Key.S) } },
                {
                    GameAction.MenuConfirm,
                    new[] { Binding.FromKey(Key.Enter), Binding.FromKey(Key.Space), Binding.FromKey(Key.J) }
                },
            };

        /// <summary>两个绑定是不是同一个（查重、UI 显示用）</summary>
        public static bool SameBinding(Binding a, Binding b)
        {
            if (a.Kind != b.Kind)
            {
                return false;
            }

            switch (a.Kind)
            {
                case BindingKind.Key:
                    return a.Key == b.Key;
                case BindingKind.Gamepad:
                case BindingKind.Mouse:
                    return a.Button == b.Button;
                default:
                    return false;
            }
        }

        /// <summary>绑定的可读文本，给设置页和日志用</summary>
        public static string BindingLabel(Binding b)
        {
            switch (b.Kind)
            {
                case BindingKind.Key:
                    return KeyLabel(b.Key);
                case BindingKind.Gamepad:
                    return $"手柄按钮 {b.Button}";
                case BindingKind.Mouse:
                    return $"鼠标按钮 {b.Button}";
                default:
                    return "未知";
            }
        }

        /// <summary><see cref="Key"/> → 可读文本。只覆盖项目用得到的键，其余回落成枚举名。</summary>
        public static string KeyLabel(Key k)
        {
            switch (k)
            {
                case Key.Space: return "空格";
                case Key.Enter: return "回车";
                case Key.Escape: return "Esc";
                case Key.UpArrow: return "↑";
                case Key.DownArrow: return "↓";
                case Key.LeftArrow: return "←";
                case Key.RightArrow: return "→";
                case Key.Tab: return "Tab";
                case Key.Backspace: return "退格";
                case Key.LeftShift: return "左Shift";
                case Key.RightShift: return "右Shift";
                default:
                    // Unity 的 Key.A..Key.Z 与 Key.Digit1..Digit0 的枚举名本身就是可读的
                    string name = k.ToString();

                    if (name.Length == 1)
                    {
                        return name;
                    }

                    if (name.StartsWith("Digit"))
                    {
                        return name.Substring(5);
                    }

                    return name;
                }
        }
    }
}
