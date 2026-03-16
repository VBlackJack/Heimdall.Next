<#
.SYNOPSIS
    Build script for Heimdall.Next -produces portable distributions.

.PARAMETER Mode
    Build mode: 'Debug' (default) or 'Release'.

.PARAMETER SkipTests
    Skip running tests before build.

.EXAMPLE
    .\Build.ps1                    # Debug build
    .\Build.ps1 -Mode Release      # Release build with installer prep

.NOTES
    Copyright 2026 Julien Bombled
    Licensed under the Apache License, Version 2.0
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Mode = 'Debug',

    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = $PSScriptRoot
$AppProject = Join-Path $ProjectRoot 'src\Heimdall.App\Heimdall.App.csproj'
$SolutionFile = Get-ChildItem -Path $ProjectRoot -Filter '*.slnx' | Select-Object -First 1

# ── Build number: YYYY.MMDDxx (xx = sequential within day) ──────────────────

$today = Get-Date
$datePrefix = $today.ToString('yyyy.MMdd')
$distDir = Join-Path $ProjectRoot "Dist\$($Mode.ToLower())"

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

# Find highest existing build number for today
$existingBuilds = Get-ChildItem -Path $distDir -Directory -Filter "Heimdall.Next_build.${datePrefix}*" -ErrorAction SilentlyContinue |
    ForEach-Object {
        if ($_.Name -match "build\.${datePrefix}(\d{2})$") { [int]$Matches[1] }
    } | Sort-Object -Descending

$sequence = if ($existingBuilds.Count -gt 0) { $existingBuilds[0] + 1 } else { 1 }
$buildNumber = "{0}{1:D2}" -f $datePrefix, $sequence
$buildFolder = "Heimdall.Next_build.${buildNumber}"
$outputDir = Join-Path $distDir $buildFolder

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Heimdall.Next Build v${buildNumber}" -ForegroundColor Cyan
Write-Host "  Mode: $Mode" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ── Update version in csproj ────────────────────────────────────────────────

$csprojContent = Get-Content $AppProject -Raw
$csprojContent = $csprojContent -replace '<Version>[^<]+</Version>', "<Version>${buildNumber}</Version>"
[System.IO.File]::WriteAllText($AppProject, $csprojContent, [System.Text.UTF8Encoding]::new($false))
Write-Host "[1/5] Version set to $buildNumber" -ForegroundColor Green

# ── Run tests ───────────────────────────────────────────────────────────────

if (-not $SkipTests) {
    Write-Host "[2/5] Running tests..." -ForegroundColor Yellow
    $testResult = dotnet test $SolutionFile.FullName --verbosity quiet --nologo 2>&1
    $testExitCode = $LASTEXITCODE
    if ($testExitCode -ne 0) {
        Write-Host "Tests FAILED. Aborting build." -ForegroundColor Red
        $testResult | ForEach-Object { Write-Host $_ }
        exit 1
    }
    $passedMatch = ($testResult | Select-String 'réussite\s*:\s*(\d+)|Passed\s*:\s*(\d+)' | ForEach-Object { $_.Matches[0].Groups[1].Value, $_.Matches[0].Groups[2].Value } | Where-Object { $_ }) -join '+'
    Write-Host "[2/5] Tests passed ($passedMatch)" -ForegroundColor Green
} else {
    Write-Host "[2/5] Tests skipped" -ForegroundColor DarkYellow
}

# ── Build ───────────────────────────────────────────────────────────────────

Write-Host "[3/5] Building $Mode..." -ForegroundColor Yellow
dotnet build $AppProject -c $Mode --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED." -ForegroundColor Red
    exit 1
}
Write-Host "[3/5] Build succeeded" -ForegroundColor Green

# ── Publish portable ────────────────────────────────────────────────────────

Write-Host "[4/5] Publishing portable to $buildFolder..." -ForegroundColor Yellow
dotnet publish $AppProject -c $Mode -o $outputDir --nologo --verbosity quiet --self-contained true -r win-x64 -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish FAILED." -ForegroundColor Red
    exit 1
}

# Copy config defaults and locales
$configDest = Join-Path $outputDir 'config'
$localesDest = Join-Path $outputDir 'locales'
if (-not (Test-Path $configDest)) { New-Item -ItemType Directory -Path $configDest -Force | Out-Null }
if (-not (Test-Path $localesDest)) { New-Item -ItemType Directory -Path $localesDest -Force | Out-Null }
Copy-Item (Join-Path $ProjectRoot 'config\settings.default.json') $configDest -Force
Copy-Item (Join-Path $ProjectRoot 'config\servers.default.json') $configDest -Force
if (Test-Path (Join-Path $ProjectRoot 'locales\en.json')) {
    Copy-Item (Join-Path $ProjectRoot 'locales\en.json') $localesDest -Force
}
if (Test-Path (Join-Path $ProjectRoot 'locales\fr.json')) {
    Copy-Item (Join-Path $ProjectRoot 'locales\fr.json') $localesDest -Force
}

Write-Host "[4/5] Portable published" -ForegroundColor Green

# ── Release extras ──────────────────────────────────────────────────────────

if ($Mode -eq 'Release') {
    Write-Host "[5/5] Creating release archive..." -ForegroundColor Yellow
    $zipPath = Join-Path $distDir "${buildFolder}.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path $outputDir -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "[5/5] Archive created: $zipPath" -ForegroundColor Green

    Write-Host ""
    Write-Host "Release artifacts:" -ForegroundColor Cyan
    Write-Host "  Portable: $outputDir" -ForegroundColor White
    Write-Host "  Archive:  $zipPath" -ForegroundColor White
} else {
    Write-Host "[5/5] Debug build -no archive" -ForegroundColor DarkYellow
}

# ── Summary ─────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build complete: v${buildNumber} ($Mode)" -ForegroundColor Cyan
Write-Host "  Output: $outputDir" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
