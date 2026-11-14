using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GammaLift
{
    public class MainForm : Form
    {
        // ---------- 状态 ----------
        private List<DisplayMonitor> _monitors = new List<DisplayMonitor>();
        private AppSettings _settings;
        private bool _loadingUi;          // 抑制程序写 UI 时触发提交
        private bool _exitRequested;
        private bool _restarting;         // 切换语言后的自重启：跳过未保存修改的询问
        private bool _restoreNeutralOnExit;
        private int _channelIdx;          // 0=R 1=G 2=B（rdoAll 模式下忽略）
        private Profile _activeProfile;    // 当前生效的配置文件：下拉框选中即应用，与它不一致即视为有未保存修改
        private bool _suppressProfileChange;  // 抑制程序性改动下拉框触发的自动应用
        private readonly List<Profile> _hotkeyOrder = new List<Profile>();
        private readonly List<int> _hotkeyIds = new List<int>();
        private readonly HotkeyGestureEngine _hotkeyEngine = new HotkeyGestureEngine();
        private readonly GlobalHotkeyListener _hotkeyListener = new GlobalHotkeyListener();
        private System.Windows.Forms.Timer _gestureTimer;
        private int _hotkeyGeneration;
        private int _hotkeyPauseDepth;
        private bool _hotkeysClosed;
        private bool _updatingHotkeyUi;
        private bool _capturingHotkey;
        private bool _powerHotkeysPaused;
        private bool _sessionHotkeysPaused;
        private static readonly uint ShowWindowMsg = NativeMethods.RegisterWindowMessage(Program.ShowWindowMessageName);
        private static string AppTitle { get { return Loc.T("GammaLift"); } }
        private string _statusText = "";      // 普通状态文本（构造函数里按当前语言取词）
        private bool _rampRejected;           // 当前是否处于"数值被系统拒绝"状态（状态栏优先显示该提示）

        // ---------- 控件 ----------
        private Label lblMon;                      // "显示器"标签（布局要算位置, 存字段）
        private ComboBox cboMonitors;              // 顶部显示器下拉
        private Button btnIdentify, btnRefresh;
        private CheckBox chkApplyAll;              // 只在多显示器时出现
        private bool _applyAllShown;               // 它"应该"显示吗（Control.Visible 在父窗体未显示时恒为 false, 布局改用此标志）

        private GroupBox grpGamma;
        private Label lblBright, lblContrast, lblGamma;
        private RadioButton rdoAll, rdoR, rdoG, rdoB;
        private TrackBar tbBright, tbContrast, tbGamma;
        private NumericUpDown numBright, numContrast, numGamma;
        private Button btnResetMonitor;            // 重置当前显示器的软件调节（重置所有显示器在托盘菜单里）
        private Button btnCopyShareCode, btnImportShareCode;   // 单屏参数分享码
        private CurveView picCurve;

        private GroupBox grpMore;                  // 「更多」分组：硬件调节, 标题行即展开 / 收起开关
        private Label lblDdcTitle;                 // 「更多」里的硬件调节小标题
        private Panel pnlDdc;                      // 硬件调节内容（默认收起）
        private bool _moreExpanded;
        private bool _loaded;                      // 窗体已完成 Load: DDC 读取依赖 BeginInvoke 的句柄, Load 之前没有
        private Label lblDdcStatus;
        private Label lblDdcBrightName, lblDdcContrastName, lblDdcSatName;
        private TrackBar tbDdcBright, tbDdcContrast, tbDdcSat;
        private NumericUpDown numDdcBright, numDdcContrast, numDdcSat;   // 硬件值也可直接输入
        private Label lblDdcBrightMax, lblDdcContrastMax, lblDdcSatMax;  // "/100" 之类的量程提示
        private Button btnDdcRefresh;

        private GroupBox grpProfiles;
        private ComboBox cboProfiles;
        private Button btnSaveProfile, btnNewProfile;
        private Button btnClearHotkey;
        private Panel pnlHotkey;
        private ToolStripStatusLabel lblDirty;   // "未保存"标记, 放在状态栏, 避免与多语言的长译文抢位置
        private TextBox txtHotkey;
        private ComboBox cboHotkeyTrigger;

        // 循环切换预设的全局热键：放在配置文件组上方, 与预设热键一样支持三种手势
        private Label lblCycleHotkey;
        private Panel pnlCycleHotkey;
        private TextBox txtCycleHotkey;
        private Button btnClearCycle;
        private ComboBox cboCycleTrigger;
        private Button btnEditProfiles;
        private bool _updatingCycleUi;

        private MenuStrip menuMain;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatus;
        private ToolTip _tips;

        private bool _hiddenToTray;
        private bool _suppressMinimizeHide;

        // ---------- 布局 ----------
        private bool _profileSingleRow;
        private float _uiScale = 1f;      // 字号预设: 小 0.85 / 中 1.0 / 大 1.15
        private float _dpiScale = 1f;     // 系统 DPI / 96, 让布局随缩放比例一起放大

        // 96 dpi、界面缩放 100% 下的客户区基准（单位: 设计像素）
        private const int DesignClientWidth = 560;
        private const int DesignCompactHeight = 368;   // 默认高度: 软件调节区三行 26 px, 与硬件调节区等高
        private const int DesignMaxWidth = 760;        // 只允许适度拉伸, 不提供最大化
        private const int DesignMaxHeight = 600;

        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;

        private System.Windows.Forms.Timer _saveTimer;          // 设置延迟保存
        private System.Windows.Forms.Timer _keepTimer;          // 定时重新应用
        private System.Windows.Forms.Timer _ddcSetTimer;        // DDC 写入去抖
        private System.Windows.Forms.Timer _displayChangeTimer; // 显示器变更去抖

        // 待发送的 DDC 写入及其目标显示器 (滑块拖动时捕获, 避免去抖期间切换选中项导致写错屏)
        private readonly Dictionary<byte, uint> _pendingDdcWrites = new Dictionary<byte, uint>();
        private readonly List<DisplayMonitor> _pendingDdcTargets = new List<DisplayMonitor>();

        // DDC 写入批次按入队顺序由单个工作线程依次发送, 保证后发的值不会被先发的覆盖
        private readonly object _ddcQueueLock = new object();
        private readonly Queue<KeyValuePair<List<DisplayMonitor>, Dictionary<byte, uint>>> _ddcQueue =
            new Queue<KeyValuePair<List<DisplayMonitor>, Dictionary<byte, uint>>>();
        private bool _ddcWorkerRunning;
        // 「自动展开」正在探测的显示器 (按 StableKey), 避免刷新列表与自动策略重复探测同一台
        private readonly HashSet<string> _ddcAutoProbing = new HashSet<string>();

        public MainForm()
        {
            _settings = SettingsStore.Load();
            Loc.SetLanguage(_settings.Language);   // 先于 BuildUi 调用: 界面文案在构建时取词
            _statusText = Loc.T("就绪");
            _uiScale = ScaleFromPercent(_settings.UiScalePercent);
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    _dpiScale = g.DpiX / 96f;      // 高 DPI 下整体放大, 坐标系仍按 96dpi 设计
            }
            catch (Exception) { _dpiScale = 1f; }
            BuildUi();
            BuildTray();

            _saveTimer = MakeTimer(1000, delegate { _saveTimer.Stop(); SettingsStore.Save(_settings); });
            _keepTimer = MakeTimer(5000, delegate { ReapplyNonNeutral(); });
            _ddcSetTimer = MakeTimer(400, delegate { _ddcSetTimer.Stop(); FlushDdcWrites(); });
            _displayChangeTimer = MakeTimer(1200, delegate { _displayChangeTimer.Stop(); RefreshMonitors(true); });
            _gestureTimer = MakeTimer(1, delegate
            {
                _gestureTimer.Stop();
                DispatchHotkeyActions(_hotkeyEngine.FireDue(HotkeyClock.Now));
                ArmGestureTimer();
            });

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            Load += delegate
            {
                if (_settings.KeepApplied) _keepTimer.Start();

                _loaded = true;
                RefreshMonitors(false);
                RefreshProfileCombo();
                RestoreActiveProfile();
                ReRegisterHotkeys();

                if (_settings.ApplyOnStartup)
                {
                    int n = ReapplyNonNeutral();
                    if (n > 0) SetStatus(Loc.F("已恢复 {0} 台显示器的上次设置", n));
                }
            };
        }

        private System.Windows.Forms.Timer MakeTimer(int interval, EventHandler tick)
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = interval;
            t.Tick += tick;
            return t;
        }

        // ====================================================================
        //  UI 构建
        // ====================================================================

        private void BuildUi()
        {
            SuspendLayout();

            Text = AppTitle;                         // 标题只显示中英文名
            Font = Loc.UiFont(9f * _uiScale);
            AutoScaleMode = AutoScaleMode.None;      // 坐标全部由 LayoutUi 算出, 自动缩放会在其上二次叠加
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;                     // 不提供最大化: 窗口只需适度拉伸, 全屏没有意义
            ClientSize = new Size(S(560), S(368));   // 软件调节区默认与硬件调节区一样紧凑
            StartPosition = FormStartPosition.CenterScreen;
            Icon = AppIcon.Load();
            ApplyWindowSizeLimits(false);

            // ---------- 顶部菜单：设置（分页）/ 帮助 ----------
            menuMain = new MenuStrip();
            menuMain.Dock = DockStyle.Top;
            menuMain.ShowItemToolTips = true;
            ToolStripMenuItem mnuSettings = new ToolStripMenuItem(Loc.T("设置"));
            mnuSettings.Name = "mnuSettings";
            mnuSettings.ToolTipText = Loc.T("开机自启、界面语言、显示器和托盘与通知");
            AddSettingsEntry(mnuSettings, "mnuSettingsStartup", "启动与权限", SettingsPage.Startup);
            AddSettingsEntry(mnuSettings, "mnuSettingsAppearance", "界面与语言", SettingsPage.Appearance);
            AddSettingsEntry(mnuSettings, "mnuSettingsDisplay", "显示器", SettingsPage.Display);
            AddSettingsEntry(mnuSettings, "mnuSettingsTray", "托盘与通知", SettingsPage.Tray);
            ToolStripMenuItem mnuHelp = new ToolStripMenuItem(Loc.T("帮助"));
            mnuHelp.Name = "mnuHelp";
            ToolStripMenuItem mnuAbout = new ToolStripMenuItem(Loc.T("关于 GammaLift"));
            mnuAbout.Name = "mnuAbout";
            mnuAbout.ToolTipText = Loc.T("版本、版权、MIT 许可证与项目仓库");
            mnuAbout.Click += delegate { OpenAbout(); };
            mnuHelp.DropDownItems.Add(mnuAbout);
            menuMain.Items.AddRange(new ToolStripItem[] { mnuSettings, mnuHelp });
            Controls.Add(menuMain);

            // ---------- 顶部：显示器选择（单屏时只有一个下拉）----------
            lblMon = NewLabel(Loc.T("显示器"), 14, 14);
            Controls.Add(lblMon);

            cboMonitors = new ComboBox();
            cboMonitors.DropDownStyle = ComboBoxStyle.DropDownList;
            cboMonitors.SetBounds(60, 8, 300, 25);
            cboMonitors.SelectedIndexChanged += delegate { LoadMonitorToUi(); };
            Controls.Add(cboMonitors);

            btnIdentify = NewButton(Loc.T("识别显示器"), 372, 6, 108, 28,
                delegate { IdentifyForm.ShowAll(_monitors, _uiScale * _dpiScale); });
            btnRefresh = NewButton(Loc.T("刷新列表"), 486, 6, 100, 28, delegate { RefreshMonitors(true); });
            Controls.Add(btnIdentify);
            Controls.Add(btnRefresh);

            // ---------- 软件调节（标签+数值一行, 滑块通栏, 参考经典 Gamma Panel 的紧凑排布）----------
            grpGamma = new GroupBox();
            grpGamma.Text = Loc.T("软件调节 (Gamma 查找表) — 对该显示器单独生效");
            grpGamma.SetBounds(12, 42, 676, 236);
            Controls.Add(grpGamma);

            rdoAll = NewRadio(Loc.T("RGB 联动"), 14, 14, true);
            rdoR = NewRadio(Loc.T("红"), 108, 14, false);
            rdoG = NewRadio(Loc.T("绿"), 160, 14, false);
            rdoB = NewRadio(Loc.T("蓝"), 212, 14, false);
            rdoR.ForeColor = Color.Firebrick;
            rdoG.ForeColor = Color.ForestGreen;
            rdoB.ForeColor = Color.RoyalBlue;
            EventHandler chSwitch = delegate
            {
                if (_loadingUi) return;
                _channelIdx = rdoG.Checked ? 1 : (rdoB.Checked ? 2 : 0);
                LoadChannelToSliders();
            };
            rdoAll.CheckedChanged += chSwitch;
            rdoR.CheckedChanged += chSwitch;
            rdoG.CheckedChanged += chSwitch;
            rdoB.CheckedChanged += chSwitch;
            grpGamma.Controls.Add(rdoAll);
            grpGamma.Controls.Add(rdoR);
            grpGamma.Controls.Add(rdoG);
            grpGamma.Controls.Add(rdoB);

            chkApplyAll = NewCheck(Loc.T("同步调节所有显示器"), 262, 16);
            chkApplyAll.Visible = false;   // 只有一台显示器时无意义, 由 RefreshMonitors 决定是否显示
            grpGamma.Controls.Add(chkApplyAll);   // 单选行与它的位置都在 LayoutGammaInner 里算

            tbBright = NewTrack(-100, 100, 25, 14, 62, 404);
            numBright = NewNum(-100, 100, 0, 356, 38);
            lblBright = AddRow(grpGamma, Loc.T("亮度"), 38, tbBright, numBright);

            tbContrast = NewTrack(-100, 100, 25, 14, 114, 404);
            numContrast = NewNum(-100, 100, 0, 356, 90);
            lblContrast = AddRow(grpGamma, Loc.T("对比度"), 90, tbContrast, numContrast);

            tbGamma = NewTrack(30, 300, 30, 14, 166, 404);
            tbGamma.Value = 100;
            numGamma = new NumericUpDown();
            numGamma.SetBounds(356, 142, 62, 22);
            numGamma.Minimum = 0.30m;
            numGamma.Maximum = 3.00m;
            numGamma.DecimalPlaces = 2;
            numGamma.Increment = 0.05m;
            numGamma.Value = 1.00m;
            lblGamma = AddRow(grpGamma, Loc.T("伽马"), 142, tbGamma, numGamma);

            // 滑块 <-> 数字框 双向同步，并提交修改
            WireSlider(tbBright, numBright, 1);
            WireSlider(tbContrast, numContrast, 1);
            WireSlider(tbGamma, numGamma, 100);

            btnCopyShareCode = NewButton(Loc.T("复制分享码"), 0, 0, 96, 26,
                delegate { CopyCurrentShareCode(); });
            btnImportShareCode = NewButton(Loc.T("导入分享码"), 0, 0, 96, 26,
                delegate { ImportShareCode(); });
            btnResetMonitor = NewButton(Loc.T("重置"), 0, 0, 64, 26, delegate { ResetMonitor(); });
            grpGamma.Controls.Add(btnCopyShareCode);
            grpGamma.Controls.Add(btnImportShareCode);
            grpGamma.Controls.Add(btnResetMonitor);

            picCurve = new CurveView();
            picCurve.SetBounds(420, 14, 232, 200);
            picCurve.BorderStyle = BorderStyle.FixedSingle;
            picCurve.BackColor = Color.FromArgb(28, 28, 30);
            picCurve.Paint += PaintCurve;
            grpGamma.Controls.Add(picCurve);

            // ---------- 更多：标题行即展开开关, 内含硬件调节 ----------
            grpMore = new GroupBox();
            grpMore.SetBounds(12, 284, 676, 30);
            grpMore.AccessibleName = Loc.T("展开或收起「更多」面板");
            UpdateMoreHeaderText();
            // 标题行（分组框自带的标题区）点击展开 / 收起；铺满整行的按钮会盖住分组框顶边
            grpMore.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && e.Y <= MoreHeaderHeight())
                    SetMoreExpanded(!_moreExpanded, true);
            };
            grpMore.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                grpMore.Cursor = e.Y <= MoreHeaderHeight() ? Cursors.Hand : Cursors.Default;
            };
            grpMore.MouseLeave += delegate { grpMore.Cursor = Cursors.Default; };
            Controls.Add(grpMore);

            lblDdcTitle = new Label();
            lblDdcTitle.Text = Loc.T("硬件调节 (DDC/CI)");
            lblDdcTitle.AutoSize = false;
            lblDdcTitle.Font = new Font(Font, FontStyle.Bold);
            grpMore.Controls.Add(lblDdcTitle);

            pnlDdc = new Panel();
            pnlDdc.BorderStyle = BorderStyle.None;
            pnlDdc.Visible = false;
            grpMore.Controls.Add(pnlDdc);

            lblDdcStatus = NewLabel(Loc.T("正在检测…"), 12, 10);
            lblDdcStatus.AutoSize = false;
            lblDdcStatus.SetBounds(12, 10, 528, 18);
            pnlDdc.Controls.Add(lblDdcStatus);

            btnDdcRefresh = NewButton(Loc.T("重新读取"), 556, 6, 104, 26, delegate { LoadDdcToUi(true); });
            pnlDdc.Controls.Add(btnDdcRefresh);

            tbDdcBright = NewTrack(0, 100, 10, 70, 34, 470);
            numDdcBright = NewNum(0, 100, 0, 546, 36);
            lblDdcBrightMax = NewLabel("", 608, 40);
            lblDdcBrightName = AddDdcRow(pnlDdc, Loc.T("亮度"), 34, tbDdcBright, numDdcBright, lblDdcBrightMax);

            tbDdcContrast = NewTrack(0, 100, 10, 70, 68, 470);
            numDdcContrast = NewNum(0, 100, 0, 546, 70);
            lblDdcContrastMax = NewLabel("", 608, 74);
            lblDdcContrastName = AddDdcRow(pnlDdc, Loc.T("对比度"), 68, tbDdcContrast, numDdcContrast, lblDdcContrastMax);

            tbDdcSat = NewTrack(0, 100, 10, 70, 102, 470);
            numDdcSat = NewNum(0, 100, 0, 546, 104);
            lblDdcSatMax = NewLabel("", 608, 108);
            lblDdcSatName = AddDdcRow(pnlDdc, Loc.T("饱和度"), 102, tbDdcSat, numDdcSat, lblDdcSatMax);

            WireDdcControls(tbDdcBright, numDdcBright, VcpCodes.Brightness);
            WireDdcControls(tbDdcContrast, numDdcContrast, VcpCodes.Contrast);
            WireDdcControls(tbDdcSat, numDdcSat, VcpCodes.Saturation);
            SetDdcEnabled(false, false, false);

            // ---------- 循环切换预设：配置文件组上方的一行 ----------
            lblCycleHotkey = NewLabel(Loc.T("循环切换预设"), 14, 0);
            Controls.Add(lblCycleHotkey);

            pnlCycleHotkey = new Panel();
            pnlCycleHotkey.BorderStyle = BorderStyle.FixedSingle;
            pnlCycleHotkey.BackColor = SystemColors.Window;
            Controls.Add(pnlCycleHotkey);

            txtCycleHotkey = new TextBox();
            txtCycleHotkey.ReadOnly = true;
            txtCycleHotkey.BorderStyle = BorderStyle.None;
            txtCycleHotkey.BackColor = SystemColors.Window;
            txtCycleHotkey.SetBounds(0, 0, 104, 25);
            txtCycleHotkey.HandleCreated += delegate { NativeMethods.SetWindowTheme(txtCycleHotkey.Handle, " ", " "); };
            txtCycleHotkey.KeyDown += CycleHotkeyCapture;
            txtCycleHotkey.GotFocus += delegate { SetHotkeyCapture(true); };
            txtCycleHotkey.LostFocus += delegate { SetHotkeyCapture(false); };
            pnlCycleHotkey.Controls.Add(txtCycleHotkey);

            btnClearCycle = new Button();
            btnClearCycle.Text = "×";
            btnClearCycle.FlatStyle = FlatStyle.Flat;
            btnClearCycle.FlatAppearance.BorderSize = 0;
            btnClearCycle.BackColor = SystemColors.Window;
            btnClearCycle.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
            btnClearCycle.Visible = false;
            btnClearCycle.Click += delegate { ClearCycleHotkey(); };
            pnlCycleHotkey.Controls.Add(btnClearCycle);

            cboCycleTrigger = new ComboBox();
            cboCycleTrigger.DropDownStyle = ComboBoxStyle.DropDownList;
            cboCycleTrigger.AccessibleName = Loc.T("触发方式");
            cboCycleTrigger.Items.AddRange(new object[] { Loc.T("单击"), Loc.T("双击"), Loc.T("长按") });
            cboCycleTrigger.SelectedIndexChanged += delegate { ChangeCycleTrigger(); };
            Controls.Add(cboCycleTrigger);

            btnEditProfiles = NewButton(Loc.T("编辑…"), 0, 0, 64, 26, delegate { EditProfiles(); });
            btnEditProfiles.Enabled = false;
            Controls.Add(btnEditProfiles);

            // ---------- 底部：配置文件 ----------
            grpProfiles = new GroupBox();
            grpProfiles.Text = Loc.T("配置文件 (可绑定全局热键一键切换)");
            grpProfiles.SetBounds(12, 318, 676, 76);
            Controls.Add(grpProfiles);

            cboProfiles = new ComboBox();
            cboProfiles.DropDownStyle = ComboBoxStyle.DropDownList;
            cboProfiles.SetBounds(14, 28, 170, 25);
            cboProfiles.SelectedIndexChanged += delegate { OnProfileSelectionChanged(); };
            grpProfiles.Controls.Add(cboProfiles);

            btnSaveProfile = NewButton(Loc.T("保存"), 0, 0, 72, 27, delegate { SaveToActiveProfile(); });
            btnNewProfile = NewButton(Loc.T("新建"), 0, 0, 70, 27, delegate { NewProfile(); });
            grpProfiles.Controls.Add(btnSaveProfile);
            grpProfiles.Controls.Add(btnNewProfile);

            pnlHotkey = new Panel();
            pnlHotkey.BorderStyle = BorderStyle.FixedSingle;
            pnlHotkey.BackColor = SystemColors.Window;
            grpProfiles.Controls.Add(pnlHotkey);

            txtHotkey = new TextBox();
            txtHotkey.ReadOnly = true;
            txtHotkey.BorderStyle = BorderStyle.None;
            txtHotkey.BackColor = SystemColors.Window;
            txtHotkey.SetBounds(0, 0, 104, 25);
            txtHotkey.HandleCreated += delegate { NativeMethods.SetWindowTheme(txtHotkey.Handle, " ", " "); };
            txtHotkey.Text = Loc.T("按组合键");           // 输入框很窄, 用最短的提示
            txtHotkey.KeyDown += HotkeyCapture;
            txtHotkey.GotFocus += delegate { SetHotkeyCapture(true); };
            txtHotkey.LostFocus += delegate { SetHotkeyCapture(false); };
            Deactivate += delegate { SetHotkeyCapture(false); };
            pnlHotkey.Controls.Add(txtHotkey);

            cboHotkeyTrigger = new ComboBox();
            cboHotkeyTrigger.DropDownStyle = ComboBoxStyle.DropDownList;
            cboHotkeyTrigger.AccessibleName = Loc.T("触发方式");
            cboHotkeyTrigger.Items.AddRange(new object[] { Loc.T("单击"), Loc.T("双击"), Loc.T("长按") });
            cboHotkeyTrigger.SelectedIndex = 0;
            cboHotkeyTrigger.SelectedIndexChanged += delegate { ChangeHotkeyTrigger(); };
            grpProfiles.Controls.Add(cboHotkeyTrigger);

            // 清除按钮放在输入框容器内部右侧, 不额外占一行
            btnClearHotkey = new Button();
            btnClearHotkey.Text = "×";
            btnClearHotkey.FlatStyle = FlatStyle.Flat;
            btnClearHotkey.FlatAppearance.BorderSize = 0;
            btnClearHotkey.BackColor = SystemColors.Window;
            btnClearHotkey.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
            btnClearHotkey.Visible = false;
            btnClearHotkey.Click += delegate { ClearHotkey(); };
            pnlHotkey.Controls.Add(btnClearHotkey);

            _tips = new ToolTip();   // 由字段持有: 局部 ToolTip 会被回收, 提示随即失效
            _tips.SetToolTip(txtHotkey, Loc.T("点击输入框后按组合键完成录入。\r\n只接受 Ctrl/Alt 组合键或 F 功能键。\r\n按 Delete 或点击右侧 × 可清除。"));
            _tips.SetToolTip(btnClearHotkey, Loc.T("清除该配置文件的热键"));
            _tips.SetToolTip(cboHotkeyTrigger, Loc.T("长按约 400 ms 触发；双击间隔跟随 Windows。\r\n同一个键绑定双击时，单击会等待双击间隔结束。"));
            _tips.SetToolTip(btnCopyShareCode, Loc.T("复制当前显示器的分享码"));
            _tips.SetToolTip(btnImportShareCode, Loc.T("把框中的分享码应用到当前显示器（不会自动保存到预设）"));
            _tips.SetToolTip(btnResetMonitor, Loc.T("把当前显示器的软件调节恢复为默认"));
            _tips.SetToolTip(txtCycleHotkey, Loc.T("点击输入框后按组合键完成录入。\r\n只接受 Ctrl/Alt 组合键或 F 功能键。\r\n按 Delete 或点击右侧 × 可清除。"));
            _tips.SetToolTip(btnClearCycle, Loc.T("清除循环切换热键"));
            _tips.SetToolTip(cboCycleTrigger, Loc.T("长按约 400 ms 触发；双击间隔跟随 Windows。\r\n同一个键绑定双击时，单击会等待双击间隔结束。"));
            _tips.SetToolTip(btnEditProfiles, Loc.T("预设编辑"));
            _tips.SetToolTip(grpMore, Loc.T("展开或收起「更多」面板"));
            _tips.SetToolTip(lblDdcTitle, Loc.T("硬件调节 (DDC/CI) — 亮度 / 对比度 / 饱和度, 直接写入显示器面板"));

            // ---------- 状态栏 ----------
            statusStrip = new StatusStrip();
            statusStrip.SizingGrip = false;

            lblStatus = new ToolStripStatusLabel(Loc.T("就绪"));
            lblStatus.Spring = true;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Click += delegate { if (lblStatus.IsLink) OpenSettings(SettingsPage.Startup, true); };
            statusStrip.Items.Add(lblStatus);

            lblDirty = new ToolStripStatusLabel("");
            lblDirty.ForeColor = Color.Firebrick;
            lblDirty.ToolTipText = Loc.T("当前配置文件有未保存的修改");
            statusStrip.Items.Add(lblDirty);

            Controls.Add(statusStrip);

            // 默认高度按当前语言实测（预设组两行时多留一行）, 再按上次偏好决定「更多」是否展开
            // 自动展开时先收起, Load 里探测/读取缓存后再决定, 避免为不支持的显示器撑高窗口
            ClientSize = new Size(S(DesignClientWidth), CompactClientHeight());
            ApplyWindowSizeLimits(false);
            SetMoreExpanded(_settings.AutoExpandDdc ? false : _settings.ShowDdcPanel, false);
            ShowCycleHotkey();

            ResumeLayout(true);
        }

        private Label NewLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            return l;
        }

        private Button NewButton(string text, int x, int y, int w, int h, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.Click += onClick;
            return b;
        }

        private CheckBox NewCheck(string text, int x, int y)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.Location = new Point(x, y);
            return c;
        }

        private RadioButton NewRadio(string text, int x, int y, bool isChecked)
        {
            RadioButton r = new RadioButton();
            r.Text = text;
            r.AutoSize = true;
            r.Location = new Point(x, y);
            r.Checked = isChecked;
            return r;
        }

        /// <summary>通道单选按实际文字宽度顺排：中文"红/绿/蓝"很短, 其它语言可能长得多,
        /// 用固定坐标会在切换语言后重叠。</summary>
        /// <returns>这一排最右侧的 x, 供"同步调节所有显示器"接着摆。</returns>
        private int LayoutChannelRadios(int x, int y)
        {
            RadioButton[] radios = new RadioButton[] { rdoAll, rdoR, rdoG, rdoB };
            for (int i = 0; i < radios.Length; i++)
            {
                radios[i].Location = new Point(x, y);
                x += radios[i].PreferredSize.Width + S(10);
            }
            return x;
        }

        /// <summary>只量通道单选排的结束位置, 不移动控件（供高度预留使用）。</summary>
        private int ChannelRadiosEndX(int x)
        {
            RadioButton[] radios = new RadioButton[] { rdoAll, rdoR, rdoG, rdoB };
            for (int i = 0; i < radios.Length; i++)
                x += radios[i].PreferredSize.Width + S(10);
            return x;
        }

        // ====================================================================
        //  布局
        // ====================================================================

        /// <summary>把 96dpi 下的设计值换算成实际像素（界面字号缩放 × 系统 DPI）。</summary>
        private int S(int v)
        {
            return (int)Math.Round(v * _uiScale * _dpiScale);
        }

        /// <summary>按实际文字量按钮宽度。AutoSize=false 的 Button 上 PreferredSize 返回当前尺寸,
        /// 每次布局都会把宽度越推越大。</summary>
        private int ButtonWidth(Button b, int minWidth, int padding)
        {
            Size sz = TextRenderer.MeasureText(b.Text, b.Font);
            int w = sz.Width + S(padding);
            int min = S(minWidth);
            return w < min ? min : w;
        }

        private static float ScaleFromPercent(int percent)
        {
            if (percent <= 0) percent = 100;
            if (percent < 70) percent = 70;
            if (percent > 150) percent = 150;
            return percent / 100f;
        }

        /// <summary>全部控件的坐标都在这里算：窗口尺寸、字号缩放、语言文案长度都会影响结果。
        /// 不用 Anchor/Dock 也不依赖 AutoScale —— 窗口一变化就整体重排, 因此可以自由拉伸。</summary>
        private void LayoutUi()
        {
            if (IsDisposed) return;
            if (grpProfiles == null || statusStrip == null || lblMon == null || menuMain == null) return;
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

            SuspendLayout();
            try
            {
                int m = S(10);
                int gap = S(8);
                int W = ClientSize.Width;
                int H = ClientSize.Height;
                int statusH = statusStrip.Height;

                // ---------- 顶部工具栏（菜单栏之下）----------
                int barY = menuMain.Height + S(6);
                int barH = S(26);
                lblMon.Location = new Point(m, barY + Math.Max(0, (barH - lblMon.PreferredSize.Height) / 2));

                int refreshW = ButtonWidth(btnRefresh, 72, 18);
                int identifyW = ButtonWidth(btnIdentify, 78, 18);
                btnRefresh.SetBounds(W - m - refreshW, barY, refreshW, barH);
                btnIdentify.SetBounds(btnRefresh.Left - gap - identifyW, barY, identifyW, barH);

                int comboX = lblMon.Right + gap;
                int comboW = Math.Max(S(90), btnIdentify.Left - gap - comboX);
                cboMonitors.SetBounds(comboX, barY, comboW, S(22));

                // ---------- 纵向自下而上分配：配置文件贴底, 其余往上排 ----------
                int profileRowH = Math.Max(S(25), Font.Height + S(8));
                bool singleProfileRow = ProfileSingleRowMode(W - 2 * m);
                _profileSingleRow = singleProfileRow;
                int profilesH = S(22) + profileRowH + S(8);
                if (!singleProfileRow) profilesH += profileRowH + S(6);
                int profilesY = H - statusH - m - profilesH;

                // 循环切换热键单独一行, 位于配置文件组上方
                int cycleRowH = Math.Max(S(26), Font.Height + S(10));
                int cycleY = profilesY - gap - cycleRowH;

                // 「更多」分组: 收起时只有标题行, 展开时容纳硬件调节与分享码
                int moreHeaderH = MoreHeaderHeight();
                int moreH = moreHeaderH + (_moreExpanded ? MoreContentHeight() : 0);
                int moreY = cycleY - gap - moreH;

                int gammaY = barY + barH + gap;
                int gammaH = Math.Max(GammaMinHeight(W - 2 * m - S(6)), moreY - gap - gammaY);

                grpGamma.SetBounds(m, gammaY, W - 2 * m, gammaH);
                grpMore.SetBounds(m, moreY, W - 2 * m, moreH);
                grpProfiles.SetBounds(m, profilesY, W - 2 * m, profilesH);

                LayoutGammaInner();
                LayoutMoreInner();
                LayoutDdcInner();
                LayoutCycleRow(m, cycleY, W - 2 * m, cycleRowH);
                LayoutProfileInner();
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        /// <summary>软件调节区顶部条（通道单选 / 同步调节 / 三个按钮）的度量结果。
        /// LayoutGammaInner 用它摆控件, CompactClientHeight 用同一份结果预留窗口高度,
        /// 两处算法同源, 否则长译文下窗口会比内容矮。</summary>
        private struct GammaTopLayout
        {
            public int RadiosH;
            public int BtnH;
            public int BandH;
            public bool BtnSameLine;
            public bool ApplyAllSameLine;
            public int BtnLeft;
            public int BtnGap;
            public int CopyW;
            public int ImportW;
            public int ResetW;
        }

        /// <summary>按分组框宽度实测顶部条：按钮与单选放不下时整排按钮换到第二行,
        /// 显示"同步调节所有显示器"时再判断它是否要独占一行。</summary>
        private GammaTopLayout MeasureGammaTop(int groupW)
        {
            int pad = S(12);
            GammaTopLayout t = new GammaTopLayout();

            t.RadiosH = Math.Max(Math.Max(rdoAll.PreferredSize.Height, rdoR.PreferredSize.Height),
                Math.Max(rdoG.PreferredSize.Height, rdoB.PreferredSize.Height));
            t.BtnH = Math.Max(S(26), t.RadiosH);

            t.CopyW = ButtonWidth(btnCopyShareCode, 84, 18);
            t.ImportW = ButtonWidth(btnImportShareCode, 84, 18);
            t.ResetW = ButtonWidth(btnResetMonitor, 52, 18);
            t.BtnGap = S(6);
            t.BtnLeft = groupW - pad - t.ResetW - t.BtnGap - t.ImportW - t.BtnGap - t.CopyW;

            int radiosEndX = ChannelRadiosEndX(pad);
            t.BtnSameLine = radiosEndX <= t.BtnLeft - S(8);
            t.BandH = t.BtnSameLine ? t.BtnH : t.BtnH * 2 + S(4);

            int applyAllX = radiosEndX + S(12);
            t.ApplyAllSameLine = _applyAllShown && t.BtnSameLine &&
                applyAllX + chkApplyAll.PreferredSize.Width <= t.BtnLeft - S(8);
            if (_applyAllShown && !t.ApplyAllSameLine)
                t.BandH += chkApplyAll.PreferredSize.Height + S(4);
            return t;
        }

        /// <summary>软件调节区的最小高度：分组标题 + 顶部条 + 行距 + 三行 26 px（曲线取三行总高）+ 底部留白。</summary>
        private int GammaMinHeight(int groupW)
        {
            return S(22) + MeasureGammaTop(groupW).BandH + S(8) + 3 * S(26) + S(8);
        }

        /// <summary>软件调节区：左侧 2/3 是"标签 | 滑块 | 数值框", 右侧 1/3 是正方形曲线预览。
        /// 曲线的边长 = 三项调节的总高度, 三者严格对齐。</summary>
        private void LayoutGammaInner()
        {
            int pad = S(12);
            int gap = S(12);
            int gw = grpGamma.ClientSize.Width;
            int gh = grpGamma.ClientSize.Height;

            int bottomPad = S(8);
            int topBase = S(22);          // 分组标题占掉顶部约 16px, 单选行从 topBase 起排

            int innerW = gw - 2 * pad;

            // ---- 顶部条：左边单选, 右边"复制分享码 / 导入分享码 / 重置"；放不下时按钮另起一行 ----
            GammaTopLayout top = MeasureGammaTop(gw);
            int radiosH = top.RadiosH;
            int btnH = top.BtnH;
            int bandH = top.BandH;
            int wCopy = top.CopyW;
            int wImport = top.ImportW;
            int wReset = top.ResetW;
            int btnGap = top.BtnGap;
            int btnLeft = top.BtnLeft;
            bool btnSameLine = top.BtnSameLine;
            bool applyAllSameLine = top.ApplyAllSameLine;
            int applyAllH = chkApplyAll.PreferredSize.Height;
            int applyAllX = ChannelRadiosEndX(pad) + S(12);

            // 曲线边长：宽度 1/3, 同时不能超过可用高度（默认高度下正好是三项 26 px 行高的 3 倍）
            int avail = Math.Max(S(60), gh - topBase - bandH - S(8) - bottomPad);
            int curveSide = Math.Min(innerW / 3, avail);
            int ctrlW = innerW - curveSide - gap;

            // 内容块（顶部条 + 三行）在组内垂直居中, 窗口拉高时空白上下均分而不是全堆在下面
            int blockH = bandH + S(8) + curveSide;
            int slack = gh - topBase - blockH - bottomPad;
            int topY = topBase + Math.Max(0, slack / 2);

            int radiosY = topY + (btnH - radiosH) / 2;   // 始终与按钮同一条基线, 不随"同步调节"另起一行而下移
            LayoutChannelRadios(pad, radiosY);

            int rowsTop = topY + bandH + S(8);
            // 行高严格 = 正方形边长 / 3, 三者才能与曲线框上下对齐
            int rowH = Math.Max(S(24), curveSide / 3);

            Label[] labels = new Label[] { lblBright, lblContrast, lblGamma };
            TrackBar[] tracks = new TrackBar[] { tbBright, tbContrast, tbGamma };
            Control[] nums = new Control[] { numBright, numContrast, numGamma };

            int numW = S(58);
            int numX = pad + ctrlW - numW;
            int labelW = Math.Max(labels[0].PreferredSize.Width,
                         Math.Max(labels[1].PreferredSize.Width, labels[2].PreferredSize.Width));
            int trackX = pad + labelW + S(8);
            int trackW = Math.Max(S(60), numX - S(8) - trackX);

            for (int i = 0; i < labels.Length; i++)
            {
                int y = rowsTop + i * rowH;
                int sliderH = Math.Min(S(26), Math.Max(S(18), rowH - S(22)));
                labels[i].Location = new Point(pad, y + Math.Max(0, (S(20) - labels[i].PreferredSize.Height) / 2));
                nums[i].Location = new Point(numX, y);
                nums[i].Width = numW;
                // 滑块紧跟标签下方, 行高多出来的部分留作行间距
                tracks[i].SetBounds(trackX, y + Math.Min(rowH - sliderH, S(22)), trackW, sliderH);
            }

            // 曲线：正方形, 高度与三项调节的总高度一致
            picCurve.SetBounds(pad + ctrlW + gap, rowsTop, curveSide, curveSide);
            picCurve.Invalidate();   // SetBounds 只失效新暴露的区域, 这里强制整块按新尺寸重画

            // 三个按钮放到右上角（曲线正上方）, 顺序: 复制分享码 / 导入分享码 / 重置
            int btnY = btnSameLine ? topY : topY + btnH + S(4);
            btnCopyShareCode.SetBounds(btnLeft, btnY, wCopy, btnH);
            btnImportShareCode.SetBounds(btnLeft + wCopy + btnGap, btnY, wImport, btnH);
            btnResetMonitor.SetBounds(btnLeft + wCopy + btnGap + wImport + btnGap, btnY, wReset, btnH);

            // "同步调节所有显示器"：能并排就并排, 否则放在顶部条最后一行
            if (_applyAllShown)
            {
                if (applyAllSameLine)
                    chkApplyAll.Location = new Point(applyAllX,
                        topY + Math.Max(0, (btnH - applyAllH) / 2));
                else
                    chkApplyAll.Location = new Point(pad, topY + bandH - applyAllH - S(2));
            }
        }

        /// <summary>「更多」分组框标题行高度（分组框标题本身就占这么多）。</summary>
        private int MoreHeaderHeight()
        {
            return Math.Max(S(24), Font.Height + S(6));
        }

        /// <summary>「更多」展开时在标题行下方增加的内容高度（硬件调节区）。</summary>
        private int MoreContentHeight()
        {
            int titleH = Math.Max(S(18), Font.Height);
            return S(4) + titleH + S(4) + S(112) + S(10);
        }

        /// <summary>默认 / 最小客户区高度：紧凑基准 + 长译文多占的行。
        /// 预设组换行、软件调节区顶部条换行（含"同步调节"独占一行）都要算进来,
        /// 否则默认高度下软件调节区第三行会被「更多」分组框盖住。</summary>
        private int CompactClientHeight()
        {
            int extra = 0;
            int designW = S(DesignClientWidth) - 2 * S(10);
            if (btnSaveProfile != null && !ProfileSingleRowMode(designW))
                extra += Math.Max(S(25), Font.Height + S(8)) + S(6);

            // 顶部条比单行多出来的高度（分组框客户区比外框略窄, 这里保守少算 6 px）
            if (btnCopyShareCode != null)
            {
                GammaTopLayout top = MeasureGammaTop(designW - S(6));
                extra += Math.Max(0, top.BandH - top.BtnH);
            }
            return S(DesignCompactHeight) + extra;
        }

        /// <summary>客户区最小尺寸：展开「更多」时给硬件调节留出实际高度,
        /// 否则窗口压到最小时各分组框会互相重叠。默认高度就是紧凑基准, 所以最小 = 默认。</summary>
        private Size MinClientSize()
        {
            return new Size(S(DesignClientWidth),
                CompactClientHeight() + (_moreExpanded ? MoreContentHeight() : 0));
        }

        /// <summary>客户区最大尺寸：只允许适度拉伸, 不允许拖得很大。</summary>
        private Size MaxClientSize()
        {
            return new Size(S(DesignMaxWidth),
                S(DesignMaxHeight) + (_moreExpanded ? MoreContentHeight() : 0));
        }

        /// <summary>按当前「更多」状态刷新最小 / 最大尺寸; resize 为 true 时把窗口夹回范围内。
        /// 先清空上一组限制再改尺寸, 否则新尺寸会被上一组最小 / 最大值夹住。</summary>
        private void ApplyWindowSizeLimits(bool resize)
        {
            Size min = MinClientSize();
            Size max = MaxClientSize();

            int w = Math.Min(Math.Max(ClientSize.Width, min.Width), max.Width);
            int h = Math.Min(Math.Max(ClientSize.Height, min.Height), max.Height);

            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            if (resize && WindowState == FormWindowState.Normal &&
                (w != ClientSize.Width || h != ClientSize.Height))
            {
                ClientSize = new Size(w, h);
            }
            MinimumSize = SizeFromClientSize(min);
            MaximumSize = SizeFromClientSize(max);
        }

        /// <summary>「更多」分组框标题：展开 / 收起时同步文字与箭头。</summary>
        private void UpdateMoreHeaderText()
        {
            if (grpMore == null) return;
            grpMore.Text = _moreExpanded ? Loc.T("更多 ▴") : Loc.T("更多 ▾");
        }

        /// <summary>「更多」内部：硬件调节小标题 + 滑块区；收起时全部隐藏。</summary>
        private void LayoutMoreInner()
        {
            int pad = S(10);
            int w = grpMore.ClientSize.Width;
            int headerH = MoreHeaderHeight();

            // 标题行整行可点：收起 / 展开「更多」
            UpdateMoreHeaderText();

            lblDdcTitle.Visible = _moreExpanded;
            pnlDdc.Visible = _moreExpanded;
            if (!_moreExpanded) return;

            int innerW = w - 2 * pad;
            int titleH = Math.Max(S(18), Font.Height);
            int y = headerH + S(4);
            lblDdcTitle.SetBounds(pad, y, innerW, titleH);
            y += titleH + S(4);

            pnlDdc.SetBounds(pad, y, Math.Max(S(100), innerW), S(112));
        }

        /// <summary>硬件调节区：状态行 + 三行"标签 | 滑块 | 数值 | 量程"。</summary>
        private void LayoutDdcInner()
        {
            int pad = S(10);
            int gap = S(8);
            int pw = pnlDdc.ClientSize.Width;

            int refreshW = ButtonWidth(btnDdcRefresh, 70, 18);
            btnDdcRefresh.SetBounds(pw - pad - refreshW, S(4), refreshW, S(24));
            lblDdcStatus.SetBounds(pad, S(8), Math.Max(S(100), btnDdcRefresh.Left - gap - pad), S(18));

            Label[] names = new Label[] { lblDdcBrightName, lblDdcContrastName, lblDdcSatName };
            TrackBar[] tracks = new TrackBar[] { tbDdcBright, tbDdcContrast, tbDdcSat };
            NumericUpDown[] nums = new NumericUpDown[] { numDdcBright, numDdcContrast, numDdcSat };
            Label[] maxes = new Label[] { lblDdcBrightMax, lblDdcContrastMax, lblDdcSatMax };

            int maxW = S(36);
            int numW = S(52);
            int numX = pw - pad - maxW - numW;
            int labelW = Math.Max(names[0].PreferredSize.Width,
                         Math.Max(names[1].PreferredSize.Width, names[2].PreferredSize.Width));
            int trackX = pad + labelW + S(8);
            int trackW = Math.Max(S(50), numX - gap - trackX);

            int rowTop = S(30);
            int rowH = S(26);
            for (int i = 0; i < names.Length; i++)
            {
                int y = rowTop + i * rowH;
                names[i].Location = new Point(pad, y + Math.Max(0, (S(22) - names[i].PreferredSize.Height) / 2));
                tracks[i].SetBounds(trackX, y + S(2), trackW, S(24));
                nums[i].Location = new Point(numX, y);
                nums[i].Width = numW;
                maxes[i].Location = new Point(numX + numW + S(2), y + S(3));
            }
        }

        /// <summary>触发方式下拉的最窄宽度：只按当前语言三项实测, 长译文靠下拉层完整显示。</summary>
        private int TriggerComboWidth(ComboBox combo)
        {
            int w = S(56);
            foreach (object item in combo.Items)
                w = Math.Max(w, TextRenderer.MeasureText(item.ToString(), Font).Width + S(20));
            return w;
        }

        /// <summary>循环切换预设与编辑入口共用一行；编辑右对齐，不挤压配置文件组。</summary>
        private void LayoutCycleRow(int x, int y, int w, int h)
        {
            int editW = ButtonWidth(btnEditProfiles, 52, 16);
            int editX = x + w - editW;
            btnEditProfiles.SetBounds(editX, y, editW, h);

            int fieldW = S(120);
            int minTriggerW = S(56);
            int maxLabelW = Math.Max(S(48), w - editW - fieldW - minTriggerW - S(32));
            Size labelSize = TextRenderer.MeasureText(lblCycleHotkey.Text, lblCycleHotkey.Font);
            int labelW = Math.Min(labelSize.Width, maxLabelW);
            int labelH = labelSize.Height;
            lblCycleHotkey.AutoSize = false;
            lblCycleHotkey.AutoEllipsis = true;
            lblCycleHotkey.SetBounds(x, y + Math.Max(0, (h - labelH) / 2), labelW, labelH);

            int fieldX = x + labelW + S(8);
            pnlCycleHotkey.SetBounds(fieldX, y, fieldW, h);
            LayoutInlineField(pnlCycleHotkey, txtCycleHotkey, btnClearCycle, h);

            int triggerW = TriggerComboWidth(cboCycleTrigger);
            int triggerX = fieldX + fieldW + S(8);
            int availableW = Math.Max(minTriggerW, editX - S(8) - triggerX);
            cboCycleTrigger.SetBounds(triggerX, y, Math.Min(triggerW, availableW), h);
            cboCycleTrigger.DropDownWidth = Math.Max(triggerW, MaxComboItemWidth(cboCycleTrigger));
        }

        private int ProfileButtonsWidth()
        {
            return ButtonWidth(btnSaveProfile, 42, 12) + ButtonWidth(btnNewProfile, 42, 12) + S(6);
        }

        /// <summary>热键 / 触发方式与预设、保存 / 新建同处一行；长译文下回退成两行, 避免溢出。</summary>
        private bool ProfileSingleRowMode(int pw)
        {
            int fixedW = S(20) + ProfileButtonsWidth() + TriggerComboWidth(cboHotkeyTrigger) + S(104) + S(18);
            return pw - fixedW >= S(100);   // 预设下拉至少留 100 px, 长译文语言自动回退成两行
        }

        private int MaxComboItemWidth(ComboBox combo)
        {
            int w = S(80);
            foreach (object item in combo.Items)
                w = Math.Max(w, TextRenderer.MeasureText(item.ToString(), combo.Font).Width + S(20));
            return w;
        }

        /// <summary>输入框内部右侧的 ×：文本框与按钮同处一个细边框容器, 避免被原生 TextBox 盖住。</summary>
        private void LayoutInlineField(Panel panel, TextBox box, Button clear, int height)
        {
            int clearW = S(18);
            int innerH = Math.Max(S(18), height - S(4));
            int boxW = Math.Max(S(20), panel.ClientSize.Width - clearW - S(5));
            box.SetBounds(S(3), Math.Max(0, (panel.ClientSize.Height - box.Height) / 2),
                boxW, box.Height);
            clear.SetBounds(panel.ClientSize.Width - clearW - S(2),
                Math.Max(0, (panel.ClientSize.Height - innerH) / 2), clearW, innerH);
        }

        private void LayoutProfileInner()
        {
            int pad = S(10);
            int pw = grpProfiles.ClientSize.Width;
            int rowH = Math.Max(S(25), Font.Height + S(8));
            int y = S(22);

            int newW = ButtonWidth(btnNewProfile, 42, 12);
            int saveW = ButtonWidth(btnSaveProfile, 42, 12);
            int buttonsW = saveW + newW + S(6);
            int triggerW = TriggerComboWidth(cboHotkeyTrigger);
            int hotW = S(104);

            if (_profileSingleRow)
            {
                // ---- 一行：预设 → 热键 → 触发方式 → 保存 / 新建 ----
                int comboW = pw - S(20) - buttonsW - triggerW - hotW - S(18);
                cboProfiles.SetBounds(pad, y, comboW, rowH);
                cboProfiles.DropDownWidth = Math.Max(comboW, MaxComboItemWidth(cboProfiles));

                int x = pad + comboW + S(6);
                pnlHotkey.SetBounds(x, y, hotW, rowH);
                LayoutInlineField(pnlHotkey, txtHotkey, btnClearHotkey, rowH);
                x += hotW + S(6);

                cboHotkeyTrigger.SetBounds(x, y, triggerW, rowH);
                cboHotkeyTrigger.DropDownWidth = Math.Max(triggerW, MaxComboItemWidth(cboHotkeyTrigger));
                x += triggerW + S(6);

                btnSaveProfile.SetBounds(x, y, saveW, rowH);
                x += saveW + S(6);
                btnNewProfile.SetBounds(x, y, newW, rowH);

                y += rowH + S(8);
            }
            else
            {
                // ---- 长译文回退：第一行预设 + 保存 / 新建, 第二行热键 + 触发方式 ----
                int comboW = Math.Max(S(90), pw - S(20) - buttonsW - S(6));
                cboProfiles.SetBounds(pad, y, comboW, rowH);
                cboProfiles.DropDownWidth = Math.Max(comboW, MaxComboItemWidth(cboProfiles));

                int x = pw - pad - buttonsW;
                btnSaveProfile.SetBounds(x, y, saveW, rowH);
                x += saveW + S(6);
                btnNewProfile.SetBounds(x, y, newW, rowH);

                y += rowH + S(6);
                pnlHotkey.SetBounds(pad, y, hotW, rowH);
                LayoutInlineField(pnlHotkey, txtHotkey, btnClearHotkey, rowH);
                int xTrigger = pad + hotW + S(6);
                cboHotkeyTrigger.SetBounds(xTrigger, y, Math.Max(S(60), pw - pad - xTrigger), rowH);
                cboHotkeyTrigger.DropDownWidth = Math.Max(cboHotkeyTrigger.Width,
                    MaxComboItemWidth(cboHotkeyTrigger));
                y += rowH + S(6);
            }
        }

        /// <summary>切换字号预设：改字体 + 把窗口恢复到设计基准尺寸, 然后整体重排（不需要重启）。
        /// 窗口尺寸一律按基准重算 —— 100% 回到默认启动大小, 不按手动拖过的当前尺寸做比例缩放。</summary>
        private void ApplyUiScale(float scale)
        {
            if (scale < 0.7f) scale = 0.7f;
            if (scale > 1.5f) scale = 1.5f;

            _uiScale = scale;

            SuspendLayout();
            try
            {
                Font = Loc.UiFont(9f * scale);
                if (menuMain != null)
                {
                    menuMain.Font = Font;
                    menuMain.PerformLayout();
                }
                if (btnClearHotkey != null)
                {
                    btnClearHotkey.Font = new Font(Font.FontFamily, 10f * scale, FontStyle.Bold);
                    btnClearCycle.Font = new Font(Font.FontFamily, 10f * scale, FontStyle.Bold);
                }

                int wantW = S(DesignClientWidth);
                int wantH = CompactClientHeight() + (_moreExpanded ? MoreContentHeight() : 0);

                // 先放开旧的最小 / 最大尺寸, 否则按新基准重算时会被上一组限制夹住
                MinimumSize = Size.Empty;
                MaximumSize = Size.Empty;
                if (WindowState != FormWindowState.Minimized)
                    ClientSize = new Size(wantW, wantH);
                ApplyWindowSizeLimits(true);
            }
            finally
            {
                ResumeLayout(true);
            }

            if (WindowState != FormWindowState.Minimized)
                LayoutUi();
        }


        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                if (!_suppressMinimizeHide && _settings != null && _settings.MinimizeToTray &&
                    !_exitRequested && !_restarting && !_hiddenToTray)
                {
                    _hiddenToTray = true;
                    BeginInvoke((MethodInvoker)HideToTrayFromMinimize);
                }
                return;
            }
            _hiddenToTray = false;
            LayoutUi();
        }

        /// <summary>最小化到托盘：Hide() 会同时去掉任务栏按钮, 只留右下角通知区域图标。</summary>
        private void HideToTrayFromMinimize()
        {
            if (IsDisposed) return;
            if (!Visible || WindowState != FormWindowState.Minimized ||
                _settings == null || !_settings.MinimizeToTray)
            {
                _hiddenToTray = false;
                return;
            }
            Hide();
            if (_settings.NotifyEnabled && _settings.NotifyTrayOnClose)
                ToastForm.ShowMessage(Loc.T("仍在后台运行, 双击托盘图标可重新打开"), _uiScale, _dpiScale);
        }

        private TrackBar NewTrack(int min, int max, int tick, int x, int y, int w)
        {
            TrackBar t = new TrackBar();
            t.Minimum = min;
            t.Maximum = max;
            t.TickFrequency = tick;
            t.AutoSize = false;          // 默认自适应高度约 45px, 会与下一行重叠
            t.SetBounds(x, y, w, 32);
            t.Value = Math.Max(min, Math.Min(max, 0));
            return t;
        }

        private NumericUpDown NewNum(int min, int max, int val, int x, int y)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.Value = val;
            n.SetBounds(x, y, 62, 24);
            return n;
        }

        /// <summary>软件调节行：标签 | 滑块 | 数值框 同处一行。具体坐标由 LayoutGammaInner 计算,
        /// 这里只负责创建控件并返回标签（布局需要它的实际宽度, 各语言文案差别很大）。</summary>
        private Label AddRow(Control grp, string caption, int top, TrackBar tb, Control num)
        {
            Label l = NewLabel(caption, 14, top);
            grp.Controls.Add(l);
            if (num != null) grp.Controls.Add(num);
            grp.Controls.Add(tb);
            return l;
        }

        /// <summary>硬件调节行：标签 + 滑块 + 可输入数值框 + 量程提示, 一行放完不增加高度。</summary>
        private Label AddDdcRow(Control grp, string caption, int trackY, TrackBar tb, NumericUpDown num, Label maxLabel)
        {
            Label l = NewLabel(caption, 12, trackY + 6);
            grp.Controls.Add(l);
            grp.Controls.Add(tb);
            grp.Controls.Add(num);
            grp.Controls.Add(maxLabel);
            return l;
        }

        /// <summary>软件 Gamma 滑块与数字框双向绑定；scale 是 TrackBar 值相对数字值的倍率。</summary>
        private void WireSlider(TrackBar tb, NumericUpDown num, int scale)
        {
            tb.Scroll += delegate
            {
                if (_loadingUi) return;
                _loadingUi = true;
                decimal v = (decimal)tb.Value / scale;
                if (v < num.Minimum) v = num.Minimum;
                if (v > num.Maximum) v = num.Maximum;
                num.Value = v;
                _loadingUi = false;
                CommitSliderChange();
            };
            num.ValueChanged += delegate
            {
                if (_loadingUi) return;
                _loadingUi = true;
                int tv = (int)Math.Round(num.Value * scale);
                if (tv < tb.Minimum) tv = tb.Minimum;
                if (tv > tb.Maximum) tv = tb.Maximum;
                tb.Value = tv;
                _loadingUi = false;
                CommitSliderChange();
            };
        }

        /// <summary>硬件调节的滑块与数值框双向绑定, 两者都走同一条 DDC 去抖写入队列。</summary>
        private void WireDdcControls(TrackBar tb, NumericUpDown num, byte code)
        {
            tb.Scroll += delegate
            {
                if (_loadingUi) return;
                _loadingUi = true;
                num.Value = Clamp(tb.Value, (int)num.Minimum, (int)num.Maximum);
                _loadingUi = false;
                EnqueueDdcWrite(code, tb.Value);
            };
            num.ValueChanged += delegate
            {
                if (_loadingUi) return;
                _loadingUi = true;
                int v = Clamp((int)num.Value, tb.Minimum, tb.Maximum);
                tb.Value = v;
                _loadingUi = false;
                EnqueueDdcWrite(code, v);
            };
        }

        /// <summary>把一次硬件写入排进去抖队列（目标显示器在触发瞬间确定, 之后切换选中项不会写错屏）。</summary>
        private void EnqueueDdcWrite(byte code, int value)
        {
            DisplayMonitor m = SelectedMonitor();
            if (m == null) return;

            List<DisplayMonitor> newTargets = new List<DisplayMonitor>();
            if (chkApplyAll.Checked) newTargets.AddRange(_monitors);
            else newTargets.Add(m);

            // 去抖窗口内目标变了 (切换了显示器): 先把旧批次按旧目标冲刷出去, 不能混入新目标
            bool flushOld;
            lock (_pendingDdcWrites)
            {
                flushOld = _pendingDdcWrites.Count > 0 && !TargetsEqual(_pendingDdcTargets, newTargets);
            }
            if (flushOld) FlushDdcWrites();

            lock (_pendingDdcWrites)
            {
                _pendingDdcWrites[code] = (uint)value;
                _pendingDdcTargets.Clear();
                _pendingDdcTargets.AddRange(newTargets);
            }
            _ddcSetTimer.Stop();
            _ddcSetTimer.Start();
        }

        private static bool TargetsEqual(List<DisplayMonitor> a, List<DisplayMonitor> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        // ====================================================================
        //  托盘
        // ====================================================================

        private void BuildTray()
        {
            trayMenu = new ContextMenuStrip();
            RebuildTrayMenu();

            trayIcon = new NotifyIcon();
            // 托盘按小图标尺寸取帧, 直接用 32px 帧会被系统缩放得发虚
            trayIcon.Icon = AppIcon.Load(SystemInformation.SmallIconSize.Width);
            trayIcon.Text = AppTitle;
            trayIcon.Visible = true;
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += delegate { ShowMainWindow(); };
        }

        private void RebuildTrayMenu()
        {
            trayMenu.Items.Clear();
            ToolStripMenuItem show = new ToolStripMenuItem(Loc.T("显示主窗口"));
            show.Font = new Font(trayMenu.Font, FontStyle.Bold);
            show.Click += delegate { ShowMainWindow(); };
            trayMenu.Items.Add(show);
            trayMenu.Items.Add(new ToolStripSeparator());

            if (_settings.Profiles.Count > 0)
            {
                foreach (Profile p in _settings.Profiles)
                {
                    Profile captured = p;
                    string text = p.Name;
                    if (p.HotKey != 0) text += "  (" + HotkeyRules.Format(p.HotKey, p.HotKeyTrigger) + ")";
                    ToolStripMenuItem item = new ToolStripMenuItem(text);
                    item.Checked = (p == _activeProfile);   // 勾出当前生效的配置
                    item.Click += delegate { SwitchToProfile(captured); };
                    trayMenu.Items.Add(item);
                }
                trayMenu.Items.Add(new ToolStripSeparator());
            }

            ToolStripMenuItem reset = new ToolStripMenuItem(Loc.T("重置所有显示器"));
            reset.Click += delegate { ResetAllMonitors(); };
            trayMenu.Items.Add(reset);
            trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem about = new ToolStripMenuItem(Loc.T("关于 GammaLift"));
            about.Click += delegate { ShowMainWindow(); OpenAbout(); };
            trayMenu.Items.Add(about);
            trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitKeep = new ToolStripMenuItem(Loc.T("退出 (保留当前色彩)"));
            exitKeep.Click += delegate { ExitApp(false); };
            trayMenu.Items.Add(exitKeep);

            ToolStripMenuItem exitRestore = new ToolStripMenuItem(Loc.T("退出并恢复默认色彩"));
            exitRestore.Click += delegate { ExitApp(true); };
            trayMenu.Items.Add(exitRestore);
        }

        private void ShowMainWindow()
        {
            _suppressMinimizeHide = true;
            try
            {
                if (!Visible) Show();
                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;
                Activate();
            }
            finally
            {
                _suppressMinimizeHide = false;
                _hiddenToTray = false;
            }
            LayoutUi();
        }

        // ====================================================================
        //  显示器枚举与选择
        // ====================================================================

        private void RefreshMonitors(bool reapply)
        {
            string prevKey = null;
            DisplayMonitor prev = SelectedMonitor();
            if (prev != null) prevKey = prev.StableKey;

            MonitorService.ReleaseAll(_monitors);
            _monitors = MonitorService.Enumerate();
            ResolveBaselines();   // 先读硬件 LUT, 再重新应用

            _loadingUi = true;
            cboMonitors.Items.Clear();
            foreach (DisplayMonitor m in _monitors)
                cboMonitors.Items.Add(m);

            int sel = 0;
            for (int i = 0; i < _monitors.Count; i++)
            {
                if (prevKey != null && _monitors[i].StableKey == prevKey) { sel = i; break; }
                if (prevKey == null && _monitors[i].IsPrimary) sel = i;
            }
            if (cboMonitors.Items.Count > 0)
                cboMonitors.SelectedIndex = sel;
            _loadingUi = false;

            // "同步调节所有显示器" 只在多显示器时才有意义
            _applyAllShown = _monitors.Count > 1;
            chkApplyAll.Visible = _applyAllShown;
            if (_monitors.Count <= 1) chkApplyAll.Checked = false;
            ApplyWindowSizeLimits(true);   // 顶部条可能因"同步调节"多占一行, 窗口最小高度要同步
            LayoutUi();   // 顶部条高度取决于"同步调节"是否显示, 可见性变了要重排

            if (reapply)
                ReapplyNonNeutral();

            LoadMonitorToUi();

            // 后台探测所有显示器的 DDC/CI 支持情况；「更多」收起且未开启自动展开时完全不碰 DDC
            // （部分显示器/转接器读取 DDC 会闪屏或明显变慢, 没必要为隐藏的 UI 付这个代价）
            // 自动展开收起时只需探测当前显示器, 展开时仍探测全部
            if (_moreExpanded || _settings.AutoExpandDdc)
            {
                List<DisplayMonitor> snapshot = new List<DisplayMonitor>(_monitors);
                List<DisplayMonitor> probes = new List<DisplayMonitor>();
                if (_moreExpanded)
                {
                    probes.AddRange(snapshot);
                }
                else
                {
                    DisplayMonitor cur = SelectedMonitor();
                    if (cur != null) probes.Add(cur);
                }
                // 自动策略可能已经在探测同一台, 不重复探测
                probes.RemoveAll(delegate(DisplayMonitor m) { return _ddcAutoProbing.Contains(m.StableKey); });

                ThreadPool.QueueUserWorkItem(delegate
                {
                    foreach (DisplayMonitor m in probes)
                    {
                        try { MonitorService.ProbeDdc(m); }
                        catch (Exception) { }
                    }
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            // 若探测期间列表已被新一轮刷新替换, 释放被淘汰对象上重新获取的句柄, 防止泄漏
                            foreach (DisplayMonitor m in snapshot)
                            {
                                if (!_monitors.Contains(m))
                                    MonitorService.ReleaseDdc(m);
                            }
                            foreach (DisplayMonitor m in probes)
                            {
                                if (_monitors.Contains(m))
                                    RecordDdcCapability(m);
                            }

                            bool readStarted = ApplyAutoDdcPolicy();
                            DisplayMonitor cur = SelectedMonitor();
                            if (_moreExpanded && !readStarted && cur != null && snapshot.Contains(cur))
                                LoadDdcToUi(false);
                        });
                    }
                    catch (Exception) { }
                });
            }

            SetStatus(Loc.F("检测到 {0} 台显示器", _monitors.Count));
        }

        private DisplayMonitor SelectedMonitor()
        {
            return cboMonitors.SelectedItem as DisplayMonitor;
        }

        private MonitorConfig GetConfig(DisplayMonitor m)
        {
            MonitorConfig cfg;
            if (!_settings.Current.TryGetValue(m.StableKey, out cfg))
            {
                cfg = new MonitorConfig();
                _settings.Current[m.StableKey] = cfg;
            }
            return cfg;
        }

        // ---------- 基线 LUT (保留系统 ICC 校准) ----------

        /// <summary>为每台显示器确定基线 LUT：首次见到时捕获当前硬件曲线；
        /// 若硬件曲线与"基线+已存配置"的预期不符 (说明外部已重新校准或注销重置), 则以当前硬件曲线为新基线。</summary>
        private void ResolveBaselines()
        {
            foreach (DisplayMonitor m in _monitors)
            {
                ushort[] hw = MonitorService.GetGammaRamp(m.DeviceName);
                if (hw == null) continue;

                List<int> stored;
                if (!_settings.Baselines.TryGetValue(m.StableKey, out stored) || stored == null || stored.Count != 768)
                {
                    _settings.Baselines[m.StableKey] = ToIntList(hw);
                    ScheduleSave();
                    continue;
                }

                MonitorConfig cfg;
                if (!_settings.Current.TryGetValue(m.StableKey, out cfg)) cfg = new MonitorConfig();
                ushort[] expected = GammaMath.BuildRamp(cfg, ToRamp(stored));
                if (MaxAbsDiff(hw, expected) > 1500)   // 容忍驱动量化误差, 但能识别 vcgt/夜间模式级别的变化
                {
                    _settings.Baselines[m.StableKey] = ToIntList(hw);
                    ScheduleSave();
                }
            }
        }

        private ushort[] GetBaseline(DisplayMonitor m)
        {
            List<int> stored;
            if (_settings.Baselines.TryGetValue(m.StableKey, out stored) && stored != null && stored.Count == 768)
                return ToRamp(stored);
            return null;
        }

        private static List<int> ToIntList(ushort[] ramp)
        {
            List<int> list = new List<int>(768);
            for (int i = 0; i < 768; i++) list.Add(ramp[i]);
            return list;
        }

        private static ushort[] ToRamp(List<int> list)
        {
            ushort[] ramp = new ushort[768];
            for (int i = 0; i < 768; i++)
            {
                int v = list[i];
                if (v < 0) v = 0;
                if (v > 65535) v = 65535;
                ramp[i] = (ushort)v;
            }
            return ramp;
        }

        private static int MaxAbsDiff(ushort[] a, ushort[] b)
        {
            int max = 0;
            for (int i = 0; i < 768; i++)
            {
                int d = Math.Abs(a[i] - b[i]);
                if (d > max) max = d;
            }
            return max;
        }

        /// <summary>统一的 Gamma 应用入口：在基线之上合成配置。</summary>
        private bool ApplyConfig(DisplayMonitor m, MonitorConfig cfg)
        {
            return MonitorService.ApplyGamma(m.DeviceName, cfg, GetBaseline(m));
        }

        /// <summary>把选中显示器的配置载入右侧面板。</summary>
        private void LoadMonitorToUi()
        {
            if (_loadingUi) return;
            DisplayMonitor m = SelectedMonitor();
            if (m == null) return;

            MonitorConfig cfg = GetConfig(m);
            SyncRadioToConfig(cfg);

            LoadChannelToSliders();
            picCurve.Invalidate();
            bool readStarted = ApplyAutoDdcPolicy();   // 自动展开会跟随当前显示器伸缩
            if (_moreExpanded && !readStarted) LoadDdcToUi(false);
            UpdateDirtyMark();
            UpdateShareButtons();
        }

        /// <summary>让通道单选按钮如实反映配置：联动配置显示"RGB 联动",
        /// 分通道配置切到单通道视图 — 否则滑块显示红通道值却按联动提交, 会悄悄毁掉绿/蓝通道。</summary>
        private void SyncRadioToConfig(MonitorConfig cfg)
        {
            _loadingUi = true;
            if (cfg.Linked)
            {
                rdoAll.Checked = true;
            }
            else if (rdoAll.Checked)
            {
                if (_channelIdx == 1) rdoG.Checked = true;
                else if (_channelIdx == 2) rdoB.Checked = true;
                else rdoR.Checked = true;
            }
            _loadingUi = false;
        }

        private void LoadChannelToSliders()
        {
            DisplayMonitor m = SelectedMonitor();
            if (m == null) return;
            MonitorConfig cfg = GetConfig(m);
            ChannelSettings cs = rdoAll.Checked ? cfg.Red : cfg.GetChannel(_channelIdx);

            _loadingUi = true;
            tbBright.Value = Clamp(cs.Brightness, tbBright.Minimum, tbBright.Maximum);
            numBright.Value = tbBright.Value;
            tbContrast.Value = Clamp(cs.Contrast, tbContrast.Minimum, tbContrast.Maximum);
            numContrast.Value = tbContrast.Value;
            int g = (int)Math.Round(cs.Gamma * 100.0);
            tbGamma.Value = Clamp(g, tbGamma.Minimum, tbGamma.Maximum);
            numGamma.Value = (decimal)tbGamma.Value / 100;
            _loadingUi = false;
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        // ====================================================================
        //  软件 Gamma 提交
        // ====================================================================

        /// <summary>滑块变动 → 写入配置并立即应用。</summary>
        private void CommitSliderChange()
        {
            DisplayMonitor m = SelectedMonitor();
            if (m == null) return;
            MonitorConfig cfg = GetConfig(m);

            int bright = (int)numBright.Value;
            int contrast = (int)numContrast.Value;
            double gamma = (double)numGamma.Value;

            if (rdoAll.Checked)
            {
                SetChannel(cfg.Red, bright, contrast, gamma);
                SetChannel(cfg.Green, bright, contrast, gamma);
                SetChannel(cfg.Blue, bright, contrast, gamma);
                cfg.Linked = true;
            }
            else
            {
                SetChannel(cfg.GetChannel(_channelIdx), bright, contrast, gamma);
                cfg.Linked = false;
            }

            if (chkApplyAll.Checked)
            {
                foreach (DisplayMonitor other in _monitors)
                {
                    if (other != m)
                        _settings.Current[other.StableKey] = cfg.Clone();
                }
                ApplyToMonitors(_monitors);
            }
            else
            {
                ApplyToMonitors(new List<DisplayMonitor> { m });
            }

            picCurve.Invalidate();
            ScheduleSave();
            UpdateDirtyMark();
            UpdateShareButtons();
        }

        private static void SetChannel(ChannelSettings cs, int bright, int contrast, double gamma)
        {
            cs.Brightness = bright;
            cs.Contrast = contrast;
            cs.Gamma = gamma;
        }

        private void ApplyToMonitors(List<DisplayMonitor> targets)
        {
            bool anyRejected = false;
            foreach (DisplayMonitor m in targets)
            {
                MonitorConfig cfg = GetConfig(m);
                if (!ApplyConfig(m, cfg))
                    anyRejected = true;
            }
            ShowRampRejected(anyRejected);
        }

        /// <summary>数值被系统拒绝时, 提示放在状态栏的可点击链接里, 不占用主界面一行。</summary>
        private void ShowRampRejected(bool rejected)
        {
            _rampRejected = rejected;
            RenderStatus();
        }

        /// <summary>重新应用所有已连接且非默认值的显示器配置。返回应用台数。</summary>
        private int ReapplyNonNeutral()
        {
            int n = 0;
            bool anyRejected = false;
            foreach (DisplayMonitor m in _monitors)
            {
                MonitorConfig cfg;
                if (_settings.Current.TryGetValue(m.StableKey, out cfg) && !cfg.IsNeutral())
                {
                    if (!ApplyConfig(m, cfg))
                        anyRejected = true;
                    n++;
                }
            }
            if (anyRejected)
                ShowRampRejected(true);
            return n;
        }

        private void ResetMonitor()
        {
            DisplayMonitor m = SelectedMonitor();
            if (m == null) return;
            MonitorConfig cfg = new MonitorConfig();
            _settings.Current[m.StableKey] = cfg;
            ApplyConfig(m, cfg);   // 中性配置 = 精确还原基线 (含系统 ICC 校准)
            ShowRampRejected(false);   // 中性配置不会再被系统拒绝, 清掉残留的提示
            SyncRadioToConfig(cfg);
            LoadChannelToSliders();
            picCurve.Invalidate();
            ScheduleSave();
            UpdateDirtyMark();
            UpdateShareButtons();
            SetStatus(Loc.F("{0} 已重置", m.ToString()));
        }

        private void ResetAllMonitors()
        {
            MonitorConfig last = null;
            foreach (DisplayMonitor m in _monitors)
            {
                MonitorConfig cfg = new MonitorConfig();
                _settings.Current[m.StableKey] = cfg;
                ApplyConfig(m, cfg);
                last = cfg;
            }
            ShowRampRejected(false);
            if (last != null) SyncRadioToConfig(last);
            LoadChannelToSliders();
            picCurve.Invalidate();
            ScheduleSave();
            UpdateDirtyMark();
            UpdateShareButtons();
            SetStatus(Loc.T("所有显示器已重置为默认"));
        }

        // ====================================================================
        //  曲线预览
        // ====================================================================

        private void PaintCurve(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int w = picCurve.ClientSize.Width;
            int h = picCurve.ClientSize.Height;
            if (w <= 2 || h <= 2) return;

            // 先铺底色: 缓冲区里可能残留上一次尺寸的画面, 不覆盖就会看到旧曲线
            g.Clear(picCurve.BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen grid = new Pen(Color.FromArgb(55, 55, 60)))
            {
                for (int i = 1; i < 4; i++)
                {
                    g.DrawLine(grid, w * i / 4, 0, w * i / 4, h);
                    g.DrawLine(grid, 0, h * i / 4, w, h * i / 4);
                }
            }
            using (Pen diag = new Pen(Color.FromArgb(90, 90, 95)))
            {
                diag.DashStyle = DashStyle.Dash;
                g.DrawLine(diag, 0, h - 1, w - 1, 0);
            }

            DisplayMonitor m = SelectedMonitor();
            if (m != null)
            {
                MonitorConfig cfg = GetConfig(m);

                bool same = ChannelsEqual(cfg.Red, cfg.Green) && ChannelsEqual(cfg.Green, cfg.Blue);
                if (same)
                {
                    DrawChannelCurve(g, cfg.Red, Color.Gainsboro, w, h);
                }
                else
                {
                    DrawChannelCurve(g, cfg.Red, Color.FromArgb(235, 80, 80), w, h);
                    DrawChannelCurve(g, cfg.Green, Color.FromArgb(90, 220, 110), w, h);
                    DrawChannelCurve(g, cfg.Blue, Color.FromArgb(100, 140, 250), w, h);
                }
            }

            // 自己补一圈边框: 开启 AllPaintingInWmPaint 后默认边框绘制被接管
            using (Pen edge = new Pen(Color.FromArgb(100, 100, 100)))
                g.DrawRectangle(edge, 0, 0, w - 1, h - 1);
        }

        private static bool ChannelsEqual(ChannelSettings a, ChannelSettings b)
        {
            return a.Brightness == b.Brightness && a.Contrast == b.Contrast && Math.Abs(a.Gamma - b.Gamma) < 0.001;
        }

        private void DrawChannelCurve(Graphics g, ChannelSettings cs, Color color, int w, int h)
        {
            PointF[] pts = new PointF[64];
            for (int i = 0; i < 64; i++)
            {
                int idx = i * 255 / 63;
                double v = GammaMath.Evaluate(cs, idx);
                pts[i] = new PointF((float)(idx / 255.0 * (w - 1)), (float)((1.0 - v) * (h - 1)));
            }
            using (Pen p = new Pen(color, 1.6f))
            {
                g.DrawLines(p, pts);
            }
        }

        // ====================================================================
        //  DDC/CI
        // ====================================================================

        /// <summary>展开/收起「更多」：硬件调节与分享码一起显示，窗体高度与整体布局同步调整。</summary>
        private void SetMoreExpanded(bool expanded, bool persist)
        {
            SetMoreExpandedCore(expanded, persist, true);
        }

        /// <summary>forceDdcRead=false 时不强制重探 DDC（自动展开已用缓存/刚探测过的结果, 直接读值即可）。</summary>
        private void SetMoreExpandedCore(bool expanded, bool persist, bool forceDdcRead)
        {
            bool changed = (_moreExpanded != expanded);
            _moreExpanded = expanded;
            UpdateMoreHeaderText();

            if (grpProfiles == null) return;

            // 展开/收起时把窗口高度同步加上/减掉, 否则软件调节区会被挤扁。
            // 先放开旧限制, 收起时窗口才不会被展开态的最小高度顶住。
            if (changed)
            {
                int delta = MoreContentHeight();
                int newH = ClientSize.Height + (expanded ? delta : -delta);
                MinimumSize = Size.Empty;
                MaximumSize = Size.Empty;
                ClientSize = new Size(ClientSize.Width, newH);
            }
            ApplyWindowSizeLimits(true);

            LayoutUi();

            // 展开时强制重新探测: 之前后台探测失败的结果可能已经过期（例如显示器刚唤醒/菜单里重新打开 DDC）。
            // 窗体还没 Load 完时先跳过 —— 那时 BeginInvoke 没有句柄, 结果会丢; Load 里会补上读取。
            if (expanded && _loaded && SelectedMonitor() != null)
                LoadDdcToUi(forceDdcRead);

            // 自动展开模式下展开状态由显示器能力决定, 不写回「启动时展开」偏好
            if (persist && !_settings.AutoExpandDdc)
            {
                _settings.ShowDdcPanel = expanded;
                ScheduleSave();
            }
        }

        /// <summary>「自动展开」：按选中显示器缓存/探测到的 DDC/CI 能力决定「更多」是否展开。
        /// 能力未知时保持现状并在后台探测, 探测完成后再决定。返回是否已开始读取 DDC 滑块值。</summary>
        private bool ApplyAutoDdcPolicy()
        {
            if (!_settings.AutoExpandDdc) return false;
            DisplayMonitor m = SelectedMonitor();
            if (m == null) return false;

            DdcCapability cap = null;
            bool known = _settings.DdcCapabilities != null &&
                         _settings.DdcCapabilities.TryGetValue(m.StableKey, out cap) && cap != null;
            if (!known)
                return ProbeDdcForAuto(m);   // 探测完成后由回调决定展开并读取滑块值

            bool supported = cap.Brightness || cap.Contrast || cap.Saturation;
            if (supported == _moreExpanded) return false;

            SetMoreExpandedCore(supported, false, false);
            return supported && _loaded;
        }

        /// <summary>自动展开需要的后台探测：只探测、记录能力，完成后回到 UI 线程重新套用策略。
        /// 返回 true 表示已经有人在探测（回调会负责后续读取）, 调用方据此跳过重复读取。</summary>
        private bool ProbeDdcForAuto(DisplayMonitor target)
        {
            if (!_loaded || target == null) return false;
            if (_ddcAutoProbing.Contains(target.StableKey)) return true;
            _ddcAutoProbing.Add(target.StableKey);

            ThreadPool.QueueUserWorkItem(delegate
            {
                try { MonitorService.ProbeDdc(target); }
                catch (Exception) { }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _ddcAutoProbing.Remove(target.StableKey);
                        if (!_monitors.Contains(target))
                        {
                            MonitorService.ReleaseDdc(target);
                            return;
                        }
                        RecordDdcCapability(target);

                        bool readStarted = false;
                        if (_settings.AutoExpandDdc && SelectedMonitor() == target)
                            readStarted = ApplyAutoDdcPolicy();
                        if (!readStarted && _moreExpanded && SelectedMonitor() == target)
                            LoadDdcToUi(false);   // 能力已确认但没有换展开状态时, 也要把滑块值读出来
                    });
                }
                catch (Exception) { }
            });
            return true;
        }

        /// <summary>把已经探测过的能力缓存到配置里, 供下次启动即时判断是否自动展开。</summary>
        private void RecordDdcCapability(DisplayMonitor m)
        {
            if (m == null) return;
            if (_settings.DdcCapabilities == null)
                _settings.DdcCapabilities = new Dictionary<string, DdcCapability>();
            DdcCapability cap = new DdcCapability();
            cap.Brightness = m.DdcBrightnessOk;
            cap.Contrast = m.DdcContrastOk;
            cap.Saturation = m.DdcSaturationOk;
            _settings.DdcCapabilities[m.StableKey] = cap;
            ScheduleSave();
        }

        /// <summary>「设置」菜单的二级项：每项打开独立的一页设置窗口。</summary>
        private void AddSettingsEntry(ToolStripMenuItem parent, string name, string text, SettingsPage page)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(Loc.T(text));
            item.Name = name;
            item.Click += delegate { OpenSettings(page, false); };
            parent.DropDownItems.Add(item);
        }

        /// <summary>打开单页模态设置窗口，并把可能变化的开关同步回运行时状态。</summary>
        private void OpenSettings(SettingsPage page, bool focusUnlock)
        {
            string langBefore = _settings.Language;

            using (SettingsForm f = new SettingsForm(_settings, page, focusUnlock, _uiScale, _dpiScale))
            {
                ShowOwnedDialog(f);
            }

            if (!_settings.NotifyEnabled) ToastForm.CloseCurrent();
            if (_settings.KeepApplied)
            {
                if (!_keepTimer.Enabled) _keepTimer.Start();
            }
            else
            {
                _keepTimer.Stop();
            }

            if (_settings.AutoExpandDdc)
                ApplyAutoDdcPolicy();
            else if (_moreExpanded != _settings.ShowDdcPanel)
                SetMoreExpanded(_settings.ShowDdcPanel, false);

            float scale = ScaleFromPercent(_settings.UiScalePercent);
            if (Math.Abs(scale - _uiScale) > 0.0001f)
                ApplyUiScale(scale);

            ScheduleSave();

            // 界面文案是构建窗体时取词的, 运行中无法原地换语言, 询问是否立刻重启
            if (!string.Equals(langBefore, _settings.Language, StringComparison.Ordinal))
                PromptRestartForLanguage();
        }

        /// <summary>打开模态「关于」对话框（版本、版权、MIT 全文与项目仓库）。</summary>
        private void OpenAbout()
        {
            using (AboutForm f = new AboutForm(_uiScale, _dpiScale))
                ShowOwnedDialog(f);
        }

        /// <summary>语言变更后询问是否重启；选"否"则下次启动生效。
        /// 确认框的文案用切换后的目标语言显示（重启前就让人看到新语言）, 按钮固定英文 Yes / No。</summary>
        private void PromptRestartForLanguage()
        {
            string targetCode = Loc.Resolve(_settings.Language);
            bool restart;
            using (ConfirmForm f = new ConfirmForm(
                       Loc.TIn(targetCode, "语言已更改, 需要重启程序后生效。\r\n\r\n现在重启?"),
                       targetCode, _uiScale, _dpiScale))
            {
                restart = ShowOwnedDialog(f) == DialogResult.Yes;
            }
            if (!restart)
                return;

            SettingsStore.Save(_settings);
            _restarting = true;
            _exitRequested = true;   // 重启路径真正退出: 走"关闭到托盘"会让新旧进程并存

            // 互斥锁不提前释放: 新进程带 --restarted, 会等旧进程退出后自己接管
            if (!Program.RestartApplication())
            {
                _restarting = false;
                _exitRequested = false;
                ShowMessage(Loc.T("重启失败, 请手动重新打开程序。"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Close();
        }

        /// <summary>按显示器支持情况启用/禁用硬件调节控件；不支持的项目清空量程提示。</summary>
        private void SetDdcEnabled(bool bright, bool contrast, bool sat)
        {
            tbDdcBright.Enabled = bright;
            tbDdcContrast.Enabled = contrast;
            tbDdcSat.Enabled = sat;
            numDdcBright.Enabled = bright;
            numDdcContrast.Enabled = contrast;
            numDdcSat.Enabled = sat;

            _loadingUi = true;   // 复位数值期间屏蔽 DDC 写入
            if (!bright) { numDdcBright.Value = 0; lblDdcBrightMax.Text = ""; }
            if (!contrast) { numDdcContrast.Value = 0; lblDdcContrastMax.Text = ""; }
            if (!sat) { numDdcSat.Value = 0; lblDdcSatMax.Text = ""; }
            _loadingUi = false;
        }

        /// <summary>后台读取选中显示器的 DDC 数值并刷新硬件调节区。</summary>
        private void LoadDdcToUi(bool forceReprobe)
        {
            DisplayMonitor m = SelectedMonitor();
            if (m == null) return;

            SetDdcEnabled(false, false, false);
            lblDdcStatus.Text = Loc.T("正在读取显示器硬件参数…");

            DisplayMonitor target = m;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (forceReprobe || !target.DdcProbed)
                        MonitorService.ProbeDdc(target);

                    uint bCur = 0, bMax = 0, cCur = 0, cMax = 0, sCur = 0, sMax = 0;
                    bool bOk = target.DdcBrightnessOk && MonitorService.DdcGet(target, VcpCodes.Brightness, out bCur, out bMax);
                    bool cOk = target.DdcContrastOk && MonitorService.DdcGet(target, VcpCodes.Contrast, out cCur, out cMax);
                    bool sOk = target.DdcSaturationOk && MonitorService.DdcGet(target, VcpCodes.Saturation, out sCur, out sMax);

                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!_monitors.Contains(target))
                        {
                            MonitorService.ReleaseDdc(target);     // 对象已被刷新淘汰, 回收句柄防泄漏
                            return;
                        }
                        if (SelectedMonitor() != target) return;   // 选择已变, 丢弃过期结果

                        RecordDdcCapability(target);

                        _loadingUi = true;
                        if (bOk && bMax > 0)
                        {
                            tbDdcBright.Maximum = (int)bMax;
                            tbDdcBright.Value = Clamp((int)bCur, 0, (int)bMax);
                            numDdcBright.Maximum = (int)bMax;
                            numDdcBright.Value = Clamp((int)bCur, 0, (int)bMax);
                            lblDdcBrightMax.Text = "/" + bMax;
                        }
                        if (cOk && cMax > 0)
                        {
                            tbDdcContrast.Maximum = (int)cMax;
                            tbDdcContrast.Value = Clamp((int)cCur, 0, (int)cMax);
                            numDdcContrast.Maximum = (int)cMax;
                            numDdcContrast.Value = Clamp((int)cCur, 0, (int)cMax);
                            lblDdcContrastMax.Text = "/" + cMax;
                        }
                        if (sOk && sMax > 0)
                        {
                            tbDdcSat.Maximum = (int)sMax;
                            tbDdcSat.Value = Clamp((int)sCur, 0, (int)sMax);
                            numDdcSat.Maximum = (int)sMax;
                            numDdcSat.Value = Clamp((int)sCur, 0, (int)sMax);
                            lblDdcSatMax.Text = "/" + sMax;
                        }
                        _loadingUi = false;

                        SetDdcEnabled(bOk, cOk, sOk);
                        if (!bOk && !cOk && !sOk)
                            lblDdcStatus.Text = Loc.T("此显示器不支持 DDC/CI (或被显示器菜单关闭)");
                        else if (!sOk)
                            lblDdcStatus.Text = Loc.T("已连接 — 此显示器不提供饱和度(VCP 0x8A), 可调亮度/对比度");
                        else
                            lblDdcStatus.Text = Loc.T("已连接 — 参数直接写入显示器硬件");
                    });
                }
                catch (Exception)
                {
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            lblDdcStatus.Text = Loc.T("DDC/CI 通信失败");
                        });
                    }
                    catch (Exception) { }
                }
            });
        }

        /// <summary>把积压的 DDC 写入按顺序排队发送（去抖后调用）。</summary>
        private void FlushDdcWrites()
        {
            Dictionary<byte, uint> writes;
            List<DisplayMonitor> targets;
            lock (_pendingDdcWrites)
            {
                if (_pendingDdcWrites.Count == 0 || _pendingDdcTargets.Count == 0) return;
                writes = new Dictionary<byte, uint>(_pendingDdcWrites);
                targets = new List<DisplayMonitor>(_pendingDdcTargets);
                _pendingDdcWrites.Clear();
                _pendingDdcTargets.Clear();
            }

            lock (_ddcQueueLock)
            {
                _ddcQueue.Enqueue(new KeyValuePair<List<DisplayMonitor>, Dictionary<byte, uint>>(targets, writes));
                if (_ddcWorkerRunning) return;   // 已有工作线程在按序发送
                _ddcWorkerRunning = true;
            }
            ThreadPool.QueueUserWorkItem(delegate { DrainDdcQueue(); });
        }

        private void DrainDdcQueue()
        {
            while (true)
            {
                KeyValuePair<List<DisplayMonitor>, Dictionary<byte, uint>> batch;
                lock (_ddcQueueLock)
                {
                    if (_ddcQueue.Count == 0)
                    {
                        _ddcWorkerRunning = false;
                        return;
                    }
                    batch = _ddcQueue.Dequeue();
                }
                foreach (DisplayMonitor t in batch.Key)
                {
                    foreach (KeyValuePair<byte, uint> kv in batch.Value)
                    {
                        bool supported =
                            (kv.Key == VcpCodes.Brightness && t.DdcBrightnessOk) ||
                            (kv.Key == VcpCodes.Contrast && t.DdcContrastOk) ||
                            (kv.Key == VcpCodes.Saturation && t.DdcSaturationOk);
                        if (supported)
                        {
                            try { MonitorService.DdcSet(t, kv.Key, kv.Value); }
                            catch (Exception) { }
                        }
                    }
                }
            }
        }

        // ====================================================================
        //  配置文件
        // ====================================================================

        private void RefreshProfileCombo()
        {
            string prev = _activeProfile != null ? _activeProfile.Name : (cboProfiles.SelectedItem as string);

            _suppressProfileChange = true;
            cboProfiles.Items.Clear();
            foreach (Profile p in _settings.Profiles)
                cboProfiles.Items.Add(p.Name);
            // 下拉选中与 _activeProfile 保持一致；无生效预设时 SelectedIndex = -1。
            if (_activeProfile != null && prev != null && cboProfiles.Items.Contains(prev))
                cboProfiles.SelectedItem = prev;
            else
                cboProfiles.SelectedIndex = -1;
            _suppressProfileChange = false;

            RebuildTrayMenu();
            ShowProfileHotkey();
            UpdateDirtyMark();
        }

        /// <summary>启动时按 LastProfile 定位"当前配置"。只选中不应用 —— 启动重放由 ApplyOnStartup 负责,
        /// 否则会先套用配置再重放, 把用户上次未保存的调整悄悄改掉。</summary>
        private void RestoreActiveProfile()
        {
            Profile p = null;
            if (!string.IsNullOrEmpty(_settings.LastProfile))
                p = FindProfile(_settings.LastProfile);
            _activeProfile = p;
            if (p != null) SelectProfileInCombo(p.Name);
            else
            {
                _suppressProfileChange = true;
                cboProfiles.SelectedIndex = -1;
                _suppressProfileChange = false;
                ShowProfileHotkey();
            }
            UpdateDirtyMark();
        }

        private Profile FindProfile(string name)
        {
            foreach (Profile p in _settings.Profiles)
                if (p.Name == name) return p;
            return null;
        }

        private void SelectProfileInCombo(string name)
        {
            _suppressProfileChange = true;
            if (name != null && cboProfiles.Items.Contains(name))
                cboProfiles.SelectedItem = name;
            _suppressProfileChange = false;
            ShowProfileHotkey();
        }

        private Profile SelectedProfile()
        {
            string name = cboProfiles.SelectedItem as string;
            if (name == null) return null;
            return FindProfile(name);
        }

        private Dictionary<string, MonitorConfig> SnapshotCurrent()
        {
            Dictionary<string, MonitorConfig> snap = new Dictionary<string, MonitorConfig>();
            foreach (KeyValuePair<string, MonitorConfig> kv in _settings.Current)
                snap[kv.Key] = kv.Value.Clone();
            return snap;
        }

        // ---------- 未保存修改判定 ----------

        private static bool ConfigEquals(MonitorConfig a, MonitorConfig b)
        {
            if (a == null) a = new MonitorConfig();
            if (b == null) b = new MonitorConfig();
            if (a.Linked != b.Linked) return false;
            return ChannelEquals(a.Red, b.Red)
                && ChannelEquals(a.Green, b.Green)
                && ChannelEquals(a.Blue, b.Blue);
        }

        private static bool ChannelEquals(ChannelSettings a, ChannelSettings b)
        {
            if (a == null) a = new ChannelSettings();
            if (b == null) b = new ChannelSettings();
            return a.Brightness == b.Brightness
                && a.Contrast == b.Contrast
                && Math.Abs(a.Gamma - b.Gamma) < 0.001;
        }

        /// <summary>只比较当前已连接的显示器：拔掉的屏改不了, 拉进比较只会天天误报"未保存"。</summary>
        private bool HasUnsavedChanges()
        {
            if (_activeProfile == null) return false;
            foreach (DisplayMonitor m in _monitors)
            {
                MonitorConfig cur;
                _settings.Current.TryGetValue(m.StableKey, out cur);
                MonitorConfig saved = null;
                if (_activeProfile.Monitors != null)
                    _activeProfile.Monitors.TryGetValue(m.StableKey, out saved);
                if (!ConfigEquals(cur, saved)) return true;
            }
            return false;
        }

        private void UpdateDirtyMark()
        {
            bool dirty = HasUnsavedChanges();
            if (lblDirty != null) lblDirty.Text = dirty ? Loc.T("● 未保存") : "";
            if (btnEditProfiles != null) btnEditProfiles.Enabled = _settings.Profiles.Count > 0;
        }

        // ---------- 切换 / 保存 / 新建 / 删除 ----------

        /// <summary>下拉框选择变化 = 立即应用该配置。离开前若有改动先问是否保存。</summary>
        private void OnProfileSelectionChanged()
        {
            if (_suppressProfileChange) return;
            Profile p = SelectedProfile();
            if (p == null) { UpdateDirtyMark(); return; }
            if (p == _activeProfile) { UpdateDirtyMark(); return; }

            if (!SwitchToProfile(p))
                SelectProfileInCombo(_activeProfile != null ? _activeProfile.Name : null);   // 用户取消, 退回原选项
        }

        /// <summary>切换到指定配置（下拉框、托盘菜单、全局热键共用此入口）。
        /// 返回 false 表示用户在"是否保存"里选择了取消。</summary>
        private bool SwitchToProfile(Profile p)
        {
            if (p == null) return false;
            if (p == _activeProfile)
            {
                SelectProfileInCombo(p.Name);
                return true;
            }
            if (!ConfirmSaveChanges(_activeProfile)) return false;

            _activeProfile = p;
            SelectProfileInCombo(p.Name);
            ApplyProfile(p);
            UpdateDirtyMark();
            RebuildTrayMenu();     // 同步托盘里的勾选状态
            return true;
        }

        private void CycleProfile()
        {
            int count = _settings.Profiles.Count;
            if (count == 0) return;
            int current = _settings.Profiles.IndexOf(_activeProfile);
            if (count == 1 && current == 0) return;
            SwitchToProfile(_settings.Profiles[(current + 1) % count]);
        }

        /// <summary>离开某个配置前询问是否保存。返回 false 表示用户选择取消（留在原配置）。</summary>
        private bool ConfirmSaveChanges(Profile leaving)
        {
            if (leaving == null || !HasUnsavedChanges()) return true;

            DialogResult r = ShowMessage(
                Loc.F("配置「{0}」有未保存的修改。\r\n\r\n是: 保存后继续\r\n否: 丢弃修改\r\n取消: 留在当前配置",
                    leaving.Name),
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) SaveToProfile(leaving);
            return true;
        }

        /// <summary>把当前状态写入指定配置（不改变 Current）。</summary>
        private void SaveToProfile(Profile p)
        {
            p.Monitors = SnapshotCurrent();
            _settings.LastProfile = p.Name;
            ScheduleSave();
            UpdateDirtyMark();
            SetStatus(Loc.F("配置「{0}」已保存", p.Name));
        }

        private void SaveToActiveProfile()
        {
            if (_activeProfile == null) { NewProfile(); return; }
            SaveToProfile(_activeProfile);
        }

        /// <summary>用当前状态新建一个预设并切换过去。</summary>
        private void NewProfile()
        {
            string name = PromptText(Loc.T("新建配置"), Loc.T("输入新配置名称 (以当前状态创建):"));
            if (string.IsNullOrEmpty(name)) return;
            if (FindProfile(name) != null)
            {
                ShowMessage(Loc.F("已存在同名配置「{0}」, 请换一个名称。", name),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Profile p = new Profile();
            p.Name = name;
            p.Monitors = SnapshotCurrent();
            _settings.Profiles.Add(p);

            _activeProfile = p;
            _settings.LastProfile = p.Name;
            ScheduleSave();
            RefreshProfileCombo();
            SelectProfileInCombo(p.Name);
            UpdateDirtyMark();
            SetStatus(Loc.F("已新建配置「{0}」", name));
        }

        /// <summary>统一编辑预设：顺序、名称与删除在副本里调整，确定后一次性提交。</summary>
        private void EditProfiles()
        {
            if (_settings.Profiles.Count == 0) return;

            List<Profile> before = new List<Profile>(_settings.Profiles);
            using (ProfileEditForm f = new ProfileEditForm(_settings.Profiles, _activeProfile,
                HasUnsavedChanges(), _uiScale, _dpiScale))
            {
                if (ShowOwnedDialog(f) != DialogResult.OK) return;
                List<Profile> ordered = f.OrderedProfiles;

                bool sameOrder = ordered.Count == before.Count;
                for (int i = 0; sameOrder && i < ordered.Count; i++)
                    if (ordered[i] != before[i]) sameOrder = false;
                bool changed = !sameOrder;
                foreach (Profile p in before)
                {
                    if (!ordered.Contains(p)) changed = true;
                    else if (f.NameFor(p) != p.Name) changed = true;
                }
                if (!changed) return;

                Profile activeBefore = _activeProfile;
                bool activeDeleted = activeBefore != null && !ordered.Contains(activeBefore);

                // 先写名称, 再替换列表；Profile 对象身份保持不变, 热键 / 参数随对象一起保留。
                foreach (Profile p in ordered) p.Name = f.NameFor(p);
                if (activeDeleted) _activeProfile = null;
                _settings.Profiles.Clear();
                _settings.Profiles.AddRange(ordered);
                _settings.LastProfile = _activeProfile != null ? _activeProfile.Name : "";

                ScheduleSave();
                SetStatus(Loc.T("预设已更新"));   // 先报成功; 热键重注册失败时需要覆盖它
                RefreshProfileCombo();   // 下拉与托盘共用 Profiles 顺序，并按对象名恢复当前选中项
                if (!sameOrder) ReRegisterHotkeys();   // 仅改名不影响动作编号, 避免无谓地重注册

                if (activeDeleted && _settings.Profiles.Count > 0)
                    SwitchToProfile(_settings.Profiles[0]);
                else
                    UpdateDirtyMark();
            }
        }

        /// <summary>分享码按钮：复制只在参数非默认时可用, 导入需要选中显示器。</summary>
        private void UpdateShareButtons()
        {
            DisplayMonitor m = SelectedMonitor();
            bool hasCode = false;
            if (m != null)
                hasCode = !GetConfig(m).IsNeutral();
            if (btnCopyShareCode != null) btnCopyShareCode.Enabled = hasCode;
            if (btnImportShareCode != null) btnImportShareCode.Enabled = m != null;
        }

        /// <summary>把当前选中显示器的单屏参数复制到剪贴板；默认参数没有分享码。</summary>
        private void CopyCurrentShareCode()
        {
            DisplayMonitor m = SelectedMonitor();
            if (m == null) return;

            string code = "";
            MonitorConfig cfg = GetConfig(m);
            if (!cfg.IsNeutral())
            {
                try { code = ProfileShareCode.Encode(cfg); }
                catch (ArgumentException) { code = ""; }
            }
            if (code.Length == 0)
            {
                ToastForm.ShowMessage(Loc.T("当前为默认参数，没有可复制的分享码"), _uiScale, _dpiScale);
                return;
            }

            try
            {
                Clipboard.SetText(code);
                SetStatus(Loc.T("已复制分享码"));
                ToastForm.ShowMessage(Loc.T("分享码已复制到剪贴板"), _uiScale, _dpiScale);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                ShowMessage(Loc.T("剪贴板暂时不可用，请重试。"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>弹出粘贴框导入单屏分享码, 只应用到当前显示器, 不新增预设。</summary>
        private void ImportShareCode()
        {
            DisplayMonitor m = SelectedMonitor();
            if (m == null) return;

            using (ShareCodeForm f = new ShareCodeForm(_uiScale, _dpiScale))
            {
                if (ShowOwnedDialog(f) != DialogResult.OK || f.Config == null) return;
                ApplySharedConfig(f.Config, m);
            }
        }

        private void ApplySharedConfig(MonitorConfig cfg, DisplayMonitor m)
        {
            _settings.Current[m.StableKey] = cfg.Clone();
            bool accepted = ApplyConfig(m, cfg);
            ShowRampRejected(!accepted);
            SyncRadioToConfig(cfg);
            LoadChannelToSliders();
            picCurve.Invalidate();
            ScheduleSave();
            UpdateDirtyMark();
            UpdateShareButtons();
            if (accepted)
                SetStatus(Loc.F("已把分享码应用到 {0}（尚未保存到预设）", m.ToString()));
            else
                SetStatus(Loc.F("已把分享码应用到 {0}，但部分数值被系统拒绝", m.ToString()));
        }

        private void ApplyProfile(Profile p)
        {
            // 对当前已连接的显示器, 预设缺项按默认值重置；否则未记录该显示器的预设会残留上一套参数,
            // 切换后立刻被 HasUnsavedChanges 误判为"未保存"。
            foreach (DisplayMonitor m in _monitors)
            {
                MonitorConfig cfg = null;
                if (p.Monitors != null) p.Monitors.TryGetValue(m.StableKey, out cfg);
                _settings.Current[m.StableKey] = cfg != null ? cfg.Clone() : new MonitorConfig();
            }
            bool anyRejected = false;
            foreach (DisplayMonitor m in _monitors)
            {
                MonitorConfig cfg;
                if (_settings.Current.TryGetValue(m.StableKey, out cfg))
                {
                    if (!ApplyConfig(m, cfg))
                        anyRejected = true;
                }
            }
            ShowRampRejected(anyRejected);
            _settings.LastProfile = p.Name;
            ScheduleSave();
            DisplayMonitor sel = SelectedMonitor();
            if (sel != null) SyncRadioToConfig(GetConfig(sel));
            LoadChannelToSliders();
            picCurve.Invalidate();
            UpdateShareButtons();
            if (anyRejected)
                SetStatus(Loc.F("已应用「{0}」, 但部分数值被系统拒绝", p.Name));
            else
                SetStatus(Loc.F("已应用配置文件「{0}」", p.Name));
            if (_settings.NotifyEnabled && _settings.NotifyProfileApplied &&
                (!Visible || _settings.NotifyProfileAppliedForeground))
                ToastForm.ShowMessage(Loc.F("已应用「{0}」", p.Name), _uiScale, _dpiScale);
        }

        private string PromptText(string title, string caption, string initialValue = null)
        {
            using (Form f = new Form())
            {
                int w = S(320);
                int h = S(120);
                int bw = S(80);
                int bh = Math.Max(S(28), Font.Height + S(12));

                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.AutoScaleMode = AutoScaleMode.None;
                f.ClientSize = new Size(w, h);
                f.StartPosition = FormStartPosition.CenterParent;
                f.Font = Font;

                Label l = NewLabel(caption, S(12), S(12));
                TextBox t = new TextBox();
                t.SetBounds(S(12), S(38), w - S(24), Math.Max(S(24), Font.Height + S(8)));
                if (initialValue != null) t.Text = initialValue;
                f.Shown += delegate { t.Focus(); t.SelectAll(); };
                Button ok = new Button();
                ok.Text = Loc.T("确定");
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(w - S(12) - bw - S(8) - bw, h - S(12) - bh, bw, bh);
                Button cancel = new Button();
                cancel.Text = Loc.T("取消");
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(w - S(12) - bw, h - S(12) - bh, bw, bh);

                f.Controls.Add(l);
                f.Controls.Add(t);
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;

                if (ShowOwnedDialog(f) == DialogResult.OK)
                    return t.Text.Trim();
                return null;
            }
        }

        // ====================================================================
        //  热键
        // ====================================================================

        /// <summary>刷新循环切换热键的显示与触发方式；未绑定时显示「未设置」。</summary>
        private void ShowCycleHotkey()
        {
            if (txtCycleHotkey == null) return;
            HotkeyTrigger trigger = _settings.CycleHotKeyTrigger;
            if (trigger != HotkeyTrigger.Single &&
                trigger != HotkeyTrigger.DoublePress &&
                trigger != HotkeyTrigger.LongPress)
                trigger = HotkeyTrigger.Single;
            _updatingCycleUi = true;
            txtCycleHotkey.Text = _settings.CycleHotKey == 0
                ? Loc.T("未设置")
                : HotkeyRules.Format(_settings.CycleHotKey, trigger);
            cboCycleTrigger.SelectedIndex = (int)trigger;
            _updatingCycleUi = false;
            btnClearCycle.Visible = _settings.CycleHotKey != 0;
        }

        private void ChangeCycleTrigger()
        {
            if (_updatingCycleUi || cboCycleTrigger.SelectedIndex < 0) return;
            HotkeyTrigger trigger = (HotkeyTrigger)cboCycleTrigger.SelectedIndex;
            if (_settings.CycleHotKey != 0)
            {
                string conflict = HotkeyRules.FindConflict(_settings, null, true, _settings.CycleHotKey, trigger);
                if (conflict != null)
                {
                    SetStatus(Loc.F("该按键和触发方式已被「{0}」使用", conflict));
                    ShowCycleHotkey();
                    return;
                }
            }
            _settings.CycleHotKeyTrigger = trigger;
            ShowCycleHotkey();
            ScheduleSave();
            ReRegisterHotkeys();
        }

        private void CycleHotkeyCapture(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;

            Keys key = e.KeyCode;
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu ||
                key == Keys.LWin || key == Keys.RWin || key == Keys.None)
                return;
            if (key == Keys.Escape || key == Keys.Back || key == Keys.Delete)
            {
                ClearCycleHotkey();
                return;
            }
            if (!HotkeyRules.IsAllowed((int)e.KeyData))
            {
                SetStatus(Loc.T("请使用 Ctrl/Alt 组合键或 F 功能键；F12 为系统保留键。"));
                return;
            }

            HotkeyTrigger trigger = cboCycleTrigger.SelectedIndex >= 0
                ? (HotkeyTrigger)cboCycleTrigger.SelectedIndex
                : HotkeyTrigger.Single;
            string conflict = HotkeyRules.FindConflict(_settings, null, true, (int)e.KeyData, trigger);
            if (conflict != null)
            {
                SetStatus(Loc.F("该按键和触发方式已被「{0}」使用", conflict));
                return;
            }

            _settings.CycleHotKey = (int)e.KeyData;
            _settings.CycleHotKeyTrigger = trigger;
            ShowCycleHotkey();
            ScheduleSave();
            ReRegisterHotkeys();
        }

        private void ClearCycleHotkey()
        {
            _settings.CycleHotKey = 0;
            ShowCycleHotkey();
            ScheduleSave();
            ReRegisterHotkeys();
        }

        private void ShowProfileHotkey()
        {
            Profile p = SelectedProfile();
            bool bound = p != null && p.HotKey != 0;
            _updatingHotkeyUi = true;
            txtHotkey.Text = bound ? HotkeyRules.Format(p.HotKey, HotkeyTrigger.Single) : Loc.T("按组合键");
            txtHotkey.Enabled = p != null;
            cboHotkeyTrigger.Enabled = p != null;
            cboHotkeyTrigger.SelectedIndex = p != null ? (int)p.HotKeyTrigger : 0;
            _updatingHotkeyUi = false;
            if (btnClearHotkey != null) btnClearHotkey.Visible = bound && p != null;
        }

        private void ChangeHotkeyTrigger()
        {
            if (_updatingHotkeyUi || cboHotkeyTrigger.SelectedIndex < 0) return;
            Profile p = SelectedProfile();
            if (p == null) return;
            HotkeyTrigger trigger = (HotkeyTrigger)cboHotkeyTrigger.SelectedIndex;
            string conflict = HotkeyRules.FindConflict(_settings, p, false, p.HotKey, trigger);
            if (conflict != null)
            {
                SetStatus(Loc.F("该按键和触发方式已被「{0}」使用", conflict));
                ShowProfileHotkey();
                return;
            }
            p.HotKeyTrigger = trigger;
            ScheduleSave();
            ReRegisterHotkeys();
            RebuildTrayMenu();
        }

        private void HotkeyCapture(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;

            Keys key = e.KeyCode;
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu ||
                key == Keys.LWin || key == Keys.RWin || key == Keys.None)
                return;

            Profile p = SelectedProfile();
            if (p == null)
            {
                SetStatus(Loc.T("请先创建并选中一个配置文件, 再设置热键"));
                return;
            }
            if (key == Keys.Escape || key == Keys.Back || key == Keys.Delete)
            {
                ClearHotkey();
                return;
            }

            if (!HotkeyRules.IsAllowed((int)e.KeyData))
            {
                SetStatus(Loc.T("请使用 Ctrl/Alt 组合键或 F 功能键；F12 为系统保留键。"));
                return;
            }
            string conflict = HotkeyRules.FindConflict(_settings, p, false, (int)e.KeyData, p.HotKeyTrigger);
            if (conflict != null)
            {
                SetStatus(Loc.F("该按键和触发方式已被「{0}」使用", conflict));
                return;
            }

            p.HotKey = (int)e.KeyData;
            ShowProfileHotkey();          // 同时刷新文本与右侧 × 按钮的可见性
            ScheduleSave();
            ReRegisterHotkeys();
            RebuildTrayMenu();
        }

        private void ClearHotkey()
        {
            Profile p = SelectedProfile();
            if (p == null) return;
            p.HotKey = 0;
            ShowProfileHotkey();
            ScheduleSave();
            ReRegisterHotkeys();
            RebuildTrayMenu();
        }

        private void ReRegisterHotkeys()
        {
            if (_hotkeysClosed) return;
            StopHotkeys();
            if (!_loaded || _hotkeyPauseDepth > 0) return;
            _hotkeyOrder.Clear();
            List<HotkeyBinding> bindings = new List<HotkeyBinding>();
            if (_settings.CycleHotKey != 0)
                bindings.Add(new HotkeyBinding(0, _settings.CycleHotKey, _settings.CycleHotKeyTrigger));
            foreach (Profile p in _settings.Profiles)
            {
                if (p.HotKey == 0) continue;
                _hotkeyOrder.Add(p);
                bindings.Add(new HotkeyBinding(_hotkeyOrder.Count, p.HotKey, p.HotKeyTrigger));
            }
            List<HotkeyBinding> valid = new List<HotkeyBinding>();
            HashSet<string> gestures = new HashSet<string>();
            foreach (HotkeyBinding binding in bindings)
            {
                if (!HotkeyRules.IsAllowed(binding.KeyData) ||
                    !gestures.Add(binding.KeyData + ":" + (int)binding.Trigger))
                {
                    SetStatus(Loc.F("热键 {0} 无效或重复，请重新绑定", HotkeyRules.Format(binding.KeyData, binding.Trigger)));
                    continue;
                }
                valid.Add(binding);
            }
            HashSet<int> attempted = new HashSet<int>();
            HashSet<int> registered = new HashSet<int>();
            foreach (HotkeyBinding binding in valid)
            {
                if (!attempted.Add(binding.KeyData)) continue;
                Keys kd = (Keys)binding.KeyData;
                uint mods = NativeMethods.MOD_NOREPEAT;
                if ((kd & Keys.Control) != 0) mods |= NativeMethods.MOD_CONTROL;
                if ((kd & Keys.Shift) != 0) mods |= NativeMethods.MOD_SHIFT;
                if ((kd & Keys.Alt) != 0) mods |= NativeMethods.MOD_ALT;
                int id = 0x100 + _hotkeyIds.Count;
                if (NativeMethods.RegisterHotKey(Handle, id, mods, (uint)(kd & Keys.KeyCode)))
                {
                    _hotkeyIds.Add(id);
                    registered.Add(binding.KeyData);
                }
                else
                    SetStatus(Loc.F("热键 {0} 注册失败 (可能已被其他程序占用)",
                        HotkeyRules.Format(binding.KeyData, HotkeyTrigger.Single)));
            }
            valid.RemoveAll(delegate(HotkeyBinding binding) { return !registered.Contains(binding.KeyData); });
            _hotkeyEngine.SetBindings(valid);
            if (registered.Count == 0) return;
            int generation = _hotkeyGeneration;
            if (!_hotkeyListener.Start(registered, delegate(int keyData, bool pressed, long timestamp)
            {
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed || generation != _hotkeyGeneration || _hotkeyPauseDepth > 0) return;
                        DispatchHotkeyActions(_hotkeyEngine.Handle(keyData, pressed, timestamp));
                        ArmGestureTimer();
                    });
                }
                catch (InvalidOperationException) { }
            }))
            {
                StopHotkeys();
                SetStatus(Loc.T("快捷键监听启动失败，请重新绑定或重启程序。"));
            }
        }

        private void StopHotkeys()
        {
            _hotkeyGeneration++;
            if (_gestureTimer != null) _gestureTimer.Stop();
            _hotkeyListener.Stop();
            if (IsHandleCreated)
                foreach (int id in _hotkeyIds) NativeMethods.UnregisterHotKey(Handle, id);
            _hotkeyIds.Clear();
            _hotkeyEngine.Reset();
        }

        private void SetHotkeyCapture(bool capturing)
        {
            if (_capturingHotkey == capturing) return;
            _capturingHotkey = capturing;
            if (capturing) PauseHotkeys();
            else ResumeHotkeys();
        }

        private void PauseHotkeys()
        {
            _hotkeyPauseDepth++;
            if (_hotkeyPauseDepth == 1) StopHotkeys();
        }

        private void ResumeHotkeys()
        {
            if (_hotkeyPauseDepth == 0) return;
            _hotkeyPauseDepth--;
            if (_hotkeyPauseDepth == 0) ReRegisterHotkeys();
        }

        private void ArmGestureTimer()
        {
            _gestureTimer.Stop();
            long deadline = _hotkeyEngine.NextDeadline;
            if (deadline < 0 || _hotkeyPauseDepth > 0 || _hotkeysClosed) return;
            _gestureTimer.Interval = (int)Math.Max(1, Math.Min(int.MaxValue, deadline - HotkeyClock.Now));
            _gestureTimer.Start();
        }

        private void DispatchHotkeyActions(List<int> actions)
        {
            int generation = _hotkeyGeneration;
            foreach (int action in actions)
            {
                if (_hotkeyPauseDepth > 0 || _hotkeysClosed || generation != _hotkeyGeneration) break;
                if (action == 0) CycleProfile();
                else if (action > 0 && action <= _hotkeyOrder.Count) SwitchToProfile(_hotkeyOrder[action - 1]);
            }
        }

        private DialogResult ShowOwnedDialog(Form dialog)
        {
            PauseHotkeys();
            try { return dialog.ShowDialog(this); }
            finally { ResumeHotkeys(); }
        }

        private DialogResult ShowMessage(string text, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            PauseHotkeys();
            try { return MessageBox.Show(this, text, AppTitle, buttons, icon); }
            finally { ResumeHotkeys(); }
        }

        // ====================================================================
        //  系统集成
        // ====================================================================

        protected override void WndProc(ref Message msg)
        {
            if (msg.Msg == NativeMethods.WM_HOTKEY)
            {
                return;
            }
            else if (msg.Msg == NativeMethods.WM_DISPLAYCHANGE)
            {
                _displayChangeTimer.Stop();
                _displayChangeTimer.Start();
            }
            else if (ShowWindowMsg != 0 && msg.Msg == (int)ShowWindowMsg)
            {
                ShowMainWindow();
            }
            base.WndProc(ref msg);
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (IsDisposed || _hotkeysClosed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { OnPowerModeChanged(sender, e); }); }
                catch (InvalidOperationException) { }
                return;
            }
            if (e.Mode == PowerModes.Suspend && !_powerHotkeysPaused)
            {
                _powerHotkeysPaused = true;
                PauseHotkeys();
            }
            if (e.Mode == PowerModes.Resume)
            {
                if (_powerHotkeysPaused) { _powerHotkeysPaused = false; ResumeHotkeys(); }
                _displayChangeTimer.Stop();
                _displayChangeTimer.Start();
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (IsDisposed || _hotkeysClosed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { OnSessionSwitch(sender, e); }); }
                catch (InvalidOperationException) { }
                return;
            }
            if (e.Reason == SessionSwitchReason.SessionLock && !_sessionHotkeysPaused)
            {
                _sessionHotkeysPaused = true;
                PauseHotkeys();
            }
            if (e.Reason == SessionSwitchReason.SessionUnlock)
            {
                if (_sessionHotkeysPaused) { _sessionHotkeysPaused = false; ResumeHotkeys(); }
                ReapplyNonNeutral();
            }
        }

        // ====================================================================
        //  退出
        // ====================================================================

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 语言切换的自重启也会走 UserClosing: 在此拦截会把旧进程留在托盘
            if (!_exitRequested && !_restarting && e.CloseReason == CloseReason.UserClosing && _settings.TrayOnClose)
            {
                e.Cancel = true;
                Hide();
                if (_settings.NotifyEnabled && _settings.NotifyTrayOnClose)
                    ToastForm.ShowMessage(Loc.T("仍在后台运行, 双击托盘图标可重新打开"), _uiScale, _dpiScale);
                return;
            }

            // 真正退出前问一次未保存修改；最小化到托盘时已提前返回, 不会打扰
            if (!_restarting && !ConfirmSaveChanges(_activeProfile))
            {
                e.Cancel = true;
                _exitRequested = false;
                _restoreNeutralOnExit = false;
                return;
            }

            if (_restoreNeutralOnExit)
            {
                // 中性配置在基线之上合成 = 精确还原系统默认/ICC 校准曲线
                foreach (DisplayMonitor m in _monitors)
                    ApplyConfig(m, new MonitorConfig());
            }

            SettingsStore.Save(_settings);
            _hotkeysClosed = true;
            StopHotkeys();
            _hotkeyListener.Dispose();
            _gestureTimer.Dispose();
            MonitorService.ReleaseAll(_monitors);
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            ToastForm.CloseCurrent();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            base.OnFormClosing(e);
        }

        private void ExitApp(bool restoreNeutral)
        {
            _exitRequested = true;
            _restoreNeutralOnExit = restoreNeutral;
            Close();
        }

        private void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>普通状态文本。若当前仍存在"数值被拒绝"的提示, 状态栏优先显示该提示（可点击）。</summary>
        private void SetStatus(string text)
        {
            _statusText = text;
            RenderStatus();
        }

        private void RenderStatus()
        {
            if (lblStatus == null) return;
            if (_rampRejected)
            {
                lblStatus.Text = Loc.T("部分数值被系统拒绝 (超出 Windows 默认范围) — 点此查看原因与解除方法");
                lblStatus.IsLink = true;
                lblStatus.ToolTipText = Loc.T("Windows 会钳制 gamma 查找表范围, 极端数值写入无效。\r\n点击打开 设置 › 启动与权限 查看说明并解除限制。");
            }
            else
            {
                lblStatus.Text = _statusText;
                lblStatus.IsLink = false;
                lblStatus.ToolTipText = "";
            }
        }
    }
}
