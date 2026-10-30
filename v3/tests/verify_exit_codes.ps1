# ============================================================================
#  verify_exit_codes.ps1 —— 退出码契约的端到端验证（P0-1）
#  ---------------------------------------------------------------------------
#  为什么需要它：
#    标记行（*_FAIL）与进程退出码曾经各说各话 —— 打了 FAIL 仍 return 0。
#    只断言内部函数不够：本脚本对每一条改过的分支**真跑一个失败场景**，
#    同时断言 ①退出码 ②标记行 ③（拒绝类）目标没有被改动。
#
#  契约（README 同步写明）：
#    0 = 成功（含计划/只读预览）
#    1 = 执行失败或拒绝（前置条件不满足）
#    2 = 用法错误
#    3 = 启动自完整性拒绝（既有，未改）
#
#  覆盖（每条 <=> 源码里一个 return 分支）：
#    config-set      未知键 -> 2 · 只读配置写不进去 -> 1
#    backup-dir      相对路径 -> 2 · 建不出目录 -> 1 · 存不下设置 -> 1 · 删不掉设置 -> 1
#    restore --dry-run  无有效备份 -> 1 · 无效包目录 -> 1 · 根外路径 -> 1 · 未完成包（自动/显式）-> 1
#    wipe            缺 --yes -> 1 · 带 --yes -> 0（两种都不删除）
#    RunQuiet        狂写 stderr 的子进程不卡死 · 不退出子进程在超时后被结束
#   ★ EXIT-02（2026-10-30 政策 A：全量裁决并补齐）—— 新增族：
#    import / backup-delete / backup-export / restore-path   用法 2 · 执行失败 1
#    verify-install  文件不存在/清单缺失/哈希不一致/下载失败 -> 1 · 一致 -> 0
#    ui              DSHT_GUI 缺失或不可执行 -> 1
#    start / stop    非法 profile -> 2 · 端口被非 dsh 占/无可停实例/安全拒绝 -> 1
#    bridge-install  非法 profile -> 2 · 没有随包插件 -> 1
#    sessions/cost   参数非法（FailKind=arg）-> 2 · 无投影/血缘不可用 -> 1
#    install/update  --version 白名单不过 -> 2（npm 失败面 not-covered，见 §12）
#    裁决表：docs/退出码裁决-2026-10-30.md（逐族理由 + 反例 + 覆盖/未覆盖）
#
#  用法：
#    powershell -NoProfile -ExecutionPolicy Bypass -File v3\tests\verify_exit_codes.ps1 -Repo .
#    powershell ... -Cli <dsh-minato.exe 路径>
#  退出码：0 = 全绿；1 = 有失败；2 = 找不到/构建不出 CLI
# ============================================================================
[CmdletBinding()]
param(
    [string]$Repo = ".",
    [string]$Cli = "",
    [switch]$Keep
)

$ErrorActionPreference = "Stop"
function Invoke-External([scriptblock]$sb) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $sb } finally { $ErrorActionPreference = $old }
}

$script:pass = 0
$script:fail = 0
$script:skip = 0
function Check([string]$name, [bool]$ok) {
    if ($ok) { $script:pass++; Write-Host ("  [PASS] " + $name) }
    else { $script:fail++; Write-Host ("  [FAIL] " + $name) }
}
function Skip([string]$name, [string]$why) {
    $script:skip++
    Write-Host ("  [SKIP] " + $name + " —— " + $why)
}
function Section([string]$t) { Write-Host ""; Write-Host ("== " + $t) }
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
function Run-Cli([string]$exe, [string[]]$cmdArgs) {
    $raw = (Invoke-External { & $exe @cmdArgs 2>&1 }) | Out-String
    return @{ rc = $LASTEXITCODE; raw = $raw; out = (Clean $raw) }
}

function HasMarker([string]$clean, [string]$regex) {
    return (@(($clean -split "`n") | Where-Object { $_ -match $regex }).Count -gt 0)
}

# ---- 定位 CLI：优先 -Cli；否则 Release 构建产物（缺了就构建一次）----
$repoFull = (Resolve-Path -LiteralPath $Repo).Path
if ([string]::IsNullOrEmpty($Cli)) {
    $Cli = Join-Path $repoFull 'v3\src\Dsht.Cli\bin\Release\net8.0\dsh-minato.exe'
    if (-not (Test-Path -LiteralPath $Cli)) {
        $dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
        if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = 'dotnet' }
        Write-Host "未找到 Release 构建，先构建 CLI …"
        & $dotnet build (Join-Path $repoFull 'v3\src\Dsht.Cli\Dsht.Cli.csproj') -c Release --nologo 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $Cli)) {
            Write-Host ("找不到 CLI：" + $Cli); exit 2
        }
    }
}
if (-not (Test-Path -LiteralPath $Cli)) { Write-Host ("找不到 CLI：" + $Cli); exit 2 }
$Cli = (Resolve-Path -LiteralPath $Cli).Path

$iso = Join-Path $env:TEMP ("dsht_exitcodes_" + [guid]::NewGuid().ToString("N").Substring(0, 8))
$cliDir = Join-Path $iso 'cli'
$homeDir = Join-Path $iso 'home'
$bkDir = Join-Path $iso 'bk'
$bkEmpty = Join-Path $iso 'bk-empty'
New-Item -ItemType Directory -Path $cliDir, $homeDir, $bkDir, $bkEmpty -Force | Out-Null
Copy-Item -Path (Join-Path (Split-Path -Parent $Cli) '*') -Destination $cliDir -Recurse -Force
$exe = Join-Path $cliDir (Split-Path -Leaf $Cli)
Remove-Item -LiteralPath (Join-Path $cliDir '.backup-dir'), (Join-Path $cliDir 'launcher.config') -Force -ErrorAction SilentlyContinue
$cfg = Join-Path $cliDir 'launcher.config'

