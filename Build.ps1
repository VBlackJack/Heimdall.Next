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

    # SelfContained: bundles WebView2 runtime (~653 MB) for air-gapped servers without Edge
    # Standard:      no bundled runtime (~195 MB) for PCs with Edge (pre-installed on Windows 10/11)
    # Both:          produces both editions (Release mode only)
    # Legacy aliases: Light = Standard, Portable = SelfContained
    [ValidateSet('Standard', 'SelfContained', 'Both', 'Light', 'Portable')]
    [string]$Variant = 'Both',

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

# Find highest existing build number for today across BOTH debug and release folders
# so the sequence never regresses when switching between modes.
$allDistDirs = @(
    (Join-Path $ProjectRoot 'Dist\debug'),
    (Join-Path $ProjectRoot 'Dist\release')
) | Where-Object { Test-Path $_ }

$existingBuilds = @($allDistDirs | ForEach-Object {
    Get-ChildItem -Path $_ -Directory -Filter "Heimdall.Next_build.${datePrefix}*" -ErrorAction SilentlyContinue
} | ForEach-Object {
    if ($_.Name -match "build\.${datePrefix}(\d{2})(?:_|$)") { [int]$Matches[1] }
})

# Also check the csproj InformationalVersion to avoid duplicating a published release
$csprojRaw = Get-Content $AppProject -Raw
if ($csprojRaw -match '<InformationalVersion>(\d{4}\.\d{6})</InformationalVersion>') {
    $currentVer = $Matches[1]
    if ($currentVer -match "${datePrefix}(\d{2})$") {
        $existingBuilds += [int]$Matches[1]
    }
}

# Also check GitHub releases to avoid collisions with already-published versions
try {
    $ghTags = & gh release list --limit 20 2>$null
    if ($LASTEXITCODE -eq 0 -and $ghTags) {
        $ghTags | ForEach-Object {
            if ($_ -match "v${datePrefix}(\d{2})") {
                $existingBuilds += [int]$Matches[1]
            }
        }
    }
} catch {
    # gh CLI not available or no network — continue with local-only detection
}

$existingBuilds = $existingBuilds | Sort-Object -Descending

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

# Use InformationalVersion for our YYYY.MMDDxx format (no Win32 limit)
# AssemblyVersion uses a compatible 1.0.MMDD.xx format
$assemblyVer = "1.0.$($today.ToString('MMdd')).$sequence"
$csprojContent = Get-Content $AppProject -Raw
$csprojContent = $csprojContent -replace '<Version>[^<]+</Version>', "<Version>${assemblyVer}</Version>"
if ($csprojContent -match '<InformationalVersion>') {
    $csprojContent = $csprojContent -replace '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>${buildNumber}</InformationalVersion>"
} else {
    $csprojContent = $csprojContent -replace '</Version>', "</Version>`n    <InformationalVersion>${buildNumber}</InformationalVersion>"
}
[System.IO.File]::WriteAllText($AppProject, $csprojContent, [System.Text.UTF8Encoding]::new($false))
Write-Host "[1/5] Version set to $buildNumber (assembly: $assemblyVer)" -ForegroundColor Green

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

# ── Determine which variants to produce ────────────────────────────────────

$variants = switch ($Variant) {
    'Both'          { @('Standard', 'SelfContained') }
    'Standard'      { @('Standard') }
    'Light'         { @('Standard') }
    'SelfContained' { @('SelfContained') }
    'Portable'      { @('SelfContained') }
}

$wv2Runtime = Join-Path $ProjectRoot 'runtimes\webview2'
$hasWv2Runtime = Test-Path (Join-Path $wv2Runtime 'msedgewebview2.exe')

if ($variants -contains 'SelfContained' -and -not $hasWv2Runtime) {
    Write-Host "[!] WebView2 Fixed Version Runtime not found in runtimes/webview2/" -ForegroundColor Red
    Write-Host "    Run Setup-WebView2.ps1 first, or use -Variant Standard" -ForegroundColor Red
    Write-Host "    Falling back to Standard edition only." -ForegroundColor DarkYellow
    $variants = @('Standard')
}

# ── Helper: publish one variant ────────────────────────────────────────────

function Publish-Variant {
    param([string]$VariantName)

    $suffix = if ($VariantName -eq 'SelfContained') { '_selfcontained' } else { '_standard' }
    # For single-variant builds, omit the suffix for backward compatibility
    if ($Variant -ne 'Both') { $suffix = '' }

    $variantFolder = "Heimdall.Next_build.${buildNumber}${suffix}"
    $variantDir = Join-Path $distDir $variantFolder

    Write-Host "  Publishing $VariantName to $variantFolder..." -ForegroundColor Yellow
    dotnet publish $AppProject -c $Mode -o $variantDir --nologo --verbosity quiet --self-contained true -r win-x64 -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Publish FAILED." -ForegroundColor Red
        exit 1
    }

    # Copy config defaults and locales
    $configDest = Join-Path $variantDir 'config'
    $localesDest = Join-Path $variantDir 'locales'
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

    # Copy WebView2 native loader
    $wv2Loader = Join-Path $ProjectRoot 'src\Heimdall.App\lib\webview2\WebView2Loader.dll'
    if (Test-Path $wv2Loader) {
        Copy-Item $wv2Loader $variantDir -Force
    }

    # Copy terminal assets
    $assetsSrc = Join-Path $ProjectRoot 'src\Heimdall.App\Assets'
    $assetsDest = Join-Path $variantDir 'Assets'
    if (Test-Path $assetsSrc) {
        Copy-Item $assetsSrc $assetsDest -Recurse -Force
    }

    # Bundle WebView2 runtime for SelfContained edition only
    if ($VariantName -eq 'SelfContained') {
        $wv2Dest = Join-Path $variantDir 'runtimes\webview2'
        Copy-Item $wv2Runtime $wv2Dest -Recurse -Force
        Write-Host "    + WebView2 Fixed Version Runtime bundled" -ForegroundColor DarkGray
    }

    # Create archive in Release mode
    if ($Mode -eq 'Release') {
        $zipPath = Join-Path $distDir "${variantFolder}.zip"
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        Compress-Archive -Path $variantDir -DestinationPath $zipPath -CompressionLevel Optimal
        Write-Host "    + Archive: $zipPath" -ForegroundColor DarkGray
    }

    return $variantDir
}

