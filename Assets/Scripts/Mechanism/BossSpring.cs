using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// Boss 战场上的灵泉：站进去回灵炁，但**泉内不能放灵弹**。
    ///
    /// 为什么单独写一个而不是复用 <see cref="Checkpoint"/>：
    /// 关卡里的灵泉位置由 <c>LevelDirector</c> 按关卡配置逐帧判定
    /// （<c>SpiritEnergySystem.SetInSpring</c> 只由它维护），而祭坛场景是独立一关，
    /// 没有那条配置项。这个组件自己用触发器维护「在泉内」这个状态，
    /// <c>CombatController.FireMistShot</c> 里的禁射逻辑就直接生效了。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class BossSpring : MonoBehaviour
    {
        [Tooltip("每秒回复的灵炁")]
        [SerializeField] private float restorePerSecond = 30f;

        [Tooltip("最多回到最大灵炁的这个比例")]
        [SerializeField] private float restoreCapRatio = 0.8f;

        private bool _inside;
        private QingheController _player;

        private void Reset()
        {
            Collider2D col = GetComponent<Collider2D>();
            col.isTrigger = true;
        }

        private void Awake()
        {
            Collider2D col = GetComponent<Collider2D>();
            col.isTrigger = true;
        }

        private void OnDisable()
        {
            if (_inside)
            {
                _inside = false;
                SpiritEnergySystem.SetInSpring(false);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponentInParent<QingheController>() == null)
            {
                return;
            }

            _player = other.GetComponentInParent<QingheController>();
            _inside = true;
            SpiritEnergySystem.SetInSpring(true);
            EventBus.Emit(GameEvents.HintRequested,
                          new HintEvent { Text = "灵泉：在这里回灵炁（泉内不能放灵弹）", Seconds = 3f });
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (other.GetComponentInParent<QingheController>() == null)
            {
                return;
            }

            _inside = false;
            SpiritEnergySystem.SetInSpring(false);
        }

        private void Update()
        {
            if (!_inside)
            {
                return;
            }

            float cap = SpiritEnergySystem.Max * restoreCapRatio;

            if (SpiritEnergySystem.GetCurrent() < cap)
            {
                SpiritEnergySystem.Restore(restorePerSecond * Time.deltaTime);
            }
        }
    }
}
