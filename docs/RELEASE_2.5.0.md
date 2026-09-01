# SciCanvas v2.5.0-alpha 发布说明

SciCanvas `v2.5.0-alpha` 在 v2.4 科研组图与可复现出版链路上，交付 **Scientific Data、Registered Imaging 与 Plot Workspace**。产品版本为 `2.5.0-alpha`，文件版本为 `2.5.0.0`；当前工程 writer 使用 schema `3.0`。

这是 Windows 10/11 x64 预览版本。源文件继续保持只读，显示与分析操作不覆写原始科研图像；Figure、Data、Plot、QC、导出和 provenance 使用稳定 ID 与 source revision 建立可验证关系。

## 主要更新

### Scientific Correctness Gate

- 高位深处理区分 raw 与 display，UInt16 管线避免先量化到 8-bit；亮度、对比度、Gamma、黑白点等调整只应用一次。
- `ScientificPlaneRef` 明确资产、revision、frame、channel、位深与尺寸身份；多通道组合和 registration 会验证 plane identity。
- Translation、Rigid 与 Affine registration 使用显式矩阵和可追溯重采样；越界像素、插值和输出尺寸不再隐式猜测。
- Canonical ROI 使用统一像素几何和严格边界验证；ROI 传播、统计与 Figure 投影保留来源映射和 revision。
- Figure QC、保存、CLI、导出与投稿包共用统一科学 QC 协调层，避免不同入口得到互相矛盾的判断。
- PDF 对许可允许且可可靠映射的 TrueType / OpenType TrueType 执行实际字形子集嵌入；严格策略会阻止不可靠字体，偏好策略记录原因后使用文字轮廓。

### Scientific Data

- `TabularDataAsset` 保存稳定资产/列 ID、类型化单元格、单位、列角色、只读来源指纹、source revision 与完整导入选择。
- CSV/TSV 支持严格 UTF-8、RFC 引号和自动分隔符检测；XLSX 直接读取 OOXML，可选择工作表、A1 范围与表头行，不依赖 Excel。
- 导入必须经过 Preview → Confirm；确认时重新读取并验证 SHA-256，来源变化时拒绝创建资产。
- 只有表格而没有图像的工程同样参与脏状态、保存、恢复和自动恢复。

### Plot Workspace

- 原生支持 Line、Scatter、Line + Symbol、Error Bar、Histogram、Box Plot 与 Heatmap。
- 轴、刻度、图例、标注、线型和 marker 样式全部进入类型化模型与工程持久化。
- Filter 和 ordered transforms 可复算并进入 provenance；每个投影点保留 source row index，原始 DataAsset 不被修改。
- Log 轴非正数据、非法误差范围、过期 revision、外部列与被篡改的过滤统计会明确阻止保存或导出，不会静默丢点。

### Plot → Figure

- 已保存 Plot 可成为 Figure 原生 Panel，并保存 Plot/DataAsset/revision 引用和冻结的类型化投影快照，不持久化截图。
- Plot Panel 支持选择、拖动、缩放、锁定、层级、编号、撤销/重做以及 Project → Figure → Panel → Plot Object 样式继承。
- PNG、8-bit TIFF 与 16-bit TIFF 使用高质量栅格路径；SVG 输出原生图元；PDF 输出 path/text operators，不嵌入 Plot 位图。
- GUI、CLI、Preflight、投稿包和 provenance 消费同一 `FigurePlotPanelExportItem` contract。

### 工程与架构

- 工程迁移链覆盖历史版本，并逐步引入 DataAsset、Plot、filter/transforms 与 Figure Plot Panel；schema `3.0` 保存完整原生状态。
- 主 ViewModel 的 QC、导出、投稿包、工程 I/O 与科学分析已拆分为协调器；Figure Panel/Object/Link 状态和 Inspector/Layers/Data/Plot 页面也已拆分为独立模块。
- 辅助区域与颗粒分析取消 1000 条候选截断；超过资源政策上限时明确失败，不把部分结果伪装成完整结果。

## 验证

- Core Tests：`188/188` passed。
- Windows/WPF Tests：`276/276` passed。
- Total：`464/464` passed，`0` failed，`0` skipped。
- `dotnet build SciCanvas.sln --no-restore`：`0` warnings，`0` errors。
- Windows x64 self-contained Release build：成功。

## 下载与校验

- `SciCanvas-v2.5.0-alpha-Setup.exe`：Windows x64 当前用户安装器，同时安装 GUI 与 CLI。
- `SciCanvas-v2.5.0-alpha-Portable.zip`：自包含便携包，无需预装 .NET。
- `SciCanvas-v2.5.0-alpha-SHA256.txt`：上述两个文件的 SHA-256 校验值。

本地验收制品：

```text
SciCanvas-v2.5.0-alpha-Setup.exe     193460414 bytes
SHA-256 51AD044BE631AB21F25735A65551B49FF02E94F548117B09FFC3391D22489FF3

SciCanvas-v2.5.0-alpha-Portable.zip   77362288 bytes
SHA-256 A9A17CB4EBB9F9EA7CFFD196CC2DE3A5282A9D67139AF9913ED5C403F177BC79
```

安装器与便携包均为 self-contained `win-x64`。安装器无需管理员权限，默认安装到当前用户目录。当前制品未使用代码签名证书，Windows SmartScreen 可能显示未知发布者提示。

## 已知边界

- 不宣称完整 OME-TIFF、CZI、LIF、ND2、DM3、DM4 或 Bio-Formats 支持。
- 不猜测未保存的 interleaved RGB component，也不把显示代理冒充原始通道平面。
- 本版本不提供 3D volume rendering、AI registration、EBSD processing engine、TEM 自动晶体索引、完整 Origin/Excel 替代或复杂 nonlinear fitting suite。
- Plot 目前聚焦可审计的二维科研绘图与 Figure 集成；高级拟合和更多专业图型留待后续版本。
