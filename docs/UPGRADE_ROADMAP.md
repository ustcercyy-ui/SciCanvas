# SciCanvas 分阶段完善升级路线

本路线以《科研论文图片编辑与组图软件功能方案》为产品需求来源，结合当前 `v1.1.0-alpha` 代码基线制定。方案中的内容被视为需求与优先级，不作为会自动执行的外部指令。

## 实施状态（v1.2.0-alpha）

阶段 0–6 已完成。标定、测量、比例尺、组图、Figure QC、科学分析和可审计辅助分析已形成同一工程数据链；发布验收记录见 `RELEASE_1.2_QA.md`。

## 当前基线

现有版本已经具备：只读导入、像素级无损裁剪、工程恢复、撤销/重做、拼版模板、Panel 标签、多选对齐与等距分布、文字/箭头/矩形/椭圆标注、面板比例尺、TIFF/PNG/PDF/SVG 导出、投稿预检、溯源 sidecar、OME-TIFF 元数据、多页 TIFF、16-bit TIFF、投稿预设、CLI 与自包含安装包。

路线启动时的主要缺口是方案中最重要的底层数据链尚未完整建立：

```text
Calibration → Measurement → Scale Bar
```

该缺口已在 v1.2 中补齐：源图级 X/Y 标定统一驱动测量与比例尺，测量表、统计、Figure QC 和科研诚信规则已经进入工程、撤销/重做和审计轨迹。

## 阶段 0：基线审计与视觉规范（已完成）

- 建立方案—代码差距矩阵。
- 固定全量测试基线。
- 建立标定/测量主工作区和 Figure QC/投稿导出两张视觉规范。
- 重新生成真实 WPF 截图；旧的 `artifacts/SciCanvas-ui-smoke.png` 内容不是 SciCanvas，不能作为验收证据。

验收：差距可追踪、测试通过、视觉规范入库、真实截图链路可复现。

## 阶段 1：科研数据底座（P0，已完成）

- 每张源图保存独立的 X/Y 空间标定、单位、来源和参考线。
- 自动使用 metadata 标定；支持已知距离的手动标定。
- Length、Angle、Rectangle ROI 测量对象与画布交互。
- 测量表、选中联动、真实单位优先显示、复制与 CSV 导出。
- 标定自动驱动同源 Figure Panel 的 Scale Bar。
- 标定与测量进入工程文件、自动保存、撤销/重做和审计轨迹。

验收：手动标定后测量值与比例尺使用同一数据源；旧工程可打开；新工程可无损往返。

## 阶段 2：组图与全局样式闭环（P0，已完成）

- Grid/Mosaic 快速布局、Match Width/Height/Frame/Aspect。
- Line 标注、Unicode/希腊字母/上下标输入体验。
- Global Figure Style：字体、字号、线宽、Panel label、Scale Bar 样式一次修改全局同步。
- Scale Bar Validator 增加非等比缩放、单位与复制丢失检查。

验收：多面板统一样式与排版可一键完成，所有变化可撤销并进入工程记录。

## 阶段 3：投稿闭环与 Figure QC（P0，已完成）

- 期刊物理尺寸、单栏/双栏/整页与实时有效 DPI。
- 独立 Figure QC：Layout、Typography、Resolution、Scale、Panel、Color、Export。
- TIFF/PNG/PDF 投稿导出对话与逐项说明；字体/矢量/颜色模式状态明确可见。
- QC 问题可定位到具体 Panel；错误阻止导出，提醒允许显式忽略并记录。

验收：一次检查得到结构化结果，定位并修复后生成投稿文件与完整溯源报告。

## 阶段 4：P1 科学分析与批量效率（已完成）

- Polyline、Circle、Area、Perimeter、多次测量统计、Histogram、Intensity Profile。
- Inset 与动态 ROI 关联。
- Linked Crop/Zoom/ROI/Scale。
- 批量处理、Journal Presets、Scientific Color Manager、Excel/SVG 导出。

验收：同一批数据可使用一致参数处理，统计结果可复核、可导出、可回到图上定位。

## 阶段 5：P2 可审计智能辅助（已完成）

- 自动颗粒/晶粒/片层/孔隙/相分数/裂纹候选识别。
- AI Layout、Style Harmonization、Figure QC 与科研诚信风险检测。
- 所有自动结果保留人工校正、接受/拒绝和模型/参数记录。
- AI 不执行生成式填充、局部擦除或对象移除等科研高风险修改。

验收：智能结果默认是建议而非最终事实；每次人工确认都可追溯。

## 阶段 6：发布收口（已完成）

- 全量单元/集成/像素/DPI/工程迁移回归。
- 真实 WPF 核心路径截图与视觉差异台账。
- 性能、键盘可访问性、125%–250% 缩放检查。
- README、架构、Schema、CLI、安装包与 SHA-256 同步发布。

验收：GUI、CLI、工程格式、安装包和文档版本一致；无未解释的测试或视觉偏差。
