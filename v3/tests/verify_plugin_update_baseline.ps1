# ============================================================================
#  verify_plugin_update_baseline.ps1 -- bridge plugin update check, end to end.
#  ---------------------------------------------------------------------------
#  What it proves (2026-10-11, maintainer option (1)): the "update centre" plugin row for
#  dsh-minato-bridge is decided OFFLINE against the copy of the plugin that ships WITH the
#  toolkit (<exe dir>\plugin\dsh-minato-bridge\). Two comparisons, both exercised here:
#    1. version  (VersionComparer)                       -> update-available / newer-than-bundled
#    2. content  (SHA-256 of snapshot.js and index.js)   -> up-to-date vs content-mismatch
#  "same version, different hash" (the half-stale case) is the one a version-only check misses.
#
#  Discipline:
#    * the real data root is never used: every run gets DSH_HOME pointed at a temp fixture
#      (the real root is snapshotted before/after and must be identical in listing)
#    * the tool is only ever run with `update-center` (read-only command)
#    * nothing under the repo is written; all fixtures live in %TEMP% and are removed at the end
#
#  ASCII only on purpose: the repo invariant "non-ASCII .ps1 must carry a UTF-8 BOM" cannot bite
#  a script that is pure ASCII, and this file stays runnable from Windows PowerShell 5.1.
#  Therefore every assertion below keys on ASCII tokens that the CLI prints in BOTH languages
#  (state names, the bridge-install command line, file names, paths) - never on Chinese prose.
#
#  Note: `update-center` also asks npm/GitHub for the webui/minato rows (unchanged, not part of
#  this check); the plugin rows themselves need no network. Expect the whole run to take a while.
#
#  Prerequisite: the CLI is already built (release):
#      dotnet build v3/src/Dsht.Cli/Dsht.Cli.csproj -c Release
#  Exit codes: 0 = all checks passed; 1 = something failed; 2 = environment not ready.
# ============================================================================
param(
    [string]$Repo = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$CliExe = "",
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'
$pass = 0
$fail = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { $script:pass++; Write-Host ("  [PASS] " + $name) }
    else { $script:fail++; Write-Host ("  [FAIL] " + $name + "   <- " + $detail) }
}
function RunCli([string]$exe, [string]$dataHome) {
    # native stderr (INTEGRITY_SKIPPED) must not be turned into a terminating error by PS 5.1
    # NOTE: the parameter must not be called $home - PowerShell's $HOME is read-only (case-insensitive).
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $saved = $env:DSH_HOME
    $env:DSH_HOME = $dataHome
    try { $out = (& $exe update-center 2>$null | Out-String) }
    finally { $env:DSH_HOME = $saved; $ErrorActionPreference = $old }
    return $out
}
function LinesOf([string]$out) { return @($out -split "`r?`n") }
function ItemLines([string]$out) {
    return @(LinesOf $out | Where-Object { $_.Trim().StartsWith('UPDATECENTER_ITEM plugin:dsh-minato-bridge ') })
}
function Notes([string]$out) {
    return @(LinesOf $out | Where-Object { $_.Trim().StartsWith('UPDATECENTER_NOTE plugin:dsh-minato-bridge ') })
}
function RowOf([string]$out, [string]$profile) {
    $r = @(ItemLines $out | Where-Object { $_ -match (' profile=' + [regex]::Escape($profile) + '$') })
    if ($r.Count -gt 0) { return $r[0].Trim() }
    return ''
}
function StateOf([string]$row) {
    $m = [regex]::Match($row, ' state=([a-z\-]+) ')
    if ($m.Success) { return $m.Groups[1].Value }
    return ''
}
function FieldOf([string]$row, [string]$key) {
    $m = [regex]::Match($row, (' ' + $key + '=([^ ]+)'))
    if ($m.Success) { return $m.Groups[1].Value }
    return ''
}
# NOTE lines carry no profile field: they are emitted in the same order as the ITEM rows, so the
# n-th NOTE belongs to the n-th row (the GUI binds them per row the same way).
function NoteMap([string]$out) {
    $items = ItemLines $out
    $notes = Notes $out
    $map = @{}
    for ($i = 0; $i -lt $items.Count; $i++) {
        $p = FieldOf $items[$i] 'profile'
        $map[$p] = ''
        if ($i -lt $notes.Count) { $map[$p] = $notes[$i].Trim() }
    }
    return $map
}
function NoteFor($map, [string]$profile) {
    if ($map.ContainsKey($profile)) { return $map[$profile] }
    return ''
}
function HasDiag([string]$out) {
    return (@(LinesOf $out | Where-Object { $_.Trim().StartsWith('UPDATECENTER_DIAG plugin:') }).Count -gt 0)
}
function DiagText([string]$out) {
    $r = @(LinesOf $out | Where-Object { $_.Trim().StartsWith('UPDATECENTER_DIAG plugin:') })
    if ($r.Count -gt 0) { return $r[0].Trim() }
    return ''
}
function OkLine([string]$out) {
    return (@(LinesOf $out | Where-Object { $_.Trim().StartsWith('UPDATECENTER_OK ') }) -join ' / ')
}

