using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 命中火花：短暂放大 + 淡出后自毁。
    /// 对应 Cocos 侧 <c>combat/HitSpark.ts</c>。
    /// 纯表现层（灰盒视觉），**不参与任何伤害结算**，正式特效接入后删掉。
    /// </summary>
    public class HitSpark : MonoBehaviour
    {
        private const float Duration = 0.18f;

        private float _elapsed;
        private float _baseScale = 1f;
        private float _scaleMult = 1f;
        private SpriteRenderer _sprite;

        private void Awake()
        {
            _baseScale = transform.localScale.x;
            _sprite = GetComponent<SpriteRenderer>();
        }

        /// <summary>体型倍数（重击的火花更大）</summary>
        public void SetScaleMult(float mult)
        {
            _scaleMult = mult;
            float s = _baseScale * mult;
            transform.localScale = new Vector3(s, s, 1f);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = Mathf.Min(1f, _elapsed / Duration);

            // 先撑开再收，淡出
            float grow = 0.7f + 0.8f * t;
            float s = _baseScale * _scaleMult * grow;
            transform.localScale = new Vector3(s, s, 1f);

            // 淡出：Cocos 用的是 UIOpacity（UI 体系），Unity 这边灰盒火花挂在普通节点上，
            // 走 SpriteRenderer 的 alpha。没有 SpriteRenderer 就只做缩放。
            if (_sprite != null)
            {
                Color c = _sprite.color;
                c.a = 1f - t;
                _sprite.color = c;
            }

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
