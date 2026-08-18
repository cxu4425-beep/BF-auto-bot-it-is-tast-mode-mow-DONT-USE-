using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BloxFruitsLauncher
{
    /// <summary>找出遊戲視窗用的最小 Win32 包裝。</summary>
    internal static class NativeWindows
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        /// <summary>是否存在標題含指定關鍵字的可見視窗（不分大小寫）。</summary>
        public static bool Exists(string titleContains)
        {
            if (string.IsNullOrWhiteSpace(titleContains)) return false;

            bool found = false;
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                int len = GetWindowTextLength(hWnd);
                if (len <= 0) return true;

                var sb = new StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);

                if (sb.ToString().IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = true;
                    return false; // 找到就停
                }
                return true;
            }, IntPtr.Zero);

            return found;
        }
    }
}
