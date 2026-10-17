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

        [Tooltip("跟随速度。0 = 硬跟（瞬时对齐）")]
        public float smooth = 6f;

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
        private float _viewW = 1280f;
        private float _viewH = 720f;
        private float _cameraY;

        /// <summary>相机当前的 x。视差层与 <see cref="ScreenFixed"/> 都读它</summary>
        [System.NonSerialized] public float cameraX;

        private void Awake()
        {
            QingheController qinghe = FindAnyObjectByType<QingheController>();

            if (qinghe != null)
            {
                _target = qinghe.transform;
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

            cameraX = ClampX(cameraX);
            Sync();
        }

        private void Update()
        {
            if (locked || _target == null)
            {
                return;
            }

            float want = ClampX(_target.position.x);

            if (smooth <= 0f)
            {
                cameraX = want;
            }
            else
            {
                // 指数趋近，帧率无关
                float k = 1f - Mathf.Exp(-smooth * Time.deltaTime);
                cameraX += (want - cameraX) * k;
            }

            // 纵向跟随：只有关卡给了上下边界才启用。
            // 纵向用**更小**的平滑系数（比横向慢），否则上下抖动会比横向更晃眼。
            if (TracksVertical)
            {
                float wantY = ClampY(_target.position.y);

                if (smooth <= 0f)
                {
                    _cameraY = wantY;
                }
                else
                {
                    float k = 1f - Mathf.Exp(-smooth * 0.55f * Time.deltaTime);
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
    }
}
