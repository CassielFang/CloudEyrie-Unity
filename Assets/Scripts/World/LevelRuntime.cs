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
