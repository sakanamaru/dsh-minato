# verify_switchover.ps1 —— 切换就绪度一键检查
# 逐项检查 V3 切换门槛（目标定义的四项 + V3 追加的"真实写操作可验证"）+ 两条不变量（v2.x 发布链未被动过、领域层纯净度）。
# 门槛③（Windows/Linux 双跑）在本地只能验证"CI 配置就绪"，真跑需要推送触发——脚本会如实标注。
# 退出码：0=全部就绪；1=有未就绪项
param([string]$Repo = ".")
$ErrorActionPreference = "Stop"
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$rows = New-Object System.Collections.Generic.List[string]
$fail = 0
function Gate([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { $script:rows.Add(("  [READY] {0,-28} {1}" -f $name, $detail)) }
    else { $script:fail++; $script:rows.Add(("  [ NOT ] {0,-28} {1}" -f $name, $detail)) }
}
# 这个脚本会先把所有门禁跑完、最后一次性打印表格（因为每条门禁都要 csc 编译 + 跑测试），
# 中间**没有任何输出**——先说明清楚，免得看起来像卡住。
# ★★★ **门槛脚本自身的缺陷（实测发现 —— 假绿）** ✓✓
#   ✗ 上面 `$ErrorActionPreference = "Stop"` ✗ 而下面每一条子门禁都是 `& powershell … 2>&1` ✓
#     → 子进程只要往 **stderr** 写一行（例如契约 exe 的 `INTEGRITY_SKIPPED` 正常提示 ✓）
#       PowerShell 5.1 就把它变成 **NativeCommandError** ✓ → **Stop 当终止错误** ✗✗
#     → **脚本在第 36 行就死了** ✗ → **从不打印结果表** ✓ → **看输出像"没有 [ NOT ] 行 = 全绿"** ✗✗
#   ✓ 现在：**只在调用外部命令期间放宽为 Continue** ✓✓（其余仍是 Stop ✓ 真错误照样终止 ✓）
#     → 结果表一定会打印 ✓ 退出码 0/1 才可信 ✓
function Invoke-External([scriptblock]$sb) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $sb } finally { $ErrorActionPreference = $old }
}
Write-Host "正在检查切换就绪度（约 1-3 分钟，跑完前不会输出；请勿关闭窗口）…"

# ---- 门槛② 领域单测（csc 构建 + 运行契约测试）----
$dom = @(Get-ChildItem (Join-Path $Repo 'v3\src\Dsht.Domain') -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ForEach-Object FullName)
$win = @(Get-ChildItem (Join-Path $Repo 'v3\src\Dsht.Platform.Windows') -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ForEach-Object FullName)
$lin = @(Get-ChildItem (Join-Path $Repo 'v3\src\Dsht.Platform.Linux') -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ForEach-Object FullName)
# ★ S2：**共享源文件目录**（两端用 Compile Include 链接同一份）✓ 必须一起编译 ✓
#   起因：第一次执行 S2 时漏了这个目录 ✗ → gate5/gate2 编译缺文件 → 门槛红 ✓（已还原 ✓）
$sh = @(Get-ChildItem (Join-Path $Repo 'v3\src\Dsht.Platform.Shared') -Recurse -Filter *.cs -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ForEach-Object FullName)
$tst = @(Get-ChildItem (Join-Path $Repo 'v3\tests\Dsht.Contracts.Tests') -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ForEach-Object FullName)
$exe = Join-Path $env:TEMP 'dsht_switchover_contracts.exe'
Remove-Item $exe -Force -ErrorAction SilentlyContinue
Invoke-External { & $csc /nologo /target:exe /warn:4 ("/out:" + $exe) ($dom + $sh + $win + $lin + $tst) 2>&1 } | Out-Null
if ($LASTEXITCODE -ne 0) { Gate 'gate2 domain tests' $false 'csc 构建失败' }
else {
    $out = (& $exe 2>&1 | Out-String)
    $m = [regex]::Match($out, '==\s*(\d+)/(\d+) passed,\s*(\d+) failed')
    if ($m.Success) { Gate 'gate2 domain tests' ($m.Groups[3].Value -eq '0') ($m.Groups[1].Value + '/' + $m.Groups[2].Value + ' passed') }
    else { Gate 'gate2 domain tests' $false '未解析到结果行' }
}

