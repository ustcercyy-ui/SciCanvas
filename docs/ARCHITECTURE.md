# SciCanvas 技术架构

版本：1.2
对应产品规格：[MVP_SPEC.md](MVP_SPEC.md)

## 1. 架构目标

架构围绕四个不可妥协的目标设计：

1. 源文件不可变。
2. 裁剪坐标像素准确。
3. 预览与最终导出解耦。
4. 所有结果可追溯和可复现。

推荐采用模块化单体桌面应用。MVP 不引入微服务、本地数据库服务器或插件进程，避免部署和故障面扩大。

## 2. 技术选型

| 层 | 技术 | 说明 |
|---|---|---|
| 语言/运行时 | C# / .NET 10 | Windows 自包含发布 |
| 桌面 UI | WPF | 成熟的 Windows 工具型界面和输入系统 |
| UI 架构 | MVVM + 命令 | 业务状态与界面分离 |
| 画布渲染 | SkiaSharp | 自定义高 DPI 画布、标尺、裁剪框和图层 |
| 图像 I/O/导出 | NetVips / libvips | 按需读取、区域裁剪和大图流水线 |
| 工程格式 | JSON + JSON Schema | 可审阅、可迁移、易于开源协作 |
| 日志 | Microsoft.Extensions.Logging | 本地诊断，不记录敏感路径时可脱敏 |
| 测试 | xUnit | 单元、集成、像素回归和路径安全测试 |
| 安装 | MSIX + portable zip | 标准安装与免安装两种方式 |

应用代码建议使用 MIT 许可证。发布前生成第三方依赖清单；libvips 采用 LGPL-2.1-or-later，分发动态库时需要满足其许可证义务。

## 3. 解决方案结构

```text
SciCanvas.sln
src/
  SciCanvas.App/             WPF窗口、视图、快捷键、资源和启动
  SciCanvas.Presentation/    ViewModel、UI命令和状态映射
  SciCanvas.Core/            文档模型、坐标、命令、撤销和领域规则
  SciCanvas.Imaging/         解码、元数据、缩略图、分块和像素读取
  SciCanvas.Rendering/       SkiaSharp画布与命中测试
  SciCanvas.Persistence/     工程序列化、迁移、自动保存和恢复
  SciCanvas.Export/          合成、位深、颜色、报告和安全写入
  SciCanvas.Templates/       模板加载、验证、期刊预设和合规检查
  SciCanvas.Platform.Windows/Windows文件标识、路径和系统集成
tests/
  SciCanvas.Core.Tests/
  SciCanvas.Imaging.Tests/
  SciCanvas.Persistence.Tests/
  SciCanvas.Export.Tests/
  SciCanvas.PathSafety.Tests/
  SciCanvas.UiSmokeTests/
testdata/
  images/                    小型、可公开分发的合成测试图
  golden/                    已知正确的像素输出
docs/
schemas/
```

依赖方向必须单向：

```text
App → Presentation → Core
              ↘ Imaging / Rendering / Persistence / Export / Templates
Platform.Windows → Core 中定义的抽象接口
```

`Core` 不引用 WPF、SkiaSharp 或 NetVips，以便独立测试坐标和文档规则。

## 4. 核心领域模型

### 4.1 ProjectDocument

```csharp
public sealed record ProjectDocument(
    Guid ProjectId,
    string SchemaVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    CanvasDocument Canvas,
    IReadOnlyList<SourceAsset> Sources,
    IReadOnlyList<Layer> Layers,
    IReadOnlyList<CropPreset> CropPresets,
    IReadOnlyList<Guide> Guides,
    IReadOnlyList<ExportProfile> ExportProfiles);
```

工程文件只保存描述和引用，不保存由预览缓存生成的像素。

### 4.2 SourceAsset

```csharp
public sealed record SourceAsset(
    Guid Id,
    string DisplayName,
    string OriginalPath,
    string? ProjectRelativePath,
    SourceFingerprint Fingerprint,
    ImageMetadata Metadata,
    SourceLinkState LinkState);

public sealed record SourceFingerprint(
    long ByteLength,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256,
    string? WindowsFileId);
```

