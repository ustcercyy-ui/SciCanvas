# SciCanvas v2.4 实施路线图

SciCanvas v2.4 的产品主题是 **Scientific Objects, Multichannel & Reproducible Publishing**。路线图以源文件只读、非破坏处理、raw/display 分离、源像素坐标和物理坐标为科学真值、确定性处理和可审计导出为不可协商的约束。

本文档同时记录已经落地的阶段成果与尚未完成的工作。`v2.4.0-alpha.1` 是 PR1–PR5 阶段性预发布，不代表 v2.4 全部功能完成。

## 总体目标

v2.4 建立四条统一能力：

1. Canonical Scientific Object Foundation。
2. Scientific Multichannel、Linked Views 与 Registration。
3. Reproducible Publishing 与跨机器可移植性。
4. Scientific Integrity 与精确 QC 导航。

目标场景包括 SEM、BSE、HAADF/STEM、EDS 元素分布图、多通道显微图、同视场多模态比较和前后对比。

## 已完成：PR1–PR5

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

## 待完成：PR6–PR12

### PR6：Linked Views + SpatialMapping

- 正式实现 `LinkGroup` 与持久化的 `LinkSyncOptions`。
- 支持不同 `SourceAsset` 之间的 Identity 与 Translation mapping。
- 至少完整同步 Crop、ROI 与 ColorScale；各 Panel 始终保留自己的 `SourceAssetId`。
- 任何同步都不得通过替换目标素材来伪造跨源联动。

### PR7：Registration

- 手工 landmark pairs。
- Translation、Rigid 与 Affine 求解。
- 明确矩阵约定、正反向映射和非共线约束。
- 计算 pixel RMS；存在兼容标定时同时报告物理单位 RMS。
- Mapping 绑定两端 source revision；修订变化后进入 `ReviewRequired`。

### PR8：ROI Propagation

- Polygon ROI 通过 SpatialMapping 映射到其他通道。
- 跨通道 ROI Statistics 必须逐通道读取 raw plane。
- 禁止在 pseudocolor 或 composite RGB 上计算科研统计。

### PR9：Integrity QC

- QC issue location 精确到 Asset、Panel、Object、Measurement、Analysis、Channel、LinkGroup 和 Mapping。
- 增加旋转/镜像 exact duplicate 检测，覆盖 UInt8/UInt16，禁止先降为 8-bit。
- 增加 channel/link/mapping revision、colorbar/channel range 和 registration validity 规则。

### PR10：Publishing Portability

- Journal preset pack 的导入、导出与团队共享。
- 显式 Font substitution UI；requested font 永远不被 fallback 改写。
- PDF font strategy：OutlineText、PreferEmbedded、PreferEmbeddedWithOutlineFallback。

### PR11：Export + Provenance Integration

- 将所有 Scientific Objects、multichannel、registration、font resolution 和 PDF policy 统一接入 GUI/CLI/export provenance。
- Preview 与 Export 使用同一科学参数。
- Derived composite 明确记录 source bit depths、算法、版本和参数，不能冒充原始位深。

### PR12：Schema 2.4、Regression、Docs、Release

- 正式把 project schema 升级到 `2.4`，提供显式、确定性的 2.3 → 2.4 migration。
- 更新 JSON Schema、示例工程、README、架构说明和最终发布说明。
- 完成全量单元、集成、像素、DPI、迁移、CLI、安装/卸载和 Windows CI 回归。
- 发布正式 v2.4 预览版或稳定候选版，并准确列出仍不支持的格式和能力。

## 当前验证基线

- v2.3 基线：227 tests。
- PR1–PR5 新增：28 tests。
- `v2.4.0-alpha.1`：255 passed，0 failed，0 skipped。
- Release solution build：0 warnings，0 errors。

## 当前明确限制

- 尚未实现跨源 LinkGroup/SpatialMapping、Registration、ROI propagation 和跨通道统计。
- 尚未正式升级 schema 2.4；阶段包继续写入 schema 2.3，并为新增字段提供向后兼容默认。
- 不宣称 full OME-TIFF、CZI、LIF、ND2、DM3/DM4、Bio-Formats、GPU 或 AI registration。
- 不提供生成式填充、对象移除、clone stamp、content-aware fill 或 AI beautification。
