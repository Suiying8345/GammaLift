using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GammaLift
{
    /// <summary>自绘的置顶提示窗（替代系统气泡通知）。
    /// 便携单文件应用没有开始菜单快捷方式与 AppUserModelID, Windows 10/11 把 Shell_NotifyIcon
    /// 气泡转成 toast 时会把应用图标归到通用图标或空白; 自己画一个反而能保证图标、字号、
    /// 位置在所有系统上一致, 也不会抢焦点。单实例复用, 连续触发只更新文字。</summary>
    internal sealed class ToastForm : Form
    {
        private const int ShowMs = 1700;

        private static ToastForm _current;

        private readonly Timer _timer;
        private readonly float _uiScale;
        private readonly float _dpiScale;
        private readonly Icon _icon;
        private string _text = "";
        private DateTime _hideAt;
        private bool _fading;

        private ToastForm(float uiScale, float dpiScale)
        {
            _uiScale = uiScale > 0f ? uiScale : 1f;
            _dpiScale = dpiScale > 0f ? dpiScale : 1f;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(76, 76, 78);
            Opacity = 0.94;
            Font = Loc.UiFont(10f * _uiScale);
            DoubleBuffered = true;

            _icon = AppIcon.Load(S(32));

            _timer = new Timer();
            _timer.Interval = 60;
            _timer.Tick += delegate { OnTick(); };

            Click += delegate { HideNow(); };
        }

        /// <summary>显示一条提示；已显示时只更新文字并重置计时器。</summary>
        public static void ShowMessage(string text, float uiScale, float dpiScale)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                ToastForm f = _current;
                if (f != null && !f.IsDisposed &&
                    (Math.Abs(f._uiScale - uiScale) > 0.0001f || Math.Abs(f._dpiScale - dpiScale) > 0.0001f))
                {
                    f.DisposeCurrent();
                    f = null;
                }
                if (f == null || f.IsDisposed)
                    _current = f = new ToastForm(uiScale, dpiScale);

                f.ShowMessageCore(text);
            }
            catch (Exception)
            {
                // 提示失败不影响主流程
            }
        }

        /// <summary>退出程序时调用：彻底销毁提示窗。</summary>
        public static void CloseCurrent()
        {
            ToastForm f = _current;
            _current = null;
            if (f == null) return;
            try { f.DisposeCurrent(); }
            catch (Exception) { }
        }

        private int S(int v)
        {
            return (int)Math.Round(v * _uiScale * _dpiScale);
        }

        /// <summary>不抢前台焦点, 也不出现在 Alt-Tab / 任务栏。</summary>
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        private void ShowMessageCore(string text)
        {
            _text = text;

            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int padX = S(18);
            int padY = S(12);
            int gap = S(12);
            int iconSize = S(32);
            int maxTextW = Math.Max(S(120), Math.Min(S(620), wa.Width - S(120)));

            Size textSize = TextRenderer.MeasureText(text, Font, new Size(maxTextW, 0),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

            int w = padX * 2 + iconSize + gap + textSize.Width;
            int h = Math.Max(iconSize, textSize.Height) + padY * 2;
            int maxW = Math.Max(S(200), wa.Width - S(40));
            if (w > maxW) w = maxW;
            if (h > wa.Height - S(40)) h = Math.Max(S(40), wa.Height - S(40));

            ClientSize = new Size(w, h);
            UpdateRegion();
            // 放在鼠标所在屏幕的底部居中, 不压住任务栏（WorkingArea 已排除任务栏）
            Location = new Point(wa.Left + (wa.Width - w) / 2, wa.Bottom - h - S(56));

            _fading = false;
            Opacity = 0.94;
            _hideAt = DateTime.UtcNow.AddMilliseconds(ShowMs);
            if (!Visible) Show();
            Invalidate();
            _timer.Stop();
            _timer.Start();
        }

        private void OnTick()
        {
            if (!_fading)
            {
                if (DateTime.UtcNow < _hideAt) return;
                _fading = true;
            }

            double next = Opacity - 0.18;
            if (next <= 0.02)
            {
                HideNow();
                return;
            }
            Opacity = next;
        }

        private void HideNow()
        {
            _timer.Stop();
            _fading = false;
            Opacity = 0.94;
            Hide();
        }

        private void DisposeCurrent()
        {
            _timer.Stop();
            _timer.Dispose();
            if (!IsDisposed) Dispose();
        }

        private void UpdateRegion()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;
            Region old = Region;
            using (GraphicsPath path = RoundRect(new Rectangle(0, 0, w, h), S(10)))
                Region = new Region(path);
            if (old != null) old.Dispose();
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            using (SolidBrush brush = new SolidBrush(BackColor))
                g.FillRectangle(brush, ClientRectangle);

            int padX = S(18);
            int gap = S(12);
            int iconSize = S(32);
            if (_icon != null)
            {
                try
                {
                    g.DrawIcon(_icon, new Rectangle(padX, (ClientSize.Height - iconSize) / 2, iconSize, iconSize));
                }
                catch (Exception) { }
            }

            int tx = padX + iconSize + gap;
            Rectangle rect = new Rectangle(tx, 0, Math.Max(0, ClientSize.Width - tx - padX), ClientSize.Height);
            TextRenderer.DrawText(g, _text, Font, rect, Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }
}
