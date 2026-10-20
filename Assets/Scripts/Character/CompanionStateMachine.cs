using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary><see cref="GameEvents.CompanionFormChanged"/> 事件的载荷。</summary>
    public struct CompanionFormChangedEvent
    {
        public CompanionForm Previous;
        public CompanionForm Current;

        /// <summary>切换原因：手动 / 灵炁过低强制</summary>
        public bool Forced;
    }

    /// <summary>
    /// 灵伴三形态状态机（挂在青禾节点上）。
    /// 对应 Cocos 侧 <c>character/CompanionStateMachine.ts</c>。
    ///
    /// 只负责「跨形态的规则」：当前形态、状态转换、成本、冷却、休眠、强制切雾。
    /// 各形态自身的移动/消耗内聚在 <c>Forms/</c> 下的状态类里（状态模式），
    /// 由 <see cref="QingheController"/> 每帧调用 <see cref="Tick"/> 驱动。
    ///
    /// <b>转换规则</b>
    /// <code>
    /// 实体 ↔ 灵雾：手动，消耗 10 / 5
    /// 灵雾 → 灵合：手动，需灵炁 ≥ MergeMinSpirit，消耗 30
    /// 灵合 → 实体：手动，消耗 15
    /// 灵合 → 灵雾：手动解除（10）或灵炁过低强制
    /// 任意形态 → 灵雾：灵炁 &lt; ForcedMistThreshold 强制
    /// </code>
    /// </summary>
    public class CompanionStateMachine : MonoBehaviour
    {
        private CompanionForm _form = CompanionForm.Entity;

        private readonly Dictionary<CompanionForm, ICompanionForm> _states =
            new Dictionary<CompanionForm, ICompanionForm>();

        private ICompanionForm _currentState;

        /// <summary>切换硬直剩余（期间禁止手动切换）</summary>
        private float _switchLockRemaining;

        /// <summary>灵合冷却剩余（离开灵合后禁止再次进入）</summary>
        private float _mergeCooldownRemaining;

        /// <summary>灵合后休眠剩余（休眠期间无法召唤灵伴）</summary>
        private float _mergeSleepRemaining;

        private void Awake()
        {
            _states[CompanionForm.Entity] = new EntityForm();
            _states[CompanionForm.Mist] = new MistForm();
            _states[CompanionForm.Merge] = new MergeForm();
            _currentState = _states[CompanionForm.Entity];
        }

        /// <summary>
        /// 形态切换的按键读取。
        ///
        /// 由 <see cref="QingheController"/> 每帧驱动 —— 动作层没有「事件回调」这种入口，
        /// 边沿要在帧内轮询（<c>WasPressed</c> 只在按下的那一帧为 true）。
        /// </summary>
        private void PollSwitchKeys()
        {
            if (InputSystem.WasPressed(GameAction.FormSwitch))
            {
                // 实体 ↔ 灵雾
                if (_form == CompanionForm.Entity)
                {
                    TrySwitchTo(CompanionForm.Mist);
                }
                else if (_form == CompanionForm.Mist)
                {
                    TrySwitchTo(CompanionForm.Entity);
                }
            }

            if (InputSystem.WasPressed(GameAction.FormMerge))
            {
                // 灵雾 → 灵合；灵合 → 实体
                if (_form == CompanionForm.Mist)
                {
                    TrySwitchTo(CompanionForm.Merge);
                }
                else if (_form == CompanionForm.Merge)
                {
                    TrySwitchTo(CompanionForm.Entity);
                }
            }
        }

        public CompanionForm GetCurrentForm()
        {
            return _form;
        }

        /// <summary>形态限制：默认每关只有一次 5 秒灵雾；接触灵泉 / 进 Boss 后解除</summary>
        public enum FormAccess
        {
            Limited,
            Unlimited,
        }

        /// <summary>受限期内一次灵雾能维持多久</summary>
        public const float MistWindowSeconds = 5f;

        private FormAccess _access = FormAccess.Limited;
        private bool _mistUsed;
        private float _mistTimeLeft;

        /// <summary>每关开始重置：回到「只有一次灵雾」，并强制回到实体</summary>
        public void ResetLimitedAccess()
        {
            _access = FormAccess.Limited;
            _mistUsed = false;
            _mistTimeLeft = 0f;

            if (_form != CompanionForm.Entity)
            {
                ForceSwitchTo(CompanionForm.Entity);
            }
        }

        /// <summary>解除限制（接触灵泉 / 进入 Boss 战）</summary>
        public void UnlockForms()
        {
            _access = FormAccess.Unlimited;
        }

        public bool IsFormUnlocked()
        {
            return _access == FormAccess.Unlimited;
        }

        public bool IsSwitchLocked()
        {
            return _switchLockRemaining > 0f;
        }

        /// <summary>手动切换到目标形态（受硬直、冷却、灵炁约束）</summary>
        public bool TrySwitchTo(CompanionForm target)
        {
            return ApplySwitch(target, false);
        }

        /// <summary>
        /// <b>剧情强制</b>切换：忽略硬直、冷却、灵炁门槛，也不扣灵炁。
        ///
        /// 用途：Boss P3 转场时莹翳主动飞向青禾、触发灵合强制教学。
        /// 那是演出，**不能因为玩家当刻灵炁不够或正在冷却就失败** ——
        /// 失败的话 Boss 战会卡在 P3 门口，而且玩家完全不知道原因。
        ///
        /// 与「灵炁耗尽自动切灵雾」（<see cref="CheckForcedMist"/>）是同一条路径。
        /// </summary>
        public bool ForceSwitchTo(CompanionForm target)
        {
            return ApplySwitch(target, true);
        }

        /// <summary>
        /// 每帧形态逻辑（由 <see cref="QingheController"/> 驱动）：
        /// 计时器衰减 + 移动 + 持续消耗 + 强制切雾。
        /// </summary>
        public void Tick(float dt, FormContext ctx)
        {
            // 切换键先读：它走的是形态机自己的硬直/冷却，和下面的移动是两码事
            PollSwitchKeys();

            _switchLockRemaining = Mathf.Max(0f, _switchLockRemaining - dt);
            _mergeCooldownRemaining = Mathf.Max(0f, _mergeCooldownRemaining - dt);
            _mergeSleepRemaining = Mathf.Max(0f, _mergeSleepRemaining - dt);

            // 受限期的灵雾只维持 5 秒，到点自动拍回实体
            if (_form == CompanionForm.Mist && _access == FormAccess.Limited)
            {
                _mistTimeLeft -= dt;

                if (_mistTimeLeft <= 0f)
                {
                    EventBus.Emit(GameEvents.HintRequested,
                                  new HintEvent { Text = "灵雾只能维持 5 秒 · 接触灵泉后可自由切换", Seconds = 3f });
                    ForceSwitchTo(CompanionForm.Entity);
                }
            }

            // 移动（委托给当前形态状态）
            _currentState.Update(dt, ctx);

            // 持续消耗
            float drain = _currentState.DrainPerSecond();
            if (drain > 0f)
            {
                SpiritEnergySystem.Consume(drain * dt);
            }

            // 灵炁过低强制切灵雾
            CheckForcedMist();
        }

        private bool ApplySwitch(CompanionForm target, bool forced)
        {
            if (target == _form)
            {
                // ⚠️ **强制切换即使目标相同，也要再广播一次**，不能直接返回。
                //
                // 调用方的意图是「让玩家处于这个形态（并且是重新开始的一轮）」——
                // 典型是 Boss P3 的「强制灵合」（<c>BossAI.EnterP3</c> →
                // <c>ForceSwitchTo(Merge)</c>）。玩家要是**已经在灵合**，
                // 原来的 `return false` 会让这次广播整个消失，于是监听方
                // （灵炁爆发的「每次灵合限一次」）不会复位 —— P3 想「送你一次爆发」，
                // 实际一次也放不出来，而且不报错。
                //
                // 手动切换则是真的空操作，保持原样。
                if (!forced)
                {
                    return false;
                }

                EmitFormChanged(_form, _form, true);
                Debug.Log($"[Companion] 强制切换目标与当前相同（{_form}），重广播一次以复位状态");
                return true;
            }

            SpiritConfig spirit = GameConfig.Current.Spirit;

            if (!forced)
            {
                if (_switchLockRemaining > 0f)
                {
                    Debug.Log("[Companion] 切换硬直中，无法切换");
                    return false;
                }

                SwitchResolution result = ResolveManualSwitch(target);

                if (!result.Allowed)
                {
                    Debug.Log($"[Companion] {result.Reason}");

                    // 切不了的原因必须**让玩家看得见**。这一串拒绝原来只进日志，
                    // 玩家侧的表现就是「条件都满足却切不了」，只能归结为玄学
                    // （实测困惑过：「切回灵合要等很久，是时间限制吗？」）。
                    EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = result.Reason });
                    return false;
                }

                if (!SpiritEnergySystem.Consume(result.Cost))
                {
                    Debug.Log("[Companion] 灵炁不足，切换失败");
                    return false;
                }
            }

            CompanionForm previous = _form;
            _form = target;
            _currentState = _states[target];

            // 灵雾计时：进雾就开始倒计时（受限期只有 5 秒）
            if (target == CompanionForm.Mist)
            {
                _mistUsed = true;
                _mistTimeLeft = MistWindowSeconds;
            }
            else
            {
                _mistTimeLeft = 0f;
            }

            if (!forced)
            {
                _switchLockRemaining = spirit.SwitchLockDuration;
            }

            // 离开灵合：进入冷却 + 休眠。
            //
            // ⚠️ **只有玩家自己离开才罚冷却**。被灵炁耗尽**强制踢出**时也加 20 秒冷却，
            // 是拿惩罚去顶一次非自愿的退出 —— 而灵合每秒净扣 15~19 点、初始 80 只够撑
            // 三秒多，被踢出本来就很常见；再罚 20 秒直接和 Boss P3「灵泉旁反复灵合」的
            // 循环打架（玩家走到泉边却被告知「冷却中」）。
            if (previous == CompanionForm.Merge && !forced)
            {
                _mergeCooldownRemaining = spirit.MergeCooldown;
                _mergeSleepRemaining = spirit.MergeSleepDuration;
            }

            EmitFormChanged(previous, _form, forced);

            Debug.Log($"[Companion] 形态切换 {previous} -> {_form} ({(forced ? "forced" : "manual")})");
            return true;
        }

        /// <summary>广播形态变化。<paramref name="previous"/> 与 <paramref name="current"/> 相同时表示「重申」。</summary>
        private static void EmitFormChanged(CompanionForm previous, CompanionForm current, bool forced)
        {
            EventBus.Emit(GameEvents.CompanionFormChanged, new CompanionFormChangedEvent
            {
                Previous = previous,
                Current = current,
                Forced = forced,
            });
        }

        private struct SwitchResolution
        {
            public bool Allowed;
            public string Reason;
            public float Cost;
        }

        /// <summary>解析手动切换：返回是否允许 + 原因 + 灵炁消耗</summary>
        private SwitchResolution ResolveManualSwitch(CompanionForm target)
        {
            SpiritConfig spirit = GameConfig.Current.Spirit;

            switch (target)
            {
                case CompanionForm.Mist:
                    if (_form == CompanionForm.Entity)
                    {
                        // 未接触灵泉前的关卡里，灵雾只有一次机会（持续 MistWindowSeconds）。
                        if (_access == FormAccess.Limited && _mistUsed)
                        {
                            return Deny("本关灵雾已用过（接触灵泉后可自由切换）");
                        }

                        return Ok(spirit.SwitchEntityToMist);
                    }

                    if (_form == CompanionForm.Merge)
                    {
                        return Ok(spirit.SwitchMergeToMist);
                    }

                    return Deny("无法切到灵雾");

                case CompanionForm.Entity:
                    if (_form == CompanionForm.Mist)
                    {
                        return Ok(spirit.SwitchMistToEntity);
                    }

                    if (_form == CompanionForm.Merge)
                    {
                        return Ok(spirit.SwitchMergeToEntity);
                    }

                    return Deny("无法切到实体");

                case CompanionForm.Merge:
                    if (_form != CompanionForm.Mist)
                    {
                        return Deny("只能从灵雾进入灵合");
                    }

                    // ⚠️ 拒绝语要**带上还剩几秒**。原来只说「冷却中」，
                    // 玩家既不知道是时间限制、也不知道还要等多久 ——
                    // 表现就是「能量都满了为什么还切不了」。
                    if (_mergeCooldownRemaining > 0f || _mergeSleepRemaining > 0f)
                    {
                        float left = Mathf.Max(_mergeCooldownRemaining, _mergeSleepRemaining);
                        return Deny($"灵合冷却中  还要 {left:F0} 秒");
                    }

                    if (SpiritEnergySystem.GetCurrent() < spirit.MergeMinSpirit)
                    {
                        return Deny($"灵炁不足 {spirit.MergeMinSpirit:F0}，无法灵合");
                    }

                    return Ok(spirit.SwitchMistToMerge);

                default:
                    return Deny("未知形态");
            }
        }

        private static SwitchResolution Ok(float cost)
        {
            return new SwitchResolution { Allowed = true, Reason = string.Empty, Cost = cost };
        }

        private static SwitchResolution Deny(string reason)
        {
            return new SwitchResolution { Allowed = false, Reason = reason, Cost = 0f };
        }

        private void CheckForcedMist()
        {
            if (_form == CompanionForm.Mist)
            {
                return;
            }

            if (SpiritEnergySystem.GetCurrent() < GameConfig.Current.Spirit.ForcedMistThreshold)
            {
                ApplySwitch(CompanionForm.Mist, true);
            }
        }

        // ---- 调试 / 测试入口 ----

        public float MergeCooldownRemaining
        {
            get { return _mergeCooldownRemaining; }
        }

        public float MergeSleepRemaining
        {
            get { return _mergeSleepRemaining; }
        }
    }
}