`SourceAsset` 是不可变记录。核心层不存在 `WriteSource`、`DeleteSource` 或 `MoveSource` 接口。

### 4.3 CropRegion

```csharp
public readonly record struct PixelRect64(long X, long Y, long Width, long Height)
{
    public long Right => checked(X + Width);
    public long Bottom => checked(Y + Height);
}
```

约束：

- `X >= 0`、`Y >= 0`。
- `Width > 0`、`Height > 0`。
- `Right <= SourceWidth`、`Bottom <= SourceHeight`。
- 使用 64-bit 整数并进行溢出检查。

### 4.4 图层

```csharp
public abstract record Layer(
    Guid Id,
    string Name,
    bool IsVisible,
    bool IsLocked,
    int ZIndex,
    double Opacity);

public sealed record ImageLayer(
    Guid Id,
    string Name,
    bool IsVisible,
    bool IsLocked,
    int ZIndex,
    double Opacity,
    Guid SourceAssetId,
    PixelRect64 SourceRect,
    CanvasTransform Transform,
    IReadOnlyList<NonDestructiveAdjustment> Adjustments)
    : Layer(Id, Name, IsVisible, IsLocked, ZIndex, Opacity);
```

源图在裁剪工作区可以表现为系统锁定图层。拼版中的派生 `ImageLayer` 可以移动和排序，但它只引用 `SourceAssetId + SourceRect`。

### 4.5 画布变换

```csharp
public sealed record CanvasTransform(
    double X,
    double Y,
    double ScaleX,
    double ScaleY,
    int RotationQuarterTurns);
```

MVP 默认 `ScaleX = ScaleY = 1`。非等比缩放需要单独的显式命令和强警告；自由角度旋转放入 P1。

## 5. 三套坐标系统

### 5.1 ImagePixelSpace

- 原点：源图像左上角。
- 单位：源像素。
- 裁剪使用整数坐标。
- 与 Windows DPI 无关。

### 5.2 CanvasWorldSpace

- 原点：拼版画布左上角。
- 单位：画布像素。
- 图层位置使用双精度，最终导出时根据导出合同转换。
- 对齐和分布全部在此空间计算。

### 5.3 DeviceSpace

- 原点：控件左上角。
- 单位：当前显示器设备像素。
- 只用于渲染和输入命中测试。

唯一允许的转换路径：

```text
ImagePixel ←→ CanvasWorld ←→ Device
```

禁止 ViewModel 或视图自行拼接矩阵。转换集中在 `ICoordinateMapper`，并针对 100%、125%、150%、175%、200%、250% Windows 缩放建立测试。

鼠标拖动裁剪框时：

1. 将 Device 坐标逆变换到 ImagePixel 浮点位置。
2. 根据拖动策略统一舍入。
3. 生成整数 `PixelRect64`。
4. 通过边界验证器夹取或阻止越界。
5. 界面显示的 X/Y 必须来自最终整数模型，而不是鼠标浮点值。

## 6. 源文件保护架构

保护源文件不能依赖界面上的锁图标，必须在平台服务、路径策略和测试三层同时实现。

### 6.1 只读源访问

```csharp
public interface ISourceAssetReader
{
    Task<SourceAsset> ImportAsync(string path, CancellationToken ct);
    Task<ImageRegion> ReadRegionAsync(
        SourceAsset asset,
        PixelRect64 region,
        PixelFormatContract format,
        CancellationToken ct);
    Task<SourceVerification> VerifyAsync(SourceAsset asset, CancellationToken ct);
}
```

实现要求：

- 使用 `FileAccess.Read`。
- 不对源路径调用 `Create`、`CreateNew`、`Truncate`、`Write`、`Delete`、`Move` 或 `Replace`。
- 读取结束及时释放句柄；不以“保护”为由长期独占锁定用户文件。
- 导入目录监视只用于报告外部变化，不修改外部文件。

### 6.2 导出路径安全策略

```csharp
public interface IPathSafetyPolicy
{
    Task<ExportPathDecision> ValidateExportTargetAsync(
        string targetPath,
        IReadOnlyCollection<SourceAsset> sources,
        CancellationToken ct);
}
```

检查顺序：

