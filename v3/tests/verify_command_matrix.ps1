# ============================================================================
#  verify_command_matrix.ps1 —— 「命令 × 隔离数据根」冒烟矩阵
#  ---------------------------------------------------------------------------
#  为什么需要它（批次 E4c，来源：外部审查建议 §2.2）：
#    这个项目的门槛跑的是**编译器**与**契约测试**，而外部审查抓到的那条致命缺陷
#    （`backup-export` 因 `CopyTree` 返回值语义而**恒失败**）一路活到 3.0.6 ——
#    原因很具体：**这条命令的成功路径从来没有被任何人跑过一次** ✗
#    → 结论：发版前，每个**对外宣传的命令**都要在**隔离数据根**里真跑一次，
#      并且同时断言①**标记行**（GUI 与脚本都靠它判断）②**退出码**（`&&`/`set -e` 靠它）。
#
#  反假绿设计（写清楚，因为这个文件自己也曾是"绿但不证明任何事"的样子）：
#    · 每个用例跑在**全新的隔离 DSH_HOME** 里，并显式把 ws 指到隔离目录 ——
#      否则工作区自动探测会把仓库当工作区一起备份（真机踩过 ✓）
#    · 输出**只保留标记行**（把 PS 5.1 的 NativeCommandError 噪声剔掉 ✓）
#    · 用例**必须有终态标记**（OK 或明确 FAIL）✓ —— 静默/崩溃会判红 ✓
#    · 失败语义的退出码一并断言 ✓（拒绝时若仍是 0，脚本无法判断 ✗ —— D2 修复项 ✓）
#    · `backup-export` 是**已知缺陷的守卫位**（A1/D1，2026-10-07 恒失败）：
#      · 现在代码里它已修好 → 断言**期望成功**（BKEXPORT_OK + rc=0）✓
#      · 若在某个提交上它又失败 → 默认把它记成 [XFAIL] 并在汇总单列 ✓
#        **绝不为了让矩阵变绿去改产品代码** ✗✓
#      · 交付/CI 用 `-AllowKnownDefect:$false`（export 失败 = 硬红 ✓）
#
#  用法：
#    powershell -NoProfile -ExecutionPolicy Bypass -File v3\tests\verify_command_matrix.ps1 -Repo .
#    powershell ... -Cli <dsh-minato.exe 路径>          # 不构建时直接指定二进制（CI 里这样用）
#    powershell ... -AllowKnownDefect:$false            # 修好之后：把 export 当硬失败（CI 用它 ✓）
#  退出码：0 = 矩阵全绿；1 = 有用例红；2 = 找不到 CLI
# ============================================================================
[CmdletBinding()]
param(
    [string]$Repo = ".",
    [string]$Cli = "",
    [switch]$AllowKnownDefect = $false,
    [switch]$Keep
)

$ErrorActionPreference = "Stop"
# 与 verify_restore_apply / verify_switchover 同一手法：只在调外部命令期间放宽为 Continue，
# 否则 V3 exe 往 stderr 打的一行正常诊断（INTEGRITY_SKIPPED）会被 PS 5.1 变成终止错误 ✗
function Invoke-External([scriptblock]$sb) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $sb } finally { $ErrorActionPreference = $old }
}

$script:pass = 0
$script:fail = 0
$script:xfail = New-Object System.Collections.Generic.List[string]
$script:lastRc = $null

function Check([string]$name, [bool]$ok) {
    if ($ok) { $script:pass++; Write-Host ("  [PASS] " + $name) }
    else { $script:fail++; Write-Host ("  [FAIL] " + $name) }
}
function Section([string]$t) { Write-Host ""; Write-Host ("== " + $t) }

