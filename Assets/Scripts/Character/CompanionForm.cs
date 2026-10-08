namespace CloudEyrie
{
    /// <summary>
    /// 灵伴三形态。对应 Cocos 侧 <c>character/forms/ICompanionForm.ts</c> 的 <c>CompanionForm</c>。
    ///
    /// TS 那边用字符串枚举（<c>'entity'</c> 等）是因为它会被序列化；Unity 这份不落盘，
    /// 用普通枚举即可。
    /// </summary>
    public enum CompanionForm
    {
        /// <summary>实体形态（青禾主导）</summary>
        Entity,

        /// <summary>灵雾形态（莹翳主导）</summary>
        Mist,

        /// <summary>灵合形态（二人合一）</summary>
        Merge,
    }
}
