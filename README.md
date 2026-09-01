# SciCanvas（暂定名）

SciCanvas 是一款面向材料科学与实验研究人员的 Windows 本地图像裁剪、拼接和论文组图软件。它以像素准确、非破坏编辑、源文件保护和可追溯导出为首要原则。

当前仓库已经包含可运行的 WPF 工作台、源图级 X/Y 标定、科学测量与统计、统一科研对象样式、Figure QC、可审计辅助分析、投稿包、CLI 和 Windows 安装器。

## 当前可用

- 从 Windows 文件选择器批量导入 TIFF、PNG、JPEG、BMP；读取过程使用只读文件流。
- 自动读取像素尺寸、位深/通道、DPI 和多页帧数等基础元数据，并计算 SHA-256 与 Windows 文件标识。
- 在源图像像素坐标中拖拽新建裁剪框、拖动已有裁剪框，使用八个边角/边中手柄调整大小，或直接输入整数 `X / Y / 宽 / 高`。
- 切换不同源图像时可锁定裁剪宽高，分别自由调整每张图的裁剪位置。
- 将裁剪框左对齐、水平居中、右对齐、上对齐、垂直居中或下对齐到源图像。
- 源图与拼版画布均支持滚轮缩放、按钮缩放、适合窗口、`1:1` 和 Space/中键平移；视图缩放不改变任何像素坐标或导出结果。
- 测量、拼版面板、科研标注和参考线都作为可选图层进入统一图层页；可从画布或列表选中、显隐和锁定，`Delete` 删除当前未锁定对象。源图层始终只读锁定。
- 导出当前裁剪为 TIFF、PNG、BMP 或 JPEG；导出前重新验证源文件，且只写入全新文件。
- 核心层已实现源文件变化验证与导出路径防覆盖策略，编码器也使用 `CreateNew` 拒绝覆盖已有文件。
- 将当前裁剪作为非破坏面板加入论文拼版，不生成中间修改图。
- 内置 6 套材料科研组图模板，新增“储能电化学 · 六面板证据链”“物相—结构—机理 · 六面板”和“力学性能—断口 · 五面板”。
- 可在空拼版中切换模板；工程文件记录模板 ID，重新打开时自动恢复对应模板。
- 模板按期刊物理尺寸生成 300 dpi 画布，并检查每个面板的有效 DPI。
- 拼版面板可自由拖动、显示/隐藏、锁定、调整图层顺序或移除。
- 选中面板可输入宽高、按百分比等比缩放；默认锁定源图比例，也可解除锁定后自由调整宽高。
- 选中面板可一键左对齐、水平居中、右对齐、上对齐、垂直居中或下对齐到最终画布。
- `Ctrl+单击` 可多选面板，也可在图层列表中扩展选择；选择组可整体拖动并统一限制在画布边界内。
- 多选面板可按左边、中心、右边、上边、垂直中心或下边相互对齐；至少 3 个未锁定面板可按两端位置进行水平或垂直等距分布。
- 锁定面板可以作为多选对齐参照，但不会被拖动或对齐命令修改；也可一键全选或取消选择。
- 可添加水平/垂直参考线，输入精确画布像素位置并锁定；参考线只属于编辑器辅助层，不进入最终导出图。
- 面板拖动可吸附到画布边缘/中心、参考线以及其他可见面板的边缘/中心，吸附阈值可在 1–100 px 间设置或完全关闭。
- 多选面板可输入精确的相邻边界间距，并从最靠左或最靠上的面板开始应用水平/垂直排版；超出画布时阻止命令。
- 主窗口的源图像栏、画布栏和检查器栏可通过两条拖拽分隔条独立调整宽度；右侧检查器保留滚动区域。
- 选中拼版面板时显示青色右下角缩放手柄；选中测量时显示黄色端点手柄；拖动即可调整大小，锁定图层时禁止移动、缩放和删除。
- 顶部文件、编辑、视图、图像、裁剪、拼版和工具菜单均绑定到实际命令；右侧“检查器 / 图层”页签可切换，图层页支持显隐、锁定、选择、排序和移除。
- 亮度与对比度支持 `-100—+100` 连续滑块无极调节，并与数字输入框双向同步。
- 画布背景支持 `#RRGGBB` / `#AARRGGBB` 自定义颜色或透明背景，预览、工程恢复、撤销和最终导出使用同一颜色值。
- 面板编号可自动生成小写字母、大写字母或数字序列，也可关闭自动编号后手工编辑，并按画布阅读顺序一键重新编号。
- 每个面板可输入“物理尺寸/源像素”、比例尺长度和单位（nm、µm、mm 或自定义单位），预览并导出经过校准的比例尺。
- 比例尺长度超过裁剪宽度 80% 或校准值无效时阻止导出；软件不会猜测缺失的物理尺度。
- 可添加文字、箭头、矩形和椭圆科研标注，并直接在画布拖动；每个标注可显示/隐藏、锁定、排序或移除。
- 选中面板可用当前源图和有效裁剪一键替换；替换时重新校验边界，若新源缺少物理尺度则自动关闭旧比例尺，避免沿用错误校准。
- 文字支持 4–72 pt、粗体和十六进制颜色；箭头与形状支持 0.25–10 pt 线宽及精确像素坐标。
- 未完成的标注可以先保存到工程中继续编辑，但颜色、坐标、字号或线宽无效时会严格阻止最终导出。
- 导出整张 TIFF、PNG、BMP 或 JPEG 拼版；最终渲染重新读取原始图像，不使用界面预览代理。
- 300 dpi 导出使用像素坐标到 WPF 设备单位的精确换算，确保面板位置、标签与比例尺不会因系统 96 dpi 坐标而偏移。
- 新建、保存、另存为和打开 `*.scicanvas` 工程。
- 工程恢复源图引用、SHA-256、活动裁剪、模板、面板位置、图层顺序、显隐、锁定、比例尺、标注、参考线和吸附设置。
- 连续保存采用同目录临时文件原子替换，并保留一个 `.scicanvas.bak` 上一版本备份。
- 打开工程时重新计算全部源图指纹；文件缺失或内容变化时要求选择替代文件，只允许 SHA-256 完全一致的文件安全重新链接，否则停止打开。
- 对确实需要采用的源图新版本，可执行受控接受流程：显示新旧 SHA-256 和尺寸、再次确认、检查全部裁剪边界、重新读取验证，并把变更写入工程审计轨迹。
- 接受新版本不会写入源文件；为防止旧指纹被误当作可撤销状态，该操作会清空旧撤销历史，并将工程标记为必须手动保存。
- 未保存状态显示在窗口标题中，关闭程序时会询问保存。
- 最近 100 步文档编辑可撤销/重做；连续拖动合并为一步，导入源图不进入撤销栈。
- 工程快捷键：`Ctrl+Z` 撤销、`Ctrl+Y` 或 `Ctrl+Shift+Z` 重做、`Ctrl+N` 新建、`Ctrl+O` 打开、`Ctrl+S` 保存、`Ctrl+Shift+S` 另存为、`Ctrl+I` 导入、`Ctrl+Enter` 加入拼版。
- 画布快捷键：`Ctrl++` / `Ctrl+-` 缩放、`Ctrl+0` 或 `F` 适合窗口、`Ctrl+1` 或 `1` 原始大小、Space/中键拖动平移、方向键微调、`Shift+方向键` 以 10 px 微调、`Delete` / `Backspace` 删除、`Esc` 取消或清除选择、`Ctrl+A` 全选拼版面板。
- 科研工具快捷键：`V` 裁剪、`K` 标定、`L` 长度、`A` 角度、`R` 矩形 ROI、`E` 圆形测量、`P` 折线；在文本框和下拉框输入时不会误触工具切换。
- 编辑停止 10 秒后自动写入独立恢复副本；已保存工程使用同目录旁车文件，未命名工程使用本机 `%LocalAppData%\SciCanvas\Recovery`。
- 启动或打开工程时检测较新的恢复副本，并由用户决定恢复或放弃；恢复后保持未保存状态，手动保存成功才清理副本。

