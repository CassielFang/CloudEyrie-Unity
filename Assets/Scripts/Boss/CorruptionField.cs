using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 浊湮领域 —— Boss P2 期间铺满整个战场的地面浊湮。
    ///
    /// <b>玩法</b>（<c>docs/关于boss战的一些修改.md</c> P2「浊湮领域」）：
    /// 站上去持续扣灵炁；**灵雾 / 灵合形态飞起来就免疫** ——
    /// 这就是 P2 逼玩家在两个形态之间来回切的抓手。
    ///
    /// <b>为什么没有对应美术也能做</b>：它是一层**程序化的暗色叠层**，
    /// 靠 <see cref="UiDraw.White"/> 拉宽 + 染色即可，不需要新素材。
    /// 正式美术（黑墨水洼 + 颗粒流动）到位后，把 <see cref="Build"/> 里的
    /// 纯色块换成贴图节点就行，玩法侧不用动。
    ///
    /// ⚠️ <b>不是碰撞体、也不是触发器</b> —— 判定完全靠每帧比对玩家的形态与位置。
    /// 碰撞矩阵里没有「地面伤害区」这种分组，硬塞进去会连带影响别的接触关系
    /// （本项目在 <see cref="PhysicsGroups"/> 上已经吃过一次这种亏）。
    /// </summary>
    public class CorruptionField : MonoBehaviour
    {
        /// <summary>领域相对战场中心的半宽 / 半高（配置驱动）</summary>
        private static float HalfWidth { get { return GameConfig.Current.Boss.CorruptionHalfWidth; } }
        private static float HalfHeight { get { return GameConfig.Current.Boss.CorruptionHalfHeight; } }

        /// <summary>叠层颜色：黑墨，压在场景之上但低于角色</summary>
        private static readonly Color FieldTint = new Color32(18, 10, 34, 190);

        /// <summary>领域的下边缘（世界 y，取碰撞体顶边再往下一点，保证「贴地」的观感）</summary>
        private const float FieldBottom = -360f;

        private SpriteRenderer _renderer;
        private Transform _player;
        private CompanionStateMachine _playerForms;

        private float _drainAccum;

        /// <summary>已经生效多久（用来做淡入）。</summary>
        private float _elapsed;

        /// <summary>淡入时长 /秒（配置驱动）</summary>
        private static float FadeInDuration { get { return GameConfig.Current.Boss.CorruptionFadeIn; } }

        public static CorruptionField Spawn(Transform parent, float centerX)
        {
            GameObject n = new GameObject("CorruptionField");
            n.transform.SetParent(parent, false);
            n.transform.position = new Vector3(centerX, FieldBottom + HalfHeight, 0f);

            CorruptionField f = n.AddComponent<CorruptionField>();
            f.Build();
            return f;
        }

        private void Build()
        {
            GameObject go = new GameObject("Art");
            go.transform.SetParent(transform, false);

            _renderer = go.AddComponent<SpriteRenderer>();
            _renderer.sprite = UiDraw.White;
            _renderer.color = new Color(FieldTint.r, FieldTint.g, FieldTint.b, 0f);

            // 压低到景物之上、地面美术之下 —— 浊湮是「铺在地上的」，不能盖住角色
            _renderer.sortingOrder = SortingOrders.GroundArt + 1;

            // 1×1 的纯白精灵靠缩放铺满整个战场
            go.transform.localScale = new Vector3(HalfWidth * 2f, HalfHeight * 2f, 1f);

            QingheController qinghe = FindAnyObjectByType<QingheController>();

            if (qinghe != null)
            {
                _player = qinghe.transform;
                _playerForms = qinghe.GetComponent<CompanionStateMachine>();
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _elapsed += dt;

            // 淡入
            if (_renderer != null)
            {
                float a = Mathf.Min(1f, _elapsed / FadeInDuration) * FieldTint.a;
                Color c = _renderer.color;
                c.a = a;
                _renderer.color = c;
            }

            if (_player == null || _playerForms == null)
            {
                return;
            }

            // 灵雾 / 灵合形态**飞起来了** → 免疫。
            // 这正是 P2 的教学点：不是「换形态免伤」，是「浮空免伤」。
            CompanionForm form = _playerForms.GetCurrentForm();

            if (form != CompanionForm.Entity)
            {
                return;
            }

            if (!IsPlayerOverField())
            {
                return;
            }

            float drain = GameConfig.Current.Spirit.CorruptionContactDrain;
            _drainAccum += drain * dt;

            // 攒够 1 点再扣：逐帧调 Damage(0.08) 会把灵炁变化事件刷爆
            if (_drainAccum >= 1f)
            {
                float amount = Mathf.Floor(_drainAccum);
                _drainAccum -= amount;
                SpiritEnergySystem.Damage(amount);
            }
        }

        private bool IsPlayerOverField()
        {
            Vector3 p = _player.position;
            Vector3 c = transform.position;

            return Mathf.Abs(p.x - c.x) <= HalfWidth && p.y <= c.y + HalfHeight;
        }

        /// <summary>P3 开始前把浊湮收掉（转场里那一下「灵光驱散浊湮」）。</summary>
        public void Dismiss()
        {
            Destroy(gameObject);
        }
    }
}
