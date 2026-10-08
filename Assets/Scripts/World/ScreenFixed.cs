using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 把节点钉在屏幕上，不随相机滚动。
    /// 对应 Cocos 侧 <c>world/ScreenFixed.ts</c>。
    ///
    /// 相机是**直接移动自身 Transform** 实现的（见 <see cref="CameraFollow"/>），
    /// 所以跟着它跑的一切都会被推走 —— HUD、全屏转场叠层这些必须反向补偿。
    /// 补偿量就是相机的位移，所以每帧把自己的 x 设成 <c>cameraX + 基准x</c>。
    ///
    /// 正式做法是给 UI 单开一层 + 第二台相机；这里是灰盒期的省事做法。
    /// </summary>
    public class ScreenFixed : MonoBehaviour
    {
        [Tooltip("场景里挂着 CameraFollow 的组件。留空则运行时自动查找")]
        public CameraFollow follow;

        /// <summary>不含相机位移时的本来位置</summary>
        private float _baseX;

        private void Awake()
        {
            if (follow == null)
            {
                follow = FindAnyObjectByType<CameraFollow>();
            }

            _baseX = transform.localPosition.x;
        }

        private void Update()
        {
            if (follow == null)
            {
                return;
            }

            Vector3 p = transform.localPosition;
            transform.localPosition = new Vector3(follow.cameraX + _baseX, p.y, p.z);
        }
    }
}