1. 用 `Path.GetFullPath` 规范化目标和源路径。
2. 使用 Windows 不区分大小写规则比较。
3. 获取目标（若存在）与源文件的 Windows File ID，阻止硬链接或不同路径指向同一底层文件。
4. 阻止目标落在由工程标记为“只读源包”的路径。
5. 若目标已存在，使用单独的覆盖确认；即使确认，也不能覆盖源文件。

路径防护必须位于 `ExportService` 内部。任何 UI、命令行或未来插件都不能绕过。

### 6.3 安全导出事务

```text
验证目标路径
  → 在目标目录创建唯一临时文件
  → 从源数据重新渲染
  → 刷新并关闭临时文件
  → 验证输出尺寸/位深
  → 原子移动到最终目标
  → 写出操作报告
```

失败或取消时只清理本次任务创建的已验证临时文件。不得对宽泛目录执行递归清理。

### 6.4 指纹与重新链接

打开工程时先做快速检查：大小、修改时间、Windows File ID。任何不一致再计算 SHA-256。

状态：

- `Verified`：路径与指纹一致。
- `Relocated`：路径变化但哈希一致。
- `Modified`：找到文件但哈希不同。
- `Missing`：找不到文件。
- `Unverified`：用户选择暂不校验。

`Modified` 状态不得自动刷新项目指纹。用户必须主动执行“接受并更新引用”，核对新旧 SHA-256 与尺寸后再次确认。

当前 `0.9.0-alpha` 的安全重新链接仍只接受与工程记录 SHA-256 完全一致的替代文件。对于同一路径下确实需要采用的新内容，受控接受流程会两次导入并在更新预览后再做最终指纹验证，同时检查活动裁剪和全部面板裁剪是否仍在新尺寸边界内。成功后保留工程内源图 ID、更新指纹和元数据、写入 `AcceptSourceRevision` 审计项并要求手动保存。该操作不修改源文件；为避免旧指纹被撤销恢复，接受时清空既有撤销/重做历史。

### 6.5 工程和自动保存

- 工程文件与源图像没有写入关系。
- 未命名工程的自动保存只写入 `%LocalAppData%\SciCanvas\Recovery`；已命名工程写入工程旁边的 `*.autosave.scicanvas` 独立旁车文件。
- 自动保存经过 10 秒空闲防抖并采用原子替换；只序列化工程状态、源图路径和指纹，不复制、改写或删除源图。
- 恢复副本必须比手动保存工程新才提示恢复；恢复内容保持“未保存”状态，手动保存成功后才删除对应恢复副本及其备份。
- “便携工程”若未来支持，只能复制源文件到新目录，不能移动；复制后对源和副本分别计算指纹。

## 7. 图像读取和预览流水线

```text
SourceAsset
  → MetadataProbe
  → ThumbnailBuilder
  → Pyramid/TileProvider
  → PreviewColorMapper
  → Skia Canvas
```

### 7.1 元数据探测

优先从 libvips 读取像素、位深、通道和常见 TIFF 字段。EXIF/ICC/OME 信息通过专用解析器补充。探测失败不得阻止普通像素读取，但应显示缺失字段。

### 7.2 分块缓存

缓存键：

```text
sourceSha256 / pyramidLevel / tileX / tileY / previewMappingVersion
```

规则：

- 内存使用 LRU 缓存并设置上限。
- 磁盘缓存仅包含派生预览，可随时删除。
- 缓存不进入工程文件，也不能作为导出来源。
- 源指纹改变时缓存键自然失效。

### 7.3 16-bit 显示

16-bit 数据的屏幕预览需要映射到 8-bit 显示，但原始 16-bit 像素保持不变。MVP 的显示映射只用于预览；最终导出直接读取原始位深。

## 8. 渲染架构

`CanvasScene` 是渲染器唯一输入：

```csharp
public sealed record CanvasScene(
    CanvasDocument Canvas,
    IReadOnlyList<RenderLayer> Layers,
    Viewport Viewport,
    SelectionState Selection,
    OverlayState Overlays);
```

渲染顺序：

1. 画布外背景。
2. 画布背景。
3. 图像层。
4. 标注、文本和比例尺。
5. 选择框、裁剪框和控制柄。
6. 标尺、参考线和吸附提示。

