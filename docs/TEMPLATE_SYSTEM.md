# SciCanvas 材料科研组图模板系统

版本：0.2

## 1. 目标

模板系统将材料领域高水平论文中反复出现的组图逻辑转化为通用、可编辑、可验证的布局。模板学习的是“结构语法”，不复制具体论文的图片、图注、数据、艺术设计或具有独创性的完整构图。

模板由两类可组合对象构成：

1. **期刊规格预设**：画布宽高、最终字号、颜色模式、输出格式和最低分辨率。
2. **科研叙事模板**：面板数量、面板关系、数据角色、标签和检查规则。

例如用户可以选择：

```text
Nature类双栏规格
  + 电催化证据链模板
  + 24 px面板间距
```

## 2. 当前内置模板

### 2.1 通用网格

| ID | 名称 | 典型用途 |
|---|---|---|
| materials.comparison-2x2.nature-double | 通用对照 · 2×2 双栏 | 四组对照或四种表征 |
| general.hero-plus-4 | 主面板 + 四个支持面板 | 一张核心图配四项证据 |
| general.comparison-matrix | 多条件对照矩阵 | 行为样品、列为条件 |

### 2.2 材料科研结构

| ID | 名称 | 插槽逻辑 |
|---|---|---|
| materials.multiscale-morphology.nature-double | 多尺度形貌 · Nature类双栏 | 低倍SEM/高倍SEM/TEM/HRTEM/元素分布 |
| materials.synthesis-structure-performance.nature-double | 制备—结构—性能 · 证据链 | 制备示意/成分物相/微观结构/机理/核心性能 |
| materials.energy-storage-electrochemistry.nature-double | 储能电化学 · 六面板证据链 | 器件/CV/GCD/倍率/循环/EIS |
| materials.phase-structure-mechanism.nature-double | 物相—结构—机理 · 六面板 | 衍射/光谱/表面化学/显微/元素/机理 |
| materials.mechanics-fracture.nature-double | 力学性能—断口 · 五面板 | 应力应变/统计疲劳/断口多尺度/变形机理 |

P1 继续扩展为电催化、光伏、发光/传感、热电、原位时间序列与补充数据模板。

## 3. 期刊规格预设

MVP 预设只作为兼容性辅助，不声称得到出版商认证。每条规则包含来源 URL、访问日期和规则版本。

### 3.1 Nature 类

- 单栏 89 mm。
- 双栏 183 mm。
- 最大高度 170 mm。
- 最终字体通常 5–7 pt。
- 独立面板标签按目标期刊规则设置。
- 主图优先保留可编辑矢量层。

