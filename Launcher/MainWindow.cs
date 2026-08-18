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

namespace BloxFruitsLauncher
{
    internal sealed class MainWindow : Form
    {
        // ── 啟動步驟 ────────────────────────────────────────────────
        private readonly StepRow _stepPython = new() { Title = "Python 環境" };
        private readonly StepRow _stepOllama = new() { Title = "Ollama 與視覺模型" };
        private readonly StepRow _stepServer = new() { Title = "AI 決策伺服器" };
        private readonly StepRow _stepBot = new() { Title = "遊戲控制端" };

        // ── 設定 ────────────────────────────────────────────────────
        private readonly ComboBox _model = new();
        private readonly NumericUpDown _imageWidth = new();
        private readonly NumericUpDown _loopDelay = new();
        private readonly NumericUpDown _frameCount = new();
        private readonly CheckBox _dryRun = new();

        private readonly FlatButton _btnStart = new();
        private readonly FlatButton _btnStop = new();
        private readonly FlatButton _btnFrame = new();
        private readonly RichTextBox _log = new();

        private readonly System.Windows.Forms.Timer _pulseTimer = new() { Interval = 33 };
        private readonly System.Windows.Forms.Timer _fadeTimer = new() { Interval = 15 };
        private float _phase;

        private readonly List<Process> _children = new();
        private CancellationTokenSource? _cts;
        private string _botDir = "";

        public MainWindow()
        {
            Text = "Blox Fruits Bot Launcher";
            ClientSize = new Size(920, 700);
            MinimumSize = new Size(820, 600);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.UI(9.75f);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            Opacity = 0d;                       // 淡入用，載入後補回 1

            BuildLayout();

            _pulseTimer.Tick += (_, _) =>
            {
                _phase = (_phase + 0.045f) % 1f;
                _stepPython.Tick(_phase);
                _stepOllama.Tick(_phase);
                _stepServer.Tick(_phase);
                _stepBot.Tick(_phase);
            };
            _pulseTimer.Start();

            _fadeTimer.Tick += (_, _) =>
            {
                if (Opacity >= 0.99d)
                {
                    Opacity = 1d;
                    _fadeTimer.Stop();
                    return;
                }
                Opacity = Math.Min(1d, Opacity + 0.09d);
            };

            Load += (_, _) =>
            {
                _fadeTimer.Start();
                LocateBotDirectory();
            };
            FormClosing += (_, _) => StopAll(quiet: true);
        }

        // ── 版面 ────────────────────────────────────────────────────

        private void BuildLayout()
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 78,
                BackColor = Theme.Surface,
            };
            header.Paint += (_, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                TextRenderer.DrawText(g, "Blox Fruits Bot", Theme.FontTitle,
                    new Point(22, 14), Theme.Text);
                TextRenderer.DrawText(g, "本機視覺模型自動遊玩 · Ollama + FastAPI + C#",
                    Theme.FontSubtitle, new Point(24, 44), Theme.TextDim);

                // 底部一條漸層強調線，唯一的裝飾
                if (header.Width <= 0) return;
                using var grad = new LinearGradientBrush(
                    new Rectangle(0, header.Height - 2, header.Width, 2),
                    Theme.Accent, Theme.Surface, LinearGradientMode.Horizontal);
                g.FillRectangle(grad, 0, header.Height - 2, header.Width, 2);
            };

            var steps = new Panel { Dock = DockStyle.Top, Height = 158, Padding = new Padding(20, 12, 20, 8) };
            _stepBot.Dock = DockStyle.Top;
            _stepServer.Dock = DockStyle.Top;
            _stepOllama.Dock = DockStyle.Top;
            _stepPython.Dock = DockStyle.Top;
            steps.Controls.AddRange(new Control[] { _stepBot, _stepServer, _stepOllama, _stepPython });

            var settings = BuildSettingsPanel();
            var actions = BuildActionPanel();

            _log.Dock = DockStyle.Fill;
            _log.BackColor = Color.FromArgb(14, 16, 21);
            _log.ForeColor = Theme.TextDim;
            _log.Font = Theme.Mono(9f);
            _log.BorderStyle = BorderStyle.None;
            _log.ReadOnly = true;
            _log.WordWrap = false;
            _log.ScrollBars = RichTextBoxScrollBars.Both;

