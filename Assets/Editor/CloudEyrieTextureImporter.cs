using UnityEditor;
using UnityEngine;

namespace CloudEyrie.EditorTools
{
    /// <summary>
    /// 云岫美术资源的纹理导入规则。
    ///
    /// 为什么要有它：本作的美术全部是**运行时按名字加载**的
    /// （<c>Resources.Load&lt;Sprite&gt;("背景/远景")</c>、
    /// <c>Resources.LoadAsync&lt;Texture2D&gt;("video_fx/…/page_00")</c>），
    /// 一旦某张图被导成 <c>Texture Type: Default</c>，<c>Resources.Load&lt;Sprite&gt;</c>
    /// 就会返回 null —— 而且**不报错**，只表现为「那张图不见了」。
    /// 143 张图手工核对不现实，所以用导入器统一钉死。
    ///
    /// 规则：
    /// <list type="bullet">
    ///   <item>Sprite 类型、像素单位 100（与 Cocos 的 <c>pixelsToUnit = 100</c> 一致）</item>
    ///   <item>不生成 mipmap、不压缩边缘、Clamp 寻址（长卷背景平铺会串边）</item>
    ///   <item>保留 alpha（水墨素材大量依赖柔和透明边）</item>
    /// </list>
    /// </summary>
    public class CloudEyrieTextureImporter : AssetPostprocessor
    {
        /// <summary>
        /// ⚠️ <b>必须是 1，不是 100。</b>
        ///
        /// Cocos 的 UI 世界里 **1 个图集像素 = 1 个世界单位**（Sprite 按
        /// <c>UITransform.contentSize</c> 渲染，与引擎的 <c>pixelsToUnit=100</c> 无关）——
        /// 证据：地面碰撞体 4400 宽、青禾碰撞体 150 高、关卡 3200 长，
        /// 这些数字就是设计像素数。
        ///
        /// 而 Unity 的 SpriteRenderer 默认 100 像素/单位，照默认建的话：
        /// 小怪帧 256px 会变成 2.56 个世界单位，比它的碰撞体（130 高）小两个数量级，
        /// **整个场景的比例会全错**。
        ///
        /// 三处独立数据都指向「像素 = 单位」：
        /// <code>
        /// 小怪帧 256px × 0.66 ≈ 169 单位
        /// Boss 帧 384px × 1.2  ≈ 460 单位
        /// 青禾立绘 1370px × 0.27 ≈ 370 单位
        /// </code>
        /// </summary>
        private const float PixelsPerUnit = 1f;

        /// <summary>序列帧图集页所在的目录。这一批很大，压缩策略单独处理。</summary>
        private const string SequenceRoot = "Assets/Resources/video_fx/";

        private void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');

            // ⚠️ 判据是「**任意层级**的 Resources 目录」，不是只看根目录那一个。
            //
            // Unity 把**任何**名为 `Resources` 的文件夹都并入全局 Resources 命名空间
            // （`Resources.Load` 只认相对路径，不认它在哪一层），所以嵌套 Resources 下的美术
            // **本来就必须** ppu=1。原来写成 `StartsWith("Assets/Resources/")` 时，
            // `Assets/Levels/Mu/Resources/…` 会被判成「非美术」→ 走 Unity 默认 ppu=100 →
            // `Resources.Load<Sprite>` 返回 null，**且不报任何错**。
            //
            // 关卡模组化后每关都有自带的嵌套 Resources，所以这条不是可选项。
            // 用 Contains("/Resources/") 而不是 EndsWith/StartsWith：前者同时覆盖
            // 根目录与任意深度的嵌套；前后的斜杠保证 `Assets/MyResources/` 这类不会被误判。
            bool isArt =
                path.Contains("/Resources/") ||
                path.StartsWith("Assets/Art/");

            if (!isArt)
            {
                return;
            }

            TextureImporter importer = (TextureImporter)assetImporter;
            bool isSequencePage = path.StartsWith(SequenceRoot);

            importer.textureType = TextureImporterType.Sprite;

            // Sprite 模式下单一精灵；具体切分由运行时按 manifest 做（Sprite.Create），
            // 编辑器这边不需要切，也不该切成 Multiple 浪费导入时间
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;

            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;

            // 长卷背景横向很宽，repeat 寻址会在接缝处串边
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;

            // 图集页可达 1930×1930，默认 2048 的顶格很危险（超一点就被缩、帧会错位）
            importer.maxTextureSize = isSequencePage ? 4096 : 4096;

            // 压缩策略：
            //   序列帧（143 张 / 294MB，占全部素材的 8 成）走正常压缩 —— 全不压缩 Library 会膨胀到 GB 级；
            //   其余（背景 / 立绘 / UI）走不压缩 —— 水墨淡彩的柔和渐变上 DXT 的色带很明显。
            importer.textureCompression = isSequencePage
                ? TextureImporterCompression.Compressed
                : TextureImporterCompression.Uncompressed;

            // 不自动裁透明边：帧的 canvas 尺寸是 manifest 里写死的，
            // 编辑器再裁一刀会让 Sprite 的 rect 与 manifest 对不上、整段动画错位。
            //
            // ⚠️ `spriteMeshType` 不在 TextureImporter 上，得走 TextureImporterSettings
            // （Unity 6 里 TextureImporter 没有这个属性，直接写 `importer.spriteMeshType`
            // 编不过 —— CS1061）。
            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;

            // 宣纸面板的九宫格切边。
            //
            // Cocos 那边是**运行时**补的（切边只写在 README 里、没进 .meta）：
            //   f.insetLeft = f.insetRight = f.insetTop = f.insetBottom = 72;
            // 不切的话 1024×1024 的方图压到 860×540，笔锋粗细两个方向会差 1.6 倍。
            //
            // ⚠️ Unity 的 slice 边界属于 **sprite 资产**（`Sprite.border`），
            // 运行时改不了 —— 只能在导入时设好。所以这条规则挪到了这里。
            if (path.EndsWith("Resources/ui_title/panel_paper.png"))
            {
                settings.spriteBorder = new Vector4(72f, 72f, 72f, 72f);
            }

            importer.SetTextureSettings(settings);
        }
    }
}
