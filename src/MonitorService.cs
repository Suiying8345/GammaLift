using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace GammaLift
{
    /// <summary>一台已连接显示器的运行时信息。</summary>
    public class DisplayMonitor
    {
        public string DeviceName { get; set; }     // \\.\DISPLAY1
        public string FriendlyName { get; set; }   // 显示器型号字符串
        public string StableKey { get; set; }      // 基于硬件 DeviceID 的稳定标识（用于保存配置）
        public Rectangle Bounds { get; set; }
        public bool IsPrimary { get; set; }
        public IntPtr HMonitor { get; set; }

        // DDC/CI 状态（由后台线程填充）
        public IntPtr PhysicalHandle { get; set; }
        public bool DdcProbed { get; set; }
        public bool DdcBrightnessOk { get; set; }
        public bool DdcContrastOk { get; set; }
        public bool DdcSaturationOk { get; set; }

        public override string ToString()
        {
            string n = DeviceName.Replace("\\\\.\\", "");
            string s = n + "  " + FriendlyName;
            if (IsPrimary) s += Loc.T(" (主屏)");
            return s;
        }
    }

    public static class VcpCodes
    {
        public const byte Brightness = 0x10;
        public const byte Contrast = 0x12;
        public const byte Saturation = 0x8A;
    }

    public static class MonitorService
    {
        /// <summary>枚举当前所有激活的显示器。</summary>
        public static List<DisplayMonitor> Enumerate()
        {
            List<DisplayMonitor> result = new List<DisplayMonitor>();

            // 1) 枚举显卡输出（\\.\DISPLAYn），取友好名称和稳定 ID
            for (uint i = 0; ; i++)
            {
                NativeMethods.DISPLAY_DEVICE adapter = new NativeMethods.DISPLAY_DEVICE();
                adapter.cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICE));
                if (!NativeMethods.EnumDisplayDevices(null, i, ref adapter, 0))
                    break;

                if ((adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
                    continue;
                if ((adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_MIRRORING_DRIVER) != 0)
                    continue;

                DisplayMonitor m = new DisplayMonitor();
                m.DeviceName = adapter.DeviceName;
                m.IsPrimary = (adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;
                m.FriendlyName = "";
                m.StableKey = adapter.DeviceName;
                m.PhysicalHandle = IntPtr.Zero;

                NativeMethods.DISPLAY_DEVICE mon = new NativeMethods.DISPLAY_DEVICE();
                mon.cb = adapter.cb;
                if (NativeMethods.EnumDisplayDevices(adapter.DeviceName, 0, ref mon, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME))
                {
                    m.FriendlyName = mon.DeviceString;
                    if (!string.IsNullOrEmpty(mon.DeviceID))
                        m.StableKey = mon.DeviceID;
                }
                if (string.IsNullOrEmpty(m.FriendlyName))
                    m.FriendlyName = Loc.T("通用显示器");

                result.Add(m);
            }

            // 2) 关联 HMONITOR（DDC/CI 需要）和屏幕坐标
            List<KeyValuePair<IntPtr, NativeMethods.MONITORINFOEX>> hmons =
                new List<KeyValuePair<IntPtr, NativeMethods.MONITORINFOEX>>();
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                delegate(IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data)
                {
                    NativeMethods.MONITORINFOEX info = new NativeMethods.MONITORINFOEX();
                    info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
                    if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
                        hmons.Add(new KeyValuePair<IntPtr, NativeMethods.MONITORINFOEX>(hMonitor, info));
                    return true;
                }, IntPtr.Zero);

            foreach (DisplayMonitor m in result)
            {
                foreach (KeyValuePair<IntPtr, NativeMethods.MONITORINFOEX> kv in hmons)
                {
                    if (string.Equals(kv.Value.szDevice, m.DeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        m.HMonitor = kv.Key;
                        NativeMethods.RECT r = kv.Value.rcMonitor;
                        m.Bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                        break;
                    }
                }
            }

            return result;
        }

        // ---------- 软件 Gamma ----------

        /// <summary>把配置生成的 LUT 写入指定显示器。返回 false 表示被系统拒绝（超出允许范围）。
        /// baseline 为该显示器的基线 LUT（如系统 ICC 校准），调节在其上合成；可为 null。</summary>
        public static bool ApplyGamma(string deviceName, MonitorConfig cfg, ushort[] baseline)
        {
            ushort[] ramp = GammaMath.BuildRamp(cfg, baseline);
            IntPtr dc = OpenDc(deviceName);
            if (dc == IntPtr.Zero)
                return false;
            try
            {
                return NativeMethods.SetDeviceGammaRamp(dc, ramp);
            }
            finally
            {
                NativeMethods.DeleteDC(dc);
            }
        }

        /// <summary>读取显示器当前生效的 LUT（用于捕获 ICC 校准基线）。失败返回 null。</summary>
        public static ushort[] GetGammaRamp(string deviceName)
        {
            IntPtr dc = OpenDc(deviceName);
            if (dc == IntPtr.Zero)
                return null;
            try
            {
                ushort[] ramp = new ushort[768];
                return NativeMethods.GetDeviceGammaRamp(dc, ramp) ? ramp : null;
            }
            finally
            {
                NativeMethods.DeleteDC(dc);
            }
        }

        private static IntPtr OpenDc(string deviceName)
        {
            IntPtr dc = NativeMethods.CreateDC(null, deviceName, null, IntPtr.Zero);
            if (dc == IntPtr.Zero)
                dc = NativeMethods.CreateDC(deviceName, null, null, IntPtr.Zero);
            return dc;
        }

        // ---------- DDC/CI ----------

        private static readonly object DdcLock = new object();

        /// <summary>获取物理显示器句柄并探测支持的 VCP 功能。慢操作，应在后台线程调用。
        /// 部分显示器 / 转接器第一次 DDC 查询会超时失败（手动"重新读取"又正常），
        /// 所以整个"取句柄 + 查询"流程重试几次再判定为不支持。</summary>
        public static void ProbeDdc(DisplayMonitor m)
        {
            lock (DdcLock)
            {
                ReleaseDdc(m);
                m.DdcProbed = true;
                m.DdcBrightnessOk = false;
                m.DdcContrastOk = false;
                m.DdcSaturationOk = false;

                if (m.HMonitor == IntPtr.Zero)
                    return;

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (!OpenPhysicalLocked(m))
                        return;

                    // 个别驱动/转接器第一次返回的句柄是 0（后续 DdcGet 必然失败），视为本次没拿到有效句柄
                    if (m.PhysicalHandle == IntPtr.Zero)
                    {
                        if (attempt < 2) Thread.Sleep(120);
                        continue;
                    }

                    uint type, cur, max;
                    m.DdcBrightnessOk = NativeMethods.GetVCPFeatureAndVCPFeatureReply(m.PhysicalHandle, VcpCodes.Brightness, out type, out cur, out max) && max > 0;
                    m.DdcContrastOk = NativeMethods.GetVCPFeatureAndVCPFeatureReply(m.PhysicalHandle, VcpCodes.Contrast, out type, out cur, out max) && max > 0;
                    m.DdcSaturationOk = NativeMethods.GetVCPFeatureAndVCPFeatureReply(m.PhysicalHandle, VcpCodes.Saturation, out type, out cur, out max) && max > 0;

                    if (m.DdcBrightnessOk || m.DdcContrastOk || m.DdcSaturationOk)
                        return;

                    // 三项全失败: 可能是这条 DDC 通道刚建立, 换一个句柄再试
                    ReleaseDdc(m);
                    if (attempt < 2) Thread.Sleep(120);
                }
            }
        }

        /// <summary>在已持有 DdcLock 的前提下取物理显示器句柄（一个 HMONITOR 正常只对应一个物理屏, 取第一个）。</summary>
        private static bool OpenPhysicalLocked(DisplayMonitor m)
        {
            uint count;
            if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(m.HMonitor, out count) || count == 0)
                return false;

            NativeMethods.PHYSICAL_MONITOR[] phys = new NativeMethods.PHYSICAL_MONITOR[count];
            if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(m.HMonitor, count, phys))
                return false;

            m.PhysicalHandle = phys[0].hPhysicalMonitor;
            if (count > 1)
            {
                NativeMethods.PHYSICAL_MONITOR[] rest = new NativeMethods.PHYSICAL_MONITOR[count - 1];
                Array.Copy(phys, 1, rest, 0, (int)(count - 1));
                NativeMethods.DestroyPhysicalMonitors(count - 1, rest);
            }
            return true;
        }

        /// <summary>读取一个 VCP 值。返回 false 表示失败。慢操作。
        /// 首次读取偶发超时, 失败后短暂等待再重试一次。</summary>
        public static bool DdcGet(DisplayMonitor m, byte code, out uint current, out uint max)
        {
            current = 0;
            max = 0;
            lock (DdcLock)
            {
                if (m.PhysicalHandle == IntPtr.Zero) return false;

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    uint type;
                    if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(m.PhysicalHandle, code, out type, out current, out max))
                        return true;
                    if (attempt == 0) Thread.Sleep(60);
                }
                return false;
            }
        }

        /// <summary>写入一个 VCP 值。慢操作。</summary>
        public static bool DdcSet(DisplayMonitor m, byte code, uint value)
        {
            lock (DdcLock)
            {
                if (m.PhysicalHandle == IntPtr.Zero) return false;
                return NativeMethods.SetVCPFeature(m.PhysicalHandle, code, value);
            }
        }

        public static void ReleaseDdc(DisplayMonitor m)
        {
            // 与 Get/Set/Probe 共用 DdcLock: 否则销毁句柄会与进行中的 DDC 调用形成 use-after-free
            lock (DdcLock)
            {
                if (m.PhysicalHandle != IntPtr.Zero)
                {
                    NativeMethods.PHYSICAL_MONITOR[] arr = new NativeMethods.PHYSICAL_MONITOR[1];
                    arr[0].hPhysicalMonitor = m.PhysicalHandle;
                    NativeMethods.DestroyPhysicalMonitors(1, arr);
                    m.PhysicalHandle = IntPtr.Zero;
                }
            }
        }

        public static void ReleaseAll(IEnumerable<DisplayMonitor> monitors)
        {
            foreach (DisplayMonitor m in monitors)
                ReleaseDdc(m);
        }
    }
}
