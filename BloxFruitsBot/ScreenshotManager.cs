using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace BloxFruitsBot
{
    /// <summary>
    /// 負責截取指定視窗的客戶區畫面並存成 PNG。
    /// MainForm.cs 的截圖迴圈會呼叫 CaptureWindow()，回傳的路徑再透過 Named Pipe 傳給 Python。
    /// </summary>
    public static class ScreenshotManager
    {
        /// <summary>
        /// 截取視窗客戶區並存檔，回傳存檔的完整路徑。
        /// </summary>
        /// <param name="hWnd">目標視窗句柄</param>
        /// <param name="saveDirectory">存放截圖的資料夾（不存在會自動建立）</param>
        public static string CaptureWindow(IntPtr hWnd, string saveDirectory)
        {
            if (hWnd == IntPtr.Zero)
            {
                throw new ArgumentException("視窗句柄無效。", nameof(hWnd));
            }

            if (!Win32.GetClientRect(hWnd, out Win32.RECT clientRect))
            {
                throw new InvalidOperationException("無法取得視窗客戶區大小 (GetClientRect 失敗)。");
            }

            int width = clientRect.Right - clientRect.Left;
            int height = clientRect.Bottom - clientRect.Top;

            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException($"視窗客戶區大小不合法 ({width}x{height})，視窗可能已最小化。");
            }

            if (!Directory.Exists(saveDirectory))
            {
                Directory.CreateDirectory(saveDirectory);
            }

            // 把客戶區左上角換算成螢幕座標，再從螢幕複製像素。
            //
            // 不用 BitBlt 從視窗 DC 抓：Roblox 之類的 DirectX 遊戲是硬體渲染，
            // GDI 讀不到它的畫面內容，抓回來會是黑畫面或雜訊。
            // CopyFromScreen 讀的是桌面合成器（DWM）合成後的結果，含 DirectX 內容。
            //
            // 代價是遊戲必須在前景且沒被遮住，呼叫端要自行確保這點。
            var origin = new Win32.POINT { X = 0, Y = 0 };
            Win32.ClientToScreen(hWnd, ref origin);

            using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (Graphics gfx = Graphics.FromImage(bmp))
                {
                    gfx.CopyFromScreen(origin.X, origin.Y, 0, 0,
                                       new Size(width, height), CopyPixelOperation.SourceCopy);
                }

                string fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
                string fullPath = Path.GetFullPath(Path.Combine(saveDirectory, fileName));
                bmp.Save(fullPath, ImageFormat.Png);
                return fullPath;
            }
        }
    }
}
