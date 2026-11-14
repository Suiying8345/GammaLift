using System;
using System.Drawing;
using System.Windows.Forms;

namespace GammaLift
{
    /// <summary>设置窗口的页码。菜单「设置」的每个二级项对应一页, 各开一个独立窗口。</summary>
    public enum SettingsPage
    {
        Startup,
        Appearance,
        Display,
        Tray
    }

    /// <summary>单页模态设置窗口: 只显示一组的控件, 确定后只提交本页字段。
    /// 尺寸与主界面走同一套缩放（界面字号 × 系统 DPI），高 DPI / 长译文时内容区可滚动。</summary>
    public class SettingsForm : Form
    {
        private const int DesignWidth = 560;       // 96dpi、界面缩放 100% 时的客户区宽度
        private const int DesignMargin = 12;       // 对话框外边距
        private const int DesignGroupPad = 16;     // 组框内边距

        private readonly AppSettings _settings;
        private readonly SettingsPage _page;
        private readonly ToolTip _tips = new ToolTip();
        private readonly float _uiScale;
        private readonly float _dpiScale;

        private Panel pnlContent;
        private GroupBox grpPage;
        private ComboBox cboLanguage, cboUiScale;
        private CheckBox chkAutoStart, chkTrayOnClose, chkMinimizeToTray, chkApplyOnStartup;
        private CheckBox chkKeepApplied, chkShowDdc, chkAutoDdc;
        private bool _showDdcBeforeAuto;   // 勾选「自动展开」前「启动时展开」的状态, 取消自动时恢复
        private CheckBox chkNotifyEnabled, chkNotifyProfileApplied, chkNotifyAppliedForeground, chkNotifyTrayOnClose;
        private Label lblLanguageCaption, lblScaleCaption;
        private Label lblInfoAppearance, lblInfoDisplay, lblInfoNotify, lblInfoRange;
        private Label lblRangeTitle, lblRangeState;
        private Label lblNotifyTitle;
        private Button btnUnlock;
        private Button btnOk, btnCancel;

        public SettingsForm(AppSettings settings, SettingsPage page, bool focusUnlock, float uiScale, float dpiScale)
        {
            _settings = settings;
            _page = page;
            _uiScale = uiScale > 0f ? uiScale : 1f;
            _dpiScale = dpiScale > 0f ? dpiScale : 1f;

            Text = Loc.T("设置 — GammaLift");
            Font = Loc.UiFont(9f * _uiScale);
            AutoScaleMode = AutoScaleMode.None;      // 坐标全部由 S() 算出, 与 AutoScaleMode=None 配合, 不叠加系统缩放
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(DesignWidth), S(220));   // LayoutDialog 会按实际内容重算

            _tips.AutoPopDelay = 20000;
            _tips.InitialDelay = 350;
            _tips.ReshowDelay = 100;
            _tips.ShowAlways = true;

            BuildUi();
            LayoutDialog();

            if (focusUnlock && btnUnlock != null)
                Shown += delegate { btnUnlock.Focus(); };
        }

        /// <summary>96dpi 设计值 × 界面缩放 × 系统 DPI。</summary>
        private int S(int v)
        {
            return (int)Math.Round(v * _uiScale * _dpiScale);
        }

        private int RowHeight()
        {
            return Math.Max(S(18), Font.Height + S(4));
        }

        private int ComboHeight()
        {
            return Math.Max(S(25), Font.Height + S(10));
        }

        private int ButtonHeight()
        {
            return Math.Max(S(30), Font.Height + S(12));
        }

        private static string PageTitle(SettingsPage page)
        {
            switch (page)
            {
                case SettingsPage.Startup: return Loc.T("启动与权限");
                case SettingsPage.Appearance: return Loc.T("界面与语言");
                case SettingsPage.Display: return Loc.T("显示器");
                default: return Loc.T("托盘与通知");
            }
        }

        // ====================================================================
        //  UI 构建：只创建本页控件, 位置由 LayoutDialog 统一计算
        // ====================================================================

