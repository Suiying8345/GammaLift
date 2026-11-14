using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace GammaLift
{
    internal static class HotkeyClock
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        public static long Now
        {
            get { return Clock.ElapsedMilliseconds; }
        }
    }

    internal sealed class HotkeyBinding
    {
        public int Action;
        public int KeyData;
        public HotkeyTrigger Trigger;

        public HotkeyBinding(int action, int keyData, HotkeyTrigger trigger)
        {
            Action = action;
            KeyData = keyData;
            Trigger = trigger;
        }
    }

    internal sealed class HotkeyGestureEngine
    {
        private sealed class Group
        {
            public int KeyData;
            public readonly int?[] Actions = new int?[3];
            public bool SecondPress;
            public bool LongFired;
            public long LongDeadline = -1;
            public long TapDeadline = -1;

            public void Reset()
            {
                SecondPress = false;
                LongFired = false;
                LongDeadline = -1;
                TapDeadline = -1;
            }
        }

        private readonly int _longMs;
        private readonly int _doubleMs;
        private Dictionary<int, Group> _byChord = new Dictionary<int, Group>();
        private List<Group> _groups = new List<Group>();
        private readonly HashSet<int> _downKeys = new HashSet<int>();
        private readonly Dictionary<int, Group> _activeKeys = new Dictionary<int, Group>();

        public HotkeyGestureEngine() : this(400, (int)NativeMethods.GetDoubleClickTime())
        {
        }

        public HotkeyGestureEngine(int longMs, int doubleMs)
        {
            if (longMs <= 0) throw new ArgumentOutOfRangeException("longMs");
            if (doubleMs <= 0) throw new ArgumentOutOfRangeException("doubleMs");
            _longMs = longMs;
            _doubleMs = doubleMs;
        }

        public void SetBindings(IEnumerable<HotkeyBinding> bindings)
        {
            Dictionary<int, Group> byChord = new Dictionary<int, Group>();
            List<Group> groups = new List<Group>();
            if (bindings != null)
            {
                foreach (HotkeyBinding binding in bindings)
                {
                    if (binding == null) throw new ArgumentException("Null hotkey binding.", "bindings");
                    if (binding.KeyData == 0) continue;
                    int trigger = (int)binding.Trigger;
                    if (!HotkeyRules.IsAllowed(binding.KeyData) || trigger < 0 || trigger > 2)
                        throw new ArgumentException("Invalid hotkey binding.", "bindings");
                    Group group;
                    if (!byChord.TryGetValue(binding.KeyData, out group))
                    {
                        group = new Group();
                        group.KeyData = binding.KeyData;
                        byChord.Add(group.KeyData, group);
                        groups.Add(group);
                    }
                    if (group.Actions[trigger].HasValue)
                        throw new ArgumentException("Duplicate hotkey gesture.", "bindings");
                    group.Actions[trigger] = binding.Action;
                }
            }
            // 整批验证成功后才替换，避免失败时破坏正在使用的绑定。
            _byChord = byChord;
            _groups = groups;
            Reset();
        }

        public List<int> Handle(int keyData, bool pressed, long now)
        {
            // 先结算到期事件，防止延迟的第二次按键吞掉已经到期的单击。
            List<int> fired = FireDue(now);
            int key = keyData & (int)Keys.KeyCode;
            Group group;
            if (pressed)
            {
                if (!_downKeys.Add(key)) return fired;
                if (!_byChord.TryGetValue(keyData, out group)) return fired;
                _activeKeys[key] = group;
                group.LongFired = false;
                group.SecondPress = group.TapDeadline >= 0;
                group.TapDeadline = -1;
                if (group.Actions[(int)HotkeyTrigger.LongPress].HasValue)
                    group.LongDeadline = now + _longMs;
                if (!group.Actions[1].HasValue && !group.Actions[2].HasValue)
                    AddAction(fired, group.Actions[0]);
                return fired;
            }

            _downKeys.Remove(key);
            // 抬键按最初的主键配对，修饰键先松开也不会丢失这次手势。
            if (!_activeKeys.TryGetValue(key, out group)) return fired;
            _activeKeys.Remove(key);
            group.LongDeadline = -1;
            if (group.LongFired)
            {
                group.Reset();
            }
            else if (group.SecondPress)
            {
                AddAction(fired, group.Actions[1]);
                group.Reset();
            }
            else if (group.Actions[1].HasValue)
            {
                group.TapDeadline = now + _doubleMs;
            }
            else if (group.Actions[2].HasValue)
            {
                AddAction(fired, group.Actions[0]);
                group.Reset();
            }
            return fired;
        }

        public List<int> FireDue(long now)
        {
            List<int> fired = new List<int>();
            while (true)
            {
                Group next = null;
                long deadline = long.MaxValue;
                bool isLong = false;
                foreach (Group group in _groups)
                {
                    if (group.LongDeadline >= 0 && group.LongDeadline <= now && group.LongDeadline < deadline)
                    {
                        next = group;
                        deadline = group.LongDeadline;
                        isLong = true;
                    }
                    if (group.TapDeadline >= 0 && group.TapDeadline <= now && group.TapDeadline < deadline)
                    {
                        next = group;
                        deadline = group.TapDeadline;
                        isLong = false;
                    }
                }
                if (next == null) return fired;
                if (isLong)
                {
                    next.LongDeadline = -1;
                    next.LongFired = true;
                    next.SecondPress = false;
                    next.TapDeadline = -1;
                    AddAction(fired, next.Actions[2]);
                }
                else
                {
                    next.TapDeadline = -1;
                    next.SecondPress = false;
                    AddAction(fired, next.Actions[0]);
                }
            }
        }

        public long NextDeadline
        {
            get
            {
                long next = -1;
                foreach (Group group in _groups)
                {
                    if (group.LongDeadline >= 0 && (next < 0 || group.LongDeadline < next))
                        next = group.LongDeadline;
                    if (group.TapDeadline >= 0 && (next < 0 || group.TapDeadline < next))
                        next = group.TapDeadline;
                }
                return next;
            }
        }

        public void Reset()
        {
            _downKeys.Clear();
            _activeKeys.Clear();
            foreach (Group group in _groups) group.Reset();
        }

        private static void AddAction(List<int> fired, int? action)
        {
            if (action.HasValue) fired.Add(action.Value);
        }
    }

    internal static class HotkeyRules
    {
        public static bool IsAllowed(int keyData)
        {
            int modifiers = (int)(Keys.Control | Keys.Alt | Keys.Shift);
            if ((keyData & ~((int)Keys.KeyCode | modifiers)) != 0) return false;
            int key = keyData & (int)Keys.KeyCode;
            if (key < (int)Keys.Back || key > 254 || key == (int)Keys.F12 || IsModifier(key)) return false;
            return (keyData & (int)(Keys.Control | Keys.Alt)) != 0 ||
                (key >= (int)Keys.F1 && key <= (int)Keys.F24);
        }

        internal static bool IsModifier(int key)
        {
            return key == (int)Keys.ShiftKey || key == (int)Keys.ControlKey || key == (int)Keys.Menu ||
                key == (int)Keys.LShiftKey || key == (int)Keys.RShiftKey ||
                key == (int)Keys.LControlKey || key == (int)Keys.RControlKey ||
                key == (int)Keys.LMenu || key == (int)Keys.RMenu ||
                key == (int)Keys.LWin || key == (int)Keys.RWin;
        }

        public static string Format(int keyData, HotkeyTrigger trigger)
        {
            if (keyData == 0) return "";
            string chord = "";
            if ((keyData & (int)Keys.Control) != 0) chord += "Ctrl+";
            if ((keyData & (int)Keys.Alt) != 0) chord += "Alt+";
            if ((keyData & (int)Keys.Shift) != 0) chord += "Shift+";
            chord += ((Keys)(keyData & (int)Keys.KeyCode)).ToString();
            if (trigger == HotkeyTrigger.DoublePress) return Loc.T("双击") + " " + chord;
            if (trigger == HotkeyTrigger.LongPress) return Loc.T("长按") + " " + chord;
            return chord;
        }

        public static string FindConflict(AppSettings settings, Profile ignoredProfile, bool ignoreCycle,
            int keyData, HotkeyTrigger trigger)
        {
            if (settings == null || keyData == 0) return null;
            if (settings.Profiles != null)
            {
                foreach (Profile profile in settings.Profiles)
                    if (profile != null && !object.ReferenceEquals(profile, ignoredProfile) &&
                        profile.HotKey == keyData && profile.HotKeyTrigger == trigger)
                        return profile.Name;
            }
            if (!ignoreCycle && settings.CycleHotKey == keyData && settings.CycleHotKeyTrigger == trigger)
                return Loc.T("循环切换预设");
            return null;
        }
    }

    internal sealed class GlobalHotkeyListener : IDisposable
    {
        internal sealed class ChordMatcher
        {
            private static readonly int[] Modifiers =
            {
                (int)Keys.LShiftKey, (int)Keys.RShiftKey, (int)Keys.LControlKey,
                (int)Keys.RControlKey, (int)Keys.LMenu, (int)Keys.RMenu,
                (int)Keys.LWin, (int)Keys.RWin
            };
            private readonly HashSet<int> _chords = new HashSet<int>();
            private readonly HashSet<int> _watched = new HashSet<int>();
            private readonly bool[] _down = new bool[256];
            private readonly int[] _active = new int[256];

            internal ChordMatcher(IEnumerable<int> chords)
            {
                if (chords == null) return;
                foreach (int chord in chords)
                {
                    if (!HotkeyRules.IsAllowed(chord)) throw new ArgumentException("Invalid hotkey chord.", "chords");
                    _chords.Add(chord);
                    _watched.Add(chord & (int)Keys.KeyCode);
                }
            }

            internal void Synchronize(Func<int, bool> isDown)
            {
                Array.Clear(_down, 0, _down.Length);
                Array.Clear(_active, 0, _active.Length);
                foreach (int key in Modifiers) _down[key] = isDown(key);
                // 启动时已经按住的主键只等待抬起, 自动重复不算新手势。
                foreach (int key in _watched) _down[key] = isDown(key);
            }

            internal bool Handle(int key, bool pressed, out int chord)
            {
                chord = 0;
                if (key < 0 || key >= _down.Length) return false;
                if (HotkeyRules.IsModifier(key))
                {
                    _down[key] = pressed;
                    return false;
                }
                if (!_watched.Contains(key)) return false;
                if (!pressed)
                {
                    _down[key] = false;
                    chord = _active[key];
                    _active[key] = 0;
                    return chord != 0;
                }
                if (_down[key]) return false;
                _down[key] = true;
                // Keys 无法编码 Win, 含 Win 的修饰状态单独拒绝。
                if (_down[(int)Keys.LWin] || _down[(int)Keys.RWin]) return false;
                int current = key;
                if (AnyDown(Keys.ControlKey, Keys.LControlKey, Keys.RControlKey)) current |= (int)Keys.Control;
                if (AnyDown(Keys.Menu, Keys.LMenu, Keys.RMenu)) current |= (int)Keys.Alt;
                if (AnyDown(Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey)) current |= (int)Keys.Shift;
                if (!_chords.Contains(current)) return false;
                chord = current;
                _active[key] = chord;
                return true;
            }

            private bool AnyDown(Keys generic, Keys left, Keys right)
            {
                return _down[(int)generic] || _down[(int)left] || _down[(int)right];
            }
        }

        private sealed class Session
        {
            public readonly ChordMatcher Matcher;
            public readonly Action<int, bool, long> Callback;
            public readonly ManualResetEvent Ready = new ManualResetEvent(false);
            public Thread Thread;
            public volatile uint ThreadId;
            public IntPtr Hook;
            public NativeMethods.LowLevelKeyboardProc HookProc;
            public volatile bool StopRequested;
            public volatile bool Installed;

            public Session(ChordMatcher matcher, Action<int, bool, long> callback)
            {
                Matcher = matcher;
                Callback = callback;
            }
        }

        private readonly object _gate = new object();
        private Session _session;
        private bool _disposed;

        public bool Start(IEnumerable<int> successfulRegisteredKeyData, Action<int, bool, long> callback)
        {
            Stop();
            if (callback == null) return false;
            Session session;
            try
            {
                session = new Session(new ChordMatcher(successfulRegisteredKeyData), callback);
            }
            catch (Exception)
            {
                return false;
            }
            lock (_gate)
            {
                if (_disposed || _session != null)
                {
                    session.Ready.Dispose();
                    return false;
                }
                _session = session;
                try
                {
                    session.Thread = new Thread(delegate() { Run(session); });
                    session.Thread.IsBackground = true;
                    session.Thread.Name = "GammaLift hotkey observer";
                    session.Thread.Start();
                }
                catch (Exception)
                {
                    _session = null;
                    session.Ready.Dispose();
                    return false;
                }
            }
            bool ready = session.Ready.WaitOne(5000);
            bool success = ready && session.Installed && !session.StopRequested;
            if (!success) StopSession(session);
            session.Ready.Dispose();
            return success;
        }

        public void Stop()
        {
            Session session;
            lock (_gate)
            {
                session = _session;
                if (session != null) session.StopRequested = true;
            }
            if (session != null) StopSession(session);
        }

        public void Dispose()
        {
            lock (_gate) _disposed = true;
            Stop();
        }

        private void StopSession(Session session)
        {
            session.StopRequested = true;
            uint id = session.ThreadId;
            if (id != 0) NativeMethods.PostThreadMessage(id, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            // 不持有生命周期锁等待线程，回调内停止监听也不会形成互锁。
            if (session.Thread != Thread.CurrentThread) session.Thread.Join();
            lock (_gate)
            {
                if (session.Thread != Thread.CurrentThread && object.ReferenceEquals(_session, session))
                    _session = null;
            }
        }

        private void Run(Session session)
        {
            try
            {
                try
                {
                    NativeMethods.MSG message;
                    NativeMethods.PeekMessage(out message, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);
                    session.ThreadId = NativeMethods.GetCurrentThreadId();
                    if (!session.StopRequested)
                    {
                        session.Matcher.Synchronize(delegate(int key)
                        {
                            return (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
                        });
                        session.HookProc = delegate(int code, IntPtr wParam, IntPtr lParam)
                        {
                            return Observe(session, code, wParam, lParam);
                        };
                        session.Hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL,
                            session.HookProc, NativeMethods.GetModuleHandle(null), 0);
                        session.Installed = session.Hook != IntPtr.Zero;
                    }
                }
                catch (Exception)
                {
                    session.Installed = false;
                }
                finally
                {
                    session.Ready.Set();
                }
                if (!session.Installed || session.StopRequested) return;
                NativeMethods.MSG msg;
                while (!session.StopRequested && NativeMethods.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    NativeMethods.TranslateMessage(ref msg);
                    NativeMethods.DispatchMessage(ref msg);
                }
            }
            finally
            {
                if (session.Hook != IntPtr.Zero)
                {
                    NativeMethods.UnhookWindowsHookEx(session.Hook);
                    session.Hook = IntPtr.Zero;
                }
                session.Installed = false;
                lock (_gate)
                {
                    if (object.ReferenceEquals(_session, session)) _session = null;
                }
            }
        }

        private static IntPtr Observe(Session session, int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code == NativeMethods.HC_ACTION && !session.StopRequested)
                {
                    long now = HotkeyClock.Now;
                    int message = unchecked((int)wParam.ToInt64());
                    bool pressed = message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN;
                    bool released = message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP;
                    if (pressed || released)
                    {
                        NativeMethods.KBDLLHOOKSTRUCT data = (NativeMethods.KBDLLHOOKSTRUCT)
                            System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                        int chord;
                        if (session.Matcher.Handle((int)data.vkCode, pressed, out chord))
                            session.Callback(chord, pressed, now);
                    }
                }
            }
            catch (Exception)
            {
                // 托管回调异常止步于此, 不越过非托管边界, 也不阻断其它程序的键盘输入。
            }
            return NativeMethods.CallNextHookEx(session.Hook, code, wParam, lParam);
        }
    }
}