命中测试使用同一份几何模型，不允许用另一个近似布局重复计算。

当前 `0.9.0-alpha` 已实现图像面板之上的文字、箭头、矩形与椭圆标注层。预览使用画布像素坐标，最终导出按目标 DPI 换算字号和线宽；选择框只属于编辑器覆盖层，不会进入导出结果。标注可以保留未完成状态到工程文件，但导出前必须通过坐标、颜色、字号、线宽和形状尺寸检查。

面板选择状态不写入科研源图或正式工程语义，但会随编辑历史快照保留。多选拖动以所有未锁定选择项的联合边界进行画布限位；相互对齐允许锁定面板作为参照，等距分布则要求所有选择项未锁定并保留两端面板位置。

参考线以独立工程对象保存，只参与编辑器预览、吸附和历史，不进入 `FigureExportDocument`。拖动吸附比较选择组的左/中/右或上/中/下与画布、对应方向参考线和其他可见面板的候选坐标，并选择阈值内距离最小的偏移。精确间距命令保留排序后的首个面板位置，按输入像素值顺序放置其余面板，无法容纳时不执行。

画布背景以标准 `#AARRGGBB` 写入工程和 `FigureExportDocument`，预览与最终像素渲染使用同一颜色。面板编号可按小写字母、大写字母或数字生成；自动模式在面板集合变化时维护编号，显式“重新编号”按画布 `Y → X → Z` 阅读顺序生成。编号显隐、序列和手工标签都进入工程与编辑历史。

## 9. 命令与撤销

所有文档修改实现 `IDocumentCommand`：

```csharp
public interface IDocumentCommand
{
    string Description { get; }
    ProjectDocument Apply(ProjectDocument before);
    ProjectDocument Revert(ProjectDocument after);
    AuditEntry ToAuditEntry();
}
```

典型命令：

- `SetCropRectCommand`
- `AddCropToCanvasCommand`
- `MoveLayersCommand`
- `AlignLayersCommand`
- `DistributeLayersCommand`
- `SetLayerLockCommand`
- `ApplyTemplateCommand`
- `RelinkSourceCommand`

连续鼠标拖动在松开时合并为一个历史命令。只改变视口的平移/缩放不进入文档历史。

当前实现采用最多 100 个完整编辑快照。裁剪、面板、比例尺、标注、模板、图层状态、背景和面板编号进入历史；同一结构上的连续属性变化在 750 ms 窗口内合并。源图导入不进入历史，受控接受源图新版本会清空历史并写审计轨迹，避免撤销操作恢复已经失效的旧指纹。

## 10. 工程持久化

### 10.1 文件格式

MVP 使用 UTF-8 JSON，扩展名 `*.scicanvas`。写入前按照 [工程 JSON Schema](../schemas/scicanvas-project.schema.json) 验证。

### 10.2 原子保存

```text
序列化到同目录临时文件
  → JSON Schema验证
  → 刷新文件
  → 若存在旧工程则创建单个备份
  → 原子替换
```

工程升级由明确的迁移链完成：`0.1 → 0.2 → 1.0`。未知更高版本以只读方式提示，不擅自降级。

## 11. 导出合同

每次导出先生成不可变 `ExportContract`：

```csharp
public sealed record ExportContract(
    string TargetPath,
    PixelSize OutputSize,
    int Dpi,
    OutputColorMode ColorMode,
    OutputBitDepth BitDepth,
    ResamplingMode? Resampling,
    string? JournalPresetId,
    bool WriteAuditReport);
```

规则：

- 输出尺寸与画布像素一致时，`Resampling` 必须为空。
- 输出尺寸不同则必须显式指定插值方式。
- JPEG 不作为 16-bit 或透明输出格式。
- 混合位深图层的降位深策略必须在导出对话框展示。
- P0 输出 PNG/TIFF；PDF/SVG 进入 P1。

## 12. 模板和期刊规则

模板系统读取 [模板 JSON Schema](../schemas/scicanvas-template.schema.json)。模板由以下部分组成：

- 画布尺寸或期刊尺寸预设。
- 网格和间距。
- 带语义角色的插槽。
- 标签规则。
- 验证器规则。
- 规范来源、访问日期和模板版本。

