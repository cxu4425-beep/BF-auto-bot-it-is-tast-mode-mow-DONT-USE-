using System.Drawing;
using System.Drawing.Drawing2D;

namespace BloxFruitsLauncher
{
    /// <summary>深色配色與共用繪圖工具。集中放這裡，改一個地方整體換色。</summary>
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(18, 20, 26);
        public static readonly Color Surface = Color.FromArgb(26, 29, 38);
        public static readonly Color SurfaceHi = Color.FromArgb(34, 38, 49);
        public static readonly Color Border = Color.FromArgb(48, 54, 70);

        public static readonly Color Text = Color.FromArgb(232, 236, 245);
        public static readonly Color TextDim = Color.FromArgb(140, 149, 170);

        public static readonly Color Accent = Color.FromArgb(88, 166, 255);
        public static readonly Color AccentHi = Color.FromArgb(122, 187, 255);
        public static readonly Color Ok = Color.FromArgb(63, 185, 121);
        public static readonly Color Warn = Color.FromArgb(226, 173, 62);
        public static readonly Color Fail = Color.FromArgb(230, 90, 90);

        public static Font UI(float size, FontStyle style = FontStyle.Regular)
            => new Font("Segoe UI", size, style, GraphicsUnit.Point);

        public static Font Mono(float size)
            => new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);

        // OnPaint 每秒會被呼叫幾十次，在裡面 new Font 等於持續洩漏 GDI 控制代碼。
        // 需要在繪圖中使用的字型一律走這些快取實例。
        public static readonly Font FontTitle = UI(16f, FontStyle.Bold);
        public static readonly Font FontSubtitle = UI(9f);
        public static readonly Font FontStep = UI(9.75f);
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
    }
}
