using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 元素传送门：靠近即切到目标关卡。
    ///
    /// <b>为什么用距离判定而不是 <c>OnTriggerEnter2D</c></b>
    ///
    /// 切关前后经常处在 <see cref="GameFlow"/>.Push（<c>Time.timeScale = 0</c>）里，
    /// 而此时 Unity **不步进物理、也就不产生新的接触事件**（见 <c>GameFlow</c> 的注释）。
    /// 用触发回调会出现「站在门里不动、门却不响应」。
    /// 距离判定每帧都算，绕开这个问题。
    ///
    /// 表现层亮/暗用子节点换 sprite（美术已给「灵界机关（亮/暗）（传送门）」两张图）。
    /// </summary>
    public class LevelPortal : MonoBehaviour
    {
        [Header("去哪")]
        [Tooltip("目标关卡的 id（LevelDefinition.id）")]
        public string targetLevelId = "huo";

        [Tooltip("落在目标关的哪个入口点（场景里的空节点名）。空 = 用目标关的默认出生点")]
        public string entryMarkerId;

        [Tooltip("是否把当前灵炁带过去（Boss 关 → 祭坛这类连续战斗用）")]
        public bool carryPlayerState;

        [Header("触发")]
        [Tooltip("触发半径（世界单位）")]
        public float radius = 200f;

        [Tooltip("需要本关小怪先清完吗（沿用「屏障解锁」式的条件）")]
        public bool requireCleared;

        [Tooltip("条件不满足时给的提示")]
        public string lockedHint = "先清掉这里的敌人";

        [Tooltip("未激活的门挂的节点（一般是暗色那张图）。激活时换成亮色")]
        public GameObject visualOff;

        [Tooltip("激活时挂的节点（亮色那张图）")]
        public GameObject visualOn;

        private Transform _player;
        private float _nextCheck;
        private bool _used;
        private bool _wasInside;

        private void Start()
        {
            RefreshVisual();
        }

        private void Update()
        {
            // 每 0.1 秒查一次就够 —— 这是个半径两百的圆，玩家不可能一帧穿过去
            if (Time.time < _nextCheck)
            {
                return;
            }

            _nextCheck = Time.time + 0.1f;

            // 条件满足时把门点亮 —— 同一轮里顺带做，不额外开计时器
            RefreshVisual();

            if (_used || LevelHost.Instance == null)
            {
                return;
            }

            QingheController player = LevelHost.Instance.Player;

            if (player == null)
            {
                return;
            }

            float d = Vector2.Distance(player.transform.position, transform.position);
            bool inside = d <= radius;

            if (inside && !_wasInside)
            {
                TryEnter();
            }

            _wasInside = inside;
        }

        /// <summary>
        /// 首次抵达本境终点门时获得对应的**五行印记**。
        ///
        /// 放在传送门上、而不是另摆一个收集品，是因为设计稿的规则就是
        /// 「抵达终点传送门即得」—— 那个印记本来就是"走通这一境"的凭证，
        /// 不该需要玩家额外去捡。
        /// </summary>
        private void GrantSeal()
        {
            if (LevelHost.Instance == null)
            {
                return;
            }

            LevelDefinition def = LevelHost.Instance.Current;

            if (def == null || def.element == ElementKind.Altar)
            {
                return;   // 祭坛不发印记
            }

            if (PlayerProgress.GrantSeal(def.element))
            {
                EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = "获得五行印记" });
            }
        }

        private void TryEnter()
        {
            if (requireCleared && HasLivingEnemy())
            {
                if (!string.IsNullOrEmpty(lockedHint))
                {
                    EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = lockedHint });
                }

                return;
            }

            _used = true;

            GrantSeal();

            GameAudio.PlaySfx(SfxCue.Spirit);
            LevelHost.Instance.EnterLevel(targetLevelId, entryMarkerId, carryPlayerState);
        }

        private static bool HasLivingEnemy()
        {
            return FindObjectsByType<EnemyAI>(FindObjectsInactive.Exclude).Length > 0;
        }

        private void RefreshVisual()
        {
            bool open = !requireCleared;

            if (visualOff != null) visualOff.SetActive(!open);
            if (visualOn != null) visualOn.SetActive(open);
        }
    }
}