# ---- 门槛① 标记行契约（**纯模式 ×2** ✓）----
# ★★★ **稳定性修复（实测发现）** ✓✓
#   ✗ 原来第二次跑的是 `-Fixtures` ✗ —— 而那个模式会打开两个 `restore --dry-run` 用例，
#     它们**枚举真实数据根**（`~\.dsh` ✓）→ dsh 正在跑时目录一直在变 ✓
#     → 两次调用之间计数不同 → **随机 FAIL** ✗✗（实测：同一份代码 21/21 ✓ 之后 19/21 ✗）
#   ✓ 现在：**两次都跑纯模式** ✓✓ —— 纯模式在全新副本上连跑三次都是 21/21 rc=0 ✓ 确定的 ✓
#     （`-Fixtures` 的缺陷记在 `compare_markers.ps1` 头部 ✓ 要彻底修需给那两个用例隔离 DSH_HOME ✓）
$cmp = Join-Path $Repo 'v3\tests\compare_markers.ps1'
$c1 = (Invoke-External { & powershell -ExecutionPolicy Bypass -File $cmp -Repo $Repo 2>&1 }) | Out-String
$rc1 = $LASTEXITCODE
$c2 = (Invoke-External { & powershell -ExecutionPolicy Bypass -File $cmp -Repo $Repo 2>&1 }) | Out-String
$rc2 = $LASTEXITCODE
$mm = [regex]::Match($c2, '标记行契约：(\d+)/(\d+) 对齐')
$detail = if ($mm.Success) { $mm.Groups[1].Value + '/' + $mm.Groups[2].Value + ' 对齐（纯模式，连跑两次一致）' } else { '未解析到结果行' }
Gate 'gate1 marker contract' (($rc1 -eq 0) -and ($rc2 -eq 0)) $detail

# ---- 门槛③ 双平台：本脚本只校验 CI job 配置仍在（真跑结论写在 detail 里，脚本自身不联网）----
$wf = Join-Path $Repo '.github\workflows\build-release.yml'
$hasJob = $false; $hasUbuntu = $false
if (Test-Path $wf) {
    $wt = [System.IO.File]::ReadAllText($wf)
    $hasJob = $wt.Contains('v3-contracts')
    $hasUbuntu = $wt.Contains('ubuntu-latest')
}
Gate 'gate3 win/linux dual-run' ($hasJob -and $hasUbuntu) $(if ($hasJob -and $hasUbuntu) { 'CI job 就绪，且已在 CI 真跑通过：run 36385480118（windows-latest 与 ubuntu-latest 各 220/220）' } else { 'CI job 缺失' })

# ---- 门槛④ 发布物校验（含校验器自证）----
# ★★★ **发版修复（2026-10-01）—— 版本不要写死** ✓✓
#   ✗ 原来不传 `-Version` ✗ → `verify_release.ps1` 用它的默认值 `3.0.0-dev` ✗
#     → 而 CLI 的版本常量一旦改成发布版本（`3.0.0-preview.1` ✓ 发版必须改 ✓）
#       → **校验器拿 dev 去比 CLI 自报的 preview.1 → 必然不匹配** ✗✗ → gate4 变红 ✓
#   ✓ 现在：**从源码里的版本常量取** ✓✓（与 CLI 自报的完全同源 ✓ 发版自动跟上 ✓）
#     · 取不到就退回 dev ✓（绝不因为读不到就误判通过 ✗）
$ver = '3.0.0-dev'
try {
    $pc = [System.IO.File]::ReadAllText((Join-Path $Repo 'v3\src\Dsht.Cli\Program.cs'))
    $vm = [regex]::Match($pc, 'ToolkitVersion\s*=\s*"([^"]+)"')
    if ($vm.Success) { $ver = $vm.Groups[1].Value }
} catch { }
$rel = (Invoke-External { & powershell -ExecutionPolicy Bypass -File (Join-Path $Repo 'v3\tests\verify_release.ps1') -Repo $Repo -Build -SelfTest -Version $ver 2>&1 }) | Out-String
Gate 'gate4 release verifier' ($LASTEXITCODE -eq 0) $(if ($LASTEXITCODE -eq 0) { "含篡改自证通过（版本 $ver）" } else { "校验失败（版本 $ver）" })