        private void BuildUi()
        {
            pnlContent = new Panel();
            pnlContent.BorderStyle = BorderStyle.None;
            Controls.Add(pnlContent);

            btnOk = new Button();
            btnOk.Text = Loc.T("确定");
            btnOk.Click += delegate { ApplyAndClose(); };
            Controls.Add(btnOk);

            btnCancel = new Button();
            btnCancel.Text = Loc.T("取消");
            btnCancel.DialogResult = DialogResult.Cancel;
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            grpPage = NewGroup(PageTitle(_page));

            switch (_page)
            {
                case SettingsPage.Startup:
                    BuildStartupPage();
                    RefreshRangeState();
                    break;
                case SettingsPage.Appearance:
                    BuildAppearancePage();
                    break;
                case SettingsPage.Display:
                    BuildDisplayPage();
                    break;
                default:
                    BuildTrayPage();
                    break;
            }
        }

        private void BuildStartupPage()
        {
            chkAutoStart = NewCheck(Loc.T("开机自动启动"), grpPage);
            chkAutoStart.Checked = SystemIntegration.ReadAutoStart();
            _tips.SetToolTip(chkAutoStart,
                Loc.T("写入当前用户的启动项 (HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run)。\r\n" +
                      "只对当前 Windows 账户生效, 不需要管理员权限。\r\n" +
                      "关闭后下次登录不会自动启动。"));

            chkApplyOnStartup = NewCheck(Loc.T("启动时恢复上次应用的颜色设置"), grpPage);
            chkApplyOnStartup.Checked = _settings.ApplyOnStartup;
            _tips.SetToolTip(chkApplyOnStartup,
                Loc.T("程序启动后自动把上次的调节结果重新写入各显示器的 gamma 查找表。\r\n" +
                      "关闭后启动即为系统默认色彩, 需要手动拖动滑杆或选择配置文件。"));

            lblRangeTitle = NewWrapLabel(Loc.T("解除极端数值限制"));
            lblRangeTitle.Font = new Font(Font, FontStyle.Bold);
            grpPage.Controls.Add(lblRangeTitle);

            lblInfoRange = NewInfoMarker(grpPage,
                Loc.T("极端数值被系统拒绝？\r\n\r\n" +
                      "原因: GDI 在 SetDeviceGammaRamp 时会把每通道的 LUT 值钳制在系统允许的区间内,\r\n" +
                      "超出部分的调节会被静默丢弃 (界面上的数字变了, 屏幕却没变化)。\r\n\r\n" +
                      "解决: 以管理员权限写入\r\n" +
                      "HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\ICM\r\n" +
                      "GdiIcmGammaRange = 256 (DWORD)\r\n" +
                      "然后注销或重启。此操作会放宽整台机器的 gamma 范围, 请自行评估风险。"));

            lblRangeState = NewWrapLabel("");
            grpPage.Controls.Add(lblRangeState);

            btnUnlock = new Button();
            btnUnlock.Text = Loc.T("立即解除限制…");
            btnUnlock.Click += delegate { UnlockRange(); };
            grpPage.Controls.Add(btnUnlock);
            _tips.SetToolTip(btnUnlock, Loc.T("会弹出 UAC 提权窗口; 取消则不会做任何修改。"));
        }

        private void BuildAppearancePage()
        {
            lblLanguageCaption = NewWrapLabel(Loc.T("界面语言"));
            grpPage.Controls.Add(lblLanguageCaption);

            cboLanguage = new ComboBox();
            cboLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
            cboLanguage.Items.Add(AutoLanguageItem());
            foreach (UiLanguage l in Loc.Languages)
                cboLanguage.Items.Add(l);
            cboLanguage.SelectedIndex = 0;
            for (int i = 0; i < cboLanguage.Items.Count; i++)
            {
                UiLanguage item = cboLanguage.Items[i] as UiLanguage;
                if (item != null && string.Equals(item.Code, _settings.Language, StringComparison.OrdinalIgnoreCase))
                {
                    cboLanguage.SelectedIndex = i;
                    break;
                }
            }
            grpPage.Controls.Add(cboLanguage);
            string languageTip = Loc.T("切换后需要重启程序才能生效。");
            _tips.SetToolTip(cboLanguage, languageTip);

            lblScaleCaption = NewWrapLabel(Loc.T("界面缩放"));
            grpPage.Controls.Add(lblScaleCaption);

            cboUiScale = new ComboBox();
            cboUiScale.DropDownStyle = ComboBoxStyle.DropDownList;
            cboUiScale.Items.Add(Loc.T("小 (85%)"));
            cboUiScale.Items.Add(Loc.T("中 (100%, 默认)"));
            cboUiScale.Items.Add(Loc.T("大 (115%)"));
            cboUiScale.SelectedIndex = ScaleIndex(_settings.UiScalePercent);
            grpPage.Controls.Add(cboUiScale);
            string scaleTip = Loc.T("调整界面字号与控件尺寸, 立即生效。\r\n小 85% / 中 100% (默认) / 大 115%。\r\n" +
                                   "切换后窗口会恢复到该比例下的默认大小。");
            _tips.SetToolTip(cboUiScale, scaleTip);

            lblInfoAppearance = NewInfoMarker(grpPage, languageTip + "\r\n\r\n" + scaleTip);
        }

