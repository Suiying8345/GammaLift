using System;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GammaLift
{
    public enum GammaRangeResult
    {
        AlreadyUnlocked,   // 注册表里已经是 256
        Unlocked,          // 本次写入成功
        Failed,            // 写了但没读回预期值
        Canceled           // 用户取消了 UAC
    }

    /// <summary>注册表相关的系统集成：开机自启动、Windows Gamma 范围限制。
    /// 设置对话框与主窗体共用。</summary>
    public static class SystemIntegration
    {
        private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string RunKeyName = "GammaLift";
        private const string IcmKeyPath = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\ICM";
        private const string GammaRangeValue = "GdiIcmGammaRange";
        private const int GammaRangeUnlocked = 256;

        // ---------- 开机自启动 ----------

        public static bool ReadAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    return k != null && k.GetValue(RunKeyName) != null;
                }
            }
            catch (Exception) { return false; }
        }

        /// <summary>写入/删除 HKCU 启动项。返回 false 表示写注册表失败。</summary>
        public static bool WriteAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (enable)
                        k.SetValue(RunKeyName, "\"" + Application.ExecutablePath + "\"");
                    else
                        k.DeleteValue(RunKeyName, false);
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        // ---------- Windows Gamma 范围限制 ----------

        /// <summary>GdiIcmGammaRange = 256 表示已解除 GDI 对 gamma LUT 的默认钳制。</summary>
        public static bool IsGammaRangeUnlocked()
        {
            try
            {
                object v = Registry.GetValue("HKEY_LOCAL_MACHINE\\" + IcmKeyPath, GammaRangeValue, null);
                return v is int && (int)v == GammaRangeUnlocked;
            }
            catch (Exception) { return false; }
        }

        /// <summary>以管理员权限写入 GdiIcmGammaRange=256（会弹 UAC）。</summary>
        public static GammaRangeResult UnlockGammaRange()
        {
            if (IsGammaRangeUnlocked()) return GammaRangeResult.AlreadyUnlocked;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "reg";
                psi.Arguments = "add \"HKLM\\" + IcmKeyPath + "\" /v " + GammaRangeValue +
                                " /t REG_DWORD /d " + GammaRangeUnlocked + " /f";
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process proc = Process.Start(psi);
                proc.WaitForExit(10000);
            }
            catch (Exception)
            {
                return GammaRangeResult.Canceled;   // 用户取消了 UAC 提示
            }
            return IsGammaRangeUnlocked() ? GammaRangeResult.Unlocked : GammaRangeResult.Failed;
        }
    }
}
