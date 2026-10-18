using System.Collections.Generic;
using UnityEngine;

namespace CloudEyrie
{
    /// <summary>
    /// 关卡场景里的「编排执行者」：读 <see cref="LevelDefinition.segments"/> 里的 cue，
    /// 按 cue 说的去锁相机 / 换 BGM / 平移背景 / 交还操作权。
    ///
    /// <b>它取代的是 <c>LevelDirector.EnterSegment</c> 里那个写死的 <c>switch (seg)</c></b>。
    /// 那个 switch 把「第几段该干什么」钉死在代码里，于是新关卡想配自己的编排就必须改代码；
    /// 现在同样的行为写在关卡的 <see cref="LevelDefinition"/> 数据里。
    ///
    /// 依赖方向是「<b>宿主注入</b>」而不是自己去场景里找：
    /// 相机 / 背景 / 玩家都在 <c>Host</c> 场景（常驻），而本组件在关卡场景里 ——
    /// 跨场景 <c>FindAnyObjectByType</c> 在多关卡叠加时可能找到别的副本，
    /// 所以由 <see cref="LevelHost"/> 在加载完关卡后显式传进来。
    /// </summary>
    public class LevelRuntime : MonoBehaviour
    {
        private LevelDefinition _def;
        private CameraFollow _camera;
        private ParallaxBackground _background;
        private QingheController _player;
        private ScreenFade _fade;

        /// <summary>已经触发过的 cue id —— cue 只生效一次。</summary>
        private readonly HashSet<string> _fired = new HashSet<string>();

        private float _nextEnemyCheck;
        private bool _enemiesClearedFired;

        /// <summary>
        /// 坠落保险丝：低于这个 y 就判定为「掉出关卡」。
        ///
        /// 设计稿的坑并不是每个都有 Hazard 安全网（有些坑底下什么都没有），
        /// 而工程里没有全局 kill plane —— 掉进这种坑会**一直往下掉**，
        /// 相机跟到纵向边界后角色就彻底看不见了。
        /// 这里的阈值在所有岩浆/坠落区（约 -820 ~ -930）之下。
        /// </summary>
        [SerializeField] private float fallKillY = -1000f;

        private bool _fallRestarting;

        /// <summary>
        /// 本段的重生点（由 <see cref="SegmentVolume"/> 在玩家进入新段落时写入）。
        ///
        /// 设计稿的掉落惩罚是「落坑/碰岩浆**回到本段起点**，不回存档点」——
        /// 整关重开会让玩家把已经打过的段再打一遍，挫败感大得多。
        /// 没有记录时（例如刚进场就掉下去）退回原来的「重开本关」。
        /// </summary>
        private Vector3? _segmentRespawn;

        /// <summary>记下当前段落的进入点。空值表示清除（换关时用）。</summary>
        public void SetSegmentRespawn(Vector3 worldPos)
        {
            _segmentRespawn = worldPos;
        }

        public void ClearSegmentRespawn()
        {
            _segmentRespawn = null;
        }

        public LevelDefinition Definition
        {
            get { return _def; }
        }

        /// <summary>cue 触发时广播。参数是 cue 的 id。</summary>
        public event System.Action<string> OnCueFired;

        public void Init(LevelDefinition def, CameraFollow camera, ParallaxBackground background,
                         QingheController player, ScreenFade fade)
        {
            _def = def;
            _camera = camera;
            _background = background;
            _player = player;
            _fade = fade;

            _fired.Clear();
            _enemiesClearedFired = false;

            FireByTrigger(CueTrigger.OnStart);

            GameLog.I($"[Level] {def.displayName} 已就绪（{def.segments.Length} 条编排）");
        }

        private void Update()
        {
            if (_def == null || _def.segments == null)
            {
                return;
            }

            FireByTrigger(CueTrigger.OnPlayerX);
            CheckAllEnemiesCleared();
            CheckFallenOut();
        }

        /// <summary>
        /// 掉出关卡时重开本关（回最近存档点）。只触发一次 ——
        /// 换关协程是异步的，不设标志会在压黑那几帧里反复调 RestartCurrent。
        /// </summary>
        private void CheckFallenOut()
        {
            if (_fallRestarting || _player == null || _player.transform.position.y > fallKillY)
            {
                return;
            }

            // 有段落重生点就回段首（设计稿的规则），没有才整关重开
            if (_segmentRespawn.HasValue)
            {
                GameLog.I($"[Level] 玩家坠出关卡（y={_player.transform.position.y:F0}），回到本段起点");
                RespawnAtSegment();
                return;
            }

            _fallRestarting = true;
            GameLog.I($"[Level] 玩家坠出关卡（y={_player.transform.position.y:F0}），重开本关");
            LevelHost.Instance?.RestartCurrent();
        }

