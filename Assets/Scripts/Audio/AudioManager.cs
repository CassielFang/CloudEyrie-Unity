using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>可独立调节的音轨分组（不含 master，master 是总开关）。</summary>
    public enum AudioBus
    {
        Bgm,
        Sfx,
    }

    /// <summary>播 BGM 的选项。填播放时用得上，先定好形状。</summary>
    public struct BgmOptions
    {
        public bool Loop;

        /// <summary>淡入时长 /秒。0 = 不淡入。</summary>
        public float FadeIn;
    }

    /// <summary>播音效的选项。</summary>
    public struct SfxOptions
    {
        /// <summary>相对音量，会在分组音量之上再乘一道</summary>
        public float Volume;
    }

    /// <summary>
    /// 音频管理器：音量 + 播放。对应 Cocos 侧 <c>audio/AudioManager.ts</c>。
    ///
    /// <b>2026-10-07 接上素材了。</b> 美术交了 2 首 BGM + 6 条音效（在
    /// <c>Resources/audio/</c>），<see cref="PlayBgm"/> / <see cref="PlaySfx"/> 从空实现
    /// 换成了真播放。播放用的 <see cref="AudioSource"/> 挂在一个 <c>DontDestroyOnLoad</c>
    /// 的常驻节点上，跨场景不断。
    ///
    /// 具体该放哪一段音、哪个动作配哪条音效，集中在 <see cref="GameAudio"/> 那张表里，
    /// 调用方不直接碰这里的路径。
    ///
    /// 音量这条链路一直是完整的、真的：设置页拖动滑条 → <see cref="SetChannelVolume"/>
    /// → 落盘 → 广播 → <see cref="GetEffectiveVolume"/> 返回主音量 × 分组音量。
    ///
    /// <b>为什么音量要自己封装</b>
    ///
    /// Unity 的 <see cref="AudioSource"/> 只有单实例 <c>volume</c>，引擎不做分组音量聚合
    /// （<c>AudioListener.volume</c> 是唯一的全局音量，但它是个总开关，不构成「主 × 分组」）。
    /// 所以「主音量 × BGM/音效」这套必须自己算、自己往每个 AudioSource 上刷 ——
    /// 这点和 Cocos 侧同构。
    /// </summary>
    public static class AudioManager
    {
        private static bool _initialized;

        private static void EnsureInit()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            // 设置一变就把有效音量刷下去（现在没有 AudioSource 可刷，是空转；
            // 接上播放后这一句就自动生效，不用再改这里）
            EventBus.On<SettingsChangedEvent>(GameEvents.SettingsChanged, OnSettingsChanged);
        }

        private static void OnSettingsChanged(SettingsChangedEvent e)
        {
            if (e.Audio)
            {
                ApplyVolumes();
            }
        }

        // ============================================================ 音量（真的）

        public static float GetMasterVolume()
        {
            return GameSettings.GetVolume(AudioChannel.Master);
        }

        public static void SetMasterVolume(float v)
        {
            EnsureInit();
            GameSettings.SetVolume(AudioChannel.Master, v);
        }

        public static float GetChannelVolume(AudioBus bus)
        {
            return GameSettings.GetVolume(BusToChannel(bus));
        }

        public static void SetChannelVolume(AudioBus bus, float v)
        {
            EnsureInit();
            GameSettings.SetVolume(BusToChannel(bus), v);
        }

        /// <summary>
        /// 三档音量一起恢复默认（主 0.8 / 音乐 0.7 / 音效 0.8）。
        ///
        /// 默认值表在 <see cref="GameSettings"/> 里是私有的，设置页拿不到、也不该硬编码一份，
        /// 所以在这里开个口子 —— 界面只跟 AudioManager 说话。
        /// </summary>
        public static void ResetAudio()
        {
            EnsureInit();
            GameSettings.ResetAudio();
        }

        /// <summary>实际生效的音量 = 主音量 × 分组音量。播放时用这个值，不要直接用分组值。</summary>
        public static float GetEffectiveVolume(AudioBus bus)
        {
            return GetMasterVolume() * GetChannelVolume(bus);
        }

        /// <summary>
        /// 把一个 <see cref="AudioSource"/> 的音量刷成它所在分组的有效音量。
        /// 接上播放后，每次新建/复用 AudioSource 都要调一次。
        /// </summary>
        public static void ApplyToSource(AudioSource source, AudioBus bus, float scale = 1f)
        {
            if (source == null)
            {
                return;
            }

            source.volume = GetEffectiveVolume(bus) * scale;
        }

        /// <summary>把有效音量刷到**所有**在管的 AudioSource 上。设置变更时自动调用。</summary>
        public static void ApplyVolumes()
        {
            EnsureInit();

            if (_bgmSource != null)
            {
                _bgmSource.volume = GetEffectiveVolume(AudioBus.Bgm);
            }

            foreach (AudioSource s in SfxPool)
            {
                if (s != null)
                {
                    s.volume = GetEffectiveVolume(AudioBus.Sfx);
                }
            }
        }

        private static AudioChannel BusToChannel(AudioBus bus)
        {
            return bus == AudioBus.Bgm ? AudioChannel.Bgm : AudioChannel.Sfx;
        }

        // ============================================================ 播放

        /// <summary>音效的声道数（同时能响几个）。超过就抢最早那个。</summary>
        private const int SfxVoices = 8;

        /// <summary>
        /// 播放用的常驻节点。
        ///
        /// ⚠️ 必须 <c>DontDestroyOnLoad</c>：BGM 要跨场景不断（首页 → 关卡 → 结束页），
        /// 挂在场景里的 <see cref="AudioSource"/> 一切场景就跟着没了，音乐会断。
        /// </summary>
        private static GameObject _host;

        private static AudioSource _bgmSource;
        private static AudioClip _bgmClip;

        private static readonly List<AudioSource> SfxPool = new List<AudioSource>();
        private static int _sfxCursor;

        private static void EnsureHost()
        {
            // Unity 的「假 null」：节点被销毁后引用还在，得用 == null 判
            if (_host != null)
            {
                return;
            }

            // ⚠️ 名字**不能叫 "AudioManager"**。
            //
            // 起因：曾经 Title 场景里有个叫 `AudioManager` 的空物体（只剩 Transform），
            // 而 `GameObject.Find` 会先撞上它 —— 调试时按名字找常驻节点，找到的是那个空壳，
            // 很容易误判成「节点没建出来」。
            //
            // 现状（2026-10-07 核实）：**那个空壳已经不在任何场景里了**
            // （Bootstrap / Title / Main / Ending 四个场景逐个查过，没有只剩 Transform 的物体，
            //  全工程搜 `AudioManager` 也只命中 ProjectSettings/AudioManager.asset —— 那是 Unity 的音频设置）。
            // 所以这条改名约束现在是「历史遗留的保险」，保留它只是不想再踩同一个坑：
            // 常驻节点一律叫 AudioRuntime，和场景里的东西不重名。
            _host = new GameObject("AudioRuntime");
            Object.DontDestroyOnLoad(_host);

            _bgmSource = _host.AddComponent<AudioSource>();
            _bgmSource.playOnAwake = false;
            _bgmSource.loop = true;

            for (int i = 0; i < SfxVoices; i++)
            {
                AudioSource s = _host.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                SfxPool.Add(s);
            }
        }

        /// <summary>
        /// 播放背景音乐，**单声道独占**。
        ///
        /// 同一首正在播时**不重头放** —— 否则每次进新场景都要从头起一遍，
        /// 听感是「每过一个段落音乐就咔一下重来」。
        /// </summary>
        public static void PlayBgm(AudioClip clip, BgmOptions opts = default)
        {
            if (clip == null)
            {
                return;
            }

            EnsureInit();
            EnsureHost();

            if (_bgmSource == null)
            {
                return;
            }

            if (_bgmClip == clip && _bgmSource.isPlaying)
            {
                return;
            }

            _bgmClip = clip;
            _bgmSource.clip = clip;
            _bgmSource.loop = opts.Loop;

            float target = GetEffectiveVolume(AudioBus.Bgm);

            if (opts.FadeIn > 0f)
            {
                _bgmSource.volume = 0f;
                FadeTo(_bgmSource, target, target / opts.FadeIn);
            }
            else
            {
                _bgmSource.volume = target;
            }

            _bgmSource.Play();
        }

        /// <summary>停止背景音乐。</summary>
        public static void StopBgm()
        {
            _bgmClip = null;

            if (_bgmSource != null)
            {
                _bgmSource.Stop();
            }
        }

        /// <summary>
        /// 播放一次性音效。走池子而不是每次新建 <see cref="AudioSource"/> ——
        /// 战斗里一秒能响好几声，边打边 new 会一路堆垃圾。
        ///
        /// 用 <c>PlayOneShot</c> 而不是 <c>Play</c>：同一个声道要能**叠加**播同一段音效
        /// （连砍两刀不能把第一刀掐掉）。
        /// </summary>
        public static void PlaySfx(AudioClip clip, SfxOptions opts = default)
        {
            if (clip == null)
            {
                return;
            }

            EnsureInit();
            EnsureHost();

            if (SfxPool.Count == 0)
            {
                return;
            }

            // 优先挑空闲的；全忙就轮转抢最早那个
            AudioSource src = null;

            for (int i = 0; i < SfxPool.Count; i++)
            {
                AudioSource s = SfxPool[i];

                if (s != null && !s.isPlaying)
                {
                    src = s;
                    break;
                }
            }

            if (src == null)
            {
                src = SfxPool[_sfxCursor % SfxPool.Count];
                _sfxCursor++;
            }

            if (src == null)
            {
                return;
            }

            float scale = opts.Volume > 0f ? opts.Volume : 1f;
            ApplyToSource(src, AudioBus.Sfx, scale);
            src.PlayOneShot(clip, 1f);
        }

        /// <summary>
        /// 把音量渐变到目标值。挂在常驻节点上，走 <c>unscaledDeltaTime</c> ——
        /// 暂停 / 慢镜头时音乐渐入不该跟着停。
        /// </summary>
        private static void FadeTo(AudioSource source, float target, float speed)
        {
            Fader fader = _host.AddComponent<Fader>();
            fader.Source = source;
            fader.Target = target;
            fader.Speed = Mathf.Max(0.0001f, speed);
        }

        private class Fader : MonoBehaviour
        {
            public AudioSource Source;
            public float Target;
            public float Speed;

            private void Update()
            {
                if (Source == null)
                {
                    Destroy(this);
                    return;
                }

                Source.volume = Mathf.MoveTowards(Source.volume, Target, Speed * Time.unscaledDeltaTime);

                if (Mathf.Approximately(Source.volume, Target))
                {
                    Destroy(this);
                }
            }
        }
    }
}
