using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace GammaLift
{
    /// <summary>单个颜色通道的调节参数。</summary>
    public class ChannelSettings
    {
        public int Brightness { get; set; }   // -100 .. 100
        public int Contrast { get; set; }     // -100 .. 100
        public double Gamma { get; set; }     // 0.30 .. 3.00

        public ChannelSettings()
        {
            Brightness = 0;
            Contrast = 0;
            Gamma = 1.0;
        }

        public ChannelSettings Clone()
        {
            ChannelSettings c = new ChannelSettings();
            c.Brightness = Brightness;
            c.Contrast = Contrast;
            c.Gamma = Gamma;
            return c;
        }

        public bool IsNeutral()
        {
            return Brightness == 0 && Contrast == 0 && Math.Abs(Gamma - 1.0) < 0.001;
        }
    }

    /// <summary>一台显示器的完整调节状态。</summary>
    public class MonitorConfig
    {
        public ChannelSettings Red { get; set; }
        public ChannelSettings Green { get; set; }
        public ChannelSettings Blue { get; set; }
        public bool Linked { get; set; }

        public MonitorConfig()
        {
            Red = new ChannelSettings();
            Green = new ChannelSettings();
            Blue = new ChannelSettings();
            Linked = true;
        }

        public MonitorConfig Clone()
        {
            MonitorConfig c = new MonitorConfig();
            c.Red = Red.Clone();
            c.Green = Green.Clone();
            c.Blue = Blue.Clone();
            c.Linked = Linked;
            return c;
        }

        public bool IsNeutral()
        {
            return Red.IsNeutral() && Green.IsNeutral() && Blue.IsNeutral();
        }

        public ChannelSettings GetChannel(int idx)
        {
            if (idx == 1) return Green;
            if (idx == 2) return Blue;
            return Red;
        }
    }

    public enum HotkeyTrigger
    {
        Single = 0,
        DoublePress = 1,
        LongPress = 2
    }

    /// <summary>命名配置文件：记录所有显示器的状态，可绑定全局热键。</summary>
    public class Profile
    {
        public string Name { get; set; }
        public int HotKey { get; set; }   // System.Windows.Forms.Keys 的整数值（含修饰键），0 表示未绑定
        public HotkeyTrigger HotKeyTrigger { get; set; }
        public Dictionary<string, MonitorConfig> Monitors { get; set; }

        public Profile()
        {
            Name = "";
            HotKey = 0;
            Monitors = new Dictionary<string, MonitorConfig>();
        }
    }

    /// <summary>每台显示器上一次探测到的 DDC/CI 能力; 「自动展开」用它做启动判断, 避免每次都先探测。</summary>
    public class DdcCapability
    {
        public bool Brightness { get; set; }
        public bool Contrast { get; set; }
        public bool Saturation { get; set; }
    }

    public class AppSettings
    {
        public Dictionary<string, MonitorConfig> Current { get; set; }
        /// <summary>列表顺序即用户预设顺序，下拉、托盘、循环切换与持久化共用。</summary>
        public List<Profile> Profiles { get; set; }
        /// <summary>每台显示器首次见到时捕获的基线 LUT (768 项), 通常是系统 ICC 校准曲线。</summary>
        public Dictionary<string, List<int>> Baselines { get; set; }
        public bool KeepApplied { get; set; }      // 定时重新应用（防止游戏/驱动重置）
        public bool TrayOnClose { get; set; }      // 点关闭按钮时最小化到托盘
        public bool MinimizeToTray { get; set; }   // 点最小化按钮时隐藏到托盘（任务栏不显示）
        public bool ApplyOnStartup { get; set; }   // 启动时自动恢复上次的设置
        public bool ShowDdcPanel { get; set; }     // 固定展开硬件调节 (DDC/CI) 面板; 与 AutoExpandDdc 互斥
        public bool AutoExpandDdc { get; set; }    // 选中显示器支持 DDC/CI 时自动展开「更多」, 默认开启
        /// <summary>按显示器硬件 ID 缓存上次探测到的 DDC/CI 能力, 供「自动展开」在启动时即时判断。</summary>
        public Dictionary<string, DdcCapability> DdcCapabilities { get; set; }
        public int UiScalePercent { get; set; }    // 界面字号预设: 85 小 / 100 中 / 115 大
        public string Language { get; set; }       // 界面语言代码；空 = 跟随系统
        public string LastProfile { get; set; }
        /// <summary>首次启动的系统预设已种入；用户删除后不再自动恢复。</summary>
        public bool PresetsSeeded { get; set; }
        public int CycleHotKey { get; set; }
        public HotkeyTrigger CycleHotKeyTrigger { get; set; }
        public bool NotifyEnabled { get; set; }
        public bool NotifyProfileApplied { get; set; }
        public bool NotifyProfileAppliedForeground { get; set; }   // 主窗口可见时应用预设也弹提示
        public bool NotifyTrayOnClose { get; set; }

        public AppSettings()
        {
            Current = new Dictionary<string, MonitorConfig>();
            Profiles = new List<Profile>();
            Baselines = new Dictionary<string, List<int>>();
            KeepApplied = false;
            TrayOnClose = true;
            MinimizeToTray = true;
            ApplyOnStartup = true;
            ShowDdcPanel = false;
            AutoExpandDdc = true;
            DdcCapabilities = new Dictionary<string, DdcCapability>();
            UiScalePercent = 100;                  // 现有字号即"中"
            Language = Loc.AutoCode;               // 默认跟随系统语言
            LastProfile = "";
            PresetsSeeded = false;
            NotifyEnabled = true;
            NotifyProfileApplied = true;
            NotifyProfileAppliedForeground = false;   // 前台只更新状态栏, 不弹提示
            NotifyTrayOnClose = true;
        }
    }

    public static class SettingsStore
    {
        private const string SettingsFileName = "GammaLiftSettings.json";

        private static string _settingsDir;   // 缓存可写性判定, 避免每次访问都探针

        /// <summary>便携优先: 与 exe 同目录; 该目录不可写（Program Files / 只读共享）时回退用户文档文件夹。</summary>
        public static string SettingsDir
        {
            get
            {
                if (_settingsDir == null)
                {
                    string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                    _settingsDir = IsWritable(exeDir)
                        ? exeDir
                        : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                }
                return _settingsDir;
            }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(SettingsDir, SettingsFileName); }
        }

        /// <summary>目录可写性探针: 建一个临时文件再删掉, 失败视为不可写。</summary>
        private static bool IsWritable(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                string probe = Path.Combine(dir, "." + Guid.NewGuid().ToString("N") + ".tmp");
                using (File.Create(probe)) { }
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    AppSettings s = ser.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        if (s.Current == null) s.Current = new Dictionary<string, MonitorConfig>();
                        if (s.Profiles == null) s.Profiles = new List<Profile>();
                        if (s.Baselines == null) s.Baselines = new Dictionary<string, List<int>>();
                        if (s.DdcCapabilities == null) s.DdcCapabilities = new Dictionary<string, DdcCapability>();
                        // 缺少 AutoExpandDdc 字段时 (反序列化成 false) 按默认值开启自动展开
                        if (json.IndexOf("\"AutoExpandDdc\"", StringComparison.Ordinal) < 0)
                            s.AutoExpandDdc = true;
                        if (s.AutoExpandDdc) s.ShowDdcPanel = false;   // 两个选项互斥, 自动优先
                        if (s.Language == null) s.Language = Loc.AutoCode;   // 缺少该字段时跟随系统语言
                        if ((int)s.CycleHotKeyTrigger < 0 || (int)s.CycleHotKeyTrigger > 2)
                            s.CycleHotKeyTrigger = HotkeyTrigger.Single;
                        foreach (Profile p in s.Profiles)
                            if (p != null && ((int)p.HotKeyTrigger < 0 || (int)p.HotKeyTrigger > 2))
                                p.HotKeyTrigger = HotkeyTrigger.Single;
                        return s;
                    }
                }
            }
            catch (Exception)
            {
                // 配置文件损坏时回退到默认设置
            }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                File.WriteAllText(SettingsPath, ser.Serialize(settings));
            }
            catch (Exception)
            {
                // 保存失败不应让程序崩溃
            }
        }
    }
}
