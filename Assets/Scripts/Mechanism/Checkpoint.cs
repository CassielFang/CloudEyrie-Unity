using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 存档点。设计稿里每境的终点传送门前必设一座，形制就是**灵泉石台**：
    /// 踩到即回满灵炁 + 记住复活点。
    ///
    /// <b>为什么要单独一个组件，而不是靠灵泉石台自己判断</b>
    ///
    /// 灵泉的"回炁"是**持续**的（站在里面每秒回 30，见 <c>LevelDirector.UpdateSpring</c>），
    /// 而"记住复活点"是**一次性**的。两件事的时机不同，混在一个组件里会出现
    /// 「每帧都在写复活点」这种看着无害、但会让复活点跟着玩家在泉里走动而漂移的问题。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Checkpoint : LevelMechanism
    {
        [Tooltip("复活点 id。**同一关内唯一** —— 死亡重开时按它找落点")]
        public string checkpointId = "spring";

        [Tooltip("踩到时是否回满灵炁")]
        [SerializeField] private bool restoreSpirit = true;

        [Tooltip("首次激活时给的提示。留空则不提示")]
        [SerializeField] private string firstHint = "灵泉·已记录";

        [Tooltip("已经激活过的存档点，再踩上来就不再提示（免得反复刷屏）")]
        [SerializeField] private bool hintOnlyOnce = true;

        private bool _activated;

        private void Reset()
        {
            Collider2D col = GetComponent<Collider2D>();

            if (col != null)
            {
                col.isTrigger = true;
            }

            gameObject.layer = PhysicsGroups.World;
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
            if (LevelHost.Instance != null)
            {
                LevelHost.Instance.SetCheckpoint(checkpointId);
            }

            // 接触灵泉之后解除形态限制：灵雾/灵合可以随意切
            QingheController qinghe = who != null ? who.GetComponentInParent<QingheController>() : null;
            CompanionStateMachine forms = qinghe != null ? qinghe.GetComponent<CompanionStateMachine>() : null;

            if (forms != null)
            {
                forms.UnlockForms();
            }

            if (restoreSpirit)
            {
                SpiritEnergySystem.ResetToFull();
            }

            if (_activated && hintOnlyOnce)
            {
                return;
            }

            _activated = true;

            if (!string.IsNullOrEmpty(firstHint))
            {
                EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = firstHint });
            }

            GameLog.I($"[Checkpoint] 记录复活点「{checkpointId}」（灵炁回满）");
        }
    }
}
