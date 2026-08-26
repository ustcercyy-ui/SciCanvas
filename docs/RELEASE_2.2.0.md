# SciCanvas v2.2.0-alpha

这是 Scientific Figure Workspace V2 的科学分析与自动化预发布版本，适用于 Windows 10/11 x64。本版汇总尚未发布的 v2.0.2 正确性加固、v2.1 原始像素统计分析，并交付 v2.2 的阈值颗粒分析与批处理配方首个端到端切片。GUI、CLI 和安装器文件版本均为 `2.2.0.0`，产品语义版本均为 `2.2.0-alpha`。

## 下载

- [Windows x64 安装器](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.2.0-alpha/SciCanvas-v2.2.0-alpha-Setup.exe)（192,743,614 字节）
- [Windows x64 便携包](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.2.0-alpha/SciCanvas-v2.2.0-alpha-Portable.zip)（76,674,096 字节）
- [SHA-256 校验文件](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.2.0-alpha/SciCanvas-v2.2.0-alpha-SHA256.txt)

## 本次更新

### 科学图像分析 I

- 新增绑定 Source Asset、source revision、frame、channel、bit depth、算法版本和时间戳的 ROI 统计、强度剖面与直方图结果。
- 统一读取原始 8-bit / 16-bit 像素，支持 Luminance、Red、Green、Blue、Alpha 通道；16-bit 结果不经 8-bit 量化。
- 分析表统一导出 CSV / XLSX，工程 schema 升级后仍通过显式迁移兼容旧项目。

### 科学图像分析 II 与自动化

- 辅助区域分析升级为确定性阈值颗粒分析：支持自动 Otsu 与手动阈值、亮/暗前景、最小面积和候选上限。
- 输出颗粒数、面积分数、面积、周长、等效直径、圆度、长宽比、原始平均强度，以及基于凸包和旋转卡尺计算的真实 Feret 最大/最小径。
- 新增可复用颗粒分析配方，可把当前模式、阈值、最小面积、候选上限和通道批量应用到裁剪队列中的多个来源。
- 批处理逐项校验原始来源与 ROI，把结果分别绑定对应 source revision；整个批次作为单个撤销历史手势并写入审计记录。
- 工程 schema 升级为 `2.2`，严格校验颗粒边界、阈值、候选唯一性、原始值范围和 Feret 指标；`2.1` 项目显式迁移并保留既有分析。

### 正确性与交付

- 修复 Figure 全局样式、OME metadata 标定、各向异性角度、像素精确裁剪、格式感知导出预检和 Panel 标签序列问题。
- 新增 Windows CI，覆盖 Release 严格编译、Core 与 Windows/WPF 测试。
- 源图始终只读；分析只产生可审计派生结果，不提供生成式填充、克隆、擦除或对象移除。

## 验证记录

| 检查 | 结果 |
| --- | --- |
| Release 严格编译 | 0 警告、0 错误（`-warnaserror`） |
| Windows/WPF 自动化测试 | 130/130 通过 |
| Core 自动化测试 | 76/76 通过 |
| 发布版 CLI | 便携版与安装版 `SciCanvas.Cli.exe --help` 均返回 0 |
| 便携包结构 | 497 个 ZIP 条目；GUI、CLI、运行时与安装脚本齐全 |
| 隔离安装冒烟 | Setup 返回 0；GUI/CLI 均为 2.2.0.0；快捷方式和卸载脚本生成成功；卸载无残留 |
| 二进制源版本 | `58ba0e1cd438dcf6b6fc584be1cef1c8a625bc53` |

安装冒烟测试通过重定向到系统临时目录的 `LOCALAPPDATA` / `APPDATA` 完成，没有覆盖实际用户安装；验证后临时目录已删除。

## 安装与升级

1. 关闭正在运行的 SciCanvas。
2. 下载并运行 `SciCanvas-v2.2.0-alpha-Setup.exe`。
3. 安装器写入当前用户的 `%LOCALAPPDATA%\SciCanvas`，无需管理员权限。
4. 也可以下载便携包，解压后直接运行 `SciCanvas.App.exe` 或 `SciCanvas.Cli.exe`；便携包已包含 .NET 10 Windows Desktop Runtime。
5. 本预发布安装器尚未进行商业代码签名；Windows SmartScreen 可能显示未知发布者，请先核对下方 SHA-256。

## SHA-256

```text
BCE056665479554C951F77867F58290CE0035460154FAEFCB9F777B00BD5B0CB  SciCanvas-v2.2.0-alpha-Setup.exe
7F4DBCE4753CD31AA8DC72450255F432BCACD88CF58AF282E53B7CD840F6D0FF  SciCanvas-v2.2.0-alpha-Portable.zip
```
