**简体中文** | [English](README.en.md)

# GammaLift

提亮暗部，而不毁掉整幅画面。Windows 每显示器 Gamma / 亮度 / 对比度 / 饱和度调节工具。

![GammaLift 主界面](docs/screenshots/main-zh.png)

经典 [Gamma Panel](https://gamma-panel.en.softonic.com/) 的现代复刻版，基于 [GammaPanelX](https://github.com/NiseMonox/GammaPanelX)。每台显示器可独立选择、独立调节。

绿色单文件程序，无需安装，无需 .NET SDK / 运行库（使用 Windows 自带的 .NET Framework 4.x）。

界面内置 16 种语言（简体 / 繁体中文、英语、日语、韩语、德语、法语、西班牙语、巴西葡萄牙语、俄语、意大利语、土耳其语、越南语、印尼语、荷兰语、波兰语），默认跟随系统语言。

## 下载

到 [Releases](../../releases) 页面下载 `GammaLift.exe`，双击即用。需要 Windows 10 / 11 自带的 .NET Framework 4.8；exe 为 AnyCPU 单文件。

SmartScreen 提示：二进制未签名，首次运行 Windows 可能告警。选「更多信息 → 仍要运行」，或核对随 Release 公布的 SHA256，或[自行从源码构建](#构建)。

## 功能

- 每显示器独立调节亮度 / 对比度 / Gamma / 饱和度，互不干扰
- 软件 Gamma LUT 与硬件 DDC/CI 双通道，饱和度走显示器面板
- 预设通过「编辑…」统一调整顺序、重命名或删除；顺序同步到下拉、托盘与循环切换，并随预设一起保存
- 单屏参数分享码：点按钮即复制 11–22 个字符的短码，导入时在弹窗里粘贴，全程离线
- 独占全局热键支持单击 / 双击 / 长按，也可按列表循环切换预设
- 通知总开关，分别控制应用预设与后台运行提示
- 基线保护：重置精确还原系统 ICC / vcgt 校准
- 顶部「帮助 › 关于」集中展示版本号、两行版权、MIT 全文与项目仓库链接
- 便携单文件、16 种界面语言、托盘常驻；最小化可只留在通知区域

详细功能说明、使用提示与技术细节见 [docs/guide.md](docs/guide.md)。

## 使用场景

- 暗场景游戏：《逃离塔科夫》这类游戏把室内和夜图压得很暗，提高 Gamma、亮度只补一点，楼道角落和树影里的轮廓就能分辨出来，高光基本留在原处。游戏或驱动重置 Gamma LUT 时，开启设置里的「定时重新应用」。
- 夜间阅读：深夜看白底文档，把亮度往下拉、Gamma 略压一点，页面不再晃眼。只调放文档的那台显示器，另一台保持原样。
- 欠曝照片：拍暗的片子先临时提 Gamma，看暗部是可用的细节还是一层噪点。照片文件本身不动，看完点重置，显示回到捕获的系统 ICC 校准基线。
- 压制糟糕的视频：老电影和低码率压制常把暗部糊成一块黑，提 Gamma 能把阴影层次拉开，分得清哪些是布景细节，哪些是编码丢掉的色块。
- 两台显示器不一致：一台偏暗、一台偏艳，逐台调亮度和对比度，支持 DDC/CI 的机器还能直接改面板饱和度。设置按显示器硬件 ID 保存，换接口或重新插拔不会错配。
- 多套设置来回切：白天、夜晚、「提亮暗部」各存一套预设，每套记录所有显示器的状态，绑一个 Ctrl+Alt+1 这样的全局热键切换。

GammaLift 只改变显示输出（显卡 Gamma LUT 与显示器 OSD），不读取、不修改任何游戏内存或文件。

## 构建

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
# 产物: build\GammaLift.exe
```

无需安装 .NET SDK，使用 Windows 自带的 `csc.exe` 编译（因此源码保持 C# 5 语法）。

## 血缘与致谢

GammaLift 站在两个肩膀上：

- 经典 [Gamma Panel](https://gamma-panel.en.softonic.com/)。
- [NiseMonox](https://github.com/NiseMonox) 的 [GammaPanelX](https://github.com/NiseMonox/GammaPanelX)。

GammaLift 是 GammaPanelX 的衍生项目。MIT 许可证与两行版权见 [LICENSE](LICENSE)。

## 隐私

GammaLift 不联网、不需要账号、不含广告与分析统计。设置保存在与 `GammaLift.exe` 同目录的 `GammaLiftSettings.json`；该目录不可写时（Program Files / 只读共享）回退到「文档」文件夹下的同名文件。完整说明见 [隐私政策](docs/privacy-policy.md)。

## 许可证

[MIT](LICENSE)