- 支持 PDF/SVG 可编辑矢量导出（图片、标注和比例尺作为独立对象）、用户自定义模板导入与持久化，以及带逐项源文件复核的批量裁剪队列。

## v0.9 科研规范层

当前基线已接入 v0.9 的首批科研规范能力：

- 每个面板都支持可持久化的非破坏性显示参数：亮度、对比度、Gamma、黑白点、灰度化、反相和 RGB/单通道查看；预览与最终导出使用同一参数，源图始终只读。
- 工程文件 schema 升级为 `0.9`，调整参数以明确字段保存，并兼容读取 `0.1` 工程。
- 拼版导出前运行投稿预检：源图指纹状态、画布边界、裁剪区域、有效 DPI、隐藏面板、重复标签、比例尺和处理参数都会被检查；错误会阻止导出，提醒会写入报告。
- 成功导出主图后，默认在同目录生成两个全新 sidecar：`figure.provenance.json` 和 `figure.export-report.html`。其中记录源图 SHA-256、尺寸/位深/通道、面板裁剪与布局、处理参数、画布尺寸、DPI 和预检结果。
- 所有 sidecar 采用 `CreateNew`，不覆盖既有文件；主图、工程和源图之间保持可追溯关系。

这组能力对应方案中的 v0.9 P0：非破坏性处理、投稿预检、导出配置/证据链和工程可恢复性。后续可在同一数据结构上继续增加 OME-TIFF 元数据、命令行导出和课题组模板共享。

