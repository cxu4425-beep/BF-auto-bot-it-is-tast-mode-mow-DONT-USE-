using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BloxFruitsBot
{
    class Program
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static string _lastAction = "STARTUP";
        private static string _lastResult = "NONE"; // 第一輪還沒送過任何動作,不該騙AI說已經SUCCESS
        private static IntPtr _targetWindowHwnd = IntPtr.Zero;

        [STAThread]
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("====================================================");
            Console.WriteLine("    🤖 ReAct AI Game Bot - C# 遊戲控制端 (Console)");
            Console.WriteLine("====================================================");

            // 1. 尋找特定遊戲視窗 (預設為 Roblox)
            string windowTitle = "Roblox";
            _targetWindowHwnd = Win32.FindWindow(null, windowTitle);
            if (_targetWindowHwnd == IntPtr.Zero)
            {
                Console.WriteLine($"[警告] 未找到標題為 '{windowTitle}' 的視窗，將使用主螢幕進行截圖！");
            }
            else
            {
                Console.WriteLine($"[OK] 成功連結到 '{windowTitle}' 遊戲視窗，HWND: {_targetWindowHwnd}");
            }

            // 2. 啟動 ReAct 自動化閉環
            int step = 1;
            while (true)
            {
                Console.WriteLine($"\n------------------ [步驟 {step++}] ------------------");
                try
                {
                    // A. 【Perception】截取遊戲視窗並轉成 Base64
                    Bitmap screenshot = CaptureTargetScreen();
                    string base64Image = ConvertBitmapToBase64(screenshot);
                    screenshot.Dispose(); // 釋放記憶體以免洩漏

                    // B. 【Http Send】將畫面與上一次狀態打包成 JSON 發送給 FastAPI
                    Console.WriteLine("[System] 正在傳送圖片與狀態反饋至 AI 大腦...");
                    var requestPayload = new DecisionRequest
                    {
                        Image = base64Image,
                        LastAction = _lastAction,
                        LastResult = _lastResult
                    };

                    string jsonPayload = JsonSerializer.Serialize(requestPayload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    // C. 【Http Receive】等待並解析 AI 決策結果
                    var response = await _httpClient.PostAsync("http://localhost:8000/decide", content);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[錯誤] Python FastAPI 伺服器回報錯誤: {response.StatusCode}");
                        _lastResult = "HTTP_ERROR";
                        await Task.Delay(3000);
                        continue;
                    }

                    string responseJson = await response.Content.ReadAsStringAsync();
                    var decision = JsonSerializer.Deserialize<DecisionResponse>(responseJson);

                    if (decision != null)
                    {
                        // 輸出 AI 的思考過程 (Thought)
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"[AI 思考過程 (Thought)]:\n{decision.Thought}");
                        Console.ResetColor();

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[AI 決定動作 (Action)] -> {decision.Action}");
                        Console.ResetColor();

                        // D. 【Action】執行鍵盤/滑鼠驅動操作，並回報結果
                        _lastAction = decision.Action;
                        _lastResult = ExecuteGameAction(decision.Action);
                    }
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[異常錯誤] 執行循環發生錯誤: {ex.Message}");
                    Console.ResetColor();
                    _lastResult = $"EXCEPTION: {ex.Message}";
                }

                // 物理動作延遲，避免過度頻繁戳 API 與發送過多操作
                await Task.Delay(2000);
            }
        }

        /// <summary>
        /// 螢幕截圖：若有找到遊戲視窗則截取視窗客戶區（完全在記憶體中，不寫入硬碟），否則截取全螢幕
        /// </summary>
        private static Bitmap CaptureTargetScreen()
        {
            if (_targetWindowHwnd != IntPtr.Zero)
            {
                try
                {
                    Win32.RECT rect;
                    Win32.GetClientRect(_targetWindowHwnd, out rect);
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;

                    if (width > 0 && height > 0)
                    {
                        Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                        using (Graphics gfx = Graphics.FromImage(bmp))
                        {
                            IntPtr hdcBitmap = gfx.GetHdc();
                            // 用 GetDC（客戶區 DC）配 GetClientRect，原點才對得上。
                            // GetWindowDC 的原點含標題列，會讓畫面整個往下偏、底部被裁掉。
                            IntPtr hdcWindow = Win32.GetDC(_targetWindowHwnd);
                            if (hdcWindow != IntPtr.Zero)
                            {
                                try
                                {
                                    Win32.BitBlt(hdcBitmap, 0, 0, width, height, hdcWindow, 0, 0, Win32.SRCCOPY);
                                }
                                finally
                                {
                                    gfx.ReleaseHdc(hdcBitmap);
                                    Win32.ReleaseDC(_targetWindowHwnd, hdcWindow);
                                }
                                return bmp;
                            }
                            else
                            {
                                gfx.ReleaseHdc(hdcBitmap);
                                bmp.Dispose();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[警告] 視窗記憶體截圖失敗，改用全螢幕截圖: {ex.Message}");
                }
            }

            // Fallback: 截取全螢幕
            // Screen.PrimaryScreen 在沒有顯示器的工作階段會是 null，直接取 .Bounds 會炸 NullReference
            Screen? primaryScreen = Screen.PrimaryScreen;
            if (primaryScreen == null)
            {
                throw new InvalidOperationException("找不到主螢幕，無法截圖（是否在無顯示器的環境執行？）。");
            }
            Rectangle bounds = primaryScreen.Bounds;
            Bitmap fullScreenBitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(fullScreenBitmap))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            }
            return fullScreenBitmap;
        }

        /// <summary>
        /// 轉碼 Helper: 將 Bitmap 轉成 Base64 字串
        /// </summary>
        private static string ConvertBitmapToBase64(Bitmap bitmap)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                // 使用 JPEG 格式壓縮可以有效減少 Base64 封包大小與加速網路傳輸
                bitmap.Save(ms, ImageFormat.Jpeg);
                byte[] byteImage = ms.ToArray();
                return Convert.ToBase64String(byteImage);
            }
        }

        /// <summary>
        /// 執行遊戲物理動作 (利用 PostMessage 發送鍵盤滑鼠指令)
        /// </summary>
        private static string ExecuteGameAction(string action)
        {
            if (_targetWindowHwnd == IntPtr.Zero)
            {
                return "FAILED_NO_TARGET_WINDOW";
            }

            try
            {
                string actionUpper = action.Trim().ToUpper();
                switch (actionUpper)
                {
                    case "MOVE_FORWARD":
                        SendKeyPress(Win32.VK_W);
                        break;
                    case "TURN_LEFT":
                        SendKeyPress(Win32.VK_A);
                        break;
                    case "TURN_RIGHT":
                        SendKeyPress(Win32.VK_D);
                        break;
                    case "JUMP":
                        SendKeyPress(Win32.VK_SPACE);
                        break;
                    case "ATTACK":
                        SendMouseClick();
                        break;
                    case "TALK_TO_NPC":
                        SendKeyPress(Win32.VK_E); // 假設 E 鍵為與 NPC 互動/對話
                        break;
                    case "SWITCH_CHANNEL":
                        SendKeyPress(Win32.VK_TAB); // 模擬切換頻道的操作
                        break;
                    case "RECONNECT":
                        SendKeyPress(Win32.VK_ESCAPE); // 模擬連線超時自我修復
                        break;
                    case "IDLE":
                    default:
                        Console.WriteLine("[Action] 保持原地靜止 (IDLE)");
                        break;
                }
                return actionUpper; // 誠實回報剛剛送出的動作,而不是騙AI說永遠SUCCESS,避免文字提示汙染畫面判讀
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Action] 執行按鍵失敗: {ex.Message}");
                return $"FAILED_{ex.Message}";
            }
        }

        private static void SendKeyPress(int vk)
        {
            Console.WriteLine($"[Action] 前景發送按鍵: {((Keys)vk).ToString()}");

            // 1. 先把遊戲視窗拉到最前面並取得焦點
            //    (PostMessage 送背景訊息 Roblox 完全不吃，必須真的是前景視窗
            //     SendInput 才會被遊戲引擎的硬體輸入判斷讀到)
            Win32.SetForegroundWindow(_targetWindowHwnd);
            Thread.Sleep(80); // 給系統一點時間真正切換焦點

            ushort scanCode = (ushort)Win32.MapVirtualKey((uint)vk, Win32.MAPVK_VK_TO_VSC);

            var inputs = new Win32.INPUT[2];
            inputs[0] = new Win32.INPUT
            {
                type = Win32.INPUT_KEYBOARD,
                U = new Win32.InputUnion
                {
                    ki = new Win32.KEYBDINPUT { wVk = (ushort)vk, wScan = scanCode, dwFlags = 0, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            };
            inputs[1] = new Win32.INPUT
            {
                type = Win32.INPUT_KEYBOARD,
                U = new Win32.InputUnion
                {
                    ki = new Win32.KEYBDINPUT { wVk = (ushort)vk, wScan = scanCode, dwFlags = Win32.KEYEVENTF_KEYUP, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            };

            // 按下
            Win32.SendInput(1, new[] { inputs[0] }, Marshal.SizeOf(typeof(Win32.INPUT)));
            Thread.Sleep(120); // 模擬按鍵按壓物理時間 (稍微拉長，讓Roblox角色動作有時間觸發)
            // 放開
            Win32.SendInput(1, new[] { inputs[1] }, Marshal.SizeOf(typeof(Win32.INPUT)));
        }

        private static void SendMouseClick()
        {
            Console.WriteLine("[Action] 前景發送滑鼠左鍵點擊");

            Win32.SetForegroundWindow(_targetWindowHwnd);
            Thread.Sleep(80);

            Win32.RECT rect;
            Win32.GetClientRect(_targetWindowHwnd, out rect);
            var centerPoint = new Win32.POINT { X = (rect.Right - rect.Left) / 2, Y = (rect.Bottom - rect.Top) / 2 };
            Win32.ClientToScreen(_targetWindowHwnd, ref centerPoint);

            // 先把滑鼠游標實際移到視窗中心 (SendInput的點擊是對「目前游標位置」生效，不是座標)
            Win32.SetCursorPos(centerPoint.X, centerPoint.Y);
            Thread.Sleep(30);

            var down = new Win32.INPUT
            {
                type = Win32.INPUT_MOUSE,
                U = new Win32.InputUnion { mi = new Win32.MOUSEINPUT { dwFlags = Win32.MOUSEEVENTF_LEFTDOWN } }
            };
            var up = new Win32.INPUT
            {
                type = Win32.INPUT_MOUSE,
                U = new Win32.InputUnion { mi = new Win32.MOUSEINPUT { dwFlags = Win32.MOUSEEVENTF_LEFTUP } }
            };

            Win32.SendInput(1, new[] { down }, Marshal.SizeOf(typeof(Win32.INPUT)));
            Thread.Sleep(50);
            Win32.SendInput(1, new[] { up }, Marshal.SizeOf(typeof(Win32.INPUT)));
        }
    }

    public class DecisionRequest
    {
        [JsonPropertyName("image_base64")]
        public string Image { get; set; } = string.Empty;

        [JsonPropertyName("last_action")]
        public string LastAction { get; set; } = string.Empty;

        [JsonPropertyName("last_result")]
        public string LastResult { get; set; } = string.Empty;
    }

    public class DecisionResponse
    {
        [JsonPropertyName("thought")]
        public string Thought { get; set; } = string.Empty;

        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;
    }
}
