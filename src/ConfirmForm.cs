using System;
using System.Drawing;
using System.Windows.Forms;

namespace GammaLift
{
    /// <summary>通用二选一确认框（目前用于语言切换后的重启提示）。
    /// 文案由调用方按目标语言传入, 按钮固定英文 Yes / No ——
    /// 系统 MessageBox 的按钮跟随 Windows 显示语言, 换过语言的用户反而认不出"是/否"。</summary>
    public class ConfirmForm : Form
    {
        private const int DesignWidth = 400;
        private const int DesignIcon = 32;

        private readonly float _uiScale;
        private readonly float _dpiScale;
        private readonly string _message;

        private PictureBox picIcon;
        private Label lblMessage;
        private Button btnYes;
        private Button btnNo;

        public ConfirmForm(string message, string fontCode, float uiScale, float dpiScale)
        {
            _message = message ?? "";
            _uiScale = uiScale > 0f ? uiScale : 1f;
            _dpiScale = dpiScale > 0f ? dpiScale : 1f;

            Text = "GammaLift";
            Font = Loc.UiFont(fontCode, 9f * _uiScale);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(DesignWidth), S(140));

            BuildUi();
            LayoutForm();
            AcceptButton = btnYes;
            CancelButton = btnNo;
            ActiveControl = btnYes;
        }

        /// <summary>96dpi 设计值 × 界面缩放 × 系统 DPI。</summary>
        private int S(int v)
        {
            return (int)Math.Round(v * _uiScale * _dpiScale);
        }

        private void BuildUi()
        {
            picIcon = new PictureBox();
            picIcon.SizeMode = PictureBoxSizeMode.Zoom;
            picIcon.Image = AppIcon.Load(S(DesignIcon)).ToBitmap();
            Controls.Add(picIcon);

            lblMessage = new Label();
            lblMessage.Text = _message;
            lblMessage.AutoSize = false;
            lblMessage.UseMnemonic = false;
            Controls.Add(lblMessage);

            btnYes = new Button();
            btnYes.Text = "Yes";
            btnYes.DialogResult = DialogResult.Yes;
            Controls.Add(btnYes);

            btnNo = new Button();
            btnNo.Text = "No";
            btnNo.DialogResult = DialogResult.No;
            Controls.Add(btnNo);
        }

        private void LayoutForm()
        {
            int margin = S(16);
            int gap = S(12);
            int iconSize = S(DesignIcon);

            picIcon.SetBounds(margin, margin, iconSize, iconSize);

            int textX = margin + iconSize + gap;
            int textW = Math.Max(S(160), ClientSize.Width - textX - margin);
            Size textSize = TextRenderer.MeasureText(_message, Font, new Size(textW, int.MaxValue),
                TextFormatFlags.WordBreak);
            int textH = Math.Max(S(22), textSize.Height + S(2));
            lblMessage.SetBounds(textX, margin, textW, textH);

            int btnH = S(26);
            int btnW = ButtonWidth(btnYes, 84, 18);
            int by = margin + Math.Max(iconSize, textH) + S(16);

            ClientSize = new Size(ClientSize.Width, by + btnH + margin);

            // 与 Windows MessageBox 一致: Yes 在左、No 在右
            btnNo.SetBounds(ClientSize.Width - margin - btnW, by, btnW, btnH);
            btnYes.SetBounds(btnNo.Left - S(8) - btnW, by, btnW, btnH);
        }

        private int ButtonWidth(Button b, int minWidth, int padding)
        {
            Size sz = TextRenderer.MeasureText(b.Text, b.Font);
            int w = sz.Width + S(padding);
            int min = S(minWidth);
            return w < min ? min : w;
        }
    }
}
