# SciCanvas v2.4 实施路线图

SciCanvas v2.4 的产品主题是 **Scientific Objects, Multichannel & Reproducible Publishing**。路线图以源文件只读、非破坏处理、raw/display 分离、源像素坐标和物理坐标为科学真值、确定性处理和可审计导出为不可协商的约束。

本文档记录 PR1–PR12 的最终实施结果。`v2.4.0-alpha` 已完成全部路线图阶段，工程 schema 为 `2.4`；各能力边界和验证结果以本文及 [最终发布说明](RELEASE_2.4.0.md) 为准。

## 总体目标

v2.4 建立四条统一能力：

1. Canonical Scientific Object Foundation。
2. Scientific Multichannel、Linked Views 与 Registration。
3. Reproducible Publishing 与跨机器可移植性。
4. Scientific Integrity 与精确 QC 导航。

目标场景包括 SEM、BSE、HAADF/STEM、EDS 元素分布图、多通道显微图、同视场多模态比较和前后对比。

## 已完成：PR1–PR12

### PR1：v2.3 correctness closure

- Measurement 以保留 `MeasurementId`、源素材、源修订、源几何、标定关系和独立样式的 scientific overlay 进入 Figure。
- Measurement overlay 进入 PNG/TIFF/16-bit TIFF/SVG/PDF；SVG/PDF 尽量保持矢量语义。
- 16-bit RGB TIFF 的 alpha-channel 不再静默输出错误结果，预检会明确阻止不支持的组合。
- Submission Package 改为 staging → 完整生成 → 原子提交；失败和取消不留下 partial package。
- Legacy migration audit 使用确定性时间来源，相同输入产生相同语义结果。
- 跨素材同步没有复用旧 `CropLinkGroupId` 的同源替换语义。

### PR2：尺寸语义与多尺度标尺

- 比例尺语义统一绑定源像素尺度和 Panel 几何。
- 一个 Panel 支持多个独立比例尺，并保存长度、单位、锚点、标签和可见状态。
- 主比例尺和附加比例尺进入工程、Undo/Redo、预览、栅格、16-bit、SVG 与 PDF 导出。
- 对无效标定、超出 Panel 宽度和不一致单位进行确定性验证。

### PR3：Canonical Scientific Objects

- 建立 Polygon Annotation、Canonical ROI、Direction Marker、Colorbar、Channel Legend 和 Measurement Overlay 的 Core/Presentation/Export 链路。
- Polygon 与 ROI 保存语义几何，不把屏幕坐标冒充源像素坐标。
- Direction Marker 支持独立线条、箭头、文字、字体与颜色。
- Colorbar 与 Channel Legend 支持可自定义字体、颜色、描边、背景和通道条目。
- Scientific Objects 进入工程 round-trip、Undo/Redo、PNG/TIFF/16-bit/SVG/PDF；SVG/PDF 中的适用对象保持矢量。

### PR4：Scientific Channel Domain

- 新增 `ScientificChannelDescriptor`、`ChannelDisplaySettings`、类型化 `ImagePlane` 和 `IImagePlaneReader`。
- `UInt8` 与 `UInt16` raw samples 保持真实位深；科学分析只读取 raw plane。
- 颜色、窗口范围、Gamma、Invert、Opacity 和 Composite 只属于显示层。
- 新增确定性的 additive composite：逐通道归一化、Gamma、颜色和透明度后相加并 clamp 到 0–1。
- WPF reader 支持 Gray8/Gray16/BGRA32/RGB48/RGBA64 的精确分量读取。
- 多帧必须显式指定 `FrameIndex`；不猜测 WPF Frame 与 OME C/Z/T 的关系。
- Float32、packed 1/2/4-bit 和无法证明的 OME plane mapping 未宣称支持。

### PR5：MultiChannel Asset Groups

- 新增 `MultiChannelAssetGroup`、`ChannelGroupMember` 与 `ChannelNameOrigin`。
- 多文件 EDS 组保存参考素材、AssetId、ChannelId、FrameIndex、名称、Role、颜色和完整显示参数。
- 文件名只作为 `FilenameSuggestion`；必须经用户确认。用户改写名称后来源切换为 `User`。
- 新增独立 `MultiChannelWorkspaceViewModel` 和右侧 Channels Inspector。
- 实现六步 EDS workflow：参考图、元素图、名称确认、颜色确认、同视场/待配准决策、建组。
- 默认颜色按通道顺序提供，不把具体元素硬编码到固定颜色。
- 多通道组进入项目 JSON 校验、保存/打开、自动恢复和 Undo/Redo。
- 待配准组可保存，但在 SpatialMapping 完成前不会启用跨源联动。

