# SciCanvas v2.3.0-alpha

SciCanvas v2.3 的主题是 **Scientific Styling, Integrity & Submission**。本版继续坚持源图只读、非破坏处理、源像素坐标为科学真值和导出拒绝覆盖；GUI、CLI 与安装器产品版本为 `2.3.0-alpha`，文件版本为 `2.3.0.0`，工程 schema 为 `2.3`。

## 下载

- [Windows x64 安装器](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.3.0-alpha/SciCanvas-v2.3.0-alpha-Setup.exe)（192,809,150 字节）
- [Windows x64 便携包](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.3.0-alpha/SciCanvas-v2.3.0-alpha-Portable.zip)（76,739,281 字节）
- [SHA-256 校验文件](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.3.0-alpha/SciCanvas-v2.3.0-alpha-SHA256.txt)

## 1. 自定义科研对象颜色

科研样式颜色统一接受 `#RRGGBB` 与 `#AARRGGBB`，由 Core 的同一套 normalize/validate/parse 规则处理并保留 alpha。Measurement、Annotation、Scale Bar 与 Panel Label 均提供可编辑 HEX 和 Windows 系统取色器；Scientific Color Palette 作为快捷来源，不限制任意颜色输入。

## 2. 自定义文字字体

字体目录来自缓存的 Windows `Fonts.SystemFontFamilies` 并按名称排序。下拉框允许直接输入任意字体名；打开跨机器工程时，即使字体未安装也保留原字符串，界面显示缺失提示，预览/导出使用系统 fallback，QC 产生 Warning，绝不静默改写为 Arial。

## 3. Measurement label style

Length、Angle、Rectangle ROI、Circle/Ellipse ROI 与 Polyline 共用一套完整样式：独立 Stroke、Fill、Marker 和 Label 字段。标签支持独立颜色、字体、4–72 pt 字号与粗体，因此可组合“黄色线 + 黑色端点 + 白色文字”。ROI 使用独立填充颜色与 0–100% 透明度；非 ROI 隐藏填充编辑器。

Measurement Inspector 支持 Reset to inherited、Copy Style、Paste Style、Apply to Same Type，并可从项目科研颜色字典应用到描边、填充、端点或标签。首次绘制从当前 Figure/Project 继承；连续绘制继承最近完整样式。

## 4. Annotation stroke/fill/text style

`FigureAnnotationViewModel.Color` 仅保留为 v2.2 兼容适配器。新语义是：Text 使用 `TextColor` 与本地 FontFamily/FontSize/Bold；Arrow/Line 使用 `StrokeColor`；Rectangle/Ellipse 使用独立 `StrokeColor`、`FillColor` 与 `FillOpacityPercent`。连续新建对象继承上一标注的完整样式，Inspector 根据 Kind 动态显示相关字段。

## 5. Scale bar style

Scale Bar 尺线颜色/厚度与 Label 颜色/字体/字号/粗体完全分离。Figure 级样式可被单个 Panel 局部覆盖；未覆盖字段继续随上级变化，恢复继承会删除局部覆盖。预览、8-bit raster、16-bit TIFF overlay、SVG 与 PDF 使用同一个 resolved style。

## 6. Panel label font/color

Panel Label 支持字体、字号、文字颜色和粗体。Figure 样式负责统一外观，Panel Inspector 可建立局部覆盖并随时恢复继承。Panel 局部样式进入工程保存、自动恢复、模板/画布迁移、撤销/重做、CLI 与全部导出后端。

## 7. Font availability QC

新增 `typography.font-availability`，覆盖 Project/Figure/Panel/Scientific Object override、Annotation 字体、Measurement Label、Scale Bar text 与 Panel Label。缺失字体为 Warning，不阻止普通导出；export report 与投稿包 QC report 会保留警告。Typography consistency 还提示混用 Panel Label 字体/字号，并以 Info 提示 Measurement/Annotation 字体混用。

## 8. Scientific integrity QC

确定性、可审计规则新增或扩展：

- 不同 Asset 的完全相同 SHA-256；
- 同 Source/Frame 的完全相同 crop；
- 同 Source/Frame 超过 90% 的 source-pixel crop 重叠；
- analysis source revision 过期；
- measurement source revision 过期；
- source replacement 后需复核的 Panel/scientific object；
- 非破坏显示参数差异、极端调整和狭窄 crop 的规则提示。

不同 source 或不同 frame 不会被误报为 crop 重复。没有引入神经网络相似度检测、生成式填充、对象移除或任何破坏性像素工具。

## 9. Submission Package

“生成投稿包”只接受全新或空目录，存在 QC Error 时阻止，任何已有文件都不会被覆盖。默认结构为：