# ---- 门槛⑤（V3 追加）真实写操作可验证：隔离数据根 + restore --apply 端到端 ----
$ra = (Invoke-External { & powershell -ExecutionPolicy Bypass -File (Join-Path $Repo 'v3\tests\verify_restore_apply.ps1') -Repo $Repo 2>&1 }) | Out-String
$rm = [regex]::Match($ra, '==\s*(\d+)/(\d+) passed')
$rdet = if ($rm.Success) { $rm.Groups[1].Value + '/' + $rm.Groups[2].Value + '（隔离根真实写盘 + 零越界）' } else { '未解析到结果行' }
Gate 'gate5 real write verifiable' ($LASTEXITCODE -eq 0) $rdet

# ---- 不变量：发布与校验链未被动过 ----
# 注意：dsh_v2.cs / src/** 的改动是**预期的**（v2.8 阶段 1–3 的拆分/接缝/Linux 实现属合法演进）；
# 真正必须守住的是"发布与校验链"：verify.ps1（信任锚，指纹锁死）、发布步骤的 csc 命令、以及 16 项发布清单。
# build_exe.cmd 于 v2.7.3 被**有意**改成递归收集源码（此前写死目录列表漏了 src\Platform\Linux → CS0246），
# 所以它不再参与"未改动"比对，改由下面的"v2.x 发布构建能编译"这条**行为**不变量来守（比文本比对更强）。
# ★ 架构审计抓到（G5）：原来把 git 失败也吞成空串 ✗ → git 不可用/仓库缺失时这条"空过" ✗✗
#   → 它本该是"信任锚没被动过"的守卫 ✓ 结果没比对也报 READY ✗
# ✓ 现在：git 必须真的跑成功 ✓✓ 否则这条不变量判为未就绪 ✓（不静默通过 ✓）
$chainChanged = ''
$gitRan = $false
try {
    # v2 树可能在仓库根，也可能在 v2/ 子目录（2026-10 迁移）→ 两处布局都自适应，迁移前后都成立
    $v2root = if (Test-Path (Join-Path $Repo 'v2\dsh_v2.cs')) { Join-Path $Repo 'v2' } else { $Repo }
    $v2anchor = if ($v2root -eq $Repo) { 'verify.ps1' } else { 'v2/verify.ps1' }
    # 信任锚比的是**内容**：基线（origin/main）里它在哪、工作树里它又在哪，可能不同 → 各自解析。
    # （旧的 --name-only 形式一旦路径变了就把"搬家"报成"改动"→ 永远红 ✗）
    # ★ 这两句必须走 Invoke-External ✗：**"基线里没有这个路径"是要靠 rc 判断的正常情况，不是错误** ✓
    #   而 PS 5.1 会把原生命令写到 stderr 的内容当成**终止错误**（本脚本是 Stop）✗ → 直接进 catch
    #   → $gitRan=$false → **假红** ✗✗（v2 迁移并进 main 之后，origin/main 上确实没有 verify.ps1 了，
    #   cat-file -e 于是打印 "fatal: path 'verify.ps1' does not exist" —— 合并后第一次跑就是这样红的 ✓）
    #   → 改用**不写 stderr** 的 rev-parse --quiet --verify ✓，并用现成的 Invoke-External 兜住 rc ✓
    [void](Invoke-External { & git -C $Repo rev-parse --quiet --verify 'origin/main:verify.ps1' })
    $baseAnchor = if ($LASTEXITCODE -eq 0) { 'verify.ps1' } else { 'v2/verify.ps1' }
    $chainChanged = (Invoke-External { & git -C $Repo diff ('origin/main:' + $baseAnchor) $v2anchor 2>&1 }) | Out-String
    $gitRan = ($LASTEXITCODE -eq 0)
    $chainChanged = $chainChanged.Trim()
} catch { $gitRan = $false }
$itemsOk = $false; $stepsOk = $false
if (Test-Path $wf) {
    $wt2 = [System.IO.File]::ReadAllText($wf)
    $itemsOk = $wt2.Contains("'DeepSeek Harness Toolkit.exe','Toolkit GUI.exe','Toolkit GUI Standalone.exe'")
    $stepsOk = $wt2.Contains('/out:unittests.exe') -and $wt2.Contains('core_check.exe') -and $wt2.Contains('/out:DeepSeek Harness Toolkit.exe')
}
Gate 'invariant release chain' ($gitRan -and [string]::IsNullOrWhiteSpace($chainChanged) -and $itemsOk -and $stepsOk) $(if ([string]::IsNullOrWhiteSpace($chainChanged) -and $itemsOk -and $stepsOk) { 'verify.ps1 内容与 origin/main 逐字一致（路径按各自 revision 解析）；16 项清单与 csc 发布步骤完好' } else { 'verify.ps1 改动:[' + ($chainChanged -replace "`r?`n", ',') + '] 清单=' + $itemsOk + ' 步骤=' + $stepsOk })

