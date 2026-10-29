using UnityEngine;

namespace CloudEyrie.Elements
{
    /// <summary>
    /// 把实体接进材质格子：每 0.1 秒看一次脚下那一格是什么，转成状态。
    ///
    /// 状态效果刻意做成"读格子"而不是"读碰撞体"：火是格子里蔓延的，
    /// 玩家站在火苗里就该烧起来，跟火的碰撞体在哪无关。
    /// </summary>
    public class StatusHost : MonoBehaviour
    {
        [Tooltip("燃烧时每秒扣多少灵炁")]
        [SerializeField] private float burnDps = 6f;

        [Tooltip("离开火源后还烧多久（秒）")]
        [SerializeField] private float burnLinger = 1.2f;

        [Tooltip("潮湿持续时间（秒）")]
        [SerializeField] private float wetDuration = 3f;

        [Tooltip("潮湿时燃烧会不会被立刻扑灭")]
        [SerializeField] private bool wetBlocksBurn = true;

        public ElementStatus Status { get; private set; }

        private MaterialGrid _grid;
        private float _burnRemaining;
        private float _wetRemaining;
        private float _probeTimer;
        private Collider2D _collider;

        private void Awake()
        {
            _collider = GetComponent<Collider2D>();
        }

        private void Update()
        {
            if (_grid == null)
            {
                _grid = FindAnyObjectByType<MaterialGrid>();

                if (_grid == null)
                {
                    return;
                }
            }

            float dt = Time.deltaTime;

            _probeTimer -= dt;

            if (_probeTimer <= 0f)
            {
                _probeTimer = 0.1f;
                Probe();
            }

            TickTimers(dt);
            ApplyStatus();
        }

        private void Probe()
        {
            Vector2 p = _collider != null
                ? new Vector2(_collider.bounds.center.x, _collider.bounds.min.y)
                : (Vector2)transform.position;

            // 采样三个高度：脚边、小腿、身体中心。
            // 只采脚边会漏 —— 玩家站在地上时脚底刚好比那一格火低一格（实测 status 一直是 None）。
            MaterialType feet = _grid.GetAt(p + Vector2.up * 8f);
            MaterialType legs = _grid.GetAt(p + Vector2.up * 24f);
            MaterialType body = _grid.GetAt(p + Vector2.up * 56f);

            bool inWater = feet == MaterialType.Water || legs == MaterialType.Water || body == MaterialType.Water;
            bool inFire = feet == MaterialType.Fire || legs == MaterialType.Fire || body == MaterialType.Fire;
            bool inLava = feet == MaterialType.Lava || legs == MaterialType.Lava || body == MaterialType.Lava;

            if (inWater)
            {
                _wetRemaining = wetDuration;
            }

            if (inFire && !(wetBlocksBurn && _wetRemaining > 0f))
            {
                _burnRemaining = burnLinger;
            }

            if (inLava)
            {
                // 岩浆按"重度燃烧"处理
                _burnRemaining = Mathf.Max(_burnRemaining, burnLinger * 2f);
            }
        }

        private void TickTimers(float dt)
        {
            if (_wetRemaining > 0f)
            {
                _wetRemaining -= dt;
            }

            if (_burnRemaining > 0f)
            {
                _burnRemaining -= dt;

                float dmg = burnDps * dt;

                // 直接扣灵炁，不走 CombatController.ReceivePlayerDamage。
                //
                // 那条路是**格挡判定入口**：它只返回一个 SpiritCost，真正的扣除由调用方做。
                // 之前这里调了它却没用返回值 —— 结果是"身上着火、状态显示 Burning、
                // 灵炁一点不掉"（实测 80.0 烧了几秒还是 80.0）。
                // 而且灼烧本来就不该被格挡掉。
                SpiritEnergySystem.Damage(dmg);
            }
        }

        private void ApplyStatus()
        {
            ElementStatus s = ElementStatus.None;

            if (_wetRemaining > 0f) s |= ElementStatus.Wet;
            if (_burnRemaining > 0f) s |= ElementStatus.Burning;

            Status = s;
        }
    }
}
