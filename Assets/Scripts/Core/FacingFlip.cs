using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 素材的天然朝向：1 = 这张图本身画的是**朝右**；-1 = 画的是**朝左**。
    /// </summary>
    public enum NaturalFacing
    {
        Left = -1,
        Right = 1,
    }

    /// <summary>
    /// 按角色朝向水平翻转节点 —— 对应 Cocos 侧 <c>core/FacingFlip.ts</c>。
    ///
    /// ⚠️ <b>务必传对 <paramref name="naturalFacing"/></b> —— 它表示**素材本身画的是朝哪边**，
    /// 不是角色朝向。传错的后果是「攻击打到背后」：判定盒和特效都在按角色朝向摆位，
    /// 立绘却停在反方向。
    ///
    /// 美术目前只提供单侧朝向的素材，左右靠镜像表示 —— <b>这是临时方案</b>，
    /// 正式美术（双向立绘 / 骨骼动画）接入后整个文件可以删掉。
    ///
    /// 用 <c>Mathf.Abs(s.x) * sign</c> 而不是直接赋 ±1，有两个原因：
    ///   1. 保留美术可能在 Inspector 里预设的缩放倍数；
    ///   2. 幂等 —— 同一个朝向重复调用不会把自己翻回去。
    /// </summary>
    public static class FacingFlip
    {
        public static void Apply(Transform node, float facing, NaturalFacing naturalFacing)
        {
            if (node == null)
            {
                return;
            }

            // 素材朝向与角色朝向同侧 -> 不翻；异侧 -> 翻
            bool sameSide = (facing >= 0f) == ((int)naturalFacing >= 0);
            float want = Mathf.Abs(node.localScale.x) * (sameSide ? 1f : -1f);

            Vector3 s = node.localScale;
            if (!Mathf.Approximately(s.x, want))
            {
                node.localScale = new Vector3(want, s.y, s.z);
            }
        }
    }
}