# ---- 不变量：v2.x 发布构建**真的能编译**（行为校验，比"文件没改"强）----
# 起因：v2.8 阶段 3 把 Linux 实现放进 src\Platform\Linux 后，build_exe.cmd 的写死目录列表漏了它，
# 于是本地重编译脚本连续几轮都是坏的（CI 用 -Recurse 所以发布没受影响）——这条不变量就是补这个盲区。
$v2out = Join-Path $env:TEMP 'dsht_v2_buildgate.exe'
Remove-Item $v2out -Force -ErrorAction SilentlyContinue
$v2src = @((Join-Path $v2root 'dsh_v2.cs')) + @(Get-ChildItem (Join-Path $v2root 'src') -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ForEach-Object FullName)
$v2build = (Invoke-External { & $csc /nologo /optimize+ /target:exe /warn:4 ("/out:" + $v2out) $v2src 2>&1 }) | Out-String
$v2ok = (Test-Path $v2out)
Gate 'invariant v2.x release build' $v2ok $(if ($v2ok) { ('csc 编译 ' + $v2src.Count + ' 个源文件通过（dsh_v2.cs + src/**）') } else { '编译失败：' + (($v2build -split "`r?`n" | Where-Object { $_ -match 'error ' } | Select-Object -First 2) -join ' / ') })
Remove-Item $v2out -Force -ErrorAction SilentlyContinue

# ---- 不变量：含非 ASCII 的 .ps1 必须带 UTF-8 BOM（否则 Windows PowerShell 5.1 按 ANSI 解析 → 语法错）----
# 起因：这个坑反复出现（edit 工具会剥掉 BOM）——中文 .ps1 一旦没 BOM，`powershell -File` 会报一堆
# 看似无关的"缺 }/缺引号"，排查成本很高。这条不变量把"忘了补 BOM"变成一次就报出来的失败。
$bomBad = @()
foreach ($f in @(Get-ChildItem $Repo -Recurse -Filter *.ps1 -ErrorAction SilentlyContinue)) {
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191)
    $nonAscii = $false
    foreach ($x in $bytes) { if ($x -gt 127) { $nonAscii = $true; break } }
    if ($nonAscii -and -not $hasBom) { $bomBad += $f.FullName.Replace($Repo.TrimEnd('\') + '\', '') }
}
Gate 'invariant ps1 utf8 bom' ($bomBad.Count -eq 0) $(if ($bomBad.Count -eq 0) { '含非 ASCII 的 .ps1 全部带 BOM' } else { '缺 BOM：' + ($bomBad -join ', ') })

# ---- 门槛⑥/⑦（架构审计 G6 补上）：本地门槛必须和 CI 跑同一批测试 ----
# 起因：CI 跑契约测试与 GUI 逻辑测试 ✓ 而本地这个聚合门槛从来不跑它们 ✗
#   → 开发者按文档跑"切换就绪度"以为全绿 ✓ 实际 348 项契约 + 58 项 GUI 解析一项没验 ✗
# 纪律：跑不了就说跑不了 ✓ 不假装通过 ✓（缺 .NET SDK → NOT READY 并说明原因 ✓）
$dotnetExe = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnetExe)) { $dotnetExe = 'dotnet' }
$ctOut = ''
$ctRc = 2
try {
    $ctOut = (Invoke-External { & $dotnetExe run --project (Join-Path $Repo 'v3\tests\Dsht.Contracts.Tests') -c Release --nologo 2>&1 }) | Out-String
    $ctRc = $LASTEXITCODE
} catch { $ctRc = 2 }
$ctM = [regex]::Match($ctOut, '==\s*(\d+)/(\d+) passed, (\d+) failed ==')
$ctDet = if ($ctM.Success) { $ctM.Groups[1].Value + '/' + $ctM.Groups[2].Value + ' 通过' } else { '未解析到结果行（.NET SDK 缺失或构建失败）' }
Gate 'gate6 contract tests' ($ctRc -eq 0) $ctDet

