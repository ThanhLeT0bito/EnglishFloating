param(
    [string]$OutputPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'release')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/PteFloatingSentence.Windows/PteFloatingSentence.Windows.csproj'

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $OutputPath `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false

Write-Host "Published EnglishFloating.exe to $OutputPath"
