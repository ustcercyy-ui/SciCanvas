# SciCanvas v2.0.1-alpha

这是 Scientific Figure Workspace V2 的交互改进版本，适用于 Windows 10/11 x64。GUI、CLI 和安装器文件版本均为 `2.0.1.0`，产品语义版本均为 `2.0.1-alpha`。

## 下载

- [Windows x64 安装器](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.0.1-alpha/SciCanvas-v2.0.1-alpha-Setup.exe)（192,694,462 字节）
- [Windows x64 便携包](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.0.1-alpha/SciCanvas-v2.0.1-alpha-Portable.zip)（76,628,389 字节）
- [SHA-256 校验文件](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v2.0.1-alpha/SciCanvas-v2.0.1-alpha-SHA256.txt)

## 本次更新

- 顶部命令区、左侧 Asset Library、右侧检查器和底部测量表新增独立收放按钮；收起后会重新适配当前图像或 Figure 画布。
- 测量检查器和科研标注检查器新增 Windows 系统取色器，保留当前 Alpha 通道并写回 `#AARRGGBB`。
- 连续创建测量对象时继承上一对象的颜色、线宽、线型、端点大小、端点显示、标签显示和填充透明度；连续创建 Figure 标注时继承颜色、线宽、字号与粗体。
- 新建或打开其他工程不再受当前工程锁定。存在未保存更改时明确提供“保存 / 放弃 / 取消”三种选择；取消保存或选择取消都会保留当前工程。
- 已有内容时可以中途切换拼图模板。普通面板迁移到新模板插槽，额外面板保留为 inset；标注、参考线、图层选择、比例尺、样式和科学有效性状态一并迁移。
- 模板检查器新增 100–20,000 px 自定义画布宽高。自定义尺寸可保存、重开、撤销与重做，现有面板、标注和参考线按比例迁移。

## 验证记录

| 检查 | 结果 |
| --- | --- |
| Windows/WPF 自动化测试 | 111/111 通过 |
| Core 自动化测试 | 52/52 通过 |
| 新增重点回归 | 7/7 通过：四区收放、测量样式继承、标注样式继承、模板切换、自定义尺寸、新建工程、打开其他工程 |
| WPF 视觉证据 | Figure 与测量工作区截图测试 2/2 通过 |
| 发布版 CLI | `SciCanvas.Cli.exe --help` 返回 0 |
| 便携包结构 | 497 个条目；GUI、CLI、WinForms 取色依赖与安装脚本齐全 |
| 隔离安装冒烟 | Setup 返回 0；GUI/CLI 均为 2.0.1.0；卸载脚本与开始菜单快捷方式生成成功 |
| 二进制源版本 | `9f6bd690ed460dec474038e8cb8b423617bdd21f` |

安装冒烟测试通过重定向的临时 `LOCALAPPDATA` / `APPDATA` 完成，没有覆盖实际用户安装；验证后临时目录已删除。

## 安装与升级

1. 关闭正在运行的 SciCanvas。
2. 下载并运行 `SciCanvas-v2.0.1-alpha-Setup.exe`。
3. 安装器写入当前用户的 `%LOCALAPPDATA%\SciCanvas`，无需管理员权限。
4. 也可以下载便携包，解压后直接运行 `SciCanvas.App.exe` 或 `SciCanvas.Cli.exe`；便携包已包含 .NET 10 Windows Desktop Runtime。

## SHA-256

```text
E86BF54997A38E2133B5E3491FCEECDE8FDE553528137553359CDE9AE3498FCA  SciCanvas-v2.0.1-alpha-Setup.exe
0C09F93250A5E47D457E20B22D54AE7E1EABB20AE24E461247C7987C535E8E58  SciCanvas-v2.0.1-alpha-Portable.zip
```
