SciCanvas v2.4.0-alpha.2 Windows x64 阶段安装包

运行 Install-SciCanvas.cmd 即可安装到当前用户的：
%LOCALAPPDATA%\SciCanvas

安装程序不会要求管理员权限，也不会修改原始科研图像。安装完成后会创建开始菜单快捷方式“ SciCanvas ”；安装目录同时包含可用于自动化导出的 SciCanvas.Cli.exe。
卸载：在安装目录运行 Uninstall-SciCanvas.ps1。

这是自包含 win-x64 版本，已包含 .NET 10 Windows Desktop 运行时，不需要另外安装 .NET。

本阶段版交付 v2.4 路线图 PR1–PR5：正确性闭环、尺寸语义与多尺度标尺、科学对象、原始多通道平面和多通道工作区。PR6–PR12 仍按路线图继续开发。

本热修订修复右侧检查器、图层和通道页面同时显示导致的文字/列表叠层，并为深色工作区提供一致的滚动条样式。
