param(
    [string]$Version = "2.4.0-alpha.1",
    [string]$FileVersion = "2.4.0.1"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$workRoot = Join-Path $artifactsRoot "release-$Version-work"
$portableRoot = Join-Path $workRoot "portable"
$installerPayloadRoot = Join-Path $workRoot "installer-payload"
$setupPublishRoot = Join-Path $workRoot "setup-publish"
$payloadArchive = Join-Path $workRoot "SciCanvas.Payload.zip"
$portableArchive = Join-Path $artifactsRoot "SciCanvas-v$Version-Portable.zip"
$setupArtifact = Join-Path $artifactsRoot "SciCanvas-v$Version-Setup.exe"
$hashArtifact = Join-Path $artifactsRoot "SciCanvas-v$Version-SHA256.txt"

foreach ($target in @($workRoot, $portableArchive, $setupArtifact, $hashArtifact)) {
    if (Test-Path -LiteralPath $target) {
        throw "发布目标已存在，为避免覆盖已生成制品，构建已停止：$target"
    }
}

New-Item -ItemType Directory -Path $portableRoot -Force | Out-Null
New-Item -ItemType Directory -Path $installerPayloadRoot -Force | Out-Null
New-Item -ItemType Directory -Path $setupPublishRoot -Force | Out-Null

function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet 命令失败，退出码：$LASTEXITCODE"
    }
}

$commonVersionProperties = @(
    "-p:Version=$Version",
    "-p:InformationalVersion=$Version",
    "-p:IncludeSourceRevisionInInformationalVersion=false",
    "-p:AssemblyVersion=$FileVersion",
    "-p:FileVersion=$FileVersion"
)

$publishAppArguments = @(
    "publish",
    (Join-Path $repositoryRoot "src\SciCanvas.App\SciCanvas.App.csproj"),
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "--no-restore",
    "--output", $portableRoot
) + $commonVersionProperties
Invoke-DotNet $publishAppArguments

$publishCliArguments = @(
    "publish",
    (Join-Path $repositoryRoot "src\SciCanvas.Cli\SciCanvas.Cli.csproj"),
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "--no-restore",
    "--output", $portableRoot
) + $commonVersionProperties
Invoke-DotNet $publishCliArguments

Copy-Item -LiteralPath (Join-Path $PSScriptRoot "README.txt") -Destination $portableRoot
Compress-Archive -Path (Join-Path $portableRoot "*") -DestinationPath $portableArchive -CompressionLevel Optimal

Copy-Item -Path (Join-Path $portableRoot "*") -Destination $installerPayloadRoot -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Install-SciCanvas.cmd") -Destination $installerPayloadRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Install-SciCanvas.ps1") -Destination $installerPayloadRoot
Compress-Archive -Path (Join-Path $installerPayloadRoot "*") -DestinationPath $payloadArchive -CompressionLevel Optimal

$publishSetupArguments = @(
    "publish",
    (Join-Path $PSScriptRoot "SciCanvas.Setup\SciCanvas.Setup.csproj"),
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "--no-restore",
    "--output", $setupPublishRoot,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:PayloadPath=$payloadArchive"
) + $commonVersionProperties
Invoke-DotNet $publishSetupArguments

$setupExecutable = Join-Path $setupPublishRoot "SciCanvas.Setup.exe"
if (!(Test-Path -LiteralPath $setupExecutable)) {
    throw "安装器项目未生成 SciCanvas.Setup.exe。"
}
Copy-Item -LiteralPath $setupExecutable -Destination $setupArtifact

$requiredPortableFiles = @(
    "SciCanvas.App.exe",
    "SciCanvas.Cli.exe",
    "SciCanvas.App.dll",
    "SciCanvas.Cli.dll",
    "SciCanvas.Persistence.dll",
    "SciCanvas.Imaging.dll",
    "README.txt"
)
foreach ($required in $requiredPortableFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $portableRoot $required))) {
        throw "便携包缺少必要文件：$required"
    }
}

$appVersion = (Get-Item -LiteralPath (Join-Path $portableRoot "SciCanvas.App.exe")).VersionInfo.FileVersion
$cliVersion = (Get-Item -LiteralPath (Join-Path $portableRoot "SciCanvas.Cli.exe")).VersionInfo.FileVersion
$setupVersion = (Get-Item -LiteralPath $setupArtifact).VersionInfo.FileVersion
if ($appVersion -ne $FileVersion -or $cliVersion -ne $FileVersion -or $setupVersion -ne $FileVersion) {
    throw "制品版本不一致：GUI=$appVersion CLI=$cliVersion Setup=$setupVersion，预期=$FileVersion"
}

$hashLines = @($setupArtifact, $portableArchive) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    "$($hash.Hash)  $([System.IO.Path]::GetFileName($_))"
}
[System.IO.File]::WriteAllLines($hashArtifact, $hashLines, [System.Text.UTF8Encoding]::new($false))

[pscustomobject]@{
    Version = $Version
    FileVersion = $FileVersion
    Setup = $setupArtifact
    SetupBytes = (Get-Item -LiteralPath $setupArtifact).Length
    Portable = $portableArchive
    PortableBytes = (Get-Item -LiteralPath $portableArchive).Length
    Hashes = $hashArtifact
}
