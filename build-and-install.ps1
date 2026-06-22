# Flow Run Finder - Build and Install Script

$ErrorActionPreference = "Stop"
$RepoRoot   = $PSScriptRoot
$ProjectDir = Join-Path $RepoRoot "src\FlowRunFinder"
$ProjectFile = Join-Path $ProjectDir "FlowRunFinder.csproj"
$LibDir     = Join-Path $ProjectDir "lib"
$PluginsDir = "$env:APPDATA\MscrmTools\XrmToolBox\Plugins"

Write-Host ""
Write-Host "=== Flow Run Finder - Build and Install ===" -ForegroundColor Cyan
Write-Host ""

# 1. Check for dotnet
Write-Host "Checking for .NET SDK..." -ForegroundColor Gray
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host "ERROR: dotnet not found. Install the .NET SDK from https://dotnet.microsoft.com/download" -ForegroundColor Red
    pause; exit 1
}
Write-Host "Found .NET SDK $((& dotnet --version))" -ForegroundColor Green

# 2. Get XrmToolBox extensibility DLLs from the local XrmToolBox installation
Write-Host ""
Write-Host "Locating XrmToolBox extensibility DLLs..." -ForegroundColor Gray

$neededDlls = @("XrmToolBox.Extensibility.dll", "McTools.Xrm.Connection.dll")

# Search common XrmToolBox locations
$searchPaths = @(
    "$env:APPDATA\MscrmTools\XrmToolBox",
    "$env:LOCALAPPDATA\XrmToolBox",
    "C:\Program Files\XrmToolBox",
    "C:\Program Files (x86)\XrmToolBox",
    "$env:USERPROFILE\XrmToolBox",
    "$env:USERPROFILE\Downloads\XrmToolBox",
    "$env:USERPROFILE\Desktop\XrmToolBox"
)

$xtbDir = $null
foreach ($path in $searchPaths) {
    if (Test-Path "$path\XrmToolBox.Extensibility.dll") {
        $xtbDir = $path
        break
    }
}

# If not found in fixed paths, do a broader search in common drives
if (-not $xtbDir) {
    Write-Host "Searching for XrmToolBox installation (this may take a moment)..." -ForegroundColor Gray
    $found = Get-ChildItem -Path "$env:USERPROFILE" -Filter "XrmToolBox.Extensibility.dll" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $xtbDir = $found.DirectoryName }
}

if (-not $xtbDir) {
    Write-Host ""
    Write-Host "Could not find XrmToolBox automatically." -ForegroundColor Yellow
    Write-Host "Please enter the full path to the folder containing XrmToolBox.exe:" -ForegroundColor Yellow
    $xtbDir = Read-Host "XrmToolBox folder path"
    if (-not (Test-Path "$xtbDir\XrmToolBox.Extensibility.dll")) {
        Write-Host "ERROR: XrmToolBox.Extensibility.dll not found in: $xtbDir" -ForegroundColor Red
        pause; exit 1
    }
}

Write-Host "Found XrmToolBox at: $xtbDir" -ForegroundColor Green

# Copy needed DLLs into lib\ folder
if (-not (Test-Path $LibDir)) { New-Item -ItemType Directory -Path $LibDir | Out-Null }

foreach ($dll in $neededDlls) {
    $src = Join-Path $xtbDir $dll
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $LibDir $dll) -Force
        Write-Host "  Copied: $dll" -ForegroundColor Gray
    } else {
        Write-Host "WARNING: $dll not found in XrmToolBox folder - build may fail." -ForegroundColor Yellow
    }
}

# 3. Restore NuGet packages (only Microsoft.CrmSdk now)
Write-Host ""
Write-Host "Restoring NuGet packages..." -ForegroundColor Gray
& dotnet restore $ProjectFile
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: NuGet restore failed." -ForegroundColor Red
    pause; exit 1
}

# 4. Build
Write-Host ""
Write-Host "Building plugin..." -ForegroundColor Gray
& dotnet build $ProjectFile -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Build failed. See errors above." -ForegroundColor Red
    pause; exit 1
}
Write-Host "Build succeeded!" -ForegroundColor Green

# 5. Install to XrmToolBox plugins folder
Write-Host ""
Write-Host "Installing to XrmToolBox plugins folder..." -ForegroundColor Gray

if (-not (Test-Path $PluginsDir)) {
    Write-Host "ERROR: Plugins folder not found at: $PluginsDir" -ForegroundColor Red
    pause; exit 1
}

$outputDir = Join-Path $ProjectDir "bin\Release\net48"
Get-ChildItem $outputDir -Filter "FlowRunFinder.dll" | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $PluginsDir $_.Name) -Force
    Write-Host "  Installed: $($_.Name)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "=== Installation complete! ===" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Open or restart XrmToolBox"
Write-Host "  2. Search for 'Flow Run Finder' in the tool list"
Write-Host "  3. Connect to any saved environment and open the tool"
Write-Host ""
pause