$cli = if ($CliExe -ne '') { $CliExe } else { Join-Path $Repo 'v3\src\Dsht.Cli\bin\Release\net8.0\dsh-minato.exe' }
if (-not (Test-Path -LiteralPath $cli)) {
    Write-Host ('SKIP: CLI exe not found: ' + $cli)
    Write-Host '      build it first:  dotnet build v3/src/Dsht.Cli/Dsht.Cli.csproj -c Release'
    exit 2
}
$exeDir = Split-Path -Parent $cli
$bundled = Join-Path $exeDir 'plugin\dsh-minato-bridge'
if (-not (Test-Path -LiteralPath (Join-Path $bundled 'package.json'))) {
    Write-Host ('SKIP: the exe dir carries no bundled plugin, so there is no baseline to test against: ' + $bundled)
    Write-Host '      copy plugin/dsh-minato-bridge next to the exe (that is what the release payload does).'
    exit 2
}
$bver = [regex]::Match([System.IO.File]::ReadAllText((Join-Path $bundled 'package.json')), '"version"\s*:\s*"([^"]+)"').Groups[1].Value
if ($bver -eq '') { Write-Host 'SKIP: cannot read the bundled version'; exit 2 }
$bsnap = [System.IO.File]::ReadAllText((Join-Path $bundled 'snapshot.js'))
Write-Host ('CLI      : ' + $cli)
Write-Host ('baseline : bundled plugin ' + $bver + ' at ' + $bundled)

# ---- safety net: the real data root must not change (same idea as compare_markers.ps1) ----
$realRoots = @()
foreach ($cand in @((Join-Path $env:USERPROFILE '.dsh'), (Join-Path $env:APPDATA '.dsh'), (Join-Path $env:LOCALAPPDATA '.dsh'))) {
    if ($cand -and (Test-Path -LiteralPath $cand)) {
        $items = Get-ChildItem -LiteralPath $cand -Force -ErrorAction SilentlyContinue | Sort-Object Name |
            ForEach-Object { if ($_.PSIsContainer) { 'D:' + $_.Name } else { 'F:' + $_.Name + ':' + $_.Length } }
        $realRoots += @{ path = $cand; snap = ($items -join '|') }
    }
}

# ---- fixtures (temp only) ----
$root = Join-Path $env:TEMP ('dsht_plugin_baseline_' + $PID)
$bare = Join-Path $env:TEMP ('dsht_plugin_bare_' + $PID)
foreach ($d in @($root, $bare)) { if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force } }
New-Item -ItemType Directory -Path (Join-Path $root 'profiles') -Force | Out-Null
New-Item -ItemType Directory -Path $bare -Force | Out-Null

# Expected CLI phrases (Chinese text) built from code points, so this file stays pure ASCII and
# needs no UTF-8 BOM (the repo gates on "non-ASCII .ps1 must carry a BOM"). Decoded:
#   $zhManaged      = U+7531 + " dsh " + U+684C U+9762 U+7AEF U+72EC U+5360 U+7BA1 U+7406
#                     = the desktop guidance line ("managed exclusively by the dsh desktop app")
#   $zhNotInstalled = U+672A U+5B89 U+88C5  = "not installed" (the state word we require)
#   $zhInstalled    = U+5DF2 U+88C5         = "installed" (must NOT be claimed when the plugin is absent)
$zhManaged = -join ([char]0x7531, ' dsh ', [char]0x684C, [char]0x9762, [char]0x7AEF, [char]0x72EC, [char]0x5360, [char]0x7BA1, [char]0x7406)
$zhNotInstalled = -join ([char]0x672A, [char]0x5B89, [char]0x88C5)
$zhInstalled = -join ([char]0x5DF2, [char]0x88C5)

