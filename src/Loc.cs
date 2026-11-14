using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace GammaLift
{
    /// <summary>语言下拉框的一项：语言代码 + 该语言自己的显示名（如 "Deutsch"）。</summary>
    public class UiLanguage
    {
        public string Code { get; set; }
        public string DisplayName { get; set; }

        public UiLanguage(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    /// <summary>界面多语言。
    /// 简体中文是源语言, 代码里直接写中文原文, 用 T("中文原文") 包一层;
    /// 其它语言只要提供 "中文原文 → 译文" 的字典, 查不到就原样返回中文 ——
    /// 所以漏翻最多显示原文, 不会出现空白或资源 key。</summary>
    public static class Loc
    {
        /// <summary>语言代码为空表示"跟随系统"。</summary>
        public const string AutoCode = "";
        public const string SourceCode = "zh-Hans";

        private static string _code = SourceCode;

        private static readonly Dictionary<string, Dictionary<string, string>> Tables =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<UiLanguage> _languages = new List<UiLanguage>();

        static Loc()
        {
            // 语言注册表: 每加一种语言在此登记一行, 并在 LangXX.cs 提供"中文原文 → 译文"映射
            Register(SourceCode, "简体中文", null);
            Register("zh-Hant", "繁體中文", LangZhHant.Table);
            Register("en", "English", LangEn.Table);
            Register("ja", "日本語", LangJa.Table);
            Register("ko", "한국어", LangKo.Table);
            Register("de", "Deutsch", LangDe.Table);
            Register("fr", "Français", LangFr.Table);
            Register("es", "Español", LangEs.Table);
            Register("pt-BR", "Português (Brasil)", LangPtBr.Table);
            Register("ru", "Русский", LangRu.Table);
            Register("it", "Italiano", LangIt.Table);
            Register("tr", "Türkçe", LangTr.Table);
            Register("vi", "Tiếng Việt", LangVi.Table);
            Register("id", "Bahasa Indonesia", LangId.Table);
            Register("nl", "Nederlands", LangNl.Table);
            Register("pl", "Polski", LangPl.Table);
        }

        private static void Register(string code, string displayName, Dictionary<string, string> table)
        {
            if (table != null) Tables[code] = table;
            _languages.Add(new UiLanguage(code, displayName));
        }

        /// <summary>已支持的语言（不含"跟随系统"）。</summary>
        public static IList<UiLanguage> Languages
        {
            get { return _languages; }
        }

        /// <summary>当前生效的语言代码（永远是具体语言, 不会是空）。</summary>
        public static string Code
        {
            get { return _code; }
        }

        /// <summary>语言代码对应的本地化名称; 未知代码返回代码本身。</summary>
        public static string DisplayNameOf(string code)
        {
            foreach (UiLanguage l in _languages)
                if (string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))
                    return l.DisplayName;
            return code;
        }

        /// <summary>设置界面语言。传空串 / null / 未知代码时回退到跟随系统。</summary>
        public static void SetLanguage(string code)
        {
            _code = Resolve(code);
        }

        /// <summary>把语言代码解析成实际生效的语言代码（空 / 未知时跟随系统）。</summary>
        public static string Resolve(string code)
        {
            if (!string.IsNullOrEmpty(code))
            {
                foreach (UiLanguage l in _languages)
                {
                    if (string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))
                        return l.Code;
                }
            }
            return DetectSystem();
        }

        /// <summary>按 Windows 的界面语言自动选一个受支持的语言。</summary>
        public static string DetectSystem()
        {
            try
            {
                CultureInfo c = CultureInfo.CurrentUICulture;
                string name = c.Name;
                string two = c.TwoLetterISOLanguageName;

                if (two == "zh")
                {
                    // 繁体: 台湾 / 香港 / 澳门, 以及带 Hant 脚本标记的区域
                    if (name.IndexOf("TW", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("HK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("MO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "zh-Hant";
                    return SourceCode;
                }
                if (two == "pt") return "pt-BR";

                foreach (UiLanguage l in _languages)
                    if (string.Equals(l.Code, two, StringComparison.OrdinalIgnoreCase))
                        return l.Code;
            }
            catch (Exception) { }
            return "en";
        }

        /// <summary>取译文：以简体中文原文为 key, 缺失时回退原文。</summary>
        public static string T(string chinese)
        {
            if (string.IsNullOrEmpty(chinese)) return "";
            if (_code == SourceCode) return chinese;

            Dictionary<string, string> table;
            if (Tables.TryGetValue(_code, out table) && table != null)
            {
                string s;
                if (table.TryGetValue(chinese, out s) && !string.IsNullOrEmpty(s))
                    return s;
            }
            return chinese;
        }

        /// <summary>按指定语言代码取译文, 不改变当前界面语言。
        /// 用于"切换后目标语言"的提示（例如重启确认框在切换生效前就要用新语言显示）。</summary>
        public static string TIn(string code, string chinese)
        {
            if (string.IsNullOrEmpty(chinese)) return "";
            string resolved = Resolve(code);
            if (resolved == SourceCode) return chinese;

            Dictionary<string, string> table;
            if (Tables.TryGetValue(resolved, out table) && table != null)
            {
                string s;
                if (table.TryGetValue(chinese, out s) && !string.IsNullOrEmpty(s))
                    return s;
            }
            return chinese;
        }

        /// <summary>T 的格式化版本：T 的译文里用 {0} {1} 占位。</summary>
        public static string F(string chinese, params object[] args)
        {
            return string.Format(T(chinese), args);
        }

        /// <summary>按当前语言挑一个能显示该语言文字的界面字体, 字号保持不变。</summary>
        public static Font UiFont(float size)
        {
            return UiFont(_code, size);
        }

        /// <summary>按指定语言代码挑字体, 不改变当前界面语言（配合 TIn 使用）。</summary>
        public static Font UiFont(string code, float size)
        {
            string[] chain;
            switch (Resolve(code))
            {
                case "zh-Hans":
                    chain = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" };
                    break;
                case "zh-Hant":
                    chain = new string[] { "Microsoft JhengHei UI", "Microsoft JhengHei", "Segoe UI" };
                    break;
                case "ja":
                    chain = new string[] { "Yu Gothic UI", "Meiryo UI", "Segoe UI" };
                    break;
                case "ko":
                    chain = new string[] { "Malgun Gothic", "Segoe UI" };
                    break;
                default:
                    chain = new string[] { "Segoe UI", "Microsoft Sans Serif" };
                    break;
            }

            for (int i = 0; i < chain.Length; i++)
                if (FontExists(chain[i]))
                    return new Font(chain[i], size);

            return new Font(FontFamily.GenericSansSerif, size);
        }

        private static bool FontExists(string name)
        {
            try
            {
                using (FontFamily f = new FontFamily(name))
                    return true;
            }
            catch (Exception)
            {
                return false;   // 未安装该字体时 FontFamily 构造会抛 ArgumentException
            }
        }
    }
}