## v1.0 投稿版本与多页图支持

下一阶段已落地两条可验证链路：

- 多页 TIFF/序列图像在素材元数据中显示帧数；面板可选择具体帧，预览、TIFF/PDF/SVG 最终导出、工程恢复、撤销/重做和溯源报告使用同一个帧索引。
- 图组工作区新增“批量导出投稿版本”：一次生成主图无损 TIFF、补充图 PNG 和 1200 px 缩略图 PNG；每个版本独立运行投稿预检，并生成独立的 `.provenance.json` 与 `.export-report.html`，已有文件一律不覆盖。
- 导出预设现在包含格式、DPI、缩放/目标尺寸和溯源开关，并写入工程 `exportProfiles`；只设置一个目标尺寸时保持画布纵横比。
- 批量版本的溯源 sidecar 额外记录预设 ID/名称和多页帧索引，便于复核同一工程的不同投稿输出。

## v1.1 OME、16-bit 与自动化导出

当前下一阶段新增四条可审计链路：

- OME-TIFF 导入会从 TIFF `ImageDescription` 安全解析 OME-XML，记录维度顺序、像素类型、Z/C/T 尺寸、物理像素尺寸、时间间隔、通道名称和 XML SHA-256；这些字段进入素材摘要、工程 `1.1` 文件和导出溯源报告。
- 主图预设默认输出真正的 16-bit RGB48 TIFF。图像平面在 16-bit 缓冲中进行双线性合成和非破坏参数处理，文字、比例尺与科研标注作为单独覆盖层合成；不会先把整张组图量化为 8-bit。透明画布会被明确阻止，避免静默扁平化。
- 右侧拼版检查器新增可编辑投稿预设，可新增、移除和恢复默认，并编辑格式、DPI、缩放、目标宽高、位深和溯源开关；设置随工程保存、自动恢复和重开。
- 新增 `SciCanvas.Cli`，可在无界面批处理、脚本或课题组工作站中读取同一个 `.scicanvas` 工程，复核所有源图 SHA-256、运行投稿预检、拒绝覆盖已有文件，并使用工程内预设批量导出。

CLI 示例：

```powershell
# 查看工程内预设
.\SciCanvas.Cli.exe export --project .\paper.scicanvas --list-profiles

# 按工程内全部预设导出
.\SciCanvas.Cli.exe export --project .\paper.scicanvas --output-dir .\submission

# 只导出指定预设；可重复传入 --profile
.\SciCanvas.Cli.exe export --project .\paper.scicanvas --output-dir .\submission --profile main-tiff
```

CLI 退出码：`0` 成功、`2` 参数错误、`3` 工程或源图验证失败、`4` 部分或全部导出失败。默认仍生成溯源 JSON/HTML；只有明确传入 `--no-provenance` 才关闭。

## v1.2 科学工作流与可审计辅助分析

`v1.2.0-alpha` 按分阶段升级路线补齐了从标定到投稿的主工作流；`v1.2.1-alpha` 继续完善画布缩放/平移、裁剪手柄、统一图层选择与测量对象编辑；`v1.2.2-alpha` 修复空工程伪裁剪框、裁剪创建/移动/缩放失效和拖动卡顿：

