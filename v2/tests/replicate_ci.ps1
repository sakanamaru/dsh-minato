# replicate_ci.ps1 —— 在本地复刻 CI 的 v2 步骤（补上"本地门槛看不到 v2 测试"的盲区）
#
# 为什么存在：`v3/tests/verify_switchover.ps1` 会**编译** v2（invariant v2.x release build），
# 但**不跑** v2 的单元测试与 GUI 逻辑测试 —— 那两块只在 CI 的 `unit + integration tests` job 里跑。
# 结果是：改动 v2 的路径/夹具/相对位置时，本地全绿而 CI 变红（2026-10 的 v2 迁移就是这么失败三次的，
# 其中一次是 294/318）。
#
# 本脚本逐条照抄 `.github/workflows/build-release.yml` 里的命令，在本地把它们跑一遍。
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File v2\tests\replicate_ci.ps1 -Fast
#       → 只跑"快且高价值"的两项：v2 单元测试 + GUI 逻辑测试（几秒；门槛里用的就是这个）
#   powershell -NoProfile -ExecutionPolicy Bypass -File v2\tests\replicate_ci.ps1
#       → 完整复刻：单元 / dotnet build / GUI 变体编译 / GUI 逻辑 / 集成测试 / 发布编译 / 清单+zip 断言
#
# 说明：
#   · 集成测试只碰打桩目录 ~/.dsh_test，绝不接触真实 ~/.dsh；3080 未开时相关用例标 SKIP。
#   · 本脚本**不是**发布链的一部分（发布清单只收集 v2\src\**，不收集 v2\tests\**）。
#   · 本地复刻 ≠ CI：Linux 平台实现、5 个 shell 验证脚本、打包 job 只在 CI 跑。
# 退出码：0=全过；1=有失败
param(
    [switch]$Fast,
    [string]$Repo = ""
)
$ErrorActionPreference = 'Continue'   # 靠 rc 判断；csc 往 stderr 写警告不该终止脚本

