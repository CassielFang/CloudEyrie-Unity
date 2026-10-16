using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 灵界屏障 —— 本作的核心教学机关：**实体态被挡，灵雾态穿行。**
    /// 五境各有一个皮肤（木境是「枯藤灵障」、土境是「岩封灵障」…），
    /// 逻辑完全一样，只有贴图与调色不同 —— 所以做成一个组件 + 换皮肤。
    ///
    /// <b>怎么判断玩家是不是灵雾态</b>
    ///
    /// 用 <c>Rigidbody2D.gravityScale</c> 而不是去问形态机：
    /// 实体态重力系数是 1，灵雾 / 灵合是 0。
    /// 这个判据是调弹跳花时找到的（见 <c>BounceFlower</c> 里那段注释），
    /// 好处是**不必给形态机加接口**，也不必让机关认识形态枚举。
    ///
    /// <b>为什么碰撞体是一个独立子节点</b>
    ///
    /// 屏障是「门」：左右两半分处玩家前后，穿过时才像走进门里。
    /// 而阻挡用的碰撞体必须**居中**（挂在半边上会让玩家从另一侧穿进门框才被挡）。
    /// 所以阻挡体单独一个节点，只有它会被开关。
    /// </summary>
    public class ElementBarrier : LevelMechanism
    {
        [Header("阻挡")]
        [Tooltip("真正挡路的碰撞体节点。会按玩家形态开关它")]
        [SerializeField] private Collider2D blocker;

        [Tooltip("感应范围（比阻挡体宽一些）。玩家进这个范围才判断形态")]
        [SerializeField] private Collider2D sense;

        [Header("解锁条件")]
        [Tooltip("勾上则场上还有敌人时谁都拦 —— 设计稿「先战再穿」的教学节奏")]
        [SerializeField] private bool requireEnemiesCleared;

        [Tooltip("未清场时给的提示。留空则不提示")]
        [SerializeField] private string lockedHint = "先清掉这里的敌人";

        [Header("表现")]
        [Tooltip("放行时是否把屏障立绘压暗（暗示此刻可以穿过去）")]
        [SerializeField] private bool dimWhenOpen = true;

        [SerializeField] private Transform art;

        [SerializeField] private float dimAlpha = 0.35f;

        private bool _playerInside;
        private bool _hinted;
        private SpriteRenderer[] _artRenderers;
        private Color[] _artBaseColors;

        private void Reset()
        {
            if (sense != null)
            {
                sense.isTrigger = true;
            }

            gameObject.layer = PhysicsGroups.World;
        }

        private void Awake()
        {
            if (sense != null)
            {
                sense.isTrigger = true;
            }

            if (art != null)
            {
                _artRenderers = art.GetComponentsInChildren<SpriteRenderer>(true);
                _artBaseColors = new Color[_artRenderers.Length];

                for (int i = 0; i < _artRenderers.Length; i++)
                {
                    _artBaseColors[i] = _artRenderers[i].color;
                }
            }

            Apply(open: false, playerBody: null);
        }

        private void OnTriggerEnter2D(Collider2D other) { Handle(other, true); }
        private void OnTriggerStay2D(Collider2D other) { Handle(other, true); }
        private void OnTriggerExit2D(Collider2D other) { Handle(other, false); }

        private void Handle(Collider2D other, bool inside)
        {
            if (PlayerFrom(other) == null)
            {
                return;
            }

            _playerInside = inside;

            if (!inside)
            {
                // 离开后恢复阻挡 —— 否则穿过去之后屏障永久失效，回头路就没了
                Apply(open: false, playerBody: null);
                _hinted = false;
                return;
            }

            Rigidbody2D body = other.attachedRigidbody;

            if (requireEnemiesCleared && HasLivingEnemy())
            {
                if (!_hinted && !string.IsNullOrEmpty(lockedHint))
                {
                    _hinted = true;
                    EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = lockedHint });
                }

                Apply(open: false, playerBody: null);
                return;
            }

            // 无重力形态 = 灵雾 / 灵合 → 放行
            bool mist = body != null && body.gravityScale <= 0f;
            Apply(open: mist, playerBody: body);
        }

        private void Apply(bool open, Rigidbody2D playerBody)
        {
            if (blocker != null && blocker.enabled == open)
            {
                // 只有在需要改变时才写 enabled —— 反复写会白白刷新物理形状
                blocker.enabled = !open;
            }

            if (!dimWhenOpen)
            {
                return;
            }

            float k = open ? dimAlpha : 1f;

            if (_artRenderers == null)
            {
                return;
            }

            for (int i = 0; i < _artRenderers.Length; i++)
            {
                Color c = _artBaseColors[i];
                c.a *= k;
                _artRenderers[i].color = c;
            }
        }

        private static bool HasLivingEnemy()
        {
            return FindObjectsByType<EnemyAI>(FindObjectsInactive.Exclude).Length > 0;
        }

        protected override void OnActivate(GameObject who)
        {
            // 屏障是被动生效的（按形态开关），不需要外部触发
        }
    }
}