- 源图级 X/Y Calibration、metadata/手动标定、Length/Angle/Rectangle/Circle/Polyline 测量、真实单位优先显示、面积/周长、统计直方图、强度剖面，以及 CSV/XLSX/复制表格。
- 测量对象可作为独立图层整体移动或用端点手柄改大小；支持实线、虚线、点线、点划线、描边色/宽、端点大小、端点/标签开关、ROI 填充透明度、显隐与锁定，全部随工程和撤销历史保存。
- Calibration 同时驱动 Measurement 与同源 Figure Scale Bar；数据进入 `1.2` 工程、自动恢复、撤销/重做和审计轨迹。
- Match Width/Height/Frame/Aspect、Line 标注、项目全局样式、Inset 局部放大、动态 ROI、同源 Linked Crop 和矢量 Inset 边框。
- 独立 Figure QC 面板检查源图完整性、有效 DPI、边界/重叠、标签、标尺、标注样式、背景和未保存状态，并可定位到问题面板。
- 科研颜色字典在工程内固定“物理对象 → HEX 颜色”，支持名称唯一性和红绿色觉缺陷近似检查，可应用到选中标注或全局图形样式。
- 可解释辅助区域分析直接读取原始像素，提供亮/暗颗粒、晶粒区域、孔隙、相区、裂纹和片层候选；记录 ROI、阈值、最小面积和算法版本。候选默认不写入测量，必须人工接受/拒绝后才能转换为等效直径、裂纹长度或片层宽度测量。
- 粒子分析由 `AnalysisResourcePolicy` 约束 ROI 像素、峰值工作内存、候选连通域和单域凸包支持点；预算内返回全部结果，触及安全上限则以 `AnalysisTooComplex` 明确中止并建议提高 MinimumArea、调整 threshold 或缩小 ROI，绝不把部分结果伪装为完整科研结果。
- 辅助布局、样式协调和科研诚信检查均使用明确规则并可撤销；软件不提供生成式填充、克隆、局部擦除或对象移除。
- Figure 可继续输出 16-bit TIFF、PNG/JPEG、可编辑 PDF/SVG，并生成 provenance JSON / HTML 报告；CLI 使用相同工程与预检规则。

完整阶段、验收条件和边界见 [升级路线](docs/UPGRADE_ROADMAP.md)。

## v2.2 科学图像分析 II 与自动化

`v2.2.0-alpha` 汇总尚未发布的 v2.0.2 正确性加固、v2.1 原始像素统计分析，并交付 v2.2 的首个端到端自动化切片：阈值颗粒分析与可复用批处理配方。

- ROI 统计、强度剖面、直方图与颗粒分析都绑定 Source Asset、source revision、frame、channel、bit depth、算法版本和分析时间；替换源图后不会把旧结果误认为当前结果。
- 原始像素读取支持 8-bit / 16-bit 与 Luminance、Red、Green、Blue、Alpha 通道；16-bit 阈值与均值不经 8-bit 量化。
- 亮颗粒、暗颗粒等阈值模式可使用确定性 Otsu 或手动阈值，输出连通域数量、面积分数、面积、周长、等效直径、圆度、长宽比、原始强度与真实 Feret 最大/最小径。
- 当前阈值、最小面积与通道可形成批处理配方，并应用于裁剪队列中的多个来源；所有满足条件的连通区域都会返回，不再按候选数量截断。每个结果仍分别绑定各自来源 revision，整个批次作为单个历史手势并写入审计轨迹。
- 分析结果可随 `2.2` 工程保存、严格校验和迁移，并统一导出为 CSV / XLSX；旧 `2.1` 工程会显式迁移且保留既有分析。
- Figure 全局样式、OME 标定、各向异性角度、像素精确裁剪、格式感知导出预检和 Panel 标签序列等正确性问题同步修复。

源图仍保持只读；辅助分析只生成可审计结果，不进行生成式填充、克隆、擦除或对象移除。

## v2.5 Scientific Data Asset Foundation

- `TabularDataAsset` 以稳定 ID、名称、可选只读来源路径/指纹、source revision、类型化列与行、完整导入元数据进入 Core；列角色支持 X、Y、YError、Category、Label 与 Other。
- CSV/TSV 使用严格 UTF-8 / UTF-8 BOM、RFC 引号、自动分隔符检测和 Invariant numeric parsing；XLSX 直接读取 OOXML，支持工作表发现/选择、A1 范围和表头行，不依赖 Excel 安装或新增第三方包。
- 导入遵循强制 `Preview → Confirm`：页面先显示列名、前几行、类型推断和从表头提取的单位；用户可复核类型、单位与角色，确认时重新读取并核对 SHA-256。来源在预览后或导入中变化时不会创建资产。
- `ScientificDataWorkspace` 是独立 `UserControl`；当前工程 schema `3.0` 继续保存全部类型化单元格和导入选择。旧 2.6 及更早工程确定性迁移为空 DataAsset 集合，2.7+ 工程会原样保留 DataAsset。只有表格、没有图像的工程也会正确标脏和自动保存。
- Phase 9 门禁为 `412/412` tests（Core 168 + Windows/WPF 244），solution build 为 0 warnings、0 errors。

## v2.5 Plot Workspace Foundation

