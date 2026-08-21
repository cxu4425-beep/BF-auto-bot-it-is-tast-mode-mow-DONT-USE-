using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
// WinForms 專案的 ImplicitUsings 會自動引入 System.Threading，
// 它和 System.Windows.Forms 都有 Timer，不指定就是 CS0104 模稜兩可。
// 用別名一次講清楚，底下就能照常寫 Timer。
using Timer = System.Windows.Forms.Timer;

namespace BloxFruitsLauncher
{
    /// <summary>自繪圓角按鈕。hover 用內插做漸變，不是瞬間跳色。</summary>
    internal sealed class FlatButton : Control
    {
        private float _hover;          // 0 = 未停留, 1 = 完全停留
        private bool _pointerInside;
        private readonly Timer _anim;

        public Color BaseColor { get; set; } = Theme.SurfaceHi;
        public Color HoverColor { get; set; } = Theme.Border;
        public Color LabelColor { get; set; } = Theme.Text;
        public Color GlowColor { get; set; } = Color.Empty;
        public Color BorderColor { get; set; } = Theme.Border;
        public int Radius { get; set; } = 18;   // Android 風格的大圓角

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.UserPaint
                   | ControlStyles.SupportsTransparentBackColor, true);
            Font = Theme.UI(10f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            _anim = new Timer { Interval = 16 };
            _anim.Tick += (_, _) =>
            {
                float target = _pointerInside && Enabled ? 1f : 0f;
                float step = 0.18f;
                if (Math.Abs(_hover - target) < 0.01f)
                {
                    _hover = target;
                    _anim.Stop();
                }
                else
                {
                    _hover += (target - _hover) * step;
                }
                Invalidate();
            };
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _pointerInside = true;
            _anim.Start();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _pointerInside = false;
            _anim.Start();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var rect = new Rectangle(4, 4, Width - 9, Height - 9);
            Color fill = Enabled
                ? Theme.Lerp(BaseColor, HoverColor, _hover)
                : Color.FromArgb(14, 20, 34);
            Color label = Enabled ? LabelColor : Theme.TextDim;

            // 停留時輝光加強，是這個介面主要的「科幻感」來源
            if (Enabled && GlowColor != Color.Empty)
            {
                Theme.DrawGlow(g, rect, Radius, GlowColor, 4, 0.5f + _hover * 1.4f);
            }

            using (var path = Theme.RoundedRect(rect, Radius))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
                using var edge = new Pen(Enabled
                    ? Theme.Lerp(BorderColor, Theme.Lerp(BorderColor, Color.White, 0.35f), _hover)
                    : Theme.Border, 1.2f);
                g.DrawPath(edge, path);
            }

