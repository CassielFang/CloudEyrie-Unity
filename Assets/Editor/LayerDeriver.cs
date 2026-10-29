using System.IO;
using UnityEditor;
using UnityEngine;

namespace CloudEyrie.EditorTools
{
    /// <summary>
    /// 从每关唯一的那张远景长卷派生出两层，补出纵深。
    ///
    /// 现状：`Assets/Levels/&lt;关&gt;/Resources/&lt;关&gt;/bg/` 里只有一张 `*_far.png`，
    /// 主题里也只挂了一层 —— 画面自然"平"。美术没有交付分层素材，
    /// 所以这里用**算法派生**：
    /// <list type="bullet">
    ///   <item><b>haze</b>：重模糊 + 提亮 + 降 alpha → 放到最远，做雾/大气；</item>
    ///   <item><b>near</b>：只取下半部分、压暗成剪影、上沿做羽化 → 放到最近，当前景山脊。</item>
    /// </list>
    /// 两层都从同一张图派生，所以色调与笔触天然一致，不会像拼素材那样违和。
    ///
    /// ⚠️ 逐像素调用 <c>GetPixel</c> 在 1920×1080 上会超过编辑器 eval 的 5 秒上限，
    /// 所以这里一律用 <c>GetPixels32</c> 取整块数组再算。
    /// </summary>
    public static class LayerDeriver
    {
        private static readonly string[] Realms = { "Mu", "Huo", "Tu", "Jin", "Shui", "Altar" };

        [MenuItem("CloudEyrie/美术工具/派生视差层（全部关卡）")]
        public static void DeriveAll()
        {
            foreach (string realm in Realms)
            {
                Derive(realm);
            }

            AssetDatabase.Refresh();
            Debug.Log("[LayerDeriver] 派生完成");
        }

        /// <summary>供编辑器内批量调用：一次只处理一个关卡，避免超时</summary>
        public static string Derive(string realm)
        {
            string dir = $"Assets/Levels/{realm}/Resources/{realm}/bg";
            string farPath = $"{dir}/{realm.ToLowerInvariant()}_far.png";

            if (!File.Exists(farPath))
            {
                return "missing " + farPath;
            }

            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            src.LoadImage(File.ReadAllBytes(farPath));

            Color32[] px = src.GetPixels32();
            int w = src.width;
            int h = src.height;

            // 降采样到 1/4：派生出来的两层本身就是"雾"与"剪影"，不需要原分辨率；
            // 原尺寸处理 + 两次 PNG 编码会超过编辑器 eval 的 5 秒上限。
            const int step = 4;
            int w2 = Mathf.Max(1, w / step);
            int h2 = Mathf.Max(1, h / step);
            var small = new Color32[w2 * h2];

            for (int y = 0; y < h2; y++)
            {
                for (int x = 0; x < w2; x++)
                {
                    small[y * w2 + x] = px[(y * step) * w + x * step];
                }
            }

            SaveHaze(small, w2, h2, $"{dir}/{realm.ToLowerInvariant()}_haze.png");
            SaveNear(small, w2, h2, $"{dir}/{realm.ToLowerInvariant()}_near.png");

            Object.DestroyImmediate(src);
            return realm + " derived " + w2 + "x" + h2;
        }

        /// <summary>雾层：8× 块平均（近似大半径模糊）+ 提亮 + 半透明</summary>
        private static void SaveHaze(Color32[] src, int w, int h, string outPath)
        {
            var dst = new Color32[w * h];
            const int block = 8;

            for (int y = 0; y < h; y += block)
            {
                for (int x = 0; x < w; x += block)
                {
                    int r = 0, g = 0, b = 0, n = 0;

                    for (int dy = 0; dy < block && y + dy < h; dy++)
                    {
                        for (int dx = 0; dx < block && x + dx < w; dx++)
                        {
                            Color32 c = src[(y + dy) * w + x + dx];
                            r += c.r; g += c.g; b += c.b; n++;
                        }
                    }

                    byte ar = (byte)(r / n), ag = (byte)(g / n), ab = (byte)(b / n);

                    // 提亮 + 降饱和
                    byte luma = (byte)((ar * 77 + ag * 150 + ab * 29) >> 8);
                    ar = (byte)Mathf.Min(255, (ar + luma) / 2 + 14);
                    ag = (byte)Mathf.Min(255, (ag + luma) / 2 + 14);
                    ab = (byte)Mathf.Min(255, (ab + luma) / 2 + 18);

                    // alpha 别太高：雾层是**打底**，216 会把整屏糊成一片单色（实测过）。
                    var c2 = new Color32(ar, ag, ab, 70);

                    for (int dy = 0; dy < block && y + dy < h; dy++)
                    {
                        for (int dx = 0; dx < block && x + dx < w; dx++)
                        {
                            dst[(y + dy) * w + x + dx] = c2;
                        }
                    }
                }
            }

            Write(dst, w, h, outPath);
        }

        /// <summary>近景层：保留下 45%，压成剪影，上沿羽化</summary>
        private static void SaveNear(Color32[] src, int w, int h, string outPath)
        {
            var dst = new Color32[w * h];
            int cut = Mathf.RoundToInt(h * 0.55f);
            int feather = Mathf.Max(8, h / 12);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (y >= cut)
                    {
                        dst[y * w + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    Color32 c = src[y * w + x];
                    int luma = (c.r * 77 + c.g * 150 + c.b * 29) >> 8;

                    int shade = Mathf.RoundToInt(Mathf.Lerp(16f, 78f, luma / 255f));
                    float edge = Mathf.Clamp01((cut - y) / (float)feather);

                    byte a = (byte)Mathf.RoundToInt(Mathf.Lerp(64f, 235f, edge));
                    dst[y * w + x] = new Color32((byte)shade, (byte)(shade + 2), (byte)(shade + 6), a);
                }
            }

            Write(dst, w, h, outPath);
        }

        private static void Write(Color32[] px, int w, int h, string path)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