- `SciCanvas.Core.Plotting.PlotObject` 是不可变、数据绑定的二维绘图对象，覆盖 Line、Scatter、Line + Symbol、Error Bar、Histogram、Box Plot 与 Heatmap；每个对象都保存 DataAsset ID、source revision 和稳定列 ID，不以截图充当科学数据。
- X/Y 轴支持标题、单位、linear/log、显式 min/max、major tick interval 与 minor tick count。axis/tick/legend/annotation 四类字体直接复用 canonical `TextStyle`，series 独立保存线色、线宽、线型、marker 形状/大小/填充/描边。
- Error Bar 明确区分 symmetric 与 asymmetric，并绑定一个或两个原始数值列。负误差、过期 revision、外部列、log 轴非正数据及触及非正数的 log error range 会在 Core 校验阶段失败；不会通过静默丢点让 Plot 通过。
- `PlotWorkspaceViewModel` 与 `PlotWorkspace.xaml` 是独立工作区；WPF `PlotPreviewControl` 直接读取类型化行生成七类矢量预览。`MainWindow.xaml` 只保留第九个页签宿主，DataAsset 被 Plot 引用时不能先行移除。
- Phase 10 引入工程 schema `2.8`，round-trip 数据绑定、轴、字体和 series 样式；2.7→2.8 保留 DataAssets 并新增空 Plot 集合。Plot 使用独立脏标志参与手动保存、打开恢复和自动恢复副本。
- Phase 10 门禁为 `436/436` tests（Core 177 + Windows/WPF 259），solution build 为 0 warnings、0 errors。

## v2.5 Plot Scientific Provenance

- `PlotDataFilter` 同时保存稳定 column ID、受限 operator、canonical operand、可读 expression 与 excluded row count；Core 会从 DataAsset 重新执行 filter 并核对表达式和计数，文件中的自报数值不能绕过验证。
- normalize-minmax、offset、log10 与 moving-average 都以 ordered `PlotDataTransform` 保存。moving average 明确记录 window/alignment，并用边缘部分窗口保持行数；Error Bar 的 Y 变换若需要未定义的误差传播会被拒绝。
- `PlotDataProjector` 先执行 filter、再按列表执行 transforms，输出每行 source row index、original/projected value、included/excluded/unplottable 计数；它不修改 `TabularDataAsset`。显式 filter 可以有记录地排除 log 非正行，但变换后产生非正 log 值、空 Plot 或触及非正数的误差范围仍会阻止保存。
- `PlotScientificProvenance` 完整记录 Plot/DataAsset/revision、X/Y/error/value columns、filter expression、excluded count、ordered transforms、PlotType、Style 和行数核算。WPF 七类预览统一消费该投影并显示数据核算摘要。
- 工程 schema `2.9` 保存 filter 与 transforms；2.8→2.9 保留已有 Plot 并默认空 operations。工程保存/打开会拒绝被篡改的 expression/excluded count。
- Phase 11 门禁为 `447/447` tests（Core 184 + Windows/WPF 263），solution build 为 0 warnings、0 errors。

## v2.5 Plot → Figure Native Panels

- 已保存 Plot 可通过 Plot Workspace 的“添加到 Figure”直接成为 `FigurePlotPanelExportItem`，Figure 保存 Plot/DataAsset/revision 引用与冻结的投影快照，不生成或持久化 screenshot。画布支持选择、拖动、缩放、锁定、方向键微调、Delete、统一面板编号和撤销/重做。
- Line、Scatter、Line + Symbol、Error Bar、Histogram、Box Plot、Heatmap 共用同一中立 Plot geometry scene。PNG、8-bit TIFF 与 16-bit TIFF 走高质量栅格路径；SVG 输出原生 line/rect/ellipse/polygon/text；PDF 输出直接 path/text operators，不嵌入 Plot raster image。
- Plot 的 axis/tick/legend/annotation 遵循 `Project → Figure → Panel → Plot Object` 排版继承；Panel Label 也使用 Figure canonical style。PDF Plot 文字复用 Figure 的 embedded TrueType / outline fallback 策略，字体替换和实际结果继续进入 provenance。
- 工程 writer 升级到 schema `3.0`，保存 Plot Panel 的稳定 ID、Plot ID、目标矩形、标签、可见/锁定、ZIndex、Panel style 与 Plot typography overrides。2.9→3.0 默认空 Plot Panel；加载会拒绝缺失 Plot/DataAsset、revision 不匹配、重复 ID、越界几何和非法样式。GUI、CLI、Preflight、投稿 provenance 均读取相同原生 Panel contract。
- Phase 12 release commit `f84db228735f41f6ed82627d58afe135e12e5440` 的 GitHub Actions 实际结果为 `463/464` tests（Core `188/188`；Windows/WPF `275/276`），Heatmap WPF preview 因 `5 s` 超时失败；solution build 为 0 warnings、0 errors。在新的 release commit 真实 green 前不声明 `464/464` GitHub CI passed。

