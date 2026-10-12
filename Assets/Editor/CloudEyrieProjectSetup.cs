using UnityEditor;
using UnityEngine;

namespace CloudEyrie.EditorTools
{
    /// <summary>
    /// 把代码里约定的**物理层名**与**排序层名**写进 <c>ProjectSettings/TagManager.asset</c>。
    ///
    /// <b>为什么需要它</b>
    ///
    /// <see cref="PhysicsGroups"/> 声明了 5 个游戏层（<c>World=8 … Arrow=12</c>），
    /// 但 TagManager 里这 5 个槽位**一直是空的** —— 也就是说 Inspector 的 Layer 下拉里
    /// 它们显示为空行。于是摆 prefab 的人只能靠**记忆选数字**，摆错一个数字的后果是
    /// 「这个物体对谁都碰不到」（见 <see cref="PhysicsGroups.Apply"/>：
    /// 它会把 5 个游戏层与「其它一切层」的碰撞全部断开），而且**运行时不会再校正** ——
    /// <c>Apply()</c> 有 <c>_applied</c> 幂等门，跑过一次就永不再来。
    ///
    /// 排序层同理：<see cref="SortingOrders"/> 现在靠一张 <c>const int</c> 表，
    /// 新增一类物件必须改代码。改成「Sorting Layer 名 + 层内 order」两级之后，
    /// 新类别只要在 TagManager 里加一层即可。这里先把层注册好。
    ///
    /// <b>幂等</b>：目标值已存在时不做任何写入（<c>TagManager.asset</c> 是全局资产，
    /// 每次域重载都无条件写会让版本库里出现无意义的 diff）。
    /// </summary>
    [InitializeOnLoad]
    public static class CloudEyrieProjectSetup
    {
        private const string TagManagerPath = "ProjectSettings/TagManager.asset";

        /// <summary>物理层编号 8~12 的名字，顺序必须与 <see cref="PhysicsGroups.GameLayers"/> 一致。</summary>
        private static readonly int[] PhysicsLayerIndices = { 8, 9, 10, 11, 12 };

        /// <summary>
        /// 排序层名。顺序 = 绘制顺序（先画前面的）。
        ///
        /// 与旧的 <see cref="SortingOrders"/> 数值表一一对应：
        /// Background(-200…) → Scenery(-100) → Ground(-50) → Props(0) → Minions(10…)
        /// → Boss(20) → Player(30) → BarrierFront(40) → Fx(60) → WorldHud(80)。
        /// 中间补了 <c>Hazard</c>（危险地形）—— 五行关卡要用，旧表里没有。
        /// </summary>
        private static readonly string[] SortingLayerNames =
        {
            "Background",
            "Scenery",
            "Ground",
            "Props",
            "Hazard",
            "Minions",
            "Boss",
            "Player",
            "BarrierFront",
            "Fx",
            "WorldHud",
        };

        static CloudEyrieProjectSetup()
        {
            // 静态构造器在资源数据库尚未就绪时跑，delayCall 挪到第一次编辑器空闲
            EditorApplication.delayCall += () => Ensure(silent: true);
        }

        [MenuItem("CloudEyrie/工程设置/注册层名与排序层")]
        public static void EnsureFromMenu()
        {
            Ensure(silent: false);
        }

        private static void Ensure(bool silent)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(TagManagerPath);

            if (assets == null || assets.Length == 0 || assets[0] == null)
            {
                Debug.LogError($"[ProjectSetup] 读不到 {TagManagerPath}，层名无法注册。" +
                               "Inspector 里这些层会显示为空行。");
                return;
            }

            SerializedObject so = new SerializedObject(assets[0]);

            bool changed = EnsurePhysicsLayerNames(so);
            changed |= EnsureSortingLayers(so);

            if (!changed)
            {
                if (!silent)
                {
                    Debug.Log("[ProjectSetup] 层名与排序层都已是目标状态，无需改动。");
                }

                return;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            Debug.Log("[ProjectSetup] 已注册物理层名（WORLD/PLAYER/ENEMY/HITBOX/ARROW）" +
                      "与排序层（" + string.Join("/", SortingLayerNames) + "）。");
        }

