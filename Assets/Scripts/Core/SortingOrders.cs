namespace CloudEyrie
{
    /// <summary>
    /// 2D 渲染层次。
    ///
    /// Cocos 那边靠 `Canvas` 下的 **sibling index** 决定前后关系（代码里到处
    /// `setSiblingIndex` / `setParent`）。Unity 的 SpriteRenderer 用
    /// <see cref="UnityEngine.Renderer.sortingOrder"/>，所以把那份约定**集中成一张表**，
    /// 免得每个文件各写一套魔数、改一处忘一处。
    ///
    /// 顺序（数值小的在后、先画）：
    /// <code>
    /// 视差远景 → 视差近景 → 散布景物 → 关卡道具(屏障左半/灵泉) → 小怪
    ///   → Boss → 青禾 → 屏障右半 → 特效 → HUD
    /// </code>
    /// 「屏障右半在青禾之后」是为了让角色穿门时像**走进门里**（见 <see cref="LevelDirector"/>）。
    /// </summary>
    public static class SortingOrders
    {
        /// <summary>视差背景层的起点：层按数组顺序在此之上递增</summary>
        public const int BackgroundLayerStart = -200;

        /// <summary>散布景物（山峰）</summary>
        public const int Scenery = -100;

        /// <summary>
        /// 地面美术条。
        /// 压在景物之上、道具/角色之下 —— 对应 Cocos 里 `GroundArt` 在 Canvas 子节点中的位置
        /// （在 `Background` 之后、`Level`/角色之前）。
        /// </summary>
        public const int GroundArt = -50;

        /// <summary>关卡道具：灵界屏障左半、灵泉石台</summary>
        public const int LevelProp = 0;

        /// <summary>
        /// 小怪。
        ///
        /// ⚠️ 同屏多只时**必须再按序号错开**（<c>Minion + i</c>）。
        /// <see cref="Renderer.sortingOrder"/> 完全相同时，Unity 只能退回到「按与相机的距离」
        /// 排序；而小怪 z 都是 0、距离相等，排序结果就**不稳定** —— 两只重叠时看着像
        /// 「互相穿透」，还会随移动闪。各错开一层，「谁恒定在上面」就是确定的。
        /// 小怪占 10~19 这一段，别往上排到 <see cref="Boss"/>。
        /// </summary>
        public const int Minion = 10;

        /// <summary>Boss（含分身）</summary>
        public const int Boss = 20;

        /// <summary>青禾</summary>
        public const int Player = 30;

        /// <summary>
        /// <b>环绕类特效的「远侧」</b>：轨道粒子 / 光环画在**上半圈**的那一半。
        ///
        /// 夹在 <see cref="Boss"/>(20) 与 <see cref="Player"/>(30) 之间 ——
        /// 盖得住小怪和 Boss，但会被青禾挡住。
        ///
        /// <b>为什么环绕物必须分两层</b>：整圈放同一层的话，无论放人前还是人后，
        /// 都是「要么整个圈挡在人身上、要么人被圈整个挡住」—— 看着像贴纸，
        /// 读不出「套在身上」。远侧在人后、近侧在人前，才有立体感。
        /// 用两张**半环**贴图实现（本项目特效是普通 SpriteRenderer，加不了遮罩）：
        /// 见 <c>tools/gen_fx_sprites.py</c> 的 glow_ring_half。
        /// </summary>
        public const int FxOrbitFar = Player - 1;

        /// <summary>
        /// <b>环绕类特效的「近侧」</b>：轨道粒子 / 光环画在**下半圈**的那一半 ——
        /// 朝观众的那半，要盖住青禾。
        ///
        /// 紧贴青禾之前：够盖住人，又不至于盖到 <see cref="BarrierFront"/>(40)
        /// 和普通特效 <see cref="Fx"/>(60)。
        /// </summary>
        public const int FxOrbitNear = Player + 1;

        /// <summary>灵界屏障右半 —— 必须在玩家之后</summary>
        public const int BarrierFront = 40;

        /// <summary>命中火花 / 灵弹等短命特效</summary>
        public const int Fx = 60;

        /// <summary>世界空间的 HUD（如 Boss 头顶指示器）</summary>
        public const int WorldHud = 80;
    }
}