## v2.4 Scientific Objects, Multichannel & Reproducible Publishing

`v2.4.0-alpha` 已完成路线图 PR1–PR12，工程 schema 正式升级为 `2.4`。

- Canonical Scientific Objects 覆盖 Measurement Overlay、多尺度标尺、Polygon Annotation、Canonical ROI、Direction Marker、Colorbar 与 Channel Legend，并统一进入工程、历史、预览、栅格、SVG、PDF 与 provenance。
- 多通道 EDS Asset Group 保存 raw UInt8/UInt16 plane、通道名称来源、颜色、显示范围、Gamma、Opacity、FrameIndex 和 source revision；Panel composite 从不可变 channel layers 确定性重建，旧单源工程保持兼容。
- Linked Views、Translation/Rigid/Affine registration、revision stale 阻断、Polygon ROI 顶点传播与逐通道 raw statistics 形成可保存、可恢复、可撤销的闭环。
- Integrity QC 精确定位 Asset/Panel/Object/Measurement/Analysis/Channel/LinkGroup/Mapping，并新增 UInt8/UInt16 旋转/镜像 exact duplicate 检测。
- Journal preset pack、显式字体替换和 PDF 字体策略进入 Publishing Portability 工作区；requested font 不会被 fallback 静默改写。
- Export 与投稿包统一使用不可变 `FigureExportDocument`；provenance 记录 channels、registration、ROI propagation、colorbar/legend、font resolution 和 PDF 实际策略。
- v2.5 Plot 开发前已完成架构分解：主 ViewModel 的 QC、导出、投稿包、工程 I/O 与科学分析进入五个协调器；Figure 的 Panel/Object/Link 状态进入三个独立模块；Inspector 与 Layers 进入独立 `UserControl`，`MainWindow.xaml` 仅保留页面宿主。
- 辅助区域/颗粒分析已取消 1000 条候选截断，返回所有满足阈值和最小面积条件的连通区域。

当前限制包括：不宣称 full OME-TIFF/CZI/LIF/ND2/DM3/DM4/Bio-Formats；不猜测未保存的 interleaved RGB component；任意 affine warp 尚未作为像素重采样 composite。PDF 仅对许可允许且可可靠映射的 TrueType / OpenType TrueType 生成实际字形子集；CFF、受限许可或无法可靠映射的字体在严格策略下阻止导出，在偏好策略下记录原因并回退为文字轮廓。完整内容见 [v2.4.0-alpha 发布说明](docs/RELEASE_2.4.0.md)与 [v2.4 路线图](docs/ROADMAP_2.4.md)。

## v2.3 Scientific Styling, Integrity & Submission

`v2.3.0-alpha` 把科研对象样式从界面字段升级为可迁移、可审计并贯穿预览/工程/导出的统一系统，同时交付投稿前科研完整性检查和一键投稿包。

- Measurement 统一支持独立描边、ROI 填充、端点边框/内部、标签颜色、Windows 系统字体、字号和粗体；标签不再被迫与测量线同色。
- Text/Arrow/Line/Rectangle/Ellipse annotation 使用明确的 `TextColor`、`StrokeColor` 和 `FillColor` 语义；矩形/椭圆透明填充真实进入 PNG/TIFF/SVG/PDF。
- 字体下拉缓存系统已安装字体，同时允许保留当前机器缺失的字体名称；UI 与 `FONT_MISSING` QC 明确提示，不静默改写工程。
- Panel Label 与 Scale Bar 的尺线/文字样式可在 Figure 级设置，也可为单个 Panel 建立局部覆盖；“恢复继承”删除局部覆盖并继续跟随上级样式。
- 科研颜色字典可快捷应用到 measurement、annotation、Panel Label 和 Scale Bar，同时继续允许任意 `#RRGGBB` / `#AARRGGBB` 与 Windows 取色器。
- Figure QC 新增字体可用性、字体一致性、相同 SHA 源图、相同/强重叠 crop、过期 analysis 与过期 measurement revision 检查，并提供投稿检查清单和可导航目标。
- Provenance 记录每个 Panel 的源路径、SHA-256、revision、frame、crop 和全部非破坏显示参数，以及 ROI/profile/particle algorithm ID、版本与参数。
- “生成投稿包”创建 Figure、Data、Audit、Supplement 和 README 结构，包含 TIFF、可编辑 SVG、溯源/导出报告、CSV/XLSX、source manifest 与 QC 报告；有 Error 或非空目标目录时拒绝生成，默认不复制源图。
- 工程 schema 升级为 `2.3`，显式迁移所有旧版本；v2.2 的颜色、无填充外观和测量标签视觉默认保持兼容。

