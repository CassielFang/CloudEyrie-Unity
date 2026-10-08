using UnityEngine;
using UnityEngine.UI;

namespace CloudEyrie
{
    /// <summary>
    /// 子面板内容类需要的两个「舞台」能力。
    ///
    /// 面板内容（<c>TitleSubPanels</c> / <c>SettingsPanel</c> / 以后的成就页）都是
    /// **普通工具类**，拿不到 <see cref="ScreenStage"/> 的 protected 方法，
    /// 只能由 <c>TitleScreen</c> 在构造时把这两个能力注入进来。
    ///
    /// 单独一个文件的原因：<c>TitleSubPanels</c> 要用它、<c>SettingsPanel</c> 也要用它
    /// —— 定义放在其中任意一个文件里都会让这两个文件互相引用成环。
    /// </summary>
    public interface PanelDeps
    {
        /// <summary>建一个居中文本，复用首页那套字体与描边设置。</summary>
        Text MakeLabel(Transform parent, string name, string text, int size,
                       Color color, float outline = 2f);

        /// <summary>
        /// 取一张「模板」贴图（面板宣纸、卷轴等）。
        ///
        /// Cocos 那边是从**场景里隐藏的模板节点**克隆 —— 因为那些贴图不在
        /// <c>resources/</c> 下、<c>resources.load</c> 够不着。
        /// Unity 这边已经把它们挪进 <c>Resources/ui_title/</c>，所以直接按名字加载即可，
        /// **不需要场景里摆模板节点**。
        /// </summary>
        Image CloneTemplate(string templateName, string name, Transform parent);

        /// <summary>
        /// 请求关掉当前子面板（回到首页菜单）。
        ///
        /// 设置页是全屏的，「点面板外关掉」那条路走不通（屏幕内没有「外」），
        /// 所以页首那枚返回箭头必须能自己发起关闭 —— 否则鼠标玩家进去就出不来了。
        /// </summary>
        void ClosePanel();
    }
}
