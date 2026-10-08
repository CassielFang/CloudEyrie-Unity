using System;
using UnityEngine;
using UnityEngine.UI;

namespace CloudEyrie
{
    /// <summary>
    /// 阶段转场叠层：**把画面调暗 → 叠上全屏动画 → 动画结束推进阶段**。
    /// 对应 Cocos 侧 <c>fx/StageTransition.ts</c>。
    ///
    /// 用法：
    /// <code>
    /// transition.Play("九尾狐-幻影狂乱衔接", () =&gt; EnterPhase2());
    /// </code>
    ///
    /// <b>为什么不用 <c>Ui/ScreenStage</c></b>
    ///
    /// 那是「整页场景组件」——要求宿主场景摆好背景/暗角/模板三件套。
    /// 这里要的是**叠在战斗场景上的临时层**，契约不匹配。
    ///
    /// <b>Unity 侧的实现方式</b>
    ///
    /// Cocos 版挂在 Canvas 下、自己画遮罩。Unity 这边干脆**自带一个
    /// ScreenSpaceOverlay 的 Canvas**（排序值拉高），这样「叠在最上层」是构造上保证的，
    /// 不依赖宿主场景里有没有 Canvas、也不用去抢 siblingIndex。
    /// </summary>
    public class StageTransition : MonoBehaviour
    {
        private enum Step
        {
            Idle,
            DimIn,
            Playing,
            DimOut,
        }

        [Tooltip("压暗程度，0~1，越大背景越黑")]
        public float dimOpacity = 0.745f;      // 对应 Cocos 的 190/255

        [Tooltip("压暗用时")]
        public float dimInDuration = 0.35f;

        [Tooltip("收尾用时")]
        public float dimOutDuration = 0.5f;

        [Tooltip("停顿时长：动画播完后再停多久才收尾")]
        public float holdDuration = 0.4f;

        [Tooltip("资源目录")]
        public string basePath = "video_fx";

        [Tooltip("叠层 Canvas 的排序值。要高于游戏内所有 UI")]
        public int sortingOrder = 500;

        [Tooltip("在「恰好铺满」之上再放大的比例。留一点溢出，避免四边露缝（1.0 = 不溢出）")]
        public float coverOverscan = 1.08f;

        private Step _step = Step.Idle;
        private float _timer;
        private float _holdTimer;
        private float _dimOutTimer;
        private bool _animDone;
        private float _targetDim = 0.745f;
        private float _hold = 0.4f;
        private Action _onComplete;

        private Image _maskImage;
        private RectTransform _maskRect;
        private Image _stageImage;
        private RectTransform _stageRect;
        private SequencePlayer _player;
        private Canvas _canvas;

        /// <summary>是否正在演出（调用方可据此锁住输入）</summary>
        public bool IsPlaying
        {
            get { return _step != Step.Idle; }
        }

        private void Awake()
        {
            Build();
            SetVisible(false);
        }

        /// <summary>
        /// 防御性复位：演出中途这个组件被销毁 / 关掉，也要把 timeScale 还回去。
        /// 卡在 0 的话游戏看起来"死了"，而从现象几乎推不回原因。
        /// </summary>
        private void OnDisable()
        {
            if (_step != Step.Idle)
            {
                _step = Step.Idle;
                GameFlow.ForceReset();
            }
        }