### PR6：Linked Views + SpatialMapping

- 新增独立 `SciCanvas.Core.Linking` 模型：`LinkGroup`、flags 形式的 `LinkSyncOptions`、`SpatialMapping`、`SpatialMatrix3x3` 与映射溯源。
- Matrix 明确使用 row-major 和 `TargetPoint = M × SourcePoint`；支持 Identity、Translation，并为 Rigid/Affine 保留经过验证的核心表达。
- 不同 `SourceAsset` 的 Panel 可通过用户声明的 Identity 或 Translation 同步 half-open Crop；矩形按四角映射后取确定性 bounding box。
- Crop 与 ColorScale 同步始终保留每个 Panel 原本的 `SourceAssetId`；运行时与测试均阻止旧 `ReplaceSource` 语义用于跨源同步。
- Mapping 保存两端 AssetId、source revision、类型、矩阵、origin、创建时间和 residual；revision 变化后停止同步并要求复核。
- LinkGroup 进入工程 JSON 校验、可选 2.3 扩展 schema、手动/自动保存、恢复与原子 Undo/Redo。
- 新增独立 Linked Views Inspector，可查看成员与溯源、切换 Crop/ROI/ColorScale 语义、编辑 Translation 偏移并重置 Identity。
- ROI 同步选项与 point mapping primitive 已就绪；Polygon ROI 实例传播和逐通道 raw statistics 仍按 PR8 完成，不把当前 canvas geometry 冒充 source-pixel ROI。
- 同期移除辅助区域/颗粒分析的 1000 条候选截断；旧 `maximumCandidates` 仅兼容读取，所有满足阈值与最小面积条件的连通区域均返回。

### PR7：Registration

- 新增独立 `RegistrationWorkspaceViewModel` / `RegistrationWorkspace.xaml`，可逐行录入 `sourceX,sourceY -> targetX,targetY` landmark pairs。
- Translation 使用最小二乘平均位移，Rigid 使用二维无缩放旋转 + 平移求解，Affine 使用至少 3 个不共线点的最小二乘求解；退化输入明确报错。
- 全部矩阵统一为 row-major `TargetPoint = M × SourcePoint`，Rigid 额外验证正交性和 `det=+1`，正反向映射保持可逆。
- Mapping 持久化 landmarks、逐点 residual、pixel RMS、可用目标标定下的物理 RMS/单位，以及两端 source revision。
- revision 变化进入 `ReviewRequired`，产生 `mapping-revision-stale` assessment，并停止 linked crop、ROI propagation 与 analysis；重新求解或确认会绑定当前 revision。
- Registration provenance 进入 JSON schema、工程保存/打开、自动恢复和 Undo/Redo；Linked Crop 可立即使用新 Rigid/Affine 矩阵。

### PR8：ROI Propagation

- `RoiObject` 正式成为绑定 `AssetId`、`SourceRevision`、`FrameIndex`、`RoiGeometryKind`、source-pixel geometry 和 canonical style 的 ROI 模型。
- 新增独立 `RoiPropagationWorkspaceViewModel` / `RoiPropagationWorkspace.xaml`；reference Polygon 的每个顶点通过 SpatialMapping 映射到目标素材，不用 bounding rectangle 代替 polygon geometry。
- 每个 target ROI 持久化 `ReferenceRoiId`、`TargetRoiId`、`LinkGroupId` 与 `MappingId`；工程保存/打开、自动恢复和 Undo/Redo 保留完整关系。
- Polygon mask 使用确定性的 even-odd point-in-polygon，按 `(x+0.5, y+0.5)` pixel center 取样并包含边界，只统计 polygon 内像素。
- `ROI Statistics Across Channels` 为每个 `ChannelGroupMember` 构造显式 raw plane request，保持 UInt8/UInt16 位深，保存 RoiId、ChannelId、LinkGroup/Mapping provenance。
- 统计路径不读取 pseudocolor、display range、Gamma、Opacity 或 composite RGB；无法证明 component index 的 interleaved RGB 不做猜测并明确拒绝。
- revision stale 会在传播和跨通道分析前阻断，避免静默复用过期 registration。

