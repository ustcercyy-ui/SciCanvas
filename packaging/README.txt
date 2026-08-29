SciCanvas v2.4.0-alpha Windows x64 阶段安装包

双击 SciCanvas Setup 可打开安装向导，自选安装位置，并选择是否创建开始菜单/桌面快捷方式以及安装后是否启动应用。推荐安装位置为：
%LOCALAPPDATA%\SciCanvas

便携包无需安装：解压到一个新目录后，直接运行 SciCanvas.App.exe；自动化导出可运行 SciCanvas.Cli.exe。

Setup 安装程序不会要求管理员权限，也不会修改原始科研图像。安装目录同时包含可用于自动化导出的 SciCanvas.Cli.exe。
卸载：在安装目录运行 Uninstall-SciCanvas.ps1。

这是自包含 win-x64 版本，已包含 .NET 10 Windows Desktop 运行时，不需要另外安装 .NET。

本阶段版已完成 v2.4 路线图 PR1–PR12：科学对象、多尺度标尺、原始多通道平面、Linked Views、Registration、ROI Propagation、Scientific Integrity QC、可移植出版、统一 Export/Provenance 与 schema 2.4 migration。

当前明确限制：不宣称完整 OME-TIFF/CZI/LIF/ND2/DM3/DM4/Bio-Formats；任意 affine warp 尚未作为像素级重采样 composite；内置 PDF writer 的可靠路径为文字轮廓，尚未实现可验证的字体子集嵌入与 ToUnicode。
