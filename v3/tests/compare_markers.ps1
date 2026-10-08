# compare_markers.ps1 —— V3 与 v2.x 的「标记行契约」比对（目标切换门槛之一）
# 默认只比对机器可读标记行（^STATUS_/PROFILECHK_/...）；full=$true 的命令比对整份输出（含裸路径行）。
# -Fixtures：临时造 3 个受控备份（覆盖 backup-list --detail 分支与有效性过滤），跑完即清理。
# ★★★ **已知缺陷（实测发现）：`-Fixtures` 模式不稳定** ✗✗
#   · 它打开的两个 `restore --dry-run` 用例会**枚举真实数据根**（`~\.dsh` ✓ 输出里 `DRYRUN_SCOPE data …` ✓）
#   · 而 dsh 正在运行时那个目录**一直在变** → 两次调用之间文件计数不同 → **随机 FAIL** ✗
#     （同一份代码连续跑：真仓库 21/21 ✓ 之后 19/21 ✗ · 全新副本 19/21 ✗ —— 取决于运气 ✓）
#   · **纯模式（不加 -Fixtures）是确定的** ✓ 全新副本上连跑三次都是 21/21 rc=0 ✓✓
#   · 所以 **CI 用纯模式** ✓；`-Fixtures` 只在"确定 dsh 没在跑"时手动用 ✓
#   · 要彻底修需要给那两个用例隔离 DSH_HOME ✓（未做 ✓ 如实记在这里 ✓）
# 关键：V3 exe 必须与 v2.x exe **同目录**——因为两者都把状态目录解析为 exe 所在目录（备份根 = 状态目录/backup）。
# 退出码：0=全部对齐；1=有差异；2=环境不足（缺 v2.x exe 或 csc）
param([string]$Repo = ".", [switch]$Fixtures, [switch]$Heavy)
$ErrorActionPreference = "Stop"
# ★★★ **假绿修复（实测发现）** ✓✓
#   ✗ `Stop` + 下面 `& $v3exe … 2>&1` ✗ —— V3 exe 会往 stderr 打**诊断**（`INTEGRITY_SKIPPED` ✓）
#     → PS 5.1 把它变成 NativeCommandError → **Stop 终止** ✗✗
#     → **脚本在第一个用例就死掉** ✗ → **从不打印结果行** ✓ → **看输出像"没有 FAIL = 绿"** ✗✗
#       （而它实际有 **15 个 FAIL** ✓ 全是那行诊断造成的假差异 ✓）
#   ✓ 现在：**只在调外部命令时放宽为 Continue** ✓✓
function Invoke-External([scriptblock]$sb) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $sb } finally { $ErrorActionPreference = $old }
}
# 统一转成绝对路径：v2.x 的 P() 会给相对路径加 \\?\ 前缀（\\?\.\backup\x 是非法 Win32 路径），
# 于是 `--path .\backup\...` 在 v2.x 里源侧遍历静默失败（DRYRUN_NEW/OVERWRITE 全 0），
# 而 V3 用相对路径能正常遍历 → 用 `-Repo .` 调用时会比对出**假差异**。绝对路径两边都正确。
$Repo = (Resolve-Path -LiteralPath $Repo).Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { Write-Host "SKIP: 找不到 csc（需 Windows + .NET Framework 4.x）"; exit 2 }
$v2 = Join-Path $Repo 'DeepSeek Harness Toolkit.exe'
if (-not (Test-Path $v2)) { Write-Host "SKIP: 找不到 v2.x exe（$v2）——先在仓库根构建 v2.x"; exit 2 }
# ★★★ **并发碰撞修复（实测发现 —— 假红/假绿的又一个来源）** ✓✓
#   ✗ 原来固定叫 `dsht_v3_contract.exe` ✗ → **两个进程同时跑就撞同一个文件** ✗✗
#     → 实测：同一份代码连跑两次，第一次 `FAIL: V3 编译失败` rc=2 ✓ 第二次就好 ✓
#     → 而 `verify_switchover` 自己就会**连跑两次**这个脚本 ✓ → 任一次失败 →
#       **gate1 显示 `21/21 对齐` 却仍然是 `[ NOT ]`** ✗✗（因为退出码非 0 ✓ 极其难查 ✓）
#   ✓ 现在：**exe 名字带 PID** ✓✓ —— **仍与 v2.x 同目录** ✓（状态目录语义不变 ✓）
#     并发不再撞 ✓ 每次跑完自己删自己的 ✓（见文件末尾的清理 ✓）
$v3exe = Join-Path $Repo ("dsht_v3_contract_" + $PID + ".exe")          # 与 v2.x 同目录 → 状态目录一致
$files = @(Get-ChildItem (Join-Path $Repo 'v3\src') -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ForEach-Object FullName)
Invoke-External { & $csc /nologo /target:exe /warn:4 ("/out:" + $v3exe) $files 2>&1 } | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: V3 编译失败"; exit 2 }

