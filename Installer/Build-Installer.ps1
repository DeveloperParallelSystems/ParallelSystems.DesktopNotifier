$ErrorActionPreference = 'Stop'

$projectDirectory = Split-Path $PSScriptRoot -Parent
$projectFile = Join-Path $projectDirectory 'ParallelSystems.DesktopNotifier.csproj'
$installerScript = Join-Path $PSScriptRoot 'ParallelSystems.DesktopNotifier.iss'
$publishDirectory = Join-Path $projectDirectory 'artifacts\publish'
$installerDirectory = Join-Path $projectDirectory 'artifacts\installer'

[xml]$project = Get-Content -Raw -LiteralPath $projectFile
$version = [string]$project.Project.PropertyGroup.Version
$installerSource = Get-Content -Raw -LiteralPath $installerScript
if ($installerSource -notmatch '#define MyAppVersion "([^\"]+)"' -or $Matches[1] -ne $version) {
    throw "Installer version must match the project version $version."
}

$isccCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
$isccCandidates = @(
    $isccCommand.Source,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
$isccPath = $isccCandidates | Select-Object -First 1
if (-not $isccPath) {
    throw 'Inno Setup 6 is required. Install it, then run this script again.'
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null

& dotnet publish $projectFile `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Desktop Notifier publish failed with exit code $LASTEXITCODE." }

& $isccPath $installerScript
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed with exit code $LASTEXITCODE." }

$installer = Get-ChildItem -LiteralPath $installerDirectory -Filter "*-$version-win-x64-setup.exe" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $installer) { throw 'Installer compilation completed without producing the expected setup executable.' }

Write-Host "Installer created: $($installer.FullName)" -ForegroundColor Green
