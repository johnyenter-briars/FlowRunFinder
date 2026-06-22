# publish.ps1 — Build, pack, and (optionally) push FlowRunFinder to NuGet
# Usage:
#   .\publish.ps1                   # build + pack only
#   .\publish.ps1 -Push             # build + pack + push to NuGet
#   .\publish.ps1 -ApiKey "abc..."  # supply key inline (otherwise prompted)

param(
    [switch]$Push,
    [string]$ApiKey
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot    = $PSScriptRoot
$ProjectDir  = Join-Path $RepoRoot "src\FlowRunFinder"
$ProjectFile = Join-Path $ProjectDir "FlowRunFinder.csproj"
$NuspecFile  = Join-Path $ProjectDir "FlowRunFinder.nuspec"
$NugetExe    = Join-Path $RepoRoot "tools\nuget.exe"

# ---------------------------------------------------------------------------
# 1. Ensure nuget.exe is available
# ---------------------------------------------------------------------------
if (-not (Test-Path $NugetExe)) {
    Write-Host "Downloading nuget.exe..."
    New-Item -ItemType Directory -Force -Path (Split-Path $NugetExe) | Out-Null
    Invoke-WebRequest "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $NugetExe
}

# ---------------------------------------------------------------------------
# 2. Build Release (ILRepack runs automatically as part of the build)
# ---------------------------------------------------------------------------
Write-Host "`n--- Building Release ---"
dotnet build $ProjectFile -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# ---------------------------------------------------------------------------
# 3. Remove old packages and create a fresh one
# ---------------------------------------------------------------------------
Write-Host "`n--- Packing ---"
Get-ChildItem $RepoRoot -Filter "FlowRunFinder.*.nupkg" | Remove-Item -Force

Push-Location $ProjectDir
& $NugetExe pack $NuspecFile -OutputDirectory $RepoRoot -NoDefaultExcludes
$packExitCode = $LASTEXITCODE
Pop-Location
if ($packExitCode -ne 0) { throw "nuget pack failed." }

$Package = Get-ChildItem $RepoRoot -Filter "FlowRunFinder.*.nupkg" | Select-Object -First 1
Write-Host "`nPackage created: $($Package.Name)  ($([math]::Round($Package.Length/1KB, 1)) KB)"

# ---------------------------------------------------------------------------
# 4. Optionally push to NuGet.org
# ---------------------------------------------------------------------------
if ($Push) {
    if (-not $ApiKey) {
        $ApiKey = Read-Host "Enter your NuGet API key"
    }
    Write-Host "`n--- Pushing to NuGet.org ---"
    & $NugetExe push $Package.FullName $ApiKey -Source https://api.nuget.org/v3/index.json
    if ($LASTEXITCODE -ne 0) { throw "nuget push failed." }
    Write-Host "Done. Package published — allow a few minutes for the Plugin Store to index it."
}