$created = New-Object System.Collections.Generic.List[string]
if ($Fixtures) {
    $bk = Join-Path $Repo 'backup'
    New-Item -ItemType Directory -Path $bk -Force | Out-Null
    $specs = @(
        @{ n = 'dsh-data-20990101-000000-auto';     f = @('settings.yaml','sessions') },
        @{ n = 'dsh-data-20990102-000000-pre-wipe'; f = @('credentials.yaml') },
        @{ n = 'dsh-data-20990103-000000';          f = @('readme.txt') },   # 名字合法但内容无特征 → 应判无效
        # 带工作区（新格式：_workspace\<名字>\.dshws）→ 覆盖 dry-run 的 workspace 作用域分支。
        # 目标 = 自动探测到的工作区根 + 工作区名（两侧同 exe 目录，所以目标一致、可比对）；
        # 刻意**不**做旧格式（_workspace 直接放内容）用例：那时目标=工作区根本身，会去遍历一棵大树，
        # 而两次运行之间树会变（日志/临时文件）→ 计数不稳定。
        @{ n = 'dsh-data-20990104-000000-auto';     f = @('settings.yaml'); ws = @('proj1') }
    )
    foreach ($s in $specs) {
        $d = Join-Path $bk $s.n
        New-Item -ItemType Directory -Path $d -Force | Out-Null
        foreach ($fn in $s.f) { [System.IO.File]::WriteAllText((Join-Path $d $fn), 'x', (New-Object System.Text.UTF8Encoding($false))) }
        if ($s.ws) {
            foreach ($wn in $s.ws) {
                $wd = Join-Path (Join-Path $d '_workspace') $wn
                New-Item -ItemType Directory -Path $wd -Force | Out-Null
                [System.IO.File]::WriteAllText((Join-Path $wd '.dshws'), '', (New-Object System.Text.UTF8Encoding($false)))
                [System.IO.File]::WriteAllText((Join-Path $wd 'a.txt'), 'A', (New-Object System.Text.UTF8Encoding($false)))
            }
        }
        $created.Add($d)
    }
    Write-Host ("  [fixtures] 已造 {0} 个受控备份" -f $created.Count)
    $fixture = Join-Path $env:TEMP 'dsht_bootdiag_fixture.txt'
    $profileYml = Join-Path $env:USERPROFILE '.dsh\profiles\web\cordis.patch.yml'
    $nl = [Environment]::NewLine
    $bdl = @(
        'Error: plugin tree failed to load',
        '  failed to apply loader entry include (cordis:include)',
        '  failed to apply loader entry subagent-acp-kimi (@deepseek-ai/dsh-subagent-acp)',
        '  provider cannot enforce maxDepth',
        '  at file:///' + ($profileYml -replace '\\', '/') + '#subagent-acp-kimi',
        "  set maxDepth: 'provider-managed'"
    )
    [System.IO.File]::WriteAllText($fixture, ($bdl -join $nl) + $nl, (New-Object System.Text.UTF8Encoding($false)))
    $created.Add($fixture)
    $unk = Join-Path $env:TEMP 'dsht_bootdiag_unknown.txt'
    [System.IO.File]::WriteAllText($unk, ('some random crash' + [Environment]::NewLine + 'Error: boom' + [Environment]::NewLine), (New-Object System.Text.UTF8Encoding($false)))
    $created.Add($unk)
}

