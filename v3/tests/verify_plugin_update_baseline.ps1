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
    # A plugin ITEM row is `UPDATECENTER_ITEM plugin:dsh-minato-bridge kind=plugin ...` - i.e. the id is
    # followed by the kind= field. A NOTE's freely wrapped prose can also start with the same prefix
    # (measured on the real machine), so require the id AND that kind= field, and do not swallow it.
    return @(LinesOf $out | Where-Object { $_.Trim() -match '^UPDATECENTER_ITEM plugin:dsh-minato-bridge kind=plugin( |$)' })
}
function Notes([string]$out) {
    # NOTE lines are SHARED per id (2026-10-11): the CLI emits one URL line and one NOTE line per id,
    # not one per profile row. They are card-level info, never bound to "the n-th row".
    return @(LinesOf $out | Where-Object { $_.Trim().StartsWith('UPDATECENTER_NOTE plugin:dsh-minato-bridge ') })
}
function Urls([string]$out) {
    return @(LinesOf $out | Where-Object { $_.Trim().StartsWith('UPDATECENTER_URL plugin:dsh-minato-bridge ') })
}
function RowOf([string]$out, [string]$profile) {
    # profile= is no longer the last field (a per-row `next=` follows it), so do not anchor at end of line.
    $r = @(ItemLines $out | Where-Object { $_ -match (' profile=' + [regex]::Escape($profile) + ' ') })
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
# The per-row guidance is the ITEM row's own `next=` field (2026-10-11): spaces are escaped as %20 so
# the field stays a single space-delimited token; DecodeNext is the same %20 -> space rule the GUI uses
# (Dsht.Gui.Avalonia/Markers/UpdateCenterMarkers.cs, DecodeNext).
function DecodeNext([string]$v) {
    if ($v -eq '' -or $v -eq '-') { return '' }
    return $v.Replace('%20', ' ')
}
function NextOf([string]$out, [string]$profile) {
    return (DecodeNext (FieldOf (RowOf $out $profile) 'next'))
}
function SharedNote([string]$out) {
    # @() is required: PowerShell unwraps a one-element array on assignment, and then $n[0] would be
    # the first CHARACTER of the note (that is exactly the crash this line used to produce).
    $n = @(Notes $out)
    if ($n.Count -gt 0) { return $n[0].Trim() }
    return ''
}
# The NOTE *value* = the line minus its `UPDATECENTER_NOTE <id> ` prefix. The check below asserts on the
# value (it starts with "baseline = ..."), NOT on the whole line (which starts with the marker).
function SharedNoteValue([string]$out) {
    $n = SharedNote $out
    $p = 'UPDATECENTER_NOTE plugin:dsh-minato-bridge '
    if ($n.StartsWith($p)) { return $n.Substring($p.Length) }
    return $n
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
#   $zhManageTail   = U+7BA1 U+7406 U+673A U+5236 U+91CC U+66F4 U+65B0 = "management" + "mechanism" + "updates"
#                     (desktop guidance tail; wording shortened on 2026-10-11 for the table card)
#   $zhDesktopSentence = the exact short desktop guidance sentence (assembled in section A below)
#   $zhNotInstalled = U+672A U+5B89 U+88C5  = "not installed" (the state word we require)
#   $zhInstalled    = U+5DF2 U+88C5         = "installed" (must NOT be claimed when the plugin is absent)
#   $zhBaselineWord = U+57FA U+51C6 + U+0020 U+003D U+0020 = "baseline = " (the shared NOTE's opening)
#   $zhDesktopWord  = U+684C U+9762 U+7AEF = "desktop" (About page wording)
#   $zhCliNoInstall = U+547D U+4EE4 U+884C U+88C5 U+4E0D U+8FDB = "command line cannot install"
#   $zhOffline      = U+79BB U+7EBF = "offline"
#   $zhBaseline     = U+57FA U+51C6 = "baseline"
# NOTE: `-join ([char]..,[char]..)` is WRONG in Windows PowerShell 5.1 - that overload's separator
# defaults to a SPACE, so it silently produces "管 理" instead of "管理". [string]::Join('', ...) is the fix.
$zhManageTail = [string]::Join('', [string[]]@([char]0x7BA1, [char]0x7406, [char]0x673A, [char]0x5236, [char]0x91CC, [char]0x66F4, [char]0x65B0))
$zhNotInstalled = [string]::Join('', [string[]]@([char]0x672A, [char]0x5B89, [char]0x88C5))
$zhInstalled = [string]::Join('', [string[]]@([char]0x5DF2, [char]0x88C5))
$zhBaselineWord = [string]::Join('', [string[]]@([char]0x57FA, [char]0x51C6, [char]0x20, [char]0x3D, [char]0x20))
$zhDesktopWord = [string]::Join('', [string[]]@([char]0x684C, [char]0x9762, [char]0x7AEF))
$zhCliNoInstall = [string]::Join('', [string[]]@([char]0x547D, [char]0x4EE4, [char]0x884C, [char]0x88C5, [char]0x4E0D, [char]0x8FDB))
$zhOffline = [string]::Join('', [string[]]@([char]0x79BB, [char]0x7EBF))
$zhBaseline = [string]::Join('', [string[]]@([char]0x57FA, [char]0x51C6))

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
$sharedNote = SharedNote $out

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
    ((ItemLines $out).Count -eq 6 -and (@(ItemLines $out | Where-Object { $_ -notmatch ' kind=plugin( |$)' }).Count -eq 0)) `
    ('rows=' + (ItemLines $out).Count)
Check 'no DIAG when the baseline is available (the row really does have a latest)' `
    (-not (HasDiag $out)) (DiagText $out)
Check 'ONE shared NOTE and ONE shared URL for the whole id (6 rows, 1 note, 1 url) - the same sentence is no longer repeated per profile' `
    ((Notes $out).Count -eq 1 -and (Urls $out).Count -eq 1) `
    ('notes=' + (Notes $out).Count + ' urls=' + (Urls $out).Count)
Check 'every row carries its own next= guidance (one per row, so a row can never pick up another row''s guidance)' `
    (@(ItemLines $out | Where-Object { (FieldOf $_ 'next') -eq '' }).Count -eq 0) `
    ((ItemLines $out | Where-Object { (FieldOf $_ 'next') -eq '' }) -join ' | ')
Check 'the shared NOTE still states what the baseline is (one short sentence, emitted once for the whole id)' `
    ((SharedNoteValue $out).StartsWith($zhBaselineWord)) (SharedNoteValue $out)
Check 'the shared NOTE is now SHORT (readability pass: it is no longer a whole paragraph of mechanism prose)' `
    ($sharedNote.Length -lt 120) ('len=' + $sharedNote.Length)
Check 'the card no longer carries the bundled plugin path or the hashing recipe (they moved to the About page of the GUI)' `
    ((-not $sharedNote.Contains('snapshot.js')) -and (-not $sharedNote.Contains('index.js')) -and (-not $sharedNote.Contains('SHA-256')) -and (-not $sharedNote.Contains('VersionComparer')) -and (-not $sharedNote.Contains('\plugin\'))) `
    $sharedNote
Check 'web guidance is one short command: bridge-install --profile web --yes' `
    ((NextOf $out 'web') -eq 'bridge-install --profile web --yes') (NextOf $out 'web')
# The desktop row's guidance must be exact: the short sentence, and never a bridge-install promise.
# (Exact equality is used, not a suffix search: an EndsWith on a value built from code points is a
#  fragile assertion - if the code points are ever wrong it fails *silently* as a "missing word".)
$dbgDesk = NextOf $out 'desktop'
$zhDeskExpected = [string]::Join('', [string[]]@([char]0x5728, [char]0x684C, [char]0x9762, [char]0x7AEF, [char]0x63D2, [char]0x4EF6, [char]0x673A, [char]0x5236, [char]0x91CC, [char]0x66F4, [char]0x65B0))
Check 'desktop guidance is the exact short sentence about the desktop plugin mechanism, and does NOT promise a bridge-install command' `
    (($dbgDesk -eq $zhDeskExpected) -and (-not $dbgDesk.Contains('bridge-install'))) ('[' + $dbgDesk + ']')
# Every next= value must be EITHER a command OR a short Chinese sentence of at most 20 characters.
# A "command" is a value whose FIRST WORD is pure ASCII (bridge-install, ...). Anything else is prose.
# Computed once, then asserted and reported.
$longNext = @(ItemLines $out | Where-Object {
    $v = DecodeNext (FieldOf $_ 'next')
    $head = ($v -split ' ')[0]
    $isAsciiCmd = ($v.IndexOf(' ') -gt 0) -and ($head -match '^[A-Za-z][A-Za-z0-9_.-]*$')
    (-not $isAsciiCmd) -and ($v.Length -gt 20)
})
Check 'every next= value is either a command (first word is an ASCII command name) or a short sentence of at most 20 characters' `
    ($longNext.Count -eq 0) (($longNext | ForEach-Object { DecodeNext (FieldOf $_ 'next') }) -join ' | ')
Check 'per-profile guidance really differs (desktop next != web next)' `
    ((NextOf $out 'desktop') -ne (NextOf $out 'web')) 'identical next text'
Check 'the content-mismatch row points at the hashed file in its own next= (it names the .js files)' `
    ((NextOf $out 'stale').Contains('.js')) (NextOf $out 'stale')
Check 'the not-installed row says not-installed and never claims an installed version' `
    ((NextOf $out 'none').Contains($zhNotInstalled) -and (-not (NextOf $out 'none').Contains($zhInstalled + ' '))) (NextOf $out 'none')
Check 'a not-installed profile is not told to run the web command' `
    (-not (NextOf $out 'none').Contains('bridge-install --profile web')) (NextOf $out 'none')

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

# ---- C. information conservation: what left the card must be READABLE somewhere (2026-10-11) ----
# The readability pass moved the mechanism prose and the bundled path out of the update-page card and
# into the GUI's About page. That is only honest if the moved text really exists there - so grep the GUI
# source itself. Two notes:
#   * the About prose lives in Markers\UpdateBaselineExplain.cs as a PURE function, so LogicTests asserts
#     it too (this static grep is the second, independent witness);
#   * the file is read as UTF-8 EXPLICITLY: the .ps1 itself is pure ASCII (no BOM), so the default
#     encoding of Windows PowerShell 5.1 would mangle the Chinese text and produce a false failure.
# The rendered page is proven by shots\gui-work\pluginread-*.png.
Write-Host ''
Write-Host '== C. the moved explanation is really present in the GUI About page =='
function ReadUtf8([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $enc = New-Object System.Text.UTF8Encoding($false)
    return $enc.GetString($bytes)
}
$explainPath = Join-Path $Repo 'v3\gui\Dsht.Gui.Avalonia\Markers\UpdateBaselineExplain.cs'
$explain = ReadUtf8 $explainPath
Check 'GUI About-page text source was found (Markers\UpdateBaselineExplain.cs)' ($explain.Length -gt 0) $explainPath
Check 'About page still explains the baseline (the bundled copy, decided offline, no new channel)' `
    ($explain.Contains('plugin\dsh-minato-bridge') -and $explain.Contains($zhOffline) -and $explain.Contains($zhBaseline)) 'About text lost the baseline paragraph'
Check 'About page still explains the double comparison (version + content hash of snapshot.js / index.js)' `
    ($explain.Contains('VersionComparer') -and $explain.Contains('SHA-256') -and $explain.Contains('snapshot.js') -and $explain.Contains('index.js')) 'About text lost the double-comparison paragraph'
Check 'About page still gives the full path of the bundled baseline (exe dir + plugin folder)' `
    ($explain.Contains('PathsText') -and $explain.Contains('"plugin", "dsh-minato-bridge"')) 'About text lost the path paragraph'
Check 'About page still says the desktop profile is managed by the desktop app and cannot be installed from the command line' `
    ($explain.Contains($zhDesktopWord) -and $explain.Contains($zhCliNoInstall)) 'About text lost the desktop-managed wording'

if (-not $Keep) {
    foreach ($d in @($root, $bare)) { Remove-Item -LiteralPath $d -Recurse -Force -ErrorAction SilentlyContinue }
}
Write-Host ''
Write-Host ('== plugin update baseline: ' + $pass + '/' + ($pass + $fail) + ' passed, ' + $fail + ' failed ==')
if ($fail -gt 0) { exit 1 }
exit 0