## 最终阶段：PR9–PR12

### PR9：Integrity QC

- QC issue location 已精确到 Asset、Panel、Object、Measurement、Analysis、Channel、LinkGroup 和 Mapping，并进入可导航工作区。
- 旋转/镜像 exact duplicate 检测覆盖 UInt8/UInt16，直接比较 raw samples，禁止先降为 8-bit。
- 已增加 channel/link/mapping revision、colorbar/channel range 和 registration validity 规则。

### PR10：Publishing Portability

- Journal preset pack 已支持导入、导出、团队共享和独立 schema 校验。
- Font substitution UI 显式保存 Requested/Substitute；requested font 永远不被 fallback 改写。
- PDF font strategy 已提供 OutlineText、PreferEmbedded、PreferEmbeddedWithOutlineFallback；当前 writer 的可靠输出为文字轮廓，无法保证子集嵌入与 ToUnicode 时严格阻断或带原因回退。

### PR11：Export + Provenance Integration

- 不可变 `FigureExportDocument` 已统一承载 Scientific Objects、Measurement Overlays、Polygon points 和 multichannel layer items；exporter 不读取 ViewModel。
- Panel composite 由各通道 raw UInt8/UInt16 plane、source revision、crop、frame、selector 和 display settings 确定性重建；旧单源导出保持兼容。
- Provenance 已覆盖 channel、registration、ROI propagation、colorbar/legend、font resolution/substitution 和 PDF 实际策略；derived composite 不冒充原始位深。

### PR12：Schema 2.4、Regression、Docs、Release

- Project schema 已升级到 `2.4`，保留全部历史版本读取，并提供显式、确定性、幂等的 2.3 → 2.4 migration。
- JSON Schema、README、路线图与最终发布说明已同步，完整迁移 fixture 保留颜色、字体、测量样式、Panel Label、比例尺、裁剪和投稿设置。
- 本地 Release solution build 与完整单元、集成、像素、迁移和桌面工作区回归已通过；安装包与远端 CI 结果记录在最终 release 页面。
- 产品版本为 `2.4.0-alpha`、文件版本为 `2.4.0.0`，并准确列出仍不支持的格式和能力。

## 当前验证基线

- v2.3 基线：227 tests。
- PR1–PR5 新增：28 tests。
- `v2.4.0-alpha.1`：255 passed，0 failed，0 skipped。
- `v2.4.0-alpha.2`：256 passed，0 failed，0 skipped；新增右侧标签页互斥显示与滚动回归。
- PR6 当前 main：266 passed，0 failed，0 skipped；包含 1089 个连通区域全部返回、跨源 Identity/Translation、修订失效、SourceAsset 不变、JSON 往返、Undo/Redo 与四页互斥 UI 回归。
- PR7–PR8 当前 main：281 passed，0 failed，0 skipped；Core 111 + Windows 170，覆盖 Translation/Rigid/Affine、退化输入、像素/物理 RMS、mapping-revision-stale、Affine Polygon propagation、10×10 polygon mask、逐通道 raw plane、工程往返、Undo/Redo 与六页互斥 UI 回归。
- PR9–PR12 最终本地 Release：306 passed，0 failed，0 skipped；Core 129 + Windows 177，新增 exact duplicate/QC、preset/font/PDF portability、真实 UInt16 composite、GUI/CLI 统一 provenance、schema 2.4 与完整 2.3 migration fixture 回归。
- Release solution build：0 warnings，0 errors。

## 当前明确限制

- 跨通道统计当前要求每个 `ChannelGroupMember` 能证明为单通道 ExternalAsset/FramePlane；尚未在 group 模型中保存任意 interleaved RGB component index，因此不会猜测 RGB 分量。
- 当前 composite renderer 要求映射后的 raw layer 尺寸一致；不宣称任意 affine warp 已完成像素级重采样合成。
- 内置 PDF writer 当前以文字轮廓为可靠路径；尚未实现可验证的字体子集嵌入与 ToUnicode 映射。
- 不宣称 full OME-TIFF、CZI、LIF、ND2、DM3/DM4、Bio-Formats、GPU 或 AI registration。
- 不提供生成式填充、对象移除、clone stamp、content-aware fill 或 AI beautification。