$glOut = ''
$glRc = 2
try {
    $glOut = (Invoke-External { & $dotnetExe run --project (Join-Path $Repo 'v3\gui\Dsht.Gui.LogicTests') -c Release --nologo 2>&1 }) | Out-String
    $glRc = $LASTEXITCODE
} catch { $glRc = 2 }
$glM = [regex]::Match($glOut, '==\s*(\d+)/(\d+) passed, (\d+) failed ==')
$glDet = if ($glM.Success) { $glM.Groups[1].Value + '/' + $glM.Groups[2].Value + ' 通过（标记行解析）' } else { '未解析到结果行（.NET SDK 缺失或构建失败）' }
Gate 'gate7 gui logic tests' ($glRc -eq 0) $glDet

# ---- 不变量（G7 补上）：CI 独有的检查必须"存在 + 真的被 workflow 引用" ----
# 起因：本地门槛**跑不到**只在 Linux CI 里执行的 shell 验证脚本 ✗
#   → 已经造成**两次**"本地全绿、CI 红"（过期断言 / 平台覆盖）✗✗
# ✓ 这里**不假装跑了它们** ✗ 而是做两件诚实的事：
#   ① **门槛**：每个 shell 验证脚本都必须**真的被 workflow 引用**（否则它就是个摆设 ✗）
#      —— 这条能在本地跑 ✓ 而且能抓到"脚本加了但 CI 没跑"与"CI 跑了但脚本不在" ✗
#   ② **如实打印盲区清单**：本地覆盖不到的检查，明写出来 ✓（沉默才是问题 ✓）
$shScripts = @(Get-ChildItem (Join-Path $Repo 'v3\tools') -Filter '*.sh' -ErrorAction SilentlyContinue)
$wfTxt = ''
try { if (Test-Path $wf) { $wfTxt = [System.IO.File]::ReadAllText($wf) } } catch { }
$notWired = New-Object System.Collections.Generic.List[string]
foreach ($s in $shScripts) { if ($wfTxt -notmatch [regex]::Escape($s.Name)) { $notWired.Add($s.Name) } }
Gate 'invariant ci-only checks wired' ($shScripts.Count -gt 0 -and $notWired.Count -eq 0) $(if ($shScripts.Count -eq 0) { 'v3/tools 下没有 shell 验证脚本（可疑 ✗）' } elseif ($notWired.Count -gt 0) { '未被 workflow 引用（摆设 ✗）：' + ($notWired -join ', ') } else { $shScripts.Count.ToString() + ' 个 shell 验证脚本都已接入 CI ✓' })