        private void BuildDisplayPage()
        {
            chkAutoDdc = NewCheck(Loc.T("硬件支持时自动展开"), grpPage);
            chkAutoDdc.Checked = _settings.AutoExpandDdc;
            _tips.SetToolTip(chkAutoDdc,
                Loc.T("选中显示器支持 DDC/CI 时自动展开「更多」面板，不支持时自动收起。\r\n" +
                      "切换显示器、刷新列表或重新启动都会重新判断；读取 DDC/CI 时，部分显示器可能短暂闪屏或变慢。\r\n" +
                      "与「启动时展开」互斥：勾选后该项不可用。"));

            chkShowDdc = NewCheck(Loc.T("启动时展开「更多」面板"), grpPage);
            chkShowDdc.Checked = _settings.ShowDdcPanel;
            _tips.SetToolTip(chkShowDdc,
                Loc.T("「更多」面板包含硬件调节 (DDC/CI)。\r\n" +
                      "展开时才会读取 DDC/CI, 避免部分显示器因 DDC 通信而闪屏或响应变慢。"));

            // 两项互斥: 勾选自动展开时, 「启动时展开」置灰并暂存原状态
            _showDdcBeforeAuto = chkShowDdc.Checked;
            chkAutoDdc.CheckedChanged += delegate { SyncDdcOptionState(); };
            SyncDdcOptionState();

            chkKeepApplied = NewCheck(Loc.T("定时重新应用 (每 5 秒, 防止游戏/驱动重置)"), grpPage);
            chkKeepApplied.Checked = _settings.KeepApplied;
            _tips.SetToolTip(chkKeepApplied,
                Loc.T("部分游戏、全屏程序或显卡驱动会重置 gamma 查找表。\r\n" +
                      "开启后每 5 秒检查并重新写入一次, 代价是有极轻微的常驻开销。"));

            lblInfoDisplay = NewInfoMarker(grpPage,
                Loc.T("饱和度只能走硬件调节 (gamma 是逐通道映射, 无法改变饱和度)。"));
        }

        /// <summary>「自动展开」与「启动时展开」只能选一个：自动优先, 取消自动时恢复原选择。</summary>
        private void SyncDdcOptionState()
        {
            if (chkAutoDdc.Checked)
            {
                _showDdcBeforeAuto = chkShowDdc.Checked;
                chkShowDdc.Checked = false;
                chkShowDdc.Enabled = false;
            }
            else
            {
                chkShowDdc.Enabled = true;
                chkShowDdc.Checked = _showDdcBeforeAuto;
            }
        }

