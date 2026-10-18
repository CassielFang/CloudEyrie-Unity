using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>平台的运动方式。一个枚举覆盖五行里所有会动的台子。</summary>
    public enum MotionKind
    {
        /// <summary>横向正弦摆动 —— 木境「摆荡藤台」。</summary>
        SwingHorizontal,

        /// <summary>纵向正弦升降 —— 火境「沉浮浮石」、土境流沙里的浮台。</summary>
        BobVertical,

        /// <summary>土境「升降石柱」：升上去停一会儿、再落下来停一会儿（不是正弦，是方波）。</summary>
        StepVertical,

        /// <summary>金境「磁力平台」：站上去才开始沿轨迹走，走到底悬停一会儿再复位。</summary>
        MagneticRide,
    }

    /// <summary>
    /// 会动的平台。五行里所有"站在上面被带着走"的机关都用它。
    ///
    /// <b>为什么带人不走摩擦力</b>
    ///
    /// 最直觉的做法是把平台做成 Kinematic 刚体、靠摩擦把玩家拖走 —— **本作行不通**：
    /// 玩家的物理材质 friction 是 0，而 Box2D 的摩擦是两边**相乘**的，
    /// 结果永远是 0（这条坑 `docs/dev/避坑清单.md` 里记着，灵泉石台踩上去滑就是它）。
    ///
    /// 所以改成**显式搬运**：平台每帧算出自己移动了多少，把这同一个位移**加给站在它上面的物体**。
    /// 好处是与摩擦、材质、质量全都无关，且玩家在平台上走动、跳跃都不受影响
    /// （我们只补平台的位移，不碰玩家自己的速度）。
    ///
    /// <b>谁算"站在上面"</b>：接触法线接近竖直、且对方在平台中心之上。
    /// 用 `OnCollisionStay2D` 每物理帧刷新一个集合，离开时 `OnCollisionExit2D` 移除。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public class MovingPlatform : LevelMechanism
    {
        [Header("运动")]
        public MotionKind kind = MotionKind.SwingHorizontal;

        [Tooltip("振幅（世界单位）。横向 = 左右各摆多远；纵向 = 上下各升降多少")]
        [SerializeField] private float amplitude = 128f;

        [Tooltip("一个完整来回的周期 /秒")]
        [SerializeField] private float period = 3f;

        [Tooltip("相位偏移（0~1）。多台同型平台靠它错开，形成波浪状的接力")]
        [Range(0f, 1f)]
        [SerializeField] private float phase;

        [Tooltip("StepVertical 专用：升/降完之后各停多久 /秒")]
        [SerializeField] private float stepHold = 1.5f;

        [Tooltip("MagneticRide 专用：到终点后悬停多久再复位 /秒")]
        [SerializeField] private float magneticHold = 3f;

        [Tooltip("MagneticRide 专用：单程移动速度（世界单位/秒）")]
        [SerializeField] private float magneticSpeed = 96f;

        [Tooltip("MagneticRide 专用：移动方向")]
        [SerializeField] private Vector2 magneticDirection = new Vector2(0f, 1f);

        [Header("表现")]
        [Tooltip("平台的可视节点。留空则用自己。会被摆到运动轨迹上")]
        [SerializeField] private Transform visual;

        private Vector2 _origin;
        private Vector2 _lastPosition;
        private float _clock;

        /// <summary>MagneticRide 的状态：0=待机 1=去程 2=悬停 3=回程。</summary>
        private int _rideState;
        private float _rideTimer;
        private float _rideProgress;

        /// <summary>当前站在平台上的物体（每物理帧由碰撞回调刷新）。</summary>
        private readonly HashSet<Rigidbody2D> _riders = new HashSet<Rigidbody2D>();

        private void Awake()
        {
            // ⚠️ 平台位置完全由脚本在 FixedUpdate 里写 —— 刚体必须是 Kinematic，
            // 而且**冻住旋转**。火境的浮石在场景里存成了 Dynamic，玩家一站上去
            // 接触冲量就把它顶得飞速自转（视觉上平台整个翻起来）。
            Rigidbody2D body = GetComponent<Rigidbody2D>();

            if (body != null)
            {
                body.bodyType = RigidbodyType2D.Kinematic;
                body.freezeRotation = true;
                body.angularVelocity = 0f;
                body.gravityScale = 0f;
            }

            _origin = transform.position;
            _lastPosition = _origin;
            _clock = phase * period;

            if (visual == null)
            {
                visual = transform;
            }
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector2 next;

            switch (kind)
            {
                case MotionKind.SwingHorizontal:
                    _clock += dt;
                    next = _origin + new Vector2(Wave(_clock, period) * amplitude, 0f);
                    break;

                case MotionKind.BobVertical:
                    _clock += dt;
                    next = _origin + new Vector2(0f, Wave(_clock, period) * amplitude);
                    break;

                case MotionKind.StepVertical:
                    next = StepPosition(dt);
                    break;

                case MotionKind.MagneticRide:
                    next = MagneticPosition(dt);
                    break;

                default:
                    next = _origin;
                    break;
            }

            // 显式搬运：平台走多少，站在它上面的人就走多少。
            // 放在 MovePosition **之前**算位移，之后统一施加 —— 顺序反了会慢一帧，
            // 表现是高台上的人轻微打滑。
            Vector2 delta = next - _lastPosition;

            if (delta != Vector2.zero && _riders.Count > 0)
            {
                CarryRiders(delta);
            }

            transform.position = next;
            _lastPosition = next;
        }

        private void CarryRiders(Vector2 delta)
        {
            // 集合可能在本帧的物理回调里被改过，先拷一份再遍历
            _riders.RemoveWhere(r => r == null);

            foreach (Rigidbody2D rider in _riders)
            {
                // 直接搬位置而不是加力/设速度：我们只想「跟着平台走」，
                // 不想干预玩家自己的移动与跳跃（设速度会把跳跃吃掉）。
                rider.position += delta;
            }
        }

        /// <summary>正弦波，返回值域 [-1, 1]，起点在 0。</summary>
        private static float Wave(float clock, float period)
        {
            if (period <= 0.0001f)
            {
                return 0f;
            }

            return Mathf.Sin(clock / period * Mathf.PI * 2f);
        }

        /// <summary>升降石柱：升 → 停 → 降 → 停 的方波。</summary>
        private Vector2 StepPosition(float dt)
        {
            float rise = Mathf.Max(0.01f, period * 0.5f);
            float cycle = rise * 2f + stepHold * 2f;

            _clock = Mathf.Repeat(_clock + dt, cycle);

            float y;

            if (_clock < rise)
            {
                y = _clock / rise;
            }
            else if (_clock < rise + stepHold)
            {
                y = 1f;
            }
            else if (_clock < rise * 2f + stepHold)
            {
                y = 1f - (_clock - rise - stepHold) / rise;
            }
            else
            {
                y = 0f;
            }

            // 缓入缓出，免得柱子像电梯一样生硬
            float eased = y * y * (3f - 2f * y);

            return _origin + new Vector2(0f, eased * amplitude);
        }

        /// <summary>磁力平台：没人站时不走；有人站上才启动，走完悬停再复位。</summary>
        private Vector2 MagneticPosition(float dt)
        {
            Vector2 dir = magneticDirection.sqrMagnitude > 0.0001f
                ? magneticDirection.normalized
                : Vector2.up;

            float travel = Mathf.Max(0.01f, amplitude);

            switch (_rideState)
            {
                case 0:     // 待机：有人站上来才启动
                    if (_riders.Count > 0)
                    {
                        _rideState = 1;
                    }
                    break;

                case 1:     // 去程
                    _rideProgress = Mathf.Min(1f, _rideProgress + magneticSpeed * dt / travel);

                    if (_rideProgress >= 1f)
                    {
                        _rideState = 2;
                        _rideTimer = 0f;
                    }
                    break;

                case 2:     // 悬停
                    _rideTimer += dt;

                    if (_rideTimer >= magneticHold)
                    {
                        _rideState = 3;
                    }
                    break;

                case 3:     // 回程
                    _rideProgress = Mathf.Max(0f, _rideProgress - magneticSpeed * dt / travel);

                    if (_rideProgress <= 0f)
                    {
                        _rideState = 0;
                    }
                    break;
            }

            return _origin + dir * (_rideProgress * travel);
        }

        // ============================================================
        // 乘客判定
        // ============================================================

        private void OnCollisionEnter2D(Collision2D collision) { Track(collision, true); }
        private void OnCollisionStay2D(Collision2D collision) { Track(collision, true); }
        private void OnCollisionExit2D(Collision2D collision) { Track(collision, false); }

        private void Track(Collision2D collision, bool on)
        {
            Rigidbody2D body = collision.rigidbody;

            if (body == null || body.bodyType != RigidbodyType2D.Dynamic)
            {
                return;
            }

            if (!on)
            {
                _riders.Remove(body);
                return;
            }

            if (IsStandingOnTop(collision))
            {
                _riders.Add(body);
            }
        }

        /// <summary>
        /// 判断这次接触算不算「站在平台上面」。
        ///
        /// 两个条件缺一不可：<b>接触法线接近竖直</b>（否则贴着侧面蹭过去也被当成乘客，
        /// 表现是人被平台横着拖走），且<b>对方在平台中心之上</b>
        /// （否则从下面顶到平台底也算，表现是人被平台按下去）。
        /// </summary>
        private bool IsStandingOnTop(Collision2D collision)
        {
            for (int i = 0; i < collision.contactCount; i++)
            {
                Vector2 n = collision.GetContact(i).normal;

                if (Mathf.Abs(n.y) > 0.5f)
                {
                    return collision.transform.position.y > transform.position.y;
                }
            }

            return false;
        }

        protected override void OnActivate(GameObject who)
        {
            // 移动平台是被动生效的（站上去就在动），不需要外部触发
        }
    }
}
