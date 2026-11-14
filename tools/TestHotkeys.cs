using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace GammaLift
{
    internal static class TestHotkeys
    {
        private const int Key = (int)Keys.F8;
        private static int _checks;

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 1 && args[0] == "--native")
                {
                    Application.EnableVisualStyles();
                    using (NativeTestForm form = new NativeTestForm())
                    {
                        Application.Run(form);
                        return form.Result;
                    }
                }
                if (args.Length != 0) throw new ArgumentException("Usage: TestHotkeys.exe [--native]");
                TestRules();
                TestBindings();
                TestCombinations();
                TestBoundaries();
                TestIndependentGroups();
                TestMatcher();
                TestLayoutsAndClock();
                Console.WriteLine("PASS: {0} deterministic checks; no native input or settings access.", _checks);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex.Message);
                return 1;
            }
        }

        private static void Check(bool condition, string name)
        {
            _checks++;
            if (!condition) throw new InvalidOperationException(name);
        }

        private static void Actions(List<int> actual, params int[] expected)
        {
            Check(actual.Count == expected.Length, "Action count: expected " + string.Join(",", expected) +
                "; got " + string.Join(",", actual));
            for (int i = 0; i < expected.Length; i++) Check(actual[i] == expected[i], "Action order/value");
        }

        private static void Throws(Action action, string name)
        {
            bool thrown = false;
            try { action(); }
            catch (ArgumentException) { thrown = true; }
            Check(thrown, name);
        }

        private static HotkeyGestureEngine Engine(int mask)
        {
            HotkeyGestureEngine engine = new HotkeyGestureEngine(400, 200);
            List<HotkeyBinding> bindings = new List<HotkeyBinding>();
            for (int i = 0; i < 3; i++)
                if ((mask & (1 << i)) != 0) bindings.Add(new HotkeyBinding(i + 1, Key, (HotkeyTrigger)i));
            engine.SetBindings(bindings);
            return engine;
        }

        private static void TestRules()
        {
            Loc.SetLanguage(Loc.SourceCode);
            Check(!HotkeyRules.IsAllowed(0), "Empty binding");
            Check(!HotkeyRules.IsAllowed((int)Keys.A), "Bare letter");
            Check(!HotkeyRules.IsAllowed((int)(Keys.Shift | Keys.A)), "Shift-only letter");
            Check(HotkeyRules.IsAllowed((int)(Keys.Control | Keys.A)), "Ctrl letter");
            Check(HotkeyRules.IsAllowed((int)(Keys.Alt | Keys.A)), "Alt letter");
            Check(HotkeyRules.IsAllowed((int)(Keys.Control | Keys.Alt | Keys.Shift | Keys.A)), "All modifiers");
            for (int key = (int)Keys.F1; key <= (int)Keys.F24; key++)
            {
                Check(HotkeyRules.IsAllowed(key) == (key != (int)Keys.F12), "Function key validation");
                Check(HotkeyRules.IsAllowed(key | (int)Keys.Control) == (key != (int)Keys.F12), "Ctrl F12 exclusion");
            }
            int[] invalid = { (int)Keys.None, (int)Keys.ShiftKey, (int)Keys.ControlKey, (int)Keys.Menu,
                (int)Keys.LShiftKey, (int)Keys.RShiftKey, (int)Keys.LControlKey, (int)Keys.RControlKey,
                (int)Keys.LMenu, (int)Keys.RMenu, (int)Keys.LWin, (int)Keys.RWin, (int)Keys.LButton, 255, 512 };
            foreach (int key in invalid) Check(!HotkeyRules.IsAllowed(key | (int)Keys.Control), "Non-key/modifier rejection");
            Check(!HotkeyRules.IsAllowed(Key | 0x08000000), "Unknown modifier bits");
            Check(HotkeyRules.Format(Key | (int)Keys.Control, HotkeyTrigger.Single) == "Ctrl+F8", "Single format");
            Check(HotkeyRules.Format(Key, HotkeyTrigger.DoublePress) == "双击 F8", "Double format");
            Check(HotkeyRules.Format(Key, HotkeyTrigger.LongPress) == "长按 F8", "Long format");
            Check(HotkeyRules.Format(0, HotkeyTrigger.LongPress) == "", "Empty format");
            Check(HotkeyRules.Format(Key | (int)(Keys.Control | Keys.Alt | Keys.Shift), HotkeyTrigger.Single) ==
                "Ctrl+Alt+Shift+F8", "Modifier order");

            AppSettings settings = new AppSettings();
            Profile profile = new Profile();
            profile.Name = "Test profile";
            profile.HotKey = Key;
            profile.HotKeyTrigger = HotkeyTrigger.DoublePress;
            settings.Profiles.Add(null);
            settings.Profiles.Add(profile);
            settings.CycleHotKey = Key;
            settings.CycleHotKeyTrigger = HotkeyTrigger.LongPress;
            Check(HotkeyRules.FindConflict(settings, null, false, Key, HotkeyTrigger.DoublePress) == profile.Name, "Profile conflict");
            Check(HotkeyRules.FindConflict(settings, profile, false, Key, HotkeyTrigger.DoublePress) == null, "Ignore profile");
            Check(HotkeyRules.FindConflict(settings, null, false, Key, HotkeyTrigger.Single) == null, "Different gesture");
            Check(HotkeyRules.FindConflict(settings, null, false, Key | (int)Keys.Control, HotkeyTrigger.DoublePress) == null, "Different chord");
            Check(HotkeyRules.FindConflict(settings, null, false, Key, HotkeyTrigger.LongPress) == Loc.T("循环切换预设"), "Cycle conflict");
            Check(HotkeyRules.FindConflict(settings, null, true, Key, HotkeyTrigger.LongPress) == null, "Ignore cycle");
            Check(HotkeyRules.FindConflict(settings, null, false, 0, HotkeyTrigger.Single) == null, "Zero conflict");
            settings.Profiles = null;
            Check(HotkeyRules.FindConflict(settings, null, false, Key, HotkeyTrigger.LongPress) != null, "Null profiles");
            Check(HotkeyRules.FindConflict(null, null, false, Key, HotkeyTrigger.Single) == null, "Null settings");
        }

        private static void TestBindings()
        {
            Throws(delegate { new HotkeyGestureEngine(0, 200); }, "Invalid long timing");
            Throws(delegate { new HotkeyGestureEngine(400, 0); }, "Invalid double timing");
            HotkeyGestureEngine engine = Engine(1);
            Throws(delegate
            {
                engine.SetBindings(new HotkeyBinding[] { new HotkeyBinding(7, Key, HotkeyTrigger.Single),
                    new HotkeyBinding(8, Key, HotkeyTrigger.Single) });
            }, "Duplicate rejection");
            Actions(engine.Handle(Key, true, 0), 1);
            Actions(engine.Handle(Key, false, 1));
            Throws(delegate { engine.SetBindings(new HotkeyBinding[] { null }); }, "Null binding rejection");
            Throws(delegate { engine.SetBindings(new HotkeyBinding[] { new HotkeyBinding(0, Key, (HotkeyTrigger)3) }); }, "Invalid trigger");
            Throws(delegate { engine.SetBindings(new HotkeyBinding[] { new HotkeyBinding(0, (int)Keys.F12, HotkeyTrigger.Single) }); }, "Invalid chord");
            HotkeyBinding mutable = new HotkeyBinding(0, Key, HotkeyTrigger.Single);
            engine.SetBindings(new HotkeyBinding[] { mutable });
            mutable.Action = 99;
            mutable.KeyData = (int)Keys.F9;
            Actions(engine.Handle(Key, true, 10), 0);
            engine.SetBindings(null);
            Check(engine.NextDeadline == -1, "Empty bindings reset");
            Actions(engine.Handle(Key, false, 11));
            Actions(engine.Handle(Key, true, 12));
            engine.SetBindings(new HotkeyBinding[] { new HotkeyBinding(1, 0, HotkeyTrigger.Single) });
            Actions(engine.Handle(0, true, 13));
        }

        private static void TestCombinations()
        {
            for (int mask = 1; mask <= 7; mask++)
            {
                bool single = (mask & 1) != 0;
                bool doublePress = (mask & 2) != 0;
                bool longPress = (mask & 4) != 0;
                HotkeyGestureEngine engine = Engine(mask);
                Actions(engine.Handle(Key, false, 0));
                Actions(engine.Handle(Key, true, 10), mask == 1 ? new int[] { 1 } : new int[0]);
                Check(engine.NextDeadline == (longPress ? 410 : -1), "Initial deadline");
                Actions(engine.Handle(Key, true, 20));
                Actions(engine.Handle(Key, false, 40), single && longPress && !doublePress ? new int[] { 1 } : new int[0]);
                Check(engine.NextDeadline == (doublePress ? 240 : -1), "Tap deadline");
                Actions(engine.FireDue(239));
                Actions(engine.FireDue(240), doublePress && single ? new int[] { 1 } : new int[0]);
                Check(engine.NextDeadline == -1, "Tap expiry cleanup");
                Actions(engine.Handle(Key, false, 250));
                Actions(engine.FireDue(1000));

                engine.Reset();
                Actions(engine.Handle(Key, true, 0), mask == 1 ? new int[] { 1 } : new int[0]);
                Actions(engine.Handle(Key, false, 20), single && longPress && !doublePress ? new int[] { 1 } : new int[0]);
                Actions(engine.Handle(Key, true, 100), mask == 1 ? new int[] { 1 } : new int[0]);
                Actions(engine.Handle(Key, true, 110));
                Actions(engine.Handle(Key, false, 150), doublePress ? new int[] { 2 } :
                    (single && longPress ? new int[] { 1 } : new int[0]));
                Actions(engine.FireDue(1000));
                Check(engine.NextDeadline == -1, "Double cleanup");

                engine.Reset();
                Actions(engine.Handle(Key, true, 0), mask == 1 ? new int[] { 1 } : new int[0]);
                Actions(engine.FireDue(399));
                Actions(engine.FireDue(400), longPress ? new int[] { 3 } : new int[0]);
                Actions(engine.Handle(Key, true, 450));
                Actions(engine.FireDue(900));
                Actions(engine.Handle(Key, false, 950));
                Actions(engine.FireDue(1150), !longPress && doublePress && single ? new int[] { 1 } : new int[0]);
                Check(engine.NextDeadline == -1, "Held key cleanup");

                engine.Reset();
                Actions(engine.Handle(Key, true, 0), mask == 1 ? new int[] { 1 } : new int[0]);
                Actions(engine.Handle(Key, false, 20), single && longPress && !doublePress ? new int[] { 1 } : new int[0]);
                Actions(engine.Handle(Key, true, 100), mask == 1 ? new int[] { 1 } : new int[0]);
                Actions(engine.FireDue(499));
                Actions(engine.FireDue(500), longPress ? new int[] { 3 } : new int[0]);
                Actions(engine.Handle(Key, false, 600), !longPress && doublePress ? new int[] { 2 } : new int[0]);
                Actions(engine.FireDue(1000));
                Actions(engine.Handle(Key, true, 1100), mask == 1 ? new int[] { 1 } : new int[0]);
                Actions(engine.Handle(Key, false, 1120), single && longPress && !doublePress ? new int[] { 1 } : new int[0]);
                Actions(engine.FireDue(1320), single && doublePress ? new int[] { 1 } : new int[0]);
                Check(engine.NextDeadline == -1, "Second-hold reset");

                engine.Handle(Key, true, 1400);
                engine.Reset();
                Check(engine.NextDeadline == -1, "Lifecycle held reset");
                Actions(engine.Handle(Key, false, 1800));
                Actions(engine.FireDue(3000));
                engine.Handle(Key, true, 3010);
                engine.Handle(Key, false, 3020);
                engine.SetBindings(new HotkeyBinding[] { new HotkeyBinding(9, Key, HotkeyTrigger.Single) });
                Actions(engine.FireDue(4000));
                Actions(engine.Handle(Key, true, 4010), 9);
            }
        }

        private static void TestBoundaries()
        {
            for (int delta = -1; delta <= 1; delta++)
            {
                HotkeyGestureEngine engine = Engine(5);
                Actions(engine.Handle(Key, true, 0));
                Actions(engine.Handle(Key, false, 400 + delta), delta < 0 ? 1 : 3);
                Actions(engine.FireDue(1000));
                Check(engine.NextDeadline == -1, "Long boundary cleanup");

                engine = Engine(3);
                Actions(engine.Handle(Key, true, 0));
                Actions(engine.Handle(Key, false, 10));
                Actions(engine.Handle(Key, true, 210 + delta), delta < 0 ? new int[0] : new int[] { 1 });
                Actions(engine.Handle(Key, false, 230), delta < 0 ? new int[] { 2 } : new int[0]);
                Actions(engine.FireDue(430), delta < 0 ? new int[0] : new int[] { 1 });
                Check(engine.NextDeadline == -1, "Double boundary cleanup");
            }
            HotkeyGestureEngine both = Engine(7);
            both.Handle(Key, true, 0);
            both.Handle(Key, false, 10);
            Actions(both.Handle(Key, true, 1000), 1);
            Check(both.NextDeadline == 1400, "Late second is new long press");
            Actions(both.Handle(Key, false, 1400), 3);
            Actions(both.FireDue(5000));
            both.Reset();
            both.Handle(Key, true, 0);
            both.Handle(Key, false, 10);
            both.Handle(Key, true, 209);
            Actions(both.Handle(Key, false, 609), 3);
            Actions(both.FireDue(1000));

            HotkeyGestureEngine repeated = Engine(4);
            repeated.Handle(Key, true, 0);
            Actions(repeated.Handle(Key, true, 400), 3);
            Actions(repeated.Handle(Key, true, 800));
            Actions(repeated.Handle(Key, false, 900));
        }

        private static void TestIndependentGroups()
        {
            HotkeyGestureEngine engine = new HotkeyGestureEngine(400, 200);
            int ctrl = Key | (int)Keys.Control;
            int other = (int)Keys.F9;
            engine.SetBindings(new HotkeyBinding[] { new HotkeyBinding(10, Key, HotkeyTrigger.Single),
                new HotkeyBinding(11, Key, HotkeyTrigger.DoublePress), new HotkeyBinding(20, ctrl, HotkeyTrigger.LongPress),
                new HotkeyBinding(30, other, HotkeyTrigger.Single), new HotkeyBinding(31, other, HotkeyTrigger.DoublePress) });
            Actions(engine.Handle(Key, true, 0));
            Actions(engine.Handle(Key, false, 10));
            Actions(engine.Handle(ctrl, true, 20));
            Actions(engine.Handle(ctrl, true, 30));
            Actions(engine.Handle(other, true, 40));
            Actions(engine.Handle(other, false, 50));
            Check(engine.NextDeadline == 210, "Earliest independent deadline");
            Actions(engine.FireDue(500), 10, 30, 20);
            Actions(engine.Handle(Key, false, 510));
            Actions(engine.Handle(Key, true, 520));
            Actions(engine.Handle(Key, false, 530));
            Actions(engine.FireDue(730), 10);

            engine.SetBindings(new HotkeyBinding[] { new HotkeyBinding(1, ctrl, HotkeyTrigger.Single),
                new HotkeyBinding(3, ctrl, HotkeyTrigger.LongPress) });
            Actions(engine.Handle(ctrl, true, 1000));
            Actions(engine.Handle(Key, true, 1010));
            Actions(engine.Handle(Key, false, 1020), 1);
            Actions(engine.Handle(ctrl, false, 1030));
            Actions(engine.FireDue(2000));

            engine = Engine(3);
            engine.Handle(Key, true, 0);
            engine.Handle(Key, false, 10);
            Actions(engine.Handle((int)Keys.F10, true, 210), 1);
        }

        private static void Match(GlobalHotkeyListener.ChordMatcher matcher, Keys key, bool pressed, int expected)
        {
            int actual;
            bool matched = matcher.Handle((int)key, pressed, out actual);
            Check(matched == (expected != 0) && actual == expected, "Chord matcher: " + key + " pressed=" + pressed);
        }

        private static void TestMatcher()
        {
            for (int bindingModifiers = 0; bindingModifiers < 8; bindingModifiers++)
            {
                int binding = Key;
                if ((bindingModifiers & 1) != 0) binding |= (int)Keys.Control;
                if ((bindingModifiers & 2) != 0) binding |= (int)Keys.Alt;
                if ((bindingModifiers & 4) != 0) binding |= (int)Keys.Shift;
                for (int state = 0; state < 16; state++)
                {
                    GlobalHotkeyListener.ChordMatcher matcher = new GlobalHotkeyListener.ChordMatcher(new int[] { binding });
                    if ((state & 1) != 0) Match(matcher, Keys.LControlKey, true, 0);
                    if ((state & 2) != 0) Match(matcher, Keys.RMenu, true, 0);
                    if ((state & 4) != 0) Match(matcher, Keys.RShiftKey, true, 0);
                    if ((state & 8) != 0) Match(matcher, Keys.LWin, true, 0);
                    int expected = state == bindingModifiers ? binding : 0;
                    Match(matcher, Keys.F8, true, expected);
                    Match(matcher, Keys.F8, true, 0);
                    Match(matcher, Keys.LControlKey, false, 0);
                    Match(matcher, Keys.RMenu, false, 0);
                    Match(matcher, Keys.RShiftKey, false, 0);
                    Match(matcher, Keys.LWin, false, 0);
                    Match(matcher, Keys.F8, true, 0);
                    Match(matcher, Keys.F8, false, expected);
                    Match(matcher, Keys.F8, false, 0);
                    Match(matcher, Keys.F9, true, 0);
                    Match(matcher, Keys.F9, false, 0);
                }
            }
            int ctrl = Key | (int)Keys.Control;
            GlobalHotkeyListener.ChordMatcher held = new GlobalHotkeyListener.ChordMatcher(new int[] { Key, ctrl });
            held.Synchronize(delegate(int key) { return key == Key || key == (int)Keys.RControlKey; });
            Match(held, Keys.F8, true, 0);
            Match(held, Keys.F8, false, 0);
            Match(held, Keys.F8, true, ctrl);
            Match(held, Keys.LControlKey, true, 0);
            Match(held, Keys.RControlKey, false, 0);
            Match(held, Keys.F8, false, ctrl);
            Match(held, Keys.F8, true, ctrl);
            Match(held, Keys.LControlKey, false, 0);
            Match(held, Keys.F8, false, ctrl);
            Match(held, Keys.F8, true, Key);
            Match(held, Keys.RWin, true, 0);
            Match(held, Keys.F8, false, Key);
            Match(held, Keys.F8, true, 0);
            Match(held, Keys.RWin, false, 0);
            Match(held, Keys.F8, false, 0);
            held.Synchronize(delegate(int key) { return false; });
            Match(held, Keys.F8, false, 0);
            Match(held, Keys.F8, true, Key);
            held.Synchronize(delegate(int key) { return key == Key; });
            Match(held, Keys.F8, false, 0);
            Match(held, Keys.F8, true, Key);
            Throws(delegate { new GlobalHotkeyListener.ChordMatcher(new int[] { (int)Keys.F12 }); }, "Matcher rejects invalid chord");
            using (GlobalHotkeyListener listener = new GlobalHotkeyListener())
            {
                listener.Stop();
                listener.Stop();
                listener.Dispose();
                listener.Dispose();
                Check(!listener.Start(new int[] { Key }, delegate(int k, bool p, long n) { }), "Disposed listener cannot restart");
            }
        }

        private static void TestLayoutsAndClock()
        {
            Check(Marshal.SizeOf(typeof(NativeMethods.KBDLLHOOKSTRUCT)) == (IntPtr.Size == 8 ? 24 : 20), "Hook structure size");
            Check(Marshal.OffsetOf(typeof(NativeMethods.KBDLLHOOKSTRUCT), "dwExtraInfo").ToInt32() == 16, "Hook pointer offset");
            Check(Marshal.SizeOf(typeof(NativeMethods.INPUT)) == (IntPtr.Size == 8 ? 40 : 28), "SendInput structure size");
            Check(Marshal.OffsetOf(typeof(NativeMethods.INPUT), "data").ToInt32() == (IntPtr.Size == 8 ? 8 : 4), "Input union offset");
            Check(Marshal.SizeOf(typeof(NativeMethods.MSG)) == (IntPtr.Size == 8 ? 48 : 32), "Message structure size");
            long last = HotkeyClock.Now;
            for (int i = 0; i < 100; i++)
            {
                long next = HotkeyClock.Now;
                Check(next >= last, "Monotonic clock");
                last = next;
            }
        }

        private sealed class NativeTestForm : Form
        {
            private const int HotkeyId = 0x4711;
            private readonly GlobalHotkeyListener _listener = new GlobalHotkeyListener();
            private readonly HotkeyGestureEngine _engine = new HotkeyGestureEngine(400, 120);
            private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
            private readonly HashSet<Keys> _injectedHeld = new HashSet<Keys>();
            private readonly int[] _actionCounts = new int[4];
            private readonly List<Action> _steps = new List<Action>();
            private readonly List<int> _times = new List<int>();
            private bool _registered;
            private long _started;
            private int _step;
            private int _generation;
            private int _downs;
            private int _ups;
            private int _ordinaryDowns;
            private int _ordinaryUps;
            private int _hotkeyMessages;
            public int Result = 1;

            public NativeTestForm()
            {
                Text = "GammaLift native hotkey test (automatic, under 6 s)";
                ClientSize = new Size(540, 100);
                StartPosition = FormStartPosition.CenterScreen;
                Label label = new Label();
                label.Dock = DockStyle.Fill;
                label.TextAlign = ContentAlignment.MiddleCenter;
                label.Text = "Keep this test window focused; do not press keys.\r\nOnly F8/F9/Ctrl are injected. Results go to the console.";
                Controls.Add(label);
                _engine.SetBindings(new HotkeyBinding[] { new HotkeyBinding(1, Key, HotkeyTrigger.Single),
                    new HotkeyBinding(2, Key, HotkeyTrigger.DoublePress), new HotkeyBinding(3, Key, HotkeyTrigger.LongPress) });
                _timer.Interval = 10;
                _timer.Tick += Tick;
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                try
                {
                    Keys[] mustBeUp = { Keys.F8, Keys.F9, Keys.LControlKey, Keys.RControlKey,
                        Keys.LShiftKey, Keys.RShiftKey, Keys.LMenu, Keys.RMenu, Keys.LWin, Keys.RWin };
                    foreach (Keys key in mustBeUp)
                        if ((NativeMethods.GetAsyncKeyState((int)key) & 0x8000) != 0)
                            throw new InvalidOperationException("Release F8/F9 and all modifiers before --native.");
                    _registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, NativeMethods.MOD_NOREPEAT, (uint)Keys.F8);
                    if (!_registered) throw new InvalidOperationException("Cannot reserve F8; another application may own it.");
                    bool duplicateSucceeded = false;
                    Thread competing = new Thread(delegate()
                    {
                        duplicateSucceeded = NativeMethods.RegisterHotKey(IntPtr.Zero, HotkeyId + 1,
                            NativeMethods.MOD_NOREPEAT, (uint)Keys.F8);
                        if (duplicateSucceeded) NativeMethods.UnregisterHotKey(IntPtr.Zero, HotkeyId + 1);
                    });
                    competing.Start();
                    competing.Join();
                    if (duplicateSucceeded) throw new InvalidOperationException("F8 was not exclusively reserved.");
                    StartListener();
                    Activate();
                    NativeMethods.SetForegroundWindow(Handle);
                    AddStep(250, delegate { SendKey(Keys.F9, true); });
                    AddStep(280, delegate { SendKey(Keys.F9, true); });
                    AddStep(310, delegate { SendKey(Keys.F9, false); Tap(true); });
                    AddStep(650, delegate { Tap(false); });
                    AddStep(710, delegate { Tap(false); });
                    AddStep(1000, delegate { SendKey(Keys.F8, true); });
                    AddStep(1100, delegate { SendKey(Keys.F8, true); });
                    AddStep(1500, delegate { SendKey(Keys.F8, false); });
                    AddStep(1700, delegate
                    {
                        SendKey(Keys.LControlKey, true);
                        SendKey(Keys.F8, true);
                        SendKey(Keys.LControlKey, false);
                        SendKey(Keys.F8, false);
                    });
                    AddStep(1900, delegate { SendKey(Keys.F8, true); });
                    AddStep(1950, delegate { _listener.Stop(); _listener.Stop(); _engine.Reset(); StartListener(); });
                    AddStep(2000, delegate { SendKey(Keys.F8, true); SendKey(Keys.F8, false); });
                    AddStep(2200, delegate { Tap(false); });
                    AddStep(2550, VerifyNative);
                    _started = HotkeyClock.Now;
                    _timer.Start();
                    Console.WriteLine("NATIVE: F8 reserved; passive observer started. Do not change focus or press keys.");
                }
                catch (Exception ex) { Fail(ex); }
            }

            private void AddStep(int ms, Action action)
            {
                _times.Add(ms);
                _steps.Add(action);
            }

            private void StartListener()
            {
                int generation = ++_generation;
                if (!_listener.Start(new int[] { Key }, delegate(int key, bool pressed, long now)
                {
                    BeginInvoke((Action)delegate
                    {
                        if (generation != _generation || IsDisposed) return;
                        if (pressed) _downs++; else _ups++;
                        Console.WriteLine("observer F8 {0} @ {1} ms", pressed ? "down" : "up", now);
                        Record(_engine.Handle(key, pressed, now));
                    });
                })) throw new InvalidOperationException("WH_KEYBOARD_LL startup failed.");
            }

            private void Tick(object sender, EventArgs e)
            {
                try
                {
                    long elapsed = HotkeyClock.Now - _started;
                    if (elapsed >= 5000) throw new InvalidOperationException("Native test exceeded its deadline.");
                    if (NativeMethods.GetForegroundWindow() != Handle)
                        throw new InvalidOperationException("Foreground focus unavailable/lost; native results are not valid.");
                    Record(_engine.FireDue(HotkeyClock.Now));
                    while (_step < _steps.Count && elapsed >= _times[_step])
                    {
                        Action action = _steps[_step++];
                        action();
                        if (IsDisposed) return;
                    }
                }
                catch (Exception ex) { Fail(ex); }
            }

            private void Record(List<int> actions)
            {
                foreach (int action in actions)
                {
                    _actionCounts[action]++;
                    Console.WriteLine("action {0}; count={1}", (HotkeyTrigger)(action - 1), _actionCounts[action]);
                }
            }

            private void Tap(bool repeat)
            {
                SendKey(Keys.F8, true);
                if (repeat) SendKey(Keys.F8, true);
                SendKey(Keys.F8, false);
            }

            private void SendKey(Keys key, bool pressed)
            {
                NativeMethods.INPUT input = new NativeMethods.INPUT();
                input.type = NativeMethods.INPUT_KEYBOARD;
                input.data.ki.wVk = (ushort)key;
                input.data.ki.dwFlags = pressed ? 0 : NativeMethods.KEYEVENTF_KEYUP;
                if (NativeMethods.SendInput(1, new NativeMethods.INPUT[] { input }, Marshal.SizeOf(typeof(NativeMethods.INPUT))) != 1)
                    throw new InvalidOperationException("SendInput failed (possibly UIPI/session restrictions).");
                if (pressed) _injectedHeld.Add(key); else _injectedHeld.Remove(key);
            }

            private void VerifyNative()
            {
                Console.WriteLine("NATIVE counts: single={0}, double={1}, long={2}, observer down/up={3}/{4}, ordinary F9 down/up={5}/{6}, WM_HOTKEY={7}",
                    _actionCounts[1], _actionCounts[2], _actionCounts[3], _downs, _ups, _ordinaryDowns, _ordinaryUps, _hotkeyMessages);
                if (_actionCounts[1] != 2 || _actionCounts[2] != 1 || _actionCounts[3] != 1 ||
                    _downs != 6 || _ups != 5 || _ordinaryDowns < 2 || _ordinaryUps != 1 || _hotkeyMessages == 0)
                    throw new InvalidOperationException("Native action/repeat/release/pass-through/lifecycle assertions failed.");
                Result = 0;
                Console.WriteLine("PASS: native registration/observer test; no display or settings access.");
                Close();
            }

            private void Fail(Exception ex)
            {
                Result = 1;
                Console.Error.WriteLine("NATIVE FAIL: " + ex.Message);
                Close();
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == NativeMethods.WM_HOTKEY)
                {
                    _hotkeyMessages++;
                    Console.WriteLine("WM_HOTKEY observed (no business dispatch)");
                }
                if (m.Msg == NativeMethods.WM_KEYDOWN || m.Msg == NativeMethods.WM_KEYUP ||
                    m.Msg == NativeMethods.WM_SYSKEYDOWN || m.Msg == NativeMethods.WM_SYSKEYUP)
                {
                    int key = unchecked((int)m.WParam.ToInt64());
                    bool down = m.Msg == NativeMethods.WM_KEYDOWN || m.Msg == NativeMethods.WM_SYSKEYDOWN;
                    if (key == (int)Keys.F8 || key == (int)Keys.F9)
                    {
                        Console.WriteLine("ordinary {0} {1}", (Keys)key, down ? "down" : "up");
                        if (key == (int)Keys.F9)
                        {
                            if (down) _ordinaryDowns++; else _ordinaryUps++;
                        }
                    }
                }
                base.WndProc(ref m);
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                _timer.Stop();
                _generation++;
                _listener.Dispose();
                foreach (Keys key in new List<Keys>(_injectedHeld))
                {
                    try { SendKey(key, false); }
                    catch (Exception ex) { Result = 1; Console.Error.WriteLine("Input cleanup failed: " + ex.Message); }
                }
                if (_registered)
                {
                    NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                    _registered = false;
                }
                _timer.Dispose();
                base.OnFormClosed(e);
            }
        }
    }
}