        private void BuildTrayPage()
        {
            chkTrayOnClose = NewCheck(Loc.T("关闭按钮：隐藏到托盘（不退出程序）"), grpPage);
            chkTrayOnClose.Checked = _settings.TrayOnClose;
            _tips.SetToolTip(chkTrayOnClose,
                Loc.T("关闭主窗口后程序继续在后台运行, 颜色设置保持不变。\r\n" +
                      "需要真正退出时用托盘右键菜单:\r\n" +
                      "  · 退出 (保留当前色彩)\r\n" +
                      "  · 退出并恢复默认色彩"));

            chkMinimizeToTray = NewCheck(Loc.T("最小化按钮：隐藏到托盘（任务栏不显示）"), grpPage);
            chkMinimizeToTray.Checked = _settings.MinimizeToTray;
            _tips.SetToolTip(chkMinimizeToTray,
                Loc.T("最小化后任务栏不再显示按钮, 只保留右下角通知区域的托盘图标。\r\n" +
                      "双击托盘图标可重新打开窗口。不符合预期时取消勾选, 最小化会留在任务栏。"));

            lblNotifyTitle = NewWrapLabel(Loc.T("通知"));
            lblNotifyTitle.Font = new Font(Font, FontStyle.Bold);
            grpPage.Controls.Add(lblNotifyTitle);

            chkNotifyEnabled = NewCheck(Loc.T("启用通知"), grpPage);
            chkNotifyEnabled.Checked = _settings.NotifyEnabled;
            string masterTip = Loc.T("关闭后不再显示任何通知；下面两项的选择会保留。");
            _tips.SetToolTip(chkNotifyEnabled, masterTip);

            chkNotifyProfileApplied = NewCheck(Loc.T("应用预设时显示通知"), grpPage);
            chkNotifyProfileApplied.Checked = _settings.NotifyProfileApplied;

            chkNotifyAppliedForeground = NewCheck(Loc.T("应用预设时，前台也显示提示"), grpPage);
            chkNotifyAppliedForeground.Checked = _settings.NotifyProfileAppliedForeground;
            _tips.SetToolTip(chkNotifyAppliedForeground,
                Loc.T("开启后, 主窗口可见时应用预设也会弹出置顶提示窗；关闭则只更新状态栏。"));

            chkNotifyTrayOnClose = NewCheck(Loc.T("隐藏到托盘时显示提示"), grpPage);
            chkNotifyTrayOnClose.Checked = _settings.NotifyTrayOnClose;

            chkNotifyEnabled.CheckedChanged += delegate { UpdateNotificationControls(); };
            UpdateNotificationControls();

            lblInfoNotify = NewInfoMarker(grpPage, masterTip);
        }

        private void UpdateNotificationControls()
        {
            chkNotifyProfileApplied.Enabled = chkNotifyEnabled.Checked;
            chkNotifyAppliedForeground.Enabled = chkNotifyEnabled.Checked;
            chkNotifyTrayOnClose.Enabled = chkNotifyEnabled.Checked;
        }

        private Label NewInfoMarker(Control parent, string tip)
        {
            Label l = new Label();
            l.Text = "ⓘ";
            l.AutoSize = false;
            l.ForeColor = SystemColors.GrayText;
            l.Cursor = Cursors.Help;
            l.AccessibleName = tip;
            parent.Controls.Add(l);
            _tips.SetToolTip(l, tip);
            return l;
        }

        // ====================================================================
        //  布局
        // ====================================================================

        /// <summary>按内容实测高度排布本页组框；内容超出屏幕工作区时启用内容区滚动。</summary>
        private void LayoutDialog()
        {
            Rectangle wa = WorkingArea();
            Point scrollPosition = pnlContent.AutoScrollPosition;
            pnlContent.SuspendLayout();
            // 清除滚动偏移后再布局，避免冲突提示换行时组框位置漂移。
            pnlContent.AutoScrollPosition = Point.Empty;

            int clientW = Math.Min(S(DesignWidth), Math.Max(1, wa.Width - S(48)));
            int btnH = ButtonHeight();
            int bottomH = btnH + S(22);
            int maxClientH = Math.Max(bottomH + 1, wa.Height - S(80));

            int contentH = MeasureAndPlace(clientW - 2 * S(DesignMargin));
            int clientH = Math.Min(contentH + bottomH, maxClientH);
            bool scroll = (contentH + bottomH) > clientH;

            if (scroll)
            {
                int groupW = clientW - 2 * S(DesignMargin) - SystemInformation.VerticalScrollBarWidth;
                contentH = MeasureAndPlace(groupW);
            }

            ClientSize = new Size(clientW, clientH);

            int panelH = Math.Max(1, clientH - bottomH);
            pnlContent.SetBounds(0, 0, clientW, panelH);
            pnlContent.AutoScroll = scroll;
            pnlContent.AutoScrollMinSize = scroll ? new Size(0, contentH) : Size.Empty;

            int y = clientH - S(12) - btnH;
            int wCancel = ButtonWidth(btnCancel, 72, 20);
            int wOk = ButtonWidth(btnOk, 72, 20);
            btnCancel.SetBounds(clientW - S(12) - wCancel, y, wCancel, btnH);
            btnOk.SetBounds(clientW - S(12) - wCancel - S(8) - wOk, y, wOk, btnH);

            pnlContent.ResumeLayout();
            if (scroll)
                pnlContent.AutoScrollPosition = new Point(-scrollPosition.X, -scrollPosition.Y);
        }

