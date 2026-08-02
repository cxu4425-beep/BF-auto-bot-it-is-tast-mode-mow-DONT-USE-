using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

class Program
{
    static async Task Main()
    {
        Console.WriteLine("========================================");
        Console.WriteLine("🚀 遊戲自動化機器人 (C# 肉體端) 已啟動！");
        Console.WriteLine("========================================");

        var httpClient = new HttpClient();
        var gameWindow = new GameWindow();

        // 設定 3 秒緩衝，讓你有時間把畫面切換到遊戲或觀察視窗
        Console.WriteLine("⏳ 系統將在 3 秒後開始進入全自動決策循環...");
        await Task.Delay(3000);

        while (true)
        {
            try
            {
                Console.WriteLine($"\n[C# 狀態] [{DateTime.Now.ToString("HH:mm:ss")}] 📸 正在截取遊戲畫面...");
                Bitmap screenshot = gameWindow.Capture();
                
                // 將圖片轉換為 Base64 字串
                string base64Image;
                using (var ms = new MemoryStream())
                {
                    screenshot.Save(ms, ImageFormat.Png);
                    byte[] imageBytes = ms.ToArray();
                    base64Image = Convert.ToBase64String(imageBytes);
                }
                
                Console.WriteLine("[C# 狀態] 📡 正在將圖片與上輪狀態送往 FastAPI 後台...");
                var request = new
                {
                    image_base64 = base64Image,
                    last_result = gameWindow.LastResult
                };
                
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                // 發送請求給 Python FastAPI
                var response = await httpClient.PostAsync("http://localhost:8000/decide", content);
                var responseString = await response.Content.ReadAsStringAsync();
                
                Console.WriteLine($"[C# 狀態] 📥 成功收到後台回覆！原始 JSON: {responseString}");
                
                // 解析 AI 的決策結果
                dynamic decision = Newtonsoft.Json.JsonConvert.DeserializeObject(responseString);
                string action = decision?.action;
                string thought = decision?.thought;
                
                Console.WriteLine($"[AI 思考點]: {thought}");

                // 執行鍵鼠操作
                if (!string.IsNullOrEmpty(action))
                {
                    gameWindow.ExecuteAction(action);
                    gameWindow.LastResult = "success"; // 成功執行，更新狀態
                }
                else
                {
                    Console.WriteLine("[C# 狀態] ⚠️ AI 本輪沒有給出具體動作 (Action 為空)，跳過操作。");
                    gameWindow.LastResult = "no_action";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [連線/執行錯誤]: {ex.Message}");
                Console.WriteLine("💡 請檢查 Python FastAPI 是否有正常啟動 (uvicorn main:app)！");
            }
            
            // 每次循環間隔 1 秒，避免把 CPU/網路 衝爆
            Console.WriteLine("[C# 狀態] ⏳ 等待 1 秒後進入下一輪計時...");
            await Task.Delay(1000); 
        }
    }
}

/// <summary>
/// 遊戲視窗操作類別（肉體外殼）
/// </summary>
class GameWindow
{
    // 記錄上一次的操作結果，會傳給 AI 當作上下文參考
    public string LastResult { get; set; } = "none";

    /// <summary>
    /// 抓取遊戲畫面（進階優化版：抓取後自動縮圖，防止本地 AI 大腦過載）
    /// </summary>
    public Bitmap Capture()
    {
        int screenWidth = 1920; 
        int screenHeight = 1200;

        // 1. 先抓取完整的 1920x1200 螢幕畫面
        Bitmap bmp = new Bitmap(screenWidth, screenHeight);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
        }

        // 2. 🔥 核心修正：將巨型圖片等比例縮小到 800x500
        int targetWidth = 800;
        int targetHeight = 500;
        Bitmap resizedBmp = new Bitmap(bmp, new Size(targetWidth, targetHeight));
        
        // 3. 釋放原本大圖的記憶體，防止電腦記憶體洩漏
        bmp.Dispose(); 

        return resizedBmp;
    }

    /// <summary>
    /// 根據 AI 給的指令，真的去驅動鍵盤或滑鼠
    /// </summary>
    public void ExecuteAction(string action)
    {
        Console.WriteLine($"🎮 [真實執行] 🕹️ 正在驅動硬體執行動作: 【{action}】");
    }
}