            TextRenderer.DrawText(g, Text, Font, rect, label,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _anim.Dispose();
            base.Dispose(disposing);
        }
    }

    internal enum StepState { Pending, Running, Ok, Fail }

    /// <summary>一列啟動步驟：狀態圓點 + 名稱 + 說明。執行中的圓點會呼吸。</summary>
    internal sealed class StepRow : Control
    {
        private StepState _state = StepState.Pending;
        private string _detail = "";
        private float _pulse;          // 0..1，執行中才會動

        public string Title { get; set; } = "";

        public StepState State
        {
            get => _state;
            set { _state = value; Invalidate(); }
        }

        public string Detail
        {
            get => _detail;
            set { _detail = value ?? ""; Invalidate(); }
        }

        /// <summary>由外部的單一計時器驅動，避免每列各開一個 Timer。</summary>
        public void Tick(float phase)
        {
            if (_state != StepState.Running) return;
            _pulse = phase;
            Invalidate();
        }

        public StepRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.UserPaint, true);
            Height = 34;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color dot = _state switch
            {
                StepState.Ok => Theme.Ok,
                StepState.Fail => Theme.Fail,
                StepState.Running => Theme.Accent,
                _ => Theme.Border,
            };

            int cy = Height / 2;

            // 執行中：外圈隨 pulse 擴散淡出，做出呼吸感
            if (_state == StepState.Running)
            {
                int halo = (int)(6 + _pulse * 7);
                int alpha = (int)(90 * (1f - _pulse));
                using var haloBrush = new SolidBrush(Color.FromArgb(alpha, dot));
                g.FillEllipse(haloBrush, 10 - halo / 2, cy - halo / 2, halo, halo);
            }

            using (var dotBrush = new SolidBrush(dot))
            {
                g.FillEllipse(dotBrush, 6, cy - 4, 9, 9);
            }

            // 完成時在圓點上打勾
            if (_state == StepState.Ok)
            {
                using var pen = new Pen(Theme.Background, 1.8f);
                g.DrawLines(pen, new[]
                {
                    new PointF(8.2f, cy),
                    new PointF(10.1f, cy + 2.1f),
                    new PointF(13.2f, cy - 2.4f),
                });
            }

            // 標題欄寬度隨控制項寬度算，寫死 200px 會把較長的標題切掉
            int titleW = Math.Max(170, Math.Min(380, (int)(Width * 0.42f)));
            var titleRect = new Rectangle(26, 0, titleW, Height);
            TextRenderer.DrawText(g, Title, Theme.FontStep, titleRect,
                _state == StepState.Pending ? Theme.TextDim : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            int detailX = 26 + titleW + 14;
            var detailRect = new Rectangle(detailX, 0, Math.Max(0, Width - detailX - 8), Height);
            TextRenderer.DrawText(g, _detail, Theme.FontDetail, detailRect,
                _state == StepState.Fail ? Theme.Fail : Theme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }


    /// <summary>
    /// 圓角容器。WinForms 的 ComboBox / NumericUpDown 沒辦法直接做圓角，
    /// 所以把它們塞進這個自繪的圓角面板裡，外框由面板畫、內層控制項無邊框，
    /// 看起來就像 Android 的填色輸入框。
    /// </summary>
    internal sealed class RoundedPanel : Panel
    {
        public int Radius { get; set; } = 14;
        public Color Fill { get; set; } = Theme.SurfaceHi;
        public Color Stroke { get; set; } = Color.Empty;

        public RoundedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.UserPaint
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = Theme.RoundedRect(rect, Radius);
            using (var brush = new SolidBrush(Fill))
            {
                g.FillPath(brush, path);
            }
            if (Stroke != Color.Empty)
            {
                using var pen = new Pen(Stroke, 1f);
                g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>
    /// 自繪勾選框：圓角方塊 + 勾，切換時有短暫的過渡動畫。
    /// 內建的 CheckBox 是系統繪製，配不上深色圓角的其他控制項。
    /// </summary>
    internal sealed class RoundedCheckBox : Control
    {
        private bool _checked;
        private float _t;          // 0 = 未勾, 1 = 已勾
        private readonly Timer _anim;

        public event EventHandler? CheckedChanged;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                _anim.Start();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }

        public RoundedCheckBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.UserPaint
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Height = 26;

            _anim = new Timer { Interval = 16 };
            _anim.Tick += (_, _) =>
            {
                float target = _checked ? 1f : 0f;
                if (Math.Abs(_t - target) < 0.02f)
                {
                    _t = target;
                    _anim.Stop();
                }
                else
                {
                    _t += (target - _t) * 0.25f;
                }
                Invalidate();
            };
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            const int box = 19;
            int top = (Height - box) / 2;
            var boxRect = new Rectangle(0, top, box, box);

            using (var path = Theme.RoundedRect(boxRect, 6))
            {
                using var fill = new SolidBrush(Theme.Lerp(Theme.SurfaceHi, Theme.Accent, _t));
                g.FillPath(fill, path);
                if (_t < 0.5f)
                {
                    using var pen = new Pen(Theme.Border, 1.4f);
                    g.DrawPath(pen, path);
                }
            }

            if (_t > 0.05f)
            {
                using var pen = new Pen(Color.FromArgb((int)(255 * _t), 12, 18, 28), 2.2f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };
                float cx = boxRect.X, cy = boxRect.Y;
                g.DrawLines(pen, new[]
                {
                    new PointF(cx + 4.5f, cy + 9.5f),
                    new PointF(cx + 8.0f, cy + 13.0f),
                    new PointF(cx + 14.5f, cy + 5.5f),
                });
            }

            var textRect = new Rectangle(box + 10, 0, Width - box - 10, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _anim.Dispose();
            base.Dispose(disposing);
        }
    }



    /// <summary>
    /// 深空背景：垂直漸層 + 淡網格 + 頂部光暈。
    /// 畫在最底層的容器上，其餘面板設成透明就會透出來。
    /// </summary>
    internal sealed class BackdropPanel : Panel
    {
        public BackdropPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw
                   | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (Width <= 0 || Height <= 0) return;

            var full = new Rectangle(0, 0, Width, Height);

            using (var grad = new LinearGradientBrush(full, Theme.Background, Theme.BackgroundDeep, 90f))
            {
                g.FillRectangle(grad, full);
            }

            // 淡網格。間距夠大才不會變成雜訊
            using (var pen = new Pen(Theme.Grid, 1f))
            {
                for (int x = 0; x < Width; x += 46) g.DrawLine(pen, x, 0, x, Height);
                for (int y = 0; y < Height; y += 46) g.DrawLine(pen, 0, y, Width, y);
            }

            // 頂部中央的青色光暈，讓視線往標題集中
            int glowW = Math.Max(Width, 400);
            var glowRect = new Rectangle(Width / 2 - glowW / 2, -glowW / 3, glowW, glowW / 2);
            using var path = new GraphicsPath();
            path.AddEllipse(glowRect);
            using var brush = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(46, Theme.Accent),
                SurroundColors = new[] { Color.FromArgb(0, Theme.Accent) },
            };
            g.FillEllipse(brush, glowRect);
        }
    }

}
