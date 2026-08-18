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
        public int Radius { get; set; } = 8;

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

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = Enabled
                ? Theme.Lerp(BaseColor, HoverColor, _hover)
                : Color.FromArgb(30, 33, 42);
            Color label = Enabled ? LabelColor : Theme.TextDim;

            using (var path = Theme.RoundedRect(rect, Radius))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
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

            var titleRect = new Rectangle(26, 0, 200, Height);
            TextRenderer.DrawText(g, Title, Theme.FontStep, titleRect,
                _state == StepState.Pending ? Theme.TextDim : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            var detailRect = new Rectangle(230, 0, Width - 238, Height);
            TextRenderer.DrawText(g, _detail, Theme.FontDetail, detailRect,
                _state == StepState.Fail ? Theme.Fail : Theme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
