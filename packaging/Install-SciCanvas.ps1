$ErrorActionPreference = "Stop"

$installRoot = Join-Path $env:LOCALAPPDATA "SciCanvas"
$startMenuRoot = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcutPath = Join-Path $startMenuRoot "SciCanvas.lnk"
$appExecutable = Join-Path $installRoot "SciCanvas.App.exe"
$sourceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

$running = Get-Process -Name "SciCanvas.App" -ErrorAction SilentlyContinue
if ($running) {
    throw "SciCanvas 正在运行，请先关闭后再安装。"
}

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
New-Item -ItemType Directory -Path $startMenuRoot -Force | Out-Null

$excludedNames = @(
    "Install-SciCanvas.ps1",
    "Install-SciCanvas.cmd",
    "Uninstall-SciCanvas.ps1",
    "README.txt"
)

Get-ChildItem -LiteralPath $sourceRoot -Force |
    Where-Object { $excludedNames -notcontains $_.Name } |
    ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $installRoot $_.Name) -Recurse -Force
    }

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $appExecutable
$shortcut.WorkingDirectory = $installRoot
$shortcut.Description = "SciCanvas 科研组图工作台"
$shortcut.Save()

$uninstallScript = @"
`$ErrorActionPreference = "Stop"
`$installRoot = Join-Path `$env:LOCALAPPDATA "SciCanvas"
`$shortcutPath = Join-Path `$env:APPDATA "Microsoft\Windows\Start Menu\Programs\SciCanvas.lnk"
if (Get-Process -Name "SciCanvas.App" -ErrorAction SilentlyContinue) {
    throw "SciCanvas 正在运行，请先关闭后再卸载。"
}
if (Test-Path -LiteralPath `$shortcutPath) { Remove-Item -LiteralPath `$shortcutPath -Force }
if (Test-Path -LiteralPath `$installRoot) { Remove-Item -LiteralPath `$installRoot -Recurse -Force }
Write-Host "SciCanvas 已卸载。"
"@
Set-Content -LiteralPath (Join-Path $installRoot "Uninstall-SciCanvas.ps1") -Value $uninstallScript -Encoding UTF8

Write-Host "SciCanvas 已安装到：$installRoot"
Write-Host "开始菜单快捷方式：$shortcutPath"

