using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 可推玄石。设计稿土境段3「推石机关」：
    /// **实体态**贴近按住方向键推着走（1.5 m/s），推进凹槽触发机关（石柱升起/开门）。
    /// 灵雾态推不动 —— 这是「形态分工」的教学点。
    ///
    /// <b>为什么不用纯物理推</b>
    ///
    /// 让 Dynamic 刚体互相顶是最"物理"的做法，但**结果不可控**：
    /// 玩家速度一变、地面摩擦一变，石头滑多远就变；而这一段的玩法是
    /// 「推到凹槽里」，位置必须恰到好处，滑过头就卡住过不了。
    ///
    /// 所以改成：玩家贴住石头并按下方向键时，**由脚本以恒定速度平移石头** ——
    /// 推多远完全由按键时长决定，可预测。
    ///
    /// <b>为什么限定实体态</b>
    ///
    /// 与 <see cref="BounceFlower"/> 用同一套判据（<c>gravityScale</c>）：
    /// 灵雾/灵合是无重力形态，一股雾推不动石头。这既是设定，也是玩法分工。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PushableRock : LevelMechanism
    {
        [Tooltip("推动速度（世界单位/秒）。设计稿 1.5 m/s = 48")]
        [SerializeField] private float pushSpeed = 48f;

        [Tooltip("玩家离多近才算贴着石头推")]
        [SerializeField] private float contactDistance = 90f;

        [Tooltip("推到位（凹槽）后要通知的那组机关 id")]
        [SerializeField] private string groupId = "tu_03";

        [Tooltip("推到位时的提示")]
        [SerializeField] private string hint = "玄石归位";

        /// <summary>靠近提示只发一次</summary>
        private bool _approachHinted;

        [Header("凹槽")]
        [Tooltip("凹槽中心。石头进入这个半径就算到位")]
        [SerializeField] private Transform slot;

        [SerializeField] private float slotRadius = 50f;

        /// <summary>已归位的玄石组。</summary>
        private static readonly System.Collections.Generic.HashSet<string> Seated =
            new System.Collections.Generic.HashSet<string>();

        public static bool GroupAllSeated(string id)
        {
            return !string.IsNullOrEmpty(id) && Seated.Contains(id);
        }

        public bool IsSeated { get; private set; }

        private Rigidbody2D _body;
        private QingheController _player;

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();

            // Kinematic：位置完全由脚本决定，不受重力和碰撞推挤影响
            _body.bodyType = RigidbodyType2D.Kinematic;

            gameObject.layer = PhysicsGroups.World;
        }

        private void Start()
        {
            _player = FindAnyObjectByType<QingheController>();
        }

        private void FixedUpdate()
        {
            if (IsSeated || _player == null)
            {
                return;
            }

            // 第一次靠近就说明"这东西能推"。
            // 原来的 hint 是「玄石归位」——那是**推到位之后**的结果，玩家在推之前什么都不知道。
            if (!_approachHinted &&
                Mathf.Abs(_player.transform.position.x - transform.position.x) <= 220f)
            {
                _approachHinted = true;
                EventBus.Emit(GameEvents.HintRequested,
                              new HintEvent { Text = "靠近这块玄石，按方向键可以推动它", Seconds = 4f });
            }

            Vector2 dir = PlayerPushDirection();

            if (dir == Vector2.zero)
            {
                return;
            }

            Vector2 next = _body.position + dir * (pushSpeed * Time.fixedDeltaTime);
            _body.MovePosition(next);

            CheckSlot(next);
        }

        /// <summary>
        /// 玩家是不是在推、往哪推。
        ///
        /// 三个条件：贴着石头、在实体态、按了方向键。
        /// 方向取角色与石头的水平差 —— 玩家必须在石头的**那一侧**按方向键，
        /// 否则会出现"站在石头右边按左键也能把石头往右推"这种违和感。
        /// </summary>
        private Vector2 PlayerPushDirection()
        {
            Rigidbody2D pb = _player.GetComponent<Rigidbody2D>();

            // 无重力形态推不动（灵雾/灵合）
            if (pb == null || pb.gravityScale <= 0f)
            {
                return Vector2.zero;
            }

            Vector2 rockPos = _body.position;
            Vector2 playerPos = _player.transform.position;

            if (Vector2.Distance(playerPos, rockPos) > contactDistance)
            {
                return Vector2.zero;
            }

            // 玩家在石头的左边 → 只有按右键才是"往前推"
            bool playerIsLeft = playerPos.x < rockPos.x;
            float vx = pb.linearVelocity.x;

            const float PressThreshold = 8f;

            if (playerIsLeft && vx > PressThreshold)
            {
                return Vector2.right;
            }

            if (!playerIsLeft && vx < -PressThreshold)
            {
                return Vector2.left;
            }

            return Vector2.zero;
        }

        private void CheckSlot(Vector2 pos)
        {
            if (slot == null)
            {
                return;
            }

            if (Vector2.Distance(pos, slot.position) > slotRadius)
            {
                return;
            }

            IsSeated = true;
            Seated.Add(groupId);

            // 吸附到凹槽中心，免得停在半径边缘上看着"没进去"
            _body.position = slot.position;

            GameAudio.PlaySfx(SfxCue.Spirit);

            if (!string.IsNullOrEmpty(hint))
            {
                EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = hint });
            }

            GameLog.I($"[PushableRock] 玄石归位（组 {groupId}）");
        }

        protected override void OnActivate(GameObject who)
        {
            // 推石不需要"触发"，靠玩家推
        }
    }
}
