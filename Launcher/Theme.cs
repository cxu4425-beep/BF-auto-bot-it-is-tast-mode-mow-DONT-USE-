using System.Drawing;
using System.Drawing.Drawing2D;

namespace BloxFruitsLauncher
{
    /// <summary>深空科幻配色與共用繪圖工具。改一個地方整體換色。</summary>
    internal static class Theme
    {
        // 深空底色：接近純黑但帶藍紫，比純灰更有距離感
        public static readonly Color Background = Color.FromArgb(6, 10, 20);
        public static readonly Color BackgroundDeep = Color.FromArgb(3, 5, 12);
        public static readonly Color Surface = Color.FromArgb(11, 18, 34);
        public static readonly Color SurfaceHi = Color.FromArgb(17, 28, 50);
        public static readonly Color Border = Color.FromArgb(32, 58, 96);
        public static readonly Color Grid = Color.FromArgb(16, 30, 52);

        public static readonly Color Text = Color.FromArgb(214, 234, 255);
        public static readonly Color TextDim = Color.FromArgb(104, 137, 184);

        // 主色改成青藍霓虹，輔色用紫紅拉出對比
        public static readonly Color Accent = Color.FromArgb(0, 229, 255);
        public static readonly Color AccentHi = Color.FromArgb(120, 245, 255);
        public static readonly Color Violet = Color.FromArgb(177, 74, 237);
        public static readonly Color Ok = Color.FromArgb(0, 255, 163);
        public static readonly Color Warn = Color.FromArgb(255, 176, 32);
        public static readonly Color Fail = Color.FromArgb(255, 61, 110);

        public static Font UI(float size, FontStyle style = FontStyle.Regular)
            => new Font("Segoe UI", size, style, GraphicsUnit.Point);

        public static Font Mono(float size)
            => new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);

        // OnPaint 每秒會被呼叫幾十次，在裡面 new Font 等於持續洩漏 GDI 控制代碼。
        // 需要在繪圖中使用的字型一律走這些快取實例。
        public static readonly Font FontTitle = UI(19f, FontStyle.Bold);
        public static readonly Font FontSubtitle = UI(9f);
        public static readonly Font FontStep = UI(9.75f, FontStyle.Bold);
        public static readonly Font FontDetail = UI(9f);

        /// <summary>圓角矩形路徑。呼叫端負責 Dispose。</summary>
        public static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            if (d <= 0 || r.Width <= d || r.Height <= d)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>兩色線性內插，用來做滑順的 hover 過渡。</summary>
        public static Color Lerp(Color a, Color b, float t)
        {
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>在圓角矩形外圍畫一圈遞減的輝光。用在強調用的按鈕與卡片。</summary>
        public static void DrawGlow(Graphics g, Rectangle rect, int radius, Color color, int layers, float strength)
        {
            for (int i = layers; i >= 1; i--)
            {
                var r = Rectangle.Inflate(rect, i, i);
                int alpha = (int)(strength * 26f * (1f - (float)i / (layers + 1)));
                if (alpha <= 0) continue;
                using var path = RoundedRect(r, radius + i);
                using var pen = new Pen(Color.FromArgb(alpha, color), 2f);
                g.DrawPath(pen, path);
            }
        }
    }
}