# 只留标记行：把原生命令的 PS 包装噪声与正常诊断剔掉 ✓（顺序保持 ✓）
function Clean([string]$s) {
    $out = @()
    if ($null -eq $s) { return "" }
    foreach ($l in ($s -split "`r?`n")) {
        if ($l -match '^\s*$') { continue }
        if ($l -match 'INTEGRITY_SKIPPED') { continue }
        if ($l -match 'CategoryInfo|FullyQualifiedErrorId|NativeCommandError') { continue }
        if ($l -match '^\s*\+') { continue }
        if ($l -match '^At ') { continue }
        if ($l -match '^所在位置') { continue }
        $out += $l.TrimEnd()
    }
    return ($out -join "`n")
}

# ★ 唯一的调用入口：rc 与文本**一定**成对返回 ✓（早期版本在调用点手写 $LASTEXITCODE，
#   处处漏赋值 → 断言形同虚设 ✗✓ 现在收成一个函数，漏不掉 ✓）
function Run-Cli([string]$exe, [string[]]$cmdArgs) {
    $raw = (Invoke-External { & $exe @cmdArgs }) | Out-String
    return @{ rc = $LASTEXITCODE; raw = $raw; out = (Clean $raw) }
}
function MarkLines([string]$clean, [string]$prefix) {
    return @(($clean -split "`n") | Where-Object { $_ -match ('^' + $prefix) })
}
function HasMarker([string]$clean, [string]$regex) {
    return (@(($clean -split "`n") | Where-Object { $_ -match $regex }).Count -gt 0)
}
# 从"最后一个 OK 标记行"取出它后面的路径 ✓（同时兼容 OK / FAIL 两种终态 ✓）
function LastMarkerPath([string]$clean, [string]$okPrefix, [string]$failPrefix) {
    $ok = @(MarkLines $clean $okPrefix)
    if ($ok.Count -gt 0) { return $ok[$ok.Count - 1].Substring($okPrefix.Length).Trim() }
    $bad = @(MarkLines $clean $failPrefix)
    if ($bad.Count -gt 0) { return $bad[$bad.Count - 1].Substring($failPrefix.Length).Trim() }
    return $null
}

# 造一个全新隔离夹具：数据根 + 备份根 + 工作区（三处都在隔离目录内 ✓）
function New-Fixture([string]$tag) {
    $iso = Join-Path $env:TEMP ("dsht_matrix_" + $tag + "_" + [guid]::NewGuid().ToString("N").Substring(0, 8))
    $dataRoot = Join-Path $iso "dsh"
    $bk = Join-Path $iso "bk"
    $ws = Join-Path $iso "ws"
    New-Item -ItemType Directory -Path (Join-Path $dataRoot "storages"), $bk, $ws -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $dataRoot "storages/a.txt"), "ALPHA`n")
    [System.IO.File]::WriteAllText((Join-Path $ws "ws-note.txt"), "WS`n")
    return @{ iso = $iso; home = $dataRoot; bk = $bk; ws = $ws }
}

# ---- 定位 CLI：优先显式 -Cli，否则用 Release 构建产物（**不自动构建**：与并行门槛抢 bin 会互相锁 ✗）----
$repoFull = (Resolve-Path -LiteralPath $Repo).Path
if ([string]::IsNullOrWhiteSpace($Cli)) {
    $cand = Join-Path $repoFull "v3\src\Dsht.Cli\bin\Release\net8.0\dsh-minato.exe"
    if (-not (Test-Path -LiteralPath $cand)) {
        $cand2 = Join-Path $repoFull "v3\src\Dsht.Cli\bin\Release\net8.0\dsh-minato"   # Linux
        if (Test-Path -LiteralPath $cand2) { $cand = $cand2 }
    }
    $Cli = $cand
}
if (-not (Test-Path -LiteralPath $Cli)) {
    Write-Host ("找不到 CLI：" + $Cli)
    Write-Host "先构建：dotnet build v3/src/Dsht.Cli/Dsht.Cli.csproj -c Release"
    exit 2
}
$Cli = (Resolve-Path -LiteralPath $Cli).Path

