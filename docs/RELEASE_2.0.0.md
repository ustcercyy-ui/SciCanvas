# SciCanvas v2.0.0-alpha

这是 Scientific Figure Workspace V2 的首个预发布版本，适用于 Windows 10/11 x64。GUI、CLI 和安装器文件版本均为 `2.0.0.0`。

## 下载

- [Windows x64 安装器](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.0.0-alpha/SciCanvas-v2.0.0-alpha-Setup.exe)（181,786,814 字节）
- [Windows x64 便携包](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.0.0-alpha/SciCanvas-v2.0.0-alpha-Portable.zip)（65,716,751 字节）
- [SHA-256 校验文件](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.0.0-alpha/SciCanvas-v2.0.0-alpha-SHA256.txt)

## V2 更新

- 新增独立的 Scientific Asset、Figure、Panel 与 Scientific Object 领域模型，同一素材可被多个 Panel 安全复用。
- 以归一化源图 Crop、毫米 Panel Frame 和显式 Source → Panel → Figure 坐标变换隔离科学测量与版面缩放。
- Fit、Fill、Manual Crop 共用裁剪逻辑；有效 DPI 按可见源像素与真实物理尺寸计算。
- Source Tracking 记录素材指纹、尺寸、时间戳、链接状态与 revision；替换源图会显式传播科学对象的有效、无效或待复核状态。
- Project Style 支持 Project → Figure → Panel → Scientific Object 继承、局部覆盖、重置与复制。
- 新增确定性 Scientific Figure QC，检查边界、对齐、间距、字体、有效 DPI、标定、比例尺、标签与源文件完整性。
- Auto Trim 仅提供可复核建议；应用时映射回不可变源像素并作为单步编辑进入撤销历史。
- 工程 schema 升级为 `2.0`，显式迁移 `0.1`、`0.9`、`1.1` 和 `1.2` 工程并写入审计记录。
- WPF 工作区新增 Assets、Figures、Layers、Templates 主导航、可搜索素材库、素材状态徽标、毫米 Panel Frame、Fit/Fill/Manual Crop、替换有效性与可配置 Figure QC。

完整技术说明见 [V2 实现说明](https://github.com/ustcercyy-ui/SciCanvas/blob/v2.0.0-alpha/docs/SCIENTIFIC_FIGURE_WORKSPACE_V2.md)和 [V2 架构审计](https://github.com/ustcercyy-ui/SciCanvas/blob/v2.0.0-alpha/docs/V2_ARCHITECTURE_AUDIT.md)。

## 科研完整性与兼容性

- 源文件仍以只读方式访问，编辑状态只写入工程文件；最终导出继续从已验证的原始像素重建，不使用界面截图。
- 旧版像素变换仍与 V2 毫米/归一化字段同时保存，以兼容既有导出和恢复路径。
- 自动分析和 Auto Trim 不会静默改图；建议必须由用户明确接受后才进入可撤销工程状态。
- 不提供生成式填充、克隆、局部擦除或对象移除。

## 安装与升级

1. 完全关闭正在运行的 SciCanvas。
2. 下载并运行 `SciCanvas-v2.0.0-alpha-Setup.exe`。
3. 安装器会覆盖更新当前用户目录中的版本，无需先卸载，也无需管理员权限。
4. 默认安装目录为 `%LOCALAPPDATA%\SciCanvas`，并创建开始菜单快捷方式。

安装器目前没有商业代码签名，Windows 可能显示 SmartScreen 提示。建议先使用随 Release 提供的 SHA-256 文件核验下载内容。

PowerShell 校验示例：

```powershell
Get-FileHash .\SciCanvas-v2.0.0-alpha-Setup.exe -Algorithm SHA256
Get-FileHash .\SciCanvas-v2.0.0-alpha-Portable.zip -Algorithm SHA256
```

预期结果：

```text
B146EAF52660F8505F142DA1A49DA53BAC2B87C5E9BCCE8910FC44B2B99E6F2F  SciCanvas-v2.0.0-alpha-Setup.exe
988A59063835840163C7B3F3C55DB41CBE8F8E00CDB9B5EBE16B154FF5AD6E07  SciCanvas-v2.0.0-alpha-Portable.zip
```

## 便携版

解压 `SciCanvas-v2.0.0-alpha-Portable.zip` 后，直接运行 `SciCanvas.App.exe`。命令行用户可以在同一目录运行 `SciCanvas.Cli.exe --help`。便携包为自包含版本，不要求系统预先安装 .NET 10。

## 发布验证

- Release 自动化测试 `156/156` 通过，其中 Core 52 项、Windows/WPF 104 项。
- GUI、CLI 和 Setup 文件版本均为 `2.0.0.0`，产品语义版本均为 `2.0.0-alpha`。
- 便携版 CLI 启动与帮助输出验证通过，退出码为 `0`。
- 便携包共 424 个 ZIP 条目，GUI、CLI、安装脚本和发行说明等必需文件均存在。
- 安装器为包含 .NET 10 Windows Desktop 运行时和同版便携负载的单文件自包含构建。
- 安装器与便携包已复算 SHA-256，并与本版本校验文件一致。

V2 工作区视觉概念见 [设计图](https://github.com/ustcercyy-ui/SciCanvas/blob/v2.0.0-alpha/docs/design/scicanvas-v2-workspace-concept.png)。
