using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
// WinForms 專案的 ImplicitUsings 會自動引入 System.Threading，
// 它和 System.Windows.Forms 都有 Timer，不指定就是 CS0104 模稜兩可。
using Timer = System.Windows.Forms.Timer;

namespace BloxFruitsLauncher
{
    internal sealed class MainWindow : Form
    {
        // 內容最大寬度。視窗再寬也維持這個寬度並置中，不讓元件被拉開到兩側。
        private const int ContentWidth = 1040;

        private readonly Panel _root = new() { Dock = DockStyle.Fill };

        // ── 啟動步驟 ────────────────────────────────────────────────
        private readonly StepRow _stepPython = new() { Title = "Python 環境" };
        private readonly StepRow _stepOllama = new() { Title = "Ollama 與視覺模型" };
        private readonly StepRow _stepRoblox = new() { Title = "Roblox 遊戲視窗" };
        private readonly StepRow _stepServer = new() { Title = "AI 決策伺服器" };
        private readonly StepRow _stepBot = new() { Title = "遊戲控制端" };

        // ── 設定 ────────────────────────────────────────────────────
        private readonly ComboBox _model = new();
        private readonly NumericUpDown _imageWidth = new();
        private readonly NumericUpDown _loopDelay = new();
        private readonly NumericUpDown _frameCount = new();
        private readonly RoundedCheckBox _dryRun = new();
        private readonly RoundedCheckBox _autoRoblox = new();
        private readonly TextBox _robloxLink = new();

        private readonly FlatButton _btnStart = new();
        private readonly FlatButton _btnStop = new();
        private readonly FlatButton _btnFrame = new();

        // ── 三個輸出區塊 ────────────────────────────────────────────
        private readonly RichTextBox _logBot = new();
        private readonly RichTextBox _logPython = new();
        private readonly RichTextBox _logApi = new();

        private readonly Timer _pulseTimer = new() { Interval = 33 };
        private readonly Timer _fadeTimer = new() { Interval = 15 };
        private float _phase;

        private readonly List<Process> _children = new();
        private CancellationTokenSource? _cts;
        private string _botDir = "";

        public MainWindow()
        {
            Text = "Blox Fruits Bot Launcher";
            ClientSize = new Size(1180, 900);
            MinimumSize = new Size(1020, 800);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.UI(9.75f);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            Opacity = 0d;

            BuildLayout();
            CenterContent();
            Resize += (_, _) => CenterContent();

            _pulseTimer.Tick += (_, _) =>
            {
                _phase = (_phase + 0.045f) % 1f;
                _stepPython.Tick(_phase);
                _stepOllama.Tick(_phase);
                _stepRoblox.Tick(_phase);
                _stepServer.Tick(_phase);
                _stepBot.Tick(_phase);
            };
            _pulseTimer.Start();

            _fadeTimer.Tick += (_, _) =>
            {
                if (Opacity >= 0.99d) { Opacity = 1d; _fadeTimer.Stop(); return; }
                Opacity = Math.Min(1d, Opacity + 0.09d);
            };

            Load += (_, _) => { _fadeTimer.Start(); LocateBotDirectory(); };
            FormClosing += (_, _) => StopAll(quiet: true);
        }

        /// <summary>把內容維持在 ContentWidth 並置中，靠左右內距達成。</summary>
        private void CenterContent()
        {
            int pad = Math.Max(24, (ClientSize.Width - ContentWidth) / 2);
            _root.Padding = new Padding(pad, 0, pad, 20);
        }

        // ── 版面 ────────────────────────────────────────────────────

        private void BuildLayout()
        {
            var header = BuildHeader();
            var steps = BuildSteps();
            var settings = BuildSettings();
            var actions = BuildActions();
            var logs = BuildLogs();

            // Dock.Top 的堆疊順序是「後加的在上」，所以要反著加
            _root.Controls.Add(logs);
            _root.Controls.Add(actions);
            _root.Controls.Add(settings);
            _root.Controls.Add(steps);

            Controls.Add(_root);
            Controls.Add(header);
        }

