# SciCanvas v2.4.0-alpha 发布说明

SciCanvas `v2.4.0-alpha` 完成了 2.4 路线图的 PR1–PR12。该版本围绕 **Scientific Objects、Multichannel、Registration、Integrity QC 与 Reproducible Publishing** 建立了一条从工程模型、编辑工作区、持久化、预检到导出溯源的统一链路。

这是预览版本。项目 schema 已正式升级到 `2.4`，产品版本为 `2.4.0-alpha`，文件版本为 `2.4.0.0`。

## 主要更新

### Canonical Scientific Objects

- Measurement Overlay 保存对象 ID、源素材、源修订、源几何、标定和完整样式，并进入预览、工程、历史、栅格、SVG、PDF 与 provenance。
- 一个 Panel 可保存多个独立比例尺；长度、单位、锚点、标签、可见性和样式均可独立设置。
- Polygon Annotation、Canonical Polygon ROI、Direction Marker、Colorbar 与 Channel Legend 使用明确的科学对象模型，不再把屏幕坐标冒充源像素坐标。
- 科学对象、比例尺、Panel Label、字体、颜色、描边、填充和裁剪在保存、打开、Undo/Redo 与 2.3→2.4 迁移中保持视觉语义。

### Multichannel 与 raw/display 分离

- 新增 `ScientificChannelDescriptor`、`ChannelDisplaySettings`、类型化 `ImagePlane` 与多通道 Asset Group。
- UInt8/UInt16 原始平面保持真实位深；科研统计只读取 raw plane。伪彩色、显示范围、Gamma、Opacity 和 Composite 仅属于显示层。
- 多文件 EDS 工作流保存通道名称来源、颜色、范围、Gamma、可见性、混合方式、FrameIndex 与 source revision。
- Panel 可显式绑定一个多通道组；导出时由不可变 channel-layer export items 重建组合图，并保留旧单源 Panel 路径。
- 当前确定性 additive composite 支持等尺寸、可证明的原始 UInt8/UInt16 平面。无法证明分量索引的 interleaved RGB 输入会明确拒绝，而不是猜测。

### Linked Views、Registration 与 ROI Propagation

- 跨素材 `LinkGroup` 使用 Identity、Translation、Rigid 或 Affine `SpatialMapping`，矩阵统一采用 row-major `TargetPoint = M × SourcePoint`。
- 手工 landmark registration 保存源/目标点、逐点 residual、像素 RMS、可用时的物理 RMS、两端 source revision 和 mapping origin。
- 当任一源修订变化时，mapping 进入 stale/review-required 状态，并阻断 linked crop、ROI propagation 和跨通道统计。
- Canonical Polygon ROI 按顶点传播，不用 bounding rectangle 代替几何；polygon mask 按像素中心和 even-odd 规则确定性取样。
- ROI statistics 为各通道创建显式 raw-plane request，并保存 RoiId、ChannelId、LinkGroupId 与 MappingId。

### Scientific Integrity QC

- QC location 可定位 Asset、Panel、Object、Measurement、Analysis、Channel、LinkGroup 与 Mapping。
- 新增多通道 source/revision、显示范围、colorbar、registration 可用性和 stale mapping 规则。
- 精确重复检测覆盖原图、90°/180°/270° 旋转和镜像，并直接比较 UInt8/UInt16 原始样本，不先降为 8-bit。
- 辅助区域/颗粒分析不再把候选数量截断为 1000；所有满足阈值和最小面积条件的连通区域都会返回。

### 可移植出版

- Journal preset pack 可导入、导出和共享，并通过独立 JSON schema 校验。
- Font substitution 明确保留 `RequestedFont` 与 `SubstituteFont`；缺失字体不会静默改写工程样式。
- PDF 提供 `OutlineText`、`EmbedSubsetWhenPermitted` 与 `PreferEmbeddedWithOutlineFallback` 策略和嵌入权限检查。
- 当前内置 PDF writer 的可靠路径是文字转轮廓。它尚未实现可验证的字体子集嵌入与 ToUnicode 映射：严格嵌入会被预检阻止，`PreferEmbeddedWithOutlineFallback` 会给出原因并回退为轮廓。发布说明不宣称字体已被嵌入。

