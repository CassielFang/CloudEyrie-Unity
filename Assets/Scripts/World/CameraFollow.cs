using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 横版相机跟随。对应 Cocos 侧 <c>world/CameraFollow.ts</c>。
    ///
    /// Cocos 那边是**直接移动 Canvas 里的 Camera 节点**：UI 节点都在 UI 坐标系里，
    /// 相机移过去就等于视角平移，物理世界坐标不受影响（这点很关键 ——
    /// 换成「移动世界根节点」的写法会和玩家刚体打架）。
    ///
    /// Unity 里同理：移动**相机自己的 Transform**，不动任何游戏对象。
    ///
    /// 代价：屏幕固定的东西也会被推走，所以要挂 <see cref="ScreenFixed"/> 反向补偿。
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [Tooltip("相机 Transform。留空则用 Camera.main")]
        [SerializeField] private Transform cameraTransform;

        [Tooltip("背景节点：视差层挂在它上面")]
        [SerializeField] private ParallaxBackground background;

        [Tooltip("跟随速度（指数平滑，1/秒）。0 = 硬跟（瞬时对齐）")]
        public float smooth = 6f;

        [Header("横向构图")]
        [Tooltip("横向死区半宽（世界单位）：玩家在这个窗口里走动，相机不动。建议 0.1~0.2 屏宽")]
        public float horizontalDeadZone = 140f;

        [Tooltip("朝移动方向的预看距离（世界单位）：跑起来时多看到前方一点")]
        public float lookAhead = 150f;

        [Tooltip("预看量的缓动速度（1/秒）。太大镜头会抢在角色前面，太小又跟不上")]
        public float lookAheadSmooth = 1.6f;

        [Header("纵向构图")]
        [Tooltip("常规构图：玩家脚底在相机中心**下方**多少（世界单位）。275 = Host 里摆的原始构图")]
        public float restScreenY = 275f;

        [Tooltip("下落时玩家要出现在相机中心**上方**多少。正数 = 中上部，能提前看到落点")]
        public float fallScreenY = 90f;

        [Tooltip("下落速度超过多少（世界单位/秒）才切到「看落点」构图。太小会让普通小跳也翻镜头")]
        public float fallLeadSpeed = 120f;

        [Tooltip("落地后回到常规构图的速度（1/秒）。越小越慢：0.4 秒左右比较自然")]
        public float landingRecoverSmooth = 2.5f;

        [Tooltip("玩家高出相机中心多少才向上跟。要扣掉人物身高（约 230），" +
                 "否则头顶会先出画：本作取 80。")]
        public float verticalDeadZoneAbove = 80f;

        [Tooltip("玩家低于相机中心多少才向下跟。脚底没有高度，可以给大一些（接近半屏）")]
        public float verticalDeadZoneBelow = 300f;

        [Tooltip("纵向平滑速度（1/秒）。坠落时要比横向快，否则角色会掉出画面下沿")]
        public float verticalSmooth = 10f;

        [Tooltip("相机下潜的下限（世界 y）。0 = 不越过 Host 摆的「地面构图」——\n" +
                 "掉坑时视野照到地面就停，不再下潜看地下。")]
        public float verticalFloor;

        [Header("关卡边界")]
        public float levelLeft;
        public float levelRight = 2600f;

        [Tooltip("纵向边界。两者相等（默认）时**纵向不跟随**，相机停在 Host 场景摆的高度 —— " +
                 "这正是之前岩浆/水流摆在 y=-820 却永远看不见的原因。" +
                 "关卡给了上下边界之后，相机才会跟着玩家上下走。")]
        public float levelBottom;
        public float levelTop;

        [Tooltip("Boss 战场等固定场地用；锁住后相机不再跟随")]
        public bool locked;

        private Transform _target;
        private Rigidbody2D _targetBody;
        private QingheController _targetController;
        private float _viewW = 1280f;
        private float _viewH = 720f;
        private float _cameraY;

        /// <summary>预看量的当前值。朝移动方向缓入，停下后缓回 0</summary>
        private float _lookAheadNow;

        /// <summary>相机当前的 x。视差层与 <see cref="ScreenFixed"/> 都读它</summary>
        [System.NonSerialized] public float cameraX;

        /// <summary>视口宽度（世界单位）。给背景反推图层宽度用，别在别处写死 1280</summary>
        public float ViewWidth
        {
            get { return _viewW; }
        }

        private void Awake()
        {
            QingheController qinghe = FindAnyObjectByType<QingheController>();

            if (qinghe != null)
            {
                _target = qinghe.transform;
                _targetController = qinghe;
                _targetBody = qinghe.GetComponent<Rigidbody2D>();
            }

            if (cameraTransform == null)
            {
                Camera cam = Camera.main;

                if (cam != null)
                {
                    cameraTransform = cam.transform;
                }
            }

            if (background == null)
            {
                background = FindAnyObjectByType<ParallaxBackground>();
            }

            _viewW = ResolveViewWidth();
            _viewH = ResolveViewHeight();

            // 先记住 Host 场景里摆的相机高度：关卡的纵向边界没给时，就用它当固定高度。
            if (cameraTransform != null)
            {
                _cameraY = cameraTransform.position.y;
            }

            if (_target == null || cameraTransform == null)
            {
                Debug.Log("[CameraFollow] 找不到目标或相机，跟随不生效");
                return;
            }

            // 背景层的宽度由相机行程反推，必须在建层前给到（SetRange 会触发重建布局）
            if (background != null)
            {
                float half = _viewW / 2f;
                background.SetRange(levelLeft + half, levelRight - half, _viewW);
            }

            cameraX = ClampX(_target.position.x);
            Sync();
        }

        private float ResolveViewWidth()
        {
            Camera cam = cameraTransform != null ? cameraTransform.GetComponent<Camera>() : Camera.main;

            if (cam != null && cam.orthographic)
            {
                return cam.orthographicSize * 2f * cam.aspect;
            }

            return 1280f;
        }

        private float ResolveViewHeight()
        {
            Camera cam = cameraTransform != null ? cameraTransform.GetComponent<Camera>() : Camera.main;

            if (cam != null && cam.orthographic)
            {
                return cam.orthographicSize * 2f;
            }

            return 720f;
        }

        /// <summary>锁定相机到当前位置（进入 Boss 战场时调）</summary>
        public void Lock()
        {
            locked = true;
        }

        public void Unlock()
        {
            locked = false;
        }

        /// <summary>重设关卡两端（关卡长度由 <see cref="LevelDirector"/> 决定）。会连带重算背景层的宽度。</summary>
        public void SetBounds(float left, float right)
        {
            levelLeft = left;
            levelRight = right;

            float half = _viewW / 2f;
            background?.SetRange(left + half, right - half, _viewW);

            // 换关时清掉上一关的预看量，否则新关一落地镜头就偏出去一截
            _lookAheadNow = 0f;

            cameraX = ClampX(cameraX);
            Sync();
        }

        /// <summary>
        /// 平台跳跃相机的通行做法（Cinemachine Position Composer 也是这三个参数）：
        /// <b>死区 → 阻尼 → 预看</b>。
        ///
        /// <list type="bullet">
        ///   <item><b>死区</b>：角色在窗口里走动时相机不动，避免每一步都推着世界走；</item>
        ///   <item><b>阻尼</b>：出窗口后用指数平滑追，帧率无关，避免硬切；</item>
        ///   <item><b>预看</b>：朝移动方向多给一点视野，按速度缓入缓出，停下时收回。</item>
        /// </list>
        ///
        /// 放在 <c>LateUpdate</c>：目标的位置在物理步里更新，跟在 LateUpdate 才不会抖一帧。
        /// </summary>
        private void LateUpdate()
        {
            if (locked || _target == null)
            {
                return;
            }

            // ---- 横向：死区 + 预看 ----
            float vx = _targetBody != null ? _targetBody.linearVelocity.x : 0f;
            float wantAhead = lookAhead * Mathf.Clamp(vx / 160f, -1f, 1f);

            if (lookAheadSmooth > 0f)
            {
                float ka = 1f - Mathf.Exp(-lookAheadSmooth * Time.deltaTime);
                _lookAheadNow += (wantAhead - _lookAheadNow) * ka;
            }
            else
            {
                _lookAheadNow = wantAhead;
            }

            float desiredX = _target.position.x + _lookAheadNow;
            float halfDead = Mathf.Max(0f, horizontalDeadZone);
            float wantX = cameraX;

            if (desiredX > cameraX + halfDead)
            {
                wantX = desiredX - halfDead;
            }
            else if (desiredX < cameraX - halfDead)
            {
                wantX = desiredX + halfDead;
            }

            if (smooth <= 0f)
            {
                cameraX = wantX;
            }
            else
            {
                cameraX += (wantX - cameraX) * (1f - Mathf.Exp(-smooth * Time.deltaTime));
            }

            cameraX = ClampX(cameraX);

            // ---- 纵向：常规构图 / 坠落看落点 / 空中夹紧（只有关卡给了上下边界才启用）----
            if (TracksVertical)
            {
                float vy = _targetBody != null ? _targetBody.linearVelocity.y : 0f;
                bool grounded = _targetController != null && _targetController.Context.Grounded;
                bool falling = !grounded && vy < -Mathf.Abs(fallLeadSpeed);

                float wantY = VerticalTarget(grounded, falling);

                if (smooth <= 0f)
                {
                    _cameraY = wantY;
                }
                else
                {
                    // 落地回正用更慢的系数 —— 坠落时跟得快要保命，落地回正快就会"啪"一下
                    float rate = grounded ? landingRecoverSmooth : verticalSmooth;
                    float k = 1f - Mathf.Exp(-Mathf.Max(0.01f, rate) * Time.deltaTime);
                    _cameraY += (wantY - _cameraY) * k;
                }
            }

            Sync();
        }

        /// <summary>关卡有没有给纵向边界。没给 = 保持 Host 里摆的固定高度（旧行为）。</summary>
        private bool TracksVertical
        {
            get { return levelTop - levelBottom > 0.01f; }
        }

        /// <summary>
        /// 纵向目标。三种情形：
        ///
        /// <list type="number">
        ///   <item><b>着地</b>：回到常规构图（脚底在中心下方 <see cref="restScreenY"/>）。
        ///     落地后的回正走更慢的 <see cref="landingRecoverSmooth"/>，不是"啪"一下弹回去；</item>
        ///   <item><b>快速下落</b>：把角色放到中心**上方** <see cref="fallScreenY"/>，
        ///     提前看到落点 —— 这是玩家明确要的「视野提前量」；</item>
        ///   <item><b>其余空中</b>：保持不动，只有头顶快出画（上 80）或脚底超出下沿（下 300）才夹紧。
        ///     普通跳跃因此不会上下抖。</item>
        /// </list>
        /// </summary>
        private float VerticalTarget(bool grounded, bool falling)
        {
            float playerY = _target.position.y;
            float screenY = playerY - _cameraY;

            if (grounded)
            {
                return Floor(ClampY(playerY + Mathf.Abs(restScreenY)));
            }

            if (falling)
            {
                return Floor(ClampY(playerY - Mathf.Abs(fallScreenY)));
            }

            float above = Mathf.Max(0f, verticalDeadZoneAbove);
            float below = Mathf.Max(0f, verticalDeadZoneBelow);

            if (screenY > above)
            {
                return Floor(ClampY(playerY - above));
            }

            if (screenY < -below)
            {
                return Floor(ClampY(playerY + below));
            }

            return Floor(ClampY(_cameraY));
        }

        /// <summary>相机下潜下限：看到地面就够了，不再往地下钻。</summary>
        private float Floor(float y)
        {
            return Mathf.Max(y, verticalFloor);
        }

        private void Sync()
        {
            if (cameraTransform != null)
            {
                Vector3 p = cameraTransform.position;
                cameraTransform.position = new Vector3(cameraX, _cameraY, p.z);
            }

            if (background != null)
            {
                background.SetCameraX(cameraX);
                background.SetCameraY(_cameraY);
            }
        }

        private float ClampX(float x)
        {
            float half = _viewW / 2f;
            float lo = levelLeft + half;
            float hi = levelRight - half;

            if (hi <= lo)
            {
                return (levelLeft + levelRight) / 2f;
            }

            return Mathf.Max(lo, Mathf.Min(hi, x));
        }

        /// <summary>
        /// 纵向夹紧。关卡比一屏还矮时（hi &lt;= lo）居中，而不是夹成一条边 ——
        /// 否则矮关卡会看到相机贴在地板上。
        /// </summary>
        private float ClampY(float y)
        {
            float half = _viewH / 2f;
            float lo = levelBottom + half;
            float hi = levelTop - half;

            if (hi <= lo)
            {
                return (levelBottom + levelTop) / 2f;
            }

            return Mathf.Max(lo, Mathf.Min(hi, y));
        }

        /// <summary>重设纵向边界。两者相等 = 关掉纵向跟随。</summary>
        public void SetVerticalBounds(float bottom, float top)
        {
            levelBottom = bottom;
            levelTop = top;

            _cameraY = ClampY(_cameraY);
            Sync();
        }

        /// <summary>
        /// 立即把相机 y 对到「常规构图」（玩家脚底在中心下方 restScreenY）。
        ///
        /// 换关 / 坠落重开时必须调：否则相机会保留上一处的 `_cameraY`
        /// （比如从坑底 -540 复活到地面），而开场 cue 又会把相机锁住，
        /// 表现为「复活后角色一直在画面上方，要走一段才回落」。
        /// </summary>
        public void SnapVerticalToRest()
        {
            if (_target == null)
            {
                return;
            }

            _cameraY = ClampY(_target.position.y + Mathf.Abs(restScreenY));
            Sync();
        }
    }
}