完整实现与验证说明见 [v2.3 发布说明](docs/RELEASE_2.3.0.md)。

## v2.0 Scientific Figure Workspace

`v2.0.1-alpha` 在保留 .NET 10 / WPF / MVVM、只读源文件和从原始像素重建导出的基础上，继续完善 Scientific Figure Workspace V2。

本次交互更新包括：

- 顶部命令区、左侧资源库、右侧检查器和底部测量表均可独立收放，中央图像与拼版画布可获得更多空间。
- 测量与科研标注检查器新增系统取色器；连续绘制会继承上一标注的颜色、线宽、线型、端点、标签与填充等视觉样式。
- 当前工程存在未保存更改时，新建或打开其他工程会提供“保存 / 放弃 / 取消”选择，不再被当前工程锁住。
- 已有内容的拼图工程可中途切换模板并迁移到新插槽；还可输入 100–20,000 px 的自定义画布宽高，保存、重开与撤销均保留尺寸。

V2 基础能力包括：

- 将 Scientific Asset、Figure、Panel 与 Scientific Object 明确分离；同一素材可被多个 Panel 引用，替换源图不会改变 Panel 的版面、标签与层级。
- Crop 使用归一化源图坐标，Panel Frame 使用毫米，显式区分 Source → Panel → Figure 坐标；调整 Panel 大小不会改变源坐标测量值。
- Fit、Fill 与 Manual Crop 共用裁剪计算器，有效 DPI 按可见源像素和 Panel 物理尺寸计算，并由可配置的 Figure QC 阈值检查。
- Source Tracking 记录指纹、尺寸、时间戳与 revision；Relink 保持素材身份，接受内容变更会递增 revision，并重新判定比例尺、测量、ROI、Inset 和 Colorbar 的科学有效性。
- Project Style 支持 Project → Figure → Panel → Scientific Object 继承、局部覆盖、重置与复制；Core 提供 2×2、2×3、3×2 毫米模板和可逆布局变更。
- Figure QC 以确定性规则检查边界、对齐、间距、字体、有效 DPI、标定、比例尺、标签和源文件完整性；项目 DPI 门槛会持久化并进入导出预检。
- Auto Trim 只生成可复核的白边/透明边建议，映射回不可变源像素后由用户显式应用，并作为单步操作进入撤销历史。
- 工程 schema 升级为 `2.0`，显式迁移 `0.1`、`0.9`、`1.1` 与 `1.2` 工程并记录审计项；旧像素变换仍保留用于兼容既有导出与恢复链路。
- 桌面工作区新增 Assets、Figures、Layers、Templates 主导航、可搜索 Asset Library、素材状态徽标、毫米 Panel Frame、Fit/Fill/Manual Crop、替换有效性、样式继承提示和可配置 Figure QC。

实现说明、架构审计与视觉概念分别见 [V2 实现说明](docs/SCIENTIFIC_FIGURE_WORKSPACE_V2.md)、[V2 架构审计](docs/V2_ARCHITECTURE_AUDIT.md)和 [V2 工作区视觉稿](docs/design/scicanvas-v2-workspace-concept.png)。

## 本地运行

需要 Windows 10/11 与 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。

```powershell
dotnet run --project .\src\SciCanvas.App\SciCanvas.App.csproj
```

生成 Windows x64 目录版（GUI 与 CLI）：

```powershell
dotnet publish .\src\SciCanvas.App\SciCanvas.App.csproj --configuration Release --runtime win-x64 --self-contained false --output .\artifacts\SciCanvas-win-x64
dotnet publish .\src\SciCanvas.Cli\SciCanvas.Cli.csproj --configuration Release --runtime win-x64 --self-contained false --output .\artifacts\SciCanvas-win-x64
```

生成后可双击 `.\artifacts\SciCanvas-win-x64\SciCanvas.App.exe`，或在终端运行同目录的 `SciCanvas.Cli.exe`。该目录版仍需要系统安装 .NET 10 Desktop Runtime。

当前可直接交付的版本为 `v2.5.0-alpha` 自包含 Windows x64 包：