        private Panel BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Theme.Surface };
            header.Paint += (_, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // 標題與副標題各自置中，兩者之間留出較大的行距
                var titleRect = new Rectangle(0, 22, header.Width, 32);
                TextRenderer.DrawText(g, "Blox Fruits Bot", Theme.FontTitle, titleRect, Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                var subRect = new Rectangle(0, 64, header.Width, 20);
                TextRenderer.DrawText(g, "本機視覺模型自動遊玩  ·  Ollama + FastAPI + C#",
                    Theme.FontSubtitle, subRect, Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                if (header.Width <= 0) return;
                using var grad = new LinearGradientBrush(
                    new Rectangle(0, header.Height - 2, header.Width, 2),
                    Theme.Surface, Theme.Accent, LinearGradientMode.Horizontal);
                g.FillRectangle(grad, 0, header.Height - 2, header.Width, 2);
            };
            return header;
        }

        private Panel BuildSteps()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 196, Padding = new Padding(0, 16, 0, 8) };
            foreach (var row in new[] { _stepBot, _stepServer, _stepRoblox, _stepOllama, _stepPython })
            {
                row.Dock = DockStyle.Top;
                panel.Controls.Add(row);
            }
            return panel;
        }

        /// <summary>把控制項包進圓角面板，做出 Android 填色輸入框的樣子。</summary>
        private static RoundedPanel WrapInput(Control inner, int width, int height = 36)
        {
            var host = new RoundedPanel { Width = width, Height = height };
            inner.Width = width - 22;
            inner.Location = new Point(11, Math.Max(0, (height - inner.Height) / 2));
            host.Controls.Add(inner);
            return host;
        }

        private Label Caption(string text)
            => new()
            {
                Text = text,
                ForeColor = Theme.TextDim,
                Font = Theme.UI(8.5f),
                AutoSize = true,
            };

        private Panel BuildSettings()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 190 };

            void Style(Control c)
            {
                c.BackColor = Theme.SurfaceHi;
                c.ForeColor = Theme.Text;
                c.Font = Theme.UI(9.5f);
            }

            _model.DropDownStyle = ComboBoxStyle.DropDown;
            _model.FlatStyle = FlatStyle.Flat;
            _model.Items.AddRange(new object[] { "qwen2.5vl:3b", "gemma3:4b", "minicpm-v", "qwen2.5vl:7b" });
            _model.Text = "qwen2.5vl:3b";
            Style(_model);

            _imageWidth.Minimum = 160; _imageWidth.Maximum = 1920; _imageWidth.Increment = 20;
            _imageWidth.Value = 400; _imageWidth.BorderStyle = BorderStyle.None; Style(_imageWidth);

            _loopDelay.Minimum = 0; _loopDelay.Maximum = 10000; _loopDelay.Increment = 100;
            _loopDelay.Value = 200; _loopDelay.BorderStyle = BorderStyle.None; Style(_loopDelay);

            _frameCount.Minimum = 1; _frameCount.Maximum = 5; _frameCount.Value = 2;
            _frameCount.BorderStyle = BorderStyle.None; Style(_frameCount);

            _robloxLink.Text = "roblox://";
            _robloxLink.BorderStyle = BorderStyle.None;
            Style(_robloxLink);

            // 第一列：四個設定，用表格平均分配才會跟著視窗置中
            var row1 = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 66,
                ColumnCount = 4,
                RowCount = 2,
            };
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            row1.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
            row1.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));

            row1.Controls.Add(Caption("視覺模型"), 0, 0);
            row1.Controls.Add(Caption("截圖寬度"), 1, 0);
            row1.Controls.Add(Caption("每輪延遲 ms"), 2, 0);
            row1.Controls.Add(Caption("影格數"), 3, 0);
            row1.Controls.Add(WrapInput(_model, 300), 0, 1);
            row1.Controls.Add(WrapInput(_imageWidth, 170), 1, 1);
            row1.Controls.Add(WrapInput(_loopDelay, 170), 2, 1);
            row1.Controls.Add(WrapInput(_frameCount, 170), 3, 1);

            // 第二列：Roblox 自動啟動
            var row2 = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 76,
                ColumnCount = 2,
                RowCount = 2,
            };
            row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            // 第一列要容得下 26px 高的 RoundedCheckBox，不能只給 22
            row2.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            row2.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));

            _autoRoblox.Text = "啟動時自動開啟 Roblox";
            _autoRoblox.Font = Theme.UI(9.5f);
            _autoRoblox.Checked = true;
            _autoRoblox.Width = 260;

            _dryRun.Text = "觀察模式（不送按鍵、不搶焦點）";
            _dryRun.Font = Theme.UI(9.5f);
            _dryRun.Width = 320;

            row2.Controls.Add(_autoRoblox, 0, 0);
            row2.Controls.Add(Caption("Roblox 啟動連結或執行檔路徑"), 1, 0);
            row2.Controls.Add(_dryRun, 0, 1);
            row2.Controls.Add(WrapInput(_robloxLink, 420), 1, 1);

            var hint = new Label
            {
                Dock = DockStyle.Top,
                Height = 42,
                Text = "截圖寬度過大會讓小模型輸出崩壞、過小會產生幻覺；qwen2.5vl:3b 實測可用區間約 400。\n"
                     + "找不到 Roblox 視窗時 AI 會看到你的桌面，決策不會有意義，所以這一步失敗就會停下來。",
                ForeColor = Theme.TextDim,
                Font = Theme.UI(8.25f),
                TextAlign = ContentAlignment.MiddleCenter,
            };

            panel.Controls.Add(hint);
            panel.Controls.Add(row2);
            panel.Controls.Add(row1);
            return panel;
        }

        private Panel BuildActions()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 66,
                ColumnCount = 3,
                RowCount = 1,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));

            _btnStart.Text = "啟動";
            _btnStart.BaseColor = Theme.Accent;
            _btnStart.HoverColor = Theme.AccentHi;
            _btnStart.LabelColor = Color.FromArgb(12, 18, 28);
            _btnStart.Size = new Size(180, 42);
            _btnStart.Anchor = AnchorStyles.None;      // 在儲存格內置中
            _btnStart.Click += async (_, _) => await StartAllAsync();

            _btnStop.Text = "停止";
            _btnStop.Size = new Size(180, 42);
            _btnStop.Anchor = AnchorStyles.None;
            _btnStop.Enabled = false;
            _btnStop.Click += (_, _) => StopAll(quiet: false);

            _btnFrame.Text = "開啟 AI 看到的畫面";
            _btnFrame.Size = new Size(210, 42);
            _btnFrame.Anchor = AnchorStyles.None;
            _btnFrame.Click += (_, _) => OpenLastFrame();

            table.Controls.Add(_btnStart, 0, 0);
            table.Controls.Add(_btnStop, 1, 0);
            table.Controls.Add(_btnFrame, 2, 0);
            return table;
        }

        /// <summary>三個並排的輸出區塊。每個是「圓角外框 + 標題 + 內容」。</summary>
        private Control BuildLogs()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));

            table.Controls.Add(LogPane("C#  遊戲控制端", _logBot, Theme.Ok), 0, 0);
            table.Controls.Add(LogPane("Python  AI 決策", _logPython, Theme.Accent), 1, 0);
            table.Controls.Add(LogPane("FastAPI  HTTP", _logApi, Theme.Warn), 2, 0);
            return table;
        }

        private Control LogPane(string title, RichTextBox box, Color tint)
        {
            var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5, 4, 5, 0), BackColor = Color.Transparent };

            var card = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                Radius = 14,
                Fill = Color.FromArgb(14, 16, 21),
                Stroke = Theme.Border,
                Padding = new Padding(12, 34, 10, 12),
            };

            var heading = new Label
            {
                Text = title,
                ForeColor = tint,
                Font = Theme.UI(8.75f, FontStyle.Bold),
                AutoSize = true,
                BackColor = Color.Transparent,
                Location = new Point(14, 11),
            };

            box.Dock = DockStyle.Fill;
            box.BackColor = Color.FromArgb(14, 16, 21);
            box.ForeColor = Theme.TextDim;
            box.Font = Theme.Mono(8.25f);
            box.BorderStyle = BorderStyle.None;
            box.ReadOnly = true;
            box.WordWrap = false;
            box.ScrollBars = RichTextBoxScrollBars.Both;

            card.Controls.Add(box);
            card.Controls.Add(heading);
            heading.BringToFront();
            outer.Controls.Add(card);
            return outer;
        }

        // ── 記錄 ────────────────────────────────────────────────────

        private enum Pane { Bot, Python, Api }

        private void Log(Pane pane, string line, Color? color = null)
        {
            RichTextBox box = pane switch
            {
                Pane.Bot => _logBot,
                Pane.Api => _logApi,
                _ => _logPython,
            };
            if (box.IsDisposed) return;
            if (box.InvokeRequired)
            {
                box.BeginInvoke(new Action(() => Log(pane, line, color)));
                return;
            }

            // 每個區塊只留最後 400 行，跑久了才不會吃光記憶體
            if (box.Lines.Length > 400)
            {
                box.Text = string.Join(Environment.NewLine, box.Lines[^300..]);
            }

            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = color ?? Theme.TextDim;
            box.AppendText($"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
            box.SelectionColor = box.ForeColor;
            box.ScrollToCaret();
        }

        /// <summary>系統訊息一律進 Python 區塊（啟動流程的主要敘事都在那裡）。</summary>
        private void Log(string line, Color? color = null) => Log(Pane.Python, line, color);

        /// <summary>
        /// main.py 的輸出裡混了兩種東西：uvicorn 的 HTTP 存取紀錄，
        /// 以及我們自己 print 的 AI 推理過程。依內容分流到不同區塊。
        /// </summary>
        private static bool IsUvicornLine(string line)
        {
            string t = line.TrimStart();
            return t.StartsWith("INFO:", StringComparison.Ordinal)
                || t.StartsWith("WARNING:", StringComparison.Ordinal)
                || t.StartsWith("ERROR:", StringComparison.Ordinal)
                || t.StartsWith("CRITICAL:", StringComparison.Ordinal)
                || t.Contains("Uvicorn running", StringComparison.Ordinal)
                || t.Contains("Application startup", StringComparison.Ordinal)
                || t.Contains("Started server process", StringComparison.Ordinal)
                || t.Contains("Waiting for application", StringComparison.Ordinal);
        }

        private void SetStep(StepRow row, StepState state, string detail)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetStep(row, state, detail))); return; }
            row.State = state;
            row.Detail = detail;
        }

        // ── 找出專案資料夾 ──────────────────────────────────────────

        private void LocateBotDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "BloxFruitsBot");
                if (File.Exists(Path.Combine(candidate, "main.py"))) { _botDir = candidate; break; }
                if (File.Exists(Path.Combine(dir.FullName, "main.py")) &&
                    File.Exists(Path.Combine(dir.FullName, "BloxFruitsBot.csproj")))
                {
                    _botDir = dir.FullName; break;
                }
            }

            if (string.IsNullOrEmpty(_botDir))
            {
                Log("找不到 BloxFruitsBot 資料夾（需要 main.py 與 BloxFruitsBot.csproj）。", Theme.Fail);
                Log("請把這個執行檔放在專案資料夾內，或其子資料夾中。", Theme.TextDim);
                _btnStart.Enabled = false;
                return;
            }
            Log($"專案資料夾: {_botDir}", Theme.TextDim);
        }

        // ── 啟動流程 ────────────────────────────────────────────────

        private async Task StartAllAsync()
        {
            if (string.IsNullOrEmpty(_botDir)) return;

            _btnStart.Enabled = false;
            _btnStop.Enabled = true;
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            foreach (var s in new[] { _stepPython, _stepOllama, _stepRoblox, _stepServer, _stepBot })
            {
                SetStep(s, StepState.Pending, "");
            }
            Log("開始啟動…", Theme.Accent);

            try
            {
                if (!await CheckPythonAsync(token)) { Abort(); return; }
                if (!await CheckOllamaAsync(token)) { Abort(); return; }
                if (!await EnsureRobloxAsync(token)) { Abort(); return; }
                if (!StartAiServer()) { Abort(); return; }

                SetStep(_stepServer, StepState.Running, "等待伺服器就緒…");
                await Task.Delay(4000, token);
                SetStep(_stepServer, StepState.Ok, "http://localhost:8000/decide");

                if (!StartBot()) { Abort(); return; }

                Log("全部啟動完成。", Theme.Ok);
                Log(_dryRun.Checked
                        ? "觀察模式：不會送出按鍵，也不會搶走視窗焦點。"
                        : "Bot 會把 Roblox 拉到前景並實際操作，要停請按「停止」。",
                    Theme.Warn);
            }
            catch (OperationCanceledException)
            {
                Log("啟動已取消。", Theme.TextDim);
            }
            catch (Exception ex)
            {
                Log($"啟動失敗: {ex.Message}", Theme.Fail);
                Abort();
            }

            void Abort()
            {
                StopAll(quiet: true);
                if (IsDisposed) return;
                BeginInvoke(new Action(() => { _btnStart.Enabled = true; _btnStop.Enabled = false; }));
            }
        }

        private async Task<bool> CheckPythonAsync(CancellationToken token)
        {
            SetStep(_stepPython, StepState.Running, "偵測中…");
            var (code, output) = await RunCaptureAsync("python", "--version", token);
            if (code != 0)
            {
                SetStep(_stepPython, StepState.Fail, "找不到 python，請先安裝 Python 3");
                Log("找不到 python。請安裝 Python 3 並確認它在 PATH 中。", Theme.Fail);
                return false;
            }
            SetStep(_stepPython, StepState.Ok, output.Trim());
            return true;
        }

        private async Task<bool> CheckOllamaAsync(CancellationToken token)
        {
            string model = _model.Text.Trim();
            SetStep(_stepOllama, StepState.Running, "檢查 Ollama…");

            var (code, list) = await RunCaptureAsync("ollama", "list", token);
            if (code != 0)
            {
                SetStep(_stepOllama, StepState.Fail, "找不到 ollama，請先安裝");
                Log("找不到 ollama。請至 https://ollama.com 安裝後再試。", Theme.Fail);
                return false;
            }

            if (list.Contains(model, StringComparison.OrdinalIgnoreCase))
            {
                SetStep(_stepOllama, StepState.Ok, $"{model} 已就緒");
                return true;
            }

            SetStep(_stepOllama, StepState.Running, $"下載 {model}（首次可能要好幾分鐘）…");
            Log($"本機沒有 {model}，開始下載。視覺模型約數 GB，請耐心等候。", Theme.Warn);

            var (pullCode, pullMsg) = await RunCaptureAsync("ollama", $"pull {model}", token, timeoutMs: 45 * 60 * 1000);
            if (pullCode != 0)
            {
                SetStep(_stepOllama, StepState.Fail, $"{model} 下載失敗");
                Log($"下載 {model} 失敗: {pullMsg}", Theme.Fail);
                return false;
            }

            SetStep(_stepOllama, StepState.Ok, $"{model} 已就緒");
            return true;
        }

        /// <summary>
        /// 確保 Roblox 已經在執行。找不到就停下來，不要硬跑 ——
        /// 沒有遊戲視窗時截圖會退回全螢幕，AI 看到的是桌面，決策全是廢的。
        /// </summary>
        private async Task<bool> EnsureRobloxAsync(CancellationToken token)
        {
            const string title = "Roblox";
            SetStep(_stepRoblox, StepState.Running, "尋找遊戲視窗…");

            if (NativeWindows.Exists(title))
            {
                SetStep(_stepRoblox, StepState.Ok, "已在執行");
                return true;
            }

            if (!_autoRoblox.Checked)
            {
                SetStep(_stepRoblox, StepState.Fail, "找不到 Roblox 視窗");
                Log("找不到 Roblox 視窗。請先手動開啟遊戲，或勾選「啟動時自動開啟 Roblox」。", Theme.Fail);
                return false;
            }

            string link = _robloxLink.Text.Trim();
            if (string.IsNullOrEmpty(link)) link = "roblox://";

            SetStep(_stepRoblox, StepState.Running, $"啟動 {link} …");
            Log($"Roblox 尚未執行，嘗試開啟: {link}", Theme.TextDim);

            try
            {
                Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStep(_stepRoblox, StepState.Fail, "無法啟動 Roblox");
                Log($"啟動 Roblox 失敗: {ex.Message}", Theme.Fail);
                Log("可以改填遊戲網址（https://www.roblox.com/games/...）或 RobloxPlayerBeta.exe 的完整路徑。", Theme.TextDim);
                return false;
            }

            // 從點擊到遊戲視窗出現通常要一段時間，尤其第一次啟動
            for (int waited = 0; waited < 120; waited++)
            {
                await Task.Delay(1000, token);
                if (NativeWindows.Exists(title))
                {
                    SetStep(_stepRoblox, StepState.Ok, $"已開啟（等待 {waited + 1} 秒）");
                    Log("Roblox 視窗已出現。請確認你已經進到遊戲畫面裡，不是登入頁或選單。", Theme.Warn);
                    return true;
                }
                if (waited % 10 == 9)
                {
                    SetStep(_stepRoblox, StepState.Running, $"等待遊戲視窗… {waited + 1}s");
                }
            }

            SetStep(_stepRoblox, StepState.Fail, "等了 120 秒仍未出現");
            Log("等待 Roblox 視窗逾時。請手動開啟遊戲後再按啟動。", Theme.Fail);
            return false;
        }

        private bool StartAiServer()
        {
            SetStep(_stepServer, StepState.Running, "啟動 FastAPI…");
            if (StartTracked("python", "main.py", Pane.Python) == null)
            {
                SetStep(_stepServer, StepState.Fail, "無法啟動 main.py");
                return false;
            }
            return true;
        }

        private bool StartBot()
        {
            SetStep(_stepBot, StepState.Running, "編譯並啟動…");
            if (StartTracked("dotnet", "run --project BloxFruitsBot.csproj -c Release", Pane.Bot) == null)
            {
                SetStep(_stepBot, StepState.Fail, "無法啟動 dotnet run");
                return false;
            }
            SetStep(_stepBot, StepState.Ok, _dryRun.Checked ? "觀察模式執行中" : "自動操作執行中");
            return true;
        }

        // ── 子行程 ──────────────────────────────────────────────────

        private void ApplySettings(ProcessStartInfo psi)
        {
            psi.Environment["BOT_MODEL"] = _model.Text.Trim();
            psi.Environment["BOT_MAX_IMAGE_WIDTH"] = ((int)_imageWidth.Value).ToString();
            psi.Environment["BOT_LOOP_DELAY_MS"] = ((int)_loopDelay.Value).ToString();
            psi.Environment["BOT_FRAME_COUNT"] = ((int)_frameCount.Value).ToString();
            psi.Environment["BOT_DRY_RUN"] = _dryRun.Checked ? "1" : "0";
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.Environment["PYTHONUNBUFFERED"] = "1";
        }

        private Process? StartTracked(string exe, string args, Pane pane)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    WorkingDirectory = _botDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                ApplySettings(psi);

                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

                void Route(string? data, bool isError)
                {
                    if (data == null) return;
                    // python 那條會再依內容拆成 Python / FastAPI 兩個區塊
                    Pane target = pane == Pane.Python && IsUvicornLine(data) ? Pane.Api : pane;
                    Color tint = isError ? Theme.Warn
                        : target == Pane.Bot ? Theme.Ok
                        : target == Pane.Api ? Theme.TextDim
                        : Theme.Text;
                    Log(target, data, tint);
                }

                proc.OutputDataReceived += (_, e) => Route(e.Data, false);
                proc.ErrorDataReceived += (_, e) => Route(e.Data, true);
                proc.Exited += (_, _) => Log(pane, "行程結束。", Theme.TextDim);

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                lock (_children) _children.Add(proc);
                return proc;
            }
            catch (Exception ex)
            {
                Log(pane, $"啟動失敗: {ex.Message}", Theme.Fail);
                return null;
            }
        }

        private async Task<(int code, string output)> RunCaptureAsync(
            string exe, string args, CancellationToken token, int timeoutMs = 30000)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    WorkingDirectory = string.IsNullOrEmpty(_botDir) ? Environment.CurrentDirectory : _botDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                using var proc = new Process { StartInfo = psi };
                var sb = new StringBuilder();
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(timeoutMs);

                try
                {
                    await proc.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    // 逾時，不是使用者按停止。回非零並帶出原因，
                    // 否則會被誤報成「啟動已取消」，讓人以為是自己點到的。
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    return (-1, $"逾時（超過 {timeoutMs / 1000} 秒）");
                }

                return (proc.ExitCode, sb.ToString());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }

        private void StopAll(bool quiet)
        {
            try { _cts?.Cancel(); } catch { }

            lock (_children)
            {
                foreach (var p in _children)
                {
                    try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
                    catch { /* 已經結束就算了 */ }
                    finally { p.Dispose(); }
                }
                _children.Clear();
            }

            if (quiet) return;

            Log("已停止所有元件。", Theme.Warn);
            SetStep(_stepServer, StepState.Pending, "");
            SetStep(_stepBot, StepState.Pending, "");
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
        }

        private void OpenLastFrame()
        {
            string path = Path.Combine(_botDir, "last_frame.jpg");
            if (!File.Exists(path))
            {
                Log("還沒有 last_frame.jpg — 先啟動 Bot 跑一輪。", Theme.Warn);
                return;
            }
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Log($"無法開啟圖片: {ex.Message}", Theme.Fail); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _pulseTimer.Dispose(); _fadeTimer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