        // 单页只有一组；组框高度按实际文字实测，避免高 DPI 与长译文挤出组框。
        private int MeasureAndPlace(int groupW)
        {
            int margin = S(DesignMargin);
            int bottom;
            switch (_page)
            {
                case SettingsPage.Startup:
                    bottom = PlaceStartupGroup(margin, margin, groupW);
                    break;
                case SettingsPage.Appearance:
                    bottom = PlaceAppearanceGroup(margin, margin, groupW);
                    break;
                case SettingsPage.Display:
                    bottom = PlaceDisplayGroup(margin, margin, groupW);
                    break;
                default:
                    bottom = PlaceTrayGroup(margin, margin, groupW);
                    break;
            }
            return bottom + S(DesignMargin);
        }

        private int PlaceStartupGroup(int x, int y, int groupW)
        {
            int pad = S(DesignGroupPad);
            int innerW = groupW - 2 * pad;
            int rowGap = S(6);
            int top = Math.Max(S(24), Font.Height + S(8));

            int sy = top;
            int checkH = MeasureCheckHeight(chkAutoStart, innerW);
            chkAutoStart.SetBounds(pad, sy, innerW, checkH);
            sy += checkH + rowGap;
            checkH = MeasureCheckHeight(chkApplyOnStartup, innerW);
            chkApplyOnStartup.SetBounds(pad, sy, innerW, checkH);
            sy += checkH + S(12);

            sy = PlaceRangeBlock(pad, innerW, sy);
            grpPage.SetBounds(x, y, groupW, sy);
            return y + sy;
        }

        /// <summary>「解除极端数值限制」副标题 + 状态 + 按钮。</summary>
        private int PlaceRangeBlock(int pad, int innerW, int y)
        {
            int infoW = TextRenderer.MeasureText("ⓘ", Font).Width + S(4);
            int infoH = Math.Max(S(16), Font.Height);
            int titleW = Math.Max(S(60), innerW - infoW - S(8));
            int titleH = Math.Max(S(18), MeasureTextHeight(lblRangeTitle.Text, lblRangeTitle.Font, titleW));

            lblRangeTitle.SetBounds(pad, y, titleW, titleH);
            lblInfoRange.SetBounds(pad + titleW + S(8), y + (titleH - infoH) / 2, infoW, infoH);
            y += titleH + S(6);

            int stateH = Math.Max(S(18), MeasureTextHeight(lblRangeState.Text, innerW));
            lblRangeState.SetBounds(pad, y, innerW, stateH);
            y += stateH + S(8);

            int unlockH = Math.Max(S(28), Font.Height + S(12));
            btnUnlock.SetBounds(pad, y, Math.Min(innerW, ButtonWidth(btnUnlock, 132, 26)), unlockH);
            y += unlockH + S(12);
            return y;
        }

        private int PlaceAppearanceGroup(int x, int y, int groupW)
        {
            int pad = S(DesignGroupPad);
            int innerW = groupW - 2 * pad;
            int top = Math.Max(S(24), Font.Height + S(8));
            int comboH = ComboHeight();

            int ay = PlaceComboRow(lblLanguageCaption, cboLanguage, pad, innerW, top, comboH, 220);
            ay += S(12);
            ay = PlaceComboRow(lblScaleCaption, cboUiScale, pad, innerW, ay, comboH, 170);
            ay += S(12);
            grpPage.SetBounds(x, y, groupW, ay);
            PlaceInfoMarker(lblInfoAppearance, groupW, pad);
            return y + ay;
        }

        /// <summary>标签 + 下拉框一行；放不下时自动换成上下两行。</summary>
        private int PlaceComboRow(Label caption, ComboBox combo, int pad, int innerW, int y, int comboH, int minComboWidth)
        {
            int captionW = TextRenderer.MeasureText(caption.Text, caption.Font).Width + S(2);
            int captionH = MeasureTextHeight(caption.Text, captionW);
            int comboW = ComboWidth(combo, minComboWidth);
            if (captionW + S(12) + comboW <= innerW)
            {
                int rowH = Math.Max(comboH, captionH);
                caption.SetBounds(pad, y + (rowH - captionH) / 2, captionW, captionH);
                int fieldW = innerW - captionW - S(12);
                combo.SetBounds(pad + captionW + S(12), y, fieldW, comboH);
                combo.DropDownWidth = Math.Max(comboW, fieldW);
                return y + rowH;
            }

            caption.SetBounds(pad, y, innerW, captionH);
            y += captionH + S(6);
            combo.SetBounds(pad, y, Math.Min(innerW, comboW), comboH);
            combo.DropDownWidth = comboW;
            return y + comboH;
        }

