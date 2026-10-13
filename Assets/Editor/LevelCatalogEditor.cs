using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CloudEyrie.EditorTools
{
    /// <summary>
    /// 「加一关」的另一半：扫出工程里所有 <see cref="LevelDefinition"/>，按 order 写进
    /// <see cref="LevelCatalog"/>。
    ///
    /// 为什么不做成运行时自动发现：见 <see cref="LevelCatalog"/> 的注释 ——
    /// 自动发现**顺序不可控、缺关卡时也报不出错**。这里做成显式的一步，
    /// 加完关卡跑一次，顺序和缺漏都看得见。
    /// </summary>
    public static class LevelCatalogEditor
    {
        private const string CatalogPath = "Assets/Resources/LevelCatalog.asset";

        [MenuItem("CloudEyrie/重扫关卡目录")]
        public static void Rescan()
        {
            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);

            if (catalog == null)
            {
                Debug.LogError($"[LevelCatalog] 找不到 {CatalogPath}。" +
                               "请先创建：右键 → Create → CloudEyrie/关卡目录。");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:LevelDefinition");
            var found = new List<LevelDefinition>(guids.Length);
            var seenPath = new Dictionary<string, string>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                LevelDefinition def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);

                if (def == null)
                {
                    continue;
                }

                // 重复 id 会静默覆盖 —— 在这里就报出来（LevelCatalog.Validate 也会再查一次）
                if (!string.IsNullOrEmpty(def.id))
                {
                    if (seenPath.TryGetValue(def.id, out string other))
                    {
                        Debug.LogError($"[LevelCatalog] 关卡 id 「{def.id}」在 {other} 与 {path} 里重复了。" +
                                       "ById/Next 只会找到其中一个。");
                    }
                    else
                    {
                        seenPath[def.id] = path;
                    }
                }

                found.Add(def);
            }

            // 按 order，再按显示名兜底（order 相同时顺序才稳定）
            found.Sort((a, b) =>
            {
                int byOrder = a.order.CompareTo(b.order);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(a.displayName, b.displayName);
            });

            // 保留手工条目：扫不到的（比如资产被临时移走）不删，只提醒
            foreach (LevelDefinition old in catalog.levels)
            {
                if (old != null && !found.Contains(old))
                {
                    Debug.LogWarning($"[LevelCatalog] 「{old.name}」这次没扫到 —— " +
                                     "保留在表里，但请确认它是不是被删了或移出了工程。");
                    found.Add(old);
                }
            }

            catalog.levels = found.ToArray();

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            catalog.Validate();

            Debug.Log($"[LevelCatalog] 已写入 {found.Count} 关：" +
                      string.Join(" → ", System.Array.ConvertAll(found.ToArray(),
                          d => string.IsNullOrEmpty(d.displayName) ? d.name : d.displayName)));
        }
    }
}
