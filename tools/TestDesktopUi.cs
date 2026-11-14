using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace GammaLift
{
    // 仅与排除 MonitorService.cs / SystemIntegration.cs 的测试程序集一起编译。
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
        public static int GammaApplyCount, DdcCount;
        public static readonly Dictionary<string, ushort[]> Ramps = new Dictionary<string, ushort[]>();
        public static readonly Dictionary<string, MonitorConfig> Applied = new Dictionary<string, MonitorConfig>();
        public static List<DisplayMonitor> Enumerate()
        {
            Rectangle b = Screen.PrimaryScreen.WorkingArea;
            return new List<DisplayMonitor> {
                new DisplayMonitor { DeviceName = "MOCK-DISPLAY-A", StableKey = "MOCK-A", FriendlyName = "Mock A", IsPrimary = true, Bounds = b },
                new DisplayMonitor { DeviceName = "MOCK-DISPLAY-B", StableKey = "MOCK-B", FriendlyName = "Mock B", Bounds = b } };
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
            if (!deviceName.StartsWith("MOCK-DISPLAY-")) throw new InvalidOperationException("Non-mock gamma target");
            Applied[deviceName] = cfg.Clone();
            Ramps[deviceName] = (ushort[])GammaMath.BuildRamp(cfg, baseline == null ? null : (ushort[])baseline.Clone()).Clone();
            GammaApplyCount++;
            return true;
        }
        public static void ProbeDdc(DisplayMonitor m) { DdcCount++; m.DdcProbed = true; }
        public static bool DdcGet(DisplayMonitor m, byte code, out uint current, out uint max) { DdcCount++; current = max = 0; return false; }
        public static bool DdcSet(DisplayMonitor m, byte code, uint value) { DdcCount++; return false; }
        public static void ReleaseDdc(DisplayMonitor m) { m.PhysicalHandle = IntPtr.Zero; }
        public static void ReleaseAll(IEnumerable<DisplayMonitor> monitors) { foreach (DisplayMonitor m in monitors) ReleaseDdc(m); }
    }
    public enum GammaRangeResult { AlreadyUnlocked, Unlocked, Failed, Canceled }
    public static class SystemIntegration
    {
        public static int StartupWrites;
        public static bool LastStartup;
        public static bool ReadAutoStart() { return false; }
        public static bool WriteAutoStart(bool enable) { StartupWrites++; LastStartup = enable; return true; }
        public static bool IsGammaRangeUnlocked() { return false; }
        public static GammaRangeResult UnlockGammaRange() { return GammaRangeResult.Canceled; }
    }

    internal static class TestDesktopUi
    {
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static int assertions, failures;
        private static string scratch, artifacts;
        private static MainForm main;
        private static AppSettings settings;
        private static IDataObject clipboard;
        private static bool clipboardSaved;
        private delegate bool WindowCallback(IntPtr window, IntPtr param);
        [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, IntPtr param);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder text, int length);
        [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr window, int id);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, WindowCallback callback, IntPtr param);
        private static IntPtr MessageButton(IntPtr box, int result)
        {
            IntPtr button = GetDlgItem(box, result); if (button != IntPtr.Zero) return button;
            if (result != 1) return IntPtr.Zero;
            List<IntPtr> buttons = new List<IntPtr>();
            EnumChildWindows(box, delegate(IntPtr child, IntPtr param)
            {
                System.Text.StringBuilder cls = new System.Text.StringBuilder(80); GetClassName(child, cls, cls.Capacity);
                if (cls.ToString() == "Button" && IsWindowVisible(child)) buttons.Add(child);
                return true;
            }, IntPtr.Zero);
            return buttons.Count == 1 ? buttons[0] : IntPtr.Zero;
        }
        private static IntPtr MessageBoxHandle()
        {
            IntPtr result = IntPtr.Zero;
            EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr window, IntPtr param)
            {
                System.Text.StringBuilder cls = new System.Text.StringBuilder(80); GetClassName(window, cls, cls.Capacity);
                if (cls.ToString() == "#32770" && IsWindowVisible(window)) result = window;
                return true;
            }, IntPtr.Zero);
            return result;
        }
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        private static T Field<T>(object obj, string name) { return (T)(obj is Type ? (Type)obj : obj.GetType()).GetField(name, Flags).GetValue(obj is Type ? null : obj); }
        private static void Set(object obj, string name, object value) { (obj is Type ? (Type)obj : obj.GetType()).GetField(name, Flags).SetValue(obj is Type ? null : obj, value); }
        private static object Call(object obj, string name, params object[] args) { return obj.GetType().GetMethod(name, Flags).Invoke(obj, args); }
        private static void Check(bool pass, string label) { assertions++; if (!pass) { failures++; Console.WriteLine("FAIL " + label); } }
        private static void Group(string label, Action test)
        {
            int before = failures;
            try { test(); } catch (Exception e) { Check(false, label + ": " + e.GetBaseException().Message); }
            Console.WriteLine((before == failures ? "PASS " : "FAILED ") + label);
        }
        private static IEnumerable<Control> Children(Control root)
        {
            foreach (Control c in root.Controls) { yield return c; foreach (Control d in Children(c)) yield return d; }
        }
        private static void Key(TextBox box, Keys keys) { box.Focus(); Call(box, "OnKeyDown", new KeyEventArgs(keys)); }
        private static string State(object obj) { return Json.Serialize(obj); }
        private static void CloseMain(MainForm f)
        {
            Set(f, "_restarting", true); Set(f, "_exitRequested", true);
            foreach (string n in new[] { "_saveTimer", "_keepTimer", "_ddcSetTimer", "_displayChangeTimer", "_gestureTimer" })
            { System.Windows.Forms.Timer t = Field<System.Windows.Forms.Timer>(f, n); t.Stop(); if (n != "_gestureTimer") t.Dispose(); }
            f.Close(); f.Dispose();
        }
        // 每个操作都在真实 ShowDialog 的嵌套消息循环内执行；超时关闭本测试拥有的窗口。
        private static void Automate(Action invoke, Action<Form> dialog, int messageResult = 0)
        {
            Exception error = null; bool acted = false, message = false;
            int pause = Field<int>(main, "_hotkeyPauseDepth");
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer())
            using (System.Windows.Forms.Timer watcher = new System.Windows.Forms.Timer())
            {
                Action<Exception> abort = delegate(Exception e)
                {
                    error = e; timer.Stop(); watcher.Stop();
                    foreach (Form f in Application.OpenForms.Cast<Form>().Where(x => x.Modal).ToArray()) { f.DialogResult = DialogResult.Cancel; f.Close(); }
                    IntPtr box = MessageBoxHandle(); if (box != IntPtr.Zero) SendMessage(box, 0x10, IntPtr.Zero, IntPtr.Zero);
                };
                timer.Interval = watcher.Interval = 25;
                timer.Tick += delegate
                {
                    try
                    {
                        Form f = Application.OpenForms.Cast<Form>().FirstOrDefault(x => x != main && !(x is ToastForm) && x.Modal);
                        if (!acted && f != null)
                        { acted = true; Check(f.Visible && f.IsHandleCreated, "actual modal visible"); Check(Field<int>(main, "_hotkeyPauseDepth") > pause, "modal pauses hotkeys"); dialog(f); }
                    }
                    catch (Exception e) { abort(e); }
                };
                watcher.Tick += delegate
                {
                    try
                    {
                        IntPtr box = MessageBoxHandle();
                        IntPtr button = box == IntPtr.Zero ? IntPtr.Zero : MessageButton(box, messageResult);
                        if (messageResult != 0 && !message && button != IntPtr.Zero)
                        { message = true; SendMessage(button, 0xF5, IntPtr.Zero, IntPtr.Zero); }
                        if (DateTime.UtcNow > deadline) throw new TimeoutException("UI automation timed out: acted=" + acted + " message=" + message + " forms=" + string.Join(",", Application.OpenForms.Cast<Form>().Select(x => x.Text)));
                    }
                    catch (Exception e) { abort(e); }
                };
                timer.Start(); watcher.Start(); invoke(); timer.Stop(); watcher.Stop();
            }
            if (error != null) throw error;
            Check(dialog == null || acted, "modal automation ran");
            Check(messageResult == 0 || message, "native message box acknowledged");
            Check(Field<int>(main, "_hotkeyPauseDepth") == pause, "modal resumes hotkey pause depth");
        }
        private static void Snapshot(Form f, string name)
        {
            Check(f.Visible && f.IsHandleCreated, "snapshot from shown form " + name); f.Refresh();
            using (Bitmap bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
                HashSet<int> colors = new HashSet<int>();
                for (int y = 0; y < bmp.Height; y += 7) for (int x = 0; x < bmp.Width; x += 7) colors.Add(bmp.GetPixel(x, y).ToArgb());
                Check(colors.Count > 10, "actual rendered pixel variation " + name);
                string path = Path.Combine(artifacts, name + ".png"); bmp.Save(path, ImageFormat.Png); Console.WriteLine("SCREENSHOT " + path);
            }
        }
        private static AppSettings Seed(string language)
        {
            AppSettings s = new AppSettings { ApplyOnStartup = false, KeepApplied = false, TrayOnClose = false, MinimizeToTray = false, ShowDdcPanel = false, AutoExpandDdc = false, Language = language };
            s.Current["MOCK-A"] = new MonitorConfig(); s.Current["MOCK-B"] = new MonitorConfig();
            foreach (string name in new[] { "First", "Second" })
                s.Profiles.Add(new Profile { Name = name, Monitors = s.Current.ToDictionary(k => k.Key, k => k.Value.Clone()) });
            s.LastProfile = "First"; return s;
        }
        [STAThread] public static int Main(string[] args)
        {
            artifacts = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "GammaLift-DesktopUi-" + Guid.NewGuid().ToString("N"));
            scratch = Path.Combine(artifacts, "isolated-settings"); Directory.CreateDirectory(scratch);
            Set(typeof(SettingsStore), "_settingsDir", scratch);
            SettingsStore.Save(Seed(Loc.SourceCode));
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            try
            {
                main = new MainForm(); Set(main, "_hotkeyPauseDepth", 1); // 不注册全局热键，不启用低级钩子。
                main.Shown += delegate { main.BeginInvoke((MethodInvoker)delegate
                {
                    settings = Field<AppSettings>(main, "_settings");
                    Group("shown main and safe backend", delegate { Check(MonitorService.GammaApplyCount == 0, "startup must not apply"); Check(MonitorService.DdcCount == 0, "startup collapsed hardware never queried"); Check(Field<ComboBox>(main, "cboMonitors").Items.Count == 2, "two mock monitors"); Check(settings.PresetsSeeded, "existing profiles mark built-ins as seeded"); Check(settings.Profiles.Count == 2, "existing profiles are not duplicated"); Snapshot(main, "main-zh"); });
                    Group("top menu entries", MenuEntries);
                    Group("rename", Rename); Group("edit profiles", Sort); Group("copy saved share code", Copy); Group("import", Import);
                    Group("cycle and profile capture", Cycle); Group("settings pending and commit", Settings);
                    Group("notification truth table", Notifications); Group("minimize to tray", Minimize);
                    Group("layout parameter matrix", Layouts);
                    Group("brief actual settings hotkey rebind", Rebind);
                    CloseMain(main);
                }); };
                Application.Run(main);
            }
            catch (Exception e) { Check(false, e.GetBaseException().ToString()); }
            finally
            {
                ToastForm.CloseCurrent();
                if (main != null && !main.IsDisposed) CloseMain(main);
                if (clipboardSaved) { try { Clipboard.SetDataObject(clipboard, true, 5, 100); } catch (Exception e) { Check(false, "clipboard restore: " + e.Message); } }
                if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
            }
            Console.WriteLine("RESULT " + assertions + " assertions, " + failures + " failures; artifacts " + artifacts);
            Console.WriteLine("Scope: real WinForms, mocked monitors/registry, synthetic DPI; no SendInput. Physical monitors/games/DPI switching untested.");
            return failures == 0 ? 0 : 1;
        }
        private static void MenuEntries()
        {
            MenuStrip menu = Field<MenuStrip>(main, "menuMain");
            ToolStripMenuItem settingsItem = menu.Items.Cast<ToolStripItem>().OfType<ToolStripMenuItem>()
                .First(x => x.Name == "mnuSettings");
            Check(settingsItem.DropDownItems.Count == 4, "settings menu has four page entries");
            ToolStripItem startupItem = settingsItem.DropDownItems.Cast<ToolStripItem>()
                .First(x => x.Name == "mnuSettingsStartup");
            Automate(delegate { startupItem.PerformClick(); }, delegate(Form f)
            {
                Check(f is SettingsForm, "settings submenu opens a settings page");
                ((Button)f.CancelButton).PerformClick();
            });
            ToolStripMenuItem helpItem = menu.Items.Cast<ToolStripItem>().OfType<ToolStripMenuItem>()
                .First(x => x.Name == "mnuHelp");
            ToolStripItem tutorialItem = helpItem.DropDownItems.Cast<ToolStripItem>().First(x => x.Name == "mnuTutorial");
            Automate(delegate { tutorialItem.PerformClick(); }, delegate(Form f)
            {
                Check(f is TutorialForm, "help menu opens tutorial");
                Snapshot(f, "tutorial-zh");
                ((Button)f.AcceptButton).PerformClick();
            });
            ToolStripItem aboutItem = helpItem.DropDownItems.Cast<ToolStripItem>().First(x => x.Name == "mnuAbout");
            Automate(delegate { aboutItem.PerformClick(); }, delegate(Form f)
            {
                Check(f is AboutForm, "help menu opens about");
                Snapshot(f, "about-zh");
                ((Button)f.AcceptButton).PerformClick();
            });
            StatusStrip statusBar = Field<StatusStrip>(main, "statusStrip");
            Check(statusBar.Items.Count == 3, "status bar has status, dirty and version");
            ToolStripStatusLabel version = statusBar.Items[2] as ToolStripStatusLabel;
            Check(version != null && version.Text == "v" + AppInfo.Version, "status bar shows version");
        }
        private static void Rename()
        {
            Profile p = settings.Profiles[0]; int count = MonitorService.GammaApplyCount;
            p.HotKey = (int)(Keys.Control | Keys.Alt | Keys.F6); p.HotKeyTrigger = HotkeyTrigger.LongPress; Call(main, "ShowProfileHotkey");
            string monitors = State(p.Monitors), current = State(settings.Current); int key = p.HotKey;
            Automate(delegate { Call(main, "EditProfiles"); }, delegate(Form f)
            {
                Check(f is ProfileEditForm, "rename opens the unified edit dialog");
                TextBox name = Field<TextBox>(f, "txtName");
                Check(name.Text == "First", "edit dialog prefills the active profile name");
                name.Text = "Discarded";
                ((Button)f.CancelButton).PerformClick();
            });
            Check(p.Name == "First", "edit cancel discards a rename");
            foreach (string bad in new[] { "   ", "Second" })
            {
                Automate(delegate { Call(main, "EditProfiles"); }, delegate(Form f)
                {
                    TextBox name = Field<TextBox>(f, "txtName");
                    Label error = Field<Label>(f, "lblError");
                    name.Text = bad;
                    Field<Button>(f, "btnOk").PerformClick();
                    Check(f.DialogResult != DialogResult.OK && error.Text.Length > 0,
                        "bad rename stays in the edit dialog");
                    ((Button)f.CancelButton).PerformClick();
                });
                Check(p.Name == "First", "bad rename rejected");
            }
            Automate(delegate { Call(main, "EditProfiles"); }, delegate(Form f)
            {
                Field<TextBox>(f, "txtName").Text = "  Renamed  ";
                Field<Button>(f, "btnOk").PerformClick();
            });
            Check(p.Name == "Renamed" && settings.LastProfile == "Renamed", "rename updates last profile");
            Check(Field<ComboBox>(main, "cboProfiles").Text == "Renamed" && Field<Profile>(main, "_activeProfile") == p, "rename selection and active preserved");
            ToolStripMenuItem item = Field<ContextMenuStrip>(main, "trayMenu").Items.OfType<ToolStripMenuItem>().First(x => x.Text.StartsWith("Renamed"));
            Check(item.Checked, "renamed tray checked");
            Check(State(p.Monitors) == monitors && State(settings.Current) == current && p.HotKey == key && p.HotKeyTrigger == HotkeyTrigger.LongPress, "rename parameters/hotkey unchanged");
            Check(MonitorService.GammaApplyCount == count, "rename never applies");
        }
        private static void Sort()
        {
            List<Profile> original = settings.Profiles.ToList();
            Check(original.Count == 2, "edit fixture has two profiles");
            Profile a = original[0], b = original[1];
            int count = MonitorService.GammaApplyCount;
            ComboBox combo = Field<ComboBox>(main, "cboProfiles");
            Check(Field<Button>(main, "btnEditProfiles").Enabled, "edit enabled with profiles");

            Automate(delegate { Call(main, "EditProfiles"); }, delegate(Form f)
            {
                Check(f is ProfileEditForm, "edit opens ProfileEditForm");
                Field<Button>(f, "btnDown").PerformClick();
                Field<TextBox>(f, "txtName").Text = "Discarded";
                Field<ListBox>(f, "lstProfiles").SelectedIndex = 1;
                Field<Button>(f, "btnDelete").PerformClick();
                ((Button)f.CancelButton).PerformClick();
            });
            Check(settings.Profiles.SequenceEqual(original) && a.Name != "Discarded",
                "edit cancel keeps order, names and deletions");

            Automate(delegate { Call(main, "EditProfiles"); }, delegate(Form f)
            {
                Snapshot(f, "edit-zh");
                Field<Button>(f, "btnDown").PerformClick();
                Field<Button>(f, "btnOk").PerformClick();
            });
            Check(settings.Profiles[0] == b && settings.Profiles[1] == a, "edit OK commits moved order");
            Check(Field<Profile>(main, "_activeProfile") == a && settings.LastProfile == a.Name,
                "edit keeps active profile and LastProfile identity");
            Check(combo.Items[0].ToString() == b.Name && combo.Items[1].ToString() == a.Name &&
                combo.SelectedItem.ToString() == a.Name, "edit refreshes combo order and selection");
            Check(MonitorService.GammaApplyCount == count, "edit never applies a profile");
            List<ToolStripMenuItem> trayProfiles = Field<ContextMenuStrip>(main, "trayMenu").Items
                .OfType<ToolStripMenuItem>().Where(x => x.Text.StartsWith(a.Name) || x.Text.StartsWith(b.Name)).ToList();
            Check(trayProfiles.Count == 2 && trayProfiles[0].Text.StartsWith(b.Name) &&
                trayProfiles[1].Text.StartsWith(a.Name) && trayProfiles[1].Checked,
                "tray follows edited order and keeps active check");
            Call(main, "CycleProfile");
            Check(Field<Profile>(main, "_activeProfile") == b, "cycle follows the edited order");
            Call(main, "CycleProfile");
            Check(Field<Profile>(main, "_activeProfile") == a, "cycle wraps in the edited order");

            Set(main, "_activeProfile", null); settings.LastProfile = "";
            Automate(delegate { Call(main, "EditProfiles"); }, delegate(Form f)
            {
                Field<Button>(f, "btnDown").PerformClick();
                Field<Button>(f, "btnOk").PerformClick();
            });
            Check(Field<Profile>(main, "_activeProfile") == null && combo.SelectedIndex == -1,
                "edit without an active profile does not fake a selection");
            Check(!Field<ContextMenuStrip>(main, "trayMenu").Items.OfType<ToolStripMenuItem>().Any(x => x.Checked),
                "no tray item is checked without an active profile");

            settings.Profiles.Clear(); settings.Profiles.AddRange(original);
            Set(main, "_activeProfile", a); settings.LastProfile = a.Name;
            Call(main, "RefreshProfileCombo"); Call(main, "SelectProfileInCombo", a.Name);
            Call(main, "ReRegisterHotkeys");

            int beforeDelete = MonitorService.GammaApplyCount;
            Automate(delegate { Call(main, "EditProfiles"); }, delegate(Form f)
            {
                Field<ListBox>(f, "lstProfiles").SelectedIndex = 1;
                Field<Button>(f, "btnDelete").PerformClick();
                Field<Button>(f, "btnOk").PerformClick();
            });
            Check(settings.Profiles.Count == 1 && settings.Profiles[0] == a &&
                Field<Profile>(main, "_activeProfile") == a && settings.LastProfile == a.Name,
                "deleting a non-active profile keeps the active one");
            Check(MonitorService.GammaApplyCount == beforeDelete, "deleting non-active never applies");
            Check(Field<Button>(main, "btnEditProfiles").Enabled, "edit stays enabled with one profile");

            settings.Profiles.Add(b); Call(main, "RefreshProfileCombo"); Call(main, "ReRegisterHotkeys");
            MonitorConfig savedB = null;
            if (b.Monitors != null) { b.Monitors.TryGetValue("MOCK-B", out savedB); b.Monitors.Remove("MOCK-B"); }
            Call(main, "SwitchToProfile", b);
            Check(Field<ToolStripStatusLabel>(main, "lblDirty").Text.Length == 0 &&
                settings.Current["MOCK-B"].IsNeutral(),
                "applying a profile resets monitors it does not record");
            if (b.Monitors != null && savedB != null) b.Monitors["MOCK-B"] = savedB;
            int beforeActiveDelete = MonitorService.GammaApplyCount;
            Automate(delegate { Call(main, "EditProfiles"); }, delegate(Form f)
            {
                Field<Button>(f, "btnDelete").PerformClick();
                Field<Button>(f, "btnOk").PerformClick();
            });
            Check(settings.Profiles.Count == 1 && settings.Profiles[0] == a &&
                Field<Profile>(main, "_activeProfile") == a && settings.LastProfile == a.Name,
                "deleting the active profile falls back to the first survivor");
            Check(MonitorService.GammaApplyCount > beforeActiveDelete, "active fallback applies the survivor");

            settings.Profiles.Clear(); settings.Profiles.AddRange(original);
            Set(main, "_activeProfile", a); settings.LastProfile = a.Name;
            Call(main, "RefreshProfileCombo"); Call(main, "SelectProfileInCombo", a.Name);
            Call(main, "ReRegisterHotkeys");
            Check(combo.Items[0].ToString() == a.Name && combo.SelectedItem.ToString() == a.Name,
                "edit fixture order restored");
        }

        private static void BackupClipboard()
        {
            // 所有格式先物化, 成功后再写入剪贴板；内容既不记录也不落盘。
            IDataObject original = Clipboard.GetDataObject(); DataObject backup = new DataObject();
            if (original != null) foreach (string format in original.GetFormats(false)) backup.SetData(format, false, original.GetData(format, false));
            clipboard = backup; clipboardSaved = true;
        }
        private static void Copy()
        {
            try
            {
                BackupClipboard();
                bool loaded = Field<bool>(main, "_loaded"); Set(main, "_loaded", false);
                Call(main, "SetMoreExpanded", true, false);
                Set(main, "_loaded", loaded);
                settings.Current["MOCK-A"].Red.Brightness = 17;
                settings.Current["MOCK-A"].Green.Brightness = 5;
                settings.Current["MOCK-A"].Linked = false;
                Call(main, "LoadMonitorToUi");
                Call(main, "UpdateShareButtons");
                int count = MonitorService.GammaApplyCount;
                Button copy = Field<Button>(main, "btnCopyShareCode");
                Check(copy.Enabled, "non-neutral settings enable copy");
                copy.PerformClick();
                MonitorConfig decoded; string error;
                string copied = Clipboard.GetText();
                for (int i = 0; i < 10 && !ProfileShareCode.TryDecode(copied, out decoded, out error); i++)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(50);
                    copied = Clipboard.GetText();
                }
                Check(ProfileShareCode.TryDecode(copied, out decoded, out error), "copied GL2 decodes");
                Check(decoded != null && State(decoded) == State(settings.Current["MOCK-A"]), "copy uses CURRENT live settings");
                Check(copied.Length == 22, "copy produces the compact per-channel code");
                Check(Field<ToolStripStatusLabel>(main, "lblStatus").Text == Loc.T("已复制分享码"),
                    "copy reports success in the status bar");
                Snapshot(main, "main-zh-share");
                Check(MonitorService.GammaApplyCount == count, "copy never applies");
                Clipboard.SetDataObject(clipboard, true, 5, 100); clipboardSaved = false;
                Call(main, "SetMoreExpanded", false, false);
                Console.WriteLine("CAVEAT clipboard materialized IDataObject restored; delayed-render/provider identity cannot be preserved.");
            }
            catch (Exception) { throw; }
        }
        private static void Import()
        {
            bool loaded = Field<bool>(main, "_loaded"); Set(main, "_loaded", false);
            Call(main, "SetMoreExpanded", true, false);
            Set(main, "_loaded", loaded);
            int count = MonitorService.GammaApplyCount;
            Profile active = Field<Profile>(main, "_activeProfile");
            int profiles = settings.Profiles.Count;
            string last = settings.LastProfile;

            Button importButton = Field<Button>(main, "btnImportShareCode");
            Button copyButton = Field<Button>(main, "btnCopyShareCode");
            settings.Current["MOCK-A"] = new MonitorConfig();
            Call(main, "LoadMonitorToUi");
            Call(main, "UpdateShareButtons");
            Check(!copyButton.Enabled && importButton.Enabled,
                "neutral settings disable copy but keep import available");

            string code = ProfileShareCode.Encode(new MonitorConfig
            {
                Linked = false,
                Red = new ChannelSettings { Brightness = 23, Gamma = 1.25 },
                Green = new ChannelSettings(),
                Blue = new ChannelSettings()
            });
            MonitorConfig shared = null;
            using (ShareCodeForm dialog = new ShareCodeForm(1f, 1f))
            {
                dialog.Show();
                Application.DoEvents();
                TextBox box = Field<TextBox>(dialog, "txtCode");
                Button dialogImport = Field<Button>(dialog, "btnImport");
                Label error = Field<Label>(dialog, "lblError");

                box.Text = "GL2.bad";
                dialogImport.PerformClick();
                Check(dialog.DialogResult != DialogResult.OK && error.Text.Length > 0,
                    "invalid share code stays in the dialog with an error");
                box.Text = "GL1:AAAA:BBBB";
                dialogImport.PerformClick();
                Check(dialog.DialogResult != DialogResult.OK && error.Text.Length > 0,
                    "old GL1 code is rejected in the dialog");

                box.Text = code;
                dialogImport.PerformClick();
                Check(dialog.DialogResult == DialogResult.OK && dialog.Config != null,
                    "valid share code closes the dialog");
                shared = dialog.Config;
            }

            DisplayMonitor monitor = (DisplayMonitor)Call(main, "SelectedMonitor");
            Call(main, "ApplySharedConfig", shared, monitor);
            MonitorConfig applied = settings.Current["MOCK-A"];
            Check(applied.Red.Brightness == 23 && Math.Abs(applied.Red.Gamma - 1.25) < 0.001 && !applied.Linked,
                "share code applied to current monitor");
            Check(settings.Profiles.Count == profiles && Field<Profile>(main, "_activeProfile") == active &&
                settings.LastProfile == last, "import does not add or activate a profile");
            Check(MonitorService.GammaApplyCount == count + 1, "import applies exactly once");
            Check(Field<ToolStripStatusLabel>(main, "lblStatus").Text.Contains("Mock A"),
                "status reports the applied monitor");
            Check(Field<ToolStripStatusLabel>(main, "lblDirty").Text.Length > 0,
                "import marks unsaved changes");
            Check(copyButton.Enabled, "imported settings enable copy");

            settings.Current = active.Monitors.ToDictionary(k => k.Key, k => k.Value.Clone());
            settings.LastProfile = active.Name;
            Call(main, "SetMoreExpanded", false, false);
            Call(main, "LoadMonitorToUi");
            Call(main, "UpdateShareButtons");
        }
        private static void Cycle()
        {
            List<Profile> saved = settings.Profiles; Profile first = saved[0]; int count = MonitorService.GammaApplyCount;
            settings.Profiles = new List<Profile>(); Set(main, "_activeProfile", null); Call(main, "CycleProfile"); Check(count == MonitorService.GammaApplyCount, "cycle empty no-op");
            settings.Profiles.Add(first); Set(main, "_activeProfile", first); Call(main, "CycleProfile"); Check(count == MonitorService.GammaApplyCount, "cycle single no-op");
            settings.Profiles = saved.Take(2).ToList(); Call(main, "RefreshProfileCombo");
            Call(main, "CycleProfile"); Check(Field<Profile>(main, "_activeProfile") == saved[1] && MonitorService.GammaApplyCount == count + 2, "cycle next exactly once");
            Call(main, "CycleProfile"); Check(Field<Profile>(main, "_activeProfile") == first && MonitorService.GammaApplyCount == count + 4, "cycle wrap");
            settings.Current["MOCK-A"].Red.Brightness = 14; count = MonitorService.GammaApplyCount;
            Automate(delegate { Call(main, "CycleProfile"); }, null, 2); Check(Field<Profile>(main, "_activeProfile") == first && MonitorService.GammaApplyCount == count, "dirty cancel no advance");
            Automate(delegate { Call(main, "CycleProfile"); }, null, 7); Check(Field<Profile>(main, "_activeProfile") == saved[1] && MonitorService.GammaApplyCount == count + 2, "dirty No switches exactly once");
            settings.Profiles = saved; Call(main, "RefreshProfileCombo"); Profile p = saved[1];
            settings.CycleHotKey = (int)(Keys.Control | Keys.Alt | Keys.F8); settings.CycleHotKeyTrigger = HotkeyTrigger.Single;
            TextBox capture = Field<TextBox>(main, "txtHotkey"); count = MonitorService.GammaApplyCount;
            Key(capture, (Keys)settings.CycleHotKey); Check(p.HotKey == 0, "profile capture same key+trigger conflicts cycle");
            Field<ComboBox>(main, "cboHotkeyTrigger").SelectedIndex = 1; Key(capture, (Keys)settings.CycleHotKey);
            Check(p.HotKey == settings.CycleHotKey && p.HotKeyTrigger == HotkeyTrigger.DoublePress && Field<Button>(main, "btnClearHotkey").Visible, "different trigger allowed/display updated");
            Field<ComboBox>(main, "cboHotkeyTrigger").SelectedIndex = 0; Check(p.HotKeyTrigger == HotkeyTrigger.DoublePress, "trigger conflict rolls back");
            Call(main, "DispatchHotkeyActions", new List<int> { 0 }); Check(MonitorService.GammaApplyCount == count, "focused capture pauses existing action dispatch");
            Key(capture, Keys.F12); Check(p.HotKey == settings.CycleHotKey, "reserved F12 rejected"); Key(capture, Keys.Escape); Check(p.HotKey == 0, "Esc clears profile key");
            Field<ComboBox>(main, "cboMonitors").Focus();
        }
        private static void Settings()
        {
            settings.Profiles[0].HotKey = (int)(Keys.Control | Keys.Alt | Keys.F7); settings.Profiles[0].HotKeyTrigger = HotkeyTrigger.Single;
            string before = State(settings); int writes = SystemIntegration.StartupWrites;

            Automate(delegate { Call(main, "OpenSettings", SettingsPage.Startup, false); }, delegate(Form f)
            {
                Snapshot(f, "settings-zh-startup");
                Check(Field<GroupBox>(f, "grpPage").Visible, "startup page is a regular visible group");
                Field<CheckBox>(f, "chkAutoStart").Checked = true;
                ((Button)f.CancelButton).PerformClick();
            });
            Check(State(settings) == before && SystemIntegration.StartupWrites == writes, "startup Cancel unchanged/no startup write");

            Automate(delegate { Call(main, "OpenSettings", SettingsPage.Tray, false); }, delegate(Form f)
            {
                Field<CheckBox>(f, "chkNotifyEnabled").Checked = false;
                Check(!Field<CheckBox>(f, "chkNotifyProfileApplied").Enabled && !Field<CheckBox>(f, "chkNotifyTrayOnClose").Enabled && Field<CheckBox>(f, "chkNotifyProfileApplied").Checked, "master gray preserves children");
                Snapshot(f, "settings-zh-tray");
                ((Button)f.CancelButton).PerformClick();
            });
            Check(State(settings) == before, "tray page Cancel unchanged");

            Automate(delegate { Call(main, "OpenSettings", SettingsPage.Display, false); }, delegate(Form f)
            {
                Check(!Field<CheckBox>(f, "chkAutoDdc").Checked && Field<CheckBox>(f, "chkShowDdc").Enabled, "auto expand off keeps fixed expand selectable");
                Field<CheckBox>(f, "chkShowDdc").Checked = true; Field<CheckBox>(f, "chkKeepApplied").Checked = true;
                Field<CheckBox>(f, "chkAutoDdc").Checked = true;
                Check(!Field<CheckBox>(f, "chkShowDdc").Checked && !Field<CheckBox>(f, "chkShowDdc").Enabled, "auto expand disables fixed expand");
                Field<CheckBox>(f, "chkAutoDdc").Checked = false;
                Check(Field<CheckBox>(f, "chkShowDdc").Enabled && Field<CheckBox>(f, "chkShowDdc").Checked, "unchecking auto restores fixed expand");
                ((Button)f.CancelButton).PerformClick();
            });
            Check(State(settings) == before, "display page Cancel unchanged");

            ToastForm.ShowMessage("Test only", 1f, 1f);

            Automate(delegate { Call(main, "OpenSettings", SettingsPage.Tray, false); }, delegate(Form f)
            {
                Field<CheckBox>(f, "chkNotifyEnabled").Checked = false;
                Field<CheckBox>(f, "chkMinimizeToTray").Checked = true;
                ((Button)f.AcceptButton).PerformClick();
            });
            Check(settings.MinimizeToTray && !settings.NotifyEnabled, "tray page OK committed");
            Check(Field<ToastForm>(typeof(ToastForm), "_current") == null, "master disable closes existing toast");

            Automate(delegate { Call(main, "OpenSettings", SettingsPage.Startup, true); }, delegate(Form f)
            {
                Check(Field<Button>(f, "btnUnlock").Focused, "status link focuses the unlock action");
                ((Button)f.CancelButton).PerformClick();
            });

            Automate(delegate { Call(main, "OpenSettings", SettingsPage.Appearance, false); }, delegate(Form f)
            {
                Field<ComboBox>(f, "cboUiScale").SelectedIndex = 2;
                ((Button)f.AcceptButton).PerformClick();
            });
            Check(settings.UiScalePercent == 115, "appearance page OK commits UI scale");

            Automate(delegate { Call(main, "OpenSettings", SettingsPage.Startup, false); }, delegate(Form f)
            {
                Field<CheckBox>(f, "chkAutoStart").Checked = true;
                ((Button)f.AcceptButton).PerformClick();
            });
            Check(SystemIntegration.StartupWrites == writes + 1 && SystemIntegration.LastStartup, "startup page OK mock startup write");

            // 循环切换热键位于主界面配置文件组上方, 设置页不含快捷键项
            TextBox cycleBox = Field<TextBox>(main, "txtCycleHotkey");
            ComboBox cycleTrigger = Field<ComboBox>(main, "cboCycleTrigger");
            int cycleBefore = settings.CycleHotKey;
            Key(cycleBox, (Keys)settings.Profiles[0].HotKey);
            Check(settings.CycleHotKey == cycleBefore, "cycle capture rejects profile conflict");
            cycleTrigger.SelectedIndex = 1;
            Key(cycleBox, (Keys)settings.Profiles[0].HotKey);
            Check(settings.CycleHotKey == settings.Profiles[0].HotKey && settings.CycleHotKeyTrigger == HotkeyTrigger.DoublePress,
                "cycle accepts same key with different trigger");
            cycleTrigger.SelectedIndex = 0;
            Check(cycleTrigger.SelectedIndex == 1 && settings.CycleHotKeyTrigger == HotkeyTrigger.DoublePress,
                "cycle trigger conflict rolls back");
            Key(cycleBox, Keys.Control | Keys.Alt | Keys.Shift | Keys.F9);
            Check(settings.CycleHotKey == (int)(Keys.Control | Keys.Alt | Keys.Shift | Keys.F9) &&
                settings.CycleHotKeyTrigger == HotkeyTrigger.DoublePress, "cycle hotkey capture commits");
        }
        private static void Notifications()
        {
            settings.TrayOnClose = true; Profile p = Field<Profile>(main, "_activeProfile");
            for (int bits = 0; bits < 8; bits++)
            {
                settings.NotifyEnabled = (bits & 4) != 0; settings.NotifyProfileApplied = (bits & 2) != 0; settings.NotifyTrayOnClose = (bits & 1) != 0;
                main.Show(); ToastForm.CloseCurrent(); main.Close();
                Check(!main.Visible && (Field<ToastForm>(typeof(ToastForm), "_current") != null) == (settings.NotifyEnabled && settings.NotifyTrayOnClose), "tray toast truth table " + bits);
                ToastForm.CloseCurrent(); Application.DoEvents(); Call(main, "ApplyProfile", p);
                Check((Field<ToastForm>(typeof(ToastForm), "_current") != null) == (settings.NotifyEnabled && settings.NotifyProfileApplied), "apply toast truth table " + bits);
                ToastForm toast = Field<ToastForm>(typeof(ToastForm), "_current");
                Check(toast == null || GetForegroundWindow() != toast.Handle, "toast never foreground " + bits);
                if (toast != null)
                {
                    CreateParams cp = (CreateParams)toast.GetType().GetProperty("CreateParams", Flags).GetValue(toast, null);
                    Check((cp.ExStyle & NativeMethods.WS_EX_NOACTIVATE) != 0 && (bool)toast.GetType().GetProperty("ShowWithoutActivation", Flags).GetValue(toast, null), "toast activation protection " + bits);
                }
                Check(Field<ToolStripStatusLabel>(main, "lblStatus").Text.Contains(p.Name), "status still updates " + bits);
                ToastForm.CloseCurrent();
            }
            settings.TrayOnClose = false; main.Show();

            settings.NotifyEnabled = true; settings.NotifyProfileApplied = true;
            settings.NotifyProfileAppliedForeground = true;
            ToastForm.CloseCurrent(); Call(main, "ApplyProfile", p);
            Check(Field<ToastForm>(typeof(ToastForm), "_current") != null, "foreground notification when enabled");
            ToastForm.CloseCurrent();
            settings.NotifyProfileAppliedForeground = false;
            Call(main, "ApplyProfile", p);
            Check(Field<ToastForm>(typeof(ToastForm), "_current") == null, "foreground notification disabled by default");
        }
        private static void Minimize()
        {
            settings.NotifyEnabled = false;
            settings.MinimizeToTray = true;
            main.Show(); main.WindowState = FormWindowState.Minimized;
            Application.DoEvents(); Application.DoEvents();
            Check(!main.Visible, "minimize hides to tray");
            Call(main, "ShowMainWindow");
            Check(main.Visible && main.WindowState == FormWindowState.Normal, "tray restore shows normal window");

            settings.MinimizeToTray = false;
            main.Show(); main.WindowState = FormWindowState.Minimized; Application.DoEvents();
            Check(main.Visible && main.WindowState == FormWindowState.Minimized,
                "minimize stays on taskbar when disabled");
            Call(main, "ShowMainWindow");
            Check(main.WindowState == FormWindowState.Normal, "restore from taskbar minimize");
        }
        private static void Geometry(Control parent, string context, bool scroll)
        {
            Control[] controls = parent.Controls.Cast<Control>().Where(c => c.Visible && c.Width > 0 && c.Height > 0).ToArray();
            foreach (Control c in controls)
            {
                if (parent is Panel && scroll) Check(c.Left >= 0 && c.Right <= parent.ClientSize.Width, context + " scroll horizontal containment " + c.Text);
                else Check(parent.ClientRectangle.Contains(c.Bounds), context + " contains " + c.GetType().Name + ":" + c.Text);
                if (c is GroupBox || c is Panel) Geometry(c, context, scroll);
            }
            for (int i = 0; i < controls.Length; i++) for (int j = i + 1; j < controls.Length; j++)
                Check(!controls[i].Bounds.IntersectsWith(controls[j].Bounds), context + " overlap " + controls[i].Text + controls[i].Bounds + " / " + controls[j].Text + controls[j].Bounds);
        }
        private static void Layouts()
        {
            foreach (string language in new[] { Loc.SourceCode, "en", "de", "ru" })
            {
                SettingsStore.Save(Seed(language)); MainForm f = new MainForm(); Set(f, "_hotkeyPauseDepth", 1); f.Show();
                try
                {
                    foreach (float dpi in new[] { 1f, 1.25f, 1.5f, 2f }) foreach (float scale in new[] { 0.85f, 1f, 1.15f })
                    {
                        string label = language + "/" + scale + "/" + dpi;
                        Set(f, "_dpiScale", dpi); f.ClientSize = new Size(900, 700); Call(f, "ApplyUiScale", scale);
                        Application.DoEvents(); f.PerformLayout(); Call(f, "LayoutUi"); f.Refresh();
                        Check(f.ClientSize == new Size((int)Math.Round(560 * scale * dpi),
                            (int)Call(f, "CompactClientHeight")), "design reset " + label);
                        Geometry(f, "Main " + label, false);
                        bool loaded = Field<bool>(f, "_loaded"); Set(f, "_loaded", false);
                        Call(f, "SetMoreExpanded", true, false); Application.DoEvents(); Call(f, "LayoutUi");
                        Geometry(f, "Main more " + label, false);
                        Call(f, "SetMoreExpanded", false, false); Application.DoEvents(); Call(f, "LayoutUi");
                        Set(f, "_loaded", loaded);
                        if (language == Loc.SourceCode && scale == 0.85f && dpi == 1f) Snapshot(f, "main-zh-85");
                        Loc.SetLanguage(language);
                        foreach (SettingsPage page in new[] { SettingsPage.Startup, SettingsPage.Appearance, SettingsPage.Display, SettingsPage.Tray })
                        {
                            SettingsPage currentPage = page;
                            using (SettingsForm s = new SettingsForm(Seed(language), currentPage, false, scale, dpi))
                            {
                                s.Shown += delegate { s.BeginInvoke((MethodInvoker)delegate
                                {
                                    Geometry(s, "Settings " + currentPage + " " + label, true); Panel panel = Field<Panel>(s, "pnlContent");
                                    panel.ScrollControlIntoView(Field<GroupBox>(s, "grpPage"));
                                    Check(s.ClientRectangle.Contains(Field<Button>(s, "btnOk").Bounds) && s.ClientRectangle.Contains(Field<Button>(s, "btnCancel").Bounds), "footer after scroll " + currentPage + " " + label);
                                    if (language == "de" && scale == 1f && dpi == 1.5f && currentPage == SettingsPage.Tray) Snapshot(s, "settings-de-150-tray");
                                    ((Button)s.CancelButton).PerformClick();
                                }); }; s.ShowDialog(f);
                            }
                        }
                    }
                }
                finally { CloseMain(f); }
            }
            Loc.SetLanguage(Loc.SourceCode);
        }
        private static void Rebind()
        {
            // 短暂注册无输入的唯一测试组合键，验证主界面循环热键的重新注册路径；马上卸载。
            Field<ComboBox>(main, "cboMonitors").Focus(); Set(main, "_hotkeyPauseDepth", 0);
            foreach (Profile p in settings.Profiles) p.HotKey = 0;
            int generation = Field<int>(main, "_hotkeyGeneration");
            try
            {
                Key(Field<TextBox>(main, "txtCycleHotkey"), Keys.Control | Keys.Alt | Keys.Shift | Keys.F9);
                Field<ComboBox>(main, "cboMonitors").Focus();
                Application.DoEvents();
                Check(Field<int>(main, "_hotkeyGeneration") > generation && Field<List<int>>(main, "_hotkeyIds").Count == 1, "main window cycle hotkey actually rebinds");
            }
            finally { Call(main, "StopHotkeys"); Set(main, "_hotkeyPauseDepth", 1); }
        }
    }
}
