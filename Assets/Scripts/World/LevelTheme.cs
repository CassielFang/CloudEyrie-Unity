using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 一个关卡的背景与调色。
    ///
    /// <b>为什么单独抽一份</b>：五行五境的差异按设计稿「主要靠调色 + 雾效密度 + 前景元素换色」实现，
    /// 也就是同一个 <see cref="ParallaxBackground"/> 换一套分层规格。
    /// 把「这套规格」做成资产，切关时换主题即可，不必重建背景节点。
    ///
    /// 直接复用 <see cref="ParallaxBackground"/> 已有的
    /// <see cref="LayerSpec"/> / <see cref="ScenerySpec"/>（它们是命名空间级类型）——
    /// 那两个结构的形状（path / factor / yOffset / height / drift / autoPan）已经够用，
    /// 重造一份只会让两处规格漂移。
    /// </summary>
    [CreateAssetMenu(fileName = "LevelTheme", menuName = "CloudEyrie/关卡主题")]
    public class LevelTheme : ScriptableObject
    {
        [Tooltip("视差分层。留空则沿用 ParallaxBackground 的默认层")]
        public LayerSpec[] layers;

        [Tooltip("散布景物（山峰等）")]
        public ScenerySpec[] scenery;

        [Tooltip("覆盖视口宽度。0 = 按相机正交尺寸推算（一般留 0）")]
        public float viewWidthOverride;

        [Header("调色（五境差异主要靠这个）")]
        [Tooltip("整体染色。白 = 原色。压暖 = 火境，压冷 = 水境")]
        public Color tint = Color.white;

        [Tooltip("雾效浓度。0 = 不叠雾")]
        [Range(0f, 1f)]
        public float fogDensity;

        [Tooltip("雾的颜色")]
        public Color fogColor = new Color(0.8f, 0.85f, 0.9f, 0.35f);

        /// <summary>把默认层规格复制一份回来 —— 换主题时想「回到原样」用得上。</summary>
        public void UseDefaults()
        {
            layers = ParallaxBackground.DefaultLayers;
            scenery = ParallaxBackground.DefaultScenery;
        }
    }
}
