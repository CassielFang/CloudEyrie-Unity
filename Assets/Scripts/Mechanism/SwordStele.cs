using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 剑碑序列。设计稿金境段3「剑碑序列」：
    /// 三座古剑碑，按灵炁微光的提示顺序用**灵合态共鸣**点亮；顺序错误则**全部熄灭重来**。
    /// 全部点亮后剑气门解除。
    ///
    /// <b>为什么必须是灵合态，而不是"随便什么形态靠近就行"</b>
    ///
    /// 灵合在本作里是"二人合一"的最高形态，整条主线里它的定位是**共鸣/净化**。
    /// 剑碑用灵合点亮，是在玩法上再讲一遍这件事。
    /// 而且它是**五境里唯一要求灵合的机关** —— 若用灵雾也能点亮，
    /// 玩家整局都不会有动力去碰灵合（它消耗最大、冷却最长）。
    ///
    /// <b>顺序错误为什么要"全部熄灭"而不是"这一步不算"</b>
    ///
    /// 后者会让玩家一路乱点也总能蒙对（3 座碑只有 6 种排列，代价太低）。
    /// 全灭重来才让"看提示、记顺序"变成真的需要做的事 ——
    /// 这也是设计稿里这一段被称为"解谜"而不是"跑图"的原因。
    /// </summary>
    public class SwordStele : LevelMechanism
    {
        [Header("序列")]
        [Tooltip("这一座是序列里的第几位（从 1 开始）。三座碑分别填 1/2/3")]
        [SerializeField] private int order = 1;

        [Tooltip("同一组的碑共享序列状态，填同一个 id")]
        [SerializeField] private string groupId = "jin_03";

        [Tooltip("共鸣需要玩家处于灵合态")]
        [SerializeField] private bool requireMergeForm = true;

        [Tooltip("共鸣的触发半径（世界单位）")]
        [SerializeField] private float resonateRadius = 120f;

        [Header("表现")]
        [Tooltip("未点亮时的立绘")]
        [SerializeField] private GameObject visualOff;

        [Tooltip("点亮后的立绘")]
        [SerializeField] private GameObject visualOn;

        [Tooltip("碑身的古篆数字微光（提示顺序）。留空则跳过")]
        [SerializeField] private SpriteRenderer orderGlyph;

        /// <summary>每组的当前进度。</summary>
        private class GroupState
        {
            public int Progress;
            public readonly List<SwordStele> Steles = new List<SwordStele>();
        }

        private static readonly Dictionary<string, GroupState> Groups =
            new Dictionary<string, GroupState>();

        public bool IsLit { get; private set; }

        /// <summary>整组是不是点亮完了。</summary>
        public static bool GroupComplete(string id)
        {
            if (string.IsNullOrEmpty(id) || !Groups.TryGetValue(id, out GroupState st) || st.Steles.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < st.Steles.Count; i++)
            {
                if (st.Steles[i] != null && !st.Steles[i].IsLit)
                {
                    return false;
                }
            }

            return true;
        }

        private void Awake()
        {
            gameObject.layer = PhysicsGroups.World;

            if (!Groups.TryGetValue(groupId, out GroupState st))
            {
                st = new GroupState();
                Groups[groupId] = st;
            }

            st.Steles.Add(this);
            BuildOrderGlyph();
            Refresh();
        }

        private void OnDestroy()
        {
            if (Groups.TryGetValue(groupId, out GroupState st))
            {
                st.Steles.Remove(this);
            }
        }

        /// <summary>离玩家多近开始给提示</summary>
        private void BuildOrderGlyph()
        {
            // 场景里三座剑碑的 orderGlyph 全是空的 —— 而提示说"按碑上的数字顺序"，
            // 玩家看不到任何数字，这个谜题等于无解（实测反馈"根本不知道怎么操作"）。
            // 所以这里在碑顶补一个运行时画的数字。
            if (orderGlyph != null && orderGlyph.sprite != null)
            {
                return;
            }

            var go = new GameObject("OrderGlyph");
            go.transform.SetParent(transform, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BuildDigitSprite(order);
            sr.sortingOrder = SortingOrders.Props + 5;
            sr.color = new Color32(255, 236, 190, 255);
            go.transform.localScale = new Vector3(28f, 28f, 1f);
            go.transform.localPosition = new Vector3(0f, 150f, 0f);
        }

        /// <summary>3×5 点阵数字，画成 32×32 的贴图（运行时生成，不依赖美术资源）</summary>
        private static Sprite BuildDigitSprite(int digit)
        {
            string[] glyph = digit switch
            {
                1 => new[] { "010", "110", "010", "010", "111" },
                2 => new[] { "111", "001", "111", "100", "111" },
                _ => new[] { "111", "001", "111", "001", "111" },
            };

            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];

            for (int i = 0; i < px.Length; i++)
            {
                px[i] = new Color32(255, 255, 255, 0);
            }

            const int cell = 5;
            int ox = (size - 3 * cell) / 2;
            int oy = (size - 5 * cell) / 2;

            for (int row = 0; row < 5; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    if (glyph[4 - row][col] != '1')
                    {
                        continue;
                    }

                    for (int y = 0; y < cell; y++)
                    {
                        for (int x = 0; x < cell; x++)
                        {
                            px[(oy + row * cell + y) * size + ox + col * cell + x] =
                                new Color32(255, 255, 255, 255);
                        }
                    }
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 1f);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (IsLit)
            {
                return;
            }

            var player = FindAnyObjectByType<QingheController>();

            if (player == null)
            {
                return;
            }

            if (requireMergeForm && player.CurrentForm != CompanionForm.Merge)
            {
                return;
            }

            if (Vector2.Distance(player.transform.position, transform.position) > resonateRadius)
            {
                return;
            }

            Resonate();
        }

        private void Resonate()
        {
            GroupState st = Groups[groupId];

            // 必须是序列里的**下一座**。点错 → 整组熄灭重来
            if (order != st.Progress + 1)
            {
                ResetGroup(st);
                EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = "顺序错了，剑碑重归黯淡" });
                return;
            }

            IsLit = true;
            st.Progress = order;
            Refresh();

            GameAudio.PlaySfx(SfxCue.Spirit);

            // 水境的水钟也是剑碑组件（复用共鸣判定）。场景里有水位系统就顺手抬水 ——
            // 没有就不做事，所以金境那三座剑碑不受影响。
            if (WaterLevel.Instance != null)
            {
                WaterLevel.Instance.Raise();
            }

            GameLog.I($"[SwordStele] 第 {order} 座共鸣（组 {groupId}，进度 {st.Progress}）");

            if (GroupComplete(groupId))
            {
                EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = "剑气门已解" });
            }
        }

        private static void ResetGroup(GroupState st)
        {
            st.Progress = 0;

            for (int i = 0; i < st.Steles.Count; i++)
            {
                SwordStele s = st.Steles[i];

                if (s != null)
                {
                    s.IsLit = false;
                    s.Refresh();
                }
            }
        }

        private void Refresh()
        {
            if (visualOff != null) visualOff.SetActive(!IsLit);
            if (visualOn != null) visualOn.SetActive(IsLit);
        }

        protected override void OnActivate(GameObject who)
        {
            // 剑碑靠靠近共鸣，不靠碰撞触发
        }
    }
}
