using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BloxFruitsLauncher
{
    /// <summary>找視窗與讀取輸入狀態用的 Win32 包裝。</summary>
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

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        /// <summary>
        /// 讀取按鍵目前是否被按住。用輪詢而不是安裝鍵盤 hook：
        /// 不需要管理員權限，也不可能因為我們的程式卡住而擋到你的輸入。
        /// </summary>
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public static bool IsDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        /// <summary>找出標題含指定關鍵字的可見視窗，回傳句柄；找不到回 IntPtr.Zero。</summary>
        public static IntPtr Find(string titleContains)
        {
            if (string.IsNullOrWhiteSpace(titleContains)) return IntPtr.Zero;

            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                int len = GetWindowTextLength(hWnd);
                if (len <= 0) return true;

                var sb = new StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);

                if (sb.ToString().IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            return found;
        }

        public static bool Exists(string titleContains) => Find(titleContains) != IntPtr.Zero;

        // 錄製會用到的虛擬鍵碼
        public const int VK_LBUTTON = 0x01;
        public const int VK_RBUTTON = 0x02;
        public const int VK_SHIFT = 0x10;
        public const int VK_SPACE = 0x20;
        public const int VK_F9 = 0x78;
        public const int VK_0 = 0x30;
        public const int VK_A = 0x41;
    }
}
