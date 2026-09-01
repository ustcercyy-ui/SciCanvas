SciCanvas v2.5.0-alpha Windows x64 预览安装包

双击 SciCanvas Setup 可打开安装向导，自选安装位置，并选择是否创建开始菜单/桌面快捷方式以及安装后是否启动应用。推荐安装位置为：
%LOCALAPPDATA%\SciCanvas

便携包无需安装：解压到一个新目录后，直接运行 SciCanvas.App.exe；自动化导出可运行 SciCanvas.Cli.exe。

Setup 安装程序不会要求管理员权限，也不会修改原始科研图像。安装目录同时包含可用于自动化导出的 SciCanvas.Cli.exe。
卸载：在安装目录运行 Uninstall-SciCanvas.ps1。

这是自包含 win-x64 版本，已包含 .NET 10 Windows Desktop 运行时，不需要另外安装 .NET。

本预览版在 v2.4 科研组图能力上新增 Scientific Data、Plot Workspace 与 Plot → Figure 原生面板；表格数据、过滤/变换、绘图样式、数据溯源和 Plot 矢量导出统一进入工程、QC 与投稿链路。工程 schema 为 3.0。

当前明确限制：不宣称完整 OME-TIFF/CZI/LIF/ND2/DM3/DM4/Bio-Formats；不提供 3D volume rendering、EBSD processing、TEM 自动晶体索引、完整 Origin/Excel 替代或复杂 nonlinear fitting suite。PDF 对许可允许且可可靠映射的 TrueType 字体执行子集嵌入，否则按策略阻止或回退为文字轮廓。
