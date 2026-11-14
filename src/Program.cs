using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GammaLift
{
    internal static class Program
    {
        private const string MutexName = "GammaLift_SingleInstance";
        private const string RestartSwitch = "--restarted";

        /// <summary>重复启动时广播的消息名。Program 与 MainForm 共用这一处字面量；分开写时
        /// 漏改一处会让"唤起已有窗口"静默失效, 且编译不报错。</summary>
        internal const string ShowWindowMessageName = "GammaLift_ShowWindow";

        private static Mutex _instanceMutex;
        private static bool _ownsMutex;   // 本进程是否真正持有单实例锁

        [STAThread]
        private static void Main(string[] args)
        {
            bool restarted = HasSwitch(args, RestartSwitch);
            bool createdNew;
            _instanceMutex = new Mutex(true, MutexName, out createdNew);
            _ownsMutex = createdNew;

            try
            {
                if (!_ownsMutex)
                {
                    // 语言切换后的自重启: 旧进程还在收尾, 等它退出释放互斥锁后接管,
                    // 这样就不用提前释放锁, 也不会出现"旧进程没退、新进程又起来"的双实例
                    if (restarted && WaitForMutex(_instanceMutex, 5000))
                    {
                        _ownsMutex = true;
                    }
                    else
                    {
                        // 已有实例在运行: 通知它把窗口调到前台, 然后退出
                        uint msg = NativeMethods.RegisterWindowMessage(ShowWindowMessageName);
                        if (msg != 0)
                            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
                        return;
                    }
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            finally
            {
                Mutex m = _instanceMutex;
                _instanceMutex = null;
                if (m != null)
                {
                    if (_ownsMutex)
                    {
                        try { m.ReleaseMutex(); }
                        catch (Exception) { }
                    }
                    try { m.Dispose(); }
                    catch (Exception) { }
                    _ownsMutex = false;
                }
            }
        }

        private static bool HasSwitch(string[] args, string name)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>轮询等待命名互斥锁（旧进程退出后由本进程接管）。拿到所有权返回 true。</summary>
        private static bool WaitForMutex(Mutex mutex, int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                try
                {
                    if (mutex.WaitOne(100)) return true;
                }
                catch (AbandonedMutexException)
                {
                    // 旧进程异常退出: 所有权已转到本进程
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
                waited += 100;
            }
            return false;
        }

        /// <summary>语言切换后自重启：以 --restarted 启动新进程, 由旧进程随后正常退出。
        /// 启动失败返回 false, 此时旧进程继续运行（互斥锁始终没有提前释放）。</summary>
        internal static bool RestartApplication()
        {
            try
            {
                string exe = Application.ExecutablePath;
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.Arguments = RestartSwitch;
                psi.UseShellExecute = false;
                string dir = Path.GetDirectoryName(exe);
                if (!string.IsNullOrEmpty(dir)) psi.WorkingDirectory = dir;
                Process.Start(psi);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