模板应用只创建画布对象和占位图层，不改变任何源图。详细设计见 [TEMPLATE_SYSTEM.md](TEMPLATE_SYSTEM.md)。

## 13. 线程与任务模型

- UI 线程只处理输入、ViewModel 状态和轻量渲染提交。
- 解码、缩略图、哈希和导出在后台运行。
- 每个长任务接受 `CancellationToken`。
- 关闭工程时等待关键保存事务完成；预览任务可以取消。
- 同一源文件的重复哈希任务进行去重。
- 导出使用有限并发，防止多个大图任务耗尽内存。

## 14. 错误处理

错误分三层：

- 用户可修复：路径不存在、尺寸越界、目标已存在、源版本变化。
- 文件不支持：编码器缺失、损坏文件、未知 TIFF 变体。
- 程序错误：未处理异常和不变量被破坏。

用户可修复错误提供明确行动；文件不支持错误显示文件和解码器信息；程序错误写本地诊断日志并提供复制报告按钮。日志默认对用户目录进行脱敏。

## 15. 测试策略

### 15.1 像素金标准

- 生成包含唯一像素值的合成图。
- 对已知 `PixelRect64` 进行裁剪。
- 对导出像素逐值比较。
- 覆盖边缘、单像素、奇偶尺寸和大坐标。

### 15.2 位深与颜色

- 8-bit 灰度、16-bit 灰度、8-bit RGB、16-bit RGB。
- 透明 PNG。
- ICC 存在/缺失。
- 预览映射不得影响导出值。

### 15.3 源文件不变性

对每个写操作场景执行：

```text
记录源 SHA-256
→ 执行操作
→ 模拟取消/失败/崩溃恢复
→ 再次计算 SHA-256
→ 必须完全一致
```

覆盖导出到相同路径、大小写不同路径、相对路径、符号链接/硬链接和网络路径。

### 15.4 DPI 与输入

在 100%、125%、150%、175%、200%、250% 下验证：

- 裁剪框显示位置。
- 鼠标拖动后的整数坐标。
- 方向键移动 1 px。
- 控制柄命中。

### 15.5 工程回归

- 保存后重新打开得到等价文档模型。
- 旧版本迁移结果固定。
- 未知字段的兼容策略固定。
- 源缺失、移动和修改的状态转换固定。

## 16. 发布与供应链

- 首发目标 `win-x64`；后续评估 `win-arm64`。
- 构建生成可复现版本号、依赖清单和许可证通知。
- 对安装包和可执行文件进行代码签名（若项目获得签名证书）。
- GitHub Actions 执行构建、测试、Schema验证和安装包产出。
- 开源发布不包含未经授权的论文图片、患者数据或厂商示例数据。

## 17. 架构决策记录（ADR）基线

- ADR-001：Windows MVP 使用 WPF，不在首版承担跨平台成本。
- ADR-002：核心文档模型不依赖 UI/渲染库。
- ADR-003：源文件服务只暴露读取和验证接口。
- ADR-004：裁剪使用 64-bit 整数源像素坐标。
- ADR-005：预览缓存永不参与最终导出。
- ADR-006：导出路径通过 File ID 防止源文件别名覆盖。
- ADR-007：模板存储抽象布局，不存储论文图像。

## 18. v2.4 科学对象与可复现出版边界

