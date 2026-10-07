# verify_restore_apply.ps1 — 端到端验证 V3 的真实 restore（--apply 隔离写盘）
#
# 为什么需要它：compare_markers.ps1 只能比对 v2.x 也有的命令面，而 `--apply` 是 V3 独有的开关，
# 且它的**真实写盘**必须在隔离数据根下验证。本脚本就是那条"能真跑起来"的路径：
#   $DSH_HOME 指向临时数据根 + exe 也放在临时目录（状态目录=exe 目录）→ 备份与恢复全部落在临时目录内。
#
# 安全设计（三条硬约束，缺一不可）：
#   ① 所有写操作都发生在 $iso 之下；脚本结束前比对**真实默认数据根**的快照，证明零写入；
#   ② 绝不把 $DSH_HOME 指向真实默认数据根去测 "apply-not-isolated"——那条分支由纯领域契约测试覆盖
#      （把"应当拒绝"的用例指向真实数据根，一旦判定有 bug 就会真写用户数据，这正是以前踩过的坑）；
#   ③ 每个用例结束后都复核 $iso 之外无残留（%TEMP%\backup 不存在）。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File v3\tests\verify_restore_apply.ps1 -Repo .
#   powershell -ExecutionPolicy Bypass -File v3\tests\verify_restore_apply.ps1 -Repo . -Keep   # 保留隔离目录供检查
[CmdletBinding()]
param(
    [string]$Repo = ".",
    [switch]$Keep
)

$ErrorActionPreference = "Stop"
# ★★★ **假绿修复（实测发现 —— 与 `compare_markers` / `verify_switchover` 同源）** ✓✓
#   ✗ `Stop` + 下面 `Run()` 里的 `& $exe @cmdArgs 2>&1` ✗ —— V3 exe 会往 stderr 打**诊断**
#     （`INTEGRITY_SKIPPED`：本地源码构建的 exe 旁没有 hashes.txt ✓ 完全正常 ✓）
#     → PS 5.1 把它变成 **NativeCommandError** → **Stop 终止** ✗✗
#     → **脚本在第 55 行就死掉** ✗ → **从不打印 `== N/N passed`** ✗
#     → 调用它的 `verify_switchover` 的 gate5 **只能报"未解析到结果行"** ✓✓
#   ✓ 现在：**只在调外部命令时放宽为 Continue** ✓✓ 并**不再用 `2>&1` 把诊断混进输出** ✓
function Invoke-External([scriptblock]$sb) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $sb } finally { $ErrorActionPreference = $old }
}
$script:pass = 0
$script:fail = 0

function Check([string]$name, [bool]$ok) {
    if ($ok) { $script:pass++; Write-Host ("  [PASS] " + $name) }
    else { $script:fail++; Write-Host ("  [FAIL] " + $name) -ForegroundColor Red }
}
function Section([string]$t) { Write-Host ""; Write-Host ("== " + $t) }

function Get-RealDataRoots {
    $c = @()
    if ($env:USERPROFILE) { $c += (Join-Path $env:USERPROFILE ".dsh") }
    if ($env:APPDATA) { $c += (Join-Path $env:APPDATA ".dsh") }
    if ($env:LOCALAPPDATA) { $c += (Join-Path $env:LOCALAPPDATA ".dsh") }
    return $c
}

# 真实数据根快照：只读、只看顶层（名字 + 文件长度）。用于证明"零写入"。
function Get-RootSnapshot([string]$root) {
    if (-not (Test-Path -LiteralPath $root)) { return "(absent)" }
    $items = Get-ChildItem -LiteralPath $root -Force -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object {
        if ($_.PSIsContainer) { "D:" + $_.Name } else { "F:" + $_.Name + ":" + $_.Length }
    }
    return ($items -join "|")
}

function Read-Text([string]$p) {
    if (-not (Test-Path -LiteralPath $p)) { return $null }
    return [System.IO.File]::ReadAllText($p)
}

function Run([string]$exe, [string[]]$cmdArgs) {
    $all = (Invoke-External { & $exe @cmdArgs }) | Out-String
    return $all
}

$repoFull = (Resolve-Path -LiteralPath $Repo).Path
$v3src = Join-Path $repoFull "v3\src"
if (-not (Test-Path -LiteralPath $v3src)) { Write-Host ("找不到 V3 源码: " + $v3src); exit 2 }

$iso = Join-Path $env:TEMP ("dsht_restore_apply_" + [guid]::NewGuid().ToString("N"))
$exeDir = Join-Path $iso "exe"
$data = Join-Path $iso "data"
$exe = Join-Path $exeDir "dsht_v3.exe"
$tempBackup = Join-Path $env:TEMP "backup"

$realRoots = Get-RealDataRoots
$before = @{}
foreach ($r in $realRoots) { $before[$r] = (Get-RootSnapshot $r) }

