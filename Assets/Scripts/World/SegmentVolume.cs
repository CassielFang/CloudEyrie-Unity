using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 摆在关卡场景里的触发体积：玩家踩进来就触发对应的编排
    /// （<see cref="SegmentCue.trigger"/> = <see cref="CueTrigger.OnVolumeEnter"/>）。
    ///
    /// <b>什么时候用它而不是 OnPlayerX</b>
    ///
    /// 按 x 坐标推进在一条横向长关卡里够用，但表达不了「进入这片区域才生效」——
    /// 比如一块高台上的区域、一个需要绕路才能到的房间。
    /// 体积是**可视化摆的**，策划拖一下就改了，不必去算坐标。
    ///
    /// ⚠️ 本节点必须挂在 <c>WORLD</c> 层，且碰撞体勾 <c>IsTrigger</c>。
    /// 留在 <c>Default</c> 层的话 <see cref="PhysicsGroups.Apply"/> 会把游戏层与它断开 ——
    /// 玩家走过去**不会有任何反应，也不报错**（跑一次「CloudEyrie/校验当前场景」能抓出来）。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class SegmentVolume : MonoBehaviour
    {
        [Tooltip("要触发的编排 id（LevelDefinition.segments 里的 id）")]
        public string cueId;

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

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (string.IsNullOrEmpty(cueId))
            {
                return;
            }

            // 只认玩家。敌人也会穿过触发区，不筛的话小怪一走过去就触发了
            if (other.GetComponentInParent<QingheController>() == null)
            {
                return;
            }

            LevelRuntime runtime = FindRuntime();

            if (runtime == null)
            {
                GameLog.W($"[SegmentVolume] 场景里没有 LevelRuntime，「{cueId}」触发不了");
                return;
            }

            runtime.Fire(cueId);

            // 同时记成「本段重生点」：掉坑/碰岩浆回到这里，而不是整关重开
            // （设计稿的掉落惩罚规则；见 LevelRuntime.CheckFallenOut）。
            runtime.SetSegmentRespawn(RespawnPoint());
        }

        /// <summary>
        /// 重生点取体积的**上沿中心**再抬一点：直接取中心会把玩家塞进地面里，
        /// 取上沿则落在触发区顶面上方，与正常站位一致。
        /// </summary>
        private Vector3 RespawnPoint()
        {
            Collider2D col = GetComponent<Collider2D>();

            if (col == null)
            {
                return transform.position;
            }

            Bounds b = col.bounds;
            float playerHalf = 60f;

            return new Vector3(b.center.x, b.max.y + playerHalf, transform.position.z);
        }

        /// <summary>本关的 LevelRuntime —— 它一般在场景根上，一个场景只有一个。</summary>
        private static LevelRuntime FindRuntime()
        {
            return FindAnyObjectByType<LevelRuntime>();
        }
    }
}
