using System;

namespace CloudEyrie
{
    /// <summary>
    /// 一帧在图集页里的位置。
    ///
    /// ⚠️ <b>左上原点</b> —— 与 Cocos 的 <c>SpriteFrame.rect</c> 同约定，
    /// 也是 <c>pack_atlas.py</c> 写出来的约定。
    ///
    /// <b>但 Unity 的 <see cref="UnityEngine.Sprite.rect"/> 是左下原点</b>，
    /// 所以构造 Unity 精灵时**必须翻转 Y</b>：
    /// <code>y_unity = texHeight - (entry.y + entry.h)</code>
    /// 见 <see cref="SequencePlayer"/>。不翻的话每段动画都会上下镜像，
    /// 而且不报任何错 —— 属于最该拦下的那类静默错误。
    /// </summary>
    [Serializable]
    public struct FrameEntry
    {
        public int page;
        public int x;
        public int y;
        public int w;
        public int h;
    }

    /// <summary>
    /// <c>art-source/_tools/pack_atlas.py</c> 产出的索引格式。
    ///
    /// 这份 JSON 是**引擎无关**的：Unity 侧原样解析即可，不需要改动打包流水线。
    /// </summary>
    [Serializable]
    public class SequenceManifest
    {
        public string name;

        /// <summary>[宽, 高] 出图画布尺寸（像素）</summary>
        public int[] canvas;

        public float fps;
        public int frames;

        /// <summary>"grid" | "single"</summary>
        public string layout;

        /// <summary>[宽, 高] 单页尺寸</summary>
        public int[] pageSize;

        /// <summary>页文件名列表，如 "page_00.png"</summary>
        public string[] pages;

        public FrameEntry[] entries;

        /// <summary>"trim" | "mirror" | null</summary>
        public string loop;

        /// <summary>锚点说明（信息性字段；Cocos 侧构造子帧时并未使用，Unity 侧同样按中心锚点）</summary>
        public string anchor;

        public int CanvasWidth
        {
            get { return canvas != null && canvas.Length > 0 ? canvas[0] : 0; }
        }

        public int CanvasHeight
        {
            get { return canvas != null && canvas.Length > 0 ? canvas[1] : 0; }
        }
    }
}