        /// <summary>把 5 个游戏层的名字写进 TagManager 的 layers 数组。</summary>
        private static bool EnsurePhysicsLayerNames(SerializedObject so)
        {
            SerializedProperty layers = so.FindProperty("layers");

            if (layers == null || !layers.isArray)
            {
                Debug.LogError("[ProjectSetup] TagManager 里找不到 layers 数组。");
                return false;
            }

            string[] want = PhysicsGroups.LayerNames;
            bool changed = false;

            for (int i = 0; i < PhysicsLayerIndices.Length && i < want.Length; i++)
            {
                int index = PhysicsLayerIndices[i];

                if (index >= layers.arraySize)
                {
                    Debug.LogError($"[ProjectSetup] layers 只有 {layers.arraySize} 项，装不下层 {index}。");
                    break;
                }

                SerializedProperty slot = layers.GetArrayElementAtIndex(index);

                if (slot.stringValue == want[i])
                {
                    continue;
                }

                // 只在「空着」或「已经是我们的名字但大小写不同」时才写。
                // 非空且不是我们的名字 → 是别人手工设的，不能覆盖，只提醒。
                if (!string.IsNullOrEmpty(slot.stringValue))
                {
                    Debug.LogWarning($"[ProjectSetup] 层 {index} 已被占用为「{slot.stringValue}」，" +
                                     $"但代码期望「{want[i]}」。**没有覆盖** —— 请人工确认后处理，" +
                                     "否则 PhysicsGroups 的分组会与 Inspector 对不上。");
                    continue;
                }

                slot.stringValue = want[i];
                changed = true;
            }

            return changed;
        }

        /// <summary>把排序层逐个补进 m_SortingLayers（已有的不动）。</summary>
        private static bool EnsureSortingLayers(SerializedObject so)
        {
            SerializedProperty arr = so.FindProperty("m_SortingLayers");

            if (arr == null || !arr.isArray)
            {
                Debug.LogWarning("[ProjectSetup] TagManager 里找不到 m_SortingLayers，排序层未注册。");
                return false;
            }

            bool changed = false;

            for (int i = 0; i < SortingLayerNames.Length; i++)
            {
                string want = SortingLayerNames[i];
                uint wantId = StableId(want);
                int existing = IndexOfSortingLayer(arr, want);

                if (existing < 0)
                {
                    arr.InsertArrayElementAtIndex(arr.arraySize);
                    SerializedProperty slot = arr.GetArrayElementAtIndex(arr.arraySize - 1);

                    SerializedProperty nameProp = slot.FindPropertyRelative("name");
                    SerializedProperty lockedProp = slot.FindPropertyRelative("locked");

                    if (nameProp != null)
                    {
                        nameProp.stringValue = want;
                    }

                    if (lockedProp != null)
                    {
                        lockedProp.boolValue = false;
                    }

                    WriteId(slot, wantId);
                    changed = true;
                    continue;
                }

                // 已存在则**校验并修复** id。
                // 不能只「有名字就跳过」：如果上一次是用 intValue 写进去的负数哈希，
                // 它在文件里已经是 0 了 —— 名字在、层却塌回 Default，光看名字查不出来。
                if (WriteId(arr.GetArrayElementAtIndex(existing), wantId))
                {
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>
        /// 把某个排序层元素的 <c>uniqueID</c> 写成目标值。返回值表示**是否真的改了**。
        ///
        /// ⚠️ <b>必须用 <c>uintValue</c>，不能用 <c>intValue</c>。</b>
        /// 这个字段是 uint32，而 <c>SerializedProperty.intValue</c> 是有符号的 ——
        /// 写入一个「按 int 看是负数」的哈希（约一半的名字都会这样，本工程 11 个里有 4 个），
        /// Unity 会把它当成非法值<b>归零</b>，于是该层塌回 <c>Default</c>、
        /// <c>SortingLayer.NameToID</c> 返回 0。症状是「层加了，却等于没加」，
        /// 而且文件里名字明明在，光看 YAML 看不出来。
        /// </summary>
        private static bool WriteId(SerializedProperty sortingLayerElement, uint wantId)
        {
            SerializedProperty idProp = sortingLayerElement.FindPropertyRelative("uniqueID");

            if (idProp == null || idProp.uintValue == wantId)
            {
                return false;
            }

            idProp.uintValue = wantId;
            return true;
        }

        private static int IndexOfSortingLayer(SerializedProperty arr, string name)
        {
            for (int i = 0; i < arr.arraySize; i++)
            {
                SerializedProperty nameProp = arr.GetArrayElementAtIndex(i).FindPropertyRelative("name");

                if (nameProp != null && nameProp.stringValue == name)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// FNV-1a 32 位 —— 与 Unity 给排序层算 id 的算法一致（实测逐位相符）。
        /// 返回值是 **uint**，因为 <c>uniqueID</c> 是 uint32，不能走有符号路径。
        /// </summary>
        private static uint StableId(string value)
        {
            unchecked
            {
                const uint offset = 2166136261;
                const uint prime = 16777619;

                uint hash = offset;

                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= prime;
                }

                return hash;
            }
        }
    }
}