        /// <summary>播一段转场；<paramref name="onComplete"/> 在收尾结束、叠层隐藏之后调一次。</summary>
        public void Play(string name, Action onComplete = null, float dimTo = -1f, float hold = -1f)
        {
            if (_step != Step.Idle)
            {
                Debug.LogWarning("[Transition] 上一段还没演完，忽略这次调用");
                return;
            }

            SequencePlayer player = _player;

            if (player == null)
            {
                Debug.LogWarning("[Transition] 没有动画节点，转场不播");
                onComplete?.Invoke();
                return;
            }

            _targetDim = dimTo >= 0f ? dimTo : dimOpacity;
            _hold = hold >= 0f ? hold : holdDuration;
            _onComplete = onComplete;
            _timer = 0f;
            _holdTimer = 0f;
            _dimOutTimer = 0f;
            _animDone = false;
            _step = Step.DimIn;

            SetVisible(true);
            SetMaskAlpha(0f);

            // 进演出：停住游戏逻辑 + 闸掉玩家输入（见 GameFlow 的说明）
            GameFlow.Push();

            // 先读索引拿到画布尺寸，才能按 cover 摆好；读完再起播
            player.LoadManifest(name, ok =>
            {
                if (!ok)
                {
                    // 素材缺失不该卡住流程：跳过动画，直接收尾推进
                    Debug.LogWarning($"[Transition] 转场动画 \"{name}\" 加载失败，跳过动画直接推进");
                    _animDone = true;
                    return;
                }

                if (_step == Step.Idle)
                {
                    return;                       // 加载期间被取消
                }

                if (player.TryGetCanvasSize(out Vector2 size))
                {
                    FitStage(size.x, size.y);
                }

                // 素材就位，现在才让动画层显形（见 Build 里为什么一直关着）
                if (_stageImage != null)
                {
                    _stageImage.enabled = true;
                }

                player.Play(name, false, 0, -1, 1f, () => { _animDone = true; });
            });
        }

        private void Update()
        {
            // ⚠️ **必须用未缩放时钟**：演出期间 GameFlow 把 timeScale 置 0，
            // 用 Time.deltaTime 的话这个状态机自己也停了，转场永远收不了尾。
            float dt = Time.unscaledDeltaTime;

            switch (_step)
            {
                case Step.DimIn:
                {
                    _timer += dt;
                    float t = Mathf.Min(1f, _timer / Mathf.Max(0.01f, dimInDuration));
                    SetMaskAlpha(_targetDim * t);

                    if (t >= 1f)
                    {
                        _step = Step.Playing;
                        _holdTimer = 0f;
                    }

                    break;
                }

                case Step.Playing:
                {
                    // 动画由 SequencePlayer 自己推进，这里只等它播完再停顿
                    if (!_animDone)
                    {
                        break;
                    }

                    _holdTimer += dt;

                    if (_holdTimer >= _hold)
                    {
                        BeginDimOut();
                    }

                    break;
                }

                case Step.DimOut:
                {
                    _dimOutTimer += dt;
                    float t = Mathf.Min(1f, _dimOutTimer / Mathf.Max(0.01f, dimOutDuration));
                    SetMaskAlpha(_targetDim * (1f - t));

                    if (t >= 1f)
                    {
                        Finish();
                    }

                    break;
                }
            }
        }

        // ================= 内部 =================

        private void BeginDimOut()
        {
            _step = Step.DimOut;
            _dimOutTimer = 0f;
            _player?.Stop();
        }

        private void Finish()
        {
            _step = Step.Idle;
            SetVisible(false);
            SetMaskAlpha(0f);

            GameFlow.Pop();

            Action done = _onComplete;
            _onComplete = null;
            done?.Invoke();
        }

        private void SetVisible(bool on)
        {
            if (_canvas != null)
            {
                _canvas.enabled = on;
            }
        }

        private void SetMaskAlpha(float a)
        {
            if (_maskImage == null)
            {
                return;
            }

            Color c = _maskImage.color;
            c.a = Mathf.Clamp01(a);
            _maskImage.color = c;
        }

