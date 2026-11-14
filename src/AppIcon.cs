using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace GammaLift
{
    /// <summary>统一的图标来源：窗体、托盘、置顶提示窗都从这里取 GammaLift 图标。
    /// 同一份 app.ico 由 build.ps1 以 /win32icon 嵌入 exe、以 /resource 嵌入 "GammaLift.ico",
    /// 因此资源管理器、任务栏、托盘、通知四处的图案完全一致。
    /// 资源读取走 GetManifestResourceStream: new Icon(typeof(...), "GammaLift.ico") 这种重载会拿
    /// 类型全名拼资源名 ("GammaLift.GammaLift.ico"), 与 /resource 嵌入的 "GammaLift.ico" 不符,
    /// 失败后静默回退成系统默认图标。</summary>
    internal static class AppIcon
    {
        private const string ResourceName = "GammaLift.ico";

        private static Icon _full;
        private static readonly Dictionary<int, Icon> _sized = new Dictionary<int, Icon>();

        /// <summary>完整图标（多尺寸, 由系统挑选合适帧）, 用于窗体图标。</summary>
        public static Icon Load()
        {
            if (_full != null) return _full;
            try
            {
                Stream res = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
                if (res != null)
                {
                    using (res)
                        _full = new Icon(res);
                }
            }
            catch (Exception)
            {
                _full = null;
            }
            if (_full == null) _full = SystemIcons.Application;   // 理论上不会发生, 兜底避免启动失败
            return _full;
        }

        /// <summary>取指定边长的图标帧（托盘 16/20/24、提示窗 32/48）。
        /// 直接从 ico 流按尺寸取帧, 比拿 32px 帧缩放更清晰。</summary>
        public static Icon Load(int size)
        {
            if (size <= 0) return Load();

            Icon cached;
            if (_sized.TryGetValue(size, out cached) && cached != null)
                return cached;

            Icon icon = null;
            try
            {
                Stream res = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName);
                if (res != null)
                {
                    using (res)
                        icon = new Icon(res, new Size(size, size));
                }
            }
            catch (Exception)
            {
                icon = null;
            }
            if (icon == null)
            {
                try { icon = new Icon(Load(), new Size(size, size)); }
                catch (Exception) { icon = Load(); }
            }
            _sized[size] = icon;
            return icon;
        }
    }
}