function New-Profile([string]$name) {
    $d = Join-Path (Join-Path $root 'profiles') $name
    New-Item -ItemType Directory -Path $d -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $d 'cordis.patch.yml'), "patch:`n", (New-Object System.Text.UTF8Encoding($false)))
    return $d
}
# layout 'root'   = the current package shape   (main = index.js)
# layout 'legacy' = the shape really found in the desktop profile (main = plugin/dsh-minato-bridge/index.js)
function Write-Plugin([string]$profileDir, [string]$version, [string]$layout, [string]$snapshotText) {
    $pkg = Join-Path $profileDir 'node_modules\dsh-minato-bridge'
    $code = if ($layout -eq 'legacy') { Join-Path $pkg 'plugin\dsh-minato-bridge' } else { $pkg }
    New-Item -ItemType Directory -Path $code -Force | Out-Null
    $main = if ($layout -eq 'legacy') { 'plugin/dsh-minato-bridge/index.js' } else { 'index.js' }
    $json = '{"name":"dsh-minato-bridge","version":"' + $version + '","main":"' + $main + '"}'
    [System.IO.File]::WriteAllText((Join-Path $pkg 'package.json'), $json, (New-Object System.Text.UTF8Encoding($false)))
    Copy-Item -LiteralPath (Join-Path $bundled 'index.js') -Destination (Join-Path $code 'index.js') -Force
    [System.IO.File]::WriteAllText((Join-Path $code 'snapshot.js'), $snapshotText, (New-Object System.Text.UTF8Encoding($false)))
}
# one profile per state; the bundled side is never touched (it stays the real shipped copy)
Write-Plugin (New-Profile 'desktop') '0.1.0' 'root'   $bsnap                                 # older version
Write-Plugin (New-Profile 'web')     $bver   'root'   $bsnap                                 # identical
Write-Plugin (New-Profile 'stale')   $bver   'root'   ($bsnap + "`n// stale extra line`n")   # same version, different hash
Write-Plugin (New-Profile 'legacy')  $bver   'legacy' $bsnap                                 # identical, old nested layout
Write-Plugin (New-Profile 'ahead')   '9.9.9' 'root'   $bsnap                                 # newer than the bundled copy
[void](New-Profile 'none')                                                                    # real profile, plugin not installed
New-Item -ItemType Directory -Path (Join-Path $root 'profiles\junkdir') -Force | Out-Null     # not a profile (no marker files)

Write-Host ''
Write-Host '== A. baseline available (the normal installed case) =='
$out = RunCli $cli $root
Write-Host ('  summary line: ' + (OkLine $out))
$rowDesk = RowOf $out 'desktop'
$rowWeb = RowOf $out 'web'
$rowStale = RowOf $out 'stale'
$rowLegacy = RowOf $out 'legacy'
$rowAhead = RowOf $out 'ahead'
$rowNone = RowOf $out 'none'
$map = NoteMap $out

Check 'row desktop: older than bundled -> update-available, installed=0.1.0, latest=bundled' `
    ((StateOf $rowDesk) -eq 'update-available' -and (FieldOf $rowDesk 'installed') -eq '0.1.0' -and (FieldOf $rowDesk 'latest') -eq $bver) `
    $rowDesk
Check 'row web: same version + same content hashes -> up-to-date' `
    ((StateOf $rowWeb) -eq 'up-to-date' -and (FieldOf $rowWeb 'latest') -eq $bver) $rowWeb
Check 'row stale: SAME version but a different snapshot.js hash -> content-mismatch (a version-only check would say up-to-date)' `
    ((StateOf $rowStale) -eq 'content-mismatch' -and (FieldOf $rowStale 'latest') -eq $bver -and (FieldOf $rowStale 'installed') -eq $bver) `
    $rowStale
Check 'row legacy: the nested plugin/dsh-minato-bridge layout is resolved and identical -> up-to-date' `
    ((StateOf $rowLegacy) -eq 'up-to-date') $rowLegacy
Check 'row ahead: newer than the bundled copy -> newer-than-bundled (not spun as an update)' `
    ((StateOf $rowAhead) -eq 'newer-than-bundled') $rowAhead
Check 'row none: real profile without the plugin -> not-installed with installed=unknown' `
    ((StateOf $rowNone) -eq 'not-installed' -and (FieldOf $rowNone 'installed') -eq 'unknown') $rowNone
