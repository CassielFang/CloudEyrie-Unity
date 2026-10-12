using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CloudEyrie.EditorTools
{
    /// <summary>
    /// 场景体检：抓那些**不报错、但一定出问题**的摆放错误。
    ///
    /// <b>为什么必须有它</b>
    ///
    /// 本工程最贵的一类静默失败是「物体留在 <c>Default</c> 层」。
    /// <see cref="PhysicsGroups.Apply"/> 会把 5 个游戏层与「其它一切层」的碰撞**全部断开**，
    /// 于是留在 Default 层的碰撞体**对谁都碰不到** —— 而且
    /// <c>Apply()</c> 有 <c>_applied</c> 幂等门，**运行时永远不会再校正**。
    /// 表现就是「这个门拦不住人」「这块地形踩上去掉下去」，没有任何报错。
    ///
    /// 关卡模组化之后，会有更多人摆更多 prefab，这类错误会从**偶发**变成**高频** ——
    /// 所以这个体检不是锦上添花，是让「可视化编辑」这件事不失控的前提。
    ///
    /// 用法：菜单 <b>CloudEyrie/校验当前场景</b>。改动关卡后提交前跑一次。
    /// </summary>
    public static class CloudEyrieSceneAudit
    {
        /// <summary>场景里**只应存在一个**的关键组件。多了说明 additive 加载出了岔子。</summary>
        private static readonly (string label, System.Type type)[] Singletons =
        {
            ("玩家控制器", typeof(QingheController)),
            ("形态状态机", typeof(CompanionStateMachine)),
            ("战斗控制器", typeof(CombatController)),
            ("视差背景", typeof(ParallaxBackground)),
            ("相机跟随", typeof(CameraFollow)),
            ("常驻游戏管理", typeof(GameManager)),
        };

        /// <summary>
        /// 这些节点名下的 SpriteRenderer 是**代码在运行时**赋图的，场景里空着是正常的：
        /// <list type="bullet">
        ///   <item><c>ArtSprite</c> —— 青禾的静态立绘备用节点（装了 Spine 时本来就是空的）</item>
        ///   <item><c>Art</c> —— 角色/召唤物的美术宿主，由 <c>SequencePlayer</c> 逐帧填图</item>
        /// </list>
        /// 不豁免的话它们会淹没真问题（体检一旦开始误报，人就不看了）。
        /// </summary>
        private static readonly HashSet<string> RuntimeDrivenArt = new HashSet<string>
        {
            "ArtSprite",
            "Art",
        };

        [MenuItem("CloudEyrie/校验当前场景")]
        public static void Audit()
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            CheckPhysicsLayers(errors);
            CheckNullSprites(warnings);
            CheckDuplicateSingletons(errors);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("===== 场景体检 =====");

            if (errors.Count == 0 && warnings.Count == 0)
            {
                sb.AppendLine("✅ 未发现问题。");
                Debug.Log(sb.ToString());
                return;
            }

            foreach (string e in errors)
            {
                sb.AppendLine("❌ " + e);
            }

            foreach (string w in warnings)
            {
                sb.AppendLine("⚠️ " + w);
            }

            sb.AppendLine($"合计：{errors.Count} 个错误、{warnings.Count} 个警告。");

            if (errors.Count > 0)
            {
                // 用 LogError 让它任何构建里都吵 —— 这类问题只会在运行时表现为"莫名其妙"
                Debug.LogError(sb.ToString());
            }
            else
            {
                Debug.LogWarning(sb.ToString());
            }
        }

        /// <summary>
        /// 带碰撞体的物体必须在 5 个游戏层之一。
        ///
        /// 只查**参与物理**的东西（Collider2D / Rigidbody2D / TilemapCollider2D）——
        /// 纯空节点、Canvas、UI 留在 Default 是正常的，报出来只会淹没真问题。
        /// </summary>
        private static void CheckPhysicsLayers(List<string> errors)
        {
            var reported = new HashSet<EntityId>();

            foreach (Collider2D col in FindAll<Collider2D>())
            {
                Report(col.gameObject, col.GetType().Name);
            }

            foreach (Rigidbody2D body in FindAll<Rigidbody2D>())
            {
                Report(body.gameObject, "Rigidbody2D");
            }

            void Report(GameObject go, string what)
            {
                // ⚠️ Unity 6.6：GetInstanceID() 是 error 级废弃，必须用 GetEntityId()
                if (go.layer != 0 || !reported.Add(go.GetEntityId()))
                {
                    return;
                }

                errors.Add($"{Path(go)} 挂着 {what}，却在 Default 层。" +
                           "PhysicsGroups 会把游戏层与 Default 断开 → 它对谁都碰不到。" +
                           "请改到 WORLD / PLAYER / ENEMY / HITBOX / ARROW 之一。");
            }
        }

        /// <summary>
        /// SpriteRenderer 没图。
        ///
        /// 这里是 **warning 不是 error**：运行时新建的 SpriteRenderer 在场景里根本不存在，
        /// 而<b>故意</b>留空的少量节点（例如青禾的静态立绘备用节点 <c>ArtSprite</c>，
        /// 装了 Spine 时它本来就是空的）会误报。人工判一眼即可。
        /// </summary>
        private static void CheckNullSprites(List<string> warnings)
        {
            foreach (SpriteRenderer sr in FindAll<SpriteRenderer>())
            {
                if (sr.sprite != null)
                {
                    continue;
                }

                if (RuntimeDrivenArt.Contains(sr.gameObject.name))
                {
                    continue;
                }

                warnings.Add($"{Path(sr.gameObject)} 的 SpriteRenderer 没有 sprite" +
                             "（若它本该由代码在运行时赋值，可忽略）。");
            }
        }

        /// <summary>
        /// 场景里只该有一份的关键组件。
        ///
        /// 做 additive 加载之后，「关卡场景误放了 GameManager / Canvas」这类错误会变成现实风险 ——
        /// 而 <c>GameManager</c> 现在是靠「后到者自杀」来兜的，多个副本会互相干扰。
        /// </summary>
        private static void CheckDuplicateSingletons(List<string> errors)
        {
            foreach ((string label, System.Type type) in Singletons)
            {
                // ⚠️ Unity 6.6：带 FindObjectsSortMode 的重载已废弃（CS0618），
                //    新重载不接受排序参数。这里不需要顺序，正好。
                Object[] found = Object.FindObjectsByType(type, FindObjectsInactive.Include);

                if (found == null || found.Length <= 1)
                {
                    continue;
                }

                var names = new StringBuilder();

                foreach (Object o in found)
                {
                    names.Append(names.Length > 0 ? "、" : "");
                    names.Append(o is Component c ? Path(c.gameObject) : o.name);
                }

                errors.Add($"场景里有 {found.Length} 个「{label}」({type.Name})，只应有 1 个：{names}");
            }
        }

        private static T[] FindAll<T>() where T : Component
        {
            // 同 CheckDuplicateSingletons：6.6 起不带 SortMode 的重载才是新的
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include);
        }

        /// <summary>拼出层级路径，让日志能直接定位到对象。</summary>
        private static string Path(GameObject go)
        {
            var sb = new StringBuilder(go.name);
            Transform t = go.transform.parent;

            while (t != null)
            {
                sb.Insert(0, t.name + "/");
                t = t.parent;
            }

            return "[" + sb + "]";
        }
    }
}