        private int PlaceDisplayGroup(int x, int y, int groupW)
        {
            int pad = S(DesignGroupPad);
            int innerW = groupW - 2 * pad;
            int rowGap = S(6);
            int top = Math.Max(S(24), Font.Height + S(8));

            int dy = top;
            int checkH = MeasureCheckHeight(chkAutoDdc, innerW);
            chkAutoDdc.SetBounds(pad, dy, innerW, checkH);
            dy += checkH + rowGap;
            checkH = MeasureCheckHeight(chkShowDdc, innerW);
            chkShowDdc.SetBounds(pad, dy, innerW, checkH);
            dy += checkH + rowGap;
            checkH = MeasureCheckHeight(chkKeepApplied, innerW);
            chkKeepApplied.SetBounds(pad, dy, innerW, checkH);
            dy += checkH + S(12);
            grpPage.SetBounds(x, y, groupW, dy);
            PlaceInfoMarker(lblInfoDisplay, groupW, pad);
            return y + dy;
        }

        private int PlaceTrayGroup(int x, int y, int groupW)
        {
            int pad = S(DesignGroupPad);
            int innerW = groupW - 2 * pad;
            int rowGap = S(6);
            int top = Math.Max(S(24), Font.Height + S(8));

            int ty = top;
            int checkH = MeasureCheckHeight(chkTrayOnClose, innerW);
            chkTrayOnClose.SetBounds(pad, ty, innerW, checkH);
            ty += checkH + rowGap;
            checkH = MeasureCheckHeight(chkMinimizeToTray, innerW);
            chkMinimizeToTray.SetBounds(pad, ty, innerW, checkH);
            ty += checkH + S(12);

            // 「通知」副标题 + ⓘ
            int infoW = TextRenderer.MeasureText("ⓘ", Font).Width + S(4);
            int infoH = Math.Max(S(16), Font.Height);
            int titleW = Math.Max(S(60), innerW - infoW - S(8));
            int titleH = Math.Max(S(18), MeasureTextHeight(lblNotifyTitle.Text, lblNotifyTitle.Font, titleW));
            lblNotifyTitle.SetBounds(pad, ty, titleW, titleH);
            lblInfoNotify.SetBounds(pad + titleW + S(8), ty + (titleH - infoH) / 2, infoW, infoH);
            ty += titleH + S(6);

            checkH = MeasureCheckHeight(chkNotifyEnabled, innerW);
            chkNotifyEnabled.SetBounds(pad, ty, innerW, checkH);
            ty += checkH + rowGap;
            int childIndent = S(16);
            int childW = innerW - childIndent;
            checkH = MeasureCheckHeight(chkNotifyProfileApplied, childW);
            chkNotifyProfileApplied.SetBounds(pad + childIndent, ty, childW, checkH);
            ty += checkH + rowGap;
            checkH = MeasureCheckHeight(chkNotifyAppliedForeground, childW);
            chkNotifyAppliedForeground.SetBounds(pad + childIndent, ty, childW, checkH);
            ty += checkH + rowGap;
            checkH = MeasureCheckHeight(chkNotifyTrayOnClose, childW);
            chkNotifyTrayOnClose.SetBounds(pad + childIndent, ty, childW, checkH);
            ty += checkH + S(12);
            grpPage.SetBounds(x, y, groupW, ty);
            return y + ty;
        }

        private void PlaceInfoMarker(Label marker, int groupW, int pad)
        {
            int w = TextRenderer.MeasureText(marker.Text, marker.Font).Width + S(4);
            int h = Math.Max(S(16), marker.Font.Height);
            int width = marker.Parent != null ? marker.Parent.ClientSize.Width : groupW;
            marker.SetBounds(width - pad - w, S(4), w, h);
        }

        /// <summary>多语言译文行数不一, 高度按实际字体实测换行结果, 固定值在长译文下会截断。</summary>
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

        private int MeasureCheckHeight(CheckBox check, int width)
        {
            return Math.Max(RowHeight(),
                MeasureTextHeight(check.Text, check.Font, width - S(24)) + S(4));
        }

        private int ComboWidth(ComboBox combo, int minWidth)
        {
            int width = S(minWidth);
            foreach (object item in combo.Items)
                width = Math.Max(width, TextRenderer.MeasureText(item.ToString(), combo.Font).Width + S(28));
            return width;
        }