        /// <summary>
        /// 铺满屏幕（cover）。
        ///
        /// ⚠️ <b>两个坑都在这里改掉了：</b>
        ///
        /// <b>1. 不能拿 <c>Screen.width/height</c>（像素）去和 <c>sizeDelta</c>（画布单位）比。</b>
        /// 这个 Canvas 的 <see cref="CanvasScaler"/> 是 <c>ScaleWithScreenSize</c> +
        /// 参考分辨率 1920×1080，画布单位到像素之间还隔着一个 scaleFactor。
        /// 拿像素去比，算出来的比例是错的 —— 有时候溢出一大截，有时候盖不满。
        /// 正确做法是拿**画布自己的 rect 尺寸**（已经是画布单位）来比。
        ///
        /// <b>2. 还要留一点溢出余量。</b> 恰好 cover 时，任何浮点误差或宽高比的细微差别
        /// 都会在四边露出细缝（实测反馈「四周还是有没覆盖的地方」）。
        /// 宁可裁掉一点点也不能露边 —— 所以按 <see cref="coverOverscan"/> 再放大一档。
        /// </summary>
        private void FitStage(float w, float h)
        {
            if (_stageRect == null || w <= 0f || h <= 0f)
            {
                return;
            }

            _stageRect.sizeDelta = new Vector2(w, h);

            // 画布自己的尺寸，单位就是画布单位 —— 和 sizeDelta 同一套，可以直接比
            RectTransform canvasRect = _canvas != null ? _canvas.transform as RectTransform : null;
            Vector2 screenUnits = canvasRect != null
                ? canvasRect.rect.size
                : new Vector2(Screen.width, Screen.height);

            if (screenUnits.x <= 0f || screenUnits.y <= 0f)
            {
                return;
            }

            float cover = Mathf.Max(screenUnits.x / w, screenUnits.y / h);
            float s = cover * Mathf.Max(1f, coverOverscan);
            _stageRect.localScale = new Vector3(s, s, 1f);
        }

        private void Build()
        {
            // 幂等：重复进入时先清掉上一批
            Transform old = transform.Find("TransitionCanvas");

            if (old != null)
            {
                Destroy(old.gameObject);
            }

            // ---- 自带一个全屏 Canvas，保证叠在最上层 ----
            GameObject canvasGo = new GameObject("TransitionCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // ---- 遮罩：全屏黑 ----
            GameObject maskGo = new GameObject("Mask", typeof(RectTransform), typeof(Image));
            maskGo.transform.SetParent(canvasGo.transform, false);

            _maskRect = maskGo.GetComponent<RectTransform>();
            _maskRect.anchorMin = Vector2.zero;
            _maskRect.anchorMax = Vector2.one;
            _maskRect.offsetMin = Vector2.zero;
            _maskRect.offsetMax = Vector2.zero;

            _maskImage = maskGo.GetComponent<Image>();
            _maskImage.color = new Color(0f, 0f, 0f, 0f);
            _maskImage.raycastTarget = false;

            // ---- 动画层：Image + SequencePlayer ----
            GameObject stageGo = new GameObject("Stage", typeof(RectTransform), typeof(Image));
            stageGo.transform.SetParent(canvasGo.transform, false);

            _stageRect = stageGo.GetComponent<RectTransform>();
            _stageRect.anchorMin = new Vector2(0.5f, 0.5f);
            _stageRect.anchorMax = new Vector2(0.5f, 0.5f);
            _stageRect.pivot = new Vector2(0.5f, 0.5f);
            _stageRect.anchoredPosition = Vector2.zero;

            _stageImage = stageGo.GetComponent<Image>();
            _stageImage.raycastTarget = false;

            // ⚠️ 保持关闭，直到动画真的起播（见 Play）。
            // 没有 sprite 的 Image 会渲染成一块纯白矩形 —— 压暗阶段会先糊满一屏白，
            // 看起来像闪屏。这里不能用「靠素材到位」来兜底，只能显式关。
            _stageImage.enabled = false;

            _player = stageGo.AddComponent<SequencePlayer>();
            _player.basePath = basePath;
            _player.unscaledTime = true;      // 演出层：不受 timeScale 影响

            // 全屏帧又大又只播一遍：前置留 1 页防卡，后置不留（省显存）
            _player.aheadPages = 1;
            _player.behindPages = 0;
        }
    }
}
