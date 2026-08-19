using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace BloxFruitsLauncher
{
    /// <summary>
    /// 錄製人類示範資料：定時擷取遊戲畫面，並記下當下按住的鍵與滑鼠狀態。
    /// 輸出給模仿學習（behavioural cloning）訓練用。
    /// </summary>
    internal sealed class RecorderWindow : Form
    {
        // 要記錄的按鍵。名稱同時是輸出 JSON 裡的標籤。
        private static readonly (string Name, int Vk)[] TrackedKeys =
        {
            ("W", 0x57), ("A", 0x41), ("S", 0x53), ("D", 0x44),
            ("Space", NativeWindows.VK_SPACE), ("Shift", NativeWindows.VK_SHIFT),
            ("Z", 0x5A), ("X", 0x58), ("C", 0x43), ("V", 0x56),
            ("E", 0x45), ("F", 0x46), ("Q", 0x51), ("R", 0x52),
            ("1", 0x31), ("2", 0x32), ("3", 0x33), ("4", 0x34),
            ("LeftClick", NativeWindows.VK_LBUTTON),
            ("RightClick", NativeWindows.VK_RBUTTON),
        };

        private readonly BackdropPanel _root = new() { Dock = DockStyle.Fill };
        private readonly NumericUpDown _rate = new();
        private readonly NumericUpDown _width = new();
        private readonly TextBox _outDir = new();
        private readonly FlatButton _btnStart = new();
        private readonly FlatButton _btnStop = new();
        private readonly FlatButton _btnOpen = new();
        private readonly RichTextBox _log = new();
        private readonly Label _stats = new();
        private readonly Label _live = new();

        private readonly Timer _uiTimer = new() { Interval = 100 };
        private CancellationTokenSource? _cts;
        private volatile int _frames;
        private volatile int _skipped;
        private DateTime _startedAt;
        private string _sessionDir = "";
        private volatile string _liveKeys = "";
        private volatile bool _recording;
        private bool _hotkeyWasDown;

        public RecorderWindow()
        {
            Text = "示範資料錄製器";
            ClientSize = new Size(900, 660);
            MinimumSize = new Size(820, 600);
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.UI(9.75f);
            StartPosition = FormStartPosition.CenterParent;
            DoubleBuffered = true;

            BuildLayout();

            _uiTimer.Tick += (_, _) => RefreshStats();
            _uiTimer.Start();

            FormClosing += (_, _) => StopRecording(quiet: true);
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; }
        }

        // ── 版面 ────────────────────────────────────────────────────

        private void BuildLayout()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Theme.Surface };
            header.Resize += (_, _) => header.Invalidate();
            header.Paint += (_, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int w = header.Width;
                if (w <= 0) return;

                const string title = "◤ 示 範 資 料 擷 取 ◢";
                const string sub = "記錄人類操作供模仿學習訓練  ▸  影像 + 按鍵狀態";
                Size ts = TextRenderer.MeasureText(g, title, Theme.FontTitleSmall);
                Size ss = TextRenderer.MeasureText(g, sub, Theme.FontSubtitle);
                int top = Math.Max(6, (header.Height - 2 - (ts.Height + 10 + ss.Height)) / 2);

                TextRenderer.DrawText(g, title, Theme.FontTitleSmall,
                    new Rectangle(0, top, w, ts.Height), Theme.Text, TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, sub, Theme.FontSubtitle,
                    new Rectangle(0, top + ts.Height + 10, w, ss.Height), Theme.TextDim,
                    TextFormatFlags.HorizontalCenter);

                using var grad = new LinearGradientBrush(
                    new Rectangle(0, header.Height - 2, w, 2), Theme.Violet, Theme.Accent,
                    LinearGradientMode.Horizontal);
                grad.SetBlendTriangularShape(0.5f);
                g.FillRectangle(grad, 0, header.Height - 2, w, 2);
            };

            _root.Padding = new Padding(28, 14, 28, 18);

            // 設定列
            var settings = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = Color.Transparent,
                ColumnCount = 3,
                RowCount = 2,
            };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56f));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));

            void Style(Control c)
            {
                c.BackColor = Theme.SurfaceHi;
                c.ForeColor = Theme.Text;
                c.Font = Theme.UI(9.5f);
            }

            _rate.Minimum = 2; _rate.Maximum = 30; _rate.Value = 10;
            _rate.BorderStyle = BorderStyle.None; Style(_rate);

            _width.Minimum = 128; _width.Maximum = 960; _width.Increment = 32;
            _width.Value = 320; _width.BorderStyle = BorderStyle.None; Style(_width);

            _outDir.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BloxFruitsDemos");
            _outDir.BorderStyle = BorderStyle.None; Style(_outDir);

            settings.Controls.Add(Caption("取樣頻率 Hz"), 0, 0);
            settings.Controls.Add(Caption("影像寬度 px"), 1, 0);
            settings.Controls.Add(Caption("輸出資料夾"), 2, 0);
            settings.Controls.Add(Wrap(_rate, 150), 0, 1);
            settings.Controls.Add(Wrap(_width, 150), 1, 1);
            settings.Controls.Add(Wrap(_outDir, 440), 2, 1);

            var hint = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.Transparent,
                ForeColor = Theme.TextDim,
                Font = Theme.UI(8.25f),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "▸ 只在 Roblox 是前景視窗時錄製；切走時會自動暫停，不會錄到你的桌面。\n"
                     + "▸ 按 F9 可直接開始／停止，不必切回這個視窗（切回來就會中斷錄製）。",
            };

            // 即時狀態
            _live.Dock = DockStyle.Top;
            _live.Height = 34;
            _live.BackColor = Color.Transparent;
            _live.ForeColor = Theme.Accent;
            _live.Font = Theme.Mono(10f);
            _live.TextAlign = ContentAlignment.MiddleLeft;
            _live.Text = "－";

            _stats.Dock = DockStyle.Top;
            _stats.Height = 28;
            _stats.BackColor = Color.Transparent;
            _stats.ForeColor = Theme.Text;
            _stats.Font = Theme.UI(10f, FontStyle.Bold);
            _stats.TextAlign = ContentAlignment.MiddleLeft;
            _stats.Text = "尚未開始";

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 62,
                BackColor = Color.Transparent,
                ColumnCount = 3,
                RowCount = 1,
            };
            for (int i = 0; i < 3; i++)
                actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

            _btnStart.Text = "● 開始錄製  (F9)";
            _btnStart.BaseColor = Theme.Accent;
            _btnStart.HoverColor = Theme.AccentHi;
            _btnStart.LabelColor = Color.FromArgb(4, 12, 20);
            _btnStart.GlowColor = Theme.Accent;
            _btnStart.BorderColor = Theme.Accent;
            _btnStart.Size = new Size(200, 42);
            _btnStart.Anchor = AnchorStyles.None;
            _btnStart.Click += (_, _) => StartRecording();

            _btnStop.Text = "■ 停止";
            _btnStop.GlowColor = Theme.Fail;
            _btnStop.BorderColor = Color.FromArgb(90, 40, 60);
            _btnStop.Size = new Size(160, 42);
            _btnStop.Anchor = AnchorStyles.None;
            _btnStop.Enabled = false;
            _btnStop.Click += (_, _) => StopRecording(quiet: false);

            _btnOpen.Text = "◉ 開啟資料夾";
            _btnOpen.GlowColor = Theme.Violet;
            _btnOpen.BorderColor = Color.FromArgb(70, 45, 110);
            _btnOpen.Size = new Size(180, 42);
            _btnOpen.Anchor = AnchorStyles.None;
            _btnOpen.Click += (_, _) => OpenOutputFolder();

            actions.Controls.Add(_btnStart, 0, 0);
            actions.Controls.Add(_btnStop, 1, 0);
            actions.Controls.Add(_btnOpen, 2, 0);

            _log.Dock = DockStyle.Fill;
            _log.BackColor = Color.FromArgb(14, 16, 21);
            _log.ForeColor = Theme.TextDim;
            _log.Font = Theme.Mono(8.5f);
            _log.BorderStyle = BorderStyle.None;
            _log.ReadOnly = true;
            _log.WordWrap = false;

            var logCard = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                Radius = 14,
                Fill = Color.FromArgb(14, 16, 21),
                Stroke = Theme.Border,
                Padding = new Padding(12, 12, 10, 12),
            };
            logCard.Controls.Add(_log);

            _root.Controls.Add(logCard);
            _root.Controls.Add(actions);
            _root.Controls.Add(_live);
            _root.Controls.Add(_stats);
            _root.Controls.Add(hint);
            _root.Controls.Add(settings);

            Controls.Add(_root);
            Controls.Add(header);
        }

        private static Label Caption(string text) => new()
        {
            Text = text,
            ForeColor = Theme.TextDim,
            Font = Theme.UI(8.5f),
            AutoSize = true,
            BackColor = Color.Transparent,
        };

        private static RoundedPanel Wrap(Control inner, int width, int height = 36)
        {
            var host = new RoundedPanel { Width = width, Height = height };
            inner.Width = width - 22;
            inner.Location = new Point(11, Math.Max(0, (height - inner.Height) / 2));
            host.Controls.Add(inner);
            return host;
        }

        private void Log(string line, Color? color = null)
        {
            if (_log.IsDisposed) return;
            if (_log.InvokeRequired) { _log.BeginInvoke(new Action(() => Log(line, color))); return; }

            if (_log.Lines.Length > 300)
                _log.Text = string.Join(Environment.NewLine, _log.Lines[^200..]);

            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = color ?? Theme.TextDim;
            _log.AppendText($"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
            _log.SelectionColor = _log.ForeColor;
            _log.ScrollToCaret();
        }

        // ── 狀態更新與 F9 熱鍵 ──────────────────────────────────────

        private void RefreshStats()
        {
            // 輪詢 F9。用輪詢而不是註冊全域熱鍵，避免和遊戲搶按鍵。
            bool down = NativeWindows.IsDown(NativeWindows.VK_F9);
            if (down && !_hotkeyWasDown)
            {
                if (_recording) StopRecording(quiet: false); else StartRecording();
            }
            _hotkeyWasDown = down;

            if (!_recording)
            {
                _live.Text = "－";
                return;
            }

            TimeSpan elapsed = DateTime.Now - _startedAt;
            // 320px 寬的 JPEG 實測約 12KB，面積是寬度的平方關係
            double scale = (double)_width.Value / 320.0;
            double mb = _frames * 12.0 * scale * scale / 1024.0;
            _stats.Text = $"錄製中 · {_frames} 幀 · {elapsed:hh\\:mm\\:ss} · 略過 {_skipped} 幀（非前景）· 約 {mb:F0} MB";
            _live.Text = string.IsNullOrEmpty(_liveKeys) ? "（無輸入）" : _liveKeys;
        }

        // ── 錄製 ────────────────────────────────────────────────────

        private void StartRecording()
        {
            if (_recording) return;

            IntPtr hwnd = NativeWindows.Find("Roblox");
            if (hwnd == IntPtr.Zero)
            {
                Log("◈ 找不到 Roblox 視窗 — 請先開啟遊戲再開始錄製。", Theme.Fail);
                return;
            }

            try
            {
                string root = _outDir.Text.Trim();
                _sessionDir = Path.Combine(root, $"session_{DateTime.Now:yyyyMMdd_HHmmss}");
                Directory.CreateDirectory(Path.Combine(_sessionDir, "frames"));
            }
            catch (Exception ex)
            {
                Log($"◈ 無法建立輸出資料夾 — {ex.Message}", Theme.Fail);
                return;
            }

            _frames = 0;
            _skipped = 0;
            _startedAt = DateTime.Now;
            _recording = true;
            _cts = new CancellationTokenSource();

            _btnStart.Enabled = false;
            _btnStop.Enabled = true;
            _rate.Enabled = false;
            _width.Enabled = false;

            Log($"▸ 開始錄製 → {_sessionDir}", Theme.Ok);
            Log($"▸ {_rate.Value} Hz · 影像寬度 {_width.Value}px · 切到 Roblox 開始操作", Theme.TextDim);

            int hz = (int)_rate.Value;
            int width = (int)_width.Value;
            _ = Task.Run(() => RecordLoop(hwnd, hz, width, _sessionDir, _cts.Token));
        }

        private void StopRecording(bool quiet)
        {
            if (!_recording) return;
            _recording = false;
            try { _cts?.Cancel(); } catch { }

            if (quiet) return;

            TimeSpan elapsed = DateTime.Now - _startedAt;
            Log($"▸ 錄製結束 · {_frames} 幀 · {elapsed:hh\\:mm\\:ss}", Theme.Warn);
            if (_skipped > 0)
                Log($"▸ 其中略過 {_skipped} 次（Roblox 不是前景視窗）", Theme.TextDim);
            _stats.Text = $"已完成 · {_frames} 幀 · {elapsed:hh\\:mm\\:ss}";

            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
            _rate.Enabled = true;
            _width.Enabled = true;
        }

        private void RecordLoop(IntPtr hwnd, int hz, int targetWidth, string dir, CancellationToken token)
        {
            string framesDir = Path.Combine(dir, "frames");
            string actionsPath = Path.Combine(dir, "actions.jsonl");
            int periodMs = Math.Max(1, 1000 / hz);

            try
            {
                WriteMeta(dir, hz, targetWidth);

                using var actions = new StreamWriter(actionsPath, append: false, Encoding.UTF8);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int index = 0;

                while (!token.IsCancellationRequested)
                {
                    long tickStart = sw.ElapsedMilliseconds;

                    // 只在遊戲是前景時錄。否則畫面上是別的視窗，
                    // 那筆資料會教模型「看到桌面時要按 W」，是純粹的汙染。
                    if (NativeWindows.GetForegroundWindow() != hwnd)
                    {
                        _skipped++;
                        _liveKeys = "（Roblox 非前景，暫停中）";
                        Sleep(periodMs, tickStart, sw, token);
                        continue;
                    }

                    if (!NativeWindows.GetClientRect(hwnd, out var rect)) { Sleep(periodMs, tickStart, sw, token); continue; }
                    int cw = rect.Right - rect.Left, ch = rect.Bottom - rect.Top;
                    if (cw <= 0 || ch <= 0) { Sleep(periodMs, tickStart, sw, token); continue; }

                    var origin = new NativeWindows.POINT { X = 0, Y = 0 };
                    NativeWindows.ClientToScreen(hwnd, ref origin);

                    int th = Math.Max(1, (int)Math.Round(ch * (targetWidth / (double)cw)));
                    using (var full = new Bitmap(cw, ch, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(full))
                        {
                            g.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(cw, ch), CopyPixelOperation.SourceCopy);
                        }
                        using var small = new Bitmap(targetWidth, th, PixelFormat.Format24bppRgb);
                        using (var g2 = Graphics.FromImage(small))
                        {
                            g2.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g2.DrawImage(full, 0, 0, targetWidth, th);
                        }
                        small.Save(Path.Combine(framesDir, $"{index:D6}.jpg"), ImageFormat.Jpeg);
                    }

                    // 影像存好之後才讀輸入，兩者的時間差才最小
                    var held = new List<string>();
                    foreach (var (name, vk) in TrackedKeys)
                        if (NativeWindows.IsDown(vk)) held.Add(name);

                    double mx = 0.5, my = 0.5;
                    if (NativeWindows.GetCursorPos(out var cursor))
                    {
                        mx = Math.Clamp((cursor.X - origin.X) / (double)cw, 0d, 1d);
                        my = Math.Clamp((cursor.Y - origin.Y) / (double)ch, 0d, 1d);
                    }

                    actions.WriteLine(BuildActionLine(index, sw.Elapsed.TotalSeconds, held, mx, my));
                    actions.Flush();

                    _liveKeys = held.Count == 0 ? "（無輸入）" : string.Join("  ", held);
                    _frames = ++index;

                    Sleep(periodMs, tickStart, sw, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log($"◈ 錄製中斷 — {ex.Message}", Theme.Fail);
                BeginInvoke(new Action(() => StopRecording(quiet: false)));
            }
        }

        /// <summary>扣掉這一輪已經花掉的時間，讓取樣間隔盡量穩定。</summary>
        private static void Sleep(int periodMs, long tickStart, System.Diagnostics.Stopwatch sw, CancellationToken token)
        {
            int spent = (int)(sw.ElapsedMilliseconds - tickStart);
            int rest = periodMs - spent;
            if (rest > 0) token.WaitHandle.WaitOne(rest);
        }

        /// <summary>手寫 JSON。欄位就這幾個，為此拉一個序列化程式庫不划算。</summary>
        private static string BuildActionLine(int index, double t, List<string> held, double mx, double my)
        {
            var sb = new StringBuilder(160);
            sb.Append("{\"i\":").Append(index);
            sb.Append(",\"t\":").Append(t.ToString("F3", CultureInfo.InvariantCulture));
            sb.Append(",\"keys\":[");
            for (int i = 0; i < held.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(held[i]).Append('"');
            }
            sb.Append("],\"mx\":").Append(mx.ToString("F4", CultureInfo.InvariantCulture));
            sb.Append(",\"my\":").Append(my.ToString("F4", CultureInfo.InvariantCulture));
            sb.Append('}');
            return sb.ToString();
        }

        private void WriteMeta(string dir, int hz, int width)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"recorded_at\": \"{DateTime.Now:yyyy-MM-ddTHH:mm:ss}\",");
            sb.AppendLine($"  \"sample_rate_hz\": {hz},");
            sb.AppendLine($"  \"frame_width\": {width},");
            sb.Append("  \"tracked_keys\": [");
            for (int i = 0; i < TrackedKeys.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('"').Append(TrackedKeys[i].Name).Append('"');
            }
            sb.AppendLine("],");
            sb.AppendLine("  \"mouse\": \"mx/my 為游標在客戶區內的相對座標 0..1\",");
            sb.AppendLine("  \"note\": \"僅在遊戲視窗為前景時取樣；非前景的時段直接略過而不是補空白\"");
            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(dir, "meta.json"), sb.ToString(), Encoding.UTF8);
        }

        private void OpenOutputFolder()
        {
            string dir = string.IsNullOrEmpty(_sessionDir) ? _outDir.Text.Trim() : _sessionDir;
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
            }
            catch (Exception ex) { Log($"◈ 無法開啟資料夾 — {ex.Message}", Theme.Fail); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _uiTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
