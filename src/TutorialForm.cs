using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace GammaLift
{
    /// <summary>「使用教程」对话框：精简上手步骤，完整教程在 GitHub。</summary>
    public class TutorialForm : Form
    {
        private const int DesignWidth = 470;      // 96dpi、界面缩放 100% 时的客户区宽度
        private const int DesignMargin = 16;
        private const int DesignPad = 18;

        // 简体中文原文；其它语言在 Lang*.cs 里按同一 key 提供译文。
        private const string BodyText =
            "• 选显示器，拖「亮度 / 对比度 / 伽马」，右侧曲线实时变化。\r\n" +
            "• 提亮暗部先调伽马；暗场景里加对比度会把画面压得更黑。\r\n" +
            "• 调好后「保存」为预设，可绑全局热键；托盘菜单也能切换。\r\n" +
            "• 右上角「复制 / 导入分享码」交换当前显示器的参数。\r\n" +
            "• 「重置」回到系统 ICC 校准。";

        private readonly float _uiScale;
        private readonly float _dpiScale;
        private readonly Font _titleFont;
        private readonly Font _headingFont;
        private readonly Font _bodyFont;

        private Label lblTitle, lblHeading, lblBody;
        private LinkLabel lnkGuide;
        private Button btnClose;

        public TutorialForm(float uiScale, float dpiScale)
        {
            _uiScale = uiScale > 0f ? uiScale : 1f;
            _dpiScale = dpiScale > 0f ? dpiScale : 1f;

            Text = Loc.T("使用教程");
            Font = Loc.UiFont(9f * _uiScale);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(DesignWidth), S(320));   // LayoutTutorial 会按实际内容重算

            _titleFont = new Font(Font.FontFamily, 15f * _uiScale, FontStyle.Bold);
            _headingFont = new Font(Font, FontStyle.Bold);
            _bodyFont = Loc.UiFont(9.5f * _uiScale);

            BuildUi();
            LayoutTutorial();
            ActiveControl = btnClose;
            Shown += delegate { btnClose.Focus(); };
        }

        /// <summary>96dpi 设计值 × 界面缩放 × 系统 DPI。</summary>
        private int S(int v)
        {
            return (int)Math.Round(v * _uiScale * _dpiScale);
        }

        private void BuildUi()
        {
            lblTitle = NewLabel(Loc.T("使用教程"));
            lblTitle.Font = _titleFont;
            Controls.Add(lblTitle);

            lblHeading = NewLabel(Loc.T("快速上手"));
            lblHeading.Font = _headingFont;
            Controls.Add(lblHeading);

            lblBody = NewLabel(Loc.T(BodyText));
            lblBody.Font = _bodyFont;
            Controls.Add(lblBody);

            lnkGuide = new LinkLabel();
            lnkGuide.Text = Loc.T("查看完整教程（GitHub）");
            lnkGuide.AutoSize = false;
            lnkGuide.LinkBehavior = LinkBehavior.HoverUnderline;
            lnkGuide.LinkClicked += delegate { OpenGuide(); };
            lnkGuide.AccessibleName = lnkGuide.Text;
            Controls.Add(lnkGuide);

            btnClose = new Button();
            btnClose.Text = Loc.T("关闭");
            btnClose.DialogResult = DialogResult.OK;
            Controls.Add(btnClose);
            AcceptButton = btnClose;
            CancelButton = btnClose;
        }

        private Label NewLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            return l;
        }

        private void LayoutTutorial()
        {
            Rectangle wa = WorkingArea();
            int margin = S(DesignMargin);
            int pad = S(DesignPad);
            int clientW = Math.Min(S(DesignWidth), Math.Max(S(320), wa.Width - S(48)));
            int contentW = clientW - margin * 2;

            int titleH = MeasureTextHeight(lblTitle.Text, lblTitle.Font, contentW);
            int headingH = MeasureTextHeight(lblHeading.Text, lblHeading.Font, contentW);
            int bodyH = MeasureTextHeight(lblBody.Text, lblBody.Font, contentW);
            int linkH = Math.Max(S(20), lnkGuide.Font.Height + S(4));
            int btnH = Math.Max(S(30), Font.Height + S(12));

            int y = pad;
            lblTitle.SetBounds(margin, y, contentW, titleH);
            y += titleH + S(18);
            lblHeading.SetBounds(margin, y, contentW, headingH);
            y += headingH + S(8);
            lblBody.SetBounds(margin, y, contentW, bodyH);
            y += bodyH + S(20);
            lnkGuide.SetBounds(margin, y, contentW, linkH);
            y += linkH + S(16);

            int btnW = ButtonWidth(btnClose, 80, 24);
            int totalH = y + btnH + pad;
            int maxH = Math.Max(S(240), wa.Height - S(60));
            ClientSize = new Size(clientW, Math.Min(totalH, maxH));
            btnClose.SetBounds(clientW - margin - btnW, ClientSize.Height - pad - btnH, btnW, btnH);
        }

        private int MeasureTextHeight(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            Size sz = TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), 0),
                TextFormatFlags.WordBreak);
            return sz.Height + S(2);
        }

        /// <summary>按实际文字量按钮宽度（PreferredSize 依赖 AutoSize, 取不到稳定值）。</summary>
        private int ButtonWidth(Button b, int minWidth, int padding)
        {
            Size sz = TextRenderer.MeasureText(b.Text, b.Font);
            return Math.Max(S(minWidth), sz.Width + S(padding));
        }

        private Rectangle WorkingArea()
        {
            try
            {
                return Screen.FromPoint(Cursor.Position).WorkingArea;
            }
            catch (Exception)
            {
                return Screen.PrimaryScreen.WorkingArea;
            }
        }

        private void OpenGuide()
        {
            try
            {
                Process.Start(AppInfo.GuideUrl);
            }
            catch (Exception)
            {
                MessageBox.Show(this, Loc.F("无法打开浏览器，请手动访问：\r\n{0}", AppInfo.GuideUrl),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _titleFont.Dispose();
                _headingFont.Dispose();
                _bodyFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
