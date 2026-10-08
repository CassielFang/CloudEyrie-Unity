using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CloudEyrie
{
    public enum AudioChannel
    {
        Master,
        Bgm,
        Sfx,
    }

    /// <summary><see cref="GameEvents.SettingsChanged"/> 事件的载荷。</summary>
    public struct SettingsChangedEvent
    {
        /// <summary>音量变了</summary>
        public bool Audio;

        /// <summary>键位变了</summary>
        public bool Bindings;

        /// <summary>画面类设置变了（目前只有亮度）。订阅方：<see cref="ScreenDimmer"/></summary>
        public bool Visual;
    }

    /// <summary>
    /// 玩家设置的唯一持有者与持久化入口 —— 对应 Cocos 侧 <c>core/GameSettings.ts</c>。
    ///
    /// 存两样东西：**音量**（给 <see cref="AudioManager"/> 读）和**键位绑定**
    /// （给 <see cref="InputSystem"/> 读）。两者都从 <see cref="GameEvents.SettingsChanged"/>
    /// 通知 —— 两个订阅方谁都不需要知道对方存在。
    ///
    /// <b>设计上的两点取舍（与 TS 版一致）</b>
    ///
    /// 1. <b>默认值不落盘</b>：<c>Bindings</c> 只存**被玩家改过的**动作。没改过的动作读的时候
    ///    回落到 <see cref="InputActions.DefaultBindings"/>。这样以后调整默认键位，
    ///    老玩家的存档不会把旧默认值钉死。
    /// 2. <b>坏存档不能卡死游戏</b>：读盘全程 try/catch，字段缺失 / 类型不对 / JSON 坏掉
    ///    一律回落默认值并打 warn。设置坏了大不了回到出厂状态，不该让游戏起不来。
    ///
    /// <b>与 TS 版的两处差异（都是改进）</b>
    ///
    /// - TS 侧的存储后端只实现了 Web（<c>sys.localStorage</c> 在原生平台没有实现，
    ///   打包成原生 App 后设置**不会持久化**，代码里留了 TODO）。
    ///   Unity 用 <see cref="PlayerPrefs"/>，Web / PC / 移动端一律可用，那个 TODO 自然消解。
    /// - 序列化用 <see cref="JsonUtility"/>（能直接吃 <c>[Serializable]</c> 的类和数组），
    ///   不手搓 JSON 解析器。
    /// </summary>
    public static class GameSettings
    {
        private const string StorageKey = "yunxiu.settings";

        /// <summary>存档结构版本。字段含义变了才 +1，并在 <see cref="Sanitize"/> 里处理旧版。</summary>
        private const int CurrentVersion = 1;

        /// <summary>音量数组里「这一项没被设置过」的哨兵值。</summary>
        private const float Unset = -1f;

        private static readonly float[] DefaultAudio = { 0.8f, 0.7f, 0.8f };

        /// <summary>
        /// 画面亮度的默认值（1 = 不压暗）。**只有这一份定义** ——
        /// 初始化、<see cref="ResetToDefaults"/> 与「恢复默认」按钮都读它，
        /// 各写一个 1f 迟早漂移（原来就是散的，加「回默认」时才发现没有可读的名字）。
        /// </summary>
        public const float DefaultBrightness = 1f;

        private static Dictionary<AudioChannel, float> _audio;

        /// <summary>画面亮度，1 = 原样。默认原样，所以老存档读进来没有这个字段也不该变暗。</summary>
        private static float _brightness = DefaultBrightness;
        private static Dictionary<GameAction, Binding[]> _bindings;
        private static bool _loaded;

        // ============================================================ 存档 DTO
        //
        // JsonUtility 不支持 Dictionary，所以用一个扁平结构：
        // 音量用定长数组（按 AudioChannel 的枚举顺序），键位用「动作名 → 绑定数组」的数组。
        // 存动作**名字**而不是枚举数值 —— 枚举重排不会让存档错位。

        [Serializable]
        private class SaveData
        {
            public int version = CurrentVersion;

            /// <summary>[master, bgm, sfx]；null 或缺项表示「沿用默认」</summary>
            public float[] audio;

            /// <summary>画面亮度 0~1。**没设置过是 <see cref="Unset"/> 而不是 0** —— 理由见 <see cref="Sanitize"/></summary>
            public float brightness = Unset;

            public BindingEntry[] bindings;
        }

        [Serializable]
        private class BindingEntry
        {
            public string action;
            public Binding[] items;
        }

        // ============================================================ 生命周期

        private static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            FreshData();
            Load();
        }

        private static void FreshData()
        {
            _audio = new Dictionary<AudioChannel, float>();

            foreach (AudioChannel ch in Channels)
            {
                _audio[ch] = DefaultAudio[(int)ch];
            }

            _bindings = new Dictionary<GameAction, Binding[]>();
            _brightness = DefaultBrightness;
        }

        private static readonly AudioChannel[] Channels =
        {
            AudioChannel.Master, AudioChannel.Bgm, AudioChannel.Sfx,
        };

        // ------------------------------------------------ 音量

        public static float GetVolume(AudioChannel channel)
        {
            EnsureLoaded();
            return _audio.TryGetValue(channel, out float v) ? v : 0f;
        }

        public static void SetVolume(AudioChannel channel, float value)
        {
            EnsureLoaded();

            float v = Clamp01(value);
            if (Mathf.Approximately(GetVolume(channel), v))
            {
                return;
            }

            _audio[channel] = v;
            Save();
            Notify(true, false);
        }

        public static void ResetAudio()
        {
            EnsureLoaded();

            foreach (AudioChannel ch in Channels)
            {
                _audio[ch] = DefaultAudio[(int)ch];
            }

            Save();
            Notify(true, false);
        }

        // ------------------------------------------------ 亮度

        /// <summary>
        /// 画面亮度下限。
        ///
        /// **不给到 0**：那是一片纯黑，玩家会以为游戏崩了，而且摸不回来。
        /// 留一档「很暗但还看得见」的下限，改坏了也还能把滑条拖回去。
        /// </summary>
        public const float MinBrightness = 0.35f;

        /// <summary>画面亮度（<see cref="MinBrightness"/>~1）。1 = 不加任何压暗。</summary>
        public static float GetBrightness()
        {
            EnsureLoaded();
            return _brightness;
        }

        public static void SetBrightness(float value)
        {
            EnsureLoaded();

            float v = Mathf.Clamp(value, MinBrightness, 1f);

            if (Mathf.Approximately(_brightness, v))
            {
                return;
            }

            _brightness = v;
            Save();
            Notify(false, false, true);
        }

        /// <summary>
        /// 画面亮度回默认（<see cref="DefaultBrightness"/>）。
        ///
        /// ⚠️ <b>它不在 <see cref="ResetAudio"/> 的范围内</b> —— 那个方法按名字只管三条音量。
        /// 而设置页的「恢复默认」按钮摆在**音画设置**页上，那一页有四行（三条音量 + 亮度），
        /// 玩家点它时的预期是「这一页都回默认」。所以按钮那边要**同时**调这两个方法，
        /// 曾经只调了 ResetAudio，表现是「恢复默认对亮度无效」（实测反馈）。
        /// </summary>
        public static void ResetBrightness()
        {
            SetBrightness(DefaultBrightness);
        }

        // ------------------------------------------------ 键位

        /// <summary>取某个动作的绑定。玩家没改过就回落到默认表。</summary>
        public static Binding[] GetBindings(GameAction action)
        {
            EnsureLoaded();

            bool hasCustom = _bindings.TryGetValue(action, out Binding[] custom)
                             && custom != null && custom.Length > 0;

            Binding[] source;

            if (hasCustom)
            {
                source = custom;
            }
            else
            {
                source = InputActions.DefaultBindings.TryGetValue(action, out Binding[] def)
                    ? def
                    : Array.Empty<Binding>();
            }

            // 返回副本，防止调用方改到内部状态
            Binding[] copy = new Binding[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        /// <summary>整张绑定表（<see cref="InputSystem"/> 建反查表用）</summary>
        public static Dictionary<GameAction, Binding[]> GetAllBindings()
        {
            Dictionary<GameAction, Binding[]> outMap = new Dictionary<GameAction, Binding[]>();

            foreach (GameAction action in InputActions.AllActions)
            {
                outMap[action] = GetBindings(action);
            }

            return outMap;
        }

        /// <summary>改一条绑定。整条动作的绑定会被固化进存档（不再回落默认）。</summary>
        public static bool SetBinding(GameAction action, int index, Binding binding)
        {
            EnsureLoaded();

            Binding[] current = GetBindings(action);

            if (index < 0 || index >= current.Length)
            {
                Debug.LogWarning($"[GameSettings] SetBinding 越界：{action}[{index}]");
                return false;
            }

            current[index] = binding;
            _bindings[action] = current;
            Save();
            Notify(false, true);
            return true;
        }

        /// <summary>全部恢复默认（把覆盖项清空即可，读的时候自然回落到默认表）</summary>
        public static void ResetBindings()
        {
            EnsureLoaded();

            _bindings = new Dictionary<GameAction, Binding[]>();
            Save();
            Notify(false, true);
        }

        // ------------------------------------------------ 持久化

        private static void Notify(bool audio, bool bindings, bool visual = false)
        {
            EventBus.Emit(GameEvents.SettingsChanged, new SettingsChangedEvent
            {
                Audio = audio,
                Bindings = bindings,
                Visual = visual,
            });
        }

        public static void Save()
        {
            EnsureLoaded();

            try
            {
                SaveData data = new SaveData
                {
                    version = CurrentVersion,
                    audio = new[]
                    {
                        GetVolume(AudioChannel.Master),
                        GetVolume(AudioChannel.Bgm),
                        GetVolume(AudioChannel.Sfx),
                    },
                    brightness = GetBrightness(),
                    bindings = BuildBindingEntries(),
                };

                PlayerPrefs.SetString(StorageKey, JsonUtility.ToJson(data));
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameSettings] 写入本地存储失败，本次修改不会保留：{e}");
            }
        }

        private static BindingEntry[] BuildBindingEntries()
        {
            List<BindingEntry> list = new List<BindingEntry>();

            foreach (KeyValuePair<GameAction, Binding[]> kv in _bindings)
            {
                if (kv.Value == null || kv.Value.Length == 0)
                {
                    continue;
                }

                list.Add(new BindingEntry { action = kv.Key.ToString(), items = kv.Value });
            }

            return list.ToArray();
        }

        /// <summary>读档。任何异常都回落默认值，**绝不抛**。</summary>
        public static void Load()
        {
            string raw;

            try
            {
                raw = PlayerPrefs.GetString(StorageKey, string.Empty);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameSettings] 读取本地存储失败，用默认设置：{e}");
                return;
            }

            if (string.IsNullOrEmpty(raw))
            {
                return;
            }

            try
            {
                SaveData parsed = JsonUtility.FromJson<SaveData>(raw);

                if (parsed == null)
                {
                    throw new Exception("存档解析结果为空");
                }

                Sanitize(parsed);
                Debug.Log("[GameSettings] 已载入本地设置");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameSettings] 设置文件损坏，已重置为默认值：{e}");
                FreshData();
            }
        }

        /// <summary>
        /// 逐字段校验，坏的字段单独回落，不因为一个字段坏掉就丢整份设置。
        ///
        /// ⚠️ JsonUtility 分不清「字段不存在」和「字段是 0」—— 所以音量用**定长数组 + 哨兵**：
        /// 数组为 null 或长度不对就整组回落默认，避免一次读档把所有音量静音。
        /// </summary>
        private static void Sanitize(SaveData raw)
        {
            // 版本检查：存档比当前代码新（用户降级了版本）—— 保守起见整份丢掉，
            // 硬读可能把不认识的字段当已知字段用
            if (raw.version > CurrentVersion)
            {
                Debug.LogWarning($"[GameSettings] 存档版本 {raw.version} 高于当前 {CurrentVersion}，重置为默认值");
                FreshData();
                return;
            }

            if (raw.audio != null && raw.audio.Length == Channels.Length)
            {
                for (int i = 0; i < Channels.Length; i++)
                {
                    float v = raw.audio[i];

                    if (!float.IsNaN(v) && !float.IsInfinity(v) && v >= 0f)
                    {
                        _audio[Channels[i]] = Clamp01(v);
                    }
                }
            }

            // 亮度：老存档里根本没这个字段，JsonUtility 会填成 0 —— 直接吃下去就是「一进游戏全黑」。
            // 所以哨兵值用 Unset(-1)，只有落在这个明确范围内才认。
            // （FreshData 已经把 _brightness 复位成 1，这里不动它就等于回落默认。）
            if (raw.brightness >= MinBrightness && raw.brightness <= 1f)
            {
                _brightness = raw.brightness;
            }

            if (raw.bindings == null)
            {
                return;
            }

            foreach (BindingEntry entry in raw.bindings)
            {
                if (entry == null || string.IsNullOrEmpty(entry.action))
                {
                    continue;
                }

                // 认不出的动作名（旧存档 / 手改）直接跳过 —— 它自然回落到默认绑定
                if (!Enum.TryParse(entry.action, out GameAction action))
                {
                    continue;
                }

                if (entry.items == null || entry.items.Length == 0)
                {
                    continue;
                }

                // 绑定数组里允许「键位枚举认不出」的项（比如以后删了某个 Key），
                // 整条丢弃而不是留着一条永远不触发的死绑定
                bool allValid = true;

                foreach (Binding b in entry.items)
                {
                    if (!IsValidBinding(b))
                    {
                        allValid = false;
                        break;
                    }
                }

                if (allValid)
                {
                    _bindings[action] = entry.items;
                }
            }
        }

        private static bool IsValidBinding(Binding b)
        {
            switch (b.Kind)
            {
                case BindingKind.Key:
                    return Enum.IsDefined(typeof(Key), b.Key);
                case BindingKind.Gamepad:
                case BindingKind.Mouse:
                    return b.Button >= 0;
                default:
                    return false;
            }
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
            {
                return 0f;
            }

            return Mathf.Min(1f, Mathf.Max(0f, v));
        }

        // ------------------------------------------------ 调试

        public static int CurrentStorageVersion
        {
            get { return CurrentVersion; }
        }

        /// <summary>清空存档。切账号 / 自测用。</summary>
        public static void ClearStorage()
        {
            PlayerPrefs.DeleteKey(StorageKey);
            PlayerPrefs.Save();
            FreshData();
        }
    }
}