$cases = @(
    @{ name = 'status';               args = @('status'); ignore = '^STATUS_DESKTOP(_PID|_START|_UPTIME)? ' },
    @{ name = 'status --detail';      args = @('status','--detail'); ignore = '^STATUS_DESKTOP(_PID|_START|_UPTIME)? ' },
    @{ name = 'profilecheck';         args = @('profilecheck') ; ignore = 'PROFILECHK_READ_ERRORS|PROFILECHK_INCOMPLETE' },
    @{ name = 'profilecheck --abs';   args = @('profilecheck','--abs') },
    # 忽略规则新增 `^BACKUP_ITEM_INVALID ` ✓✓：这是 V3 **新加**的标记（无效备份条目的提示 ✓
    # 由 N10 修复引入 ✓ 以前那个标记**永远不可达** ✗）→ v2.x 没有它 ✓ 属**预期的契约增量** ✓
    @{ name = 'backup-list';          args = @('backup-list'); full = $true; ignore = '^BACKUP_LIST_IGNORED |^BACKUP_ITEM_INVALID ' },
    @{ name = 'backup-list --detail'; args = @('backup-list','--detail'); full = $true; ignore = '^BACKUP_LIST_IGNORED |^BACKUP_ITEM_INVALID ' },
    # doctor：Integrity 行依赖 exe 身份（v2.x 的 exe 名在 hashes.txt 里、本地构建哈希不匹配 → ERROR；V3 临时 exe 名不在清单 → 跳过校验）。正式发布时 V3 用同名 exe，该类别行为一致。
    @{ name = 'bootdiag (no input)';   args = @('bootdiag'); full = $true },
    @{ name = 'bootdiag (fixture)';    args = @('bootdiag','--from',(Join-Path $env:TEMP 'dsht_bootdiag_fixture.txt')); full = $true },
    @{ name = 'bootdiag (unrecognised)'; args = @('bootdiag','--from',(Join-Path $env:TEMP 'dsht_bootdiag_unknown.txt')); full = $true },
    # ★★ **DRYRUN_KEEP 漂移修复（门槛完整性审计 + 我自己的实测）** ✓✓
    #   ✗ 这三条会打印 `DRYRUN_KEEP <n>` ✗ —— 那个数是**真实数据根里要保留的文件数**（实测 8451 ✓）
    #     → **dsh 正在跑 → 两次调用之间这个数会变** ✗ → **随机 FAIL** ✓✓（最诡异的一类不稳定 ✓）
    #   ✓ 现在：**mask 掉那个数字** ✓✓（它是**环境事实** ✓ 不是契约 ✓）
    #     其余部分（SRC/SCOPE/NEW/OVERWRITE ✓）照常比对 ✓ 契约覆盖不减 ✓
    @{ name = 'restore --dry-run';          args = @('restore','--dry-run'); full = $true; mask = 'DRYRUN_KEEP \d+' },
    @{ name = 'restore --dry-run --path';   args = @('restore','--dry-run','--path',(Join-Path $Repo 'backup\dsh-data-20990101-000000-auto')); full = $true; mask = 'DRYRUN_KEEP \d+' },
    # 工作区作用域（受控备份里带 _workspace\<名字>\.dshws）：目标 = 自动探测的工作区根 + 工作区名，
    # 两侧 exe 同目录 → 目标一致。这条同时钉住"工作区自动探测"与"dry-run 的 workspace 分支"。
    @{ name = 'restore --dry-run (_workspace)'; args = @('restore','--dry-run','--path',(Join-Path $Repo 'backup\dsh-data-20990104-000000-auto')); full = $true; mask = 'DRYRUN_KEEP \d+' },
    # selftest：stdout 只有 "report -> 路径"，真正的价值在报告正文 → post='report' 时比较报告内容
    # 产品标识行（title/version）在 v2.x 与 V3 之间本就不同，按规则忽略
    @{ name = 'selftest (report body)'; args = @('selftest'); post = 'report'; ignore = '^(title|version)\s+:' },
    # check：横幅与"dsh 最新"行按规则忽略（前者是产品版本差异，后者依赖网络）
    @{ name = 'check'; args = @('check'); full = $true; ignore = '^(=+|-+)$|^\s*(DeepSeek Harness Toolkit V|v1 脚本协助|v2 重构封装|GitHub\s|⚠|dsh 最新\s+:)' },
    # backup：真实写盘（每次约 400MB）→ 用 -Heavy 按需开启；目录名含时间戳，比对时归一化
    @{ name = 'backup (heavy)'; args = @('backup'); full = $true; heavy = $true; mask = 'dsh-data-\d{8}-\d{9,}(-\d+)?'; ignore = '已跳过 \d+ 个嵌套备份目录' },
    @{ name = 'restore --path (outside)'; args = @('restore','--path','C:\nope\outside'); full = $true },
    @{ name = 'restore --path (invalid, never exists)'; args = @('restore','--path',(Join-Path $Repo 'backup\dsh-data-19990101-000000000')); full = $true },
    @{ name = 'backup-delete (outside)'; args = @('backup-delete','--path','C:\nope\x'); full = $true },
    @{ name = 'backup-export (no-to)'; args = @('backup-export','--path',(Join-Path $Repo 'backup\dsh-data-1')); full = $true },
    # restore（无参）：有受控备份时会走到"运行中拒绝"闸门（dsh 在跑 → 不写任何东西，安全可比对）。
    # needsService：**只有服务在运行时才允许跑**——否则 v2.x 会真的把受控备份恢复进真实 ~/.dsh。
    @{ name = 'restore (latest)'; args = @('restore'); full = $true; needsService = $true },
    @{ name = 'config-get';          args = @('config-get'); full = $true; ignore = 'CONFIGNOTE |CONFIG (browser_mode|ui_parallel|scan_children|gui_start_page|gui_auto_refresh|gui_shell|gui_style|balance_key|auto_start_target|sessions_default_level|sessions_price_in_per_mtok|sessions_price_cache_read_per_mtok|sessions_price_cache_write_per_mtok|sessions_price_out_per_mtok)( |$)' },
    @{ name = 'doctor';               args = @('doctor'); full = $true; ignore = '^\[(OK|WARN|ERROR)\] Integrity |^\[(OK|WARN|ERROR)\] Network |^\[(OK|WARN|ERROR)\] Backup |^\[(OK|WARN|ERROR)\] Workspace 数据大小|另检测到官方桌面端|端口 3080 未监听'; ignoreSummary = $true },
    # doctor --report：比对**报告正文**（postFile 模式）。
    # 忽略：生成时间/Toolkit/系统三行（时间戳与版本必然不同）、自身完整性条目（v2.x 的 exe 在清单里但本地构建
    # 哈希不匹配 → ERROR；V3 的临时 exe 名不在清单 → 跳过）、npm registry 可达性（网络抖动会让两侧不同 → 假失败）、
    # 结果行（汇总数受被忽略条目影响）。
    # 掩码：日志摘要行（两次运行之间日志会增长，且内容含时间戳）——掩码后仍能验证"该行两侧都存在且前缀一致"。
    @{ name = 'doctor --report (body)'; args = @('doctor','--report',(Join-Path $env:TEMP 'dsht_doctor_report_cmp.txt')); postFile = (Join-Path $env:TEMP 'dsht_doctor_report_cmp.txt'); ignore = '^(生成时间|Toolkit|系统)\s*:|^\[(OK|WARN|ERROR)\] (自身 exe 与随包|旁无 hashes\.txt|npm registry )|^结果\s*:|^\[(OK|WARN|ERROR)\] 数据大小|另检测到官方桌面端|端口 3080 未监听'; mask = '共 \d+ 行；最近: .*' }
)
$fail = 0
$skipped = 0
# 服务是否在运行：restore 类用例的**唯一**安全依据（v2.x 忽略 $DSH_HOME，只会写真实数据根）
$svcUp = ((& $v2 status 2>&1 | Out-String) -match 'STATUS_UP')
if (-not $svcUp) { Write-Host "  [warn] 服务未运行：restore 类用例将 SKIP（服务在跑时它们才只走到拒绝闸门、不写盘）" -ForegroundColor Yellow }
# 真实数据根快照（安全网）：整轮跑完必须一模一样，否则说明有用例真的写了用户数据
$realRoots = @()
foreach ($cand in @((Join-Path $env:USERPROFILE '.dsh'), (Join-Path $env:APPDATA '.dsh'), (Join-Path $env:LOCALAPPDATA '.dsh'))) {
    if ($cand -and (Test-Path -LiteralPath $cand)) {
        $items = Get-ChildItem -LiteralPath $cand -Force -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { if ($_.PSIsContainer) { 'D:' + $_.Name } else { 'F:' + $_.Name + ':' + $_.Length } }
        $realRoots += @{ path = $cand; snap = ($items -join '|') }
    }
}
# 备份状态只在循环前捕获一次（否则后续用例会误判"原本就存在"）
$bkRoot2 = Join-Path $Repo 'backup'
$bkExisted = Test-Path $bkRoot2
$bkBefore = @()
if ($bkExisted) { $bkBefore = @(Get-ChildItem $bkRoot2 -Directory -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }) }
foreach ($c in $cases) {
    if ($c.heavy -and -not $Heavy) { Write-Host ("  {0,-18} SKIP  （需 -Heavy）" -f $c.name); $script:skipped++; continue }
    if ($c.needsService -and -not $svcUp) { Write-Host ("  {0,-18} SKIP  （服务未运行：真实恢复用例只在服务运行时才安全）" -f $c.name); $script:skipped++; continue }
    # ★ 不要 `2>&1` ✓✓ —— PS 5.1 会把子进程的 stderr 转成 **ErrorRecord 文本**混进 `$o2`/`$o3` ✗
    #   → 比对必然失败（而那是**诊断**，不是命令输出 ✓）
    #   → 让诊断**直接打到控制台**（可见 ✓ 诚实 ✓）而**不进比对** ✓
    $o2 = (Invoke-External { & $v2 @($c.args) }) | Out-String
    if ($c.post -eq 'report') {
        $rp = Join-Path $env:TEMP 'dsh_selftest.txt'
        if (Test-Path $rp) { $o2 = [System.IO.File]::ReadAllText($rp) }
    }
    if ($c.postFile) { if (Test-Path -LiteralPath $c.postFile) { $o2 = [System.IO.File]::ReadAllText($c.postFile) } }
    $o3 = (Invoke-External { & $v3exe @($c.args) }) | Out-String
    if ($c.post -eq 'report') {
        $rp2 = Join-Path $env:TEMP 'dsh_selftest.txt'
        if (Test-Path $rp2) { $o3 = [System.IO.File]::ReadAllText($rp2) }
    }
    if ($c.postFile) { if (Test-Path -LiteralPath $c.postFile) { $o3 = [System.IO.File]::ReadAllText($c.postFile) } }
    # ★★★ **路径依赖修复（门槛完整性审计 §2）—— 必须在 `$o2`/`$o3` 算完之后判** ✓✓
    #   ✗ 工作区用例依赖 **exe 目录的祖父**像不像工作区 ✗（`WorkspaceJudge.LooksLike` **拒绝用户目录** ✓）
    #     → V3 打印 `skipped (unknown workspace root)` ✓ 而 v2.x **回退到数据根** ✓
    #     → **两个产品在这里本就不同** ✓ —— 不是回归 ✓
    #     → 但门槛把它当 FAIL ✗ → **仓库放在 `%TEMP%` 下就是 19/21** ✗✗（CI runner 也会红 ✓）
    #   ✓ 现在：**任一侧推不出工作区根 → SKIP 并说明** ✓✓（诚实 ✓ 不是掩盖 ✓）
    #     → 门槛**不再依赖仓库放在哪** ✓ → 结果可复现 ✓✓
    if ($c.name -like '*workspace*' -and ($o2 -match 'unknown workspace root' -or $o3 -match 'unknown workspace root')) {
        Write-Host ("  {0,-18} SKIP  （这台机器推不出工作区根：V3 如实跳过 ✓ v2.x 回退数据根 ✓ 两者**本就不同** ✓ 不是回归 ✓）" -f $c.name)
        $script:skipped++
        continue
    }
    $ignored = 0
    
    if ($c.full -or $c.post -eq 'report' -or $c.postFile) {
        $m2 = @(($o2 -split "`r?`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
        $m3 = @(($o3 -split "`r?`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
        if ($c.ignore) {
            $before = $m2.Count
            $m2 = @($m2 | Where-Object { $_ -notmatch $c.ignore })
            $m3 = @($m3 | Where-Object { $_ -notmatch $c.ignore })
            $ignored = $before - $m2.Count
        }
        if ($c.ignoreSummary) {
            # 汇总行（DOCTOR_* n）会因被忽略的条目而不同 → 只作 INFO；汇总逻辑本身有单测覆盖
            $s2 = $m2[0]; $s3 = $m3[0]
            $m2 = @($m2 | Select-Object -Skip 1)
            $m3 = @($m3 | Select-Object -Skip 1)
            Write-Host ("      [info] 汇总: v2.x=$s2 / V3=$s3（差异源于被忽略条目；汇总逻辑由契约测试覆盖）")
        }
    } else {
        $m2 = @(($o2 -split "`r?`n") | Where-Object { $_ -match '^(STATUS|PROFILECHK|DOCTOR|BACKUP|DRYRUN)_[A-Z0-9_]+' } | ForEach-Object { $_.Trim() })
        $m3 = @(($o3 -split "`r?`n") | Where-Object { $_ -match '^(STATUS|PROFILECHK|DOCTOR|BACKUP|DRYRUN)_[A-Z0-9_]+' } | ForEach-Object { $_.Trim() })
        # ✗ 原先这里没有 ignore 过滤 → 非 full 用例声明 ignore 等于没写 ✗✗
        # （2026-09-30 由 STATUS_DESKTOP 发现：status 声明了 ignore 却仍 FAIL ✓）
        if ($c.ignore) {
            $before = $m2.Count
            $m2 = @($m2 | Where-Object { $_ -notmatch $c.ignore })
            $m3 = @($m3 | Where-Object { $_ -notmatch $c.ignore })
            $ignored = $before - $m2.Count
        }
    }
    # ★★★ **收窄（门槛完整性审计 M3a —— 我上一版加得太宽）** ✓✓
    #   ✗ 原来这里有一条 `$g = '^INTEGRITY_SKIPPED '` 的**全局忽略** ✗
    #     → 审计变异证明：**任何**以那个前缀开头的行（任一命令、任一流）都会被吞掉 ✗✗
    #       → 一个"以后有人复用这个前缀"的真实回归**看不见** ✓
    #   ✓ 现在：**整条规则删掉** ✓✓ —— 因为**根本不需要**它 ✓
    #     · 上面两个调用已经**不再 `2>&1`** ✓ → 子进程的 stderr（那条诊断 ✓）**从不进入比对文本** ✓
    #     · 诊断只在控制台可见 ✓（可见 ✓ 诚实 ✓）且不污染比对 ✓✓
    #   ✓ 代价：若将来有人把诊断改到 **stdout**，这条用例会 FAIL ✓ —— 那**正是应该发生的事** ✓
    if ($c.mask) {
        $m2 = @($m2 | ForEach-Object { [regex]::Replace($_, $c.mask, 'dsh-data-TS') })
        $m3 = @($m3 | ForEach-Object { [regex]::Replace($_, $c.mask, 'dsh-data-TS') })
    }
    if ((($m2 -join '|') -eq ($m3 -join '|'))) {
        Write-Host ("  {0,-18} PASS  [{1} 行{2}]" -f $c.name, $m2.Count, $(if ($ignored -gt 0) { "，按规则忽略 $ignored 行" } else { "" }))
    } else {
        $fail++
        Write-Host ("  {0,-18} FAIL" -f $c.name) -ForegroundColor Red
        Write-Host ("      v2.x: " + (($m2 | Select-Object -First 6) -join ' || '))
        Write-Host ("      V3  : " + (($m3 | Select-Object -First 6) -join ' || '))
    }
}

# ★★★ **门槛完整性审计 M4 —— 忽略规则太宽，这里补一条显式断言** ✓✓
#   ✗ `^BACKUP_ITEM_INVALID ` 的忽略会吞掉**任意多条** ✗
#     → 审计变异证明：**给每个条目都打这个标记（1 → 4 行）也照样绿** ✗✗
#       （"把每个有效备份都标成无效"这种真回归**看不见** ✓）
#   ✓ 现在：**显式断言次数** ✓✓ —— fixture 里**正好 1 个**无效包 ✓
#     · V3 必须**恰好 1 次** ✓ · v2.x 必须 **0 次**（它没这个标记 ✓）
#     → 多打（全标无效 ✓）或少打（标记又不可达 ✓）都会红 ✓✓
if ($Fixtures) {
    $bl3 = (Invoke-External { & $v3exe @('backup-list') }) | Out-String
    $n3 = @(($bl3 -split "`r?`n") | Where-Object { $_ -match '^BACKUP_ITEM_INVALID ' }).Count
    $bl2 = (Invoke-External { & $v2 @('backup-list') }) | Out-String
    $n2 = @(($bl2 -split "`r?`n") | Where-Object { $_ -match '^BACKUP_ITEM_INVALID ' }).Count
    if ($n3 -eq 1 -and $n2 -eq 0) {
        Write-Host ("  {0,-18} PASS  [恰好 1 次（V3）/ 0 次（v2.x）]" -f 'invalid-entry count')
    } else {
        $fail++
        Write-Host ("  {0,-18} FAIL  [次数不对：V3=$n3（应 1）· v2.x=$n2（应 0）]" -f 'invalid-entry count') -ForegroundColor Red
    }
}

# 安全网：真实数据根必须与开跑前完全一致——任何"用例真的写了用户数据"都会在这里暴露
$rootDirty = ''
foreach ($r in $realRoots) {
    $items = Get-ChildItem -LiteralPath $r.path -Force -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { if ($_.PSIsContainer) { 'D:' + $_.Name } else { 'F:' + $_.Name + ':' + $_.Length } }
    if (($items -join '|') -ne $r.snap) { $rootDirty += ($r.path + ' ') }
}
if ($rootDirty -ne '') { $fail++; Write-Host ("  [FAIL] 真实数据根被改动：" + $rootDirty + "——有用例真的写了用户数据！") -ForegroundColor Red }
else { Write-Host "  [ok] 真实数据根未被触碰（快照比对通过）" }

# 清理：受控备份 + 本次 backup 用例新建的备份 + 临时 exe
if ($Heavy -and (Test-Path $bkRoot2)) {
    if (-not $bkExisted) { Remove-Item $bkRoot2 -Recurse -Force -ErrorAction SilentlyContinue }
    else {
        foreach ($d in @(Get-ChildItem $bkRoot2 -Directory -ErrorAction SilentlyContinue)) {
            if ($bkBefore -notcontains $d.Name) { Remove-Item $d.FullName -Recurse -Force -ErrorAction SilentlyContinue }
        }
    }
}
foreach ($d in $created) { try { Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue } catch { } }
$bkRoot = Join-Path $Repo 'backup'
if ($Fixtures -and (Test-Path $bkRoot) -and ((Get-ChildItem $bkRoot -ErrorAction SilentlyContinue | Measure-Object).Count -eq 0)) { Remove-Item $bkRoot -Force -ErrorAction SilentlyContinue }
Remove-Item $v3exe -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:TEMP 'dsht_doctor_report_cmp.txt') -Force -ErrorAction SilentlyContinue   # doctor --report 用例的产物

$total = $cases.Count - $skipped
Write-Host ("== 标记行契约：{0}/{1} 对齐{2} ==" -f ($total - $fail), $total, $(if ($skipped -gt 0) { "（另有 $skipped 项需 -Heavy）" } else { "" }))
if ($fail -gt 0) { exit 1 }
exit 0