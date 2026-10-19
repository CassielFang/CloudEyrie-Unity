using UnityEngine;

namespace CloudEyrie
{
    /// <summary>收集品的种类。五行印记只发一次，碎片可分散在各境。</summary>
    public enum CollectibleKind
    {
        /// <summary>碎片：散在各境的收集品（设计稿共 10 枚）。</summary>
        Fragment,

        /// <summary>五行印记：每境终点传送门处获得，集齐 5 枚解锁中央祭坛。</summary>
        Seal,
    }

    /// <summary>
    /// 收集品。碰到即收，收过就不再出现（重进关卡也不会刷回来）。
    ///
    /// <b>为什么碰到即收、不做拾取动画的等待</b>
    ///
    /// 本作的收集品是**可选内容**（设计稿写明"无强制收集"），
    /// 所以交互越轻越好 —— 碰到就收、闪一行提示，不打断平台跳跃的节奏。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Collectible : LevelMechanism
    {
        public CollectibleKind kind = CollectibleKind.Fragment;

        [Tooltip("碎片 id，**全局唯一**。重进关卡靠它判断有没有拿过")]
        public string fragmentId;

        [Tooltip("kind = Seal 时用：这是哪一行的印记")]
        public ElementKind element = ElementKind.Mu;

        [Tooltip("收下时的提示文字。留空则不提示")]
        [SerializeField] private string hint = "拾得碎片";

        [Header("表现")]
        [Tooltip("悬浮幅度（世界单位）。收集品静止会很不起眼")]
        [SerializeField] private float floatAmplitude = 10f;

        [Tooltip("悬浮周期 /秒")]
        [SerializeField] private float floatPeriod = 2.4f;

        private Vector3 _origin;
        private float _phase;

        /// <summary>目标显示尺寸（世界单位）。设计稿的碎片大约 40~50。</summary>
        private const float TargetSize = 46f;

        private SpriteRenderer _renderer;
        private Vector3 _baseScale = Vector3.one;

        private void Reset()
        {
            Collider2D col = GetComponent<Collider2D>();

            if (col != null)
            {
                col.isTrigger = true;
            }

            gameObject.layer = PhysicsGroups.World;
        }

        private void Awake()
        {
            _origin = transform.position;
            _phase = Random.value * Mathf.PI * 2f;

            AttachArt();

            // 这一周目已经拿过就直接不出现 —— 避免"看到了却捡不起来"
            if (AlreadyCollected())
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 碎片的世界表现。
        ///
        /// 之前场景里的收集品**没有精灵**（SpriteRenderer 的 sprite 为空、组件还没启用），
        /// 于是玩家完全看不见它们 —— 设计稿要的"可收集碎片"等于不存在。
        /// 这里按需挂上交付层的「光碎片」贴图，并按目标尺寸归一化，
        /// 再叠一个呼吸缩放，让小东西在场景里"亮"起来。
        /// </summary>
        private void AttachArt()
        {
            _renderer = GetComponent<SpriteRenderer>();

            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<SpriteRenderer>();
            }

            if (_renderer.sprite == null && kind == CollectibleKind.Fragment)
            {
                Sprite s = Resources.Load<Sprite>("collectible/fragment");

                if (s != null)
                {
                    _renderer.sprite = s;
                }
            }

            _renderer.enabled = _renderer.sprite != null;

            if (_renderer.sprite != null)
            {
                float h = _renderer.sprite.bounds.size.y;

                if (h > 0.01f)
                {
                    float k = TargetSize / h;
                    _baseScale = new Vector3(k, k, 1f);
                    transform.localScale = _baseScale;
                }
            }
        }

        private void Update()
        {
            // 原地上下浮动 + 呼吸缩放。用 position 而不是 localPosition：收集品都是场景根的散件
            float t = Time.time / Mathf.Max(0.01f, floatPeriod) * Mathf.PI * 2f + _phase;

            transform.position = _origin + new Vector3(0f, Mathf.Sin(t) * floatAmplitude, 0f);

            if (_baseScale != Vector3.one)
            {
                float pulse = 1f + Mathf.Sin(t * 0.8f) * 0.06f;
                transform.localScale = _baseScale * pulse;
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (PlayerFrom(other) == null)
            {
                return;
            }

            Activate(other.gameObject);
        }

        protected override void OnActivate(GameObject who)
        {
            bool fresh;

            if (kind == CollectibleKind.Seal)
            {
                fresh = PlayerProgress.GrantSeal(element);
            }
            else
            {
                fresh = PlayerProgress.CollectFragment(fragmentId);
            }

            if (!fresh)
            {
                // 已经拿过（理论上 Awake 就会把自己关掉，这里是兜底）
                gameObject.SetActive(false);
                return;
            }

            if (!string.IsNullOrEmpty(hint))
            {
                string text = kind == CollectibleKind.Seal
                    ? $"{hint}·{ElementName(element)}"
                    : hint;

                EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = text });
            }

            GameAudio.PlaySfx(SfxCue.Spirit);

            GameLog.I($"[Collectible] 拾取 {(kind == CollectibleKind.Seal ? "印记" : "碎片")} " +
                      $"{fragmentId}（累计 碎片{PlayerProgress.FragmentCount} / 印记{PlayerProgress.SealCount}）");

            gameObject.SetActive(false);
        }

        private bool AlreadyCollected()
        {
            return kind == CollectibleKind.Seal
                ? PlayerProgress.HasSeal(element)
                : PlayerProgress.HasFragment(fragmentId);
        }

        private static string ElementName(ElementKind e)
        {
            switch (e)
            {
                case ElementKind.Mu: return "木";
                case ElementKind.Huo: return "火";
                case ElementKind.Tu: return "土";
                case ElementKind.Jin: return "金";
                case ElementKind.Shui: return "水";
                default: return "祭坛";
            }
        }
    }
}
