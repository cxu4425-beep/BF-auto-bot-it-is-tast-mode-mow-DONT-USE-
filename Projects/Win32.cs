using System; 
using System.Runtime.InteropServices; 
 
namespace BloxFruitsBot 
{ 
    public static class Win32 
    { 
        // 尋找視窗句柄 
        [DllImport("user32.dll", SetLastError = true)] 
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName); 
 
        // 發送消息到視窗 
        [DllImport("user32.dll", SetLastError = true)] 
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam); 
 
        // 獲取前景視窗句柄 
        [DllImport("user32.dll")] 
        public static extern IntPtr GetForegroundWindow(); 
 
        // 設置前景視窗 
        [DllImport("user32.dll")] 
        public static extern bool SetForegroundWindow(IntPtr hWnd); 
 
        // 獲取視窗矩形 
        [DllImport("user32.dll")] 
        [return: MarshalAs(UnmanagedType.Bool)] 
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect); 
 
        // 獲取客戶區矩形 
        [DllImport("user32.dll")] 
        [return: MarshalAs(UnmanagedType.Bool)] 
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect); 
 
        // 鍵盤消息常數 
        public const uint WM_KEYDOWN = 0x0100; 
        public const uint WM_KEYUP = 0x0101; 
 
        // 滑鼠消息常數 
        public const uint WM_LBUTTONDOWN = 0x0201; 
        public const uint WM_LBUTTONUP = 0x0202; 
        public const uint WM_MOUSEMOVE = 0x0200; 
 
        // 虛擬鍵碼 (Virtual Key Codes) 
        public const int VK_LBUTTON = 0x01; 
        public const int VK_RBUTTON = 0x02; 
        public const int VK_CANCEL = 0x03; 
        public const int VK_MBUTTON = 0x04; 
        public const int VK_XBUTTON1 = 0x05; 
        public const int VK_XBUTTON2 = 0x06; 
        public const int VK_BACK = 0x08; 
        public const int VK_TAB = 0x09; 
        public const int VK_CLEAR = 0x0C; 
        public const int VK_RETURN = 0x0D; 
        public const int VK_SHIFT = 0x10; 
        public const int VK_CONTROL = 0x11; 
        public const int VK_MENU = 0x12; 
        public const int VK_PAUSE = 0x13; 
        public const int VK_CAPITAL = 0x14; 
        public const int VK_KANA = 0x15; 
        public const int VK_HANGUL = 0x15; 
        public const int VK_JUNJA = 0x17; 
        public const int VK_FINAL = 0x18; 
        public const int VK_HANJA = 0x19; 
        public const int VK_KANJI = 0x19; 
        public const int VK_ESCAPE = 0x1B; 
        public const int VK_CONVERT = 0x1C; 
        public const int VK_NONCONVERT = 0x1D; 
        public const int VK_ACCEPT = 0x1E; 
        public const int VK_MODECHANGE = 0x1F; 
        public const int VK_SPACE = 0x20; 
        public const int VK_PRIOR = 0x21; 
        public const int VK_NEXT = 0x22; 
        public const int VK_END = 0x23; 
        public const int VK_HOME = 0x24; 
        public const int VK_LEFT = 0x25; 
        public const int VK_UP = 0x26; 
        public const int VK_RIGHT = 0x27; 
        public const int VK_DOWN = 0x28; 
        public const int VK_SELECT = 0x29; 
        public const int VK_PRINT = 0x2A; 
        public const int VK_EXECUTE = 0x2B; 
        public const int VK_SNAPSHOT = 0x2C; 
        public const int VK_INSERT = 0x2D; 
        public const int VK_DELETE = 0x2E; 
        public const int VK_HELP = 0x2F; 
 
        // 數字鍵 0-9 
        public const int VK_0 = 0x30; 
        public const int VK_1 = 0x31; 
        public const int VK_2 = 0x32; 
        public const int VK_3 = 0x33; 
        public const int VK_4 = 0x34; 
        public const int VK_5 = 0x35; 
        public const int VK_6 = 0x36; 
        public const int VK_7 = 0x37; 
        public const int VK_8 = 0x38; 
        public const int VK_9 = 0x39; 
 
        // 字母鍵 A-Z 
        public const int VK_A = 0x41; 
        public const int VK_B = 0x42; 
        public const int VK_C = 0x43; 
        public const int VK_D = 0x44; 
        public const int VK_E = 0x45; 
        public const int VK_F = 0x46; 
        public const int VK_G = 0x47; 
        public const int VK_H = 0x48; 
        public const int VK_I = 0x49; 
        public const int VK_J = 0x4A; 
        public const int VK_K = 0x4B; 
        public const int VK_L = 0x4C; 
        public const int VK_M = 0x4D; 
        public const int VK_N = 0x4E; 
        public const int VK_O = 0x4F; 
        public const int VK_P = 0x50; 
        public const int VK_Q = 0x51; 
        public const int VK_R = 0x52; 
        public const int VK_S = 0x53; 
        public const int VK_T = 0x54; 
        public const int VK_U = 0x55; 
        public const int VK_V = 0x56; 
        public const int VK_W = 0x57; 
        public const int VK_X = 0x58; 
        public const int VK_Y = 0x59; 
        public const int VK_Z = 0x5A; 
 
        // GDI 相關 API 
        [DllImport("gdi32.dll")] 
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc); 
 
        [DllImport("gdi32.dll")] 
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight); 
 
        [DllImport("gdi32.dll")] 
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj); 
 
        [DllImport("gdi32.dll")] 
        public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop); 
 
        [DllImport("gdi32.dll")] 
        public static extern bool DeleteDC(IntPtr hdc); 
 
        [DllImport("gdi32.dll")] 
        public static extern bool DeleteObject(IntPtr hObject); 
 
        // 視窗 DC 相關 API 
        [DllImport("user32.dll")] 
        public static extern IntPtr GetWindowDC(IntPtr hWnd); 
 
        [DllImport("user32.dll")] 
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc); 
 
        // BitBlt 的操作碼 
        public const uint SRCCOPY = 0x00CC0020; 
 
        // 結構體定義 
        [StructLayout(LayoutKind.Sequential)] 
        public struct RECT 
        { 
            public int Left; 
            public int Top; 
            public int Right; 
            public int Bottom; 
        } 
    } 
}
