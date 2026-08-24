# SciCanvas v1.2.2-alpha

这是一个针对裁剪交互正确性和响应速度的修复版本，适用于 Windows 10/11 x64。GUI、CLI 和安装器文件版本均为 `1.2.2.0`。

## 下载

- [Windows x64 安装器](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v1.2.2-alpha/SciCanvas-v1.2.2-alpha-Setup.exe)（181,704,894 字节）
- [Windows x64 便携包](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v1.2.2-alpha/SciCanvas-v1.2.2-alpha-Portable.zip)（65,634,851 字节）
- [SHA-256 校验文件](https://github.com/ustcercyy-ui/SciCanvas/releases/download/v1.2.2-alpha/SciCanvas-v1.2.2-alpha-SHA256.txt)

## 本次修复

- 空工程不再在导入提示上方显示默认的 `1200 × 800 px` 裁剪框。
- 修复在源图空白区域按下鼠标后没有进入“新建裁剪框”状态的问题。
- 修复裁剪缩放手柄在鼠标抬起后仍持有鼠标捕获，导致后续移动或调整失灵的问题。
- 裁剪 X、Y、宽、高改为一次性原子更新，每次指针移动只进行一次校验和一次语义变更通知。
- 拖动过程中不再反复捕获和序列化完整撤销快照；鼠标松开时只提交一个可撤销步骤。

## 安装与升级

1. 完全关闭正在运行的 SciCanvas。
2. 下载并运行 `SciCanvas-v1.2.2-alpha-Setup.exe`。
3. 安装器会覆盖更新当前用户目录中的版本，无需先卸载，也无需管理员权限。
4. 默认安装目录为 `%LOCALAPPDATA%\SciCanvas`，并创建开始菜单快捷方式。

安装器目前没有商业代码签名，Windows 可能显示 SmartScreen 提示。建议先使用随 Release 提供的 SHA-256 文件核验下载内容。

PowerShell 校验示例：

```powershell
Get-FileHash .\SciCanvas-v1.2.2-alpha-Setup.exe -Algorithm SHA256
Get-FileHash .\SciCanvas-v1.2.2-alpha-Portable.zip -Algorithm SHA256
```

预期结果：

```text
D6118B7531537654634A0DC131E45730ABCF5CBA227572F4A33EDEEDF6FB340D  SciCanvas-v1.2.2-alpha-Setup.exe
736C101B49676C054036F7CA7B3D23B277478E81404AC90C9D2C31988A518627  SciCanvas-v1.2.2-alpha-Portable.zip
```

## 便携版

解压 `SciCanvas-v1.2.2-alpha-Portable.zip` 后，直接运行 `SciCanvas.App.exe`。命令行用户可以在同一目录运行 `SciCanvas.Cli.exe --help`。便携包为自包含版本，不要求系统预先安装 .NET 10。

## 验证结果

- Release 自动化测试 `142/142` 通过，其中 Core 44 项、Windows/WPF 98 项。
- 新增真实 WPF 鼠标事件回归，覆盖空白画布新建裁剪框和缩放手柄释放捕获。
- 连续 200 次裁剪指针更新在松开前不生成历史快照，松开时只产生一个撤销步骤。
- 安装器在隔离用户目录中实际安装成功，退出码为 `0`。
- 安装后的 GUI、CLI 和开始菜单快捷方式均验证正常。
- 便携包 424 个 ZIP 条目通过路径安全检查。
- 两个发布文件均已按 SHA-256 复算并与校验文件一致。

更完整的发布门禁记录见 [v1.2 发布验收与视觉台账](RELEASE_1.2_QA.md)。