来源：[Nature Research Figure Guide](https://research-figure-guide.nature.com/figures/building-and-exporting-figure-panels/)。

### 3.2 Advanced Materials 类

- 单栏约 85 mm。
- 双栏约 178 mm。
- 位图目标至少 300 dpi。
- 单栏位图建议约 1000 px 宽，双栏约 2100 px 宽。
- 多面板标签位置和字体保持一致。

来源：[Advanced Materials Interfaces Author Guidelines](https://advanced.onlinelibrary.wiley.com/hub/journal/21967350/author-guidelines)。

### 3.3 自定义期刊

用户可输入：

- 单栏/双栏宽度。
- 最大高度。
- 目标 DPI。
- 正文、坐标轴和面板标签字号。
- 接受的文件格式。
- RGB/CMYK要求。

自定义预设保存在用户配置目录，不修改内置模板。

## 4. 模板选择向导

模板入口位于“拼版视图”。向导只问四类信息：

1. 研究方向。
2. 当前图的科学结论。
3. 已有数据类型。
4. 目标期刊和栏宽。

示例：

```text
研究方向：电催化
核心结论：位点结构提高活性并保持稳定
数据：示意图、TEM、XPS、LSV、Tafel、稳定性
期刊：Nature类双栏
```

系统返回不超过三个匹配模板，并说明每个模板的证据链，不使用“投稿成功率”等无法保证的措辞。

## 5. 语义插槽

模板插槽不仅包含矩形，还包含科学角色：

- `mechanism-schematic`
- `macro-photo`
- `sem-overview`
- `sem-detail`
- `tem`
- `hrtem`
- `elemental-map`
- `xrd`
- `xps`
- `spectrum`
- `performance-curve`
- `stability-curve`
- `comparison-chart`
- `time-frame`
- `gel-or-blot`
- `freeform`

插槽可以声明：

- 是否需要比例尺。
- 是否推荐矢量格式。
- 是否锁定宽高比。
- 默认使用包含（contain）或填充（cover）。
- 允许的文件格式。
- 最低有效分辨率。

模板绝不自动裁掉科学内容。`cover` 首次应用时只给出预览，用户必须确认裁剪位置。

## 6. 模板应用流程

```text
选择期刊预设
  → 选择科研叙事模板
  → 创建空白画布和语义插槽
  → 拖入源图/裁剪结果
  → 用户确认每个裁剪区域
  → 自动生成标签和间距
  → 运行合规检查
  → 导出
```

应用模板是一个可撤销命令。它只创建或调整工程对象，不写入源图。

当前实现仅允许在空拼版中切换模板，避免无提示重排已有面板。模板 ID 和比例尺校准参数都写入 `*.scicanvas` 工程，重新打开时按模板恢复。

### 6.1 比例尺校准

比例尺按 `物理长度 ÷ 每像素物理尺寸` 换算为源图像像素，再根据面板的等比包含比例换算到最终导出画布。校准值只能来自图像元数据、显微镜记录或用户明确输入；缺少校准时软件不自动猜测。长度超过裁剪宽度 80% 时视为无效，以便发现单位或数量级错误。

## 7. 检查规则

### 7.1 P0 规则

- 最终有效 DPI 低于预设阈值。
- 文本小于预设最小字号。
- 面板标签缺失、重复或顺序错误。
- 图层发生非等比缩放。
- 比例尺插槽缺少比例尺。
- 时间序列或对照矩阵的图像框尺寸不一致。
- 导出画布超出期刊预设最大高度。
- 源文件指纹未验证。

### 7.2 P1 规则

- 可能重复使用相同源区域。
- 曲线或文字被不必要地栅格化。
- 使用不利于色觉可访问性的颜色组合。
- 显微图比例尺与物理元数据不一致。
- 凝胶/Western blot重排边界未说明。
- 同组图片使用不同的非破坏显示范围。

检查结果分为：

- 错误：无法安全导出，例如目标与源文件冲突。
- 警告：允许继续，但应人工确认。
- 建议：排版或可访问性优化。

## 8. 模板版权与来源策略

允许进入模板仓库的内容：

- 出版商公开的尺寸、格式和标注规范。
- 从多篇论文归纳出的通用网格和叙事结构。
- 自行绘制的空白占位布局。
- 获得明确许可的演示素材。

禁止进入模板仓库的内容：

- 从论文截图裁下来的面板。
- 单篇论文具有高度识别性的完整布局复刻。
- 论文图注、数据、照片或示意图的复制。
- 未授权的期刊徽标或商标性视觉元素。

模板名称使用“Nature类双栏兼容预设”等描述，不使用“官方认证模板”。

## 9. 模板版本和更新

模板文件遵循 [scicanvas-template.schema.json](../schemas/scicanvas-template.schema.json)。

版本字段：

- `schemaVersion`：文件结构版本。
- `version`：模板自身语义版本。
- `publisherRulesVersion`：期刊规则快照版本。
- `provenance.accessedAt`：规范访问日期。

更新原则：

- 更正说明或不影响布局：补丁版本。
- 改变插槽、间距或检查规则：次版本。
- 破坏旧工程兼容性：主版本并保留迁移器。

打开旧工程时继续使用工程记录的模板快照。用户可以主动执行“升级模板”，软件展示差异后再应用。

## 10. 开源模板仓库建议

```text
templates/
  publishers/
    nature-like.json
    advanced-materials-like.json
  builtin/
    multiscale-morphology.nature-double.json
    comparison-2x2.nature-double.json
    synthesis-structure-performance.nature-double.json
  community/
  tests/
    fixtures/
schemas/
  scicanvas-template.schema.json
```

社区模板合并前必须：

1. 通过 JSON Schema。
2. 不引用本机路径。
3. 不包含论文图片或商标资源。
4. 声明规则来源。
5. 通过布局边界和标签唯一性测试。
