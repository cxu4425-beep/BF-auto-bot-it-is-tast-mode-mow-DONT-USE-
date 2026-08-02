using System; 
using System.Threading; 
using System.Threading.Tasks; 
using System.Windows.Forms; 
using System.Collections.Generic; 
 
namespace BloxFruitsBot 
{ 
    public class BotCore 
    { 
        private IntPtr _robloxHwnd; 
        private Action<string> _logAction; 
        private Random _random; 
        private CancellationTokenSource _cts; 
 
        public enum BotState 
        { 
            Idle, 
            GetQuest, 
            Transform, 
            Combat, 
            Navigate 
        } 
 
        public BotState CurrentState { get; private set; } 
 
        public BotCore(IntPtr robloxHwnd, Action<string> logAction) 
        { 
            _robloxHwnd = robloxHwnd; 
            _logAction = logAction; 
            _random = new Random(); 
            CurrentState = BotState.Idle; 
        } 
 
        public void Start() 
        { 
            if (_robloxHwnd == IntPtr.Zero) 
            { 
                _logAction("錯誤: Roblox 視窗句柄無效。"); 
                return; 
            } 
 
            _cts = new CancellationTokenSource(); 
            _logAction("Bot 啟動。"); 
            Task.Run(() => RunBotLoop(_cts.Token)); 
        } 
 
        public void Stop() 
        { 
            if (_cts != null) 
            { 
                _cts.Cancel(); 
                _logAction("Bot 停止。"); 
            } 
        } 
 
        private async Task RunBotLoop(CancellationToken cancellationToken) 
        { 
            while (!cancellationToken.IsCancellationRequested) 
            { 
                // 這裡將是狀態機的核心邏輯，根據 Python 的決策來切換狀態 
                // 目前先讓它保持在一個循環中，等待 Python 的回傳 
                _logAction($"當前狀態: {CurrentState}"); 
                await Task.Delay(1000, cancellationToken); // 每秒檢查一次 
            } 
        } 
 
        public void ProcessPythonDecision(PythonResponse response) 
        { 
            _logAction($"處理 Python 決策: {response.DecisionIntent}"); 
 
            switch (response.DecisionIntent) 
            { 
                case "InteractWithNPC": 
                    CurrentState = BotState.GetQuest; 
                    PerformInteractWithNPC(); 
                    break; 
                case "CombatMode": 
                    CurrentState = BotState.Combat; 
                    PerformCombatMode(); 
                    break; 
                case "Navigate": 
                    CurrentState = BotState.Navigate; 
                    PerformNavigate(); 
                    break; 
                default: 
                    _logAction($"未知決策意圖: {response.DecisionIntent}"); 
                    break; 
            } 
        } 
 
        private void PerformInteractWithNPC() 
        { 
            _logAction("執行 InteractWithNPC 動作: 2->1 變身連招"); 
            SendKeyPress(Win32.VK_2); // 開啟大佛 
            Thread.Sleep(500); 
            SendKeyPress(Win32.VK_1); // 切換龍拳 
        } 
 
        private void PerformCombatMode() 
        { 
            _logAction("執行 CombatMode 動作: 左鍵平A + Z/X 技能"); 
            SendMouseClick(); // 左鍵平A 
 
            // 隨機施放 Z 或 X 技能 
            if (_random.Next(0, 2) == 0) 
            { 
                SendKeyPress(Win32.VK_Z); 
            } 
            else 
            { 
                SendKeyPress(Win32.VK_X); 
            } 
        } 
 
        private void PerformNavigate() 
        { 
            _logAction("執行 Navigate 動作: W 前進 + Space 跳躍"); 
            SendKeyPress(Win32.VK_W); 
            if (_random.Next(0, 3) == 0) // 隨機跳躍 
            { 
                SendKeyPress(Win32.VK_SPACE); 
            } 
        } 
 
        private void SendKeyPress(int virtualKeyCode) 
        { 
            _logAction($"發送按鍵: {((Keys)virtualKeyCode).ToString()}"); 
            Win32.PostMessage(_robloxHwnd, Win32.WM_KEYDOWN, (IntPtr)virtualKeyCode, IntPtr.Zero); 
            Thread.Sleep(50); // 短暫延遲以模擬按鍵按下時間 
            Win32.PostMessage(_robloxHwnd, Win32.WM_KEYUP, (IntPtr)virtualKeyCode, IntPtr.Zero); 
        } 
 
        private void SendMouseClick() 
        { 
            _logAction("發送滑鼠左鍵點擊"); 
            // 獲取視窗客戶區中心點，作為點擊位置 
            Win32.RECT clientRect; 
            Win32.GetClientRect(_robloxHwnd, out clientRect); 
            int centerX = (clientRect.Right - clientRect.Left) / 2; 
            int centerY = (clientRect.Bottom - clientRect.Top) / 2; 
 
            IntPtr lParam = (IntPtr)((centerY << 16) | centerX); 
 
            Win32.PostMessage(_robloxHwnd, Win32.WM_LBUTTONDOWN, (IntPtr)Win32.VK_LBUTTON, lParam); 
            Thread.Sleep(50); 
            Win32.PostMessage(_robloxHwnd, Win32.WM_LBUTTONUP, (IntPtr)Win32.VK_LBUTTON, lParam); 
        } 
    } 
}
