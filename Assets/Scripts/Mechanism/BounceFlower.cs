using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 弹跳花：踩上花瓣被弹起，把角色射到高处。无伤害。
    /// （设计稿「木境特色机关」之二，段2「弹跳花谷」靠三朵递增高度的花连弹上岸。）
    ///
    /// <b>冲量怎么定的</b>
    ///
    /// 设计稿写「弹力 8m 垂直（≈2 倍跳跃）」，但它自己前后不一致：
    /// 8m × 32 = 256 世界单位，而实际单次跳跃高度是
    /// <c>jumpSpeed² / (2·g) = 320² / 640 = 250</c> —— 两者几乎相等，不是 2 倍。
    ///
    /// 所以这里**不照抄那个数**，而是按设计意图（弹得明显比跳跃高）取
    /// <see cref="bounceVelocity"/> = 560，对应高度 <c>560²/640 = 490</c> ≈ 1.96 倍跳跃。
    /// 手感不对就改这一个数。
    ///
    /// <b>为什么直接设速度而不是 AddForce</b>
    ///
    /// 与二段跳同一个理由（见 <c>EntityForm</c> 里那段注释）：设速度的高度是**常数**，
    /// 与踩上去那一帧的下落速度无关；叠加冲量的话，从高处砸下来会被弹得更高，
    /// 落点变得不可预测 —— 而弹跳花谷恰恰要求落点可预测。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class BounceFlower : LevelMechanism
    {
        [Header("弹力")]
        [Tooltip("弹起时的向上速度（世界单位/秒）。560 → 高度约 490，≈ 2 倍跳跃")]
        [SerializeField] private float bounceVelocity = 560f;

        [Tooltip("同一朵花两次弹跳的最小间隔 /秒（设计稿 0.5）")]
        [SerializeField] private float cooldown = 0.5f;

        [Header("表现")]
        [Tooltip("花瓣立绘节点。会在被压下时压扁。留空则跳过压扁动画")]
        [SerializeField] private Transform art;

        [Tooltip("被压下时 y 方向的缩放")]
        [Range(0.3f, 1f)]
        [SerializeField] private float squashY = 0.65f;

        [Tooltip("被压下时 x 方向的缩放（压扁要横向撑开才自然）")]
        [Range(1f, 2f)]
        [SerializeField] private float squashX = 1.2f;

        [Tooltip("压下的时长 /秒")]
        [SerializeField] private float pressDuration = 0.08f;

        [Tooltip("回弹到原样的时长 /秒")]
        [SerializeField] private float recoverDuration = 0.22f;

        private float _readyAt;
        private float _pressedAt = -99f;
        private Vector3 _artBaseScale;

        private void Reset()
        {
            // 新建时自动配好，省得每次手点
            Collider2D col = GetComponent<Collider2D>();

            if (col != null)
            {
                col.isTrigger = true;
            }

            gameObject.layer = PhysicsGroups.World;
        }

        private void Awake()
        {
            _artBaseScale = art != null ? art.localScale : Vector3.one;
        }

        private void Update()
        {
            if (art == null || _pressedAt < 0f)
            {
                return;
            }

            float t = Time.time - _pressedAt;

            if (t <= pressDuration)
            {
                // 压下去
                art.localScale = new Vector3(_artBaseScale.x * squashX,
                                             _artBaseScale.y * squashY,
                                             _artBaseScale.z);
                return;
            }

            float k = Mathf.Clamp01((t - pressDuration) / recoverDuration);

            // 弹回来时略微过冲，看着更有劲
            float ease = 1f - Mathf.Pow(1f - k, 3f);

            art.localScale = new Vector3(
                Mathf.Lerp(_artBaseScale.x * squashX, _artBaseScale.x, ease),
                Mathf.Lerp(_artBaseScale.y * squashY, _artBaseScale.y, ease),
                _artBaseScale.z);

            if (k >= 1f)
            {
                art.localScale = _artBaseScale;
                _pressedAt = -99f;
            }
        }

        /// <summary>
        /// <b>只在「进入」时弹，且必须离开之后再进来才能再弹。</b>
        ///
        /// 这里踩过一个坑，值得记下来：最初写的是
        /// <c>OnTriggerEnter2D</c> + <c>OnTriggerStay2D</c> 双保险（怕高速下落时
        /// 触发器一帧穿过、Enter 会漏），并用「不在上升才弹」做去重。
        /// 结果玩家被**持续**弹射，一路飞到 y=14915。
        ///
        /// 原因是**「不在上升」根本不是一个可靠的去重条件**：
        /// 只要玩家在这一帧里的竖直速度恰好不满足它（静止、被别的系统归零、
        /// 或者处在每帧重设速度的无重力形态），花就会再弹一次 —— 而且是每帧一次。
        /// 实测读数甚至出现过「0.47 秒上升 6300 单位而速度只有 400/秒」这种物理上不可能的组合。
        ///
        /// 现在的做法不依赖任何速度判据：**重叠状态本身**就是去重键。
        /// 触发区里待着不动，最多被弹一次；要再弹必须飞出去再落回来 ——
        /// 这既是设计意图（踩上去弹一次），也让它结构上不可能失控。
        ///
        /// 代价：高速下落一帧穿过触发区时会漏掉（触发器默认没有连续碰撞检测）。
        /// 但本关的弹跳花是**大面积**触发体（110×160），角色一帧的位移远小于它，
        /// 实际不会漏；真漏了也只是少弹一次，不会像原来那样失控。
        /// </summary>
        private bool _playerInside;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (PlayerFrom(other) == null || _playerInside)
            {
                return;
            }

            _playerInside = true;
            TryBounce(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (PlayerFrom(other) != null)
            {
                _playerInside = false;
            }
        }

        private void TryBounce(Collider2D other)
        {
            if (Time.time < _readyAt)
            {
                return;
            }

            QingheController player = PlayerFrom(other);

            if (player == null)
            {
                return;
            }

            Rigidbody2D body = player.GetComponent<Rigidbody2D>();

            if (body == null)
            {
                return;
            }

            // ⚠️ **无重力形态（灵雾 / 灵合）一律不触发。**
            //
            // 这一条是踩出来的：最初只写了下面那句「不在上升才弹」，结果玩家飞到了 y=14915。
            // 原因不是花算错了，是**灵雾形态每帧都把速度按输入重设**
            // （`MistForm.Update`：无输入就是 (0,0)），于是「不在上升」这个条件**永远成立** ——
            // 花每帧弹一次、速度每帧被抹一次，人就一路被推上去了。
            //
            // 重力开关正好区分这两类形态（实体 gravityScale=1，灵雾/灵合=0），
            // 不必去问形态机，也不必加新的接口。语义上也对：
            // 花瓣是被**踩**下去的，雾没有重量。
            if (body.gravityScale <= 0f)
            {
                return;
            }

            // 不在上升时才弹。不筛的话玩家从下方穿上来也会被弹回去，
            // 表现是"想跳上去却被按回来"
            if (body.linearVelocity.y > 30f)
            {
                return;
            }

            _readyAt = Time.time + cooldown;
            _pressedAt = Time.time;

            // 弹跳是稀疏事件，用 I 档常开 —— 一旦再出现「被反复弹」，
            // 日志里能直接数出弹了几次、每次的落点，比读位置去猜快得多
            GameLog.I($"[BounceFlower] 弹起：t={Time.time:F2} 位置={transform.position} " +
                      $"玩家 y={player.transform.position.y:F1} 速度 {body.linearVelocity.y:F0} → {bounceVelocity:F0}");

            body.linearVelocity = new Vector2(body.linearVelocity.x, bounceVelocity);

            GameAudio.PlaySfx(SfxCue.Dash);

            Activate(player.gameObject);
        }

        protected override void OnActivate(GameObject who)
        {
            // 弹跳花每踩一次都要弹，没有额外的一次性逻辑。
            // 真正的去重是上面那个 _readyAt 冷却 —— 它在 TryBounce 里就已经生效了
            // （弹跳本身不能等 Activate 回来才做，否则去重和弹跳分处两地、
            //   将来改一处就会漏另一处）。
        }
    }
}
