using System;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>五行之一。用于关卡分类、传送门配色、印记收集。</summary>
    public enum ElementKind
    {
        Mu,     // 木
        Huo,    // 火
        Tu,     // 土
        Jin,    // 金
        Shui,   // 水

        /// <summary>中央祭坛（Boss 关）。不属于五行，也不发印记。</summary>
        Altar,
    }

    /// <summary>一个 <see cref="SegmentCue"/> 什么时候触发。</summary>
    public enum CueTrigger
    {
        /// <summary>玩家 x 越过 <see cref="SegmentCue.atX"/> 时（沿用旧 LevelDirector 的推进方式）。</summary>
        OnPlayerX,

        /// <summary>关卡加载完成、交给玩家之前，立刻触发一次。</summary>
        OnStart,

        /// <summary>踩进场景里某个 <see cref="SegmentVolume"/> 时。</summary>
        OnVolumeEnter,

        /// <summary>本关所有敌人被清空时。</summary>
        OnAllEnemiesCleared,
    }

    /// <summary>这一段要不要动相机。</summary>
    public enum CameraMode
    {
        /// <summary>不动。</summary>
        None,

        /// <summary>锁死，不跟随玩家（开场过场 / Boss 战场）。</summary>
        Lock,

        /// <summary>恢复跟随玩家。</summary>
        Unlock,
    }

    /// <summary>段落切换时要做的淡入淡出。</summary>
    public enum FadeAction
    {
        None,

        /// <summary>把整屏压黑，然后淡入到透明（开场用）。</summary>
        BlackThenFadeIn,

        /// <summary>从当前状态淡出到全黑。</summary>
        FadeOut,
    }

    /// <summary>
    /// 关卡里的一次「编排」：什么时候、对相机/背景/BGM/玩家控制做什么。
    ///
    /// <b>为什么把它做成数据</b>
    ///
    /// 旧 <c>LevelDirector.EnterSegment</c> 是一个写死的 <c>switch (seg)</c>：
    /// 「Opening 段就锁相机 + 背景以 18 平移 + 播探索曲」这类规则全在代码里，
    /// 于是**新关卡想配一段自己的编排就必须改代码**。
    /// 搬到数据之后，「进这段锁相机、换 BGM、自动平移背景」纯配置即可。
    ///
    /// <b>诚实的分界：「何时」是数据，「做什么」是组件</b>
    ///
    /// 数据表表达不了 <c>boss.Begin()</c> 这种具体行为。那种情况用
    /// <see cref="emitEvent"/> 发一条事件，由关心它的组件（如 <c>BossAI</c>）自己订阅。
    /// </summary>
    [Serializable]
    public class SegmentCue
    {
        [Tooltip("稳定标识。事件名、关卡内引用都用它（如 \"opening\" / \"boss-p1\"）")]
        public string id;

        [Tooltip("只给人看的说明")]
        public string note;

        [Header("何时触发")]
        public CueTrigger trigger = CueTrigger.OnPlayerX;

        [Tooltip("trigger = OnPlayerX 时的推进阈值")]
        public float atX;

        [Tooltip("trigger = OnVolumeEnter 时，场景里那个 SegmentVolume 节点的名字")]
        public string volumeName;

        [Header("做什么（全是数据，新关卡不用写代码）")]
        public CameraMode camera = CameraMode.None;

        [Tooltip("背景自动平移速度。< 0 = 不改。0 = 停")]
        public float autoPanSpeed = -1f;

        [Tooltip("是否切 BGM")]
        public bool setBgm;

        public BgmTrack bgm = BgmTrack.Explore;

        public FadeAction fade = FadeAction.None;

        [Tooltip("fade 动作的时长 /秒")]
        public float fadeSeconds = 0.8f;

        [Tooltip("把移动权交还玩家（开场过场结束时用）")]
        public bool releasePlayerControl;

        [Tooltip("发一条事件（经 EventBus）。给 Boss 这类「要做具体动作」的组件用")]
        public string emitEvent;
    }

    /// <summary>
    /// 一个关卡模块的全部元数据。
    ///
    /// 关卡 = <c>Assets/Levels/&lt;Id&gt;/</c> 一个自包含文件夹：本资产 + 关卡场景 + 该关美术。
    /// 「加一关」= 复制一个文件夹 + 在 <see cref="LevelCatalog"/> 里登记。
    ///
    /// 形状照抄 <see cref="GameConfig"/>：ScriptableObject + <c>[CreateAssetMenu]</c> + <c>Validate()</c>。
    /// 但**回落策略相反** —— 见 <see cref="LevelCatalog"/> 的注释。
    /// </summary>
    [CreateAssetMenu(fileName = "LevelDefinition", menuName = "CloudEyrie/关卡定义")]
    public class LevelDefinition : ScriptableObject
    {
        [Header("身份")]
        [Tooltip("稳定 ID，存档与传送门引用它")]
        public string id;

        [Tooltip("显示名，如「木·青芽境」")]
        public string displayName;

        public ElementKind element;

        [Tooltip("排序键。小的在前，决定五行推进顺序")]
        public int order;

        [Header("场景")]
        [Tooltip("关卡场景名，必须在 Build Settings 里注册")]
        public string sceneName;

        [Tooltip("本关美术在 Resources 下的前缀（如 \"Mu\"）。\n" +
                 "美术放在 Assets/Levels/<Id>/Resources/<前缀>/ 下。")]
        public string artRoot;

        [Header("出生与边界")]
        [Tooltip("默认出生点：场景里的空节点名")]
        public string defaultSpawnMarker = "Spawn_Default";

        [Tooltip("相机左边界（世界坐标）")]
        public float levelLeft;

        [Tooltip("相机右边界（世界坐标）")]
        public float levelRight = 3200f;

        [Header("纵向边界")]
        [Tooltip("相机纵向跟随的下边界。与 levelTop 相等（默认 0）= **不纵向跟随**，" +
                 "相机保持 Host 场景里摆的固定高度 —— 这是旧行为。")]
        public float levelBottom;

        [Tooltip("相机纵向跟随的上边界。设了之后相机才会跟着玩家上下走。")]
        public float levelTop;

        [Header("表现")]
        public LevelTheme theme;

        [Header("编排")]
        public SegmentCue[] segments = Array.Empty<SegmentCue>();

        [Header("流程")]
        [Tooltip("是否是终点关（中央祭坛）。终点关之间不重置灵炁")]
        public bool isFinal;

        /// <summary>
        /// 跨字段自检，照 <see cref="GameConfig.Validate"/> 的思路。
        ///
        /// 每一条都对应一类**静默失败**：写错了不报错的那些。
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError($"[Level] {name}: id 为空 —— 存档与传送门都靠它引用。", this);
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError($"[Level] {name}: sceneName 为空 —— 加载时会得到一个 null 操作且不报错。", this);
            }

            if (levelRight <= levelLeft)
            {
                Debug.LogError($"[Level] {name}: levelRight({levelRight}) ≤ levelLeft({levelLeft})，" +
                               "相机会没有行程。", this);
            }

            if (theme == null)
            {
                Debug.LogError($"[Level] {name}: 没有指定 LevelTheme —— 背景会是空的，且不报错。", this);
            }
            else if (theme.layers == null || theme.layers.Length == 0)
            {
                Debug.LogError($"[Level] {name}: theme.layers 为空 —— 背景层一个都建不出来。", this);
            }

            if (string.IsNullOrEmpty(defaultSpawnMarker))
            {
                Debug.LogError($"[Level] {name}: defaultSpawnMarker 为空 —— 玩家会落在原点。", this);
            }

            ValidateSegments();
        }

        private void ValidateSegments()
        {
            if (segments == null)
            {
                return;
            }

            float lastX = float.NegativeInfinity;

            for (int i = 0; i < segments.Length; i++)
            {
                SegmentCue cue = segments[i];

                if (cue == null)
                {
                    Debug.LogError($"[Level] {name}: segments[{i}] 是空引用。", this);
                    continue;
                }

                if (string.IsNullOrEmpty(cue.id))
                {
                    Debug.LogError($"[Level] {name}: segments[{i}] 没有 id。", this);
                    continue;
                }

                for (int j = i + 1; j < segments.Length; j++)
                {
                    if (segments[j] != null && segments[j].id == cue.id)
                    {
                        Debug.LogError($"[Level] {name}: 段落 id 「{cue.id}」重复出现 —— " +
                                       "触发会只认第一个。", this);
                    }
                }

                if (cue.trigger != CueTrigger.OnPlayerX)
                {
                    continue;
                }

                // ⚠️ OnPlayerX 的阈值必须单调递增。
                // 旧 LevelDirector 的推进是「找已越过的最后一段」，阈值乱序会让后面的段永远不触发
                // （或者反过来，一进来就跳到很后面）。
                if (cue.atX < lastX)
                {
                    Debug.LogError($"[Level] {name}: 段落「{cue.id}」的 atX={cue.atX} 比前一个小" +
                                   $"（上一个 {lastX}）—— OnPlayerX 是按 x 顺序推进的，乱序会让段落永远不触发。", this);
                }

                lastX = cue.atX;
            }
        }
    }
}