        /// <summary>把玩家搬回段首并清掉速度，不重载场景（不回存档点、不重置机关）。</summary>
        private void RespawnAtSegment()
        {
            if (!_segmentRespawn.HasValue || _player == null)
            {
                return;
            }

            _fallRestarting = true;

            Rigidbody2D body = _player.GetComponent<Rigidbody2D>();

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.position = _segmentRespawn.Value;
            }

            _player.transform.position = _segmentRespawn.Value;

            // 下一帧再解锁，避免同一帧里连续判定
            StartCoroutine(ReleaseFallFlag());
        }

        private System.Collections.IEnumerator ReleaseFallFlag()
        {
            yield return null;
            yield return null;
            _fallRestarting = false;
        }

        /// <summary>
        /// 触发所有满足条件的 cue。
        ///
        /// 注意<b>不 break</b>：玩家一帧内越过多条阈值时（比如从传送门落地直接落到后半关），
        /// 中间那些段的编排也要按顺序补齐 —— 漏掉的话会出现「锁了相机但没人解锁」
        /// 这种卡死状态，而且极难从现象反推。
        /// </summary>
        private void FireByTrigger(CueTrigger trigger)
        {
            if (trigger == CueTrigger.OnPlayerX)
            {
                float x = _player != null ? _player.transform.position.x : 0f;

                for (int i = 0; i < _def.segments.Length; i++)
                {
                    SegmentCue cue = _def.segments[i];

                    if (cue != null && cue.trigger == CueTrigger.OnPlayerX && x >= cue.atX)
                    {
                        Fire(cue.id);
                    }
                }

                return;
            }

            for (int i = 0; i < _def.segments.Length; i++)
            {
                SegmentCue cue = _def.segments[i];

                if (cue != null && cue.trigger == trigger)
                {
                    Fire(cue.id);
                }
            }
        }

        /// <summary>每 0.25 秒数一次场上还有没有敌人，清空时发一次 OnAllEnemiesCleared。</summary>
        private void CheckAllEnemiesCleared()
        {
            if (_enemiesClearedFired || Time.time < _nextEnemyCheck)
            {
                return;
            }

            _nextEnemyCheck = Time.time + 0.25f;

            EnemyAI[] enemies = FindObjectsByType<EnemyAI>(FindObjectsInactive.Exclude);

            if (enemies.Length > 0)
            {
                return;
            }

            _enemiesClearedFired = true;
            FireByTrigger(CueTrigger.OnAllEnemiesCleared);
        }

        /// <summary>触发一条 cue（幂等）。返回是否真的首次触发。</summary>
        public bool Fire(string cueId)
        {
            if (string.IsNullOrEmpty(cueId) || !_fired.Add(cueId))
            {
                return false;
            }

            SegmentCue cue = Find(cueId);

            if (cue == null)
            {
                // 名字写错是最常见的配置错误，而它不会自己冒出来
                GameLog.W($"[Level] 找不到 id 为「{cueId}」的编排 —— 检查 LevelDefinition 里的 segments");
                return false;
            }

            Apply(cue);

            if (OnCueFired != null)
            {
                OnCueFired(cueId);
            }

            return true;
        }

        private SegmentCue Find(string cueId)
        {
            if (_def == null || _def.segments == null)
            {
                return null;
            }

            for (int i = 0; i < _def.segments.Length; i++)
            {
                if (_def.segments[i] != null && _def.segments[i].id == cueId)
                {
                    return _def.segments[i];
                }
            }

            return null;
        }

        /// <summary>就是旧 <c>EnterSegment</c> 的每一支，但改成读数据。</summary>
        private void Apply(SegmentCue cue)
        {
            if (!string.IsNullOrEmpty(cue.note))
            {
                GameLog.I($"[Level] 段落「{cue.id}」{cue.note}");
            }

            switch (cue.camera)
            {
                case CameraMode.Lock:
                    if (_camera != null) _camera.Lock();
                    break;

                case CameraMode.Unlock:
                    if (_camera != null) _camera.Unlock();
                    break;
            }

            if (cue.autoPanSpeed >= 0f && _background != null)
            {
                _background.autoPanSpeed = cue.autoPanSpeed;
            }

            if (cue.setBgm)
            {
                GameAudio.PlayBgm(cue.bgm);
            }

            if (cue.releasePlayerControl && _player != null)
            {
                _player.ScriptedMoveX = 0f;
            }

            ApplyFade(cue);

            // 具体动作交给关心它的组件 —— 数据表表达不了 boss.Begin()
            if (!string.IsNullOrEmpty(cue.emitEvent))
            {
                EventBus.Emit(cue.emitEvent);
            }
        }

        private void ApplyFade(SegmentCue cue)
        {
            if (_fade == null || cue.fade == FadeAction.None)
            {
                return;
            }

            switch (cue.fade)
            {
                case FadeAction.BlackThenFadeIn:
                    _fade.SetBlack();
                    _fade.FadeTo(0f, cue.fadeSeconds);
                    break;

                case FadeAction.FadeOut:
                    _fade.FadeTo(1f, cue.fadeSeconds);
                    break;
            }
        }
    }
}
