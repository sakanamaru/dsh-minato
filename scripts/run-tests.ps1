# One command a reviewer can trust: resolve an SDK, build, test, then check repository style.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# 'dotnet' on PATH is not always an SDK (some machines ship only the runtime, and the SDK may
# live in a user-local install). Probe for one that can actually build.
function Resolve-Dotnet {
    $candidates = @('dotnet', (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'))
    foreach ($candidate in $candidates) {
        try {
            $version = & $candidate --version 2>$null
            if ($LASTEXITCODE -eq 0 -and $version) { return $candidate }
        } catch { }
    }
    throw 'no .NET SDK found: install one, or put dotnet on PATH'
}

$dotnet = Resolve-Dotnet
Write-Host ('dotnet: ' + $dotnet)

& $dotnet build src/Dsht.Toolbox.Core/Dsht.Toolbox.Core.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'core build failed' }

& $dotnet build src/Dsht.Toolbox/Dsht.Toolbox.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'app build failed' }

& $dotnet run --project tests/Dsht.Toolbox.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw 'tests failed' }

powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-style.ps1
if ($LASTEXITCODE -ne 0) { throw 'style guard failed' }