- Core 保存 Canonical Scientific Objects、raw channel descriptors、LinkGroup/SpatialMapping、Canonical ROI 和字体/期刊预设快照；Presentation 只负责编辑状态与命令，Persistence 负责 schema `2.4` 校验和确定性迁移。
- raw/display 严格分离：科研分析读取类型化 `ImagePlane`，显示合成只消费 `ChannelDisplaySettings`；source revision 是 analysis、mapping 与 export 的有效性边界。
- 颗粒分析遵循 complete-or-error 契约：`AnalysisResourcePolicy` 在读取前限制 ROI 像素与估算峰值内存，在连通域遍历中限制候选数量和单域凸包支持点；达到上限抛出结构化 `AnalysisTooComplex`，不保存、不展示部分结果。连通域统计流式累计，凸包只保留每行左右极值角点，避免为巨大 component 保存全部 pixel corners。
- `FigureExportDocument` 是 exporter 的不可变输入。Panel 的单源 crop 或显式 `FigureChannelLayerExportItem`、Scientific Objects 与 Measurement Overlays 在进入 exporter 前完成解析，exporter 不读取 ViewModel。
- Preview、栅格、16-bit、SVG、PDF 和投稿包共享同一 export document 与科学参数；provenance 从该文档及显式 link/ROI/font resolution 快照生成。
- 跨素材坐标映射统一使用 row-major 3×3 矩阵和 source-pixel geometry。revision stale 时禁止静默复用 registration、linked crop、ROI propagation 或跨通道统计。
- 工程 writer 输出 schema `2.4`；migration pipeline 保留全部历史版本读取，并以幂等步骤执行 2.3→2.4 迁移。requested font 与 substitute font 分开持久化，fallback 不修改用户请求样式。
- PDF 字体策略与实际 writer 能力分离：TrueType 与 OpenType TrueType 字体会在许可允许时生成实际字形子集、CIDToGIDMap 与 ToUnicode；CFF、受限许可、禁止子集或无法可靠映射的字形在严格策略下被预检/导出阻止，在偏好策略下记录原因并回退为文字轮廓。

## 19. v2.5 前置架构分解边界

Plot / Scientific Data Workspace 进入主线前，Presentation 与 App 先完成以下职责拆分：

- `MainWindowViewModel` 通过 `ScientificQcCoordinator`、`FigureExportCoordinator`、`SubmissionCoordinator`、`ProjectOpenSaveCoordinator` 和 `ScientificAnalysisCoordinator` 组织事务。源验证、无覆盖导出、provenance、投稿包硬门、工程/恢复副本 I/O、源重连以及三类科学分析的有效性检查不再以内联服务字段散落在主 ViewModel 中。
- `FigurePanelCollectionViewModel` 拥有 Panel、Measurement Overlay 和 ROI Projection；`FigureObjectCollectionViewModel` 拥有 Annotation、Scientific Object、Guide 和科学调色板；`FigureLinkCoordinator` 独占 LinkGroup 状态及带 source identity / revision stale 防护的跨面板同步算法。`FigureCanvasViewModel` 暂时保留同名集合别名，保证现有绑定和工程映射兼容。
- `MainWindow.xaml` 只保留右侧 major workspace host；完整 Inspector 与 Layers 内容分别进入 `InspectorWorkspace` 和 `LayersWorkspace`。控件内命名元素留在各自 WPF namescope，对外只暴露滚动容器和“定位测量检查器”等最小接口。

Phase 8 开始时，前序科研功能已把 `MainWindowViewModel`、`FigureCanvasViewModel` 和 `MainWindow.xaml` 推至约 6329、4263、4108 行；分解后分别为 6106、4142、1906 行，另由独立控件承载 2046 行 Inspector 与 204 行 Layers。架构别名、侧栏切换、滚动、颜色选择和 Ctrl 标注多选均有回归覆盖。验收基线为 Core 163/163、Windows/WPF 231/231，solution build 0 warnings、0 errors。

## 20. v2.5 Scientific Data Asset 边界

`TabularDataAsset` 位于 `SciCanvas.Core.Data`，不引用 WPF、Excel 或具体文件格式。它保存 `Id / Name / SourcePath? / Fingerprint? / SourceRevision / Columns / Rows / ImportMetadata`；`DataColumn` 保存稳定 ID、名称、数据类型、可选单位和可选 X/Y/YError/Category/Label/Other 角色。每个非空单元格同时保存原始 token 与至多一个 Numeric/Boolean/DateTime 类型化值，Text 只保留原文；行宽、类型、有限数、列 ID/名称唯一性均由 Core 不变量验证。

文件解析位于 Persistence：

