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

        [Tooltip("Boss 战场等固定场地用；锁住后相机不再跟随")]
        public bool locked;

        private Transform _target;
        private float _viewW = 1280f;

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

            Sync();
        }

        private void Sync()
        {
            if (cameraTransform != null)
            {
                Vector3 p = cameraTransform.position;
                cameraTransform.position = new Vector3(cameraX, p.y, p.z);
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
    }
}
