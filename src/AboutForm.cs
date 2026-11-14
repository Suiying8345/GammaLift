using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace GammaLift
{
    /// <summary>「关于」对话框：版本号、两行版权、MIT 许可证全文与项目仓库链接。</summary>
    public class AboutForm : Form
    {
        private const int DesignWidth = 470;      // 96dpi、界面缩放 100% 时的客户区宽度
        private const int DesignMargin = 12;
        private const int DesignPad = 16;

        private readonly float _uiScale;
        private readonly float _dpiScale;
        private readonly Font _titleFont;
        private readonly Font _captionFont;
        private readonly Font _licenseFont;

        private PictureBox picIcon;
        private Label lblTitle, lblVersion, lblLicenseCaption, lblRepoCaption;
        private TextBox txtLicense;
        private LinkLabel lnkRepo;
        private Button btnClose;

        public AboutForm(float uiScale, float dpiScale)
        {
            _uiScale = uiScale > 0f ? uiScale : 1f;
            _dpiScale = dpiScale > 0f ? dpiScale : 1f;

            Text = Loc.T("关于 GammaLift");
            Font = Loc.UiFont(9f * _uiScale);
            AutoScaleMode = AutoScaleMode.None;      // 坐标全部由 S() 算出, 与 AutoScaleMode=None 配合, 不叠加系统缩放
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(DesignWidth), S(440));   // LayoutAbout 会按实际内容重算

            _titleFont = new Font(Font.FontFamily, 16f * _uiScale, FontStyle.Bold);
            _captionFont = new Font(Font, FontStyle.Bold);
            _licenseFont = Loc.UiFont(8.5f * _uiScale);

            BuildUi();
            LayoutAbout();
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
            picIcon = new PictureBox();
            picIcon.SizeMode = PictureBoxSizeMode.Zoom;
            picIcon.Image = AppIcon.Load(S(48)).ToBitmap();
            Controls.Add(picIcon);

            lblTitle = NewLabel("GammaLift");
            lblTitle.Font = _titleFont;
            Controls.Add(lblTitle);

            lblVersion = NewLabel(Loc.F("版本 {0}", AppInfo.Version));
            lblVersion.ForeColor = SystemColors.GrayText;
            Controls.Add(lblVersion);

            lblLicenseCaption = NewLabel(Loc.T("许可证"));
            lblLicenseCaption.Font = _captionFont;
            Controls.Add(lblLicenseCaption);

            txtLicense = new TextBox();
            txtLicense.Multiline = true;
            txtLicense.ReadOnly = true;
            txtLicense.WordWrap = true;
            txtLicense.ScrollBars = ScrollBars.Vertical;
            txtLicense.BorderStyle = BorderStyle.FixedSingle;
            txtLicense.BackColor = SystemColors.Window;
            txtLicense.Font = _licenseFont;
            txtLicense.Text = AppInfo.LicenseText;
            txtLicense.AccessibleName = Loc.T("许可证");
            txtLicense.TabStop = false;   // 不让许可证框在打开时抢焦点并全选文字
            Controls.Add(txtLicense);

            lblRepoCaption = NewLabel(Loc.T("项目仓库"));
            lblRepoCaption.Font = _captionFont;
            Controls.Add(lblRepoCaption);

            lnkRepo = new LinkLabel();
            lnkRepo.Text = AppInfo.RepositoryUrl;
            lnkRepo.AutoSize = false;
            lnkRepo.LinkBehavior = LinkBehavior.HoverUnderline;
            lnkRepo.LinkClicked += delegate { OpenRepository(); };
            Controls.Add(lnkRepo);

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

        // ====================================================================
        //  布局
        // ====================================================================

        private void LayoutAbout()
        {
            Rectangle wa = WorkingArea();
            int margin = S(DesignMargin);
            int pad = S(DesignPad);
            int clientW = Math.Min(S(DesignWidth), Math.Max(S(320), wa.Width - S(48)));
            int contentW = clientW - margin * 2;

            int iconSize = S(48);
            int textW = contentW - iconSize - S(12);
            int titleH = MeasureTextHeight(lblTitle.Text, lblTitle.Font, textW);
            int versionH = MeasureTextHeight(lblVersion.Text, lblVersion.Font, textW);
            int blockH = Math.Max(iconSize, titleH + versionH + S(2));

            int y = pad;
            picIcon.SetBounds(margin, y + (blockH - iconSize) / 2, iconSize, iconSize);
            int textX = margin + iconSize + S(12);
            lblTitle.SetBounds(textX, y + (blockH - titleH - versionH - S(2)) / 2, textW, titleH);
            lblVersion.SetBounds(textX, lblTitle.Bottom + S(2), textW, versionH);
            y += blockH + S(16);

            int licenseCapH = MeasureTextHeight(lblLicenseCaption.Text, lblLicenseCaption.Font, contentW);
            lblLicenseCaption.SetBounds(margin, y, contentW, licenseCapH);
            y += licenseCapH + S(6);

            int btnH = Math.Max(S(30), Font.Height + S(12));
            int repoRowH = Math.Max(S(18), lnkRepo.Font.Height + S(4));
            int bottomH = btnH + S(12);
            int licenseH = S(170);
            int licenseMinH = S(110);
            int fixedAfterLicense = S(12) + repoRowH + S(16) + bottomH + pad;
            int maxH = Math.Max(S(300), wa.Height - S(60));
            int totalH = y + licenseH + fixedAfterLicense;
            if (totalH > maxH)
            {
                licenseH = Math.Max(licenseMinH, licenseH - (totalH - maxH));
                totalH = y + licenseH + fixedAfterLicense;
            }
            ClientSize = new Size(clientW, totalH);

            txtLicense.SetBounds(margin, y, contentW, licenseH);
            y += licenseH + S(12);

            int repoCapW = TextRenderer.MeasureText(lblRepoCaption.Text, lblRepoCaption.Font).Width + S(2);
            int repoCapH = Math.Max(S(18), lblRepoCaption.Font.Height);
            lblRepoCaption.SetBounds(margin, y + (repoRowH - repoCapH) / 2, repoCapW, repoCapH);
            int linkX = margin + repoCapW + S(10);
            int linkH = Math.Max(S(18), lnkRepo.Font.Height + S(2));
            lnkRepo.SetBounds(linkX, y + (repoRowH - linkH) / 2, clientW - margin - linkX, linkH);

            int btnW = ButtonWidth(btnClose, 80, 24);
            btnClose.SetBounds(clientW - margin - btnW, totalH - pad - btnH, btnW, btnH);
        }

        private int MeasureTextHeight(string text, int width)
        {
            return MeasureTextHeight(text, Font, width);
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

        private void OpenRepository()
        {
            try
            {
                Process.Start(AppInfo.RepositoryUrl);
            }
            catch (Exception)
            {
                MessageBox.Show(this, Loc.F("无法打开浏览器，请手动访问：\r\n{0}", AppInfo.RepositoryUrl),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (picIcon != null && picIcon.Image != null)
                {
                    picIcon.Image.Dispose();
                    picIcon.Image = null;
                }
                _titleFont.Dispose();
                _captionFont.Dispose();
                _licenseFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