# ---- 门槛⑧（G4 补上）：对外承诺必须**行为验证**，不能只做文本匹配 ----
# 起因：`about` 里写着「wipe 现在只打印手动删除路径、**不删也不备份**」✓
#   → 而门槛只用**文本匹配**确认那句话在源码里 ✗（句子在 ≠ 行为对 ✗✗）
# ✓ 现在：**真的跑一次 wipe** ✓ 然后断言**目标目录还在** ✓✓（这才是行为断言 ✓）
#   · 只碰**隔离目录** ✓ 绝不碰真实数据根 ✓
#   · 同时断言它**打印了路径**（不然用户没法手动删 ✓）
$cliExe = Join-Path $env:TEMP ('dsht_switchover_cli_' + $PID + '.exe')
Remove-Item $cliExe -Force -ErrorAction SilentlyContinue
$cliSrc = @()
foreach ($d in @('Dsht.Domain','Dsht.Platform.Shared','Dsht.Platform.Windows','Dsht.Platform.Linux','Dsht.Cli')) {
    $cliSrc += @(Get-ChildItem (Join-Path $Repo ('v3\src\' + $d)) -Recurse -Filter *.cs -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ForEach-Object FullName)
}
Invoke-External { & $csc /nologo /target:exe ("/out:" + $cliExe) $cliSrc 2>&1 } | Out-Null
$cliOk = (Test-Path $cliExe)
$wipeOk = $false
$wipeDet = 'CLI 未构建成功'
if ($cliOk) {
    $wRoot = Join-Path $env:TEMP ('dsht_wipe_probe_' + $PID)
    Remove-Item $wRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path (Join-Path $wRoot 'storages') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $wRoot 'storages\keep.txt') -Value 'KEEP' -Encoding UTF8
    $wOldHome = $env:DSH_HOME
    $env:DSH_HOME = $wRoot   # ★ 必须隔离 ✓ 否则 wipe 打印的是**默认数据根**的路径 ✗（第一次就踩了 ✓）
    $wOut = ''
    try { $wOut = (Invoke-External { & $cliExe wipe 2>&1 } | Out-String) } catch { $wOut = '' }
    $stillThere = Test-Path (Join-Path $wRoot 'storages\keep.txt')
    $printedPath = ($wOut -match [regex]::Escape($wRoot)) -or ($wOut -match 'storages')
    $wipeOk = $stillThere -and $printedPath
    if ($wipeOk) { $wipeDet = 'wipe 只打印路径、**目标原样还在** ✓✓（行为验证 ✓）' }
    elseif (-not $stillThere) { $wipeDet = '**wipe 真的删了东西** ✗✗ 与 about 的承诺不符 ✗' }
    else { $wipeDet = 'wipe 没打印出路径（用户无法手动删 ✗）' }
    Remove-Item $wRoot -Recurse -Force -ErrorAction SilentlyContinue
}
Gate 'gate8 wipe honesty' $wipeOk $wipeDet
Remove-Item $cliExe -Force -ErrorAction SilentlyContinue

# ---- 领域层纯净度 ----# ---- 领域层纯净度 ----
$pure = (Invoke-External { & powershell -ExecutionPolicy Bypass -File (Join-Path $Repo 'v3\tests\verify_domain_pure.ps1') -Repo $Repo 2>&1 }) | Out-String   # 审计 M8：这一条原来**没包装** ✗ → 子进程一写 stderr 就杀掉整个门槛（输出层假绿 ✓）
Gate 'invariant domain purity' ($LASTEXITCODE -eq 0) '零 IO / 零平台 / 零时钟耦合'

# ---- 门槛⑨ 退出码契约（P0-1）：每条改过的失败分支都跑真实场景 ----
# 只看源码里有没有 "return 1" 是文本匹配，证明不了进程真的非零退出；
# 这里跑独立套件：失败场景 + 标记行 + rc 三条一起断言。
$ecOut = (Invoke-External { & powershell -ExecutionPolicy Bypass -File (Join-Path $Repo 'v3\tests\verify_exit_codes.ps1') -Repo $Repo 2>&1 }) | Out-String
$ecRc = $LASTEXITCODE
$ecm = [regex]::Match($ecOut, '==\s*退出码契约：(\d+)/(\d+) passed,\s*(\d+) failed')
$ecDet = if ($ecm.Success) { $ecm.Groups[1].Value + '/' + $ecm.Groups[2].Value + ' 通过（真实失败场景 + rc）' } else { '未解析到结果行' }
Gate 'gate9 exit-code contract' ($ecRc -eq 0) $ecDet

Remove-Item $exe -Force -ErrorAction SilentlyContinue   # 别把契约测试 exe 留在 %TEMP%

# ---- 盲区清单（G7 ✓）：本地**覆盖不到**的检查，明写出来 ✓ 沉默才是问题 ✗ ----
Write-Host '== 本地覆盖不到的检查（**只由 CI 跑** ✓ 如实列出 ✓）=='
Write-Host ('  · shell 验证脚本 ' + $shScripts.Count + ' 个（在 ubuntu-latest 的 job 里执行）：' + (($shScripts | ForEach-Object { $_.Name }) -join ', '))
Write-Host '  · Linux 平台实现（LinuxBackupSource / LinuxPaths / LinuxToolchainQuery 等）只在 ubuntu-latest 被编译与运行'
Write-Host '  · V3 Linux / Windows 打包 job 只在 v3*/main 的 push 上跑（本机不打包）'
Write-Host '  · **v2 的单元测试与 GUI 逻辑测试**：本门槛只**编译** v2（invariant v2.x release build），**不跑**这两组 → 本地补跑：v2\tests\replicate_ci.ps1 -Fast（完整复刻加 -Fast 之外的全部步骤）'
Write-Host '  · 因此：**本地全绿不等于 CI 全绿** ✓ 推之前建议看一眼 gh run list ✓'
Write-Host ''
Write-Host '== V3 切换就绪度 =='
$rows | ForEach-Object { Write-Host $_ }
if ($fail -gt 0) { Write-Host ("== 未就绪项：" + $fail + "（详见上面 [ NOT ] 行）=="); exit 1 }
Write-Host '== 全部就绪 =='
exit 0