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

# Bundle Microsoft's signed Evergreen bootstrapper; the runtime itself downloads on first install.
$dependencyDirectory = Join-Path $projectDirectory 'artifacts\dependencies'
New-Item -ItemType Directory -Path $dependencyDirectory -Force | Out-Null
$webViewBootstrapper = Join-Path $dependencyDirectory 'MicrosoftEdgeWebview2Setup.exe'
if (-not (Test-Path -LiteralPath $webViewBootstrapper)) {
    Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $webViewBootstrapper
}
$signature = Get-AuthenticodeSignature -LiteralPath $webViewBootstrapper
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    throw 'The WebView2 bootstrapper must have a valid Microsoft Authenticode signature.'
}

if (Test-Path -LiteralPath $publishDirectory) {
    $resolvedPublish = [IO.Path]::GetFullPath($publishDirectory)
    $expectedPublish = [IO.Path]::GetFullPath((Join-Path $projectDirectory 'artifacts\publish'))
    if ($resolvedPublish -ne $expectedPublish -or (Get-Item -LiteralPath $publishDirectory).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        throw 'Refusing to clean an unexpected publish directory.'
    }
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
