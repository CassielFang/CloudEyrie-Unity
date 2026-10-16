using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 流沙。设计稿土境段4「流沙试炼」：
    /// 区域内**持续缓慢下陷**（0.4 m/s），移动可抵消；完全沉没扣 1 格并送回起点。
    ///
    /// <b>为什么是"慢慢陷"而不是"碰到就死"</b>
    ///
    /// 这一段要玩家做的是**持续移动**，而不是一次精准的跳跃 ——
    /// 即死会把"持续移动"变成"一次失误"，压力性质完全不同。
    /// 慢慢陷给的是**时间压力**：可以站一会儿想路线，但不能停太久。
    ///
    /// <b>下陷怎么实现</b>
    ///
    /// 直接减玩家的 <c>transform.position.y</c>，而不是设速度 ——
    /// 设速度会被 <c>EntityForm</c> 每帧覆盖（它整体写回速度），
    /// 结果就是"下陷时有时无"。改位置不经过速度，稳定得多，
    /// 而且不影响玩家自己的跳跃（跳一下就能从沙里拔出来）。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Quicksand : LevelMechanism
    {
        [Tooltip("下陷速度（世界单位/秒）。设计稿 0.4 m/s = 12.8")]
        [SerializeField] private float sinkSpeed = 12.8f;

        [Tooltip("玩家水平速度超过它就不再下陷（「移动可抵消」）")]
        [SerializeField] private float moveCancelThreshold = 20f;

        [Tooltip("从进入沙面往下陷多深算沉没")]
        [SerializeField] private float sinkFatalDepth = 150f;

        [Tooltip("沉没时扣的灵炁")]
        [SerializeField] private float spiritDamage = 15f;

        [Tooltip("沉没后送回哪个节点。留空则只扣灵炁")]
        [SerializeField] private Transform respawnPoint;

        [Header("表现")]
        [Tooltip("沙面立绘。下陷时会跟着玩家的深度做点反馈（可选）")]
        [SerializeField] private Transform surface;

        private float _surfaceY;

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
            var col = GetComponent<Collider2D>();

            if (col != null)
            {
                col.isTrigger = true;
            }

            _surfaceY = transform.position.y;
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            QingheController player = PlayerFrom(other);

            if (player == null)
            {
                return;
            }

            Rigidbody2D body = other.attachedRigidbody;

            if (body == null)
            {
                return;
            }

            // 无重力形态（灵雾/灵合）浮在沙面上，不受影响
            if (body.gravityScale <= 0f)
            {
                return;
            }

            // 在移动就不陷 —— 这是设计稿给的"解法"：别停
            if (Mathf.Abs(body.linearVelocity.x) > moveCancelThreshold)
            {
                return;
            }

            Vector3 p = player.transform.position;
            p.y -= sinkSpeed * Time.deltaTime;
            player.transform.position = p;

            if (_surfaceY - p.y >= sinkFatalDepth)
            {
                Sink(player);
            }
        }

        private void Sink(QingheController player)
        {
            SpiritEnergySystem.Damage(spiritDamage);
            GameAudio.PlaySfx(SfxCue.Corruption);

            if (respawnPoint != null)
            {
                player.transform.position = respawnPoint.position;

                Rigidbody2D body = player.GetComponent<Rigidbody2D>();

                if (body != null)
                {
                    body.linearVelocity = Vector2.zero;
                }
            }

            GameLog.I($"[Quicksand] 沉没，送回 {(respawnPoint != null ? respawnPoint.name : "原地")}");
        }

        protected override void OnActivate(GameObject who)
        {
            // 流沙是被动地形
        }
    }
}
