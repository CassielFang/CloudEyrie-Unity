using UnityEngine;

namespace CloudEyrie.Elements
{
    /// <summary>
    /// 把场景里已有的机关变成"元素源"：火盆点亮 → 点一片火；火柱喷发 → 喷一片火。
    ///
    /// 为什么用独立组件而不是直接改机关：机关的行为不应该依赖元素系统是否存在 ——
    /// 元素层是这一阶段的实验，接不接都能单独跑。这里只做单向的"读状态、投元素"。
    /// </summary>
    public class ElementSource : MonoBehaviour
    {
        public enum SourceKind
        {
            Fire,
            Water,
            Lava,
            Flammable,
            Sand,
        }

        [SerializeField] private SourceKind kind = SourceKind.Fire;

        [Tooltip("每次投放的半径")]
        [SerializeField] private float radius = 90f;

        [Tooltip("每秒投放几次")]
        [SerializeField] private float emissionsPerSecond = 2f;

        [Tooltip("只在开火时投放（火盆/火柱）。留空则常驻")]
        [SerializeField] private Brazier brazier;

        [SerializeField] private Hazard hazard;

        private float _timer;
        private bool _lavaPainted;

        private void Update()
        {
            if (!IsOpen())
            {
                return;
            }

            if (kind == SourceKind.Lava)
            {
                // 岩浆是常驻材质，铺一次就够（后面由模拟自己守恒）
                if (!_lavaPainted)
                {
                    _lavaPainted = true;
                    ElementSystem.Instance?.SetMaterial(transform.position, radius, MaterialType.Lava);
                }

                return;
            }

            if (kind == SourceKind.Sand)
            {
                // 流沙：一次铺**一柱**，不是一盘。
                //
                // 只铺一盘的话，沙落地后被地面摊平，最后只剩薄薄一层（实测画面里
                // 只有二十来个格子、两块小三角），完全看不出是"沙丘"。
                // 往上一连叠几盘，落下来才会堆出形状。
                if (!_lavaPainted)
                {
                    _lavaPainted = true;

                    ElementSystem sys = ElementSystem.Instance;

                    if (sys != null)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            Vector2 p = (Vector2)transform.position + new Vector2(0f, i * radius * 1.1f);
                            sys.SetMaterial(p, radius, MaterialType.Sand);
                        }
                    }
                }

                return;
            }

            _timer -= Time.deltaTime;

            if (_timer > 0f)
            {
                return;
            }

            _timer = 1f / Mathf.Max(0.1f, emissionsPerSecond);
            Emit();
        }

        private void Emit()
        {
            ElementSystem sys = ElementSystem.Instance;

            if (sys == null)
            {
                return;
            }

            Vector2 p = transform.position;

            switch (kind)
            {
                case SourceKind.Fire:
                    // 先在这片地面上铺可燃物，再点火 —— 否则空地上点不着，看着像没生效
                    sys.MarkFlammable(p, radius * 0.6f);
                    sys.Ignite(p, radius * 0.35f);
                    break;

                case SourceKind.Water:
                    sys.Pour(p, radius * 0.5f);
                    break;

                case SourceKind.Flammable:
                    sys.MarkFlammable(p, radius);
                    break;
            }
        }

        private bool IsOpen()
        {
            if (brazier != null)
            {
                return brazier.IsLit;
            }

            if (hazard != null)
            {
                return hazard.IsDangerous;
            }

            return true;
        }

        public void Bind(Brazier b)
        {
            brazier = b;
        }

        public void Bind(Hazard h)
        {
            hazard = h;
        }

        public SourceKind Kind
        {
            get { return kind; }
            set { kind = value; }
        }

        public float Radius
        {
            get { return radius; }
            set { radius = value; }
        }
    }
}
