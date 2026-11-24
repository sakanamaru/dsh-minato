# Builds a fake desktop install so the repair path can be exercised without touching a real one.
# Usage: powershell -File scripts/make-fixture.ps1 -Root C:\tmp\dsht-fixture
param(
    [Parameter(Mandatory = $true)][string]$Root
)

$ErrorActionPreference = 'Stop'

$install = Join-Path $Root 'install'
$updater = Join-Path $Root 'updater'
$pending = Join-Path $updater 'pending'

New-Item -ItemType Directory -Path $install, $pending -Force | Out-Null

# The files the probe looks for, plus a plausible Electron version.
Set-Content -LiteralPath (Join-Path $install 'DeepSeek Harness.exe') -Value 'fixture' -Encoding ascii
Set-Content -LiteralPath (Join-Path $install 'Uninstall DeepSeek Harness.exe') -Value 'fixture' -Encoding ascii
Set-Content -LiteralPath (Join-Path $install 'version') -Value '44.7.0' -Encoding ascii

# A staged installer named the way the real updater names it.
Set-Content -LiteralPath (Join-Path $pending 'deepseek-harness-0.2.2-rc.5-win-x64.exe') -Value 'fixture' -Encoding ascii
Set-Content -LiteralPath (Join-Path $pending 'update-info.json') -Value '{"version":"0.2.2-rc.5"}' -Encoding ascii

Write-Host 'fixture ready:'
Write-Host ('  dsht diag --dir "' + $install + '" --updater "' + $updater + '"')
