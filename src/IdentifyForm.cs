using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GammaLift
{
    /// <summary>在每台显示器中央闪现编号，方便用户对应列表项和实体屏幕。</summary>
    public class IdentifyForm : Form
    {
        public IdentifyForm(string text, Rectangle monitorBounds, float scale)
        {
            if (scale <= 0f) scale = 1f;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.85;
            Size = new Size((int)Math.Round(260 * scale), (int)Math.Round(170 * scale));
            Location = new Point(
                monitorBounds.X + (monitorBounds.Width - Width) / 2,
                monitorBounds.Y + (monitorBounds.Height - Height) / 2);

            Label lbl = new Label();
            lbl.Dock = DockStyle.Fill;
            lbl.TextAlign = ContentAlignment.MiddleCenter;
            lbl.ForeColor = Color.White;
            lbl.Font = new Font("Segoe UI", 64f * scale, FontStyle.Bold);
            lbl.Text = text;
            Controls.Add(lbl);

            Timer t = new Timer();
            t.Interval = 1800;
            t.Tick += delegate(object s, EventArgs e)
            {
                t.Stop();
                t.Dispose();
                Close();
            };
            t.Start();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public static void ShowAll(List<DisplayMonitor> monitors, float scale)
        {
            for (int i = 0; i < monitors.Count; i++)
            {
                string num = monitors[i].DeviceName.Replace("\\\\.\\DISPLAY", "");
                IdentifyForm f = new IdentifyForm(num, monitors[i].Bounds, scale);
                f.Show();
            }
        }
    }
}
