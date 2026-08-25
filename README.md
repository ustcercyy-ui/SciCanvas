# SciCanvas（暂定名）

SciCanvas 是一款面向材料科学与实验研究人员的 Windows 本地图像裁剪、拼接和论文组图软件。它以像素准确、非破坏编辑、源文件保护和可追溯导出为首要原则。

当前仓库已经包含可运行的 WPF 工作台、源图级 X/Y 标定、科学测量与统计、Figure QC、可审计辅助分析、项目科研颜色字典、投稿导出、CLI 和 Windows 安装器。

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
- 辅助布局、样式协调和科研诚信检查均使用明确规则并可撤销；软件不提供生成式填充、克隆、局部擦除或对象移除。
- Figure 可继续输出 16-bit TIFF、PNG/JPEG、可编辑 PDF/SVG，并生成 provenance JSON / HTML 报告；CLI 使用相同工程与预检规则。

完整阶段、验收条件和边界见 [升级路线](docs/UPGRADE_ROADMAP.md)。

## v2.0 Scientific Figure Workspace

`v2.0.0-alpha` 在保留现有 .NET 10 / WPF / MVVM、只读源文件和从原始像素重建导出的基础上，引入独立的科学组图领域层：

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

当前已生成可直接交付的 `v2.0.0-alpha` 自包含 Windows x64 包：

- [SciCanvas-v2.0.0-alpha-Setup.exe](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.0.0-alpha/SciCanvas-v2.0.0-alpha-Setup.exe)：双击安装到当前用户目录，不需要管理员权限，同时安装 GUI 与 CLI。
- [SciCanvas-v2.0.0-alpha-Portable.zip](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.0.0-alpha/SciCanvas-v2.0.0-alpha-Portable.zip)：解压后运行 `SciCanvas.App.exe` 或 `SciCanvas.Cli.exe`，不需要安装 .NET。
- [SciCanvas-v2.0.0-alpha-SHA256.txt](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.0.0-alpha/SciCanvas-v2.0.0-alpha-SHA256.txt)：安装包与便携包的 SHA-256 校验值。

完整更新内容、安装步骤和验证记录见 [v2.0.0-alpha Release](https://github.com/ustcercyy-ui/SciCanvas/releases/tag/v2.0.0-alpha)。

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
