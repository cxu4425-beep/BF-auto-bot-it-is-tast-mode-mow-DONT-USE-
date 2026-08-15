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

            // 用 GetDC（客戶區 DC）而不是 GetWindowDC，原點才會和 GetClientRect 對齊，
            // 否則截出來的圖會被標題列往下推、底部被裁掉。
            IntPtr hdcWindow = Win32.GetDC(hWnd);
            if (hdcWindow == IntPtr.Zero)
            {
                throw new InvalidOperationException("無法取得視窗 DC (GetDC 失敗)。");
            }

            try
            {
                using (Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics gfx = Graphics.FromImage(bmp))
                    {
                        IntPtr hdcBitmap = gfx.GetHdc();
                        try
                        {
                            if (!Win32.BitBlt(hdcBitmap, 0, 0, width, height, hdcWindow, 0, 0, Win32.SRCCOPY))
                            {
                                throw new InvalidOperationException("BitBlt 截圖失敗。");
                            }
                        }
                        finally
                        {
                            gfx.ReleaseHdc(hdcBitmap);
                        }
                    }

                    string fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
                    string fullPath = Path.GetFullPath(Path.Combine(saveDirectory, fileName));
                    bmp.Save(fullPath, ImageFormat.Png);
                    return fullPath;
                }
            }
            finally
            {
                Win32.ReleaseDC(hWnd, hdcWindow);
            }
        }
    }
}
