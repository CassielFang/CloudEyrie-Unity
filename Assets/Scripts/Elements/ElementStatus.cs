using UnityEngine;

namespace CloudEyrie.Elements
{
    /// <summary>格子里的"材质"。数量刻意压到最小 —— 能互相作用就够了，不做 Noita 的完整元素表。</summary>
    public enum MaterialType : byte
    {
        Empty = 0,

        /// <summary>实心（地面/岩壁/冷却后的黑曜石）</summary>
        Solid = 1,

        Water = 2,
        Lava = 3,

        /// <summary>可燃物（草、藤、油）——本身不画，只做传播介质</summary>
        Flammable = 4,

        /// <summary>火焰（有寿命，会向可燃邻居蔓延）</summary>
        Fire = 5,

        /// <summary>灰烬 / 蒸汽（无作用，只做残留表现）</summary>
        Residue = 6,

        /// <summary>流沙 / 尘土：会下落、会堆积，遇水沉为实心 —— 土境的主元素</summary>
        Sand = 7,
    }

    /// <summary>实体身上的元素状态。位标志，可以同时"潮湿且燃烧"（潮湿会先挡住燃烧）。</summary>
    [System.Flags]
    public enum ElementStatus
    {
        None = 0,
        Burning = 1 << 0,
        Wet = 1 << 1,
        Oiled = 1 << 2,
        Frozen = 1 << 3,
        Chilled = 1 << 4,
    }
}