$oldHome = $env:DSH_HOME
$oldBk = $env:DSH_MINATO_BACKUP_DIR
$env:DSH_HOME = $homeDir
$env:DSH_MINATO_BACKUP_DIR = $bkDir

try {
    Section "0. 隔离事实"
    Write-Host ("  CLI      : " + $exe)
    Write-Host ("  副本目录 : " + $cliDir)
    Write-Host ("  数据根   : " + $homeDir)
    Write-Host ("  备份根   : " + $bkDir)
    Check "状态目录里没有旧的 .backup-dir / launcher.config（用例可重复）" `
        ((-not (Test-Path -LiteralPath (Join-Path $cliDir '.backup-dir'))) -and (-not (Test-Path -LiteralPath $cfg)))

    # ------------------------------------------------------------------
    # 1) config-set
    # ------------------------------------------------------------------
    Section "1. config-set（用法错误 2 · 写入未生效 1）"

    $r = Run-Cli $exe @("config-set", "no_such_key", "x")
    Check "未知键 -> CONFIGSET_FAIL unknown-key" (HasMarker $r.out '^CONFIGSET_FAIL unknown-key')
    Check "未知键 -> 退出码 2（用法错误）" ($r.rc -eq 2)

    $wsA = Join-Path $iso 'ws-a'
    $r = Run-Cli $exe @("config-set", "ws", $wsA)
    Check "前置：合法键写入成功（CONFIGSET_OK ws）" ((HasMarker $r.out '^CONFIGSET_OK ws') -and ($r.rc -eq 0))
    Check "前置：配置文件真的出现" (Test-Path -LiteralPath $cfg)

    # 只读配置：平台的 WriteConfig 会吞掉异常，必须靠回读发现“没写进去”
    if (Test-Path -LiteralPath $cfg) {
        Set-ItemProperty -LiteralPath $cfg -Name IsReadOnly -Value $true
        try {
            $wsB = Join-Path $iso 'ws-b'
            $r = Run-Cli $exe @("config-set", "ws", $wsB)
            Check "只读配置 -> CONFIGSET_FAIL 写入未生效" (HasMarker $r.out '^CONFIGSET_FAIL ')
            Check "只读配置 -> 退出码 1（执行失败）" ($r.rc -eq 1)
            $g = Run-Cli $exe @("config-get")
            Check "只读配置 -> 回读仍是旧值（没有假成功）" (HasMarker $g.out ('^CONFIG ws ' + [regex]::Escape($wsA) + '$'))
        } finally {
            Set-ItemProperty -LiteralPath $cfg -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue
        }
    } else {
        Check "只读配置 -> CONFIGSET_FAIL（前置配置文件缺失）" $false
    }

    # ------------------------------------------------------------------
    # 2) backup-dir
    # ------------------------------------------------------------------
    Section "2. backup-dir（相对路径 2 · 建不出/存不下/删不掉 1）"

    $r = Run-Cli $exe @("backup-dir", "--set", "relative\dir")
    Check "相对路径 -> BACKUP_DIR_FAIL" (HasMarker $r.out '^BACKUP_DIR_FAIL ')
    Check "相对路径 -> 退出码 2（用法错误）" ($r.rc -eq 2)

    # 建不出目录：父路径是一个文件
    $blocker = Join-Path $iso 'not-a-dir'
    [System.IO.File]::WriteAllText($blocker, "x")
    $r = Run-Cli $exe @("backup-dir", "--set", (Join-Path $blocker 'sub'))
    Check "父路径是文件 -> BACKUP_DIR_FAIL 无法创建目录" (HasMarker $r.out '^BACKUP_DIR_FAIL .*无法创建目录')
    Check "父路径是文件 -> 退出码 1（执行失败）" ($r.rc -eq 1)

    # 存不下设置：.backup-dir 被一个目录占住（WriteAllText 必失败）
    $sel = Join-Path $cliDir '.backup-dir'
    Remove-Item -LiteralPath $sel -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $sel -Force | Out-Null
    $r = Run-Cli $exe @("backup-dir", "--set", (Join-Path $iso 'bk-new'))
    Check "设置文件是目录 -> BACKUP_DIR_FAIL 无法保存设置" (HasMarker $r.out '^BACKUP_DIR_FAIL .*无法保存设置')
    Check "设置文件是目录 -> 退出码 1" ($r.rc -eq 1)
    Remove-Item -LiteralPath $sel -Force -Recurse -ErrorAction SilentlyContinue

    # 删不掉设置：只读的 .backup-dir
    $r = Run-Cli $exe @("backup-dir", "--set", (Join-Path $iso 'bk-new2'))
    Check "前置：设置成功（BACKUP_DIR_OK）" ((HasMarker $r.out '^BACKUP_DIR_OK ') -and ($r.rc -eq 0))
    if (Test-Path -LiteralPath $sel) {
        Set-ItemProperty -LiteralPath $sel -Name IsReadOnly -Value $true
        try {
            $r = Run-Cli $exe @("backup-dir", "--reset")
            Check "只读设置文件 -> BACKUP_DIR_FAIL 无法恢复默认" (HasMarker $r.out '^BACKUP_DIR_FAIL .*无法恢复默认')
            Check "只读设置文件 -> 退出码 1" ($r.rc -eq 1)
        } finally {
            Set-ItemProperty -LiteralPath $sel -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue
        }
    } else {
        Check "只读设置文件 -> BACKUP_DIR_FAIL（前置设置文件缺失）" $false
    }

    # 成功面不回退
    $r = Run-Cli $exe @("backup-dir")
    Check "查看 -> 退出码 0" (($r.rc -eq 0) -and (HasMarker $r.out '^BACKUP_DIR '))
    $r = Run-Cli $exe @("backup-dir", "--reset")
    Check "恢复默认 -> 退出码 0" (($r.rc -eq 0) -and (HasMarker $r.out '^BACKUP_DIR_RESET '))

    # 不可复现的防御分支：如实标注，不假装覆盖
    Skip "设置文件删掉后仍存在（BACKUP_DIR_FAIL 第二种）" `
        "需要 Delete 与 File.Exists 之间的竞态，无法确定性复现"
    Skip "目录创建后仍不存在（BACKUP_DIR_FAIL 第三种）" `
        "本机 CreateDirectory 对不存在的盘符直接抛错（走“无法创建目录”），该分支需要特定平台行为"
    Skip "GetFullPath 抛异常的“路径无效”分支" " .NET 8 在 Windows 上不再对常见非法字符抛异常（实测 8 组）"
    Skip "取不到状态目录（BACKUP_DIR_FAIL 第四种）" `
        "StateDir 由平台层保证非空（exe 目录或 %APPDATA% 回退），该分支是防御性的"

    # ------------------------------------------------------------------
    # 3) restore --dry-run
    # ------------------------------------------------------------------
    Section "3. restore --dry-run（拒绝类全部 1）"

    $env:DSH_MINATO_BACKUP_DIR = $bkEmpty
    $r = Run-Cli $exe @("restore", "--dry-run")
    Check "无有效备份 -> DRYRUN_FAIL 无有效备份" (HasMarker $r.out '^DRYRUN_FAIL .*无有效备份')
    Check "无有效备份 -> 退出码 1" ($r.rc -eq 1)
    Check "无有效备份 -> 没有 DRYRUN_OK（不假成功）" (-not (HasMarker $r.out '^DRYRUN_OK'))
    $env:DSH_MINATO_BACKUP_DIR = $bkDir

    # 无效包目录（在备份根内，有 dsh-data- 名字但没有数据特征）
    $badPkg = Join-Path $bkDir 'dsh-data-20990102-000000-auto'
    New-Item -ItemType Directory -Path $badPkg -Force | Out-Null
    $r = Run-Cli $exe @("restore", "--dry-run", "--path", $badPkg)
    Check "无效包目录 -> DRYRUN_FAIL 无效备份目录" (HasMarker $r.out '^DRYRUN_FAIL .*无效备份目录')
    Check "无效包目录 -> 退出码 1" ($r.rc -eq 1)

    # 根外路径
    $r = Run-Cli $exe @("restore", "--dry-run", "--path", $env:WINDIR)
    Check "备份根外路径 -> DRYRUN_FAIL 不是有效备份包" (HasMarker $r.out '^DRYRUN_FAIL .*不是有效备份包')
    Check "备份根外路径 -> 退出码 1" ($r.rc -eq 1)

    # 未完成包（有数据特征、没有完成标记）——自动分支与显式分支各一条
    $trunc = Join-Path $bkDir 'dsh-data-20990103-000000-auto'
    New-Item -ItemType Directory -Path $trunc -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $trunc 'credentials.yaml'), "x`n")
    $r = Run-Cli $exe @("restore", "--dry-run")
    Check "自动选择到未完成包 -> DRYRUN_FAIL（缺完成标记）" (HasMarker $r.out '^DRYRUN_FAIL .*未完成')
    Check "自动选择到未完成包 -> 退出码 1" ($r.rc -eq 1)
    $r = Run-Cli $exe @("restore", "--dry-run", "--path", $trunc)
    Check "显式指定未完成包 -> DRYRUN_FAIL（缺完成标记）" (HasMarker $r.out '^DRYRUN_FAIL .*未完成')
    Check "显式指定未完成包 -> 退出码 1" ($r.rc -eq 1)

    # 成功面：真做一份完整备份，再预览它
    $dataHome = Join-Path $iso 'data'
    New-Item -ItemType Directory -Path (Join-Path $dataHome 'storages') -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $dataHome 'storages\a.txt'), "ALPHA`n")
    $env:DSH_HOME = $dataHome
    $env:DSH_MINATO_BACKUP_DIR = $bkDir
    $b = Run-Cli $exe @("backup", "--to", $bkDir)
    $pkg = $null
    if (HasMarker $b.out '^BACKUP_OK ') {
        $line = @(($b.out -split "`n") | Where-Object { $_ -match '^BACKUP_OK ' })[0]
        $pkg = $line.Substring('BACKUP_OK '.Length).Trim()
    }
    Check "前置：backup 成功（BACKUP_OK + rc=0）" (($b.rc -eq 0) -and ($pkg -ne $null))
    $r = Run-Cli $exe @("restore", "--dry-run", "--path", $pkg)
    Check "完整包预览 -> DRYRUN_OK" (HasMarker $r.out '^DRYRUN_OK')
    Check "完整包预览 -> 退出码 0（成功面不回退）" ($r.rc -eq 0)

    # ------------------------------------------------------------------
    # 4) wipe
    # ------------------------------------------------------------------
    Section "4. wipe（未确认 1 · 已确认 0 · 两种都不删除）"
    $markerFile = Join-Path $dataHome 'storages\a.txt'
    Check "前置：数据文件在" (Test-Path -LiteralPath $markerFile)

    $r = Run-Cli $exe @("wipe")
    Check "缺 --yes -> WIPE_PLAN / WIPE_MANUAL 仍打印" ((HasMarker $r.out '^WIPE_PLAN ') -and (HasMarker $r.out '^WIPE_MANUAL '))
    Check "缺 --yes -> 退出码 1（拒绝执行清除）" ($r.rc -eq 1)
    Check "缺 --yes -> 数据文件原样还在" (Test-Path -LiteralPath $markerFile)

    $r = Run-Cli $exe @("wipe", "--yes")
    Check "带 --yes -> 退出码 0（只给报告）" (($r.rc -eq 0) -and (HasMarker $r.out '^WIPE_MANUAL '))
    Check "带 --yes -> 仍然一个文件都没删" (Test-Path -LiteralPath $markerFile)
    Skip "WIPE_FAIL 分支（GetFullPath 抛异常）" "DSH_HOME 的异常取值在 .NET 8 全部被平台层兜住，防御分支在本机不可达"

    # ------------------------------------------------------------------
    # 5) doctor / profiles / profilepatch / bootdiag / log（EXIT-02 政策 A：失败 1 · 用法 2）
    # ------------------------------------------------------------------
    Section "5. doctor / profiles / profilepatch / bootdiag / log（失败 1 · 用法 2）"

    $r = Run-Cli $exe @("profiles")
    Check "profiles 找不到 profiles 目录 -> PROFILES_FAIL" (HasMarker $r.out '^PROFILES_FAIL ')
    Check "profiles 找不到 profiles 目录 -> 退出码 1" ($r.rc -eq 1)

    $r = Run-Cli $exe @("profilepatch")
    Check "profilepatch 缺 --profile -> PROFILEPATCH_FAIL usage" (HasMarker $r.out '^PROFILEPATCH_FAIL usage')
    Check "profilepatch 缺 --profile -> 退出码 2（用法错误）" ($r.rc -eq 2)
    $r = Run-Cli $exe @("profilepatch", "--profile", "bad name", "--id", "x")
    Check "profilepatch 非法 profile 名 -> PROFILEPATCH_FAIL" (HasMarker $r.out '^PROFILEPATCH_FAIL ')
    Check "profilepatch 非法 profile 名 -> 退出码 2（用法错误）" ($r.rc -eq 2)
    $r = Run-Cli $exe @("profilepatch", "--profile", "no-such-profile", "--id", "x")
    Check "profilepatch profile 不存在 -> PROFILEPATCH_FAIL file-not-found" (HasMarker $r.out '^PROFILEPATCH_FAIL file-not-found')
    Check "profilepatch profile 不存在 -> 退出码 1（执行失败）" ($r.rc -eq 1)

    $r = Run-Cli $exe @("bootdiag")
    Check "bootdiag 缺 --from -> BOOTDIAG_FAIL no-input" (HasMarker $r.out '^BOOTDIAG_FAIL no-input')
    Check "bootdiag 缺 --from -> 退出码 2（用法错误）" ($r.rc -eq 2)
    $r = Run-Cli $exe @("bootdiag", "--from", (Join-Path $iso "no-such-boot.log"))
    Check "bootdiag 文件读不到 -> BOOTDIAG_FAIL cannot-read" (HasMarker $r.out '^BOOTDIAG_FAIL cannot-read')
    Check "bootdiag 文件读不到 -> 退出码 1" ($r.rc -eq 1)
    $junkBoot = Join-Path $iso "junk-boot.log"
    [System.IO.File]::WriteAllText($junkBoot, "not a boot log`n")
    $r = Run-Cli $exe @("bootdiag", "--from", $junkBoot)
    Check "bootdiag 内容认不出 -> BOOTDIAG_FAIL" (HasMarker $r.out '^BOOTDIAG_FAIL')
    Check "bootdiag 内容认不出 -> 退出码 1" ($r.rc -eq 1)

    $r = Run-Cli $exe @("doctor", "--report", $bkDir)
    Check "doctor --report 指向目录 -> DOCTOR_WRITE_FAIL" (HasMarker $r.out '^DOCTOR_WRITE_FAIL ')
    Check "doctor --report 指向目录 -> 退出码 1" ($r.rc -eq 1)

    $logDir = Join-Path $cliDir "logs"
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $logDir "launcher.log"), "INFO test line`n")
    $r = Run-Cli $exe @("log", "--export", $bkDir, "--yes")
    Check "log --export 指向目录 -> LOG_FAIL" (HasMarker $r.out '^LOG_FAIL ')
    Check "log --export 指向目录 -> 退出码 1" ($r.rc -eq 1)

    # ------------------------------------------------------------------
    # 6) 用法错误 2 与失败 1：import / backup-delete / backup-export / restore
    #    EXIT-02 政策 A：缺参数/非法值 = 2；路径不存在/空目录/时间相差过大 = 1
    # ------------------------------------------------------------------
    Section "6. import / backup-delete / backup-export / restore（用法 2 · 失败 1）"

    $r = Run-Cli $exe @("import")
    Check "import 缺 --path -> IMPORT_FAIL usage" (HasMarker $r.out '^IMPORT_FAIL usage')
    Check "import 缺 --path -> 退出码 2（用法错误）" ($r.rc -eq 2)

    $r = Run-Cli $exe @("import", "--path", (Join-Path $iso "no-such-source"))
    Check "import 源目录不存在 -> IMPORT_FAIL" (HasMarker $r.out '^IMPORT_FAIL .*源目录不存在')
    Check "import 源目录不存在 -> 退出码 1（执行失败）" ($r.rc -eq 1)

    $emptySrc = Join-Path $iso "empty-source"
    New-Item -ItemType Directory -Path $emptySrc -Force | Out-Null
    $r = Run-Cli $exe @("import", "--path", $emptySrc)
    Check "import 空目录 -> IMPORT_FAIL 无效备份目录" (HasMarker $r.out '^IMPORT_FAIL .*无效备份目录')
    Check "import 空目录 -> 退出码 1" ($r.rc -eq 1)

    $r = Run-Cli $exe @("backup-delete")
    Check "backup-delete 缺 --path -> BKDEL_FAIL" (HasMarker $r.out '^BKDEL_FAIL ')
    Check "backup-delete 缺 --path -> 退出码 2（用法错误）" ($r.rc -eq 2)

    if ($null -ne $pkg) {
        $r = Run-Cli $exe @("backup-delete", "--path", $pkg, "--confirm-time", "not-a-time")
        Check "backup-delete 时间格式不对 -> BKDEL_FAIL" (HasMarker $r.out '^BKDEL_FAIL ')
        Check "backup-delete 时间格式不对 -> 退出码 2（用法错误）" ($r.rc -eq 2)
        Check "backup-delete 时间格式不对 -> 备份原样还在（没有误删）" (Test-Path -LiteralPath $pkg)
        $stale = (Get-Date).AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss")
        $r = Run-Cli $exe @("backup-delete", "--path", $pkg, "--confirm-time", $stale)
        Check "backup-delete 时间相差过大 -> BKDEL_FAIL" (HasMarker $r.out '^BKDEL_FAIL .*相差')
        Check "backup-delete 时间相差过大 -> 退出码 1（执行失败/拒绝）" ($r.rc -eq 1)
        Check "backup-delete 时间相差过大 -> 备份原样还在（没有误删）" (Test-Path -LiteralPath $pkg)
    } else {
        Skip "backup-delete 时间/目标不动三件套" "§3 前置（backup 成功）没拿到包路径，无法构造"
    }

    $r = Run-Cli $exe @("backup-export", "--path", $pkg)
    Check "backup-export 缺 --to -> BKEXPORT_FAIL" (HasMarker $r.out '^BKEXPORT_FAIL ')
    Check "backup-export 缺 --to -> 退出码 2（用法错误）" ($r.rc -eq 2)

    $r = Run-Cli $exe @("restore", "--path", "   ")
    Check "restore --path 空白 -> RESTORE_FAIL 未指定备份目录" (HasMarker $r.out '^RESTORE_FAIL .*未指定备份目录')
    Check "restore --path 空白 -> 退出码 2（用法错误）" ($r.rc -eq 2)

    # ------------------------------------------------------------------
    # 7) verify-install / ui（EXIT-02 政策 A：无法核对/找不到 GUI = 1；一致 = 0）
    # ------------------------------------------------------------------
    Section "7. verify-install / ui"
    $selfCopy = Join-Path $cliDir (Split-Path -Leaf $exe)

    $r = Run-Cli $exe @("verify-install", "--file", (Join-Path $iso "no-such-file.exe"))
    Check "verify-install 文件不存在 -> VERIFY_FAIL" (HasMarker $r.out '^VERIFY_FAIL ')
    Check "verify-install 文件不存在 -> 退出码 1" ($r.rc -eq 1)

    $badMan = Join-Path $iso "bad-manifest.txt"
    [System.IO.File]::WriteAllText($badMan, "this is not a sha line`n")
    $r = Run-Cli $exe @("verify-install", "--file", $selfCopy, "--manifest", $badMan)
    Check "verify-install 清单无可解析行 -> VERIFY_MANIFEST_MISSING" (HasMarker $r.out '^VERIFY_MANIFEST_MISSING ')
    Check "verify-install 清单无可解析行 -> 退出码 1" ($r.rc -eq 1)

    $wrongMan = Join-Path $iso "wrong-manifest.txt"
    [System.IO.File]::WriteAllText($wrongMan, (("0" * 64) + "  " + (Split-Path -Leaf $selfCopy) + "`n"))
    $r = Run-Cli $exe @("verify-install", "--file", $selfCopy, "--manifest", $wrongMan)
    Check "verify-install 哈希不一致 -> VERIFY_MISMATCH" (HasMarker $r.out '^VERIFY_MISMATCH ')
    Check "verify-install 哈希不一致 -> VERIFY_FAIL 1" (HasMarker $r.out '^VERIFY_FAIL 1$')
    Check "verify-install 哈希不一致 -> 退出码 1" ($r.rc -eq 1)

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $selfCopy).Hash.ToLower()
    $goodMan = Join-Path $iso "good-manifest.txt"
    [System.IO.File]::WriteAllText($goodMan, ($hash + "  " + (Split-Path -Leaf $selfCopy) + "`n"))
    $r = Run-Cli $exe @("verify-install", "--file", $selfCopy, "--manifest", $goodMan)
    Check "verify-install 哈希一致 -> VERIFY_OK 1" (HasMarker $r.out '^VERIFY_OK 1$')
    Check "verify-install 哈希一致 -> 退出码 0（成功面不回退）" ($r.rc -eq 0)

    $r = Run-Cli $exe @("verify-install", "--file", $selfCopy, "--url", "notaurl")
    Check "verify-install 清单下载失败 -> VERIFY_FAIL" (HasMarker $r.out '^VERIFY_FAIL ')
    Check "verify-install 清单下载失败 -> 退出码 1" ($r.rc -eq 1)

    $oldGui = $env:DSHT_GUI
    $env:DSHT_GUI = (Join-Path $iso "no-such-gui.exe")
    $r = Run-Cli $exe @("ui")
    Check "ui DSHT_GUI 指向不存在文件 -> UI_FAIL" (HasMarker $r.out '^UI_FAIL ')
    Check "ui DSHT_GUI 指向不存在文件 -> 退出码 1" ($r.rc -eq 1)
    $notGui = Join-Path $iso "not-a-gui.txt"
    [System.IO.File]::WriteAllText($notGui, "x")
    $env:DSHT_GUI = $notGui
    $r = Run-Cli $exe @("ui")
    Check "ui 指向不可执行文件 -> UI_FAIL" (HasMarker $r.out '^UI_FAIL ')
    Check "ui 指向不可执行文件 -> 退出码 1" ($r.rc -eq 1)
    $env:DSHT_GUI = $oldGui

    # ------------------------------------------------------------------
    # 8) start / stop（EXIT-02 政策 A：非法参数 2 · 端口被非 dsh 占/无可停实例 = 1）
    #    纪律：只碰**空闲端口**与自己的占位进程；绝不碰默认 3080、绝不杀别人的进程。
    # ------------------------------------------------------------------
    Section "8. start / stop"
    # 纪律：只碰**空闲端口**与**自己起的占位进程**；绝不碰默认 3080、绝不杀别人的进程。
    # ⚠ 占位进程的**命令行与脚本路径都必须不含 "dsh"**：Windows 侧 IsDshCommandLine = "命令行含 dsh"，
    #   若占位进程路径带 dsh（例如本仓库路径），CLI 会把它当 dsh → stop --yes 会把测试进程自己杀掉（实测踩过 ✗）。
    $tmpL = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
    $tmpL.Start()
    $freePort = ([System.Net.IPEndPoint]$tmpL.LocalEndpoint).Port
    $tmpL.Stop()
    $tmpL2 = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
    $tmpL2.Start()
    $occPort = ([System.Net.IPEndPoint]$tmpL2.LocalEndpoint).Port
    $tmpL2.Stop()

    # ① 端口空闲：非法 profile -> 2（校验发生在"准备启动"阶段，且不会真的启动）
    $r = Run-Cli $exe @("start", "--yes", "--port", $freePort, "--profile", "bad name")
    Check "start 非法 profile -> START_FAIL" (HasMarker $r.out '^START_FAIL ')
    Check "start 非法 profile -> 退出码 2（用法错误，且没有真的启动）" ($r.rc -eq 2)

    # ② 端口空闲：stop 报"没有可停的实例" -> 1
    $r = Run-Cli $exe @("stop", "--port", $freePort)
    Check "stop 端口无监听 -> STOP_FAIL" (HasMarker $r.out '^STOP_FAIL ')
    Check "stop 端口无监听 -> 退出码 1" ($r.rc -eq 1)

    # ③ 占位监听进程（命令行/路径都不含 dsh）-> 非 dsh 监听面
    $holdDir = Join-Path $env:TEMP ("exit02_hold_" + [guid]::NewGuid().ToString("N").Substring(0, 8))
    $holdScript = Join-Path $holdDir "hold.ps1"
    $holder = $null
    $listening = $false
    try {
        New-Item -ItemType Directory -Path $holdDir -Force | Out-Null
        $holdLines = @(
            '$l2 = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, ' + $occPort + ')',
            '$l2.Start()',
            'Start-Sleep -Seconds 300')
        Set-Content -LiteralPath $holdScript -Value $holdLines -Encoding ASCII
        $holder = Start-Process powershell -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", $holdScript) -PassThru
        for ($i = 0; $i -lt 40; $i++) {
            Start-Sleep -Milliseconds 250
            $cl = New-Object System.Net.Sockets.TcpClient
            try { $cl.Connect("127.0.0.1", $occPort); $listening = $true } catch { }
            try { $cl.Close() } catch { }
            if ($listening) { break }
        }
        if ($listening) {
            $r = Run-Cli $exe @("start", "--port", $occPort, "--yes")
            Check "start 端口被非 dsh 占 -> START_FAIL" (HasMarker $r.out '^START_FAIL ')
            Check "start 端口被非 dsh 占 -> 退出码 1（拒绝，不启动）" ($r.rc -eq 1)
            $r = Run-Cli $exe @("stop", "--port", $occPort, "--yes")
            Check "stop 非 dsh 监听 -> STOP_FAIL" (HasMarker $r.out '^STOP_FAIL ')
            Check "stop 非 dsh 监听 -> 退出码 1（拒绝，不杀）" ($r.rc -eq 1)
            $r = Run-Cli $exe @("stop", "--port", $occPort)
            Check "stop 未确认 -> STOP_PLAN 且退出码 0（计划态不是失败）" ((HasMarker $r.out '^STOP_PLAN ') -and ($r.rc -eq 0))
            Check "占位进程仍在（CLI 没有误杀别人的进程）" ($null -ne $holder -and -not $holder.HasExited)
        } else {
            Skip "start/stop 端口被非 dsh 占三条" "占位监听进程 10 秒内没起来；不冒险让 start 去启动真实 dsh"
        }
    } finally {
        if ($null -ne $holder) { Stop-Process -Id $holder.Id -Force -ErrorAction SilentlyContinue }
        Remove-Item -LiteralPath $holdDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    $r = Run-Cli $exe @("stop", "--target", "desktop")
    Check "stop --target desktop 终态与 rc 一致（STOP_FAIL→1 / STOP_PLAN→0；不执行）" `
        (((HasMarker $r.out '^STOP_FAIL ') -and ($r.rc -eq 1)) -or ((HasMarker $r.out '^STOP_PLAN ') -and ($r.rc -eq 0)))

    # ------------------------------------------------------------------
    # 9) bridge-install（EXIT-02 政策 A：非法参数 2 · 没有随包插件 = 1）
    # ------------------------------------------------------------------
    Section "9. bridge-install"
    $r = Run-Cli $exe @("bridge-install", "--profile", "bad name")
    Check "bridge-install 非法 profile -> BRIDGE_FAIL" (HasMarker $r.out '^BRIDGE_FAIL ')
    Check "bridge-install 非法 profile -> 退出码 2（用法错误）" ($r.rc -eq 2)

    $r = Run-Cli $exe @("bridge-install", "--yes")
    Check "bridge-install 隔离副本没有随包插件 -> BRIDGE_NO_PLUGIN" (HasMarker $r.out '^BRIDGE_NO_PLUGIN ')
    Check "bridge-install 隔离副本没有随包插件 -> 退出码 1（执行失败）" ($r.rc -eq 1)

    # ------------------------------------------------------------------
    # 10) sessions / cost（EXIT-02 政策 A：参数非法 = 2 · 无投影/血缘不可用 = 1）
    # ------------------------------------------------------------------
    Section "10. sessions / cost"
    $r = Run-Cli $exe @("sessions", "--days", "99")
    Check "sessions 坏 --days -> SESSIONS_FAIL" (HasMarker $r.out '^SESSIONS_FAIL ')
    Check "sessions 坏 --days -> 退出码 2（用法错误）" ($r.rc -eq 2)
    $r = Run-Cli $exe @("sessions", "--from", "2026/10/01", "--to", "2026-10-30")
    Check "sessions 坏日期格式 -> SESSIONS_FAIL" (HasMarker $r.out '^SESSIONS_FAIL ')
    Check "sessions 坏日期格式 -> 退出码 2" ($r.rc -eq 2)
    $r = Run-Cli $exe @("sessions", "--days", "7", "--from", "2026-10-01", "--to", "2026-10-30")
    Check "sessions --days 与 --from/--to 互斥 -> 退出码 2" (($r.rc -eq 2) -and (HasMarker $r.out '^SESSIONS_FAIL '))
    $r = Run-Cli $exe @("sessions")
    Check "sessions 无投影 -> SESSIONS_FAIL + 退出码 1（执行失败，不是用法错误）" (($r.rc -eq 1) -and (HasMarker $r.out '^SESSIONS_FAIL '))
    $r = Run-Cli $exe @("sessions", "--level", "parents")
    Check "sessions 无血缘快照 + --level parents -> SESSIONS_FAIL + 退出码 1" (($r.rc -eq 1) -and (HasMarker $r.out '^SESSIONS_FAIL '))
    $r = Run-Cli $exe @("cost", "--days", "99")
    Check "cost 坏 --days -> COST_FAIL" (HasMarker $r.out '^COST_FAIL ')
    Check "cost 坏 --days -> 退出码 2（用法错误）" ($r.rc -eq 2)
    $r = Run-Cli $exe @("cost", "--level", "parents")
    Check "cost 无血缘快照 + --level parents -> COST_FAIL + 退出码 1" (($r.rc -eq 1) -and (HasMarker $r.out '^COST_FAIL '))

    # ------------------------------------------------------------------
    # 11) install / update（EXIT-02 政策 A：--version 白名单不过 = 2）
    # ------------------------------------------------------------------
    Section "11. install / update"
    $r = Run-Cli $exe @("update", "--version", "9.9.9;calc")
    Check "update 非法 --version -> UPDATE_FAIL" (HasMarker $r.out '^UPDATE_FAIL ')
    Check "update 非法 --version -> 退出码 2（用法错误，且没有调用 npm）" ($r.rc -eq 2)

    # ------------------------------------------------------------------
    # 12) not-covered 登记（EXIT-02 政策 A 要求：构造不出本地场景必须显式登记，不静默跳过）
    #     裁决表：docs/退出码裁决-2026-10-30.md
    # ------------------------------------------------------------------
    Section "12. not-covered 登记（不构造本地场景的原因）"
    Skip "SHORTCUT_FAIL 失败面（5 点，rc 已改 1）" "会写用户真实桌面/开始菜单；验收脚本不污染本机"
    Skip "AUTOSTART_FAIL 失败面（3 点，rc 已改 1）" "会写用户真实启动目录；验收脚本不污染本机"
    Skip "START_FAIL 的启动命令发不出 / 30 秒未就绪（rc 已改 1）" "需要真实启动 dsh 子进程（动全局安装与端口），验收脚本不启动真实 dsh"
    Skip "STOP_FAIL 的自身/桌面端停止失败/12 秒未停（rc 已改 1）" "需要杀真实桌面端或让 CLI 自身监听端口；不安全，不构造"
    Skip "BRIDGE_FAIL 的 auto-patch 失败 / 安装未成功（rc 已改 1）" "需要真实 dsh + pnpm 并改用户 profile；不构造（BRIDGE_NO_PLUGIN/非法 profile 已覆盖）"
    Skip "BRIDGE_NO_DSH / BRIDGE_NO_PNPM / BRIDGE_MANAGED（rc 已改 1）" "隔离副本先命中 BRIDGE_NO_PLUGIN 就返回；要构造需改用户环境"
    Skip "VERIFY_FAIL 的拿不到自身路径 / 无法计算哈希（rc 已改 1）" "需要无法解析 MainModule 或无法读自身文件的异常环境，防御分支不可达"
    Skip "INSTALL/UPDATE 的 npm 失败 / 回滚失败 / 版本列表拿不到（rc 已改 1）" "会真的调用 npm 改全局包；白名单/用法面已覆盖"
    Skip "BACKUP_FAIL / WIPE_FAIL / RESTORE_ROLLBACK_FAILED 的写盘失败面" "需要只读盘/恢复与回滚双重失败；构造会破坏隔离根或依赖平台竞态"
    Skip "BKDEL_FAIL 的删除后仍在 / BKEXPORT_FAIL 的导出失败" "需要平台层写盘失败（只读盘/被杀进程）；用法与拒绝面已覆盖"
    Skip "诊断行保持 0：BRIDGE_FAIL_RAW / IMPORT_PRE_BACKUP_FAILED / INSTALL_PRE_BACKUP_FAILED" "诊断行随主流程继续，裁决表明确保持 0；不当作失败族"

    # ------------------------------------------------------------------
    # 13) RunQuiet：双路并发读 + 超时 kill
    # ------------------------------------------------------------------
    Section "13. RunQuiet（狂写 stderr 不卡死 · 不退出会在超时后结束）"

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $r = Run-Cli $exe @("selftest", "--runquiet", "cmd.exe", "/c", "for /L %i in (1,1,30000) do @echo xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx 1>&2")
    $sw.Stop()
    $secs = [int]$sw.Elapsed.TotalSeconds
    Write-Host ("  -- 狂写 stderr：耗时 " + $secs + "s，rc=" + $r.rc)
    Check "狂写 stderr -> 子进程正常退出（RUNQUIET_RESULT exited）" (HasMarker $r.out '^RUNQUIET_RESULT exited')
    Check "狂写 stderr -> 退出码 0" ($r.rc -eq 0)
    Check "狂写 stderr -> 没有卡死（< 20 秒）" ($secs -lt 20)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $r = Run-Cli $exe @("selftest", "--runquiet", "cmd.exe", "/c", "ping -n 30 127.0.0.1 >nul")
    $sw.Stop()
    $secs = [int]$sw.Elapsed.TotalSeconds
    Write-Host ("  -- 长驻子进程：耗时 " + $secs + "s，rc=" + $r.rc)
    Check "长驻子进程 -> 明确超时（RUNQUIET_TIMEOUT）" (HasMarker $r.out '^RUNQUIET_TIMEOUT ')
    Check "长驻子进程 -> RUNQUIET_RESULT timeout" (HasMarker $r.out '^RUNQUIET_RESULT timeout')
    Check "长驻子进程 -> 探针退出码 1（超时不是成功）" ($r.rc -eq 1)
    Check "长驻子进程 -> 8 秒超时后很快返回（< 20 秒）" ($secs -lt 20)
} finally {
    $env:DSH_HOME = $oldHome
    $env:DSH_MINATO_BACKUP_DIR = $oldBk
    if (Test-Path -LiteralPath $cfg) { Set-ItemProperty -LiteralPath $cfg -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath (Join-Path $cliDir '.backup-dir')) {
        Set-ItemProperty -LiteralPath (Join-Path $cliDir '.backup-dir') -Name IsReadOnly -Value $false -ErrorAction SilentlyContinue
    }
    if (-not $Keep) { Remove-Item -LiteralPath $iso -Recurse -Force -ErrorAction SilentlyContinue }
    else { Write-Host ("隔离目录保留: " + $iso) }
}

Write-Host ""
$total = $script:pass + $script:fail
Write-Host ("== 退出码契约：" + $script:pass + "/" + $total + " passed, " + $script:fail + " failed（SKIP " + $script:skip + "）==")
# 项数下限：删掉用例会静默变绿 -> 这里必须红（SKIP 不计入，避免用 SKIP 冲抵）
$EXPECTED_MIN = 118   # ★ EXIT-02（2026-10-30）：全族补齐后实跑 118 项（SKIP 不计入）；删掉任何一条都会红
if (($script:pass + $script:fail) -lt $EXPECTED_MIN) {
    Write-Host ("  [FAIL] 项数不足：实跑 " + ($script:pass + $script:fail) + " < 声明 " + $EXPECTED_MIN)
    $script:fail++
}
if ($script:fail -ne 0) { exit 1 }
exit 0
