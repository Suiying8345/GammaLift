using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace GammaLift
{
    // 仅截图程序集使用：与 MonitorService.cs / SystemIntegration.cs 互斥，避免碰真实显示器与注册表。
    public class DisplayMonitor
    {
        public string DeviceName { get; set; }
        public string FriendlyName { get; set; }
        public string StableKey { get; set; }
        public Rectangle Bounds { get; set; }
        public bool IsPrimary { get; set; }
        public IntPtr HMonitor { get; set; }
        public IntPtr PhysicalHandle { get; set; }
        public bool DdcProbed { get; set; }
        public bool DdcBrightnessOk { get; set; }
        public bool DdcContrastOk { get; set; }
        public bool DdcSaturationOk { get; set; }
        public override string ToString() { return FriendlyName + (IsPrimary ? Loc.T(" (主屏)") : ""); }
    }
    public static class VcpCodes { public const byte Brightness = 0x10, Contrast = 0x12, Saturation = 0x8A; }
    public static class MonitorService
    {
        public static readonly Dictionary<string, ushort[]> Ramps = new Dictionary<string, ushort[]>();
        public static List<DisplayMonitor> Enumerate()
        {
            bool zh = Loc.Code == Loc.SourceCode;
            Rectangle b = Screen.PrimaryScreen.WorkingArea;
            return new List<DisplayMonitor> {
                new DisplayMonitor { DeviceName = "MOCK-DISPLAY-A", StableKey = "MOCK-A", FriendlyName = zh ? "演示显示器 A" : "Demo Display A", IsPrimary = true, Bounds = b },
                new DisplayMonitor { DeviceName = "MOCK-DISPLAY-B", StableKey = "MOCK-B", FriendlyName = zh ? "演示显示器 B" : "Demo Display B", Bounds = b } };
        }
        public static ushort[] GetGammaRamp(string deviceName)
        {
            ushort[] ramp;
            if (!Ramps.TryGetValue(deviceName, out ramp))
            {
                ramp = new ushort[768];
                for (int i = 0; i < 768; i++) ramp[i] = (ushort)((i % 256) * 257);
                Ramps[deviceName] = ramp;
            }
            return (ushort[])ramp.Clone();
        }
        public static bool ApplyGamma(string deviceName, MonitorConfig cfg, ushort[] baseline)
        {
            Ramps[deviceName] = (ushort[])GammaMath.BuildRamp(cfg, baseline == null ? null : (ushort[])baseline.Clone()).Clone();
            return true;
        }
        public static void ProbeDdc(DisplayMonitor m) { m.DdcProbed = true; }
        public static bool DdcGet(DisplayMonitor m, byte code, out uint current, out uint max) { current = max = 0; return false; }
        public static bool DdcSet(DisplayMonitor m, byte code, uint value) { return false; }
        public static void ReleaseDdc(DisplayMonitor m) { m.PhysicalHandle = IntPtr.Zero; }
        public static void ReleaseAll(IEnumerable<DisplayMonitor> monitors) { foreach (DisplayMonitor m in monitors) ReleaseDdc(m); }
    }
    public enum GammaRangeResult { AlreadyUnlocked, Unlocked, Failed, Canceled }
    public static class SystemIntegration
    {
        public static bool ReadAutoStart() { return false; }
        public static bool WriteAutoStart(bool enable) { return true; }
        public static bool IsGammaRangeUnlocked() { return false; }
        public static GammaRangeResult UnlockGammaRange() { return GammaRangeResult.Canceled; }
    }

    internal static class MakeScreenshots
    {
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static T Field<T>(object obj, string name)
        {
            return (T)obj.GetType().GetField(name, Flags).GetValue(obj);
        }

        private static void Set(object obj, string name, object value)
        {
            Type type = obj is Type ? (Type)obj : obj.GetType();
            FieldInfo field = type.GetField(name, Flags);
            if (field == null) throw new InvalidOperationException("Missing field: " + name);
            field.SetValue(obj is Type ? null : obj, value);
        }

        private static AppSettings Seed(string language)
        {
            return new AppSettings
            {
                ApplyOnStartup = false,
                KeepApplied = false,
                TrayOnClose = false,
                MinimizeToTray = false,
                ShowDdcPanel = false,
                AutoExpandDdc = false,
                Language = language
            };
        }

        private static void Snapshot(Form form, string path)
        {
            form.Refresh();
            using (Bitmap bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
                bmp.Save(path, ImageFormat.Png);
            }
            Console.WriteLine("SCREENSHOT " + path + " (" + form.Width + "x" + form.Height + ")");
        }

        private static void Shoot(string language, string path)
        {
            Loc.SetLanguage(language);
            SettingsStore.Save(Seed(language));
            using (MainForm form = new MainForm())
            {
                Set(form, "_hotkeyPauseDepth", 1);   // 不注册全局热键
                form.Show();
                Application.DoEvents();
                Snapshot(form, path);
                Set(form, "_restarting", true);
                Set(form, "_exitRequested", true);
                form.Close();
            }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            string outDir = args.Length > 0
                ? Path.GetFullPath(args[0])
                : Path.Combine(Directory.GetCurrentDirectory(), "docs", "screenshots");
            Directory.CreateDirectory(outDir);

            string scratch = Path.Combine(Path.GetTempPath(), "GammaLift-Shots-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            Set(typeof(SettingsStore), "_settingsDir", scratch);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Shoot(Loc.SourceCode, Path.Combine(outDir, "main-zh.png"));
                Shoot("en", Path.Combine(outDir, "main-en.png"));
                return 0;
            }
            finally
            {
                try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
                catch (Exception) { }
            }
        }
    }
}
