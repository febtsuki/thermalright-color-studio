param([string]$Dotnet = 'dotnet', [string]$OutputDirectory = (Join-Path $PSScriptRoot 'publish'))
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$project = Join-Path $PSScriptRoot 'src/ThermalrightColorStudio/ThermalrightColorStudio.csproj'
& $Dotnet restore $project --locked-mode -r win-x64
if ($LASTEXITCODE -ne 0) { throw '依赖还原失败' }
& $Dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw '发布失败' }
foreach ($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','Launch.ps1','Launch.cmd','Manage-Autostart.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $OutputDirectory -Force
}
if (Test-Path (Join-Path $PSScriptRoot 'licenses')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $OutputDirectory -Recurse -Force }
Write-Host "发布完成：$OutputDirectory/ThermalrightColorStudio.exe"
