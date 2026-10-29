using UnityEngine;

namespace CloudEyrie.Fx
{
    /// <summary>
    /// 让场景道具跟着境界色调走。
    ///
    /// 为什么要这一步：木境的道具来自两批美术 ——
    /// <c>Resources/场景小物件/</c>（冷绿灰，平均 RGB 约 150,184,180）
    /// 和 <c>Mu/props/</c>（暖褐，平均 RGB 约 157,129,90）。
    /// 同一块绿色画面里混着暖褐色木板，就是玩家说的"违和"。
    ///
    /// 重画素材是最终解法；在那之前，先按境界色调统一一次能立刻缓解：
    /// 道具乘上 <see cref="LevelTheme.tint"/>，暖褐在木境被拉向青绿、
    /// 在火境被拉向暖橙 —— 同一张图在不同境界里就"属于那里"了。
    ///
    /// 只碰**道具**（木板、弹跳花、藤蔓），不碰角色、敌人、机关火盆、UI。
    /// </summary>
    [DefaultExecutionOrder(80)]
    public class PropTint : MonoBehaviour
    {
        /// <summary>命中这些名字的精灵算"道具"</summary>
        private static readonly string[] PropTokens =
        {
            "platform", "board", "bounce", "vine", "flower", "seed",
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("[PropTint]");
            go.AddComponent<PropTint>();
        }

        private bool _applied;
        private int _retries;

        /// <summary>
        /// 最多重试多少帧。
        ///
        /// ⚠️ 必须是**有限**的：第一版写成"没拿到主题就每帧重试"，
        /// 而 TryApply 里有一次全场景 FindObjectsByType —— 主题迟迟不来时
        /// 每帧扫全场景，编辑器直接卡死（实测第九次）。
        /// 3 秒还拿不到就放弃，一次着色而已，不值得让编辑器陪葬。
        /// </summary>
        private const int MaxRetries = 180;

        private void Start()
        {
            TryApply();
        }

        private void Update()
        {
            // 关卡是异步加载的：Start 里 LevelHost.Current 往往还是 null。
            if (_applied || _retries >= MaxRetries)
            {
                return;
            }

            _retries++;
            TryApply();
        }

        private void TryApply()
        {
            LevelHost host = LevelHost.Instance;
            LevelDefinition def = host != null ? host.Current : null;

            if (def == null || def.theme == null)
            {
                return;
            }

            Color tint = def.theme.tint;

            if (tint == Color.white)
            {
                _applied = true;
                return;
            }

            int tinted = 0;

            foreach (SpriteRenderer sr in FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude))
            {
                if (sr.sprite == null || sr.GetComponent<PropTintMark>() != null)
                {
                    continue;
                }

                string name = (sr.sprite.name + " " + sr.gameObject.name).ToLowerInvariant();
                bool isProp = false;

                foreach (string token in PropTokens)
                {
                    if (name.Contains(token))
                    {
                        isProp = true;
                        break;
                    }
                }

                if (!isProp)
                {
                    continue;
                }

                sr.gameObject.AddComponent<PropTintMark>();
                sr.color = new Color(sr.color.r * tint.r, sr.color.g * tint.g, sr.color.b * tint.b, sr.color.a);
                tinted++;
            }

            if (tinted > 0)
            {
                GameLog.I($"[PropTint] {tinted} 个道具按境界色调着色");
            }

            _applied = true;
        }
    }

    /// <summary>防重复着色的标记</summary>
    public class PropTintMark : MonoBehaviour
    {
    }
}
