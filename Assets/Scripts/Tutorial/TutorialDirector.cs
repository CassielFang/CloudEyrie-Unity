using UnityEngine;
using UnityEngine.UI;

namespace CloudEyrie.Tutorial
{
    /// <summary>
    /// 试炼场的分步引导。
    ///
    /// 关卡里的教学一直是"卡住 + 弹一句提示"：玩家不知道自己是在被教、也不知道还剩几步。
    /// 这里改成显式的五拍流程：**做对才推进**，每一步给一句"现在做什么"，
    /// 并且常驻在屏幕上方（不是三秒就消失的提示）。
    ///
    /// 判定都读**真实状态**（走了多远、木桩碎没碎、形态、火、碎片），
    /// 所以玩家用自己的方式完成也算过，不强制按预设顺序。
    /// </summary>
    public class TutorialDirector : MonoBehaviour
    {
        public enum StepKind
        {
            ReachX,
            BreakDummy,
            EnterMist,
            MakeFire,
            Collect,
        }

        [System.Serializable]
        public class Step
        {
            public StepKind kind;

            [Tooltip("这一步要做什么（常驻在屏幕上方）")]
            public string text;

            [Tooltip("ReachX 用：目标是 x 坐标")]
            public float targetX;

            [Tooltip("BreakDummy 用：要打碎的木桩")]
            public Damageable dummy;

            [Tooltip("MakeFire 用：网格里至少有几格火")]
            public int fireCells = 6;
        }

        [SerializeField] private Step[] steps = new Step[0];

        [Tooltip("完成全部步骤后给的提示")]
        [SerializeField] private string finishText = "试炼完成 · 灵炁与形态都已解锁";

        private int _index;
        private Text _banner;
        private QingheController _player;
        private int _collectBaseline;
        private bool _finished;

        private void Start()
        {
            _player = FindAnyObjectByType<QingheController>();

            // 优先挂到**已有的 HUD canvas** 上，而不是自己再造一个。
            //
            // 自己造的话场上会有两个 sortingOrder 都是 100 的 canvas，
            // 谁在上面没有定义 —— 现在正好没撞车，但这属于运气，不是设计。
            Canvas canvas = FindHudCanvas();

            if (canvas == null)
            {
                canvas = UiDraw.EnsureCanvas(transform);
            }

            RectTransform node = UiDraw.MakeNode(canvas.transform, "TutorialBanner");
            node.anchorMin = new Vector2(0.5f, 1f);
            node.anchorMax = new Vector2(0.5f, 1f);
            node.pivot = new Vector2(0.5f, 1f);
            node.anchoredPosition = new Vector2(0f, -150f);
            node.sizeDelta = new Vector2(1500f, 90f);

            _banner = UiDraw.MakeText(node, "Label", "", 34,
                new Color32(255, 240, 205, 255), ScreenStage.ThinOutline);
            _banner.alignment = TextAnchor.MiddleCenter;
            _banner.rectTransform.sizeDelta = new Vector2(1480f, 84f);

            _collectBaseline = PlayerProgress.FragmentCount;
            RefreshBanner();
        }

        /// <summary>找 HUD 那个 canvas（名字固定为 UiCanvas，且在 Hud 之下）</summary>
        private static Canvas FindHudCanvas()
        {
            Canvas[] all = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude);

            foreach (Canvas c in all)
            {
                if (c.name == "UiCanvas" && c.transform.parent != null && c.transform.parent.name == "Hud")
                {
                    return c;
                }
            }

            return null;
        }

        private void Update()
        {
            if (_finished || _index >= steps.Length)
            {
                return;
            }

            // 玩家可能比教学晚生成（独立试玩时由 bootstrap 补进场景），
            // Start 里抓到 null 的话，所有"走到某处"的判定就永远不会通过。
            if (_player == null)
            {
                _player = FindAnyObjectByType<QingheController>();
            }

            if (!IsStepDone(steps[_index]))
            {
                return;
            }

            _index++;
            RefreshBanner();
        }

        private bool IsStepDone(Step step)
        {
            switch (step.kind)
            {
                case StepKind.ReachX:
                    return _player != null && _player.transform.position.x >= step.targetX;

                case StepKind.BreakDummy:
                    return step.dummy == null || step.dummy.GetHp() <= 0.01f;

                case StepKind.EnterMist:
                {
                    CompanionStateMachine forms = _player != null ? _player.GetComponent<CompanionStateMachine>() : null;
                    return forms != null && (forms.GetCurrentForm() == CompanionForm.Mist || forms.IsFormUnlocked());
                }

                case StepKind.MakeFire:
                    return CountFire() >= step.fireCells;

                case StepKind.Collect:
                    return PlayerProgress.FragmentCount > _collectBaseline;

                default:
                    return false;
            }
        }

        private int CountFire()
        {
            Elements.MaterialGrid grid = FindAnyObjectByType<Elements.MaterialGrid>();

            if (grid == null)
            {
                return 0;
            }

            int n = 0;

            foreach (var cell in grid.Cells)
            {
                if (cell == Elements.MaterialType.Fire)
                {
                    n++;
                }
            }

            return n;
        }

        private void RefreshBanner()
        {
            if (_banner == null)
            {
                return;
            }

            if (_index >= steps.Length)
            {
                if (!_finished)
                {
                    _finished = true;
                    _banner.text = finishText;
                    EventBus.Emit(GameEvents.HintRequested, new HintEvent { Text = finishText, Seconds = 4f });

                    // 试炼场现在是「启境」的第一个场景 —— 教完自动接木境，
                    // 玩家不用再回标题页点一遍。
                    StartCoroutine(AdvanceToFirstLevel());
                }

                return;
            }

            _banner.text = $"<color=#FFD98A>教学 {_index + 1}/{steps.Length}</color>  {steps[_index].text}";
        }

        /// <summary>试炼完成 → 留出读提示的时间 → 进木境</summary>
        private System.Collections.IEnumerator AdvanceToFirstLevel()
        {
            // 3.5 秒而不是 2.5：结束语是「试炼完成 · 前往青芽境」，
            // 2.5 秒刚读完一半就切场景，观感像被抢走。
            yield return new WaitForSeconds(3.5f);

            LevelHost host = LevelHost.Instance;

            if (host != null)
            {
                host.EnterLevel("mu");
            }
        }
    }
}