$oldDshHome = $env:DSH_HOME

try {
    New-Item -ItemType Directory -Path $exeDir -Force | Out-Null
    New-Item -ItemType Directory -Path $data -Force | Out-Null

    Section "0. 构建 V3 exe（csc，不需要 .NET SDK）"
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path -LiteralPath $csc)) { Write-Host ("找不到 csc: " + $csc); exit 2 }
    if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }   # 绝不留下旧 exe 造成"假通过"
    $src = @(Get-ChildItem -LiteralPath $v3src -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ForEach-Object { $_.FullName })
    $build = (Invoke-External { & $csc /nologo /target:exe /warn:4 "/out:$exe" $src }) | Out-String
    Check ("构建成功（" + $src.Count + " 个源文件）") (Test-Path -LiteralPath $exe)
    if (-not (Test-Path -LiteralPath $exe)) { Write-Host $build; exit 2 }

    # 隔离数据根：造出"可辨识"的初始内容
    [System.IO.File]::WriteAllText((Join-Path $data "settings.yaml"), "v1")
    New-Item -ItemType Directory -Path (Join-Path $data "sessions") -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $data "sessions\s1.txt"), "S1")
    $env:DSH_HOME = $data

    Section "1. 隔离根下真实备份（备份应落在 exe 目录旁，而不是真实数据根）"
    # ★★★ **假绿修复的连锁（实测发现）** ✓✓
    #   ✗ 这里原来是裸 `backup` ✗ —— 而产品的契约是**第一次备份必须显式给目录**
    #     （`BACKUP_NEEDS_DIR` + "请加 --to <目录>" ✓ 由 D4 修复引入 ✓）
    #     → 隔离根下没有 `backup/` → 产品**拒绝** ✓ → 后面 11 项**全部连锁失败** ✗✗
    #     → 而脚本第 55 行早就死了 ✓ **这个红一直没人看见** ✓✓
    #   ✓ 现在：**按产品契约传 `--to`** ✓✓（意图不变：备份必须落在隔离目录内 ✓）
    $bkTo = Join-Path $iso 'backups'
    New-Item -ItemType Directory -Path $bkTo -Force | Out-Null
    $out = Run $exe @("backup", "--to", $bkTo)
    Check "backup 打印 BACKUP_OK" ($out -match "BACKUP_OK")
    $bkLine = ($out -split "`r?`n" | Where-Object { $_ -match "^BACKUP_OK " } | Select-Object -First 1)
    $bkDir = $null
    if ($bkLine) { $bkDir = $bkLine.Substring("BACKUP_OK ".Length).Trim() }
    Check "备份目录位于隔离目录内" ($bkDir -ne $null -and $bkDir.StartsWith($iso, [StringComparison]::OrdinalIgnoreCase))
    # ★★★ **门槛完整性审计 M5a2 —— `--to` 本身原来没被断言** ✓✓
    #   ✗ 只断言"在 `$iso` 内" ✗ → 变异证明：**完全忽略 `--to`、改用默认根**（也在 `$iso` 内 ✓）照样 25/25 ✗✗
    #   ✓ 现在：**必须落在 `--to` 指定的那个目录下** ✓✓
    Check "备份目录落在 --to 指定的目录下" ($bkDir -ne $null -and $bkDir.StartsWith($bkTo, [StringComparison]::OrdinalIgnoreCase))
    Check "备份内容含 settings.yaml" ($bkDir -ne $null -and (Test-Path -LiteralPath (Join-Path $bkDir "settings.yaml")))
    Check "备份内容含 sessions\s1.txt" ($bkDir -ne $null -and (Test-Path -LiteralPath (Join-Path $bkDir "sessions\s1.txt")))

    Section "2. 篡改数据根后 restore --apply（真实写盘 + 合并语义 + 恢复前自动备份）"
    [System.IO.File]::WriteAllText((Join-Path $data "settings.yaml"), "v2-BROKEN")
    Remove-Item -LiteralPath (Join-Path $data "sessions\s1.txt") -Force
    [System.IO.File]::WriteAllText((Join-Path $data "only-local.txt"), "LOCAL")   # 目标端独有文件：恢复不应删除

    $out = Run $exe @("restore", "--apply")
    Check "打印 RESTORE_APPLY_ACK（跳过运行中闸门的事实已留证）" ($out -match "RESTORE_APPLY_ACK")
    Check "打印 RESTORE_APPLY_ROOT 且指向隔离数据根" ($out -match "RESTORE_APPLY_ROOT" -and $out.Contains($data))
    Check "打印 RESTORE_PRE_BACKUP（回滚锚点）" ($out -match "RESTORE_PRE_BACKUP")
    Check "打印 RESTORE_OK" ($out -match "RESTORE_OK")
    Check "被篡改的 settings.yaml 已恢复为 v1" ((Read-Text (Join-Path $data "settings.yaml")) -eq "v1")
    Check "被删除的 sessions\s1.txt 已恢复" ((Read-Text (Join-Path $data "sessions\s1.txt")) -eq "S1")
    Check "目标端独有文件未被删除（合并语义）" ((Read-Text (Join-Path $data "only-local.txt")) -eq "LOCAL")

    $preLine = ($out -split "`r?`n" | Where-Object { $_ -match "^RESTORE_PRE_BACKUP " } | Select-Object -First 1)
    $preDir = $null
    if ($preLine) { $preDir = $preLine.Substring("RESTORE_PRE_BACKUP ".Length).Trim() }
    Check "恢复前自动备份存在且位于隔离目录内" ($preDir -ne $null -and $preDir.StartsWith($iso, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $preDir))
    Check "恢复前自动备份里保存的是被篡改前的内容（v2-BROKEN）" ((Read-Text (Join-Path $preDir "settings.yaml")) -eq "v2-BROKEN")
    Check "恢复前自动备份带 -pre-restore 后缀" ($preDir -ne $null -and $preDir.EndsWith("-pre-restore"))

    Section "3. 不给 --apply 时不越界：要么被运行中闸门拒绝（零写入），要么真实恢复到隔离根"
    # 确定性：前面几步的 --apply 恢复各产生一个 -pre-restore 包 → "最新备份"已变 ✗
    # 不清掉的话，这步恢复的是某个 pre-restore 包，内容当然不是 v1 → 与产品无关的假失败 ✓
    # 备份根 = 那个已知好包的父目录 ✓（不是 $bkRoot —— 那个变量根本不存在 ✗，被 SilentlyContinue 吞掉了 ✗）
    # 只留下 $bkDir 这一个包 → "最新备份"确定 ✓ → 默认路径恢复出来的必然是 v1 ✓✓
    if ($bkDir) {
        $bkRootReal = Split-Path $bkDir -Parent
        Get-ChildItem $bkRootReal -Directory -ErrorAction SilentlyContinue | Where-Object { $_.FullName -ne $bkDir } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        Check '（前置）备份根里只剩已知好包，默认路径的"最新"因此确定' (@(Get-ChildItem $bkRootReal -Directory -ErrorAction SilentlyContinue).Count -eq 1)
    }
    [System.IO.File]::WriteAllText((Join-Path $data "settings.yaml"), "v3-DIRTY")
    $out = Run $exe @("restore")
    $refused = ($out -match "RESTORE_FAIL")
    $now = Read-Text (Join-Path $data "settings.yaml")
    if ($refused) {
        $why = ($out -split "`r?`n" | Where-Object { $_ -match "RESTORE_FAIL" } | Select-Object -First 1)
        if ($why) { $why = $why.Trim() }
        Check ("被拒绝（" + $why + "）且零写入") ($now -eq "v3-DIRTY")
    } else {
        # 服务确实没在跑：这是 v2.x 的默认路径，但数据根仍是隔离的，所以安全
        if ($now -ne "v1") { Write-Host ("      [诊断] 实际内容 = " + $now) }
        Check "服务未运行时默认路径真的恢复了（且只写隔离根）" ($now -eq "v1")
    }

    Section "3.5 备份链回归（2026-10-07 审查 A1/A2/A3：导出复活 · 退出码 · 截断闸门）"
    # 前置都在第 1/3 节就位：$bkDir = 已知好包 ✓ 根已 pin 到 $bkTo ✓ 备份根里包确定 ✓
    # ★ 每条都对应一个"修复前实测是坏"的行为 ✓（红/绿对照记录在 docs\修复记录-2026-10-07-备份链.md ✓）
    $pkgName = Split-Path $bkDir -Leaf

    # --- A2-a：restore 根外路径 → RESTORE_FAIL 且退出码非 0（修复前 rc=0 ✗） ---
    $out = Run $exe @("restore", "--path", "..\evil", "--yes"); $rc = $LASTEXITCODE
    Check "A2 restore 根外：RESTORE_FAIL" ($out -match "RESTORE_FAIL")
    Check "A2 restore 根外：退出码非 0（修复前为 0 ✗）" ($rc -ne 0)

    # --- A2-b：export 校验失败（--path 逃逸）→ BKEXPORT_FAIL 且退出码非 0（修复前 rc=0 ✗） ---
    $out = Run $exe @("backup-export", "--path", "..\evil", "--to", (Join-Path $iso 'exp1'), "--yes"); $rc = $LASTEXITCODE
    Check "A2 export 逃逸：BKEXPORT_FAIL" ($out -match "BKEXPORT_FAIL")
    Check "A2 export 逃逸：退出码非 0（修复前为 0 ✗）" ($rc -ne 0)

    # --- A2-c：backup 失败（数据目录不存在）→ BACKUP_FAIL 且退出码非 0 ---
    $env:DSH_HOME = (Join-Path $iso 'no-such-home')
    $out = Run $exe @("backup", "--to", $bkTo); $rc = $LASTEXITCODE
    $env:DSH_HOME = $data
    Check "A2 backup 数据目录缺失：BACKUP_FAIL" ($out -match "BACKUP_FAIL")
    Check "A2 backup 数据目录缺失：退出码非 0" ($rc -ne 0)

    # --- A1：成功导出（审查最关键验收点）→ BKEXPORT_OK + rc==0 + 文件与旁挂 .manifest 都在 ---
    $expOk = Join-Path $iso 'exp-ok'
    $out = Run $exe @("backup-export", "--path", $pkgName, "--to", $expOk, "--yes"); $rc = $LASTEXITCODE
    Check "A1 export 成功：BKEXPORT_OK（修复前非空包 100% FAIL ✗✗）" ($out -match "BKEXPORT_OK")
    Check "A1 export 成功：退出码为 0" ($rc -eq 0)
    Check "A1 导出目录含 settings.yaml 与 sessions\s1.txt" ((Test-Path -LiteralPath (Join-Path $expOk "$pkgName\settings.yaml")) -and (Test-Path -LiteralPath (Join-Path $expOk "$pkgName\sessions\s1.txt")))
    Check "A1 旁挂 .manifest 存在（审查 D1 核心症状 ✓）" (Test-Path -LiteralPath (Join-Path $expOk ($pkgName + ".manifest")))

    # --- A3：截断包（完成标记只有 bytes/failed/finished）→ restore 拒绝 + --verify 结论一致 ---
    $truncName = "dsh-data-20990101-000000000-trunc"
    $truncDir = Join-Path $bkTo $truncName
    Copy-Item -LiteralPath $bkDir -Destination $truncDir -Recurse -Force
    [System.IO.File]::WriteAllText($truncDir + ".manifest", "bytes=10`nfailed=0`nfinished=2099-01-01 00:00:00")
    $beforeA3 = Read-Text (Join-Path $data "settings.yaml")
    $out = Run $exe @("restore", "--path", $truncName); $rc = $LASTEXITCODE
    Check "A3 截断包：restore 拒绝（RESTORE_FAIL）" ($out -match "RESTORE_FAIL")
    Check "A3 截断包：理由 fail-closed（标记无法解析）" ($out -match "标记无法解析")
    Check "A3 截断包：退出码非 0（修复前竟 RESTORE_OK + rc=0 ✗✗）" ($rc -ne 0)
    Check "A3 截断包：拒绝后零写入" ((Read-Text (Join-Path $data "settings.yaml")) -eq $beforeA3)
    $out = Run $exe @("backup-list", "--verify")
    Check "A3 --verify 与 restore 结论一致（unreadable）" (($out -match [regex]::Escape($truncName)) -and ($out -match "unreadable"))
    Remove-Item -LiteralPath $truncDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath ($truncDir + ".manifest") -Force -ErrorAction SilentlyContinue

    '4. --apply 但未设 $DSH_HOME → 明确拒绝且零写入'
    Remove-Item Env:\DSH_HOME -ErrorAction SilentlyContinue
    [System.IO.File]::WriteAllText((Join-Path $data "settings.yaml"), "v4-DIRTY")
    $out = Run $exe @("restore", "--apply")
    Check "打印 RESTORE_FAIL" ($out -match "RESTORE_FAIL")
    Check "原因提到 DSH_HOME（apply-needs-dsh-home）" ($out -match "DSH_HOME")
    Check "拒绝后数据未被写入" ((Read-Text (Join-Path $data "settings.yaml")) -eq "v4-DIRTY")
    Check "拒绝后没有产生恢复前备份" ($out -notmatch "RESTORE_PRE_BACKUP")

    Section "5. 越界证明：真实默认数据根与 %TEMP%\backup 未被触碰"
    foreach ($r in $realRoots) {
        $after = Get-RootSnapshot $r
        Check ("真实数据根未变: " + $r) ($after -eq $before[$r])
    }
    Check "%TEMP%\backup 不存在（无残留）" (-not (Test-Path -LiteralPath $tempBackup))
}
finally {
    if ($oldDshHome) { $env:DSH_HOME = $oldDshHome } else { Remove-Item Env:\DSH_HOME -ErrorAction SilentlyContinue }
    if (-not $Keep) { Remove-Item -LiteralPath $iso -Recurse -Force -ErrorAction SilentlyContinue }
    else { Write-Host ("隔离目录保留在: " + $iso) }
}

Write-Host ""
Write-Host ("== " + $script:pass + "/" + ($script:pass + $script:fail) + " passed, " + $script:fail + " failed ==")
if ($script:fail -ne 0) { exit 1 }
exit 0