```text
SubmissionPackage/
  Figure1/figure1.tif
  Figure1/figure1.svg
  Figure1/figure1.provenance.json
  Figure1/figure1.export-report.html
  Supplement/
  Data/measurements.csv
  Data/measurements.xlsx
  Data/analyses.csv
  Data/analyses.xlsx
  Data/particle-analysis.csv
  Audit/project-audit.json
  Audit/source-manifest.csv
  Audit/qc-report.html
  README.txt
```

`source-manifest.csv` 记录 AssetId、原路径、SHA-256、source revision、宽高、位深、通道、frame、OME、标定和 link state。源图不会被复制进投稿包。

投稿前检查区提供 Sources、Calibration、DPI、Fonts、Panel labels、Scale bars、analysis/measurement revision、export format、Warnings 与 Errors 的明确清单；支持定位 Panel、Source 与过期 Measurement。

## 10. schema 2.3 migration

`ProjectMigrationPipeline.CurrentVersion` 从 `2.2` 升级到 `2.3`，保留 `0.1`、`0.9`、`1.1`、`1.2`、`2.0`、`2.1` 与 `2.2` 读取支持。迁移规则是确定性的：

- 旧 Measurement 的 Fill/Marker stroke/Label color 派生自旧 StrokeColor，Label font 派生自旧 global font；
- Marker fill、Label size/Bold 按 v2.2 实际预览选择兼容默认；
- 旧 Text annotation 的 Color 迁移为 TextColor；
- 旧 shape Color 迁移为 StrokeColor，FillOpacity 固定为 0，避免矩形/椭圆突然变成实心；
- Panel Label 与 Scale Bar 新字段从旧 Figure global style 派生；
- 旧 Measurement source revision 确定为 1。

schema 与 `JsonProjectStore.Validate()` 对字体长度、4–72 pt、0–100% opacity、线宽范围及 HEX pattern 使用一致约束。

## 11. 自动化测试结果

- v2.2 基线：206 tests。
- v2.3 新增：21 tests。
- v2.3 最终：227 passed，0 failed，0 skipped。
- Release solution build：0 warnings，0 errors。

新增覆盖包括独立 Measurement/Annotation 样式、RGB/ARGB 与非法值、v2.2 migration、完整工程往返、Panel 局部继承、PNG/SVG/PDF/16-bit TIFF、缺失字体、重复 SHA/crop/overlap、过期 analysis/measurement revision、Journal presets，以及投稿包完整性、Error 阻止、拒绝覆盖和不复制源图。

## 导出与溯源一致性

- 8-bit raster 从原始文件重新裁剪并应用非破坏参数；文字与 shape 使用语义样式字段。
- 16-bit TIFF 在 RGB48 缓冲中保持科学图像平面，只有 vector-like overlay 经过 8-bit alpha 合成，不先把整张科学图像降为 8-bit。
- SVG annotation text 使用对象自己的 resolved FontFamily；Rectangle/Ellipse 输出真实 `stroke`、`fill`、`fill-opacity`。
- PDF 创建文字 geometry 时使用对象/Panel resolved FontFamily，并通过 ExtGState 保留文字、描边和填充 alpha。
- provenance 记录 source revision、frame、crop、Brightness/Contrast/Gamma/BlackPoint/WhitePoint/Invert/Grayscale/Channel，以及 ROI/Profile/Particle algorithm ID、版本和参数。

## 已知边界与后续方向

- 缺失字体依赖操作系统 fallback，跨机器像素级字形度量可能不同；QC 与报告会明确记录。
- 本版只做确定性完整性规则，不做旋转/镜像的视觉相似检测，也不做黑盒 neural duplicate detector。
- Supplement 目录当前作为明确选择的补充输出保留位，不自动复制源图或未选择文件。
- v2.4 建议继续统一 Presentation adapter 与 Core Scientific Object canonical style、增加 annotation/analysis 的更细粒度 QC 导航、期刊 preset 导入/共享，以及可选的 PDF 字体嵌入策略。

## 安装

安装器面向 Windows 10/11 x64，默认安装到当前用户 `%LOCALAPPDATA%\SciCanvas`，无需管理员权限；便携包可直接解压运行。两者都包含 GUI 与 `SciCanvas.Cli.exe`。安装后可用发布页的 SHA-256 文件复核成品。

发布制品已通过 ZIP 必需项检查、portable CLI 冒烟、隔离安装/卸载冒烟以及 GUI/CLI `2.3.0.0` 文件版本检查。SHA-256：

```text
0F2CA54B00FCD91DC2AA5C0A4420F6BA988795601ED093F839BD4D86ADC79B0A  SciCanvas-v2.3.0-alpha-Setup.exe
0EFE047E0A877561CA9C403692283D75FFD8C75D9D943C4494549C8F57B766C4  SciCanvas-v2.3.0-alpha-Portable.zip
```