        /// <summary>按实际文字量按钮宽度（AutoSize=false 时 PreferredSize 返回当前尺寸, 不反映文字量）。</summary>
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

        private GroupBox NewGroup(string text)
        {
            GroupBox g = new GroupBox();
            g.Text = text;
            pnlContent.Controls.Add(g);
            return g;
        }

        private CheckBox NewCheck(string text, Control parent)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = false;
            parent.Controls.Add(c);
            return c;
        }

        /// <summary>会随宽度换行的说明文字, 高度由 MeasureTextHeight 实测。</summary>
        private Label NewWrapLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            return l;
        }

        /// <summary>界面缩放下拉框的三档：小 0.85 / 中 1.00 / 大 1.15。</summary>
        private static int ScaleIndex(int percent)
        {
            if (percent > 0 && percent < 93) return 0;
            if (percent >= 108) return 2;
            return 1;
        }

        private static int PercentOfScaleIndex(int index)
        {
            if (index == 0) return 85;
            if (index == 2) return 115;
            return 100;
        }

        /// <summary>"跟随系统"项：显示当前系统语言，避免用户看不出自动选了什么。</summary>
        private static UiLanguage AutoLanguageItem()
        {
            return new UiLanguage(Loc.AutoCode,
                Loc.T("跟随系统") + " (" + Loc.DisplayNameOf(Loc.DetectSystem()) + ")");
        }

        private void RefreshRangeState()
        {
            if (SystemIntegration.IsGammaRangeUnlocked())
            {
                lblRangeState.Text = Loc.T("当前状态: 已解除 (刚写入的记录需注销或重启后生效)");
                lblRangeState.ForeColor = Color.ForestGreen;
                btnUnlock.Enabled = false;
            }
            else
            {
                lblRangeState.Text = Loc.T("当前状态: 未解除 — 极端数值会被系统钳制");
                lblRangeState.ForeColor = Color.Firebrick;
                btnUnlock.Enabled = true;
            }
        }

        private void UnlockRange()
        {
            GammaRangeResult r = SystemIntegration.UnlockGammaRange();
            if (r == GammaRangeResult.Canceled) return;

            if (r == GammaRangeResult.AlreadyUnlocked)
            {
                MessageBox.Show(this, Loc.T("限制已经解除。如果还未生效, 请注销或重启一次。"),
                    Loc.T("GammaLift"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (r == GammaRangeResult.Unlocked)
            {
                MessageBox.Show(this, Loc.T("已写入注册表。注销或重启后, 系统将允许更大的 Gamma 调节范围。"),
                    Loc.T("GammaLift"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, Loc.T("注册表写入未生效, 请确认在弹出的 UAC 窗口中点击了\"是\"。"),
                    Loc.T("GammaLift"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RefreshRangeState();
            LayoutDialog();   // 状态行文字变了, 高度可能变化
        }

        private void ApplyAndClose()
        {
            switch (_page)
            {
                case SettingsPage.Startup:
                    _settings.ApplyOnStartup = chkApplyOnStartup.Checked;
                    if (!SystemIntegration.WriteAutoStart(chkAutoStart.Checked))
                        MessageBox.Show(this, Loc.T("写入开机启动项失败 (注册表不可写)。其余设置已保存。"),
                            Loc.T("GammaLift"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;

                case SettingsPage.Appearance:
                    UiLanguage lang = cboLanguage.SelectedItem as UiLanguage;
                    if (lang != null)
                        _settings.Language = lang.Code;
                    _settings.UiScalePercent = PercentOfScaleIndex(cboUiScale.SelectedIndex);
                    break;

                case SettingsPage.Display:
                    _settings.AutoExpandDdc = chkAutoDdc.Checked;
                    _settings.ShowDdcPanel = chkAutoDdc.Checked ? false : chkShowDdc.Checked;
                    _settings.KeepApplied = chkKeepApplied.Checked;
                    break;

                default:
                    _settings.TrayOnClose = chkTrayOnClose.Checked;
                    _settings.MinimizeToTray = chkMinimizeToTray.Checked;
                    _settings.NotifyEnabled = chkNotifyEnabled.Checked;
                    _settings.NotifyProfileApplied = chkNotifyProfileApplied.Checked;
                    _settings.NotifyProfileAppliedForeground = chkNotifyAppliedForeground.Checked;
                    _settings.NotifyTrayOnClose = chkNotifyTrayOnClose.Checked;
                    break;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
