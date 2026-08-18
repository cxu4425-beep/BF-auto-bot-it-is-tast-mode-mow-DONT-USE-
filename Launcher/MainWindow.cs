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

        private readonly BackdropPanel _root = new() { Dock = DockStyle.Fill };

        // ── 啟動步驟 ────────────────────────────────────────────────
        private readonly StepRow _stepPython = new() { Title = "◢ 執行時核心 · PYTHON" };
        private readonly StepRow _stepOllama = new() { Title = "◢ 神經視覺模組 · OLLAMA" };
        private readonly StepRow _stepRoblox = new() { Title = "◢ 目標環境鎖定 · ROBLOX" };
        private readonly StepRow _stepServer = new() { Title = "◢ 推論決策引擎 · FASTAPI" };
        private readonly StepRow _stepBot = new() { Title = "◢ 戰術執行單元 · C#" };

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
            var header = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Theme.Surface };
            header.Paint += (_, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // 標題與副標題各自置中，兩者之間留出較大的行距
                var titleRect = new Rectangle(0, 22, header.Width, 32);
                TextRenderer.DrawText(g, "◤ B L O X   F R U I T S   A U T O N O M O U S ◢", Theme.FontTitle, titleRect, Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                var subRect = new Rectangle(0, 64, header.Width, 20);
                TextRenderer.DrawText(g, "本地神經視覺推論核心  ▸  全自主戰術決策管線  ▸  OLLAMA · FASTAPI · C#",
                    Theme.FontSubtitle, subRect, Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                if (header.Width <= 0) return;
                // 底線做成 紫 -> 青 -> 紫 的漸層，是整個介面的視覺主軸
                using (var grad = new LinearGradientBrush(
                    new Rectangle(0, header.Height - 2, header.Width, 2),
                    Theme.Violet, Theme.Accent, LinearGradientMode.Horizontal))
                {
                    grad.SetBlendTriangularShape(0.5f);
                    g.FillRectangle(grad, 0, header.Height - 2, header.Width, 2);
                }
            };
            return header;
        }

        private Panel BuildSteps()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 196, Padding = new Padding(0, 16, 0, 8), BackColor = Color.Transparent };
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
                BackColor = Color.Transparent,
            };

        private Panel BuildSettings()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 190, BackColor = Color.Transparent };

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
                BackColor = Color.Transparent,
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

            row1.Controls.Add(Caption("神經視覺模型"), 0, 0);
            row1.Controls.Add(Caption("感知解析度 px"), 1, 0);
            row1.Controls.Add(Caption("決策週期 ms"), 2, 0);
            row1.Controls.Add(Caption("時序取樣數"), 3, 0);
            row1.Controls.Add(WrapInput(_model, 300), 0, 1);
            row1.Controls.Add(WrapInput(_imageWidth, 170), 1, 1);
            row1.Controls.Add(WrapInput(_loopDelay, 170), 2, 1);
            row1.Controls.Add(WrapInput(_frameCount, 170), 3, 1);

            // 第二列：Roblox 自動啟動
            var row2 = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                BackColor = Color.Transparent,
                Height = 76,
                ColumnCount = 2,
                RowCount = 2,
            };
            row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
            row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
            // 第一列要容得下 26px 高的 RoundedCheckBox，不能只給 22
            row2.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            row2.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));

            _autoRoblox.Text = "自動喚醒目標環境";
            _autoRoblox.Font = Theme.UI(9.5f);
            _autoRoblox.Checked = true;
            _autoRoblox.Width = 260;

            _dryRun.Text = "靜默觀測模式（僅推論，不介入）";
            _dryRun.Font = Theme.UI(9.5f);
            _dryRun.Width = 320;

            row2.Controls.Add(_autoRoblox, 0, 0);
            row2.Controls.Add(Caption("目標環境喚醒向量（連結或執行檔路徑）"), 1, 0);
            row2.Controls.Add(_dryRun, 0, 1);
            row2.Controls.Add(WrapInput(_robloxLink, 420), 1, 1);

            var hint = new Label
            {
                Dock = DockStyle.Top,
                Height = 42,
                Text = "▸ 感知解析度過高會使推論核心輸出崩解，過低則引發幻覺；qwen2.5vl:3b 實測穩定區間約 400。\n"
                     + "▸ 未鎖定目標環境時，視覺模組擷取到的是桌面而非遊戲畫面，決策全數失效，故此步驟失敗即中止。",
                ForeColor = Theme.TextDim,
                Font = Theme.UI(8.25f),
                BackColor = Color.Transparent,
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
                BackColor = Color.Transparent,
                Height = 66,
                ColumnCount = 3,
                RowCount = 1,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));

            _btnStart.Text = "▶  啟 動 系 統";
            _btnStart.BaseColor = Theme.Accent;
            _btnStart.HoverColor = Theme.AccentHi;
            _btnStart.LabelColor = Color.FromArgb(4, 12, 20);
            _btnStart.GlowColor = Theme.Accent;
            _btnStart.BorderColor = Theme.Accent;
            _btnStart.Size = new Size(180, 42);
            _btnStart.Anchor = AnchorStyles.None;      // 在儲存格內置中
            _btnStart.Click += async (_, _) => await StartAllAsync();

            _btnStop.Text = "■  緊 急 停 機";
            _btnStop.GlowColor = Theme.Fail;
            _btnStop.BorderColor = Color.FromArgb(90, 40, 60);
            _btnStop.Size = new Size(180, 42);
            _btnStop.Anchor = AnchorStyles.None;
            _btnStop.Enabled = false;
            _btnStop.Click += (_, _) => StopAll(quiet: false);

            _btnFrame.Text = "◉  檢視感知影像";
            _btnFrame.GlowColor = Theme.Violet;
            _btnFrame.BorderColor = Color.FromArgb(70, 45, 110);
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
                BackColor = Color.Transparent,
                ColumnCount = 3,
                RowCount = 1,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));

            table.Controls.Add(LogPane("◈  戰術執行單元 / C#", _logBot, Theme.Ok), 0, 0);
            table.Controls.Add(LogPane("◈  推論核心 / PYTHON", _logPython, Theme.Accent), 1, 0);
            table.Controls.Add(LogPane("◈  通訊匯流排 / FASTAPI", _logApi, Theme.Violet), 2, 0);
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
                Log("◈ 掛載失敗 — 找不到 BloxFruitsBot 資料夾（需要 main.py 與 BloxFruitsBot.csproj）。", Theme.Fail);
                Log("請把這個執行檔放在專案資料夾內，或其子資料夾中。", Theme.TextDim);
                _btnStart.Enabled = false;
                return;
            }
            Log($"▸ 已掛載作戰資料夾: {_botDir}", Theme.TextDim);
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
            Log("▸ 初始化序列啟動…", Theme.Accent);

            try
            {
                if (!await CheckPythonAsync(token)) { Abort(); return; }
                if (!await CheckOllamaAsync(token)) { Abort(); return; }
                if (!await EnsureRobloxAsync(token)) { Abort(); return; }
                if (!StartAiServer()) { Abort(); return; }

                SetStep(_stepServer, StepState.Running, "等待推論引擎上線…");
                await Task.Delay(4000, token);
                SetStep(_stepServer, StepState.Ok, "http://localhost:8000/decide");

                if (!StartBot()) { Abort(); return; }

                Log("▸ 全系統上線，所有模組運作中。", Theme.Ok);
                Log(_dryRun.Checked
                        ? "▸ 靜默觀測：僅產生決策，不送出按鍵，也不會搶走視窗焦點。"
                        : "▸ 全自主介入：會把 Roblox 拉到前景並實際操作，要停請按「緊急停機」。",
                    Theme.Warn);
            }
            catch (OperationCanceledException)
            {
                Log("▸ 初始化序列已中止。", Theme.TextDim);
            }
            catch (Exception ex)
            {
                Log($"◈ 初始化失敗 — {ex.Message}", Theme.Fail);
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
            SetStep(_stepPython, StepState.Running, "掃描執行時…");
            var (code, output) = await RunCaptureAsync("python", "--version", token);
            if (code != 0)
            {
                SetStep(_stepPython, StepState.Fail, "核心離線 · 找不到 python");
                Log("◈ 執行時核心離線 — 找不到 python，請安裝 Python 3 並確認它在 PATH 中。", Theme.Fail);
                return false;
            }
            SetStep(_stepPython, StepState.Ok, output.Trim());
            return true;
        }

        private async Task<bool> CheckOllamaAsync(CancellationToken token)
        {
            string model = _model.Text.Trim();
            SetStep(_stepOllama, StepState.Running, "連結神經模組…");

            var (code, list) = await RunCaptureAsync("ollama", "list", token);
            if (code != 0)
            {
                SetStep(_stepOllama, StepState.Fail, "模組離線 · 找不到 ollama");
                Log("◈ 神經模組離線 — 找不到 ollama，請至 https://ollama.com 安裝後再試。", Theme.Fail);
                return false;
            }

            if (list.Contains(model, StringComparison.OrdinalIgnoreCase))
            {
                SetStep(_stepOllama, StepState.Ok, $"{model} · 權重已載入");
                return true;
            }

            SetStep(_stepOllama, StepState.Running, $"下載權重 {model}（首次可能要好幾分鐘）…");
            Log($"▸ 本機無 {model} 權重，開始下載。視覺模型約數 GB，請耐心等候。", Theme.Warn);

            var (pullCode, pullMsg) = await RunCaptureAsync("ollama", $"pull {model}", token, timeoutMs: 45 * 60 * 1000);
            if (pullCode != 0)
            {
                SetStep(_stepOllama, StepState.Fail, $"權重取得失敗 · {model}");
                Log($"◈ 權重下載失敗 — {model}: {pullMsg}", Theme.Fail);
                return false;
            }

            SetStep(_stepOllama, StepState.Ok, $"{model} · 權重已載入");
            return true;
        }

        /// <summary>
        /// 確保 Roblox 已經在執行。找不到就停下來，不要硬跑 ——
        /// 沒有遊戲視窗時截圖會退回全螢幕，AI 看到的是桌面，決策全是廢的。
        /// </summary>
        private async Task<bool> EnsureRobloxAsync(CancellationToken token)
        {
            const string title = "Roblox";
            SetStep(_stepRoblox, StepState.Running, "掃描目標環境…");

            if (NativeWindows.Exists(title))
            {
                SetStep(_stepRoblox, StepState.Ok, "目標已鎖定");
                return true;
            }

            if (!_autoRoblox.Checked)
            {
                SetStep(_stepRoblox, StepState.Fail, "鎖定失敗 · 找不到 Roblox 視窗");
                Log("◈ 目標環境未鎖定 — 找不到 Roblox 視窗。請先手動開啟遊戲，或勾選「自動喚醒目標環境」。", Theme.Fail);
                return false;
            }

            string link = _robloxLink.Text.Trim();
            if (string.IsNullOrEmpty(link)) link = "roblox://";

            SetStep(_stepRoblox, StepState.Running, $"發送喚醒訊號 {link} …");
            Log($"▸ 目標環境未就緒，發送喚醒向量: {link}", Theme.TextDim);

            try
            {
                Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStep(_stepRoblox, StepState.Fail, "喚醒失敗");
                Log($"◈ 喚醒目標環境失敗 — {ex.Message}", Theme.Fail);
                Log("可以改填遊戲網址（https://www.roblox.com/games/...）或 RobloxPlayerBeta.exe 的完整路徑。", Theme.TextDim);
                return false;
            }

            // 從點擊到遊戲視窗出現通常要一段時間，尤其第一次啟動
            for (int waited = 0; waited < 120; waited++)
            {
                await Task.Delay(1000, token);
                if (NativeWindows.Exists(title))
                {
                    SetStep(_stepRoblox, StepState.Ok, $"目標已鎖定（{waited + 1} 秒）");
                    Log("▸ 目標環境已上線。請確認你已經進到遊戲畫面裡，不是登入頁或選單。", Theme.Warn);
                    return true;
                }
                if (waited % 10 == 9)
                {
                    SetStep(_stepRoblox, StepState.Running, $"等待目標回應… {waited + 1}s");
                }
            }

            SetStep(_stepRoblox, StepState.Fail, "逾時 · 120 秒內未偵測到視窗");
            Log("◈ 目標環境回應逾時 — 請手動開啟遊戲後再按啟動。", Theme.Fail);
            return false;
        }

        private bool StartAiServer()
        {
            SetStep(_stepServer, StepState.Running, "喚醒推論引擎…");
            if (StartTracked("python", "main.py", Pane.Python) == null)
            {
                SetStep(_stepServer, StepState.Fail, "引擎啟動失敗 · main.py");
                return false;
            }
            return true;
        }

        private bool StartBot()
        {
            SetStep(_stepBot, StepState.Running, "編譯戰術單元…");
            if (StartTracked("dotnet", "run --project BloxFruitsBot.csproj -c Release", Pane.Bot) == null)
            {
                SetStep(_stepBot, StepState.Fail, "單元啟動失敗 · dotnet run");
                return false;
            }
            SetStep(_stepBot, StepState.Ok, _dryRun.Checked ? "靜默觀測中" : "全自主運作中");
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
                proc.Exited += (_, _) => Log(pane, "▸ 模組已離線。", Theme.TextDim);

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

            Log("▸ 全模組已停機。", Theme.Warn);
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
                Log("▸ 尚無感知影像 — 先啟動系統跑一輪。", Theme.Warn);
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
