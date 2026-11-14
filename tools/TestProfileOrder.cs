using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace GammaLift
{
    /// <summary>预设编辑（排序 / 改名 / 删除）的 UI、对象身份、缺字段配置兼容与持久化回归测试。</summary>
    internal static class TestProfileOrder
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.NonPublic | BindingFlags.Public;
        private static int _checks;

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                TestDialog();
                TestEdits();
                TestLocalizedLayouts();
                TestPersistence();
                Console.WriteLine("PASS: {0} profile-edit checks; isolated settings only.", _checks);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex.GetBaseException());
                return 1;
            }
        }

        private static void Check(bool condition, string name)
        {
            _checks++;
            if (!condition) throw new InvalidOperationException(name);
        }

        private static T Field<T>(object obj, string name)
        {
            return (T)obj.GetType().GetField(name, Flags).GetValue(obj);
        }

        private static object Call(object obj, string name, params object[] args)
        {
            return obj.GetType().GetMethod(name, Flags).Invoke(obj, args);
        }

        private static Profile MakeProfile(string name)
        {
            Profile p = new Profile();
            p.Name = name;
            p.Monitors["TEST"] = new MonitorConfig();
            return p;
        }

        private static void Order(List<Profile> actual, params Profile[] expected)
        {
            Check(actual.Count == expected.Length, "Profile count");
            for (int i = 0; i < expected.Length; i++)
                Check(object.ReferenceEquals(actual[i], expected[i]), "Profile identity/order at " + i +
                    ": actual=" + actual[i].Name + ", expected=" + expected[i].Name);
        }

        private static void TestDialog()
        {
            Loc.SetLanguage(Loc.SourceCode);
            Profile a = MakeProfile("A");
            Profile b = MakeProfile("B");
            Profile c = MakeProfile("C");
            List<Profile> source = new List<Profile> { a, b, c };

            using (ProfileEditForm form = new ProfileEditForm(source, b, 1f, 1f))
            {
                ListBox list = Field<ListBox>(form, "lstProfiles");
                Button up = Field<Button>(form, "btnUp");
                Button down = Field<Button>(form, "btnDown");
                Check(list.SelectedIndex == 1, "Active profile is initially selected");
                Check(list.Items[1].ToString().StartsWith("2. B"), "Visible numbering");
                Check(list.Items[1].ToString().Contains("[" + Loc.T("当前") + "]") &&
                    !list.Items[0].ToString().Contains("[" + Loc.T("当前") + "]"),
                    "Only the active profile is marked as current");
                Check(up.Enabled && down.Enabled, "Middle item can move both ways");

                form.Show();
                Application.DoEvents();
                down.PerformClick();
                Order(form.OrderedProfiles, a, c, b);
                Check(list.SelectedIndex == 2 && !down.Enabled, "Move down updates selection and boundary");

                Call(list, "OnKeyDown", new KeyEventArgs(Keys.Alt | Keys.Up));
                Order(form.OrderedProfiles, a, b, c);
                Check(list.SelectedIndex == 1, "Alt+Up moves selected profile");

                Call(form, "ListDragDrop", list, new DragEventArgs(null, 0, 0, 0,
                    DragDropEffects.None, DragDropEffects.None));
                Order(form.OrderedProfiles, a, b, c);
                Check(list.SelectedIndex == 1, "Null drag data is ignored");

                Point client = new Point(8, list.ItemHeight * 2 + list.ItemHeight / 2);
                Point screen = list.PointToScreen(client);
                DragEventArgs drag = new DragEventArgs(new DataObject("GammaLift.ProfileEdit", 0),
                    0, screen.X, screen.Y, DragDropEffects.Move, DragDropEffects.Move);
                Call(form, "ListDragDrop", list, drag);
                Order(form.OrderedProfiles, b, c, a);
                Check(list.SelectedIndex == 2, "Drag drop moves by reference and selects target");

                ((Button)form.CancelButton).PerformClick();
                Check(form.DialogResult == DialogResult.Cancel, "Cancel result");
            }
            Order(source, a, b, c);
            Check(source[0].Monitors.ContainsKey("TEST"), "Cancel leaves source profiles and parameters untouched");

            using (ProfileEditForm form = new ProfileEditForm(source, a, 1f, 1f))
            {
                ListBox list = Field<ListBox>(form, "lstProfiles");
                Button down = Field<Button>(form, "btnDown");
                form.Show();
                Application.DoEvents();
                Check(!Field<Button>(form, "btnUp").Enabled, "First item cannot move up");
                down.PerformClick();
                down.PerformClick();
                Check(!down.Enabled && list.SelectedIndex == 2, "Last item cannot move down");
                Field<Button>(form, "btnOk").PerformClick();
                Check(form.DialogResult == DialogResult.OK, "OK result");
                Order(form.OrderedProfiles, b, c, a);
            }
            Order(source, a, b, c);
        }

        private static void TestEdits()
        {
            Loc.SetLanguage(Loc.SourceCode);
            Profile a = MakeProfile("A");
            Profile b = MakeProfile("B");
            Profile c = MakeProfile("C");
            List<Profile> source = new List<Profile> { a, b, c };

            using (ProfileEditForm form = new ProfileEditForm(source, a, 1f, 1f))
            {
                form.Show();
                Application.DoEvents();
                ListBox list = Field<ListBox>(form, "lstProfiles");
                TextBox name = Field<TextBox>(form, "txtName");
                Button delete = Field<Button>(form, "btnDelete");
                Button ok = Field<Button>(form, "btnOk");
                Label error = Field<Label>(form, "lblError");

                Check(name.Text == "A" && delete.Enabled, "Selected profile loads its draft name");
                name.Text = "A2";
                Check(form.NameFor(a) == "A2" && list.Items[0].ToString().StartsWith("1. A2") &&
                    list.Items[0].ToString().Contains("[" + Loc.T("当前") + "]"),
                    "Typing updates draft name and keeps the current marker");

                name.Text = "   ";
                ok.PerformClick();
                Check(form.DialogResult != DialogResult.OK && error.Text.Length > 0,
                    "Empty name blocks OK and reports an error");
                name.Text = "A2";
                list.SelectedIndex = 1;
                name.Text = "A2";
                ok.PerformClick();
                Check(form.DialogResult != DialogResult.OK && error.Text.Length > 0,
                    "Duplicate draft name blocks OK");

                name.Text = "B2";
                delete.PerformClick();
                Check(form.OrderedProfiles.Count == 2 && !form.OrderedProfiles.Contains(b),
                    "Delete only removes the profile from the draft");
                Check(b.Name == "B" && c.Name == "C", "Draft edits do not mutate original profiles");
                ok.PerformClick();
                Check(form.DialogResult == DialogResult.OK, "Edited draft confirms");
                Order(form.OrderedProfiles, a, c);
                Check(form.NameFor(a) == "A2" && form.NameFor(c) == "C", "Confirmed draft names survive");
            }
            Order(source, a, b, c);
            Check(a.Name == "A" && b.Name == "B" && c.Name == "C",
                "Main form owns commit; dialog never renames source profiles");

            using (ProfileEditForm form = new ProfileEditForm(new List<Profile> { a }, a, 1f, 1f))
            {
                form.Show();
                Application.DoEvents();
                Field<Button>(form, "btnDelete").PerformClick();
                Field<Button>(form, "btnOk").PerformClick();
                Check(form.DialogResult == DialogResult.OK && form.OrderedProfiles.Count == 0,
                    "Deleting every draft profile is allowed");
            }
            Check(a.Name == "A", "Deleting all drafts does not mutate the source");
        }

        private static void TestLocalizedLayouts()
        {
            List<Profile> profiles = new List<Profile>();
            for (int i = 0; i < 12; i++) profiles.Add(MakeProfile("Profile " + (i + 1)));

            foreach (UiLanguage language in Loc.Languages)
            {
                Loc.SetLanguage(language.Code);
                foreach (float scale in new float[] { 0.85f, 1f, 1.15f })
                {
                    foreach (float dpi in new float[] { 1f, 1.5f })
                    {
                        using (ProfileEditForm form = new ProfileEditForm(profiles, profiles[0], scale, dpi))
                        {
                            string context = language.Code + "/" + scale + "/" + dpi;
                            Check(form.ClientSize == new Size(
                                (int)Math.Round(520 * scale * dpi),
                                (int)Math.Round(370 * scale * dpi)), "Dialog size " + context);
                            foreach (Control control in form.Controls)
                                Check(form.ClientRectangle.Contains(control.Bounds),
                                    "Contained " + control.Name + " " + context);

                            Label hint = Field<Label>(form, "lblHint");
                            ListBox list = Field<ListBox>(form, "lstProfiles");
                            Button up = Field<Button>(form, "btnUp");
                            Button down = Field<Button>(form, "btnDown");
                            Button delete = Field<Button>(form, "btnDelete");
                            Label nameLabel = Field<Label>(form, "lblName");
                            TextBox name = Field<TextBox>(form, "txtName");
                            Label error = Field<Label>(form, "lblError");
                            Button ok = Field<Button>(form, "btnOk");
                            Button cancel = Field<Button>(form, "btnCancel");
                            Check(hint.Bottom < list.Top && list.Right < up.Left,
                                "Hint/list/move columns do not overlap " + context);
                            Check(up.Bottom < down.Top && down.Bottom < delete.Top,
                                "Move and delete buttons are stacked " + context);
                            Check(list.Bottom < nameLabel.Top && list.Bottom < name.Top,
                                "List clears the name row " + context);
                            Check(name.Bottom < error.Top && error.Bottom < ok.Top,
                                "Name/error/footer vertical separation " + context);
                            Check(ok.Right < cancel.Left && list.Items.Count == 12,
                                "Footer separation and full list " + context);
                            Check(name.Width > 0 && name.Enabled && delete.Enabled,
                                "Selected draft has editable name and delete " + context);
                            Size measured = TextRenderer.MeasureText(hint.Text, hint.Font,
                                new Size(hint.Width, int.MaxValue), TextFormatFlags.WordBreak);
                            Check(measured.Height <= hint.Height, "Wrapped hint fits " + context);
                        }
                    }
                }
            }
            Loc.SetLanguage(Loc.SourceCode);
        }

        private static void TestPersistence()
        {
            string dir = Path.Combine(Path.GetTempPath(), "GammaLift-ProfileOrder-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            typeof(SettingsStore).GetField("_settingsDir", Flags).SetValue(null, dir);
            try
            {
                // 模拟缺少排序字段与近期新增字段的配置；数组原顺序原样保留。
                File.WriteAllText(SettingsStore.SettingsPath,
                    "{\"Profiles\":[{\"Name\":\"A\",\"HotKey\":101,\"HotKeyTrigger\":0}," +
                    "{\"Name\":\"B\",\"HotKey\":102,\"HotKeyTrigger\":1}," +
                    "{\"Name\":\"C\",\"HotKey\":103,\"HotKeyTrigger\":2}]," +
                    "\"LastProfile\":\"B\",\"ApplyOnStartup\":true}");
                AppSettings old = SettingsStore.Load();
                Check(old.Profiles[0].Name == "A" && old.Profiles[1].Name == "B" &&
                    old.Profiles[2].Name == "C", "Existing order remains unchanged");
                Check(old.LastProfile == "B" && old.AutoExpandDdc && old.Current != null &&
                    old.Baselines != null && old.DdcCapabilities != null,
                    "Missing-field defaults and LastProfile remain compatible");

                List<Profile> reordered = new List<Profile> { old.Profiles[1], old.Profiles[2], old.Profiles[0] };
                old.Profiles.Clear();
                old.Profiles.AddRange(reordered);
                SettingsStore.Save(old);
                string json = File.ReadAllText(SettingsStore.SettingsPath);
                Check(json.IndexOf("\"Name\":\"B\"", StringComparison.Ordinal) <
                    json.IndexOf("\"Name\":\"C\"", StringComparison.Ordinal) &&
                    json.IndexOf("\"Name\":\"C\"", StringComparison.Ordinal) <
                    json.IndexOf("\"Name\":\"A\"", StringComparison.Ordinal),
                    "Serialized array uses the manual order");

                AppSettings loaded = SettingsStore.Load();
                Check(loaded.Profiles[0].Name == "B" && loaded.Profiles[1].Name == "C" &&
                    loaded.Profiles[2].Name == "A", "Order survives a fresh load/restart");
                Check(loaded.LastProfile == "B" && loaded.Profiles[0].HotKey == 102 &&
                    loaded.Profiles[0].HotKeyTrigger == HotkeyTrigger.DoublePress &&
                    loaded.Profiles[2].HotKey == 101,
                    "LastProfile and hotkeys follow profile identities: last=" + loaded.LastProfile +
                    ", first=" + loaded.Profiles[0].HotKey + "/" + loaded.Profiles[0].HotKeyTrigger +
                    ", third=" + loaded.Profiles[2].HotKey);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                typeof(SettingsStore).GetField("_settingsDir", Flags).SetValue(null, null);
            }
        }
    }
}
