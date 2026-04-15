<#
.SYNOPSIS
    Runs a focused navigation and gateway accessibility UIAutomation smoke test.

.PARAMETER RunBuild
    Runs dotnet build before the smoke test.

.PARAMETER RunTests
    Runs dotnet test before the smoke test.

.NOTES
    Copyright 2026 Julien Bombled
    Licensed under the Apache License, Version 2.0
#>
[CmdletBinding()]
param(
    [switch]$RunBuild,
    [switch]$RunTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'uia-common.ps1')

function Invoke-OptionalVerification {
    if ($RunBuild) {
        & dotnet build Heimdall.slnx --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
    }

    if ($RunTests) {
        & dotnet test Heimdall.slnx --no-build
        if ($LASTEXITCODE -ne 0) { throw 'dotnet test failed.' }
    }
}

function Set-LocaleInSettingsFile {
    param([ValidateSet('en','fr')][string]$Locale)
    $settings = Read-HeimdallSettingsJson
    $settings.DefaultLocale = $Locale
    Write-HeimdallSettingsJson -Settings $settings
}

function Get-ExpectedNames {
    param([ValidateSet('en','fr')][string]$Locale)
    if ($Locale -eq 'en') {
        return [ordered]@{
            TabSessions = 'Sessions tab'
            TabTunnels = 'Tunnels tab'
            TabScheduled = 'Scheduled tab'
            TabTools = 'Tools tab'
            TabSettings = 'Settings tab'
            TabAbout = 'About tab'
            Mw_SettingsGatewaysAddBtn = 'Add gateway'
            Mw_SettingsGatewaysEditBtn = 'Edit gateway'
            Mw_SettingsGatewaysDeleteBtn = 'Delete gateway'
        }
    }

    return [ordered]@{
        TabSessions = 'Onglet sessions'
        TabTunnels = 'Onglet tunnels'
        TabScheduled = 'Onglet planifié'
        TabTools = 'Onglet outils'
        TabSettings = 'Onglet paramètres'
        TabAbout = 'Onglet à propos'
        Mw_SettingsGatewaysAddBtn = 'Ajouter une passerelle'
        Mw_SettingsGatewaysEditBtn = 'Modifier la passerelle'
        Mw_SettingsGatewaysDeleteBtn = 'Supprimer la passerelle'
    }
}

function Read-NamesForLocale {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement]$MainWindow,
        [Parameter(Mandatory)]
        [ValidateSet('en','fr')]
        [string]$Locale
    )

    $expected = Get-ExpectedNames -Locale $Locale
    $actual = [ordered]@{}

    foreach ($automationId in 'TabSessions','TabTunnels','TabScheduled','TabTools','TabSettings','TabAbout') {
        $element = Wait-UiaElementById -Root $MainWindow -AutomationId $automationId
        $actual[$automationId] = [string]$element.Current.Name
    }

    Open-HeimdallSettings -MainWindow $MainWindow | Out-Null
    Select-HeimdallSettingsTab -MainWindow $MainWindow -AutomationId 'Mw_SettingsTabSsh' | Out-Null

    foreach ($automationId in 'Mw_SettingsGatewaysAddBtn','Mw_SettingsGatewaysEditBtn','Mw_SettingsGatewaysDeleteBtn') {
        $element = Wait-UiaElementById -Root $MainWindow -AutomationId $automationId
        $actual[$automationId] = [string]$element.Current.Name
    }

    return @{
        Expected = $expected
        Actual   = $actual
        Match    = ($expected.GetEnumerator() | Where-Object { $actual[$_.Key] -ne $_.Value } | Measure-Object).Count -eq 0
    }
}

Push-Location (Get-HeimdallRepoRoot)
$settingsBackup = $null
$process = $null
$report = [ordered]@{ Result = 'FAIL' }

try {
    Invoke-OptionalVerification
    $settingsBackup = Backup-HeimdallSettings
    Stop-HeimdallProcesses

    Set-LocaleInSettingsFile -Locale 'en'
    $process = Start-HeimdallApp
    $mainWindow = Wait-HeimdallMainWindow -ProcessId $process.Id
    $enResult = Read-NamesForLocale -MainWindow $mainWindow -Locale 'en'
    $report.NavigationNamesEn = $enResult.Actual
    $report.NavigationNamesEnMatch = $enResult.Match

    Stop-HeimdallApp -Process $process
    $process = $null

    Set-LocaleInSettingsFile -Locale 'fr'
    $process = Start-HeimdallApp
    $mainWindow = Wait-HeimdallMainWindow -ProcessId $process.Id
    $frResult = Read-NamesForLocale -MainWindow $mainWindow -Locale 'fr'
    $report.NavigationNamesFr = $frResult.Actual
    $report.NavigationNamesFrMatch = $frResult.Match

    $report.Result = if ($enResult.Match -and $frResult.Match) { 'OK' } else { 'FAIL' }
}
catch {
    $report.Error = $_.Exception.Message
    $report.Result = 'FAIL'
}
finally {
    if ($null -ne $process) {
        try { Stop-HeimdallApp -Process $process } catch { }
    }

    if ($null -ne $settingsBackup) {
        try { Restore-HeimdallSettings -Backup $settingsBackup } catch { }
    }

    Stop-HeimdallProcesses
    Pop-Location
    $report | ConvertTo-Json -Depth 6
}

if ($report.Result -ne 'OK') {
    exit 1
}
