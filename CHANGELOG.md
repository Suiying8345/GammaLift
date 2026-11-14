# 变更日志

本文件记录 GammaLift 值得注意的变更，格式遵循 Keep a Changelog，版本号遵循语义化版本。

## [Unreleased]

## [1.0.0] - 2026-11-14

### 新增

- 每台显示器独立调节亮度 / 对比度 / Gamma / 饱和度，互不影响
- 软件 Gamma LUT 与硬件 DDC/CI 双通道；饱和度通过显示器面板调整
- 基线保护：重置精确还原系统 ICC / vcgt 校准
- 预设管理：新建、保存，「编辑…」对话框统一调整顺序、改名与删除，顺序同步到下拉列表、托盘菜单与循环切换
- 单显示器参数分享码：11–22 个字符，离线编解码
- 独占全局热键：单击 / 双击 / 长按，支持按列表循环切换预设
- 曲线预览、多显示器识别、置顶提示窗
- 16 种界面语言，默认跟随系统
- 便携单文件，关闭 / 最小化可收进托盘

### 说明

- 首个公开发布版本，运行需要 Windows 10 / 11 自带的 .NET Framework 4.x
- 二进制未签名，首次运行可能出现 SmartScreen 提示；可核对 Release 公布的 SHA256，或从源码自行构建

[Unreleased]: https://github.com/Suiying8345/GammaLift/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Suiying8345/GammaLift/releases/tag/v1.0.0