            var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 6, 20, 18) };
            logHost.Controls.Add(_log);

            Controls.Add(logHost);
            Controls.Add(actions);
            Controls.Add(settings);
            Controls.Add(steps);
            Controls.Add(header);
        }

        private Panel BuildSettingsPanel()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 112, Padding = new Padding(20, 0, 20, 0) };

            Label Caption(string text, int x)
            {
                var l = new Label
                {
                    Text = text,
                    ForeColor = Theme.TextDim,
                    Font = Theme.UI(8.5f),
                    AutoSize = true,
                    Location = new Point(x, 6),
                };
                panel.Controls.Add(l);
                return l;
            }

            void StyleBox(Control c, int x, int width)
            {
                c.Location = new Point(x, 26);
                c.Width = width;
                c.BackColor = Theme.SurfaceHi;
                c.ForeColor = Theme.Text;
                c.Font = Theme.UI(9.5f);
                panel.Controls.Add(c);
            }

            Caption("視覺模型", 2);
            _model.DropDownStyle = ComboBoxStyle.DropDown;
            _model.FlatStyle = FlatStyle.Flat;
            _model.Items.AddRange(new object[] { "qwen2.5vl:3b", "gemma3:4b", "minicpm-v", "qwen2.5vl:7b" });
            _model.Text = "qwen2.5vl:3b";
            StyleBox(_model, 2, 190);

            Caption("截圖寬度", 210);
            _imageWidth.Minimum = 160; _imageWidth.Maximum = 1920; _imageWidth.Increment = 20;
            _imageWidth.Value = 400;
            _imageWidth.BorderStyle = BorderStyle.None;
            StyleBox(_imageWidth, 210, 92);

            Caption("每輪延遲 ms", 316);
            _loopDelay.Minimum = 0; _loopDelay.Maximum = 10000; _loopDelay.Increment = 100;
            _loopDelay.Value = 200;
            _loopDelay.BorderStyle = BorderStyle.None;
            StyleBox(_loopDelay, 316, 100);

            Caption("影格數", 430);
            _frameCount.Minimum = 1; _frameCount.Maximum = 5; _frameCount.Value = 2;
            _frameCount.BorderStyle = BorderStyle.None;
            StyleBox(_frameCount, 430, 68);

            // 這兩個放第二列。之前擠在第一列右側，視窗縮到最小寬度時會被裁掉。
            _dryRun.Text = "觀察模式（只顯示決策，不送按鍵、不搶視窗焦點）";
            _dryRun.ForeColor = Theme.Text;
            _dryRun.Font = Theme.UI(9.5f);
            _dryRun.AutoSize = true;
            _dryRun.Location = new Point(2, 62);
            panel.Controls.Add(_dryRun);

            var hint = new Label
            {
                Text = "截圖寬度過大會讓小模型輸出崩壞，過小會產生幻覺；qwen2.5vl:3b 實測可用區間約 400。",
                ForeColor = Theme.TextDim,
                Font = Theme.UI(8.25f),
                AutoSize = true,
                Location = new Point(2, 84),
            };
            panel.Controls.Add(hint);

            return panel;
        }

        private Panel BuildActionPanel()
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 62, Padding = new Padding(20, 0, 20, 0) };

            _btnStart.Text = "啟動";
            _btnStart.BaseColor = Theme.Accent;
            _btnStart.HoverColor = Theme.AccentHi;
            _btnStart.LabelColor = Color.FromArgb(12, 18, 28);
            _btnStart.Size = new Size(132, 38);
            _btnStart.Location = new Point(2, 8);
            _btnStart.Click += async (_, _) => await StartAllAsync();

            _btnStop.Text = "停止";
            _btnStop.Size = new Size(110, 38);
            _btnStop.Location = new Point(144, 8);
            _btnStop.Enabled = false;
            _btnStop.Click += (_, _) => StopAll(quiet: false);

            _btnFrame.Text = "開啟 AI 看到的畫面";
            _btnFrame.Size = new Size(190, 38);
            _btnFrame.Location = new Point(264, 8);
            _btnFrame.Click += (_, _) => OpenLastFrame();

            panel.Controls.AddRange(new Control[] { _btnStart, _btnStop, _btnFrame });
            return panel;
        }

        // ── 記錄 ────────────────────────────────────────────────────

        private void Log(string line, Color? color = null)
        {
            if (_log.IsDisposed) return;
            if (_log.InvokeRequired)
            {
                _log.BeginInvoke(new Action(() => Log(line, color)));
                return;
            }

            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = color ?? Theme.TextDim;
            _log.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
            _log.SelectionColor = _log.ForeColor;
            _log.ScrollToCaret();
        }

        private void SetStep(StepRow row, StepState state, string detail)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetStep(row, state, detail)));
                return;
            }
            row.State = state;
            row.Detail = detail;
        }

        // ── 找出專案資料夾 ──────────────────────────────────────────

        /// <summary>
        /// 從執行檔往上找含有 main.py 的 BloxFruitsBot 資料夾。
        /// 寫死相對路徑會在 bin/Debug/net8.0-windows 這種深度下失效，
        /// 而且使用者把 exe 搬走之後就再也找不到。
        /// </summary>
        private void LocateBotDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "BloxFruitsBot");
                if (File.Exists(Path.Combine(candidate, "main.py")))
                {
                    _botDir = candidate;
                    break;
                }
                if (File.Exists(Path.Combine(dir.FullName, "main.py")) &&
                    File.Exists(Path.Combine(dir.FullName, "BloxFruitsBot.csproj")))
                {
                    _botDir = dir.FullName;
                    break;
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

            foreach (var s in new[] { _stepPython, _stepOllama, _stepServer, _stepBot })
            {
                SetStep(s, StepState.Pending, "");
            }
            Log("開始啟動…", Theme.Accent);

            try
            {
                if (!await CheckPythonAsync(token)) { Abort(); return; }
                if (!await CheckOllamaAsync(token)) { Abort(); return; }
                if (!StartAiServer()) { Abort(); return; }

                SetStep(_stepServer, StepState.Running, "等待伺服器就緒…");
                await Task.Delay(4000, token);
                SetStep(_stepServer, StepState.Ok, "http://localhost:8000/decide");

                if (!StartBot()) { Abort(); return; }

                Log("全部啟動完成。", Theme.Ok);
                if (_dryRun.Checked)
                {
                    Log("觀察模式：不會送出按鍵，也不會搶走視窗焦點。", Theme.Warn);
                }
                else
                {
                    Log("Bot 會把 Roblox 拉到前景並實際操作，要停請按「停止」。", Theme.Warn);
                }
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
                if (!IsDisposed)
                {
                    BeginInvoke(new Action(() =>
                    {
                        _btnStart.Enabled = true;
                        _btnStop.Enabled = false;
                    }));
                }
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

            var (pullCode, _) = await RunCaptureAsync("ollama", $"pull {model}", token, timeoutMs: 45 * 60 * 1000);
            if (pullCode != 0)
            {
                SetStep(_stepOllama, StepState.Fail, $"{model} 下載失敗");
                Log($"下載 {model} 失敗。請確認模型名稱正確且網路正常。", Theme.Fail);
                return false;
            }

            SetStep(_stepOllama, StepState.Ok, $"{model} 已就緒");
            return true;
        }

        private bool StartAiServer()
        {
            SetStep(_stepServer, StepState.Running, "啟動 FastAPI…");
            var p = StartTracked("python", "main.py", "AI");
            if (p == null)
            {
                SetStep(_stepServer, StepState.Fail, "無法啟動 main.py");
                return false;
            }
            return true;
        }

        private bool StartBot()
        {
            SetStep(_stepBot, StepState.Running, "編譯並啟動…");
            var p = StartTracked("dotnet", "run --project BloxFruitsBot.csproj -c Release", "BOT");
            if (p == null)
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

        private Process? StartTracked(string exe, string args, string tag)
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
                Color tint = tag == "AI" ? Theme.Accent : Theme.Ok;
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) Log($"[{tag}] {e.Data}", tint); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Log($"[{tag}] {e.Data}", Theme.Warn); };
                proc.Exited += (_, _) => Log($"[{tag}] 行程結束。", Theme.TextDim);

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                lock (_children) _children.Add(proc);
                return proc;
            }
            catch (Exception ex)
            {
                Log($"[{tag}] 啟動失敗: {ex.Message}", Theme.Fail);
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
                // 找不到執行檔會走到這裡，回非零讓呼叫端統一處理
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
                    try
                    {
                        if (!p.HasExited) p.Kill(entireProcessTree: true);
                    }
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
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log($"無法開啟圖片: {ex.Message}", Theme.Fail);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pulseTimer.Dispose();
                _fadeTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
