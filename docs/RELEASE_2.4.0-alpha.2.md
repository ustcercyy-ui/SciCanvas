# SciCanvas v2.4.0-alpha.2

SciCanvas `v2.4.0-alpha.2` 是基于 `v2.4.0-alpha.1` 的 Windows 界面热修订。它不改变科学数据模型、工程 schema、源文件只读边界或 PR1–PR5 的能力范围，主要修复右侧工作区的标签页叠层与深色主题显示问题。

GUI、CLI 与安装器产品版本为 `2.4.0-alpha.2`，文件版本为 `2.4.0.2`。工程 schema 继续保持 `2.3`。

## 下载

- [Windows x64 安装器](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.4.0-alpha.2/SciCanvas-v2.4.0-alpha.2-Setup.exe)
- [Windows x64 便携包](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.4.0-alpha.2/SciCanvas-v2.4.0-alpha.2-Portable.zip)
- [SHA-256 校验文件](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.4.0-alpha.2/SciCanvas-v2.4.0-alpha.2-SHA256.txt)

## 本次修复

### 右侧标签页叠层

- 修复 `ChannelsInspector` 设置独立 `DataContext` 后错误解析 `ChannelsTabVisibility` 的问题。
- “检查器”“图层”“通道”现在严格互斥显示；切换检查器或图层时，不再叠加 `Scientific Channels` 标题、科研约束卡片或空通道列表。
- 标签按钮增加稳定名称，并通过实际绑定命令验证完整切换路径。

### 深色主题滚动条

- 为垂直和水平滚动条补充深色轨道与滑块模板，移除深色侧栏右侧的系统浅色竖带。
- 保留滑块拖动、轨道翻页、鼠标悬停和拖动反馈。

### 回归覆盖

- 新增右侧栏标签页互斥显示测试，覆盖“检查器 → 图层 → 通道 → 检查器”。
- 验证检查器仍可滚动，并覆盖常见高 DPI 逻辑视口。
- 全量自动化测试：`256 passed，0 failed，0 skipped`。
- `MainWindowImportRegressionTests`：`24 passed，0 failed，0 skipped`。

## 科学正确性边界

- 本次修复只影响 WPF 可见性绑定、主题资源和 UI 回归测试。
- SourceAsset、raw plane、标定、测量、分析、Figure 和导出逻辑均未修改。
- 源图继续保持只读；不会修改任何原始科研图像或元数据。

## 安装

安装器面向 Windows 10/11 x64，默认安装到当前用户 `%LOCALAPPDATA%\SciCanvas`，无需管理员权限。安装包内含 GUI、CLI 与自包含 .NET 10 Desktop Runtime。

若已安装 `v2.4.0-alpha.1`，直接运行 `v2.4.0-alpha.2` 安装器即可覆盖更新应用文件。便携包用户请解压到新的目录，避免与旧版本文件混用。

## 制品校验

- `SciCanvas-v2.4.0-alpha.2-Setup.exe`：`192,915,650` bytes。
- `SciCanvas-v2.4.0-alpha.2-Portable.zip`：`76,845,364` bytes。
- GUI、CLI、Setup：`ProductVersion 2.4.0-alpha.2`，`FileVersion 2.4.0.2`。
- ZIP 必需项与安装载荷目录：通过。
- 隔离 `%LOCALAPPDATA%` / `%APPDATA%` 安装、CLI 启动和卸载：通过；退出码均为 0，卸载后无残留。

```text
C3F4BBEF4AB02C074D243758BC12CEE04172527EA0982FEB0C911454C17BD9C2  SciCanvas-v2.4.0-alpha.2-Setup.exe
8CAA1523E4B4584DDD35E9B0CF8D4A6BD9176DF267C5685481EC763D996C859E  SciCanvas-v2.4.0-alpha.2-Portable.zip
```
