using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>灵炁值变化事件的载荷。对应 TS 侧的 <c>SpiritEnergyChangedEvent</c>。</summary>
    public struct SpiritEnergyChangedEvent
    {
        public float Current;
        public float Max;
        public float Delta;
    }

    /// <summary>
    /// 灵炁共享池 —— 对应 Cocos 侧 <c>core/SpiritEnergySystem.ts</c>（模块级单例 <c>spiritEnergySystem</c>）。
    ///
    /// v1 模型：实体形态不按秒持续扣灵炁，而是按动作消耗（轻击/重击/踏云闪等），
    /// 灵炁在实体态自动恢复。恢复分战斗 / 非战斗两档，并受灵脉潮汐倍率影响；
    /// 站在灵泉内额外以固定速率恢复。
    ///
    /// ⚠️ <b>max 等数值惰性读取 <see cref="GameConfig"/>，不要缓存</b>
    /// —— 与 TS 侧同一条约定。
    ///
    /// 这是全项目**唯一零引擎依赖**的两个模块之一（另一个是 <see cref="EventBus"/>），
    /// 逻辑与 TS 版逐行对应。
    /// </summary>
    public static class SpiritEnergySystem
    {
        private static float _current;

        /// <summary>是否处于战斗状态（决定恢复速率档位）</summary>
        private static bool _inCombat;

        /// <summary>
        /// 当前正在威胁玩家的来源集合（每只敌人 AI 是一个来源）。
        /// 用集合而不是布尔：同屏多只敌人时，一只回巡逻 / 死亡不能把另一只的「战斗中」也抹掉。
        /// </summary>
        private static readonly HashSet<object> CombatSources = new HashSet<object>();

        /// <summary>手动覆盖（自测与调试用）：非 null 时优先于 <see cref="CombatSources"/></summary>
        private static bool? _combatOverride;

        /// <summary>灵脉潮汐恢复倍率（默认 1，高潮 2、低潮 0.5）</summary>
        private static float _tideRegenMult = 1.0f;

        /// <summary>是否站在灵泉范围内</summary>
        private static bool _inSpring;

        static SpiritEnergySystem()
        {
            _current = GameConfig.Current.Spirit.Initial;
        }

        /// <summary>最大灵炁（惰性读取，勿缓存）</summary>
        public static float Max
        {
            get { return GameConfig.Current.Spirit.Max; }
        }

        public static float GetCurrent()
        {
            return _current;
        }

        /// <summary>当前灵炁比率 [0, 1]</summary>
        public static float GetRatio()
        {
            float max = Max;
            return max > 0f ? _current / max : 0f;
        }

        /// <summary>
        /// 手动设置战斗状态：**会覆盖**所有来源上报的状态（自测 / 调试用）。
        /// 敌人 AI 请用 <see cref="SetCombatSource"/>，不要用这个。
        /// </summary>
        public static void SetInCombat(bool value)
        {
            _combatOverride = value;
            RefreshInCombat();
        }

        /// <summary>清除手动覆盖，回到「由来源集合决定」。</summary>
        public static void ClearCombatOverride()
        {
            _combatOverride = null;
            RefreshInCombat();
        }

        /// <summary>
        /// 上报/撤销一个战斗威胁来源（敌人 AI 用，传 <c>this</c> 即可）。
        /// 只要有任意一个来源存在就算战斗中；来源销毁时**必须**传 false 退订，
        /// 否则灵炁会永久停在战斗恢复档（Cocos 侧踩过）。
        /// </summary>
        public static void SetCombatSource(object source, bool active)
        {
            if (source == null)
            {
                return;
            }

            if (active)
            {
                CombatSources.Add(source);
            }
            else
            {
                CombatSources.Remove(source);
            }

            RefreshInCombat();
        }

        /// <summary>当前战斗来源数量（调试用）</summary>
        public static int GetCombatSourceCount()
        {
            return CombatSources.Count;
        }

        private static void RefreshInCombat()
        {
            _inCombat = _combatOverride ?? (CombatSources.Count > 0);
        }

        public static bool IsInCombat()
        {
            return _inCombat;
        }

        public static void SetTideRegenMult(float value)
        {
            _tideRegenMult = value;
        }

        public static void SetInSpring(bool value)
        {
            _inSpring = value;
        }

        /// <summary>
        /// 当前是否站在灵泉范围内。
        ///
        /// 由 <see cref="LevelDirector"/> 每帧 <see cref="SetInSpring"/> 维护。
        /// 除了「回灵炁」之外还被战斗侧读：**泡在泉里不能发灵弹**
        /// （见 <c>CombatController.FireMistShot</c>），否则灵泉就从「回复点」
        /// 变成了「无限火力的安全炮台」—— 灵炁一边回一边打，消耗被完全抵消。
        /// </summary>
        public static bool IsInSpring()
        {
            return _inSpring;
        }

        /// <summary>是否可以消耗指定数量的灵炁</summary>
        public static bool CanConsume(float amount)
        {
            if (amount < 0f)
            {
                return false;
            }

            return _current >= amount;
        }

        /// <summary>
        /// 消耗灵炁。返回 true 表示消耗成功；false 代表灵炁不足，**不消耗**。
        /// </summary>
        public static bool Consume(float amount)
        {
            if (!CanConsume(amount))
            {
                return false;
            }

            if (amount == 0f)
            {
                return true;
            }

            SetCurrent(_current - amount);
            return true;
        }

        /// <summary>
        /// 受到伤害（敌人命中玩家）：扣除灵炁，最低到 0。
        /// 与 <see cref="Consume"/> 不同：允许「扣穿」—— 不足部分直接清零，而不是整次拒绝。
        /// </summary>
        public static void Damage(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            SetCurrent(_current - amount);
        }

        /// <summary>恢复灵炁（不会超过上限）</summary>
        public static void Restore(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            SetCurrent(_current + amount);
        }

        /// <summary>灵炁是否已空</summary>
        public static bool IsEmpty()
        {
            return _current <= 0f;
        }

        /// <summary>灵炁是否已满</summary>
        public static bool IsFull()
        {
            return _current >= Max;
        }

        /// <summary>重置为满（灵泉 / 复活回满）</summary>
        public static void ResetToFull()
        {
            SetCurrent(Max);
        }

        /// <summary>重置为游戏开始时的初始值</summary>
        public static void ResetToInitial()
        {
            SetCurrent(GameConfig.Current.Spirit.Initial);
        }

        /// <summary>设置当前灵炁（自动夹在 [0, max] 之间），并广播变化事件</summary>
        public static void SetCurrent(float value)
        {
            float previous = _current;
            _current = Mathf.Max(0f, Mathf.Min(Max, value));

            if (Mathf.Approximately(_current, previous))
            {
                return;
            }

            float delta = _current - previous;

            EventBus.Emit(GameEvents.SpiritEnergyChanged, new SpiritEnergyChangedEvent
            {
                Current = _current,
                Max = Max,
                Delta = delta,
            });

            if (_current <= 0f)
            {
                EventBus.Emit(GameEvents.SpiritEnergyDepleted);
            }
        }

        /// <summary>每帧更新：自然恢复（战斗/非战斗档位 × 潮汐倍率）+ 灵泉恢复</summary>
        public static void Update(float dt)
        {
            if (dt <= 0f)
            {
                return;
            }

            SpiritConfig spirit = GameConfig.Current.Spirit;

            // 自然恢复
            if (!IsFull())
            {
                float baseRate = _inCombat ? spirit.RegenInCombat : spirit.RegenOutOfCombat;
                float rate = baseRate * _tideRegenMult;
                if (rate > 0f)
                {
                    Restore(rate * dt);
                }
            }

            // 灵泉恢复（站在范围内，恢复到 capRatio 上限）
            if (_inSpring)
            {
                float cap = spirit.Max * spirit.SpringRestoreCapRatio;
                if (_current < cap)
                {
                    Restore(spirit.SpringRestoreRate * dt);
                }
            }
        }

        /// <summary>
        /// 整体复位（切场景 / 重开时用）。
        /// TS 侧没有这个方法 —— 那里模块单例活到进程结束，切场景不重置；
        /// Unity 里场景重载更常见，显式提供以免留下跨局脏状态。
        /// </summary>
        public static void ResetAll()
        {
            CombatSources.Clear();
            _combatOverride = null;
            _inCombat = false;
            _tideRegenMult = 1.0f;
            _inSpring = false;
            ResetToInitial();
        }
    }
}
