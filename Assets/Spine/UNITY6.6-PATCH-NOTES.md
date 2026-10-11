# spine-unity 3.8.0 — Unity 6.6 兼容补丁

**背景**：Unity 升级到 `6000.6.5f1` 后，spine-unity **3.8.0**（2018 年版本）编译失败 → 编辑器进入安全模式。
本目录**未升级 Spine 版本**，只在原地打了最小补丁。升级 spine-unity 4.x 需要重新导出全部骨骼资源，是另一件事。

> ⚠️ **重新导入 / 更新 spine-unity 会覆盖这些补丁，症状（安全模式 + CS0619）会原样复发。**
> 届时按下面的清单重打，或正式迁移到 spine-unity 4.x + 重导资源。

## Unity 6.6 的新规矩

- `Object.GetInstanceID()`、`EditorUtility.InstanceIDToObject(int)`、`AssetDatabase.GetAssetPath(int)`、
  `EditorApplication.hierarchyWindowItemOnGUI` → **error 级废弃**（CS0619，直接挡编译）
- **`EntityId` ↔ `int` 的隐式转换本身也是 error 级**：「EntityId will not be representable by an int in the future」
  → 所以 `(int)o.GetEntityId()` 这种省事写法**不合法**，容器键类型必须真的迁到 `UnityEngine.EntityId`
- `Rigidbody2D.isKinematic`、`FindObjectsOfType` → warning 级（不挡编译，顺手处理）

## 运行时（spine-unity）

| 文件:行 | 改动 |
|---|---|
| `Components/SkeletonMecanim.cs:649-650` | 比较器改用 `GetEntityId()` / `GetEntityId().GetHashCode()` |
| `Components/SkeletonAnimation.cs:51` | `state` 加 `[System.NonSerialized]`（清 UAC1001） |
| `Asset Types/SkeletonDataCompatibility.cs:66` | `compatibleVersions` 加 `[System.NonSerialized]`（清 UAC1009，`int[][]` 本就不可序列化） |
| `Components/SkeletonUtility/SkeletonUtility.cs:126` | `isKinematic = x` → `bodyType = x ? Kinematic : Dynamic` |

## 编辑器（spine-unity-editor）

| 文件:行 | 改动 |
|---|---|
| `Asset Types/SpineAtlasAssetInspector.cs:139,316` | `AssetDatabase.GetAssetPath(texture.GetInstanceID())` → `GetAssetPath(texture)` |
| `Utility/AssetUtility.cs:387` | `GetInstanceID()` → `GetEntityId()` |
| `Utility/AssetUtility.cs:450,452` | `!= 0` → `!= EntityId.None`；`InstanceIDToObject` → `EntityIdToObject` |
| `Utility/DataReloadHandler.cs:52` | `Dictionary<int,string>` → `Dictionary<EntityId,string>` |
| `Utility/DataReloadHandler.cs:87` | `GetInstanceID()` → `GetEntityId()` |
| `Utility/DataReloadHandler.cs:71,82,121,126` | `FindObjectsOfType<T>()` → `FindObjectsByType<T>()` |
| `Utility/SpineEditorUtilities.cs:277-280` | 4 张表 `Dictionary<int,…>` → `Dictionary<EntityId,…>` |
| `Utility/SpineEditorUtilities.cs:318,322,326,330` | `GetInstanceID()` → `GetEntityId()`（并 `FindObjectsOfType` → `FindObjectsByType`） |
| `Utility/SpineEditorUtilities.cs:333,375` | 回调签名 `int instanceId` → `EntityId instanceId` |
| `Utility/SpineEditorUtilities.cs:137,138,297,305` | `hierarchyWindowItemOnGUI` → `hierarchyWindowItemByEntityIdOnGUI` |
| `Utility/SpineEditorUtilities.cs:400` | `InstanceIDToObject` → `EntityIdToObject` |
| `Components/SkeletonUtilityBoneInspector.cs:339` | `isKinematic = true` → `bodyType = Kinematic`（**仅 2D；同文件 :470 是 3D `Rigidbody`，未废弃，勿改**） |

## 游戏代码

| 文件:行 | 改动 |
|---|---|
| `Assets/Scripts/Character/QingheController.cs:671,705,778` | `_groundContacts` 由 `HashSet<int>` 改为 `HashSet<EntityId>`，`GetInstanceID()` → `GetEntityId()` |

## 已知遗留（warning 级，不挡编译，未处理）

- `Utility/BuildSettings.cs:103,111,132,140`：`PlayerSettings.Get/SetScriptingDefineSymbolsForGroup` → 建议迁 `NamedBuildTarget`
- `Asset Types/SpineAtlasAssetInspector.cs:319,375`：`TextureImporter.spritesheet` 已废弃，Unity 官方建议改用 `ISpriteEditorDataProvider`（迁移量较大）

## 验证方式

不启动 Unity 的本地全量编译（详见项目 memory `unity-local-compile-verification`）：
用 Unity 自带 Roslyn + `Editor\Data\Managed` 引用程序集 + `Library\Bee\artifacts\*.dag\*.rsp`（Unity 真实编译参数）
+ `Tools\BuildPipeline\Unity.Analyzers.*`（挂 **两个** 分析器 UAC 警告才会跑）。
