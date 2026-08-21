using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
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

        // ── 可調參數（用環境變數覆寫，不必改程式碼重編）─────────────────
        //
        // 送給 AI 前的截圖寬度上限，設 0 表示不縮圖。
        //
        // 400 是實測出來的值，不是猜的。用同一張遊戲截圖對 qwen2.5vl:3b 測：
        //     800px → 模型退化成一整串 "@"（放大 context 到 16384 也一樣）
        //     400px → 正確描述出「角色坐在沙發上，周圍是家具」
        //     200px → 開始幻覺，把畫面說成 Minecraft
        // 所以可用區間有上下界，400 落在中間。
        // 太大會讓模型崩掉、太小會讓它看不清楚，兩邊都不是線性劣化。
        private static readonly int MaxImageWidth = ReadEnvInt("BOT_MAX_IMAGE_WIDTH", 400);

        // 每輪之間的間隔（毫秒）。這是「加」在推論時間上的，不是週期目標：
        // 一輪總時間 = 推論 + 這個值。
        private static readonly int LoopDelayMs = ReadEnvInt("BOT_LOOP_DELAY_MS", 2000);

        // 一次送給模型的連續影格數（含當前這張）。設 1 就是舊的單張行為。
        //
        // 送多張的用意是給模型「時間軸」。單張畫面看不出上一個動作有沒有生效，
        // 提示詞卻要求它自行判斷成敗 —— 那是辦不到的。
        // 保留前幾輪的畫面不必額外截圖，而且前一張正好是上個動作執行前的樣子。
        //
        // 實測 qwen2.5vl:3b 吃 3 張 400px 影格不會退化，且能正確答出
        // 「The player is moving forward」。代價是每多一張就多一份讀圖時間。
        private static readonly int FrameCount = Math.Max(1, ReadEnvInt("BOT_FRAME_COUNT", 2));

        // 觀察模式：照常截圖、問 AI、印出決策，但「不送任何按鍵滑鼠」。
        // 因為不需要送輸入，也就不必把遊戲搶到前景，你可以一邊看 log 一邊自己玩，
        // 讓 AI 在旁邊講它會怎麼做 —— 這是評估決策品質最省事的方式。
        private static readonly bool DryRun = ReadEnvBool("BOT_DRY_RUN", false);

        // 是否在截圖與送按鍵前把遊戲視窗拉到前景。
        //
        // 截圖和送輸入對焦點的需求其實不同：
        //   截圖用 CopyFromScreen，只要視窗「沒被蓋住」就抓得到，不必是前景。
        //   送輸入用 SendInput，走硬體輸入佇列，只會進到前景視窗，非前景不可。
        // 所以關掉這個開關時，只要你把 Roblox 開成視窗模式且不被遮住，
        // 截圖照樣正確，代價是按鍵可能送不進遊戲（除非它剛好是前景）。
        private static readonly bool FocusWindow = ReadEnvBool("BOT_FOCUS_WINDOW", true);

        // 觀察模式下絕對不該搶焦點。DryRun 不送任何輸入，也就沒有「必須是前景」
        // 的理由，而搶焦點正好毀掉觀察模式唯一的用途 —— 你要能一邊自己玩、
        // 一邊在旁邊看它怎麼判斷。
        //
        // 啟動訊息本來就印著「也不搶視窗焦點」，但 EnsureTargetForeground() 只檢查
        // FocusWindow，而它預設是 true。也就是說只設 BOT_DRY_RUN=1 的人，
        // 看到的訊息和實際行為是相反的。
        private static bool ShouldFocusWindow => FocusWindow && !DryRun;

        // 決策紀錄（JSONL，一行一步）。這是評估決策品質的原始資料。
        // 終端機捲過去的字沒辦法拿來算「IDLE 佔幾成」「延遲 p95 是多少」，
        // 必須落成檔案才統計得出來。設 BOT_DECISION_LOG=0 關閉。
        private static readonly string? DecisionLogPath = ResolveOptionalPath(
            "BOT_DECISION_LOG", "decisions.jsonl");

        // 每一步實際送給模型的那張畫面，檔名對得上紀錄裡的 step。
        // 統計說「IDLE 佔七成」的時候，你會想知道那七成當下畫面長什麼樣。
        // 一張約 40KB，10 分鐘的觀察大概 12MB。設 BOT_OBSERVE_FRAMES=0 關閉。
        private static readonly string? ObserveFramesDir = ResolveOptionalPath(
            "BOT_OBSERVE_FRAMES", "observe_frames");

        // 標記這次跑的是哪個設定（baseline / fewshot / ...）。做 A/B 對照時，
        // 少了這個標籤，事後就分不出兩份紀錄各是哪一邊。
        private static readonly string RunTag =
            Environment.GetEnvironmentVariable("BOT_RUN_TAG") ?? "";

        // ConvertBitmapToBase64() 編出來的 JPEG 位元組，讓迴圈能原封不動存檔。
        // 重點在「原封不動」：存的必須是送出去的那一份。重新編一次可能不一樣，
        // 那樣存下來的就不是模型看到的東西，對照也就失去意義。
        private static byte[]? _lastFrameJpeg;

        // 由舊到新的影格緩衝
        private static readonly Queue<string> _frameHistory = new Queue<string>();

        private static readonly string ApiUrl =
            Environment.GetEnvironmentVariable("BOT_API_URL") ?? "http://localhost:8000/decide";

        // 把送給 AI 的那張圖存檔（每輪覆寫），方便直接確認「AI 到底看到什麼」。
        // 截圖抓錯視窗、抓到黑畫面或雜訊時，開這個檔案一眼就知道，不必從模型的
        // 回答反推。設 BOT_SAVE_FRAME=0 可關閉。
        private static readonly string? SaveFramePath =
            Environment.GetEnvironmentVariable("BOT_SAVE_FRAME") == "0"
                ? null
                : Path.GetFullPath("last_frame.jpg");

        private static int ReadEnvInt(string name, int fallback)
        {
            string? raw = Environment.GetEnvironmentVariable(name);
            return int.TryParse(raw, out int value) ? value : fallback;
        }

        private static bool ReadEnvBool(string name, bool fallback)
        {
            string? raw = Environment.GetEnvironmentVariable(name)?.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                return fallback;
            }
            return raw == "1"
                || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
                || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 讀一個「路徑或關閉」的環境變數：沒設就用預設值，設成 0 / false 則回 null。
        /// </summary>
        private static string? ResolveOptionalPath(string name, string fallback)
        {
            string? raw = Environment.GetEnvironmentVariable(name)?.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                return Path.GetFullPath(fallback);
            }
            if (raw == "0" || raw.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return Path.GetFullPath(raw);
        }

        /// <summary>
        /// 以「標題包含關鍵字」尋找遊戲視窗。
        /// FindWindow 只認完全相符的標題，但遊戲視窗標題常常帶額外後綴，
        /// 一找不到就會悄悄退回全螢幕截圖，AI 就只能看到桌面。
        /// </summary>
        private static IntPtr FindGameWindow(string titleContains)
        {
            // 先試完全相符，命中就不用列舉了
            IntPtr exact = Win32.FindWindow(null, titleContains);
            if (exact != IntPtr.Zero)
            {
                return exact;
            }

            IntPtr found = IntPtr.Zero;
            Win32.EnumWindows((hWnd, _) =>
            {
                if (!Win32.IsWindowVisible(hWnd))
                {
                    return true;
                }

                string title = GetWindowTitle(hWnd);
                if (title.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = hWnd;
                    return false; // 找到就停止列舉
                }
                return true;
            }, IntPtr.Zero);

            return found;
        }

        private static string GetWindowTitle(IntPtr hWnd)
        {
            int length = Win32.GetWindowTextLength(hWnd);
            if (length <= 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(length + 1);
            Win32.GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static List<string> ListWindowTitles()
        {
            var titles = new List<string>();
            Win32.EnumWindows((hWnd, _) =>
            {
                if (Win32.IsWindowVisible(hWnd))
                {
                    string title = GetWindowTitle(hWnd);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        titles.Add(title);
                    }
                }
                return true;
            }, IntPtr.Zero);
            return titles;
        }

        /// <summary>
        /// 確保遊戲視窗在前景。
        ///
        /// 這對「截圖」和「送按鍵」都是必要的：
        ///  - 截圖是從螢幕上該區域「當下顯示的像素」複製的，不是視窗自己的畫面緩衝。
        ///    遊戲被其他視窗蓋住時，截到的就是蓋在上面那個視窗。
        ///  - SendInput 走的是硬體輸入佇列，只會送到前景視窗。
        ///
        /// 已經在前景時直接返回，不浪費那 120ms。
        /// BOT_FOCUS_WINDOW=0 可完全關掉搶焦點的行為（見該欄位的說明）。
        /// </summary>
        private static void EnsureTargetForeground()
        {
            if (_targetWindowHwnd == IntPtr.Zero || !ShouldFocusWindow)
            {
                return;
            }

            if (Win32.GetForegroundWindow() == _targetWindowHwnd)
            {
                return;
            }

            if (Win32.IsIconic(_targetWindowHwnd))
            {
                Win32.ShowWindow(_targetWindowHwnd, Win32.SW_RESTORE);
            }

            Win32.SetForegroundWindow(_targetWindowHwnd);
            Thread.Sleep(120); // 給系統時間真正完成焦點切換
        }

        [STAThread]
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("====================================================");
            Console.WriteLine("    🤖 ReAct AI Game Bot - C# 遊戲控制端 (Console)");
            Console.WriteLine("====================================================");

            // 1. 尋找特定遊戲視窗 (預設為 Roblox，可用 BOT_WINDOW_TITLE 覆寫)
            string windowTitle = Environment.GetEnvironmentVariable("BOT_WINDOW_TITLE") ?? "Roblox";
            _targetWindowHwnd = FindGameWindow(windowTitle);
            if (_targetWindowHwnd == IntPtr.Zero)
            {
                Console.WriteLine($"[警告] 未找到標題含 '{windowTitle}' 的視窗，將使用主螢幕進行截圖！");
                Console.WriteLine("[警告] 這代表 AI 看到的是你的桌面而不是遊戲畫面，決策不會有意義。");
                Console.WriteLine("[提示] 目前可見的視窗標題：");
                foreach (string t in ListWindowTitles())
                {
                    Console.WriteLine($"         - {t}");
                }
                Console.WriteLine("[提示] 用環境變數指定正確標題，例如： $env:BOT_WINDOW_TITLE=\"Roblox\"");
            }
            else
            {
                Console.WriteLine($"[OK] 成功連結到 '{windowTitle}' 遊戲視窗，HWND: {_targetWindowHwnd}");
            }

            if (SaveFramePath != null)
            {
                Console.WriteLine($"[OK] 每輪送給 AI 的畫面會存到: {SaveFramePath}");
                Console.WriteLine("[提示] AI 判讀怪怪的時候，打開這個檔案就知道它實際看到什麼。");
            }

            Console.WriteLine(FrameCount > 1
                ? $"[OK] 每輪送出最近 {FrameCount} 張連續影格，讓 AI 能看出上個動作有沒有生效（BOT_FRAME_COUNT）"
                : "[OK] 每輪只送 1 張畫面（設 BOT_FRAME_COUNT=2 以上可讓 AI 看出畫面變化）");

            if (DryRun)
            {
                Console.WriteLine("[OK] 觀察模式 (BOT_DRY_RUN=1)：只印出決策，不送出任何按鍵滑鼠，也不搶視窗焦點。");
                Console.WriteLine("[提示] 你可以自己玩，讓 AI 在旁邊講它會怎麼做，藉此評估決策品質。");
            }
            else if (!FocusWindow)
            {
                Console.WriteLine("[OK] 已關閉搶焦點 (BOT_FOCUS_WINDOW=0)。");
                Console.WriteLine("[警告] 遊戲不是前景時 SendInput 送不進去，按鍵多半無效。");
                Console.WriteLine("[警告] 且遊戲視窗一旦被遮住，截到的就是蓋在上面的視窗。");
            }

            if (DecisionLogPath != null)
            {
                Console.WriteLine($"[OK] 決策紀錄寫入: {DecisionLogPath}");
                Console.WriteLine("[提示] 跑完用 python analyze_decisions.py <這個檔案> 產生統計報告。");
            }
            if (ObserveFramesDir != null)
            {
                Console.WriteLine($"[OK] 每步畫面存到: {ObserveFramesDir}（檔名對應紀錄裡的 step）");
            }
            if (RunTag.Length > 0)
            {
                Console.WriteLine($"[OK] 本次執行標籤: {RunTag}（BOT_RUN_TAG，用來區分 A/B 兩組紀錄）");
            }

            Console.WriteLine("[OK] 目前的動作對應（用 BOT_ACTION_<動作名> 可覆寫）：");
            foreach (var pair in ActionMap)
            {
                Console.WriteLine($"         {pair.Key,-16} -> {pair.Value}");
            }

            // 2. 啟動 ReAct 自動化閉環
            int step = 1;
            while (true)
            {
                int currentStep = step++;
                Console.WriteLine($"\n------------------ [步驟 {currentStep}] ------------------");
                try
                {
                    // A. 【Perception】截取遊戲視窗並轉成 Base64
                    Bitmap screenshot = CaptureTargetScreen();
                    string base64Image = ConvertBitmapToBase64(screenshot);
                    screenshot.Dispose(); // 釋放記憶體以免洩漏

                    // 推進影格緩衝：最舊的擠掉，最新的排在最後
                    _frameHistory.Enqueue(base64Image);
                    while (_frameHistory.Count > FrameCount)
                    {
                        _frameHistory.Dequeue();
                    }

                    // B. 【Http Send】將畫面與上一次狀態打包成 JSON 發送給 FastAPI
                    var frames = new List<string>(_frameHistory);
                    Console.WriteLine($"[System] 正在傳送 {frames.Count} 張影格與狀態反饋至 AI 大腦...");
                    var requestPayload = new DecisionRequest
                    {
                        Images = frames,
                        LastAction = _lastAction,
                        LastResult = _lastResult
                    };

                    string jsonPayload = JsonSerializer.Serialize(requestPayload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    // C. 【Http Receive】等待並解析 AI 決策結果
                    Stopwatch sw = Stopwatch.StartNew();
                    var response = await _httpClient.PostAsync(ApiUrl, content);
                    sw.Stop();
                    Console.WriteLine($"[System] AI 回應耗時: {sw.ElapsedMilliseconds} ms");
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

                        if (decision.Degenerate)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("[警告] 這一步模型輸出退化，不是有效決策（統計時會單獨列出）。");
                            Console.ResetColor();
                        }

                        // D. 【Action】執行鍵盤/滑鼠驅動操作，並回報結果
                        _lastAction = decision.Action;
                        _lastResult = ExecuteGameAction(decision.Action);

                        // E. 【Record】留下可統計的紀錄。放在動作之後才記得到執行結果。
                        RecordDecision(currentStep, sw.ElapsedMilliseconds, frames.Count,
                                       decision, _lastResult);
                    }
                }
                catch (HttpRequestException ex)
                {
                    // AI 伺服器沒開或掛掉。原本會照一般迴圈間隔（預設 200ms）
                    // 一直重試，等於在洗畫面。這種錯誤退遠一點再試。
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[連線錯誤] 連不上 AI 伺服器 ({ApiUrl}): {ex.Message}");
                    Console.WriteLine("[提示] 檢查 AI Server 那個視窗是否還開著（python main.py）。");
                    Console.ResetColor();
                    _lastResult = "API_UNREACHABLE";
                    await Task.Delay(3000);
                    continue;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[異常錯誤] 執行循環發生錯誤: {ex.Message}");
                    Console.ResetColor();
                    _lastResult = $"EXCEPTION: {ex.Message}";
                }

                // 物理動作延遲，避免過度頻繁戳 API 與發送過多操作
                await Task.Delay(LoopDelayMs);
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
                    // 截圖前一定要先把遊戲拉到前景，否則抓到的是蓋在上面的視窗。
                    // 少了這一步會形成死結：AI 看到終端機 -> 判斷 IDLE ->
                    // IDLE 不切換前景 -> 下一張還是終端機 -> 永遠 IDLE。
                    EnsureTargetForeground();

                    Win32.RECT rect;
                    Win32.GetClientRect(_targetWindowHwnd, out rect);
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;

                    if (width > 0 && height > 0)
                    {
                        // 把客戶區左上角換算成螢幕座標，再從「螢幕」複製像素。
                        //
                        // 不能用 BitBlt 從視窗 DC 抓：Roblox 是 DirectX 硬體渲染，
                        // GDI 看不到它的畫面內容，抓回來是黑畫面或雜訊。
                        // 實測時 AI 收到的圖片從 41KB（終端機文字）暴增到 115KB，
                        // 正是雜訊壓不掉的特徵，模型也因此退化成一直吐 "@@@@"。
                        //
                        // CopyFromScreen 讀的是桌面合成器（DWM）合成後的畫面，
                        // DirectX 內容已經在裡面了。前面 EnsureTargetForeground()
                        // 已保證遊戲在最上層，所以不會抓到別的視窗。
                        var origin = new Win32.POINT { X = 0, Y = 0 };
                        Win32.ClientToScreen(_targetWindowHwnd, ref origin);

                        Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                        using (Graphics gfx = Graphics.FromImage(bmp))
                        {
                            gfx.CopyFromScreen(origin.X, origin.Y, 0, 0,
                                               new Size(width, height), CopyPixelOperation.SourceCopy);
                        }
                        return bmp;
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
        /// 轉碼 Helper: 先等比例縮圖，再轉成 Base64 字串。
        ///
        /// 縮圖是整條迴圈最大的效能槓桿。視覺模型的「看圖」成本和像素量成正比：
        /// 1920x1080 在 Qwen2.5-VL 約 2,600 個 vision token，光讀圖就要 ~10 秒；
        /// 縮到 800 寬只剩約 490 個 token，降到 ~2 秒。
        /// 遊戲畫面判讀（有沒有怪、有沒有 NPC、是否卡牆）不需要原始解析度。
        /// </summary>
        private static string ConvertBitmapToBase64(Bitmap bitmap)
        {
            Bitmap toEncode = bitmap;
            bool needsDispose = false;

            if (MaxImageWidth > 0 && bitmap.Width > MaxImageWidth)
            {
                int targetHeight = (int)Math.Round(bitmap.Height * (MaxImageWidth / (double)bitmap.Width));
                if (targetHeight < 1)
                {
                    targetHeight = 1;
                }

                toEncode = new Bitmap(MaxImageWidth, targetHeight, PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(toEncode))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(bitmap, 0, 0, MaxImageWidth, targetHeight);
                }
                needsDispose = true;
                Console.WriteLine($"[System] 截圖縮放: {bitmap.Width}x{bitmap.Height} -> {MaxImageWidth}x{targetHeight}");
            }

            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    // 使用 JPEG 格式壓縮可以有效減少 Base64 封包大小與加速網路傳輸
                    toEncode.Save(ms, ImageFormat.Jpeg);
                    byte[] byteImage = ms.ToArray();
                    _lastFrameJpeg = byteImage;

                    if (SaveFramePath != null)
                    {
                        try
                        {
                            File.WriteAllBytes(SaveFramePath, byteImage);
                        }
                        catch (Exception ex)
                        {
                            // 純診斷用途，寫檔失敗不該影響主流程
                            Console.WriteLine($"[警告] 無法寫入診斷截圖: {ex.Message}");
                        }
                    }

                    return Convert.ToBase64String(byteImage);
                }
            }
            finally
            {
                if (needsDispose)
                {
                    toEncode.Dispose();
                }
            }
        }

        // 寫 JSONL 用的設定。UnsafeRelaxedJsonEscaping 是為了讓中文的 thought 直接
        // 可讀，而不是一整排 \uXXXX —— 這是本機診斷檔，不會被塞進 HTML。
        // 一定要用不帶 BOM 的 UTF-8。Encoding.UTF8 這個靜態屬性是帶 BOM 的，
        // AppendAllText 在檔案剛建立時會把那三個位元組寫進去，於是第一行開頭
        // 多出 \ufeff，逐行 json.loads 就會在第一行掛掉。
        private static readonly Encoding LogEncoding = new UTF8Encoding(false);

        private static readonly JsonSerializerOptions LogJsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false, // 一步一行，才能用 JSONL 逐行讀
        };

        /// <summary>
        /// 把這一步的決策追加到紀錄檔，並存下送給模型的那張畫面。
        /// 任何失敗都只印警告：這是診斷用途，不該把正在跑的 bot 弄掛。
        /// </summary>
        private static void RecordDecision(int step, long latencyMs, int framesSent,
                                           DecisionResponse decision, string executed)
        {
            string frameRelPath = "";
            string? framesDir = ObserveFramesDir;
            byte[]? frameJpeg = _lastFrameJpeg;
            if (framesDir != null && frameJpeg != null)
            {
                try
                {
                    Directory.CreateDirectory(framesDir);
                    string fileName = $"{step:D6}.jpg";
                    File.WriteAllBytes(Path.Combine(framesDir, fileName), frameJpeg);
                    frameRelPath = fileName;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[警告] 無法寫入觀察畫面: {ex.Message}");
                }
            }

            string? logPath = DecisionLogPath;
            if (logPath == null)
            {
                return;
            }

            try
            {
                string? logDir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                var entry = new DecisionLogEntry
                {
                    Step = step,
                    Ts = DateTime.UtcNow.ToString("o"),
                    Tag = RunTag,
                    LatencyMs = latencyMs,
                    Action = decision.Action,
                    RawAction = decision.RawAction,
                    Thought = decision.Thought,
                    Degenerate = decision.Degenerate,
                    FramesSent = framesSent,
                    Frame = frameRelPath,
                    DryRun = DryRun,
                    Executed = executed,
                };
                File.AppendAllText(logPath,
                                   JsonSerializer.Serialize(entry, LogJsonOptions) + Environment.NewLine,
                                   LogEncoding);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[警告] 無法寫入決策紀錄: {ex.Message}");
            }
        }

        /// <summary>
        /// 執行遊戲物理動作 (利用 PostMessage 發送鍵盤滑鼠指令)
        /// </summary>
        private static string ExecuteGameAction(string action)
        {
            if (_targetWindowHwnd == IntPtr.Zero && !DryRun)
            {
                return "FAILED_NO_TARGET_WINDOW";
            }

            try
            {
                string actionUpper = action.Trim().ToUpper();

                if (!ActionMap.TryGetValue(actionUpper, out string? input))
                {
                    Console.WriteLine($"[Action] 未知動作 '{actionUpper}'，當成 IDLE 忽略");
                    return "IDLE";
                }

                if (DryRun)
                {
                    // 觀察模式：只印出「會做什麼」，不真的送出。
                    // 回傳值仍照實回報，AI 下一輪才有正確的上下文可判斷。
                    Console.WriteLine($"[DryRun] 不送出，原本會執行: {actionUpper} -> {input}");
                    return actionUpper;
                }

                if (input.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[Action] 保持原地靜止 (IDLE)");
                }
                else if (input.Equals("CLICK", StringComparison.OrdinalIgnoreCase))
                {
                    SendMouseClick();
                }
                else
                {
                    int vk = GetVirtualKeyCode(input);
                    if (vk == 0)
                    {
                        Console.WriteLine($"[Action] 無法解析按鍵名稱 '{input}'（動作 {actionUpper}）");
                        return $"FAILED_BAD_KEY_{input}";
                    }
                    SendKeyPress(vk);
                }

                return actionUpper; // 誠實回報剛剛送出的動作,而不是騙AI說永遠SUCCESS,避免文字提示汙染畫面判讀
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Action] 執行按鍵失敗: {ex.Message}");
                return $"FAILED_{ex.Message}";
            }
        }

        /// <summary>
        /// 動作名稱 -> 實際輸入的對應表。
        ///
        /// 每一項都可以用環境變數覆寫，不必改程式碼重編，例如：
        ///     $env:BOT_ACTION_TALK_TO_NPC = "CLICK"
        ///     $env:BOT_ACTION_JUMP        = "SPACE"
        /// 值可以是單一按鍵名稱（W / A / E / SPACE / TAB / ESC / 1 / 2 ...）、
        /// "CLICK"（滑鼠左鍵點畫面中心）或 "NONE"（不做事）。
        /// </summary>
        private static readonly Dictionary<string, string> ActionMap = BuildActionMap();

        private static Dictionary<string, string> BuildActionMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MOVE_FORWARD"] = "W",
                ["TURN_LEFT"] = "A",
                ["TURN_RIGHT"] = "D",
                ["JUMP"] = "SPACE",
                ["ATTACK"] = "CLICK",

                // Blox Fruits 的任務 NPC 是點畫面上的 Interact 按鈕，不是按 E。
                // 這裡原本寫死成 E 並註明「假設」，實測結果是 E 在這款遊戲
                // 綁的是見聞色 —— 想接任務卻開了見聞色。
                ["TALK_TO_NPC"] = "CLICK",

                ["SWITCH_CHANNEL"] = "TAB",
                ["RECONNECT"] = "ESC",
                ["IDLE"] = "NONE",
            };

            var names = new List<string>(map.Keys);
            foreach (string name in names)
            {
                string? overridden = Environment.GetEnvironmentVariable("BOT_ACTION_" + name);
                if (!string.IsNullOrWhiteSpace(overridden))
                {
                    map[name] = overridden.Trim();
                }
            }
            return map;
        }

        private static int GetVirtualKeyCode(string keyName)
        {
            string key = keyName.Trim().ToUpper();

            if (key.Length == 1)
            {
                char c = key[0];
                if (c >= 'A' && c <= 'Z') return Win32.VK_A + (c - 'A');
                if (c >= '0' && c <= '9') return Win32.VK_0 + (c - '0');
            }

            switch (key)
            {
                case "SPACE": return Win32.VK_SPACE;
                case "TAB": return Win32.VK_TAB;
                case "ESC":
                case "ESCAPE": return Win32.VK_ESCAPE;
                case "SHIFT": return Win32.VK_SHIFT;
                case "CTRL":
                case "CONTROL": return Win32.VK_CONTROL;
                case "ENTER":
                case "RETURN": return Win32.VK_RETURN;
                default: return 0;
            }
        }

        private static void SendKeyPress(int vk)
        {
            Console.WriteLine($"[Action] 前景發送按鍵: {((Keys)vk).ToString()}");

            // 1. 先把遊戲視窗拉到最前面並取得焦點
            //    (PostMessage 送背景訊息 Roblox 完全不吃，必須真的是前景視窗
            //     SendInput 才會被遊戲引擎的硬體輸入判斷讀到)
            EnsureTargetForeground();

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

            EnsureTargetForeground();

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
        // 由舊到新的連續影格。多送幾張讓模型看得出「動作到底有沒有生效」——
        // 單一畫面判斷不出角色是在前進、卡在牆上還是原地不動。
        [JsonPropertyName("images")]
        public List<string> Images { get; set; } = new List<string>();

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

        // 模型陷入重複字元迴圈。這種回合會被安全網變成 IDLE 送回來，
        // 不標記出來的話，統計上就會和「AI 認真決定不動」混在一起，
        // 而這兩件事的意義完全相反：一個是模型壞了，一個是模型的判斷。
        [JsonPropertyName("degenerate")]
        public bool Degenerate { get; set; }

        // 正規化前模型原本吐出來的動作字串。用來看它實際的用詞習慣，
        // 也才知道 ACTION_ALIASES 還漏接了哪些講法。
        [JsonPropertyName("raw_action")]
        public string RawAction { get; set; } = string.Empty;
    }

    /// <summary>決策紀錄的一行。欄位名稱要和 analyze_decisions.py 對得上。</summary>
    public class DecisionLogEntry
    {
        [JsonPropertyName("step")] public int Step { get; set; }
        [JsonPropertyName("ts")] public string Ts { get; set; } = string.Empty;
        [JsonPropertyName("tag")] public string Tag { get; set; } = string.Empty;
        [JsonPropertyName("latency_ms")] public long LatencyMs { get; set; }
        [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;
        [JsonPropertyName("raw_action")] public string RawAction { get; set; } = string.Empty;
        [JsonPropertyName("thought")] public string Thought { get; set; } = string.Empty;
        [JsonPropertyName("degenerate")] public bool Degenerate { get; set; }
        [JsonPropertyName("frames_sent")] public int FramesSent { get; set; }
        [JsonPropertyName("frame")] public string Frame { get; set; } = string.Empty;
        [JsonPropertyName("dry_run")] public bool DryRun { get; set; }
        [JsonPropertyName("executed")] public string Executed { get; set; } = string.Empty;
    }
}
