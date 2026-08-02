using System; 
using System.Drawing; 
using System.Threading.Tasks; 
using System.Windows.Forms; 
using System.IO; 
 
namespace BloxFruitsBot 
{ 
    public partial class MainForm : Form 
    { 
        private NamedPipeManager _pipeManager; 
        private BotCore _botCore; 
        private IntPtr _robloxHwnd = IntPtr.Zero; 
        private string _screenshotSavePath = "Screenshots"; 
        private bool _isRunning = false; 
 
        public MainForm() 
        { 
            InitializeComponent(); 
            this.Load += MainForm_Load; 
            this.FormClosing += MainForm_FormClosing; 
            Log("應用程式啟動。"); 
 
            // 確保截圖保存路徑存在 
            if (!Directory.Exists(_screenshotSavePath)) 
            { 
                Directory.CreateDirectory(_screenshotSavePath); 
            } 
        } 
 
        private void MainForm_Load(object sender, EventArgs e) 
        { 
            _pipeManager = new NamedPipeManager("BloxFruitsPipe", Log); 
            _pipeManager.OnPythonResponseReceived += HandlePythonResponse; 
            _pipeManager.StartServer(); 
        } 
 
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e) 
        { 
            _pipeManager?.StopServer(); 
            _botCore?.Stop(); 
        } 
 
        private void btnStart_Click(object sender, EventArgs e) 
        { 
            Log("嘗試啟動 Bot..."); 
            string windowTitle = "Roblox"; // Roblox 視窗標題 
            _robloxHwnd = Win32.FindWindow(null, windowTitle); 
 
            if (_robloxHwnd == IntPtr.Zero) 
            { 
                Log($"錯誤: 未找到 Roblox 視窗 (標題: {windowTitle})。"); 
                return; 
            } 
 
            Log($"已找到 Roblox 視窗，句柄: {_robloxHwnd}"); 
 
            _botCore = new BotCore(_robloxHwnd, Log); 
            _botCore.Start(); 
            _isRunning = true; 
 
            // 開始截圖並發送給 Python 
            Task.Run(async () => 
            { 
                while (_isRunning) 
                { 
                    try 
                    { 
                        string screenshotPath = ScreenshotManager.CaptureWindow(_robloxHwnd, _screenshotSavePath); 
                        Log($"截圖已保存: {screenshotPath}"); 
                        await _pipeManager.SendMessage(screenshotPath); 
                    } 
                    catch (Exception ex) 
                    { 
                        Log($"截圖或發送失敗: {ex.Message}"); 
                    } 
                    await Task.Delay(5000); // 每 5 秒截圖一次 
                } 
            }); 
        } 
 
        private void btnStop_Click(object sender, EventArgs e) 
        { 
            Log("嘗試停止 Bot..."); 
            _isRunning = false; 
            _botCore?.Stop(); 
            _pipeManager?.StopServer(); 
            _robloxHwnd = IntPtr.Zero; 
            Log("Bot 已停止。"); 
        } 
 
        private void HandlePythonResponse(PythonResponse response) 
        { 
            // 確保在 UI 線程上更新 UI 
            if (this.InvokeRequired) 
            { 
                this.Invoke(new Action(() => _botCore?.ProcessPythonDecision(response))); 
            } 
            else 
            { 
                _botCore?.ProcessPythonDecision(response); 
            } 
        } 
 
        public void Log(string message) 
        { 
            if (this.InvokeRequired) 
            { 
                this.Invoke(new Action(() => LogInternal(message))); 
            } 
            else 
            { 
                LogInternal(message); 
            } 
        } 
 
        private void LogInternal(string message) 
        { 
            if (richTextBoxLog.Lines.Length > 100) // 限制日誌行數 
            { 
                richTextBoxLog.Text = richTextBoxLog.Text.Substring(richTextBoxLog.Text.IndexOf('\n') + 1); 
            } 
            richTextBoxLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n"); 
            richTextBoxLog.ScrollToCaret(); 
        } 
    } 
}
