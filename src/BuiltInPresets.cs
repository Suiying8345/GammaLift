using System.Collections.Generic;
using System.Windows.Forms;

namespace GammaLift
{
    /// <summary>首次启动种入的系统预设：默认 / 游戏暗光 / 游戏极暗 / 暗场观影。
    /// 名称按首次启动的界面语言生成；是否已种入由 AppSettings.PresetsSeeded 记录，删除后不再恢复。</summary>
    internal static class BuiltInPresets
    {
        /// <summary>「循环切换预设」的初始热键（单击）。</summary>
        public const int CycleHotKey = (int)Keys.F4;

        /// <summary>「默认」预设的初始热键（长按），用于一键恢复默认色彩。</summary>
        public const int DefaultHotKey = (int)Keys.F4;

        /// <summary>首次启动种入。已有预设只标记 PresetsSeeded，不补种；previouslySeeded 表示是否真正新增。</summary>
        public static bool Seed(AppSettings settings, IEnumerable<string> stableKeys, out bool changed)
        {
            changed = false;
            if (settings == null || settings.PresetsSeeded) return false;
            if (settings.Profiles == null) settings.Profiles = new List<Profile>();

            if (settings.Profiles.Count > 0)
            {
                settings.PresetsSeeded = true;
                changed = true;
                return false;
            }

            List<string> keys = new List<string>();
            foreach (string key in stableKeys)
                if (!string.IsNullOrEmpty(key)) keys.Add(key);
            if (keys.Count == 0) return false;   // 没有显示器时下次启动再试, 不标记已种入

            List<Profile> profiles = Create(keys);
            settings.Profiles.AddRange(profiles);
            settings.LastProfile = profiles[0].Name;
            settings.CycleHotKey = CycleHotKey;
            settings.CycleHotKeyTrigger = HotkeyTrigger.Single;
            settings.PresetsSeeded = true;
            changed = true;
            return true;
        }

        /// <summary>按四套参数生成预设；每个预设覆盖传入的每台显示器。</summary>
        public static List<Profile> Create(IEnumerable<string> stableKeys)
        {
            List<Profile> profiles = new List<Profile>();
            profiles.Add(CreateProfile(Loc.T("默认"), 0, 0, 1.00, stableKeys, DefaultHotKey, HotkeyTrigger.LongPress));
            profiles.Add(CreateProfile(Loc.T("游戏暗光"), 0, 8, 1.40, stableKeys, 0, HotkeyTrigger.Single));
            profiles.Add(CreateProfile(Loc.T("游戏极暗"), 0, 4, 2.20, stableKeys, 0, HotkeyTrigger.Single));
            profiles.Add(CreateProfile(Loc.T("暗场观影"), 0, 4, 1.16, stableKeys, 0, HotkeyTrigger.Single));
            return profiles;
        }

        private static Profile CreateProfile(string name, int brightness, int contrast, double gamma,
            IEnumerable<string> stableKeys, int hotKey, HotkeyTrigger trigger)
        {
            Profile p = new Profile();
            p.Name = name;
            p.HotKey = hotKey;
            p.HotKeyTrigger = trigger;
            foreach (string key in stableKeys)
            {
                MonitorConfig cfg = new MonitorConfig();
                SetChannel(cfg.Red, brightness, contrast, gamma);
                SetChannel(cfg.Green, brightness, contrast, gamma);
                SetChannel(cfg.Blue, brightness, contrast, gamma);
                p.Monitors[key] = cfg;
            }
            return p;
        }

        private static void SetChannel(ChannelSettings channel, int brightness, int contrast, double gamma)
        {
            channel.Brightness = brightness;
            channel.Contrast = contrast;
            channel.Gamma = gamma;
        }
    }
}