if (-not $Repo) { $Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
# 布局自适应：v2 树可能在仓库根，也可能在 v2\ 子目录
$v2 = $Repo
if (-not (Test-Path -LiteralPath (Join-Path $v2 'dsh_v2.cs'))) {
    $alt = Join-Path $Repo 'v2'
    if (Test-Path -LiteralPath (Join-Path $alt 'dsh_v2.cs')) { $v2 = $alt }
}
Set-Location $Repo

# C2 FIX（2026-10-06 审查）：全程落盘日志 —— 上次"1 项失败"没留完整输出、失败项无从定位 ✗
# 现在每一步的完整原始输出都写进带时间戳的日志；结尾（无论成败）都打印日志路径 ✓
$script:logFile = Join-Path $env:TEMP ('v2-ci-replicate-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
Write-Host "全程日志：$script:logFile"
function LogRaw($title, $text) {
    Add-Content -LiteralPath $script:logFile -Value ("`r`n===== " + $title + " =====`r`n" + $text) -Encoding UTF8
}

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) { Write-Error "找不到 csc：$csc（需 Windows + .NET Framework 4.x）"; exit 2 }

# dotnet：优先 PATH 上**带 SDK** 的那个；否则退回用户目录里的 SDK
$dotnet = $null
foreach ($cand in @((Get-Command dotnet -ErrorAction SilentlyContinue).Source, (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'))) {
    if ($cand -and (Test-Path -LiteralPath $cand)) {
        $sdks = & $cand --list-sdks 2>$null
        if ($LASTEXITCODE -eq 0 -and $sdks) { $dotnet = $cand; break }
    }
}

$script:fail = 0
function Check($name, $ok, $detail) {
    $line = "  [{0}] {1,-34} {2}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $name, $detail
    if ($ok) { Write-Host $line }
    else { Write-Host $line -ForegroundColor Red; $script:fail++ }
    Add-Content -LiteralPath $script:logFile -Value $line -Encoding UTF8
}
function SrcList {
    return @((Join-Path $v2 'dsh_v2.cs')) + @(Get-ChildItem (Join-Path $v2 'src') -Recurse -Filter *.cs |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ForEach-Object FullName)
}

# ======================================================= v2 单元测试（CI: Build & run unit tests）
Write-Host "`n=== [test] v2 unit tests (same-assembly, /define:UNIT) ==="
$src = SrcList
& $csc /nologo /target:exe /define:UNIT /out:unittests.exe $src (Join-Path $v2 'tests\unit_tests.cs') /warn:4 2>&1 | Out-String | ForEach-Object { LogRaw "csc step output" $_ }
Check 'unit test build' ($LASTEXITCODE -eq 0) "$($src.Count) 个源文件"
if ($LASTEXITCODE -eq 0) {
    $u = & .\unittests.exe 2>&1 | Out-String
    LogRaw 'v2 unit tests full output' $u
    $rc = $LASTEXITCODE
    $sum = (($u -split "`n") | Where-Object { $_ -match 'passed' } | Select-Object -Last 1)
    Check 'unit tests run' ($rc -eq 0) ("rc=$rc  " + $sum.Trim())
    if ($rc -ne 0) { (($u -split "`n") | Where-Object { $_ -match '\[FAIL\]' } | Select-Object -First 10) | ForEach-Object { Write-Host "         $_" } }
}

# ======================================================= GUI 逻辑测试（CI: GUI logic tests）
Write-Host "`n=== [test] v2 GUI logic tests ==="
& $csc /nologo /target:exe /main:GuiLogicTests /out:guilogictests.exe (Join-Path $v2 'gui_v2.cs') (Join-Path $v2 'tests\gui_logic_tests.cs') /warn:4 2>&1 | Out-String | ForEach-Object { LogRaw "csc step output" $_ }
Check 'gui logic build' ($LASTEXITCODE -eq 0) ''
if ($LASTEXITCODE -eq 0) {
    $g = & .\guilogictests.exe 2>&1 | Out-String
    LogRaw 'v2 GUI logic tests full output' $g
    $grc = $LASTEXITCODE
    $gsum = (($g -split "`n") | Where-Object { $_ -match 'passed' } | Select-Object -Last 1)
    Check 'gui logic tests run' ($grc -eq 0) ("rc=$grc  " + $gsum.Trim())
    if ($grc -ne 0) { (($g -split "`n") | Where-Object { $_ -match '\[FAIL\]' } | Select-Object -First 10) | ForEach-Object { Write-Host "         $_" } }
}
Remove-Item unittests.exe, guilogictests.exe -Force -ErrorAction SilentlyContinue

if ($Fast) {
    Write-Host ""
    Write-Host "完整日志：$script:logFile"
    if ($script:fail -eq 0) { Write-Host "== v2 测试（快速模式）：全部通过 ==" -ForegroundColor Green; exit 0 }
    Write-Host "== v2 测试（快速模式）：$($script:fail) 项失败 ==" -ForegroundColor Red; exit 1
}

# ======================================================= dotnet build（CI: stage 2 core project）
Write-Host "`n=== [test] dotnet build (v2.8 stage 2 core project, net8.0) ==="
if (-not $dotnet) { Check 'dotnet build core project' $false 'no SDK found' }
else {
    $dbOut = & $dotnet build (Join-Path $v2 'DeepSeekHarnessToolkit.Core.csproj') -c Release --nologo 2>&1 | Out-String
    LogRaw 'dotnet build core project' $dbOut
    ($dbOut -split "`n") | Select-Object -Last 2 | ForEach-Object { Write-Host $_ }
    Check 'dotnet build core project' ($LASTEXITCODE -eq 0) ''
    $dll = Get-Item (Join-Path $v2 'bin\Release\net8.0\DeepSeek Harness Toolkit.dll') -ErrorAction SilentlyContinue
    Check 'core dll produced' ($null -ne $dll) $(if ($dll) { "$([math]::Round($dll.Length/1KB,1)) KB" } else { 'missing' })
}

# ======================================================= GUI 变体编译（CI: Compile GUI variants）
Write-Host "`n=== [test] Compile GUI variants (attached + standalone) ==="
& $csc /nologo /optimize+ /target:exe /win32icon:icon.ico /out:core_check.exe $src /warn:4 2>&1 | Out-String | ForEach-Object { LogRaw "csc step output" $_ }
Check 'core compile guard' ($LASTEXITCODE -eq 0) ''
& $csc /nologo /optimize+ /target:winexe /win32icon:icon.ico ("/win32manifest:" + (Join-Path $v2 'app.manifest')) /resource:logo.png /out:gui_check.exe (Join-Path $v2 'gui_v2.cs') /warn:4 2>&1 | Out-String | ForEach-Object { LogRaw "csc step output" $_ }
Check 'GUI (attached) compile' ($LASTEXITCODE -eq 0) ''
& $csc /nologo /optimize+ /target:winexe /win32icon:icon.ico ("/win32manifest:" + (Join-Path $v2 'app.manifest')) /resource:logo.png /resource:core_check.exe,DSHCore.exe /out:gui_standalone_check.exe (Join-Path $v2 'gui_v2.cs') /warn:4 2>&1 | Out-String | ForEach-Object { LogRaw "csc step output" $_ }
Check 'GUI (standalone) compile' ($LASTEXITCODE -eq 0) ''
Remove-Item core_check.exe, gui_check.exe, gui_standalone_check.exe -Force -ErrorAction SilentlyContinue

# ======================================================= 集成测试（CI: Run integration tests）
Write-Host "`n=== [test] Run integration tests (stubbed matrix, no real ~/.dsh) ==="
$i = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $v2 'tests\integration.ps1') -RepoRoot $v2 2>&1 | Out-String
LogRaw 'v2 integration tests full output' $i
$irc = $LASTEXITCODE
Check 'integration tests' ($irc -eq 0) "rc=$irc"
if ($irc -ne 0) { (($i -split "`n") | Where-Object { $_ -match 'FAIL' } | Select-Object -First 15) | ForEach-Object { Write-Host "         $_" } }
else { (($i -split "`n") | Where-Object { $_ -match 'PASS \d+  FAIL' } | Select-Object -Last 1) | ForEach-Object { Write-Host "         $($_.Trim())" } }

# ======================================================= 发布编译（CI: build job）
Write-Host "`n=== [build] Release compiles (csc) ==="
& $csc /nologo /optimize+ /target:exe /win32icon:icon.ico "/out:DeepSeek Harness Toolkit.exe" $src /warn:4 2>&1 | Out-String | ForEach-Object { LogRaw "csc step output" $_ }
Check 'core exe' ($LASTEXITCODE -eq 0) ''
& $csc /nologo /optimize+ /target:winexe /win32icon:icon.ico ("/win32manifest:" + (Join-Path $v2 'app.manifest')) /resource:logo.png "/out:Toolkit GUI.exe" (Join-Path $v2 'gui_v2.cs') /warn:4 2>&1 | Out-String | ForEach-Object { LogRaw "csc step output" $_ }
Check 'Toolkit GUI.exe' ($LASTEXITCODE -eq 0) ''
& $csc /nologo /optimize+ /target:winexe /win32icon:icon.ico ("/win32manifest:" + (Join-Path $v2 'app.manifest')) /resource:logo.png "/resource:DeepSeek Harness Toolkit.exe,DSHCore.exe" "/out:Toolkit GUI Standalone.exe" (Join-Path $v2 'gui_v2.cs') /warn:4 2>&1 | Out-String | ForEach-Object { LogRaw "csc step output" $_ }
Check 'Toolkit GUI Standalone.exe' ($LASTEXITCODE -eq 0) ''

# ======================================================= 发布清单（CI: Regenerate hashes.txt）
# ★ 这一节守的是"发布包布局 ≠ 仓库布局"：清单里的名字必须是**包内**的相对路径。
#   搬动 v2 树时最容易在这里翻车（清单会写成 v2\dsh_v2.cs，而包里是 dsh_v2.cs）。
Write-Host "`n=== [build] Regenerate hashes.txt — 复刻 + 断言清单名是包内平铺 ==="
$rel = if ($v2 -eq $Repo) { '' } else { 'v2\' }
$files = @('DeepSeek Harness Toolkit.exe', 'Toolkit GUI.exe', 'Toolkit GUI Standalone.exe',
    ($rel + 'dsh_v2.cs'), ($rel + 'build_exe.cmd'), 'icon.ico', 'logo.png', 'README.md',
    'README_zh-CN.md', 'LICENSE', '.github/SECURITY.md', ($rel + 'verify.ps1'), ($rel + '.dsh_launcher_root'))
$files += (Get-ChildItem ($rel + 'src') -Recurse -Filter *.cs | ForEach-Object { $_.FullName.Replace($Repo + '\', '') })
$lines = New-Object System.Collections.Generic.List[string]
foreach ($f in $files) {
    if (-not (Test-Path -LiteralPath $f)) { Check "manifest source exists: $f" $false 'MISSING'; continue }
    $h = (Get-FileHash $f -Algorithm SHA256).Hash.ToLowerInvariant()
    # 与 workflow 同一套剥前缀规则（Compress-Archive 只取叶子名 → 清单也必须写叶子/包内路径）
    $lines.Add("$h  " + ($f -replace '^v2[\\/]', '' -replace '^\.github[\\/]', ''))
}
$bad = @($lines | Where-Object { $_ -match '  v2[\\/]' })
Check 'manifest names package-flat' ($bad.Count -eq 0) "$($lines.Count) 行；带 v2\ 前缀的 $($bad.Count) 行"

# ======================================================= upload zip（CI: Pack upload zip）
Write-Host "`n=== [build] Pack upload zip — 复刻 + 断言条目平铺 ==="
$items = @('DeepSeek Harness Toolkit.exe', 'Toolkit GUI.exe', 'Toolkit GUI Standalone.exe',
    ($rel + 'dsh_v2.cs'), ($rel + 'build_exe.cmd'), 'icon.ico', 'logo.png', 'README.md',
    'README_zh-CN.md', 'LICENSE', '.github/SECURITY.md', ($rel + 'verify.ps1'), '.gitignore',
    ($rel + 'hashes.txt'), ($rel + '.dsh_launcher_root'), ($rel + 'src'))
$zip = Join-Path $env:TEMP 'dsh_zip_probe.zip'
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path $items -DestinationPath $zip -CompressionLevel Optimal -ErrorAction SilentlyContinue
if (-not (Test-Path -LiteralPath $zip)) { Check 'zip built' $false 'Compress-Archive failed' }
else {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $z = [System.IO.Compression.ZipFile]::OpenRead($zip)
    $names = @($z.Entries | ForEach-Object { $_.FullName })
    $z.Dispose()
    $badZip = @($names | Where-Object { $_ -like 'v2\*' -or $_ -like 'v2/*' })
    Check 'zip entries package-flat' ($badZip.Count -eq 0) "$($names.Count) 个条目；带 v2\ 前缀的 $($badZip.Count) 个"
    foreach ($must in @('dsh_v2.cs', '.dsh_launcher_root', 'verify.ps1', 'hashes.txt', 'build_exe.cmd', 'icon.ico', 'logo.png', 'src\Core\Program.State.cs')) {
        Check "zip contains $must" ($names -contains $must) ''
    }
    # 端到端：清单里**每个**名字都必须能在 zip 里找到（前缀错位一次抓出）
    $manNames = @($lines | ForEach-Object { ($_ -split '  ', 2)[1] })
    $notInZip = @($manNames | Where-Object { $names -notcontains $_ })
    Check 'every manifest name is in the zip' ($notInZip.Count -eq 0) "$($manNames.Count) 个名字；缺失 $($notInZip.Count) 个$(if ($notInZip.Count -gt 0) { ' -> ' + ($notInZip -join ',') })"
    Remove-Item $zip -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "完整日志：$script:logFile"
Write-Host ""
if ($script:fail -eq 0) { Write-Host "== 本地 CI 复刻：全部通过 ==" -ForegroundColor Green; exit 0 }
Write-Host "== 本地 CI 复刻：$($script:fail) 项失败 ==" -ForegroundColor Red; exit 1
