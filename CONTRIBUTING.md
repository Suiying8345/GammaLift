# 贡献指南

GammaLift 是 C# + WinForms 的便携单文件应用，欢迎提交 issue 与 pull request。

## 环境要求

- Windows 10 / 11
- 不需要安装 .NET SDK：构建脚本直接调用系统自带的 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
- 目标运行时：.NET Framework 4.x（Windows 10 / 11 自带）

## 构建

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

产物为 `build\GammaLift.exe`。

仓库没有 `.csproj`、没有 NuGet、没有 npm：`build.ps1` 把 `src/*.cs` 全量通配交给 `csc.exe` 编译，参数为 `/target:winexe /platform:anycpu /optimize+ /codepage:65001`，并嵌入 `app.manifest` 与 `app.ico`；引用集固定为 `System.dll`、`System.Core.dll`、`System.Drawing.dll`、`System.Windows.Forms.dll`、`System.Web.Extensions.dll`。

新增的 `.cs` 文件放进 `src/` 即自动参与编译，不需要改脚本。例外是嵌入资源名 `GammaLift.ico`：它在 `build.ps1` 与 `src/AppIcon.cs` 各写一处，改名时两处需同步，否则程序静默回退成系统默认图标，编译不报错。

## 测试

```powershell
powershell -ExecutionPolicy Bypass -File tools\Test.ps1
```

默认运行三套测试：`TestProfileShareCode`（分享码编解码与校验）、`TestHotkeys`（手势状态机与绑定校验）、`TestProfileOrder`（预设排序与增删）。这几套只检查纯逻辑，不访问显示器，也不读写真实设置文件。

- 加 `-Desktop` 会额外编译并运行 `TestDesktopUi`，用真实控件、模拟显示器与注册表、临时配置目录验证交互与布局，需要可用的桌面会话
- 加 `-Native` 会额外运行 `TestHotkeys.exe --native`，它会短暂打开测试窗口向前台发送按键，运行期间不要切走焦点

测试编译带 `/warnaserror+`：任何编译器警告都会直接判失败，提交 PR 前请保证测试全绿。

## 语言与语法上限

构建使用 .NET Framework 4.0 时代的 `csc.exe`，**语法上限是 C# 5**：

| 不能用                                              | 改用                            |
| --------------------------------------------------- | ------------------------------- |
| 字符串插值 `$"{x}"`                               | `string.Format` 或 `+` 拼接 |
| null 条件运算符 `?.`、`??=`                     | 显式 `if (x != null)`         |
| `nameof()`、表达式体成员 `=>`、`using static` | 常规写法                        |
| 元组、模式匹配、局部函数、`out var`               | 传统等价写法                    |
| 自动属性初始化器                                    | 在构造函数里赋值                |

可以放心使用：`var`、LINQ、lambda、泛型、`async/await`、`string.Format`。

JSON 序列化使用 `System.Web.Extensions.dll` 的 `JavaScriptSerializer`：`build.ps1` 的引用集里只有这一个 JSON 实现，因此不引入 Newtonsoft.Json 或 `System.Text.Json`。

## 代码约定

完整细节见 [AGENTS.md](AGENTS.md) 的「代码约定」与「核心实现要点」，这里只列最容易踩的几条：

- 源文件 **UTF-8 无 BOM**
- UI 全部手写坐标布局，不用 Designer 与 `.resx`；所有坐标在 `LayoutUi()` 里按当前尺寸计算，不写在构建代码里
- 尺寸统一走 `S(int)`：96 dpi 设计值 × 界面缩放 × 系统 DPI；`AutoScaleMode` 为 `None`，与 `S()` 自带缩放叠加会双重放大
- 文案长度随 16 种界面语言变化，控件宽度用实测（如 `ButtonWidth()`）；按中文宽度估的固定值会在德语 / 俄语下溢出
- 所有 P/Invoke 声明放 `src\NativeMethods.cs`，业务文件里不散落 `[DllImport]`
- 底层 API 失败要优雅降级（例如显示器不支持 DDC/CI 时给出提示），不让程序以异常中断

## 新增界面语言

1. 在 `src\Loc.cs` 的静态构造函数里登记一行：`Register("xx", "该语言自己的显示名", LangXX.Table);`。简体中文是源语言，注册时表参数传 `null`
2. 新建 `src\LangXX.cs`，照 `src\LangEn.cs` 的格式定义 `internal static class LangXX`，提供 `Dictionary<string, string> Table`：键是简体中文原文，值是译文；漏翻的条目自动回退中文，不会出现空白或资源键
3. 运行 `powershell -ExecutionPolicy Bypass -File tools\check-i18n.ps1` 检查覆盖完整性，它比对代码里 `Loc.T` / `Loc.F` 用到的原文与各语言字典的键
4. 切换语言后程序会自重启生效，手动检查主界面与四个设置页（启动与权限 / 界面与语言 / 显示器 / 托盘与通知），确认长译文没有被截断或压到其他控件

## 提交信息

提交信息沿用仓库现有的 Conventional Commits 风格：`feat:` / `fix:` / `docs:` / `chore:` / `refactor:`，冒号后用一句话说明改动。不兼容改动写成 `feat!:`，或在正文加 `BREAKING CHANGE:` 说明影响。

## 提交前自检

1. `build.ps1` 编译零警告零错误
2. 单显示器与多显示器各跑一遍：切换显示器、拖滑杆、重置、复制 / 导入分享码弹窗、保存 / 应用预设、托盘菜单（含「重置所有显示器」）、关闭到托盘、最小化到托盘
3. 确认「重置」能还原系统 ICC 校准，而不是变灰或变亮
4. 新增配置字段时，同步处理 `SettingsStore.Load` 里的 null 兜底，使已有的 settings.json 仍能正常读入
5. 改动涉及产品名、文件路径、构建方式或源码结构时，同步更新 `AGENTS.md` / `README.md` / `README.en.md` / `docs/guide.md` / `CHANGELOG.md`

## 许可证

贡献即表示同意以 [MIT](LICENSE) 许可发布。`LICENSE` 与 `src\AppInfo.cs` 里的两行版权署名顺序保持不变：本软件作者在前，原作者在后。