- `DelimitedTextTableReader` 只以 `FileAccess.Read` 打开 CSV/TSV，严格接受 UTF-8 或 UTF-8 BOM，支持引号、转义双引号和引号内换行，并从逗号、Tab、分号、竖线中确定性选择结构最一致的分隔符。
- `XlsxTableReader` 直接读取 ZIP + OOXML workbook relationships、inline/shared strings 和 worksheet cells；工作表发现独立于数据预览，因此说明页无数据时仍可选择后续 sheet。A1 范围、相对表头行和稀疏单元格按显式坐标处理，不依赖本机 Excel。
- `TabularDataMaterializer` 使用 `InvariantCulture` 推断和转换 Numeric/Boolean/DateTime，解析 `Name (unit)` / `Name [unit]`，重复列名确定性消歧。解析失败报告数据行和列名，不静默改成 Text。

导入器执行两段事务：`PreviewAsync` 生成建议列和前几行；`ImportAsync` 只接受该预览与用户确认列，重新计算来源指纹、重读同一 sheet/range/header，并再次计算指纹。任一指纹或结构变化都在创建资产前失败。`ScientificDataWorkspaceViewModel` 与独立 `ScientificDataWorkspace.xaml` 展示 sheet/range/header、列名/类型/单位/角色和前几行，只有“确认并创建 DataAsset”才修改工程集合。

Phase 9 首次引入 schema `2.7`；当前 writer 为 `3.0`。`ProjectTabularDataAssetSnapshot` 与 `TabularDataSnapshotMapper` round-trip 全部类型化值、来源指纹和导入元数据；2.6 及更早迁移为空 DataAsset 集合，2.7+ 迁移保留 DataAssets。DataAsset 集合使用独立脏标志，避免把可能很大的表格复制进每个 Figure 撤销快照，同时仍参与手动保存、恢复副本和无图像工程自动保存。Phase 9 门禁为 Core 168/168、Windows/WPF 244/244，solution build 0 warnings、0 errors。

## 21. v2.5 Plot Workspace 边界

`PlotObject` 位于 `SciCanvas.Core.Plotting`，由 `PlotKind / PlotDataBinding / PlotAxisDefinition / PlotTypography / PlotSeriesStyle` 组成。Line、Scatter、LineAndSymbol、ErrorBar、Histogram、BoxPlot、Heatmap 都以 DataAsset ID、source revision 与稳定列 ID 为科学身份；Heatmap 另存 value column，ErrorBar 以 `PlotErrorBarBinding` 明确保存 symmetric 单列或 asymmetric lower/upper 双列。Core 验证资产与 revision 相等、列归属和类型、轴范围、canonical `TextStyle`、颜色/宽度/marker 约束，并拒绝负误差及 log 轴非正原始值，因此 renderer 不需要静默删除不兼容数据。

Presentation 的 `PlotWorkspaceViewModel` 独占 Plot 草稿、DataAsset/column 选择、X/Y axis editor、四类字体 editor、series style editor 和 create/update/remove 事务。App 的 `PlotWorkspace.xaml` 是独立 UserControl；`MainWindow.xaml` 只放置页签按钮与单个 host。`PlotPreviewControl` 从类型化行直接生成 WPF 矢量图元，覆盖折线/点/marker/误差条、确定性 histogram bins、按 category 分组的 quartile box、三列 heatmap；预览从来不生成或保存 screenshot。

Phase 10 writer 引入 schema `2.8`。`ProjectPlotSnapshot` 和 `PlotSnapshotMapper` round-trip 数据身份、轴、四类 canonical typography 与 series style；`JsonProjectStore` 在写临时文件前重建严格 Core model，拒绝孤儿 DataAsset、过期 revision 和无效列。2.7→2.8 确定性新增空 Plot 集合并写审计项。MainWindow 以独立 Plot dirty flag 参与手动保存、工程恢复和 autosave，避免把 Plot 状态混入 Figure history；被 Plot 引用的 DataAsset 必须先移除关联 Plot。Phase 10 门禁为 Core 177/177、Windows/WPF 259/259，总计 436/436，solution build 0 warnings、0 errors。

## 22. v2.5 Plot Scientific Provenance 边界

`PlotDataFilter` 不是任意脚本：它保存 column ID、受限 operator、canonical operand、由结构字段确定性生成的 expression 和 excluded row count。验证时从绑定 revision 的 DataAsset 重新计算 expression 与 excluded count；Text/Boolean 只接受 equality，Numeric 使用 invariant finite number，DateTime 使用 round-trip ISO。missing/not-missing 是显式 operator。显式 filter 可以排除 log 非正行，但排除全部 source rows 会阻止创建空 Plot。

