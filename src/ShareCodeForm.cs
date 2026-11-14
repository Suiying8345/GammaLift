using System;
using System.Drawing;
using System.Windows.Forms;

namespace GammaLift
{
    /// <summary>导入分享码对话框：粘贴 GL2 码, 校验通过后返回单屏参数。
    /// 只做解析与错误提示, 应用到显示器由主窗体负责。</summary>
    public class ShareCodeForm : Form
    {
        private const int DesignWidth = 420;
        private const int DesignHeight = 160;

        private readonly float _uiScale;
        private readonly float _dpiScale;

        private Label lblPrompt;
        private Label lblHint;
        private Label lblError;
        private TextBox txtCode;
        private Button btnImport;
        private Button btnCancel;

        /// <summary>校验通过后的单屏参数; 仅在 DialogResult 为 OK 时有效。</summary>
        public MonitorConfig Config { get; private set; }

        public ShareCodeForm(float uiScale, float dpiScale)
        {
            _uiScale = uiScale > 0f ? uiScale : 1f;
            _dpiScale = dpiScale > 0f ? dpiScale : 1f;

            Text = Loc.T("导入分享码");
            Font = Loc.UiFont(9f * _uiScale);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(DesignWidth), S(DesignHeight));

            BuildUi();
            LayoutForm();
            ActiveControl = txtCode;
            Shown += delegate { txtCode.Focus(); };
        }

        /// <summary>96dpi 设计值 × 界面缩放 × 系统 DPI。</summary>
        private int S(int v)
        {
            return (int)Math.Round(v * _uiScale * _dpiScale);
        }

        private void BuildUi()
        {
            lblPrompt = new Label();
            lblPrompt.Text = Loc.T("粘贴分享码：");
            lblPrompt.AutoSize = true;
            Controls.Add(lblPrompt);

            txtCode = new TextBox();
            txtCode.MaxLength = 64;
            txtCode.TextChanged += delegate { lblError.Text = ""; };
            Controls.Add(txtCode);

            lblHint = new Label();
            lblHint.Text = Loc.T("只包含当前显示器的亮度、对比度与伽马。");
            lblHint.AutoSize = true;
            lblHint.ForeColor = SystemColors.GrayText;
            lblHint.Font = Loc.UiFont(8.5f * _uiScale);
            Controls.Add(lblHint);

            lblError = new Label();
            lblError.Text = "";
            lblError.AutoSize = false;
            lblError.ForeColor = Color.Firebrick;
            Controls.Add(lblError);

            btnImport = new Button();
            btnImport.Text = Loc.T("导入");
            btnImport.Click += delegate { TryImport(); };
            Controls.Add(btnImport);

            btnCancel = new Button();
            btnCancel.Text = Loc.T("取消");
            btnCancel.DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);

            AcceptButton = btnImport;
            CancelButton = btnCancel;
        }

        private void LayoutForm()
        {
            int margin = S(16);
            int w = ClientSize.Width;
            int y = S(14);

            lblPrompt.Location = new Point(margin, y);
            y += lblPrompt.PreferredSize.Height + S(6);

            txtCode.SetBounds(margin, y, w - 2 * margin, S(24));
            y += S(24) + S(6);

            lblHint.Location = new Point(margin, y);
            y += lblHint.PreferredSize.Height + S(4);

            lblError.SetBounds(margin, y, w - 2 * margin, Math.Max(S(18), lblError.PreferredSize.Height));

            int btnH = S(26);
            int importW = ButtonWidth(btnImport, 64, 18);
            int cancelW = ButtonWidth(btnCancel, 64, 18);
            int by = ClientSize.Height - margin - btnH;
            btnImport.SetBounds(w - margin - importW, by, importW, btnH);
            btnCancel.SetBounds(btnImport.Left - S(8) - cancelW, by, cancelW, btnH);
        }

        private int ButtonWidth(Button b, int minWidth, int padding)
        {
            Size sz = TextRenderer.MeasureText(b.Text, b.Font);
            int w = sz.Width + S(padding);
            int min = S(minWidth);
            return w < min ? min : w;
        }

        private void TryImport()
        {
            MonitorConfig cfg;
            string error;
            if (!ProfileShareCode.TryDecode(txtCode.Text, out cfg, out error))
            {
                lblError.Text = error;
                txtCode.Focus();
                txtCode.SelectAll();
                return;
            }

            Config = cfg;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
