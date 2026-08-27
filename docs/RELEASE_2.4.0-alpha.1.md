# SciCanvas v2.4.0-alpha.1

SciCanvas `v2.4.0-alpha.1` 是 **Scientific Objects, Multichannel & Reproducible Publishing** 路线的第一阶段预发布，包含 PR1–PR5。它是一个可安装、可验证的阶段成果，不代表 PR6–PR12 已完成。

GUI、CLI 与安装器产品版本为 `2.4.0-alpha.1`，文件版本为 `2.4.0.1`。工程 schema 暂时保持 `2.3`；正式 schema 2.4 migration 安排在 PR12。

## 下载

- [Windows x64 安装器](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.4.0-alpha.1/SciCanvas-v2.4.0-alpha.1-Setup.exe)
- [Windows x64 便携包](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.4.0-alpha.1/SciCanvas-v2.4.0-alpha.1-Portable.zip)
- [SHA-256 校验文件](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.4.0-alpha.1/SciCanvas-v2.4.0-alpha.1-SHA256.txt)

## 阶段成果

### 1. v2.3 correctness closure

- Measurement 以 scientific overlay 身份进入 Figure 和全部导出后端，保留 MeasurementId、源修订、源几何、标定关系和对象样式。
- 16-bit RGB TIFF alpha-channel 组合被明确预检，不再静默生成全白或错误结果。
- Submission Package 使用 staging directory 和原子提交，失败/取消不留下 partial package。
- Legacy migration audit 改为确定性来源。

### 2. 尺寸语义与多尺度标尺

- 比例尺统一绑定源像素尺度与 Panel 几何。
- 单 Panel 支持主比例尺和多个附加比例尺。
- 多尺度标尺进入工程、Undo/Redo、预览与 PNG/TIFF/16-bit/SVG/PDF。

### 3. Canonical Scientific Objects

- Polygon Annotation、Canonical ROI、Direction Marker、Colorbar、Channel Legend 和 Measurement Overlay 进入统一领域/显示/导出链路。
- 适用对象在 SVG/PDF 中保持矢量语义。
- 字体、字号、描边、填充、文字和背景颜色可自定义。

### 4. Scientific Channel Domain

- `ScientificChannelDescriptor` 与 `ChannelDisplaySettings`。
- 类型化 `UInt8` / `UInt16` raw `ImagePlane` 与 `IImagePlaneReader`。
- 科学分析使用 raw plane；pseudocolor、Gamma、Invert、Opacity 和 Composite 只属于显示。
- 确定性 additive composite，不修改原始样本。
- Gray8、Gray16、BGRA32、RGB48 和 RGBA64 精确分量读取。

### 5. Multi-file EDS Groups 与 Channel UI

- `MultiChannelAssetGroup` 保存参考素材及每个通道的 AssetId、ChannelId、FrameIndex、名称、Role、颜色和显示参数。
- 文件名建议必须人工确认；编辑后的名称来源记录为 User。
- 六步 EDS 建组流程和独立 Channels Inspector。
- 支持 Visible、Color、Opacity、Display Min/Max、Gamma 和 Invert。
- 多通道组进入项目 round-trip、JSON 校验、自动恢复和 Undo/Redo。
- 待配准组保持明确状态，不会提前启用错误的跨源同步。

## 科学正确性边界

- SourceAsset 始终只读。
- 不提供生成式修改、对象移除、clone stamp、content-aware fill 或 AI beautification。
- 科学统计不读取 pseudocolor/composite RGB。
- 不自动假设 WPF FrameIndex 等于 OME C/Z/T plane。
- Float32、packed 1/2/4-bit、full OME-TIFF、CZI、LIF、ND2、DM3/DM4 未宣称支持。

## 自动化验证

- v2.3 基线：227 tests。
- PR1–PR5 新增：28 tests。
- 当前：255 passed，0 failed，0 skipped。
- Release solution build：0 warnings，0 errors。
- 覆盖 Core domain、raw plane、16-bit 精度、composite math、scientific object export、EDS workflow、项目 round-trip、Undo/Redo 和 atomic package failure。

## 后续计划

PR6–PR12 将继续完成：

- LinkGroup、Identity/Translation 与跨源 Crop。
- 手工 landmarks、Rigid/Affine、RMS 与 revision validity。
- Polygon ROI propagation 和跨通道 raw statistics。
- 精确 QC location 与旋转/镜像 exact duplicate。
- Journal preset sharing、Font substitution 和 PDF font strategies。
- 全部对象/多通道/配准的 Export + Provenance integration。
- 正式 schema 2.4 migration、文档、CI 和最终发布收口。

完整状态和验收边界见 [v2.4 实施路线图](ROADMAP_2.4.md)。

## 安装

安装器面向 Windows 10/11 x64，默认安装到当前用户 `%LOCALAPPDATA%\SciCanvas`，无需管理员权限；便携包可直接解压运行。两者均包含自包含 .NET 10 Desktop Runtime、GUI 与 `SciCanvas.Cli.exe`。

## 制品校验

- `SciCanvas-v2.4.0-alpha.1-Setup.exe`：192,911,554 bytes。
- `SciCanvas-v2.4.0-alpha.1-Portable.zip`：76,843,856 bytes。
- GUI、CLI、Setup：`ProductVersion 2.4.0-alpha.1`，`FileVersion 2.4.0.1`。
- ZIP 必需项、WPF `zh-Hans` 资源与安装载荷目录：通过。
- 隔离 `%LOCALAPPDATA%` / `%APPDATA%` 安装：通过。
- 已安装 CLI 启动：通过，退出码 0。
- 隔离卸载与快捷方式清理：通过。

```text
2330F00EB102B60493196E3557F486598AE8100ACE0741F3542AA031A3D6D172  SciCanvas-v2.4.0-alpha.1-Setup.exe
B79EC0E5AD42655B491308C961ECF1C754466392720A24842E250F46850935C4  SciCanvas-v2.4.0-alpha.1-Portable.zip
```