# 真实数据根快照：矩阵**只许**在隔离根里写 ✓ → 跑完证明真实数据根没被动过 ✓（与 gate5 同一手法 ✓）
function Get-RealRoots {
    $c = @()
    if ($env:USERPROFILE) { $c += (Join-Path $env:USERPROFILE ".dsh") }
    if ($env:APPDATA) { $c += (Join-Path $env:APPDATA ".dsh") }
    if ($env:LOCALAPPDATA) { $c += (Join-Path $env:LOCALAPPDATA ".dsh") }
    return $c
}
function Get-RootSnapshot([string]$root) {
    if (-not (Test-Path -LiteralPath $root)) { return "(absent)" }
    return ((Get-ChildItem -LiteralPath $root -Force -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object {
                if ($_.PSIsContainer) { "D:" + $_.Name } else { "F:" + $_.Name + ":" + $_.Length } }) -join "|")
}

$realRoots = Get-RealRoots
$before = @{}
foreach ($r in $realRoots) { $before[$r] = (Get-RootSnapshot $r) }
$oldHome = $env:DSH_HOME
$script:isoList = New-Object System.Collections.Generic.List[string]

Write-Host "== 命令 × 隔离数据根 冒烟矩阵 =="
Write-Host ("  CLI : " + $Cli)
$ver = Run-Cli $Cli @("version")
Write-Host ("  版本: " + ($ver.out -replace "`n", " "))

