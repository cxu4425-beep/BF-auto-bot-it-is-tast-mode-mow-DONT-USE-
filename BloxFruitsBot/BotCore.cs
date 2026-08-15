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
        private CancellationTokenSource? _cts;
 
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
            // 先取出 token 再交給 lambda：直接在 lambda 內用 _cts.Token 會捕捉可為 null 的欄位，
            // 編譯器無法做 null 流程分析（CS8602），而且 Stop() 之後欄位被換掉也會取到錯的 token。
            CancellationToken token = _cts.Token;
            _logAction("Bot 啟動。");
            Task.Run(() => RunBotLoop(token));
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
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // 這裡將是狀態機的核心邏輯，根據 Python 的決策來切換狀態
                    // 目前先讓它保持在一個循環中，等待 Python 的回傳
                    _logAction($"當前狀態: {CurrentState}");
                    await Task.Delay(1000, cancellationToken); // 每秒檢查一次
                }
            }
            catch (OperationCanceledException)
            {
                // Stop() 取消時 Task.Delay 一定會丟這個例外。
                // 這個迴圈是 fire-and-forget，不接住的話會變成沒人觀察的 Task 例外。
            }
        }
 
        public void ProcessPythonDecision(PythonResponse response) 
        { 
            _logAction($"處理 Python 決策: {response.DecisionIntent}"); 
            
            // 根據決策更新狀態。
            // 這裡必須認得「所有」意圖別名：舊版少了 CombatMode / InteractWithNPC，
            // 一旦 AI 有給 ActionKeys 就走不到下面那個 switch，狀態會被誤設成 Idle。
            switch (response.DecisionIntent)
            {
                case "Combat":
                case "CombatMode":
                    CurrentState = BotState.Combat;
                    break;
                case "Flee":
                case "Navigate":
                case "Explore":
                case "Farm":
                    CurrentState = BotState.Navigate;
                    break;
                case "Interact":
                case "InteractWithNPC":
                case "Collect":
                    CurrentState = BotState.GetQuest;
                    break;
                default:
                    CurrentState = BotState.Idle;
                    break;
            }

            // 優先執行 AI 自訂按鍵
            if (response.ActionKeys != null && response.ActionKeys.Count > 0)
            {
                _logAction($"執行 AI 自訂按鍵序列: {string.Join(", ", response.ActionKeys)}");
                Task.Run(() => ExecuteAIKeys(response));
            }
            else
            {
                // 回退到原來的固定操作
                switch (response.DecisionIntent) 
                { 
                    case "InteractWithNPC": 
                    case "Interact":
                        CurrentState = BotState.GetQuest; 
                        PerformInteractWithNPC(); 
                        break; 
                    case "CombatMode": 
                    case "Combat":
                        CurrentState = BotState.Combat; 
                        PerformCombatMode(); 
                        break; 
                    case "Navigate": 
                        CurrentState = BotState.Navigate; 
                        PerformNavigate(); 
                        break; 
                    default: 
                        _logAction($"未知決策意圖且無按鍵序列: {response.DecisionIntent}"); 
                        break; 
                } 
            }
        } 

        private async Task ExecuteAIKeys(PythonResponse response)
        {
            // 處理長按鍵
            if (response.HoldKeys != null && response.HoldKeys.Count > 0)
            {
                foreach (var k in response.HoldKeys)
                {
                    int vk = GetVirtualKeyCode(k);
                    if (vk != 0)
                    {
                        _logAction($"AI 長按鍵按下: {k}");
                        Win32.PostMessage(_robloxHwnd, Win32.WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
                    }
                }
            }

            // 順序執行單擊按鍵
            foreach (var key in response.ActionKeys)
            {
                if (key.Equals("LeftClick", StringComparison.OrdinalIgnoreCase))
                {
                    SendMouseClick();
                }
                else
                {
                    int vk = GetVirtualKeyCode(key);
                    if (vk != 0)
                    {
                        SendKeyPress(vk);
                    }
                }
                await Task.Delay(100);
            }

            // 如果有長按鍵，等持續時間過後放開
            if (response.HoldKeys != null && response.HoldKeys.Count > 0)
            {
                int delayMs = (int)(response.ActionDuration * 1000);
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }
                foreach (var k in response.HoldKeys)
                {
                    int vk = GetVirtualKeyCode(k);
                    if (vk != 0)
                    {
                        _logAction($"AI 長按鍵釋放: {k}");
                        Win32.PostMessage(_robloxHwnd, Win32.WM_KEYUP, (IntPtr)vk, IntPtr.Zero);
                    }
                }
            }
        }

        private int GetVirtualKeyCode(string keyName)
        {
            string cleanKey = keyName.Trim().ToUpper();
            if (cleanKey.Length == 1)
            {
                char c = cleanKey[0];
                if (c >= 'A' && c <= 'Z') return Win32.VK_A + (c - 'A');
                if (c >= '0' && c <= '9') return Win32.VK_0 + (c - '0');
            }

            switch (cleanKey)
            {
                case "SPACE": return Win32.VK_SPACE;
                case "SHIFT": return Win32.VK_SHIFT;
                case "CTRL": return Win32.VK_CONTROL;
                case "TAB": return Win32.VK_TAB;
                case "ESC": return Win32.VK_ESCAPE;
                default:
                    return 0;
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