### Export 与 Provenance

- 栅格、16-bit、SVG、PDF 与投稿包统一消费不可变 `FigureExportDocument`；exporter 不读取 ViewModel。
- Provenance 记录 Panel/source/revision/crop/frame、Scientific Object、Measurement、channel group/channel/display settings、registration、ROI propagation、colorbar/legend、字体解析与 PDF 实际策略。
- 多通道派生图明确记录各 channel source、source revision、bit depth、display range、Gamma、颜色、Opacity、render/blend mode；不会冒充原始科学位深。

## Schema 2.4 与迁移

- 当前 writer 输出 schema `2.4`，并继续读取所有历史版本。
- 2.4 持久化 Scientific Objects、Asset Groups、Link Groups、Spatial Mappings、Font Substitutions、Journal Preset Snapshots、Channel Display Settings 与 Composite Panel 绑定。
- 2.3→2.4 migration 是显式、确定性且幂等的；保留 annotation 颜色/字体、measurement 样式、Panel Label、比例尺、裁剪、export profile 和 submission settings。
- 缺失的 channel source revision 会从对应 source 的已保存 revision 确定性补齐，并写入稳定 migration audit。
- 旧单源 Panel 继续使用原导出路径，不要求创建 channel group。

## 验证结果

- `dotnet build .\SciCanvas.sln --configuration Release --no-restore -warnaserror`
  - 0 warnings，0 errors。
- `dotnet test .\SciCanvas.sln --configuration Release --no-build --no-restore`
  - Core：129 passed。
  - Windows：177 passed。
  - 合计：306 passed，0 failed，0 skipped。
- 覆盖项目往返、2.3 完整迁移 fixture、UInt16 composite 像素、GUI/CLI 统一导出契约、registration、ROI propagation、无限候选颗粒分析、Integrity QC、字体替换、PDF policy 与 provenance。

上述结果为本地 Windows Release 验证；远端 GitHub Actions 状态以对应 release/tag 页面为准。

## 发布制品（2026-08-29）

- `SciCanvas-v2.4.0-alpha-Setup.exe`：193,099,966 bytes；SHA-256 `A3A5100C27BF0804BC1DFD7CFC82C2AF4D7D9DB498A2175A357C7ED013E69CFA`。
- `SciCanvas-v2.4.0-alpha-Portable.zip`：77,004,072 bytes；SHA-256 `78B9B55FE8792E259D5CE922A0E202A7AC09B6D05A06BF48400A7DA7265A4A8C`。
- `SciCanvas-v2.4.0-alpha-SHA256.txt`：包含上述两个下载文件的独立校验值。
- Setup、GUI、CLI 的 ProductVersion 均为 `2.4.0-alpha`，FileVersion 均为 `2.4.0.0`；便携包包含 495 个条目。

## 当前明确限制

- 不宣称完整 OME-TIFF plane mapping，也不原生支持 CZI、LIF、ND2、DM3/DM4 或 Bio-Formats。
- 尚未在 channel group 中保存任意 interleaved RGB component index；无法证明的分量不会参与科研统计。
- 当前 composite renderer 要求导出层映射后尺寸一致；不把未经重采样验证的任意 affine warp 描述为已完成的像素级配准合成。
- PDF 字体子集嵌入与 ToUnicode 尚未实现；可靠路径为文字轮廓。
- 不提供生成式填充、对象移除、clone stamp、content-aware fill、GPU registration 或 AI registration。

## 安装

- Setup：运行 `SciCanvas-v2.4.0-alpha-Setup.exe`，按当前用户安装 GUI 与 CLI。
- Portable：解压 `SciCanvas-v2.4.0-alpha-Portable.zip` 后运行 `SciCanvas.App.exe` 或 `SciCanvas.Cli.exe`。
- 使用同一发布页中的 `SciCanvas-v2.4.0-alpha-SHA256.txt` 校验下载文件。

完整阶段拆分和验收记录见 [v2.4 路线图](ROADMAP_2.4.md)。
