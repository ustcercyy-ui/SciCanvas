param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA "SciCanvas"),
    [switch]$NoStartMenuShortcut,
    [switch]$CreateDesktopShortcut
)

$ErrorActionPreference = "Stop"

$InstallRoot = [Environment]::ExpandEnvironmentVariables($InstallRoot)
$InstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
$startMenuRoot = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$startMenuShortcutPath = Join-Path $startMenuRoot "SciCanvas.lnk"
$desktopRoot = [Environment]::GetFolderPath("DesktopDirectory")
$desktopShortcutPath = Join-Path $desktopRoot "SciCanvas.lnk"
$appExecutable = Join-Path $InstallRoot "SciCanvas.App.exe"
$sourceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

$running = Get-Process -Name "SciCanvas.App" -ErrorAction SilentlyContinue
if ($running) {
    throw "SciCanvas is running. Close it before installation."
}

New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
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
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $InstallRoot $_.Name) -Recurse -Force
    }

function New-SciCanvasShortcut([string]$ShortcutPath) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $appExecutable
    $shortcut.WorkingDirectory = $InstallRoot
    $shortcut.Description = "SciCanvas scientific figure workspace"
    $shortcut.Save()
}

if ($NoStartMenuShortcut) {
    if (Test-Path -LiteralPath $startMenuShortcutPath) {
        Remove-Item -LiteralPath $startMenuShortcutPath -Force
    }
}
else {
    New-SciCanvasShortcut $startMenuShortcutPath
}

if ($CreateDesktopShortcut) {
    New-SciCanvasShortcut $desktopShortcutPath
}
elseif (Test-Path -LiteralPath $desktopShortcutPath) {
    Remove-Item -LiteralPath $desktopShortcutPath -Force
}

$installRootLiteral = $InstallRoot.Replace("'", "''")
$startMenuShortcutLiteral = $startMenuShortcutPath.Replace("'", "''")
$desktopShortcutLiteral = $desktopShortcutPath.Replace("'", "''")
$uninstallScript = @"
`$ErrorActionPreference = "Stop"
`$installRoot = '$installRootLiteral'
`$startMenuShortcutPath = '$startMenuShortcutLiteral'
`$desktopShortcutPath = '$desktopShortcutLiteral'
if (Get-Process -Name "SciCanvas.App" -ErrorAction SilentlyContinue) {
    throw "SciCanvas is running. Close it before uninstallation."
}
if (Test-Path -LiteralPath `$startMenuShortcutPath) { Remove-Item -LiteralPath `$startMenuShortcutPath -Force }
if (Test-Path -LiteralPath `$desktopShortcutPath) { Remove-Item -LiteralPath `$desktopShortcutPath -Force }
if (Test-Path -LiteralPath `$installRoot) { Remove-Item -LiteralPath `$installRoot -Recurse -Force }
Write-Host "SciCanvas has been uninstalled."
"@
Set-Content -LiteralPath (Join-Path $InstallRoot "Uninstall-SciCanvas.ps1") -Value $uninstallScript -Encoding UTF8

Write-Host "SciCanvas installed to: $InstallRoot"
if (!$NoStartMenuShortcut) {
    Write-Host "Start menu shortcut: $startMenuShortcutPath"
}
if ($CreateDesktopShortcut) {
    Write-Host "Desktop shortcut: $desktopShortcutPath"
}
