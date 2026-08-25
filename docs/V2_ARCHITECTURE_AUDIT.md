# Scientific Figure Workspace V2 — Architecture Audit

审查基线：SciCanvas `1.2.2-alpha`，.NET 10 / WPF / MVVM，模块化单体解决方案。

## 当前架构

- 桌面壳：`SciCanvas.App`（WPF）。
- UI 状态：`SciCanvas.Presentation`，以 `MainWindowViewModel`、`FigureCanvasViewModel`、`FigurePanelViewModel` 为中心。
- 科研与导出规则：`SciCanvas.Core`。
- 预览、像素处理与最终渲染：`SciCanvas.Imaging`。
- JSON 工程与恢复：`SciCanvas.Persistence`。
- Windows 只读源文件访问：`SciCanvas.Platform.Windows`。
- Figure 模板：`SciCanvas.Templates`。
- 测试：xUnit，Core 与 Windows/WPF 集成测试分开。

现有代码没有引入第三方 canvas/store 框架；画布由 WPF 元素与 ViewModel 几何状态驱动。高频拖动在 code-behind / 局部交互状态中处理，持久化文档状态通过 ViewModel 快照进入统一历史。

## 十个重点问题

1. **Source Image 与 Figure Panel 是否分离？**

   部分分离。`SourceAsset` 是只读源文件记录，`FigurePanelViewModel` 保存对 Source 的引用、`SourceRect` 和独立的 destination geometry；一个 Source 可以被多个 Panel 使用。但 Panel 仍是 UI/ViewModel 对象，缺少独立的 project/figure domain aggregate、mm frame、fit mode 和 scientific-object IDs。

2. **Crop 保存在哪个坐标系？**

   当前保存源图整数像素 `PixelRect64`。这是科学安全的 source coordinate，不是屏幕坐标或渲染 bitmap。V2 将在 domain 层增加 source-normalized `NormalizedRect`，并在现有 source-pixel persistence/export 边界做无损转换。

3. **Calibration 与 Scale Bar 是否真正关联？**

   运行时会用 `SpatialCalibration` 更新 Panel 的 `PhysicalUnitsPerSourcePixel`，但 Scale Bar 实际复制了 X 轴标定标量，未保存 calibration/source revision 引用，也没有独立 scientific-object validity。替换源后只会关闭或重算 UI 字段，审计性不足。

4. **Measurement 是否绑定 source coordinate？**

   是。`ScientificMeasurement` 保存 `SourceAssetId` 与 source-pixel geometry，物理值由 geometry + `SpatialCalibration` 重新计算，显示字符串不是唯一数据源。

5. **Panel resize 是否会影响真实测量值？**

   当前不会。Measurement 与 Panel destination geometry 无耦合；Panel resize 只改变 destination size 与 effective DPI。V2 新增了显式不变量测试。

6. **是否存在统一 scientific-object abstraction？**

   不存在。Measurement、Scale Bar、Annotation、Inset、Color 等分散在不同 ViewModel/导出结构中。V2 新增带 `kind`、source revision、style override 与 validity 的 `ScientificObject` 层次。

7. **Undo/Redo 在哪一层？**

   `MainWindowViewModel` 捕获完整 `EditorHistorySnapshot`，`EditorHistoryManager` 管理最近 100 步；gesture 可以合并，批量对齐已能单步撤销。结构可复用，但 domain mutation 入口仍分散，后续应逐步收敛为 transaction/command 边界。

8. **Export 是 canvas snapshot 还是 source 重建？**

   最终导出会重新读取源图精确 crop，应用非破坏调整，再叠加 label/scale bar/annotation；不是从 UI preview snapshot 导出。该管线应保留并通过 `IImageEngine` 抽象解耦 UI 与具体 WPF imaging implementation。

9. **可直接复用的模块？**

   `SourceAsset`/fingerprint/read-only reader、`SpatialCalibration`、`ScientificMeasurement`、`PixelRect64`、crop validation、WPF preview/final exporter、模板 layout engine、project recovery/atomic save、统一历史、现有 align/distribute/snap 交互、现有 QC 导航入口与大量测试夹具。

10. **必须先 refactor 的部分？**

    必须先建立 Project/Asset/Figure/Panel/ScientificObject/ProjectStyle/QCResult domain 边界；显式区分 source/panel/figure coordinate；把 effective DPI、replace validity、style resolution、source tracking 与 QC 规则移出 React/WPF component/ViewModel；为 project schema v2 与 migration 建立入口；再把现有 WPF ViewModel 逐步映射到这些纯领域服务。

## 关键风险

- `MainWindowViewModel` 和 `MainWindow.xaml` 都过大，V2 UI 不应继续把所有工作区状态堆入单一文件。
- 当前只有一个 Figure canvas；V2 的多 Figure project 需要新增 aggregate 与 selector，而不是把模板切换伪装成 Figure 列表。
- destination geometry 仍以导出像素保存，无法表达与 DPI 无关的真实 Figure mm layout。
- `FigurePreflight` 将 300 DPI 写死，规则不可配置，也没有通用 `IQcRule`。
- project 文件虽然有 `schemaVersion`，但 loader 只有版本白名单，没有显式 migration pipeline。

## V2 稳定化决策

1. 保留 .NET 10 / WPF / MVVM 与现有模块化单体。
2. 在 `SciCanvas.Core.Workspace` 新建纯领域模型和派生 selector。
3. 现有 `PixelRect64` 继续作为 source/export 边界；V2 Panel domain 使用 normalized crop 与 mm frame。
4. 引入 `IImageEngine`、独立 QC engine、Project Style resolver、source revision 与 replacement validity propagation。
5. 先用单元测试锁定科学不变量，再接入现有 ViewModel/persistence/UI。
