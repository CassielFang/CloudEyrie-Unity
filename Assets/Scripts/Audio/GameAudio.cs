using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>游戏里用到的背景音乐。对应 <c>Resources/audio/bgm_*</c>。</summary>
    public enum BgmTrack
    {
        /// <summary>探索段（开场 / 前奏 / 教学 / 过渡）</summary>
        Explore,

        /// <summary>Boss 战</summary>
        Boss,
    }

    /// <summary>游戏里用到的一次性音效。对应 <c>Resources/audio/sfx_*</c>。</summary>
    public enum SfxCue
    {
        /// <summary>剑击 —— 玩家攻击命中 / 挥空</summary>
        Slash,

        /// <summary>格挡</summary>
        Block,

        /// <summary>踏云闪（冲刺）</summary>
        Dash,

        /// <summary>灵炁爆发 —— 双灵合归</summary>
        Spirit,

        /// <summary>狐鸣 —— Boss 阶段转换</summary>
        FoxCry,

        /// <summary>浊湮嗡鸣 —— 浊湮场的声音</summary>
        Corruption,
    }

    /// <summary>
    /// 音频的**调用门面**：把「什么时候放哪一段」集中成一张表。
    ///
    /// 调用方（关卡流程、战斗、Boss AI）只说
    /// <c>GameAudio.PlayBgm(BgmTrack.Boss)</c> / <c>GameAudio.PlaySfx(SfxCue.Slash)</c>，
    /// 不碰资源路径，也不管缓存 —— 路径改动、以后换成 AudioMixer 或者
    /// 换成随机挑选的变体，都只动这一个文件。
    ///
    /// <b>素材来源</b>：美术交的 wav 直接拷进 <c>Resources/audio/</c>（文件名转成 ASCII，
    /// 中文件名走 <c>Resources.Load</c> 容易出岔子）。原始文件在 <c>美术素材/</c> 下：
    /// <code>
    ///   探索BGM-循环版.wav   -> bgm_explore.wav     60s 循环
    ///   音效-剑击.wav        -> sfx_slash.wav       2s
    ///   音效-格挡.wav        -> sfx_block.wav       1s
    ///   音效-踏云闪.wav      -> sfx_dash.wav        2s
    ///   音效-灵炁爆发.wav    -> sfx_spirit.wav      2s
    ///   音效-狐鸣.wav        -> sfx_foxcry.wav      3s
    ///   音效-浊湮嗡鸣.wav    -> sfx_corruption.wav  3s
    /// </code>
    ///
    /// ⚠️ <b><c>bgm_boss.wav</c>（Boss战BGM-循环版）暂时是缺的</b> ——
    /// 那条素材导入时把 Unity 卡死了，已退回美术重出，路径先留在
    /// <see cref="BossBgmPath"/> 上，等新文件放回来就能直接用。
    /// 缺席期间的表现见 <see cref="PlayBgm"/>。
    ///
    /// ⚠️ 这批 wav 的 **RIFF 头里两个 size 字段都是 <c>0xFFFFFFFF</c>**（导出工具没回填），
    /// 是实测出来的（`52 49 46 46 ff ff ff ff 57 41 56 45`）。
    /// 但**这不影响播放、也未必是导入卡死的原因** —— 播放器只读 fmt 块、
    /// 一路解码到文件尾，压根不看那两个字段（实测：几个文件里只有一个导得特别慢，
    /// 其余都是秒级）。<c>tools/fix_wav_headers.py</c> 能把它们补成规范的，
    /// 但那算「顺手修整齐」，不算「修 bug」。内容区没动过（改前后 SHA-256 一致）。
    /// </summary>
    public static class GameAudio
    {
        private const string ExploreBgmPath = "audio/bgm_explore";
        private const string BossBgmPath = "audio/bgm_boss";

        /// <summary>BGM 的淡入时长 —— 切场景时硬起会「啪」一下，淡进来柔和得多。</summary>
        private const float BgmFadeIn = 0.8f;

        private static AudioClip _slash;
        private static AudioClip _block;
        private static AudioClip _dash;
        private static AudioClip _spirit;
        private static AudioClip _foxCry;
        private static AudioClip _corruption;
        private static AudioClip _bgmExplore;
        private static AudioClip _bgmBoss;

        // ============================================================ BGM

        /// <summary>
        /// 切到某一段 BGM。
        ///
        /// <see cref="AudioManager.PlayBgm"/> 对「同一首正在播」是空操作，
        /// 所以段落之间来回切同一首不会把音乐打断重来。
        ///
        /// ⚠️ <b>Boss 曲暂时是缺的</b>（美术交的那条导不进来，已退回重出）。
        /// 缺的时候这里直接 return，不会去停当前这首 —— 也就是
        /// <b>打 Boss 时会继续放探索曲</b>，而不是静音。
        /// 这是**有意的兜底**，不是漏了：静音听起来像音频坏了，接着放只是「还没换」。
        /// 新素材到位后放回去即可，这里不用改。
        /// </summary>
        public static void PlayBgm(BgmTrack track)
        {
            AudioClip clip = track == BgmTrack.Boss ? Bgm(BossBgmPath, ref _bgmBoss)
                                                    : Bgm(ExploreBgmPath, ref _bgmExplore);

            if (clip == null)
            {
                return;
            }

            AudioManager.PlayBgm(clip, new BgmOptions { Loop = true, FadeIn = BgmFadeIn });
        }

        public static void StopBgm()
        {
            AudioManager.StopBgm();
        }

        // ============================================================ 音效

        // ============================================================ 音效响度
        //
        // ⚠️ **响度归一不在这里做，而是烘在 wav 文件里**（跑 tools/normalize_sfx.py）。
        //
        // 为什么不在运行时乘增益 —— **Unity 的 AudioSource.volume 硬夹在 [0,1]**
        // （实测：设 7.47 读回 1.000），PlayOneShot 的 volumeScale 同样是 0..1。
        // 所以「把狐鸣放大 7 倍」这件事运行时**根本做不到**：写一个 >1 的增益
        // 只会被静默夹成 1，听感上等于没加，而且不报错、日志里也看不出来。
        //
        // 这里曾经就挂着一张增益表（狐鸣 ×5.30、格挡 ×1.42 …），实际效果是
        // **只有 <1 的衰减生效**（灵炁爆发、浊湮被压低），>1 的几条一点没抬 ——
        // 正是「除了剑击其他都听不清」的成因。整张表已删除，避免再有人以为它有用。
        //
        // 现行做法：六个文件按「最响 300ms 窗」对齐到剑击的电平（峰值封顶 0.98 FS），
        // 基准是 sfx_slash（唯一听着正好的那条），它的增益恒为 1。要重调就改脚本重跑：
        //
        //     python tools/normalize_sfx.py            # 预演，只报数
        //     python tools/normalize_sfx.py --apply    # 写回 wav
        //
        // ⚠️ 格挡 / 踏云闪会卡在峰值上限上（源峰值已到 −4.4 / −1.6 dBFS），
        // 物理上抬不到和剑击一样响；要拉平得让美术重导这两条。
        //
        // ⚠️ BGM 没动 —— 美工那边正在重制（见类注释）。等新版到位再决定要不要一起归一。

        /// <summary>
        /// 放一条音效。
        ///
        /// <paramref name="volume"/> 是**调用处**的相对音量（一般是 1）。
        /// 最终音量 = 主音量 × 音效分组音量 × <paramref name="volume"/>。
        /// 每条音效之间的响度差**已经烘在 wav 里**（见上面那段说明），不在这里补。
        /// </summary>
        public static void PlaySfx(SfxCue cue, float volume = 1f)
        {
            string path;

            switch (cue)
            {
                case SfxCue.Block: path = "audio/sfx_block"; break;
                case SfxCue.Dash: path = "audio/sfx_dash"; break;
                case SfxCue.Spirit: path = "audio/sfx_spirit"; break;
                case SfxCue.FoxCry: path = "audio/sfx_foxcry"; break;
                case SfxCue.Corruption: path = "audio/sfx_corruption"; break;
                default: path = "audio/sfx_slash"; break;
            }

            AudioClip clip = Load(path);

            if (clip == null)
            {
                return;
            }

            AudioManager.PlaySfx(clip, new SfxOptions { Volume = volume });
        }

        // ============================================================ 缓存

        /// <summary>
        /// 已经警告过的路径。
        ///
        /// ⚠️ 缺素材时**只警告一次**，不能每次调用都喊 ——
        /// <see cref="Bgm"/> 在读到 null 时缓存仍是 null，下次还会再来读一遍；
        /// 素材缺席期间（比如 Boss 曲还在等美术重出）每进一次段落就刷一条警告，
        /// 日志会被冲得没法看。
        /// </summary>
        private static readonly HashSet<string> Warned = new HashSet<string>();

        /// <summary>
        /// 按路径取一次就缓存住。
        ///
        /// ⚠️ 这里**不用** <c>Resources.UnloadAsset</c> 去释放 —— 见项目里那条踩坑记录：
        /// Load 返回的是缓存实例，Unload 会把正在用的那份也卸掉，
        /// 表现为音效忽然哑掉 / 精灵变白块。常驻这一份几 MB 是划算的。
        /// </summary>
        private static AudioClip Load(string path)
        {
            AudioClip clip = Resources.Load<AudioClip>(path);

            if (clip == null && Warned.Add(path))
            {
                Debug.LogWarning($"[GameAudio] 读不到 {path}，对应的音效/音乐不会响" +
                                 "（素材缺席期间只提示这一次）");
            }

            return clip;
        }

        private static AudioClip Bgm(string path, ref AudioClip cache)
        {
            if (cache == null)
            {
                cache = Load(path);
            }

            return cache;
        }
    }
}
