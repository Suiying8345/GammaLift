using System.Reflection;

[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]
[assembly: AssemblyInformationalVersion("1.0.1")]

namespace GammaLift
{
    /// <summary>产品元信息：版本号、版权、项目仓库与 MIT 许可证全文。</summary>
    internal static class AppInfo
    {
        /// <summary>对外显示的版本号, 不含末尾的 .0。</summary>
        public const string Version = "1.0.1";

        /// <summary>两行版权，与仓库根目录的 LICENSE 保持一致（本软件作者在前，原作者在后）。</summary>
        public const string Copyright =
            "Copyright (c) 2026 suiying8345\r\n" +
            "Copyright (c) 2026 NiseMonox";

        /// <summary>项目仓库地址。</summary>
        public const string RepositoryUrl = "https://github.com/Suiying8345/GammaLift";

        /// <summary>完整使用教程（仓库中的 docs/guide.md）。</summary>
        public const string GuideUrl = "https://github.com/Suiying8345/GammaLift/blob/main/docs/guide.md";

        /// <summary>MIT 许可证全文。内置在 exe 里，便携版不依赖外部 LICENSE 文件。</summary>
        public const string LicenseText =
            "MIT License\r\n" +
            "\r\n" +
            "Copyright (c) 2026 suiying8345\r\n" +
            "Copyright (c) 2026 NiseMonox\r\n" +
            "\r\n" +
            "Permission is hereby granted, free of charge, to any person obtaining a copy\r\n" +
            "of this software and associated documentation files (the \"Software\"), to deal\r\n" +
            "in the Software without restriction, including without limitation the rights\r\n" +
            "to use, copy, modify, merge, publish, distribute, sublicense, and/or sell\r\n" +
            "copies of the Software, and to permit persons to whom the Software is\r\n" +
            "furnished to do so, subject to the following conditions:\r\n" +
            "\r\n" +
            "The above copyright notice and this permission notice shall be included in all\r\n" +
            "copies or substantial portions of the Software.\r\n" +
            "\r\n" +
            "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR\r\n" +
            "IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,\r\n" +
            "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE\r\n" +
            "AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER\r\n" +
            "LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,\r\n" +
            "OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE\r\n" +
            "SOFTWARE.";
    }
}