`PlotDataTransform` 是有序不可变列表，当前覆盖 min-max normalize、finite offset、log10、centered/trailing moving average。每项保存 column ID 和完整参数，且只能作用于 Plot 的 Numeric X/Y/value 列；Error Bar Y 除 offset 外不执行未经定义的误差传播。`PlotDataProjector` 在过滤后按列表顺序投影，保留 source row index、original/projected X/Y/value、原 error columns 与 category，并分别计算 source/included/excluded/unplottable。missing 行不伪装成 filter exclusion；moving-average 边缘使用部分窗口而不删行。transform 后的 log axis 与 error range 再次验证，不能通过 offset 等操作制造不可渲染对象。

`PlotScientificProvenanceBuilder` 从经过验证的 projection 生成 Plot/DataAsset/revision、X/Y/error/value column IDs、filter expression、excluded count、ordered transform audit text、PlotType、Style 与完整行数核算。WPF renderer 不再读取原始表自行选择行，而统一消费 projector；annotation 显示 included/excluded/unplottable/transforms 摘要。Persistence schema `2.9` round-trip filter 与 transforms，2.8→2.9 保留既有 Plot 并默认空 operations；JsonProjectStore 会在落盘前拒绝篡改的 expression/count。Phase 11 门禁为 Core 184/184、Windows/WPF 263/263，总计 447/447，solution build 0 warnings、0 errors。

## 23. v2.5 Plot → Figure 原生面板边界

`FigurePlotPanelExportItem` 位于 Core Export，保存稳定 Panel ID、不可变 PlotObject、冻结 `PlotDataProjection`、目标像素矩形、标签、可见性、ZIndex、Panel style override 和 Plot typography override。它不保存 WPF 控件、bitmap 或 DataAsset 的可变引用；创建时必须用绑定 revision 的 `TabularDataAsset` 重新投影。排版解析明确为 `Project → Figure → Panel → Plot Object`：Figure 的 canonical annotation style 为四类 Plot 字体的基线，Panel Annotation 可覆盖基线，Axis/Tick/Legend/Annotation 对象级字段再按成员覆盖。

Imaging 的 `FigurePlotSceneBuilder` 是七种 Plot 的唯一几何生成入口，输出 line、rectangle、ellipse、polygon、text 等中立 primitive。`WpfPlotPanelRenderer` 消费它生成 PNG/TIFF；16-bit TIFF 把 Plot 作为 overlay 合成但不降低底层图像采样深度。SVG exporter 把 primitive 写成原生 XML 元素，PDF exporter 写 path/text operators 并复用 `PdfEmbeddedFontRegistry`，所以 Plot 和 Figure 标注共享同一 TrueType subset、许可检查、outline fallback 与实际字体结果记录。

Presentation 的 `FigurePlotPanelViewModel` 独立于图像 Panel 多选/裁剪/比例尺状态，拥有单选、拖动、缩放、锁定和 Figure 样式预览。`PlotWorkspaceViewModel` 通过显式回调添加 Panel，并在存在引用时禁用 Plot 删除。Figure 导出、Preflight、自动编号、重叠检查、字体检查、历史快照与纯 Plot Figure 启用条件都把 image panels 与 plot panels 视为同级 Figure 内容。

Schema `3.0` 在 `ProjectTemplateSnapshot.PlotPanels` 中只保存 Plot 引用与 Figure 表现状态；DataAsset 和 Plot 仍在各自顶层 canonical collections。`JsonProjectStore`、GUI restore 与 CLI builder 都会重建 Plot/DataAsset model、核对 revision 并拒绝孤儿引用、重复 Panel ID、越界矩形和非法样式。2.9→3.0 迁移确定性新增空集合。Figure provenance 记录 Plot/DataAsset/revision、PlotKind、目标矩形、included/excluded/unplottable 行数、filter expression 与 ordered transforms。

Phase 12 门禁为 Core 188/188、Windows/WPF 276/276，总计 464/464；solution build 0 warnings、0 errors。
