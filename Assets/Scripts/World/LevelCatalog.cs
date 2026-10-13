using System;
using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 关卡总表 —— 「加一关」的另一半（复制文件夹 + 在这里登记）。
    ///
    /// <b>为什么是显式数组而不是运行时自动发现</b>
    ///
    /// <c>Resources.LoadAll&lt;LevelDefinition&gt;()</c> 也能拿到全部关卡，
    /// 但那样**顺序只能靠 order 字段**，而且**缺关卡时无法报错**——
    /// 少了一关就静默少一关，玩家走到传送门才发现。
    /// 显式数组 + 编辑器重扫（菜单 <b>CloudEyrie/重扫关卡目录</b>）能把这两件事都变成可见的。
    ///
    /// ⚠️ <b>回落策略与 <see cref="GameConfig"/> 相反</b>：
    /// GameConfig 缺资源时回落到代码默认值是对的（少一个数值不该让游戏崩）。
    /// 但本表若回落成空表 → 找不到任何关卡 → 进关时黑屏，**而且没有任何报错**。
    /// 所以这里**硬失败**：报错 + 退回标题。
    /// </summary>
    [CreateAssetMenu(fileName = "LevelCatalog", menuName = "CloudEyrie/关卡目录")]
    public class LevelCatalog : ScriptableObject
    {
        public const string ResourcePath = "LevelCatalog";

        [Tooltip("按推进顺序排列。传送门的\"下一境\"就按这个顺序找")]
        public LevelDefinition[] levels = Array.Empty<LevelDefinition>();

        private static LevelCatalog _current;

        /// <summary>运行时读取入口。惰性加载并缓存资源引用。</summary>
        public static LevelCatalog Current
        {
            get
            {
                if (_current == null)
                {
                    _current = Resources.Load<LevelCatalog>(ResourcePath);

                    if (_current == null)
                    {
                        // 这里**不**造一个空表兜底 —— 空表会让"进不了关"变成"黑屏且无错"
                        Debug.LogError(
                            $"[LevelCatalog] 找不到 Assets/Resources/{ResourcePath}.asset。" +
                            "没有它就无法知道有哪些关卡，进关会失败。" +
                            "请创建：右键 → Create → CloudEyrie/关卡目录，然后跑一次「CloudEyrie/重扫关卡目录」。");
                    }
                }

                return _current;
            }
        }

        public LevelDefinition ById(string id)
        {
            if (string.IsNullOrEmpty(id) || levels == null)
            {
                return null;
            }

            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i] != null && levels[i].id == id)
                {
                    return levels[i];
                }
            }

            return null;
        }

        public int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id) || levels == null)
            {
                return -1;
            }

            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i] != null && levels[i].id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>按顺序找下一关。已经是最后一关则返回 null（终点关）。</summary>
        public LevelDefinition Next(string id)
        {
            int i = IndexOf(id);

            if (i < 0 || levels == null || i + 1 >= levels.Length)
            {
                return null;
            }

            return levels[i + 1];
        }

        /// <summary>第一关。</summary>
        public LevelDefinition First()
        {
            return levels != null && levels.Length > 0 ? levels[0] : null;
        }

        public void Validate()
        {
            if (levels == null || levels.Length == 0)
            {
                Debug.LogError($"[LevelCatalog] {name}: 一关都没有。请跑「CloudEyrie/重扫关卡目录」。", this);
                return;
            }

            var seenIds = new HashSet<string>();

            for (int i = 0; i < levels.Length; i++)
            {
                LevelDefinition def = levels[i];

                if (def == null)
                {
                    Debug.LogError($"[LevelCatalog] {name}: levels[{i}] 是空引用。", this);
                    continue;
                }

                def.Validate();

                if (!string.IsNullOrEmpty(def.id) && !seenIds.Add(def.id))
                {
                    Debug.LogError($"[LevelCatalog] {name}: 关卡 id 「{def.id}」重复 —— " +
                                   "ById/Next 只会找到第一个。", this);
                }

                if (i > 0 && levels[i - 1] != null && def.order < levels[i - 1].order)
                {
                    Debug.LogWarning($"[LevelCatalog] {name}: 「{def.displayName}」的 order({def.order}) " +
                                     $"比上一关（{levels[i - 1].order}）小 —— 数组顺序与 order 不一致，" +
                                     "推进顺序以数组为准。", this);
                }
            }
        }
    }
}
