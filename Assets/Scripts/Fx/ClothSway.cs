using System.Collections.Generic;
using System.Text.RegularExpressions;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 头发 / 衣摆的**常驻**二次摆动。
    ///
    /// <b>为什么生成动画之外还要它</b>
    ///
    /// `gen_spine_anims.py` 只给 run/jump/fall 写了发丝摆动；而青禾大部分时间是**站着不动**
    /// （idle_breath 是手绘的，不能改）。于是静止时头发还是一根硬棍。
    ///
    /// 这个组件在 <see cref="SkeletonAnimation"/> 每帧把姿态算完之后（LateUpdate）
    /// 给 `t*` / `yb*` 这些发丝/衣摆骨骼**叠加**一个很小的正弦旋转，再重算一次世界变换。
    /// 叠加量每帧重新加在动画给出的角度上，不会累积漂移。
    ///
    /// 越靠末梢摆幅越大（发尾比发根明显），这是头发看起来"飘"的关键。
    /// </summary>
    public class ClothSway : MonoBehaviour
    {
        [Tooltip("摆动幅度（度）。这是叠加在动画之上的额外量，别开太大")]
        [SerializeField] private float amplitude = 1.6f;

        [Tooltip("摆动速度（弧度/秒）。约 2~3 像微风，5 以上像狂风")]
        [SerializeField] private float speed = 2.2f;

        private SkeletonAnimation _spine;
        private Bone[] _bones;
        private readonly List<float> _depthK = new List<float>();
        private float _clock;

        private static readonly Regex ClothBone = new Regex(@"^(yb|t)\d+$");

        private void Start()
        {
            _spine = GetComponent<SkeletonAnimation>();

            if (_spine == null)
            {
                _spine = GetComponentInChildren<SkeletonAnimation>();
            }

            if (_spine == null || _spine.Skeleton == null)
            {
                return;
            }

            Skeleton skeleton = _spine.Skeleton;
            var bones = new List<Bone>();

            foreach (Bone bone in skeleton.Bones)
            {
                if (ClothBone.IsMatch(bone.Data.Name))
                {
                    bones.Add(bone);
                }
            }

            if (bones.Count == 0)
            {
                return;
            }

            int maxDepth = 1;

            foreach (Bone bone in bones)
            {
                maxDepth = Mathf.Max(maxDepth, Depth(bone));
            }

            _bones = bones.ToArray();

            foreach (Bone bone in _bones)
            {
                _depthK.Add(0.45f + 0.85f * Depth(bone) / maxDepth);
            }
        }

        private static int Depth(Bone bone)
        {
            int d = 0;

            while (bone.Parent != null)
            {
                d++;
                bone = bone.Parent;
            }

            return d;
        }

        private void LateUpdate()
        {
            if (_bones == null || _bones.Length == 0)
            {
                return;
            }

            _clock += Time.deltaTime * speed;

            for (int i = 0; i < _bones.Length; i++)
            {
                float phase = _clock + i * 0.37f;
                _bones[i].Rotation += amplitude * _depthK[i] * Mathf.Sin(phase);
            }

            _spine.Skeleton.UpdateWorldTransform();
        }
    }
}
