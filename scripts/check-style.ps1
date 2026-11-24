# Repository style guard.
#
# These rules exist because every one of them was a real defect in this project's v3 tree:
# thousands of decorative marks in comments, date stamps that git blame already records,
# cross-references a reader cannot resolve, swallowed exceptions, and files large enough that
# nobody reviews them. A rule that cannot fail is a slogan, so each check below has a count
# and a non-zero exit.
#
# Usage: powershell -File scripts/check-style.ps1 [-Root <path>] [-Json]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [switch]$Json
)

$ErrorActionPreference = 'Stop'
Set-Location $Root

$files = @(Get-ChildItem 'src', 'tests' -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
if ($files.Count -eq 0) { Write-Error 'no C# sources found'; exit 2 }

$results = New-Object System.Collections.Generic.List[object]

# A maximum rule passes while found <= limit (0 means "must not occur at all").
function Add-Max([string]$name, [int]$found, [int]$limit, [string]$why) {
    $results.Add([pscustomobject]@{
        Rule = $name; Found = $found; Limit = $limit; Ok = ($found -le $limit); Why = $why
    })
}

# A minimum rule passes while found >= limit; used where "too little" is the defect.
function Add-Min([string]$name, [int]$found, [int]$limit, [string]$why) {
    $results.Add([pscustomobject]@{
        Rule = $name; Found = $found; Limit = $limit; Ok = ($found -ge $limit); Why = $why
    })
}

function Count-Matches([string]$pattern) {
    $count = 0
    foreach ($file in $files) {
        $count += ([regex]::Matches([System.IO.File]::ReadAllText($file.FullName), $pattern)).Count
    }
    return $count
}

# 1. Decorative marks in comments. Prose does not need check marks; they read as process noise.
Add-Max 'no-decorative-marks' (Count-Matches '[\u2713\u2717\u2605\u2606\u26A0]') 0 'check marks and stars belong in a review, not in source'

# 2. Arrows used as prose glue.
Add-Max 'no-arrow-prose' (Count-Matches '\u2192') 0 'rewrite the sentence instead of drawing an arrow'

# 3. Date stamps. git blame already answers when and why.
Add-Max 'no-date-stamps' (Count-Matches '20[0-9]{2}-[0-1][0-9]-[0-3][0-9]') 0 'a date in a comment is a changelog entry in the wrong place'

# 4. Section references to documents a reader of the code cannot resolve.
Add-Max 'no-doc-crossrefs' (Count-Matches '\u00A7') 0 'cite the file, not a section number from a conversation'

# 5. Placeholder markers. Unimplemented behaviour returns exit code 3 instead.
Add-Max 'no-placeholders' (Count-Matches 'TODO|FIXME|XXX|HACK') 0 'open an issue; the code stays clean'

# 6. Swallowed exceptions. `catch { }` hides the reason a repair failed.
Add-Max 'no-swallowed-exceptions' (Count-Matches 'catch\s*\{\s*\}') 0 'log it or convert it to a fact; silence is not handling'

# 7. File length. Anything over this size stops being reviewable.
$oversized = 0
foreach ($file in $files) {
    $lines = (Get-Content -LiteralPath $file.FullName | Measure-Object -Line).Lines
    if ($lines -gt 400) { $oversized++ }
}
Add-Max 'file-length' $oversized 0 'split by responsibility, not by line count'

# 8. Source must be ASCII: one language for code and comments, no drift.
$nonAscii = 0
foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($ch in $text.ToCharArray()) { if ([int]$ch -gt 127) { $nonAscii++; break } }
}
Add-Max 'ascii-source' $nonAscii 0 'code and comments are English; localisation belongs in resources'

# 9. Test-to-source ratio, measured against the code that is actually unit-testable.
# The Windows host layer is API glue; it is covered by the smoke test in CI, not by mocks.
$srcLines = 0; $testLines = 0
foreach ($file in (Get-ChildItem 'src\Dsht.Toolbox.Core' -Recurse -Filter '*.cs' -File)) {
    $srcLines += (Get-Content -LiteralPath $file.FullName | Measure-Object -Line).Lines
}
foreach ($file in (Get-ChildItem 'tests' -Recurse -Filter '*.cs' -File)) {
    $testLines += (Get-Content -LiteralPath $file.FullName | Measure-Object -Line).Lines
}
$ratio = if ($srcLines -gt 0) { [math]::Round($testLines / $srcLines, 2) } else { 0 }
Add-Min 'test-ratio' ([int]($ratio * 100)) 25 'tests below a quarter of the testable source are a gesture, not coverage'

# 10. Command surface cap is a product rule, checked here as well as in the tests.
$surface = (Select-String -Path 'src\Dsht.Toolbox.Core\CommandSurface.cs' -Pattern '"(hud|diag|scan|repair|backup|port|profile|log)"' -AllMatches).Matches.Count
Add-Max 'command-surface' $surface 8 'a toolbox that keeps growing stops being a toolbox'

$failed = @($results | Where-Object { -not $_.Ok })

if ($Json) {
    $results | ConvertTo-Json -Depth 4
} else {
    foreach ($r in $results) {
        $mark = if ($r.Ok) { 'ok  ' } else { 'FAIL' }
        Write-Host (('[{0}] {1,-24} found={2} limit={3}  {4}' -f $mark, $r.Rule, $r.Found, $r.Limit, $r.Why))
    }
    Write-Host ''
    Write-Host ('style guard: ' + ($results.Count - $failed.Count) + ' ok, ' + $failed.Count + ' failed')
}

if ($failed.Count -gt 0) { exit 1 }
exit 0