Check 'a junk directory under profiles\ is NOT reported as a profile' `
    ((RowOf $out 'junkdir') -eq '') 'junkdir produced a row'
Check 'exactly 6 plugin rows (one per real profile); every row keeps the ITEM field names and kind=plugin' `
    ((ItemLines $out).Count -eq 6 -and (@(ItemLines $out | Where-Object { $_ -notmatch ' kind=plugin ' }).Count -eq 0)) `
    ('rows=' + (ItemLines $out).Count)
Check 'no DIAG when the baseline is available (the row really does have a latest)' `
    (-not (HasDiag $out)) (DiagText $out)
Check 'one NOTE per row, in row order (the GUI binds them the same way)' `
    ((Notes $out).Count -eq 6) ('notes=' + (Notes $out).Count)
Check 'web guidance names: bridge-install --profile web --yes' `
    ((NoteFor $map 'web').Contains('bridge-install --profile web --yes')) (NoteFor $map 'web')
Check 'desktop guidance says the profile is managed by the dsh desktop app, and does NOT promise a bridge-install command' `
    ((NoteFor $map 'desktop').Contains($zhManaged) -and (-not (NoteFor $map 'desktop').Contains('bridge-install'))) (NoteFor $map 'desktop')
Check 'per-profile guidance really differs (desktop NOTE != web NOTE)' `
    ((NoteFor $map 'desktop') -ne (NoteFor $map 'web')) 'identical NOTE text'
Check 'the content-mismatch row points at the hashed file in its NOTE (snapshot.js)' `
    ((NoteFor $map 'stale').Contains('snapshot.js')) (NoteFor $map 'stale')
Check 'the not-installed row says not-installed and never claims an installed version' `
    ((NoteFor $map 'none').Contains($zhNotInstalled) -and (-not (NoteFor $map 'none').Contains($zhInstalled + ' '))) (NoteFor $map 'none')
Check 'a not-installed profile is not told to run the web command' `
    (-not (NoteFor $map 'none').Contains('bridge-install --profile web')) (NoteFor $map 'none')

# ---- B. the honesty path: an exe dir WITHOUT the bundled plugin (source builds look like this) ----
Write-Host ''
Write-Host '== B. baseline missing (exe copied without plugin\, i.e. a source build) =='
Get-ChildItem -LiteralPath $exeDir -File | Copy-Item -Destination $bare -Force
$outB = RunCli (Join-Path $bare 'dsh-minato.exe') $root
$rowB = RowOf $outB 'desktop'
Check 'baseline missing -> latest=unknown AND state=unknown (nothing invented)' `
    ((FieldOf $rowB 'latest') -eq 'unknown' -and (StateOf $rowB) -eq 'unknown') $rowB
Check 'baseline missing -> the already-installed version is still reported (0.1.0 for desktop)' `
    ((FieldOf $rowB 'installed') -eq '0.1.0') $rowB
Check 'baseline missing -> a DIAG line names the missing bundled plugin folder (common for source builds)' `
    ((HasDiag $outB) -and (DiagText $outB).Contains('plugin')) (DiagText $outB)
Check 'baseline missing -> no row is silently upgraded to up-to-date or update-available' `
    (@(ItemLines $outB | Where-Object { (StateOf $_) -ne 'unknown' }).Count -eq 0) ((ItemLines $outB) -join ' | ')

# ---- safety net: the real data root is untouched ----
$dirty = ''
foreach ($r in $realRoots) {
    $items = Get-ChildItem -LiteralPath $r.path -Force -ErrorAction SilentlyContinue | Sort-Object Name |
        ForEach-Object { if ($_.PSIsContainer) { 'D:' + $_.Name } else { 'F:' + $_.Name + ':' + $_.Length } }
    if (($items -join '|') -ne $r.snap) { $dirty += ($r.path + ' ') }
}
Check 'the real data root was never touched (snapshot before/after)' ($dirty -eq '') $dirty

if (-not $Keep) {
    foreach ($d in @($root, $bare)) { Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue }
}
Write-Host ''
Write-Host ('== plugin update baseline: ' + $pass + '/' + ($pass + $fail) + ' passed, ' + $fail + ' failed ==')
if ($fail -gt 0) { exit 1 }
exit 0
