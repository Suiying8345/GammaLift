# AGENTS.md — GammaLift 项目说明

给 AI 编码助手 / 新接手开发者的项目导览。人类文档见 [README.md](README.md)（中文默认）/ [README.en.md](README.en.md)（英文）；详细功能与使用提示见 [docs/guide.md](docs/guide.md)，贡献流程见 [CONTRIBUTING.md](CONTRIBUTING.md)，版本记录见 [CHANGELOG.md](CHANGELOG.md)，后续计划见 [docs/roadmap.md](docs/roadmap.md)。

## 这是什么

经典 [Gamma Panel](https://gamma-panel.en.softonic.com/) 的现代复刻版 —— Windows 每显示器亮度 / 对比度 / Gamma / 饱和度调节工具。每台显示器可独立选择与调节。

产品名 **GammaLift**；C# 命名空间为 `GammaLift`。

- 便携单文件应用（`GammaLift.exe`），无安装程序
- 目标运行时：**Windows 自带的 .NET Framework 4.x**，不依赖任何外部 SDK / 运行时下载
- 技术栈：C# + WinForms + P/Invoke（GDI32 / User32 / Dxva2）
- 界面语言：内置 16 种，默认跟随系统
- 许可证：MIT

## 构建

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
# 产物: build\GammaLift.exe
```

`build.ps1` 直接调用系统自带的 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，
把 `src/*.cs` 全量通配编译，`/target:winexe /platform:anycpu /optimize+`，并嵌入 `app.manifest`。
项目不使用 `.csproj`、NuGet 或 npm，新增源文件放进 `src/` 即自动进入编译。

### 图标

`app.ico`（8 个尺寸：16/20/24/32/48/64/128/256）是唯一图标源，构建时同时以两种方式嵌入：

- `/win32icon:app.ico` → 资源管理器 / 任务栏看到的 exe 图标
- `/resource:app.ico,GammaLift.ico` → 运行时由 `AppIcon.Load()` 读取，供窗体与托盘使用

两条嵌入路径都来自 `app.ico`，换图标只需重新生成这一个文件：
`csc /out:MakeIcon.exe tools\MakeIcon.cs` 然后 `MakeIcon.exe app.ico`。
图标是深色圆角底板 + 暖色光晕 + 8 道光线，光晕与光线表达「提亮暗部」。
资源名 `GammaLift.ico` 在 `build.ps1` 与 `src/AppIcon.cs` 各写一处，两处需保持一致；不一致时 `AppIcon` 静默回退成系统默认图标，编译不报错。

### 文档截图

`docs/screenshots/main-zh.png` / `main-en.png` 由 `tools/MakeScreenshots.ps1` 生成：脚本排除 `MonitorService.cs` / `SystemIntegration.cs`，改用 `tools/MakeScreenshots.cs` 里的虚拟显示器（「演示显示器 A / B」「Demo Display A / B」），不会把本机显示器名带进文档。改 UI 后重跑该脚本即可更新两张图。

`docs/screenshots/presets-compare-zh.jpg` / `-en.jpg`（室内）与 `presets-night-zh.jpg` / `-en.jpg`（极暗）由 `build/gamma-test/readme_preview.py` 生成：分别读取 `build/gamma-test/src/01-indoor-mixed-light.png` 与 `10-night-black-room.png`，先裁掉顶部物品栏、左侧人形与底部体力 / 版本信息，再套三套推荐预设拼成 2×2 对比图。换素材或改预设参数时同步重跑。

### 语言与语法上限

构建链路是 .NET Framework 4.0 的 `csc.exe`，**语法上限为 C# 5**：

- 字符串插值 `$"{x}"` 不可用 → 改用 `string.Format` 或 `+` 拼接
- null 条件运算符 `?.`、`??=` 不可用 → 改用显式 `if (x != null)`
- `nameof()`、表达式体成员 `=>`、`using static`、元组、模式匹配、局部函数、`out var` 均不可用
- 自动属性初始化器 `{ get; set; } = 1;` 不可用
- 可用：`var`、LINQ、lambda、泛型、`async/await`（C# 5 已支持）、`string.Format`

`build.ps1` 已引用 `System.Web.Extensions.dll`，所以 JSON 用 `JavaScriptSerializer`（不是 Newtonsoft/System.Text.Json）。

## 源码地图

| 文件                           |     行数 | 职责                                                                                                                                                                                                                                    |
| ------------------------------ | -------: | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/Program.cs`             |      126 | 入口。单实例 Mutex（`GammaLift_SingleInstance`）；`--restarted` 时轮询等待旧进程释放锁后接管；重复启动时广播 `GammaLift_ShowWindow` 把已有窗口唤到前台。消息名收敛在 `Program.ShowWindowMessageName`，`MainForm` 引用同一常量 |
| `src/MainForm.cs`            |     3055 | 主窗体：手写 UI、滑杆联动、曲线、折叠硬件区、托盘、预设新建 / 保存 / 统一编辑提交 / 分享导入、手势热键与模态暂停                                                                                                                        |
| `src/SettingsForm.cs`        |      731 | 单页模态设置：按「启动与权限 / 界面与语言 / 显示器 / 托盘与通知」拆成 4 页，菜单二级项各开一个独立窗口；每页一组，右上角「ⓘ」悬停说明。与主窗体共用 `S()`，文字实测、超高滚动，取消不提交                                            |
| `src/AboutForm.cs`           |      237 | 模态「关于」对话框：图标、版本、两行版权、MIT 全文（只读可滚动）、项目仓库链接。顶部「帮助」菜单与托盘菜单共用                                                                                                                          |
| `src/TutorialForm.cs`        |      185 | 模态「使用教程」对话框：标题 + 「快速上手」5 条 + GitHub 完整教程链接；帮助菜单第一项                                                                                                                                                   |
| `src/AppInfo.cs`             |       51 | 产品元信息：程序集版本、对外版本号、两行版权、仓库地址、教程地址与内置 MIT 全文                                                                                                                                                         |
| `src/CurveView.cs`           |       19 | Gamma 曲线预览控件：`PictureBox` 子类，开启双缓冲 + `ResizeRedraw`，避免缩放时残留旧曲线                                                                                                                                            |
| `src/ToastForm.cs`           |      223 | 自绘置顶提示窗（替代系统气泡通知）：GammaLift 图标、圆角深色底板、不抢焦点、自动淡出、单实例复用                                                                                                                                        |
| `src/AppIcon.cs`             |       75 | 统一图标来源：从嵌入资源 `GammaLift.ico` 读取，按尺寸取帧并缓存，供窗体 / 托盘 / 提示窗使用                                                                                                                                           |
| `src/SystemIntegration.cs`   |       93 | 注册表集成：开机自启动（`Run` 键）、`GdiIcmGammaRange` 解除 / 查询                                                                                                                                                                  |
| `src/MonitorService.cs`      |      277 | 显示器枚举（EDID / 硬件 ID 作为配置键）、gamma LUT 读写、基线 LUT 捕获、DDC/CI 探测                                                                                                                                                     |
| `src/NativeMethods.cs`       |      268 | 所有 P/Invoke 声明与结构体，含显示器、独占热键、低级键盘钩子及消息循环                                                                                                                                                                  |
| `src/Models.cs`              |      252 | 数据模型、`HotkeyTrigger`、通知 / 循环热键偏好、DDC/CI 能力缓存（`DdcCapability`）、`PresetsSeeded` 系统预设标记，以及 `SettingsStore`（便携优先，目录不可写时回退「文档」文件夹）                                              |
| `src/BuiltInPresets.cs`      |       81 | 首次启动的四套系统预设（默认 / 游戏暗光 / 游戏极暗 / 暗场观影）：参数模板、每台显示器展开、初始热键与只种一次的判断                                                                                                                     |
| `src/HotkeyGestureEngine.cs` |      522 | 单调时钟、纯手势状态机、绑定校验 / 格式化、专用线程的非阻塞键盘观察钩子                                                                                                                                                                 |
| `src/ProfileShareCode.cs`    |      237 | GL2 单屏参数投影：flags + RGB 数值 + CRC-8、Base64url；联动 11 字符 / 分通道 22 字符                                                                                                                                                    |
| `src/ShareCodeForm.cs`       |      144 | 模态「导入分享码」对话框：粘贴 GL2 码、就地校验并显示错误，返回单屏参数                                                                                                                                                                 |
| `src/ProfileEditForm.cs`     |      463 | 模态「预设编辑」对话框：在副本上排序、重命名、删除，标记当前项，删除带未保存修改的当前项前二次确认；确定后统一提交，支持高 DPI 与 16 语言                                                                                               |
| `src/ConfirmForm.cs`         |      112 | 模态二选一确认框：文案按目标语言传入，按钮固定英文 Yes / No（系统 MessageBox 按钮会跟随 Windows 显示语言）                                                                                                                              |
| `src/GammaMath.cs`           |       73 | Gamma 曲线数学：由 (brightness, contrast, gamma) 生成 256 项 LUT                                                                                                                                                                        |
| `src/IdentifyForm.cs`        |       60 | 「识别显示器」时全屏闪烁数字的覆盖窗                                                                                                                                                                                                    |
| `src/Loc.cs`                 |      229 | 多语言框架：语言注册表、`T("中文原文")` 查表、跟随系统的语言探测与持久化                                                                                                                                                              |
| `src/Lang*.cs`               | 每个 259 | 各语言字典（`LangDe` / `LangEn` / `LangZhHant` …），只提供「中文原文 → 译文」的映射，漏翻回退中文                                                                                                                               |

## 核心实现要点

**软件调节（gamma LUT）**
`CreateDC("\\.\DISPLAYn")` 取得每个输出的 DC，再 `SetDeviceGammaRamp` 写入 —— 每个输出口有独立 LUT，这是按显示器独立调节的实现基础。

**基线保护**
首次见到某台显示器时，用 `GetDeviceGammaRamp` 把当前 LUT 存为 **baseline**（通常是系统 ICC/vcgt 校准曲线）。
之后的调节都叠加在 baseline 之上合成；因此「重置」能精确还原系统校准，而不是写一条线性 ramp。
`Baselines` 按显示器硬件 ID 持久化在 settings.json 里。

**公式**（沿用经典 Gamma Panel 的公式）
`v = ((i/255)^(1/γ) − 0.5) × (contrast+100)/100 + 0.5 + brightness/200`，逐通道生成 256 项 LUT。

**硬件调节（DDC/CI）**
`dxva2.dll` 的 Monitor Configuration API，VCP 码 `0x10` 亮度 / `0x12` 对比度 / `0x8A` 饱和度。
写入走**队列 + 定时器合并**（`_pendingDdcWrites` / `_pendingDdcTargets` / `FlushDdcWrites` / `DrainDdcQueue`）：DDC 写入慢，直接跟滑杆会卡顿。
饱和度只能走硬件：gamma LUT 是逐通道映射，物理上无法改变饱和度（需要跨通道混合）。

**DDC 首次探测要重试**
部分笔记本内屏 / 转接器的第一次 `GetPhysicalMonitorsFromHMONITOR` 会返回 **句柄 0**（此时 VCP 查询看起来还成功，但后续 `DdcGet` 必然失败），第二次才给出可用句柄。
`MonitorService.ProbeDdc` 因此把句柄 0 视为本次失败并整轮重试（最多 3 次），三项 VCP 全失败也会重试；`DdcGet` 也带一次重试。
展开硬件面板时用 `LoadDdcToUi(true)` 强制重探，跳过能力缓存。窗体 `Load` 之前（`_loaded == false`）不发起读取：此时 `BeginInvoke` 没有句柄，结果会丢。

**硬件调节默认自动展开**
主界面默认只显示软件调节；硬件调节收在「更多」分组框 `grpMore` 里，收起时只保留标题行。**标题行本身就是开关**：分组框标题显示「更多 ▾ / 更多 ▴」，点击标题区（`grpMore.MouseClick` + `MoreHeaderHeight()` 判定）展开 / 收起；整行按钮会盖住分组框顶边，外框看起来没有闭合。展开后内部是粗体小标题「硬件调节 (DDC/CI)」+ `pnlDdc` 滑块区。分享码不在「更多」里（见下文「预设与分享码」）。
展开策略有两个**互斥**选项（设置 › 显示器）：`AppSettings.AutoExpandDdc`（默认开启，「硬件支持时自动展开」）与 `AppSettings.ShowDdcPanel`（固定「启动时展开」）。自动模式下按选中显示器的 DDC/CI 能力伸缩：能力缓存在 `AppSettings.DdcCapabilities`（按 `StableKey`，由每次探测更新），启动和切换显示器时优先用缓存即时判断，未知才后台探测（`ProbeDdcForAuto` + `_ddcAutoProbing` 去重），探测结果回来再展开 / 收起；不支持时收起但保留「更多」入口，便于看到「不支持 DDC/CI」的说明。自动模式下手动点标题行只临时生效，不写回 `ShowDdcPanel`；切换显示器 / 刷新列表 / 重启会重新按能力判断。
`RefreshMonitors` 的 DDC 探测条件：`_moreExpanded || AutoExpandDdc`。两者都不成立时**完全不碰 DDC**（部分显示器读取 DDC 会闪屏或明显变慢）；展开时探测全部显示器，自动模式收起时只探测当前显示器。

**语言切换后的自重启**
自重启路径不手动 `ReleaseMutex`：旧进程保持锁，`Program.RestartApplication()` 以 `--restarted` 启动新进程，新进程在 `Program.Main` 里每 100 ms 轮询等待旧进程退出并接管锁（最多 5 秒）。
`OnFormClosing` 的「关闭到托盘」分支判断 `!_restarting`：否则 `Close()`（`CloseReason` 是 `UserClosing`）会被取消，旧进程只藏到托盘、新进程又起来，造成双实例。
重启确认用 `ConfirmForm`（不是 `MessageBox`）：文案通过 `Loc.TIn(目标语言, "原文")` 取目标语言的译文，字体也用 `Loc.UiFont(目标语言, ...)`，让人在重启前就看到新语言；按钮固定英文 Yes / No，因为系统 MessageBox 的按钮跟随 Windows 显示语言，换过语言的用户按习惯找不到「是 / 否」。`Loc.TIn` 只查表、不改 `Loc.Code`，所以选「否」后当前界面语言不受影响。

**帮助菜单与使用教程**
「帮助」下拉顺序是「使用教程」/ 分隔线 /「关于 GammaLift」。`TutorialForm` 是模态单页：标题 + 「快速上手」5 条 + 「查看完整教程（GitHub）」链接（`AppInfo.GuideUrl` → `docs/guide.md`）。正文 key 是一整段带 `\r\n` 的中文原文，16 种语言在 `Lang*.cs` 里按同一 key 提供译文；新增语言时别忘了这条。`OpenTutorial()` 与 `OpenAbout()` 都走 `ShowOwnedDialog`，保持热键暂停与模态行为一致。

**置顶提示（ToastForm）**
便携单文件应用没有开始菜单快捷方式与 AppUserModelID，`NotifyIcon.ShowBalloonTip` 转成 Windows toast 后图标会变成通用图标 / 空白，所以通知一律走 `ToastForm.ShowMessage`（自绘、TopMost、`WS_EX_NOACTIVATE`、自动淡出、单实例复用）。
托盘图标与提示窗图标统一从 `AppIcon` 取；直接 `new Icon(...)` 拿不到与 exe 一致的多尺寸帧。

**预设与分享码**
配置文件组只有一行：预设下拉 + 热键（框内右侧 × 清空）+ 触发方式 + 保存 / 新建；德语 / 俄语等长译文放不下时自动回退成两行。
热键框保留白色输入框外观。只读 `TextBox` 的视觉主题会额外画一层灰色内底，因此在 `HandleCreated` 里用 `NativeMethods.SetWindowTheme(hwnd, " ", " ")` 关掉该控件主题，`BackColor = SystemColors.Window` 才会生效。
预设组上方单独一行是「循环切换预设」：热键框（× 清空）+ 单击 / 双击 / 长按 + 右对齐的「编辑…」，数据存 `CycleHotKey` / `CycleHotKeyTrigger`。
`ProfileEditForm` 在副本上同时编辑顺序、草稿名称与删除项，确定后 `MainForm.EditProfiles` 才统一改写 `Profile.Name`、替换 `_settings.Profiles`，再刷新下拉 / 托盘并重建热键动作编号；顺序直接由数组序列化，`_activeProfile` 按对象身份保持，删除当前预设后才回退到第一项。

分享码固定放在软件调节区右上角，三个按钮依次是「复制分享码 / 导入分享码 / 重置」（`LayoutGammaInner` 按 `ButtonWidth` 排布，长译文放不下时整排换到第二行）。
复制把当前选中显示器的参数写入剪贴板并弹 `ToastForm` 提示；导入打开 `ShareCodeForm` 模态框，粘贴、校验、报错都在弹窗内完成，通过后才应用到当前显示器。分享码只投影当前选中显示器的当前参数（不是已保存预设），默认参数时为空（复制按钮禁用）；编码用 GL2 二进制：flags（linked / compact）+ RGB 数值 + CRC-8，再 Base64url，联动 11 字符、分通道 22 字符。导入只改 `_settings.Current` 并立即应用，不新增预设、不改 `_activeProfile` / LastProfile。重置只作用于当前显示器；「重置所有显示器」只保留在托盘右键菜单。

**内置预设**
`BuiltInPresets.Seed` 在 `MainForm.Load` 的 `RefreshMonitors` 之后运行一次：`AppSettings.Profiles` 为空且有显示器时，种入「默认 / 游戏暗光 / 游戏极暗 / 暗场观影」四套，每套覆盖当时已连接的每台显示器；名称用 `Loc.T` 按首次启动的界面语言生成，之后切换语言不会改名。`AppSettings.PresetsSeeded` 记录已种入，用户删除后不会恢复；已有预设的配置只置位不补种。
初始热键：`CycleHotKey` = F4 单击（循环切换），「默认」预设 = F4 长按（恢复默认）。`LastProfile` 指向「默认」。
种子只覆盖首次启动时已连接的显示器；之后新接的显示器套用内置预设时按默认值处理，与现有用户预设一致。

**最小化 / 关闭到托盘**
`AppSettings.TrayOnClose` 控制关闭按钮是否隐藏到托盘；`AppSettings.MinimizeToTray`（默认 `true`，字段缺失时取构造函数默认值）控制最小化按钮。最小化时 `OnResize` 用 `BeginInvoke` 延迟 `Hide()`：同步调用会让任务栏按钮闪一下才消失。`Hide()` 同时移除任务栏按钮，只留通知区域图标。`ShowMainWindow()` 先置 `_suppressMinimizeHide` 再 `Show()` / 恢复 `Normal`，否则会再次触发隐藏。

**手势热键**
`HotkeyRules` 按键与手势共同判重；每个主键 / 修饰键组只独占注册一次，加入 `MOD_NOREPEAT`。`GlobalHotkeyListener` 在专用线程被动观察按下 / 抬起，回调不做 UI / DDC / 文件写入，WM_HOTKEY 不能再执行第二遍业务动作。
`HotkeyGestureEngine` 使用单调时间和最近截止时间：长按 400 ms，双击窗口取 Windows 设置，仅单击绑定即时触发。模态对话框、录键、休眠 / 锁屏、重绑清理待触发状态；录键暂停根据实际焦点管理，窗体失活后恢复注册：只依赖 `Enter` / `Leave` 会在 Alt-Tab 切换时漏掉。

**通知偏好**
NotifyEnabled 与 NotifyProfileApplied / NotifyProfileAppliedForeground / NotifyTrayOnClose 共同控制现有 Toast；缺失字段默认开启，其中 `NotifyProfileAppliedForeground` 默认关闭（前台只更新状态栏，不弹提示）。关闭总开关保留子项选择。状态栏、错误和保存确认不受影响，不引入 Windows 原生通知。

**界面缩放基准**
`UiScalePercent` 的 100% 对应默认启动尺寸：客户区 560×368（顶部菜单 + 循环热键行 + 一行配置文件，「更多」收起；「更多」展开时按 `MoreContentHeight()` 加高）。368 是让软件调节区三行正好 26 px、与硬件调节区等高的紧凑基准。
长译文让预设组回退成两行、或让软件调节区顶部条（通道单选 + 三个按钮 + 同步调节）换行时，`CompactClientHeight()` 会按 `MeasureGammaTop()` 的实测结果补上多出的行高 —— 顶部条两行而窗口不跟着长高时，Gamma 第三行会被「更多」分组框盖住。`LayoutUi` 里软件调节区的最小高度也由 `GammaMinHeight()` 给出，`RefreshMonitors` 改变「同步调节」可见性后要再走一次 `ApplyWindowSizeLimits(true)`。
`ApplyUiScale` 按这个设计基准重算 `ClientSize`，不以当前拖拽尺寸等比缩放（否则每次缩放都会累积误差）；改尺寸前先清空上一组 `MinimumSize` / `MaximumSize`，否则新尺寸被上一组限制夹住。
窗口只允许适度拉伸：不提供最大化（`MaximizeBox = false`）；最小客户区 = 默认紧凑高度，最大客户区 760×600（展开时高度再加 `MoreContentHeight()`），由 `MinClientSize()` / `MaxClientSize()` 给出。状态变化统一走 `ApplyWindowSizeLimits`，否则窗口压到最小时各分组框会互相重叠。
`MenuStrip` 是 `Dock=Top`，`LayoutUi` 的 `barY` 以 `menuMain.Height` 起算，改字号后同步 `menuMain.Font`。

设置窗口 `SettingsForm` 接收 `_uiScale` / `_dpiScale`，用同一套 `S()`，宽度与主窗体一致（560）；每页只构建本页控件，单组全宽，右上角（或副标题右侧）「ⓘ」承载原灰色说明。页面枚举 `SettingsPage` 决定构建、布局与提交的字段，`OpenSettings(page, focusUnlock)` 是唯一入口；「启动与权限」页内的小标题「解除极端数值限制」带独立「ⓘ」，状态栏链接以 `focusUnlock` 打开该页并聚焦按钮。文字高度用 `TextRenderer.MeasureText(..., WordBreak)` 实测；内容超出工作区高度时由 `pnlContent.AutoScroll` 滚动，底部按钮固定可见。

**曲线预览**
`picCurve` 用 `CurveView`（`PictureBox` 子类，开启 `OptimizedDoubleBuffer | AllPaintingInWmPaint | ResizeRedraw`）。
普通 `PictureBox` 在窗口缩放时只会拷贝旧像素、部分重绘，会残留上一个尺寸的曲线；`PaintCurve` 里先 `g.Clear(BackColor)`，`LayoutGammaInner` 末尾再 `Invalidate()` 一次。

## 状态与配置

- 配置文件：**便携优先**，与 exe 同目录、文件名 `GammaLiftSettings.json`；exe 目录不可写（Program Files / 只读共享）时回退用户「文档」文件夹同名文件（`SettingsStore.IsWritable` 探针判定，结果缓存）
- **版本与许可**：`AppInfo.cs` 是版本号、两行版权、仓库地址和 MIT 全文的唯一来源；程序集版本属性也在同一文件。两行版权的顺序是**本软件作者 suiying8345 在前、原作者 NiseMonox 在后**，`LICENSE` 与 `AboutForm` 的许可证全文保持同一顺序；「关于」窗口只展示许可证内的版权声明。`RepositoryUrl` 与仓库地址一致；发版时同步 `Version`、程序集版本与 `CHANGELOG.md`
- **按显示器硬件 ID 分键**，所以换接口 / 重新插拔不会错配配置
- 读写全包在 try/catch 里：配置损坏或保存失败都不能让程序崩溃（回退默认值）
- 顶部 `MenuStrip` 提供「设置」（下拉 4 个分页）「帮助 › 使用教程 / 关于」。`MinimizeToTray` 默认 `true`，`TrayOnClose` 默认 `true`
- 状态栏右下角显示版本号（`v` + `AppInfo.Version`），左侧是运行状态与「● 未保存」标记
- Windows 默认钳制 gamma LUT 范围（防全黑 / 全白）；「设置 › 启动与权限」里提供「解除极端数值限制」（`SystemIntegration.UnlockGammaRange`），写注册表 `GdiIcmGammaRange=256`，需管理员、注销后生效
- 数值被系统拒绝时不在主界面常驻提示：状态栏显示可点击链接（`ShowRampRejected` → `RenderStatus`），点击直接打开「设置 › 启动与权限」并聚焦解除按钮

## 代码约定

- 注释用**简体中文**，`/// <summary>` 写要点
- 中文文案与文档遵循《中文文案排版指北》：
  | 项                         | 口径                                                                                                            |
  | -------------------------- | --------------------------------------------------------------------------------------------------------------- |
  | 中英文之间、中文与数字之间 | 加空格                                                                                                          |
  | 数字与单位之间             | 加空格（`100 ms`、`96 dpi`）；`50%`、`30°` 不加                                                        |
  | 标点                       | 中文全角；破折号 `——`；省略号 `……`                                                                      |
  | 引号                       | 统一 `「」`，嵌套 `『』`，不用 `""` 与 `“”`                                                           |
  | 斜杠                       | 并列项写 `/`（两侧空格）                                                                                      |
  | 专有名词                   | `GammaLift`、`Windows`、`GitHub`、`DDC/CI`、`Gamma LUT`、`GDI32`、`NtUser` 大小写固定             |
  | 不套排版规则               | 代码标识符、文件路径、命令行、URL、行内代码                                                                     |
  | 引用 UI 控件标题           | 用行内代码包裹（UI 原文用半角括号，如 `硬件调节 (DDC/CI)`），正文叙述用全角括号                               |
  | 术语                       | 统一「Gamma LUT」（不写「伽马查找表」）、「预设」（不写「配置」「档案」）、「显示器」（不写「屏幕」「监视器」） |
- 源文件编码：**UTF-8 无 BOM**（若某次编译后中文乱码，给 csc 加 `/codepage:65001`）
- UI 全部手写坐标布局，不用 Designer / `.resx`；控件由 `NewLabel` / `NewButton` / `NewTrack` / `AddRow` / `AddDdcRow` 等工厂方法创建，保持风格统一
- 所有坐标在 `LayoutUi()` 里算，构建代码只创建控件、不写死位置。`AddRow` / `AddDdcRow` 只创建控件并返回标签，位置由 `LayoutGammaInner` / `LayoutDdcInner` / `LayoutProfileInner` 给出；窗口是 `Sizable`，`OnResize` 每次都会重排
- 尺寸统一走 `S(int)`（96 dpi 设计值 × 界面缩放 `_uiScale` × 系统 DPI `_dpiScale`）。`AutoScaleMode` 为 `None`：与 `S()` 自带缩放叠加会双重放大
- 按钮宽度用 `ButtonWidth()`（内部 `TextRenderer.MeasureText`）。`AutoSize=false` 的 Button 上 `PreferredSize` 返回当前尺寸，每次布局会把控件越推越大
- 文案长度随语言变化（内置 16 种语言），按中文宽度估的固定值会在德语 / 俄语下溢出；能算就算，实在要最小值就用 `Math.Max` 兜底，并同步调整 `MinimumSize`
- 软件调节区是 **2/3 : 1/3** 的左右分栏：左边「标签 | 滑块 | 数值框」单行（滑块约 190 px，有数值框就不需要通栏长条），右边曲线预览固定**正方形**。默认紧凑高度下行高 26 px（与 `pnlDdc` 一致）、曲线约 79 px，行高严格 = 曲线边长 / 3；往下拉高窗口时行高与曲线一起长大，曲线最大约 172 px。窗口压到最小时内容块在组内垂直居中
- P/Invoke 声明统一放 `NativeMethods.cs`，业务文件里不散落 `[DllImport]`
- 异常处理风格：底层 API 失败时优雅降级（例如显示器不支持 DDC/CI 时给提示而非抛异常）
- 提交前检查清单见 [CONTRIBUTING.md](CONTRIBUTING.md) 的「提交前自检」
