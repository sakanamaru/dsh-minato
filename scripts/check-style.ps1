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

# Rules about prose must look at comments only: a date inside a test fixture is data, not a
# changelog entry, and a check mark inside a string literal is a thing the program prints.
function Get-CommentText([string]$text) {
    $comments = New-Object System.Text.StringBuilder
    $i = 0
    $state = "code"
    while ($i -lt $text.Length) {
        $ch = $text[$i]
        $next = if ($i + 1 -lt $text.Length) { $text[$i + 1] } else { [char]0 }

        if ($state -eq "code") {
            if ($ch -eq '/' -and $next -eq '/') { $state = "line"; $i += 2; continue }
            if ($ch -eq '/' -and $next -eq '*') { $state = "block"; $i += 2; continue }
            if ($ch -eq '"') { $state = "string"; $i++; continue }
            if ($ch -eq "'") { $state = "char"; $i++; continue }
            $i++; continue
        }
        if ($state -eq "line") {
            if ($ch -eq [char]10) { $state = "code"; [void]$comments.Append([char]10); $i++; continue }
            [void]$comments.Append($ch); $i++; continue
        }
        if ($state -eq "block") {
            if ($ch -eq '*' -and $next -eq '/') { $state = "code"; $i += 2; continue }
            [void]$comments.Append($ch); $i++; continue
        }
        if ($state -eq "string") {
            if ($ch -eq [char]92) { $i += 2; continue }
            if ($ch -eq '"') { $state = "code" }
            $i++; continue
        }
        if ($state -eq "char") {
            if ($ch -eq [char]92) { $i += 2; continue }
            if ($ch -eq "'") { $state = "code" }
            $i++; continue
        }
    }
    return $comments.ToString()
}

function Count-InComments([string]$pattern) {
    $count = 0
    foreach ($file in $files) {
        $comments = Get-CommentText ([System.IO.File]::ReadAllText($file.FullName))
        $count += ([regex]::Matches($comments, $pattern)).Count
    }
    return $count
}

# 1. Decorative marks in comments. Prose does not need check marks; they read as process noise.
Add-Max 'no-decorative-marks' (Count-InComments '[\u2713\u2717\u2605\u2606\u26A0]') 0 'check marks and stars belong in a review, not in source'

# 2. Arrows used as prose glue.
Add-Max 'no-arrow-prose' (Count-InComments '\u2192') 0 'rewrite the sentence instead of drawing an arrow'

# 3. Date stamps. git blame already answers when and why.
Add-Max 'no-date-stamps' (Count-InComments '20[0-9]{2}-[0-1][0-9]-[0-3][0-9]') 0 'a date in a comment is a changelog entry in the wrong place'

# 4. Section references to documents a reader of the code cannot resolve.
Add-Max 'no-doc-crossrefs' (Count-InComments '\u00A7') 0 'cite the file, not a section number from a conversation'

# 5. Placeholder markers. Unimplemented behaviour returns exit code 3 instead.
Add-Max 'no-placeholders' (Count-InComments 'TODO|FIXME|XXX|HACK') 0 'open an issue; the code stays clean'

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