- [SciCanvas-v2.5.0-alpha-Setup.exe](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.5.0-alpha/SciCanvas-v2.5.0-alpha-Setup.exe)：双击安装到当前用户目录，不需要管理员权限，同时安装 GUI 与 CLI。
- [SciCanvas-v2.5.0-alpha-Portable.zip](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.5.0-alpha/SciCanvas-v2.5.0-alpha-Portable.zip)：解压后运行 `SciCanvas.App.exe` 或 `SciCanvas.Cli.exe`，不需要安装 .NET。
- [SciCanvas-v2.5.0-alpha-SHA256.txt](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.5.0-alpha/SciCanvas-v2.5.0-alpha-SHA256.txt)：安装包与便携包的 SHA-256 校验值。

完整更新内容、安装步骤和验证记录见 [v2.5.0-alpha Release](https://github.com/ustcercyy-ui/SciCanvas/releases/tag/v2.5.0-alpha)。
构建与测试：

```powershell
dotnet build .\SciCanvas.sln --configuration Debug
dotnet test .\SciCanvas.sln --configuration Debug
```

## 已确定的产品方向

- 深色精密工作台：左侧源图像库，中间像素级裁剪/拼版画布，右侧精确参数、对齐与分布、图层管理。
- 固定尺寸裁剪：可从参考图创建裁剪模板，并在后续图片上保持相同宽高、自由移动位置。
- 无损工程：裁剪、摆放、标注和显示调整只写入工程文件，源文件始终只读。
- 材料科研模板：提供期刊尺寸预设和材料领域常见的证据链组图模板。
- 可审计导出：导出结果附带源文件指纹、裁剪坐标、变换和导出参数。

## 文档

- [MVP 产品规格](docs/MVP_SPEC.md)
- [技术架构](docs/ARCHITECTURE.md)
- [分阶段升级路线与验收](docs/UPGRADE_ROADMAP.md)
- [v1.2 发布验收与视觉台账](docs/RELEASE_1.2_QA.md)
- [v1.2.2 裁剪修复与安装说明](docs/RELEASE_1.2.2.md)
- [v2.0 发布说明与安装验证](docs/RELEASE_2.0.0.md)
- [v2.0.1 交互改进与安装验证](docs/RELEASE_2.0.1.md)
- [v2.2 科学分析与自动化发布说明](docs/RELEASE_2.2.0.md)
- [v2.3 科研样式、完整性与投稿包发布说明](docs/RELEASE_2.3.0.md)
- [v2.5.0-alpha Scientific Data、Registered Imaging 与 Plot Workspace 发布说明](docs/RELEASE_2.5.0.md)
- [v2.4.0-alpha 最终阶段发布说明](docs/RELEASE_2.4.0.md)
- [v2.4.0-alpha.2 检查器与图层显示热修订](docs/RELEASE_2.4.0-alpha.2.md)
- [v2.4.0-alpha.1 科学对象与多通道阶段发布说明](docs/RELEASE_2.4.0-alpha.1.md)
- [v2.4 完整路线图与 PR1–PR12 状态](docs/ROADMAP_2.4.md)
- [Scientific Figure Workspace V2 实现说明](docs/SCIENTIFIC_FIGURE_WORKSPACE_V2.md)
- [Scientific Figure Workspace V2 架构审计](docs/V2_ARCHITECTURE_AUDIT.md)
- [模板系统](docs/TEMPLATE_SYSTEM.md)
- [工程文件 JSON Schema](schemas/scicanvas-project.schema.json)
- [组图模板 JSON Schema](schemas/scicanvas-template.schema.json)
- [最小工程示例](examples/minimal.scicanvas)
- [多尺度形貌模板示例](templates/builtin/multiscale-morphology.nature-double.json)
- [通用 2×2 对照模板](templates/builtin/comparison-2x2.nature-double.json)
- [制备—结构—性能模板](templates/builtin/synthesis-structure-performance.nature-double.json)
- [储能电化学模板](templates/builtin/energy-storage-electrochemistry.nature-double.json)
- [物相—结构—机理模板](templates/builtin/phase-structure-mechanism.nature-double.json)
- [力学性能—断口模板](templates/builtin/mechanics-fracture.nature-double.json)
- [已选主界面方向](docs/assets/scicanvas-selected-ui.png)

## 建议技术栈

- C# / .NET 10
- WPF
- SkiaSharp
- NetVips / libvips
- JSON Schema Draft 2020-12
- xUnit

## MVP 核心红线

1. 软件不得写入、移动、重命名或删除源图像。
2. 导出目标不得与任何源文件指向同一文件。
3. 裁剪区域必须保存为源图像整数像素坐标。
4. 预览代理不得被用于最终导出。
5. 未经用户明确选择，不得进行缩放、插值、位深转换或颜色空间转换。