# ── Publish all variants ───────────────────────────────────────────────────

$step = 4
$totalSteps = 5
Write-Host "[$step/$totalSteps] Publishing ($($variants -join ' + '))..." -ForegroundColor Yellow

$outputs = @()
foreach ($v in $variants) {
    $dir = Publish-Variant -VariantName $v
    $outputs += @{ Name = $v; Dir = $dir }
}

Write-Host "[$step/$totalSteps] Published" -ForegroundColor Green

# ── Installers (Release mode only) ─────────────────────────────────────────

if ($Mode -eq 'Release') {
    Write-Host "[5/6] Creating installers..." -ForegroundColor Yellow

    $installerDir = Join-Path $ProjectRoot 'Dist\installers'
    if (-not (Test-Path $installerDir)) { New-Item -ItemType Directory -Path $installerDir -Force | Out-Null }

    # Inno Setup (.exe installer)
    $issFile = Join-Path $ProjectRoot 'installer\innosetup.iss'
    $iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
    if ((Test-Path $issFile) -and (Test-Path $iscc)) {
        foreach ($o in $outputs) {
            $variantLower = $o.Name.ToLower()
            Write-Host "  Building Inno Setup installer ($($o.Name))..." -ForegroundColor DarkGray
            & $iscc /DAppVersion="$buildNumber" /DVariant="$($o.Name)" /DSourceDir="$($o.Dir)" /Q $issFile 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Host "    + Heimdall.Next_${buildNumber}_$($o.Name)_Setup.exe" -ForegroundColor DarkGray
            } else {
                Write-Host "    [!] Inno Setup failed for $($o.Name)" -ForegroundColor DarkYellow
            }
        }
    } else {
        Write-Host "  [!] Inno Setup not found - skipping .exe installer" -ForegroundColor DarkYellow
    }

    # WiX MSI (.msi installer)
    $wixAvailable = $false
    try { wix --version 2>&1 | Out-Null; $wixAvailable = ($LASTEXITCODE -eq 0) } catch {}
    if ($wixAvailable) {
        foreach ($o in $outputs) {
            $msiOutput = Join-Path $installerDir "Heimdall.Next_${buildNumber}_$($o.Name).msi"
            $wxsFile = Join-Path $ProjectRoot 'installer\Product.wxs'
            if (Test-Path $wxsFile) {
                Write-Host "  Building WiX MSI ($($o.Name))..." -ForegroundColor DarkGray
                # Create a temp symlink/copy for the harvest path
                $harvestLink = Join-Path $ProjectRoot 'Dist\release\Heimdall.Next_current'
                if (Test-Path $harvestLink) { Remove-Item $harvestLink -Recurse -Force }
                Copy-Item $o.Dir $harvestLink -Recurse -Force
                try {
                    wix build -o $msiOutput $wxsFile -ext WixToolset.UI.wixext 2>&1 | Out-Null
                    if ($LASTEXITCODE -eq 0) {
                        Write-Host "    + $(Split-Path $msiOutput -Leaf)" -ForegroundColor DarkGray
                    } else {
                        Write-Host "    [!] WiX build failed for $($o.Name)" -ForegroundColor DarkYellow
                    }
                } catch {
                    Write-Host "    [!] WiX build error: $_" -ForegroundColor DarkYellow
                }
                if (Test-Path $harvestLink) { Remove-Item $harvestLink -Recurse -Force }
            }
        }
    } else {
        Write-Host "  [!] WiX Toolset not found - skipping .msi installer" -ForegroundColor DarkYellow
    }

    Write-Host "[5/6] Installers done" -ForegroundColor Green
    Write-Host "[6/6] Release archives created" -ForegroundColor Green
} else {
    Write-Host "[5/5] Debug build - no archive" -ForegroundColor DarkYellow
}

# ── Summary ────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build complete: v${buildNumber} ($Mode)" -ForegroundColor Cyan
foreach ($o in $outputs) {
    $size = [math]::Round((Get-ChildItem $o.Dir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 0)
    Write-Host "  $($o.Name): $($o.Dir) (~${size} MB)" -ForegroundColor Cyan
}
if ($Mode -eq 'Release') {
    $installerDir = Join-Path $ProjectRoot 'Dist\installers'
    if (Test-Path $installerDir) {
        Get-ChildItem $installerDir -File -Filter "*${buildNumber}*" | ForEach-Object {
            $sz = [math]::Round($_.Length / 1MB, 0)
            Write-Host "  Installer: $($_.Name) (~${sz} MB)" -ForegroundColor Cyan
        }
    }
}
Write-Host "========================================" -ForegroundColor Cyan