try {
    # ------------------------------------------------------------------
    # 0) 矩阵自己的前置：**复位 CLI 的备份根选择** ✓✓
    #   为什么必须做：备份根是**粘性**的（`backup-dir --set` 把选择写进
    #     `<StateDir>/.backup-dir`，Windows 上 StateDir 就是 exe 所在目录）✗
    #   → 上一个用户 / 上一次矩阵留下的选择会让"隔离根"里冒出别人的备份包 ✗
    #     （真机实测：空 DSH_HOME 下 `backup-list` 报 `BACKUP_LIST_OK 1` ✗）
    #   → 复位后本矩阵只看得到**自己刚造的那些包** ✓ 用户照 VERIFY.md 跑也不会撞旧状态 ✓
    #   安全：这条命令只删 `<StateDir>/.backup-dir` 这一个文件 ✓ 不动数据根、不删备份包 ✓
    # ------------------------------------------------------------------
    Section "0. 前置：复位备份根选择（只影响 .backup-dir 这一个文件）"
    $f0 = New-Fixture "pre"; $script:isoList.Add($f0.iso)
    $env:DSH_HOME = $f0.home
    $rst = Run-Cli $Cli @("backup-dir", "--reset")
    Write-Host ("  -- backup-dir --reset   rc=" + $rst.rc)
    foreach ($l in (MarkLines $rst.out 'BACKUP_DIR' | Select-Object -First 2)) { Write-Host ("       " + $l) }
    Check "backup-dir --reset 成功（矩阵可重复、不受旧状态影响 ✓）" (HasMarker $rst.out '^BACKUP_DIR_(RESET|OK)')
    $bl0 = Run-Cli $Cli @("backup-list")
    Check "复位后隔离根里看不到任何旧备份（BACKUP_LIST_OK 0 ✓）" (HasMarker $bl0.out '^BACKUP_LIST_OK 0$')
    if (-not $Keep) { Remove-Item -LiteralPath $f0.iso -Recurse -Force -ErrorAction SilentlyContinue }

    # ------------------------------------------------------------------
    # 1) 只读命令面：每条都在隔离数据根下给出标记行 + 退出码
    # ------------------------------------------------------------------
    Section "1. 只读命令面（隔离数据根）"
    $f1 = New-Fixture "ro"; $script:isoList.Add($f1.iso)
    $env:DSH_HOME = $f1.home
    $cset = Run-Cli $Cli @("config-set", "ws", $f1.ws)     # 把工作区指进隔离目录 ✓
    Check "前置：config-set ws 成功（避免把仓库当工作区备份 ✗）" (HasMarker $cset.out '^CONFIGSET_OK')

    $roCases = @(
        @("version", @("version"), '^DSHT_VERSION [0-9]', '', 0),
        @("about", @("about"), '非官方|not affiliated|unofficial', '', 0),
        @("status", @("status"), '^STATUS_[A-Z]+', '', 0),
        @("sessions", @("sessions"), '^SESSIONS_(OK|FAIL)', '', 0),
        @("doctor", @("doctor"), '^DOCTOR_[A-Z]+', '', 0),
        @("config-get", @("config-get"), '^CONFIGGET_OK', '', 0),
        @("backup-list（空根）", @("backup-list"), '^BACKUP_LIST_OK [0-9]+', '', 0)
    )
    foreach ($c in $roCases) {
        $r = Run-Cli $Cli $c[1]
        Write-Host ("  -- " + $c[0] + "   rc=" + $r.rc)
        foreach ($l in (MarkLines $r.out '[A-Z]' | Select-Object -First 3)) { Write-Host ("       " + $l) }
        Check ($c[0] + " 打印标记行 /" + $c[2] + "/") (HasMarker $r.out $c[2])
        if ($c[3] -ne '') { Check ($c[0] + " 不该出现 /" + $c[3] + "/") (-not (HasMarker $r.out $c[3])) }
        if ($null -ne $c[4]) { Check ($c[0] + " 退出码 = " + $c[4]) ($r.rc -eq $c[4]) }
    }

    # ------------------------------------------------------------------
    # 2) 写路径：backup / backup-list --verify / backup-export / restore --dry-run
    # ------------------------------------------------------------------
    Section "2. 写路径（隔离数据根 + 隔离备份根）"
    $f2 = New-Fixture "wr"; $script:isoList.Add($f2.iso)
    $env:DSH_HOME = $f2.home
    $cset2 = Run-Cli $Cli @("config-set", "ws", $f2.ws)

    $b = Run-Cli $Cli @("backup", "--to", $f2.bk)
    Write-Host ("  -- backup --to <隔离备份根>   rc=" + $b.rc)
    foreach ($l in (MarkLines $b.out 'BACKUP_' | Select-Object -First 3)) { Write-Host ("       " + $l) }
    Check "backup 打印 BACKUP_OK" (HasMarker $b.out '^BACKUP_OK ')
    Check "backup 退出码 = 0" ($b.rc -eq 0)
    $pkg = LastMarkerPath $b.out 'BACKUP_OK ' 'BACKUP_FAIL '
    Check "备份目录落在 --to 指定的目录内" ($pkg -ne $null -and $pkg.StartsWith($f2.bk, [StringComparison]::OrdinalIgnoreCase))
    Check "完成标记写在包同级（<包>.manifest）" ($pkg -ne $null -and (Test-Path -LiteralPath ($pkg + ".manifest")))
    $marker = ""
    if ($pkg -and (Test-Path -LiteralPath ($pkg + ".manifest"))) { $marker = [System.IO.File]::ReadAllText($pkg + ".manifest") }
    Check "标记含 files= / failed= / sha256=" (($marker -match 'files=\d+') -and ($marker -match 'failed=\d+') -and ($marker -match 'sha256=[0-9a-f]{16,}'))

    $v = Run-Cli $Cli @("backup-list", "--verify")
    Write-Host ("  -- backup-list --verify   rc=" + $v.rc)
    foreach ($l in (MarkLines $v.out 'BACKUP_VERIFY' | Select-Object -First 3)) { Write-Host ("       " + $l) }
    Check "backup-list --verify 打印 BACKUP_VERIFY_TOTAL" (HasMarker $v.out '^BACKUP_VERIFY_TOTAL ')
    Check "backup-list --verify 对新鲜包报 complete" (HasMarker $v.out '^BACKUP_VERIFY .* complete ')
    Check "backup-list --verify 无 mismatch" (-not (HasMarker $v.out '^BACKUP_VERIFY .* mismatch'))
    Check "backup-list --verify 退出码 = 0" ($v.rc -eq 0)

    # ---- backup-export（成功路径 ✓ —— 这正是 D1 活到今天的直接原因 ✗）----
    $expDir = Join-Path $f2.iso "exp"
    $e = Run-Cli $Cli @("backup-export", "--path", $pkg, "--to", $expDir, "--yes")
    Write-Host ("  -- backup-export --path <包> --to <隔离目标> --yes   rc=" + $e.rc)
    foreach ($l in (MarkLines $e.out 'BKEXPORT_' | Select-Object -First 2)) { Write-Host ("       " + $l) }
    $eOk = HasMarker $e.out '^BKEXPORT_OK '
    $eFail = HasMarker $e.out '^BKEXPORT_FAIL '
    if ($eOk) {
        Check "backup-export 成功路径打印 BKEXPORT_OK" $true
        Check "backup-export 退出码 = 0" ($e.rc -eq 0)
        $expPkg = LastMarkerPath $e.out 'BKEXPORT_OK ' 'BKEXPORT_FAIL '
        Check "导出副本带完成标记（<副本>.manifest —— 否则异地无法核对 ✗）" ($expPkg -ne $null -and (Test-Path -LiteralPath ($expPkg + ".manifest")))
    } elseif ($eFail -and $AllowKnownDefect) {
        # ★ 已知缺陷（A1/D1）：不掩盖、也不拖垮矩阵 —— 记 XFAIL 并单列 ✓
        $script:xfail.Add("backup-export 成功路径（已知缺陷 A1/D1：BKEXPORT_OK 未出现，该命令当前不可用；rc=" + $e.rc + "）")
        Write-Host ("  [XFAIL] backup-export 成功路径：BKEXPORT_OK 未出现（已知缺陷 A1/D1，另批修复中）")
        Check "backup-export 至少给出了明确 FAIL（不许静默 ✗）" $true
    } else {
        Check "backup-export 打印 BKEXPORT_OK（成功路径）" $eOk
        Check "backup-export 退出码 = 0（成功路径）" ($e.rc -eq 0)
        Check "backup-export 失败时必须打印 BKEXPORT_FAIL 而非静默" ($eOk -or $eFail)
    }

    # ---- restore --dry-run：只读预览，**绝不写盘** ----
    $d = Run-Cli $Cli @("restore", "--dry-run")
    Write-Host ("  -- restore --dry-run   rc=" + $d.rc)
    foreach ($l in (MarkLines $d.out 'DRYRUN_|RESTORE_' | Select-Object -First 4)) { Write-Host ("       " + $l) }
    Check "restore --dry-run 给出明确结论（DRYRUN_OK / DRYRUN_FAIL / 明确拒绝）" ((HasMarker $d.out '^DRYRUN_(OK|FAIL)') -or (HasMarker $d.out '^RESTORE_FAIL '))
    Check "restore --dry-run 未报告真实写盘（无 RESTORE_OK / APPLY_ROOT / APPLY_ACK）" (-not (HasMarker $d.out '^RESTORE_(OK|APPLY_ROOT|APPLY_ACK)'))
    Check "restore --dry-run 退出码 = 0（只读命令）" ($d.rc -eq 0)
    Check "restore --dry-run 带出来源包路径（DRYRUN_SRC）" (HasMarker $d.out '^DRYRUN_SRC ')

    # ------------------------------------------------------------------
    # 3) 承诺的行为：`..` 逃逸 · wipe 只打印 · uninstall 不删数据
    # ------------------------------------------------------------------
    Section "3. 承诺的行为（越界 / wipe / uninstall）"
    $f3 = New-Fixture "bh"; $script:isoList.Add($f3.iso)
    $env:DSH_HOME = $f3.home
    $cset3 = Run-Cli $Cli @("config-set", "ws", $f3.ws)

    # 3a) `..` 逃逸：目标不在备份根内 → 必须拒绝，且**不许**建出目录 ✓
    $escape = Join-Path $f3.bk "..\..\dsh"
    $x = Run-Cli $Cli @("backup-export", "--path", $escape, "--to", (Join-Path $f3.iso "esc"), "--yes")
    Write-Host ("  -- backup-export --path <备份根>\..\..\dsh   rc=" + $x.rc)
    foreach ($l in (MarkLines $x.out 'BKEXPORT_' | Select-Object -First 2)) { Write-Host ("       " + $l) }
    Check "`..` 逃逸被拒（BKEXPORT_FAIL outside）" (HasMarker $x.out '^BKEXPORT_FAIL ')
    Check "逃逸路径退出码非 0" ($x.rc -ne 0)
    Check "逃逸失败后没有建出导出目录" (-not (Test-Path -LiteralPath (Join-Path $f3.iso "esc")))

    # 3b) wipe：只打印路径、**不删**（about 里的承诺 ✓ 这里做行为断言 ✓）
    $w = Run-Cli $Cli @("wipe")
    Write-Host ("  -- wipe   rc=" + $w.rc)
    foreach ($l in (MarkLines $w.out 'WIPE_' | Select-Object -First 3)) { Write-Host ("       " + $l) }
    Check "wipe 打印 WIPE_PLAN / WIPE_MANUAL 路径" ((HasMarker $w.out '^WIPE_PLAN ') -or (HasMarker $w.out '^WIPE_MANUAL '))
    Check "wipe 之后数据**原样还在**（不删 ✓）" (Test-Path -LiteralPath (Join-Path $f3.home "storages/a.txt"))

    # 3c) uninstall 计划：不带 --yes 绝不执行；且**不碰**数据根 ✓（"卸载不删数据"承诺 ✓）
    $u = Run-Cli $Cli @("uninstall")
    Write-Host ("  -- uninstall（不带 --yes）   rc=" + $u.rc)
    foreach ($l in (MarkLines $u.out 'UNINSTALL_' | Select-Object -First 3)) { Write-Host ("       " + $l) }
    Check "uninstall 先打印计划（UNINSTALL_PLAN / DRYRUN）" ((HasMarker $u.out '^UNINSTALL_PLAN ') -or (HasMarker $u.out '^UNINSTALL_DRYRUN '))
    Check "uninstall 未确认时没有执行（无 UNINSTALL_OK）" (-not (HasMarker $u.out '^UNINSTALL_OK'))
    Check "uninstall 之后数据根**原样还在**（不删数据 ✓）" (Test-Path -LiteralPath (Join-Path $f3.home "storages/a.txt"))

    # ------------------------------------------------------------------
    # 4) 越界证明：真实数据根未被触碰（矩阵自己也要自证 ✓）
    # ------------------------------------------------------------------
    Section "4. 越界证明"
    foreach ($r in $realRoots) {
        $after = Get-RootSnapshot $r
        Check ("真实数据根未变: " + $r) ($after -eq $before[$r])
    }
} finally {
    if ($null -ne $oldHome) { $env:DSH_HOME = $oldHome } else { Remove-Item Env:\DSH_HOME -ErrorAction SilentlyContinue }
    if (-not $Keep) { foreach ($p in $script:isoList) { Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue } }
    else { foreach ($p in $script:isoList) { Write-Host ("隔离目录保留: " + $p) } }
}

Write-Host ""
if ($script:xfail.Count -gt 0) {
    Write-Host "== 已知缺陷（XFAIL，另批修复中，不计入失败 ✓）=="
    foreach ($x in $script:xfail) { Write-Host ("  [XFAIL] " + $x) }
}
$total = $script:pass + $script:fail
Write-Host ("== 矩阵：" + $script:pass + "/" + $total + " passed, " + $script:fail + " failed（XFAIL " + $script:xfail.Count + "）==")
# 项数下限：删掉用例（或整段被截断）时 PASS 变少而 FAIL 仍为 0 ✗ → 这里必须红 ✓
$EXPECTED_MIN = 16
if ($script:pass -lt $EXPECTED_MIN) {
    Write-Host ("  [FAIL] 项数不足：实跑 " + $script:pass + " < 声明 " + $EXPECTED_MIN)
    $script:fail++
}
if ($script:fail -ne 0) { exit 1 }
exit 0
