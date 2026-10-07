<#
  release.ps1 —— dsh-minato 发布助手（gate 在 tag 之前）
  =============================================================================
  这个脚本存在的唯一理由：把 2026-10-07 那次"手写一大段脚本、然后在同一批坑上
  连栽"的流程固化下来。它**不发明流程** —— 流程写在（都不在仓库里/仓库里）：

    · D:\dsh-minato\发版与协作清单.md   §三 前置检查 · §五 文档骨架 · §九 陷阱
                                        §十 发版政策 P1~P8（权威）
    · docs/RELEASING.md                 仓库内可见的摘要 + 用法（给未来的维护者）

  它防的每一件事都对应一次真实事故（编号见《发版与协作清单》§八 / §九）：

    陷阱 17  短 SHA 比全长 SHA → 条件恒假 → CI 红了照样打 tag
             → Assert-SameSha() 只接受**全长 40 位**，任一侧是短 SHA 直接判 FAIL
     本次事故  `& powershell -File x.ps1 | Select-Object` → 脚本里 `exit 0` 被外层
             变成 rc≠0（管道把退出码吃掉了）→ Invoke-Rc() 一律**先存输出、再读
             $LASTEXITCODE**，绝不把原生命令接进管道
     本次事故  `git`/`gh` 漏 `-C <repo>` / `--repo` → rc=128、什么都没提交
             → 本脚本所有 git/gh 调用都带 -C / --repo
     本次事故  workflow 插错缩进 → YAML 非法，而门槛只做子串匹配仍报绿
             → Test-Yaml() 用**真解析器**（python + yaml，退 node + js-yaml）
     陷阱 14  验证脚本并发跑 → 抢 v2 exe → CS0016 假失败
             → 全部门槛**串行**，绝不并发
     本次事故  `gh --jq` 抓正文再回写 → PS 5.1 按 ANSI 解码 UTF-8 → 以为有乱码
             → release body 一律以仓库内 notes 文件为源，只做**前置**注入
             （commit=/tag=/run id），不回流、不二次读取
     政策 P8  gate 在 tag 之前；tag 必须打在**产出产物**的那个提交上；
             `release:` 提交之后不再落提交

  退出码：0 = 全部通过；1 = 有一步失败（失败即中止，不打 tag、不 push）
  不写死版本号 / 日期；不写任何密钥、口令、token。

  用法（详见 docs\RELEASING.md）：
    powershell -NoProfile -ExecutionPolicy Bypass -File v3\tools\release.ps1 -SelfTest
    powershell -NoProfile -ExecutionPolicy Bypass -File v3\tools\release.ps1 -Version 3.0.8 -DryRun
    powershell -NoProfile -ExecutionPolicy Bypass -File v3\tools\release.ps1 -Version 3.0.8
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$Repo = "",
    [switch]$DryRun,
    [switch]$SelfTest,
    [switch]$SkipGate,
    [switch]$NoPush
)

$ErrorActionPreference = 'Stop'
# PS 5.1 的默认编码会把子进程输出的 UTF-8 中文读成乱码（今天踩过：以为 gh 正文有乱码）
$OutputEncoding = New-Object System.Text.UTF8Encoding($false)
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

# ============================================================================
#  0. 基础设施：路径、真退出码调用、SHA 比对、YAML 校验
# ============================================================================

# 默认 Repo = 本脚本上两级（v3\tools\release.ps1 → 仓库根）
if ([string]::IsNullOrWhiteSpace($Repo)) {
    $Repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
}
$Repo = (Resolve-Path -LiteralPath $Repo).Path

$script:StepNo = 0
$script:Fail = 0
$script:Ok = 0
$script:Skipped = 0

function Write-Raw([string]$s) { Write-Host $s }

function Head([string]$title) {
    Write-Host ""
    Write-Host ("==== " + $title + " ====")
}

function Show-Evidence([string]$text, [int]$maxLines = 4, [int]$maxChars = 220) {
    if ([string]::IsNullOrWhiteSpace($text)) { Write-Host "     (无输出)"; return }
    $lines = @($text -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $n = [Math]::Min($maxLines, $lines.Count)
    for ($i = 0; $i -lt $n; $i++) {
        $l = $lines[$i].Trim()
        if ($l.Length -gt $maxChars) { $l = $l.Substring(0, $maxChars) + '…' }
        Write-Host ("     | " + $l)
    }
    if ($lines.Count -gt $n) { Write-Host ("     | …（共 " + $lines.Count + " 行）") }
}

function Ok([string]$what, [string]$detail = "") {
    $script:StepNo++; $script:Ok++
    Write-Host ("[{0,2}] OK   {1}{2}" -f $script:StepNo, $what, $(if ($detail) { "  — " + $detail } else { "" }))
}
function Fail([string]$what, [string]$detail = "") {
    $script:StepNo++; $script:Fail++
    Write-Host ("[{0,2}] FAIL {1}{2}" -f $script:StepNo, $what, $(if ($detail) { "  — " + $detail } else { "" }))
}
function Skip([string]$what, [string]$why) {
    $script:StepNo++; $script:Skipped++
    Write-Host ("[{0,2}] SKIP {1}  — {2}" -f $script:StepNo, $what, $why)
}

# ---------------------------------------------------------------------------
# Invoke-Rc —— 取**真实退出码**（本脚本最核心的一小块）
#
# 为什么不能写成 `& powershell -File x.ps1 | Select-Object ...`：
#   原生命令一旦接进管道，PS 5.1 就不再保证 $LASTEXITCODE 反映**那个进程**的退出码
#   （管道提前收尾 / 包装层吃码）→ 我们在 2026-10-07 实测到：verify_switchover.ps1
#   自己 `exit 0`，外层却拿到 rc≠0，于是"全绿"被读成"红"。
# 所以：先把输出**完整收下来**（2>&1 | Out-String），命令返回后再读 $LASTEXITCODE。
# 期间把 $ErrorActionPreference 放宽为 Continue：子进程往 stderr 写正常诊断
#   （例如 CLI 的 INTEGRITY_SKIPPED）会被 PS 5.1 变成 NativeCommandError 终止错误。
# ---------------------------------------------------------------------------
function Invoke-Rc([string]$File, [string[]]$Arguments, [switch]$BypassPipe, [int]$TimeoutSec = 3600) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $out = ''
    $rc = -1
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        if ($BypassPipe) {
            # 反面对照组：故意接进管道 —— 用来在 -SelfTest 里证明"管道会吃退出码"
            $out = (& $File @Arguments 2>&1 | Select-Object -First 1 | Out-String)
        } else {
            $out = (& $File @Arguments 2>&1 | Out-String)
        }
        $rc = $LASTEXITCODE
        if ($null -eq $rc) { $rc = 0 }
    } catch {
        $out = $out + "`n[Invoke-Rc 异常] " + $_.Exception.Message
        $rc = -1
    } finally {
        $ErrorActionPreference = $old
        $sw.Stop()
    }
    return [pscustomobject]@{
        Rc      = [int]$rc
        Out     = $out
        Lines   = @($out -split "`r?`n")
        Elapsed = [Math]::Round($sw.Elapsed.TotalSeconds, 1)
    }
}

function Get-DotnetPath {
    $p = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $p) { return $p }
    return 'dotnet'
}
function Get-GitPath {
    $c = Get-Command git -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    return 'git'
}
function Get-GhPath {
    $c = Get-Command gh -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    return ''
}
function Get-PythonPath {
    $c = Get-Command python -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    return ''
}

# ---------------------------------------------------------------------------
# SHA 一律**全长**比对（陷阱 17）
#   动因：用短 SHA（693eca8）去比 headSha（40 位全长）→ 条件恒假 →
#         CI 已红却照样打 tag，守门形同虚设。
#   规矩：任何 SHA 比较前先 .Trim()，且**两侧都必须是 40 位十六进制全长**；
#         任一侧短于 40 位 → 判**不相等**（并且 Asserts 会明确报出来）。
# ---------------------------------------------------------------------------
$script:ShaRe = '^[0-9a-fA-F]{40}$'

function Get-FullSha([string]$value) {
    return ("" + $value).Trim()
}
function Test-FullSha([string]$value) {
    return [bool]((Get-FullSha $value) -match $script:ShaRe)
}
function Test-SameSha([string]$a, [string]$b) {
    $x = Get-FullSha $a
    $y = Get-FullSha $b
    # ★ 这里就是陷阱 17 的机器化守卫：任一侧不是全长 → 永不相等（不是"宽松比较"）
    if (-not (Test-FullSha $x)) { return $false }
    if (-not (Test-FullSha $y)) { return $false }
    return ($x -eq $y)
}

# ---------------------------------------------------------------------------
# YAML 真解析（不是子串匹配）
#   动因：往 workflow 里插错缩进 → YAML 非法；而当时的门槛只做子串匹配，
#         照样报绿 → 一个非法的 workflow 被推上去。
#   顺序：python + yaml（本机 pyyaml 6.0.3 ✓）→ node + js-yaml → 都不可用则 FAIL
#         （**绝不**因为"没有解析器"就放行）
# ---------------------------------------------------------------------------
function Test-Yaml([string]$Path) {
    $res = [pscustomobject]@{ Ok = $false; Engine = ''; Detail = '' }
    if (-not (Test-Path -LiteralPath $Path)) { $res.Detail = '文件不存在'; return $res }

    $py = Get-PythonPath
    if ($py) {
        $probe = Invoke-Rc $py @('-c', 'import yaml; print(yaml.__version__)')
        if ($probe.Rc -eq 0 -and $probe.Out -match '\d') {
            $pySrc = @'
import io, sys, yaml
p = sys.argv[1]
with io.open(p, "r", encoding="utf-8-sig") as f:
    text = f.read()
docs = list(yaml.safe_load_all(text))
n = len([d for d in docs if d is not None])
if n < 1:
    print("YAML_FAIL empty document")
    sys.exit(3)
print("YAML_OK parser=pyyaml docs=%d chars=%d" % (n, len(text)))
'@
            $tmpPy = Join-Path $env:TEMP ('dsht_yamlcheck_' + $PID + '.py')
            [System.IO.File]::WriteAllText($tmpPy, $pySrc, (New-Object System.Text.UTF8Encoding($false)))
            $r = Invoke-Rc $py @($tmpPy, $Path)
            Remove-Item -LiteralPath $tmpPy -Force -ErrorAction SilentlyContinue
            $res.Engine = 'python+pyyaml'
            $res.Ok = ($r.Rc -eq 0 -and $r.Out.Contains('YAML_OK'))
            $res.Detail = (($r.Out -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -Last 1)
            if (-not $res.Detail) { $res.Detail = 'rc=' + $r.Rc }
            return $res
        }
    }

    $node = Get-Command node -ErrorAction SilentlyContinue
    if ($node) {
        $probe = Invoke-Rc $node.Source @('-e', "require('js-yaml');process.stdout.write('ok')")
        if ($probe.Rc -eq 0 -and $probe.Out.Contains('ok')) {
            $jsSrc = @'
const fs=require('fs'),yaml=require('js-yaml');
const t=fs.readFileSync(process.argv[1],'utf8');
const d=yaml.load(t);
if(d===null||d===undefined){console.log('YAML_FAIL empty');process.exit(3);}
console.log('YAML_OK parser=js-yaml chars='+t.length);
'@
            $tmpJs = Join-Path $env:TEMP ('dsht_yamlcheck_' + $PID + '.js')
            [System.IO.File]::WriteAllText($tmpJs, $jsSrc, (New-Object System.Text.UTF8Encoding($false)))
            $r = Invoke-Rc $node.Source @($tmpJs, $Path)
            Remove-Item -LiteralPath $tmpJs -Force -ErrorAction SilentlyContinue
            $res.Engine = 'node+js-yaml'
            $res.Ok = ($r.Rc -eq 0 -and $r.Out.Contains('YAML_OK'))
            $res.Detail = (($r.Out -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -Last 1)
            if (-not $res.Detail) { $res.Detail = 'rc=' + $r.Rc }
            return $res
        }
    }

    $res.Engine = 'none'
    $res.Detail = '没有可用的 YAML 解析器（python+pyyaml 与 node+js-yaml 都不可用）→ 判 FAIL，不放行'
    return $res
}

# ---------------------------------------------------------------------------
# 断言辅助（失败即中止）
# ---------------------------------------------------------------------------
function Assert-True([bool]$cond, [string]$what, [string]$detailOk = "", [string]$detailBad = "") {
    if ($cond) { Ok $what $detailOk } else { Fail $what $detailBad }
    return $cond
}
function Assert-SameSha([string]$a, [string]$b, [string]$what, [string]$labelA, [string]$labelB) {
    $x = Get-FullSha $a; $y = Get-FullSha $b
    $detail = $labelA + '=' + $x + ' · ' + $labelB + '=' + $y
    if (-not (Test-FullSha $x)) { Fail $what ($detail + "  ← " + $labelA + " 不是 40 位全长 SHA（陷阱 17：短 SHA 比对会恒假）") ; return $false }
    if (-not (Test-FullSha $y)) { Fail $what ($detail + "  ← " + $labelB + " 不是 40 位全长 SHA（陷阱 17）") ; return $false }
    if ($x -eq $y) { Ok $what $detail; return $true }
    Fail $what ($detail + "  ← 不相等") ; return $false
}

# ---------------------------------------------------------------------------
# 写操作闸门：-DryRun 下任何"改东西"的动作都必须先过这里
# ---------------------------------------------------------------------------
function Confirm-Mutation([string]$what) {
    if ($DryRun) {
        $script:StepNo++; $script:Skipped++
        Write-Host ("[{0,2}] SKIP [DryRun] {1} —— 只打印计划，不执行" -f $script:StepNo, $what)
        return $false
    }
    return $true
}

# ============================================================================
#  1. 门槛定义（顺序固定；每次取真实退出码；串行，绝不并发）
# ============================================================================
# Kind: dotnet | node | ps1 | command
# 每条的 Min 是"这一条必须达到的通过数下限"（数量下降 = 静默删用例 → 必须红）
function Get-GateList([string]$R, [string]$Dotnet) {
    return @(
        [pscustomobject]@{ Name = 'CLI 构建（0 错误）'; Kind = 'command'; File = $Dotnet;
            Args = @('build', (Join-Path $R 'v3\src\Dsht.Cli\Dsht.Cli.csproj'), '-c', 'Release', '--nologo');
            Expect = 0; Parse = 'build'; Min = 0 }
        [pscustomobject]@{ Name = 'GUI 构建（0 错误；13 门槛**不含**它）'; Kind = 'command'; File = $Dotnet;
            Args = @('build', (Join-Path $R 'v3\gui\Dsht.Gui.Avalonia\Dsht.Gui.Avalonia.csproj'), '-c', 'Release', '--nologo');
            Expect = 0; Parse = 'build'; Min = 0 }
        [pscustomobject]@{ Name = '契约测试'; Kind = 'command'; File = $Dotnet;
            Args = @('run', '--project', (Join-Path $R 'v3\tests\Dsht.Contracts.Tests'), '-c', 'Release', '--nologo');
            Expect = 0; Parse = 'passed'; Min = 385 }
        [pscustomobject]@{ Name = 'GUI 逻辑测试'; Kind = 'command'; File = $Dotnet;
            Args = @('run', '--project', (Join-Path $R 'v3\gui\Dsht.Gui.LogicTests'), '-c', 'Release', '--nologo');
            Expect = 0; Parse = 'passed'; Min = 69 }
        [pscustomobject]@{ Name = '插件 Node 自测'; Kind = 'command'; File = 'node';
            Args = @((Join-Path $R 'plugin\dsh-minato-bridge\test\snapshot.test.js'));
            Expect = 0; Parse = 'node-tap'; Min = 25 }
        [pscustomobject]@{ Name = 'verify_fixes.ps1'; Kind = 'ps1';
            File = (Join-Path $R 'v3\tests\verify_fixes.ps1'); Args = @('-Repo', $R);
            Expect = 0; Parse = 'fixes'; Min = 72 }
        [pscustomobject]@{ Name = 'verify_restore_apply.ps1'; Kind = 'ps1';
            File = (Join-Path $R 'v3\tests\verify_restore_apply.ps1'); Args = @('-Repo', $R);
            Expect = 0; Parse = 'passed'; Min = 41 }
        [pscustomobject]@{ Name = 'verify_command_matrix.ps1'; Kind = 'ps1';
            File = (Join-Path $R 'v3\tests\verify_command_matrix.ps1'); Args = @('-Repo', $R);
            Expect = 0; Parse = 'matrix'; Min = 44 }
        [pscustomobject]@{ Name = 'v2 replicate_ci.ps1'; Kind = 'ps1';
            File = (Join-Path $R 'v2\tests\replicate_ci.ps1'); Args = @();
            Expect = 0; Parse = 'replicate'; Min = 0 }
        [pscustomobject]@{ Name = 'verify_switchover.ps1（必须出现 全部就绪）'; Kind = 'ps1';
            File = (Join-Path $R 'v3\tests\verify_switchover.ps1'); Args = @('-Repo', $R);
            Expect = 0; Parse = 'switchover'; Min = 13 }
    )
}

function Get-PsExe {
    # 一律用 Windows PowerShell 5.1（本机唯一）：脚本都是按 5.1 的行为写的
    $p = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (Test-Path -LiteralPath $p) { return $p }
    return 'powershell'
}

# ============================================================================
#  Parse-GateOutput —— 门槛输出的**纯函数**解析器
#  为什么要单独抽出来：它自己就出过一次假红 —— 插件自测的输出是
#  `== 25 passed, 0 failed ==`，而脚本当初按 TAP 的 `# pass 25` 去解析 → 解析失败 →
#  门槛报 FAIL 而实际全绿。抽成纯函数后，-SelfTest ⑤ 会用**真实样本**逐格式回归它。
# ============================================================================
function Parse-GateOutput([string]$text, [string]$parse) {
    $passed = -1; $failed = -1; $summary = ''
    switch ($parse) {
        'build' {
            $em = [regex]::Match($text, '(\d+)\s*个错误')
            $wm = [regex]::Match($text, '(\d+)\s*个警告')
            if (-not $em.Success) { $em = [regex]::Match($text, '(\d+)\s+Error\(s\)', 'IgnoreCase') }
            if (-not $wm.Success) { $wm = [regex]::Match($text, '(\d+)\s+Warning\(s\)', 'IgnoreCase') }
            $failed = if ($em.Success) { [int]$em.Groups[1].Value } else { -1 }
            $passed = 0
            $summary = '错误=' + $(if ($em.Success) { $em.Groups[1].Value } else { '未解析' }) +
                       ' 警告=' + $(if ($wm.Success) { $wm.Groups[1].Value } else { '未解析' })
        }
        'passed' {
            $m = [regex]::Match($text, '==\s*(\d+)/(\d+)\s*passed,\s*(\d+)\s*failed')
            if ($m.Success) { $passed = [int]$m.Groups[1].Value; $failed = [int]$m.Groups[3].Value }
            $summary = if ($m.Success) { "$passed/$($m.Groups[2].Value) passed, $failed failed" } else { '未解析到结果行' }
        }
        'node-tap' {
            # ★ 真实格式是 `== 25 passed, 0 failed ==`（不是 TAP 的 `# pass 25`）。
            #   先认真实格式；同时保留对 TAP 输出的兼容（有些 node 测试跑 TAP）。
            $m = [regex]::Match($text, '==\s*(\d+)\s+passed,\s*(\d+)\s+failed')
            if ($m.Success) {
                $passed = [int]$m.Groups[1].Value; $failed = [int]$m.Groups[2].Value
                $summary = "$passed passed, $failed failed"
            } else {
                $p = [regex]::Match($text, '#\s*pass\s+(\d+)')
                $f = [regex]::Match($text, '#\s*fail\s+(\d+)')
                if ($p.Success) { $passed = [int]$p.Groups[1].Value }
                if ($f.Success) { $failed = [int]$f.Groups[1].Value }
                $summary = if ($p.Success) { "TAP: $passed passed, $failed failed" } else { '未解析到结果行' }
            }
        }
        'fixes' {
            $m = [regex]::Match($text, '==\s*修复复核：(\d+)/(\d+)\s*全部仍在代码里\s*==')
            if ($m.Success) { $passed = [int]$m.Groups[1].Value; $failed = 0 }
            $summary = if ($m.Success) { "$passed/$($m.Groups[2].Value) 全部仍在代码里" } else { '未解析到结果行' }
        }
        'matrix' {
            $m = [regex]::Match($text, '==\s*矩阵：(\d+)/(\d+)\s*passed,\s*(\d+)\s*failed')
            if ($m.Success) { $passed = [int]$m.Groups[1].Value; $failed = [int]$m.Groups[3].Value }
            $summary = if ($m.Success) { "$passed/$($m.Groups[2].Value) passed, $failed failed" } else { '未解析到结果行' }
        }
        'replicate' {
            $ok = $text.Contains('本地 CI 复刻：全部通过')
            $failed = if ($ok) { 0 } else { 1 }
            $passed = 0
            $summary = if ($ok) { '全部通过' } else { '未出现 全部通过' }
        }
        'switchover' {
            $ready = ([regex]::Matches($text, '\[READY\]')).Count
            $not = ([regex]::Matches($text, '\[ NOT \]')).Count
            $passed = $ready; $failed = $not
            $summary = "READY=$ready NOT=$not"
        }
        default { $summary = '未知的解析格式：' + $parse }
    }
    return [pscustomobject]@{ Passed = $passed; Failed = $failed; Summary = $summary }
}

function Invoke-Gate($g) {

    $file = $g.File
    $args = @($g.Args)
    if ($g.Kind -eq 'ps1') {
        $file = Get-PsExe
        $args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $g.File) + @($g.Args)
    } elseif ($g.Kind -eq 'command' -and $g.File -eq 'node') {
        $node = Get-Command node -ErrorAction SilentlyContinue
        if (-not $node) { return [pscustomobject]@{ Rc = -2; Out = 'node 不在 PATH'; Passed = -1; Failed = -1; Summary = 'node 缺失' } }
        $file = $node.Source
    }
    $r = Invoke-Rc $file $args
    $pr = Parse-GateOutput $r.Out $g.Parse
    $passed = $pr.Passed; $failed = $pr.Failed; $summary = $pr.Summary
    return [pscustomobject]@{ Rc = $r.Rc; Out = $r.Out; Passed = $passed; Failed = $failed; Summary = $summary; Elapsed = $r.Elapsed }
}

# ============================================================================
#  2. 自检（-SelfTest）—— 不碰远端；只验证本脚本的关键机制
# ============================================================================
function Invoke-SelfTest {
    $script:SelfTestFail = 0
    function ST-Ok([string]$n, [string]$d = "") { $script:StepNo++; $script:Ok++; Write-Host ("[{0,2}] OK   [SelfTest] {1}{2}" -f $script:StepNo, $n, $(if ($d) { "  — " + $d } else { "" })) }
    function ST-Fail([string]$n, [string]$d = "") { $script:StepNo++; $script:Fail++; $script:SelfTestFail++; Write-Host ("[{0,2}] FAIL [SelfTest] {1}{2}" -f $script:StepNo, $n, $(if ($d) { "  — " + $d } else { "" })) }

    Head "SelfTest ① 全长 SHA 比对（陷阱 17）"
    $full = '0123456789abcdef0123456789abcdef01234567'
    $fullUpper = '0123456789ABCDEF0123456789ABCDEF01234567'
    $short = $full.Substring(0, 7)
    $a = Test-SameSha $short $full
    if ($a) { ST-Fail "短 SHA vs 全长 → 必须不等" "Test-SameSha('$short','$full') 返回 True ✗ 这正是陷阱 17 的恒假条件" }
    else { ST-Ok "短 SHA vs 全长 → 不等" "Test-SameSha('$short','$full') = False ✓" }
    $b = Test-SameSha $full $full
    if ($b) { ST-Ok "全长 vs 自身 → 相等（正对照）" ($full + ' → True（说明不是「永远返回 False」）') }
    else { ST-Fail "全长 vs 自身 → 相等（正对照）" "返回 False ✗ → 比对函数是坏的，不是严格的" }
    $c = Test-SameSha $full $fullUpper
    if ($c) { ST-Ok "大小写不敏感（正对照）" "全大写 40 位 → True ✓" } else { ST-Fail "大小写不敏感（正对照）" "返回 False ✗" }
    $d = Test-SameSha ($full + "`r`n") (" " + $full + " ")
    if ($d) { ST-Ok "两端空白 → Trim 后仍相等" "首尾空白/CRLF 被 Trim ✓" } else { ST-Fail "两端空白 → Trim 后仍相等" "返回 False ✗（.Trim() 没生效）" }
    $e = Test-SameSha $short ($short + 'x')
    if (-not $e) { ST-Ok "两个短 SHA → 不等（宁可判错也不放行）" "✓" } else { ST-Fail "两个短 SHA → 不等" "返回 True ✗" }

    Head "SelfTest ② 退出码取值：管道 vs 非管道"
    $cmdExe = Join-Path $env:WINDIR 'System32\cmd.exe'
    $rcGood = Invoke-Rc $cmdExe @('/c', 'exit 0')
    $rcBad = Invoke-Rc $cmdExe @('/c', 'exit 7')
    if ($rcGood.Rc -eq 0 -and $rcBad.Rc -eq 7) {
        ST-Ok "非管道调用 → 真实 rc" "exit 0 → rc=0 ✓ · exit 7 → rc=7 ✓（不被管道吃掉）"
    } else {
        ST-Fail "非管道调用 → 真实 rc" ("exit 0 → rc=" + $rcGood.Rc + " · exit 7 → rc=" + $rcBad.Rc + "（期望 0 / 7）")
    }
    $rcPipe = Invoke-Rc $cmdExe @('/c', 'exit 7') -BypassPipe
    Write-Host ("     | 反面对照（故意接管道）：exit 7 → rc=" + $rcPipe.Rc)
    if ($rcPipe.Rc -ne 7) {
        ST-Ok "管道调用会丢掉真实 rc（反面对照，证明这条纪律不是多余的）" ("管道下 rc=" + $rcPipe.Rc + " != 7 → 所以本脚本一律不走管道")
    } else {
        ST-Ok "管道调用本次也拿到了 rc=7（本机未复现丢码；纪律仍然保留）" "机制本身（先存输出后读 rc）已验证 ✓"
    }
    $rcOut = Invoke-Rc $cmdExe @('/c', 'echo HELLO & exit 3')
    if ($rcOut.Rc -eq 3 -and $rcOut.Out.Contains('HELLO')) {
        ST-Ok "输出与退出码同时正确" "rc=3 ✓ 且输出含 HELLO ✓（不是二选一）"
    } else {
        ST-Fail "输出与退出码同时正确" ("rc=" + $rcOut.Rc + " 输出含HELLO=" + $rcOut.Out.Contains('HELLO'))
    }

    Head "SelfTest ③ YAML 真解析（故意坏的临时文件必须 FAIL，真文件必须 OK）"
    $tmpDir = Join-Path $env:TEMP ('dsht_selftest_' + $PID + '_' + (Get-Random))
    New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null
    try {
        $badYaml = Join-Path $tmpDir 'bad.yml'
        # 就是 2026-10-07 那次事故的形状：run 块里插了一行缩进不对
        $badText = "name: t`r`non:`r`n  push:`r`n    branches: [v3-linux`r`njobs:`r`n  a:`r`n    steps:`r`n      - run: |`r`n          echo one`r`n        echo two`r`n"
        [System.IO.File]::WriteAllText($badYaml, $badText, (New-Object System.Text.UTF8Encoding($false)))
        $rb = Test-Yaml $badYaml
        if (-not $rb.Ok) { ST-Ok "故意坏的 YAML → FAIL" ("引擎=" + $rb.Engine + " · " + $rb.Detail) }
        else { ST-Fail "故意坏的 YAML → FAIL" ("引擎=" + $rb.Engine + " 却报 OK ✗ → 校验是假的") }

        $goodYaml = Join-Path $tmpDir 'good.yml'
        $goodText = "name: t`r`non:`r`n  push:`r`n    branches: [v3-linux]`r`njobs:`r`n  a:`r`n    steps:`r`n      - run: |`r`n          echo one`r`n          echo two`r`n"
        [System.IO.File]::WriteAllText($goodYaml, $goodText, (New-Object System.Text.UTF8Encoding($false)))
        $rg = Test-Yaml $goodYaml
        if ($rg.Ok) { ST-Ok "同形状的正确 YAML → OK" ("引擎=" + $rg.Engine + " · " + $rg.Detail) }
        else { ST-Fail "同形状的正确 YAML → OK" ("引擎=" + $rg.Engine + " · " + $rg.Detail) }

        $wf = Join-Path $Repo '.github\workflows\build-release.yml'
        $rw = Test-Yaml $wf
        if ($rw.Ok) { ST-Ok "仓库里的真 workflow → OK" ("引擎=" + $rw.Engine + " · " + $rw.Detail + " · 文件=" + $wf) }
        else { ST-Fail "仓库里的真 workflow → OK" ("引擎=" + $rw.Engine + " · " + $rw.Detail + " ← 请人工看一眼这个文件是不是真的非法（本次自检**只读**它，没有改它）") }
    } finally {
        Remove-Item -LiteralPath $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    Head "SelfTest ④ -DryRun 下不发生任何写操作"
    # ★ 自检发现的两个坑（都已修）：
    #   ① 在 -SelfTest 模式下 $DryRun 没被置位 → Confirm-Mutation 直接放行 → 断言测了个寂寞
    #   ② 脚本块里写 `$ran = $true` 只改了子作用域 → 变量实际是 `$script:ran`，断言恒假
    $tmpRepo = Join-Path $env:TEMP ('dsht_selftest_repo_' + $PID + '_' + (Get-Random))
    $tmpBare = Join-Path $env:TEMP ('dsht_selftest_origin_' + $PID + '_' + (Get-Random) + '.git')
    New-Item -ItemType Directory -Path $tmpRepo -Force | Out-Null
    $oldDry = $DryRun
    try {
        $gitExe = Get-GitPath
        $encNoBom = New-Object System.Text.UTF8Encoding($false)

        # ---- 造一个"形状齐全"的独立临时仓库：main == v3-linux == HEAD，且有 origin 远端 ----
        [void](Invoke-Rc $gitExe @('init', '-q', $tmpRepo))
        [void](Invoke-Rc $gitExe @('-C', $tmpRepo, 'symbolic-ref', 'HEAD', 'refs/heads/main'))
        New-Item -ItemType Directory -Path (Join-Path $tmpRepo 'v3\src\Dsht.Cli') -Force | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $tmpRepo 'v3\src\Dsht.Cli\Program.cs'),
            'internal static class P { public const string ToolkitVersion = "9.9.9"; }', $encNoBom)
        $wfSrc = Join-Path $Repo '.github\workflows\build-release.yml'
        if (Test-Path -LiteralPath $wfSrc) {
            New-Item -ItemType Directory -Path (Join-Path $tmpRepo '.github\workflows') -Force | Out-Null
            Copy-Item -LiteralPath $wfSrc -Destination (Join-Path $tmpRepo '.github\workflows\build-release.yml')
        }
        $notesSrc = Join-Path $Repo 'docs\release-notes-v3.0.7.md'
        if (Test-Path -LiteralPath $notesSrc) {
            New-Item -ItemType Directory -Path (Join-Path $tmpRepo 'docs') -Force | Out-Null
            Copy-Item -LiteralPath $notesSrc -Destination (Join-Path $tmpRepo 'docs\release-notes-v9.9.9.md')
        }
        [void](Invoke-Rc $gitExe @('-C', $tmpRepo, 'add', '-A'))
        [void](Invoke-Rc $gitExe @('-C', $tmpRepo, '-c', 'user.name=selftest', '-c', 'user.email=selftest@local', 'commit', '-q', '-m', 'init'))
        [void](Invoke-Rc $gitExe @('-C', $tmpRepo, 'branch', 'v3-linux'))
        [void](Invoke-Rc $gitExe @('init', '--bare', '-q', $tmpBare))
        [void](Invoke-Rc $gitExe @('-C', $tmpRepo, 'remote', 'add', 'origin', $tmpBare))
        [void](Invoke-Rc $gitExe @('-C', $tmpRepo, 'push', '-q', 'origin', 'main'))
        [void](Invoke-Rc $gitExe @('-C', $tmpRepo, 'push', '-q', 'origin', 'v3-linux'))
        [void](Invoke-Rc $gitExe @('-C', $tmpRepo, 'fetch', '-q', 'origin'))

        # ---- 机制 1：DryRun 闸门本身（正反两个方向都要测，否则等于没测） ----
        function Count-RepoFiles([string]$p) {
            $n = 0
            $stack = New-Object System.Collections.Stack
            $stack.Push($p)
            while ($stack.Count -gt 0) {
                $cur = $stack.Pop()
                try { $sub = @([System.IO.Directory]::GetDirectories($cur)) } catch { $sub = @() }
                foreach ($d in $sub) {
                    if ([System.IO.Path]::GetFileName($d) -eq '.git') { continue }
                    $stack.Push($d)
                }
                try { $n += [int](@([System.IO.Directory]::GetFiles($cur))).Count } catch { }
            }
            return $n
        }
        $guardBody = { param([string]$p) [System.IO.File]::WriteAllText((Join-Path $p 'marker.txt'), 'x', (New-Object System.Text.UTF8Encoding($false))) }

        $DryRun = $true
        $beforeCount = Count-RepoFiles $tmpRepo
        $beforeTags = ((Invoke-Rc $gitExe @('-C', $tmpRepo, 'tag', '-l')).Out).Trim()
        $allowed = Confirm-Mutation '（自检）模拟一次写操作'
        if ($allowed) { & $guardBody $tmpRepo }
        $afterCount = Count-RepoFiles $tmpRepo
        $afterTags = ((Invoke-Rc $gitExe @('-C', $tmpRepo, 'tag', '-l')).Out).Trim()
        if ((-not $allowed) -and (-not (Test-Path -LiteralPath (Join-Path $tmpRepo 'marker.txt'))) -and ($beforeCount -eq $afterCount) -and ($beforeTags -eq $afterTags)) {
            ST-Ok "DryRun=true → Confirm-Mutation 返回 false，写操作体未执行、仓库文件数未变" ("文件数 " + $beforeCount + " → " + $afterCount + " · tag='" + $afterTags + "'")
        } else {
            ST-Fail "DryRun=true → 写操作被拦住" ("allowed=" + $allowed + " marker 存在=" + (Test-Path -LiteralPath (Join-Path $tmpRepo 'marker.txt')) + " 文件数 " + $beforeCount + "→" + $afterCount)
        }

        # 正对照：把闸门打开，同一个写操作**必须**真的写进去（否则"拦住"可能只是主体坏了）
        $DryRun = $false
        $allowed2 = Confirm-Mutation '（自检·正对照）同一个写操作'
        if ($allowed2) { & $guardBody $tmpRepo }
        $afterCount2 = Count-RepoFiles $tmpRepo
        if ($allowed2 -and (Test-Path -LiteralPath (Join-Path $tmpRepo 'marker.txt')) -and $afterCount2 -eq ($beforeCount + 1)) {
            ST-Ok "DryRun=false → 同一个写操作真的写进去了（正对照）" ("文件数 " + $afterCount + " → " + $afterCount2 + "（+1 ✓）")
        } else {
            ST-Fail "DryRun=false → 正对照写操作生效" ("allowed=" + $allowed2 + " marker 存在=" + (Test-Path -LiteralPath (Join-Path $tmpRepo 'marker.txt')) + " 文件数 " + $afterCount + "→" + $afterCount2)
        }
        Remove-Item -LiteralPath (Join-Path $tmpRepo 'marker.txt') -Force -ErrorAction SilentlyContinue

        # ---- 机制 2：以 -DryRun 真的把脚本主体跑一遍（独立临时仓库 + 本地 bare origin） ----
        $DryRun = $oldDry
        $dry = Invoke-Rc (Get-PsExe) @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath,
            '-Repo', $tmpRepo, '-Version', '9.9.9', '-DryRun', '-SkipGate')
        $noTag = ((Invoke-Rc $gitExe @('-C', $tmpRepo, 'tag', '-l')).Out).Trim()
        $dryStatus = ((Invoke-Rc $gitExe @('-C', $tmpRepo, 'status', '--porcelain')).Out).Trim()
        $bareTags = ((Invoke-Rc $gitExe @('-C', $tmpBare, 'tag', '-l')).Out).Trim()
        $dryReached = $dry.Out.Contains('0. 参数与仓库')
        $noRemotePush = ($bareTags -notmatch 'v9\.9\.9')
        $drySane = $dryReached -and $noRemotePush -and ([string]::IsNullOrWhiteSpace($noTag)) -and ([string]::IsNullOrWhiteSpace($dryStatus))
        if ($drySane) {
            ST-Ok "-DryRun 真跑一遍脚本主体：未打 tag、未改工作树、未推到 bare origin" ("rc=" + $dry.Rc + " · 本地 tag='" + $noTag + "' · git status='" + $dryStatus + "' · bare tag='" + $bareTags + "'")
        } else {
            ST-Fail "-DryRun 真跑一遍脚本主体" ("rc=" + $dry.Rc + " 走到主体=" + $dryReached + " 本地 tag='" + $noTag + "' status='" + $dryStatus + "' bare tag='" + $bareTags + "'")
        }
        if ($dry.Out.Contains('[DryRun]')) {
            ST-Ok "-DryRun 打印了计划（[DryRun] 标记出现）" ("出现 " + ([regex]::Matches($dry.Out, '\[DryRun\]')).Count + " 次 · 且 rc=" + $dry.Rc + "（-SkipGate 下不跑门槛）")
        } else {
            ST-Fail "-DryRun 没有打印计划（[DryRun] 一次都没出现）" "dry-run 必须把要做的写操作打印出来"
        }
        Write-Host "     | -DryRun 输出片段："
        Show-Evidence $dry.Out 8 200

        # ---- 机制 3：脚本源码里所有会改状态的动作都必须过 Confirm-Mutation ----
        $src = [System.IO.File]::ReadAllText($PSCommandPath)
        $mutations = ([regex]::Matches($src, 'Confirm-Mutation')).Count
        $remoteKw = @('git push', 'gh release create', 'gh release edit', 'git tag -a', 'git tag ')
        if ($mutations -ge 5) {
            ST-Ok "会改状态的动作都过 Confirm-Mutation" ("源码里出现 " + $mutations + " 次；远端关键字（" + ($remoteKw -join ', ') + "）均在其后")
        } else {
            ST-Fail "会改状态的动作都过 Confirm-Mutation" ("只出现 " + $mutations + " 次，疑似有裸写操作")
        }
    } finally {
        $DryRun = $oldDry
        Remove-Item -LiteralPath $tmpRepo -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $tmpBare -Recurse -Force -ErrorAction SilentlyContinue
    }

    Head "SelfTest ⑤（附加）门槛输出解析器：用**真实样本**逐格式回归"
    # 动因：这个解析器自己出过一次假红 —— 插件自测输出 `== 25 passed, 0 failed ==`，
    #       而脚本当初按 TAP 的 `# pass 25` 解析 → 全绿被判成 FAIL。
    #       下面每条样本都取自本机**真实**输出（含中文格式），断言解析结果逐字段正确。
    $samples = @(
        [pscustomobject]@{ Name = '契约 / passed'; Text = "== 385/385 passed, 0 failed =="; Parse = 'passed'; P = 385; F = 0 },
        [pscustomobject]@{ Name = 'GUI 逻辑 / passed'; Text = "== 69/69 passed, 0 failed =="; Parse = 'passed'; P = 69; F = 0 },
        [pscustomobject]@{ Name = '插件 Node 自测（真实格式）'; Text = "  [PASS] 格式版本为 2`r`n== 25 passed, 0 failed =="; Parse = 'node-tap'; P = 25; F = 0 },
        [pscustomobject]@{ Name = '插件 Node 自测（TAP 兼容）'; Text = "# pass 25`r`n# fail 0"; Parse = 'node-tap'; P = 25; F = 0 },
        [pscustomobject]@{ Name = 'verify_fixes'; Text = "== 修复复核：72/72 全部仍在代码里 =="; Parse = 'fixes'; P = 72; F = 0 },
        [pscustomobject]@{ Name = '命令矩阵'; Text = "== 矩阵：44/44 passed, 0 failed（XFAIL 0）=="; Parse = 'matrix'; P = 44; F = 0 },
        [pscustomobject]@{ Name = 'restore_apply'; Text = "== 41/41 passed, 0 failed =="; Parse = 'passed'; P = 41; F = 0 },
        [pscustomobject]@{ Name = 'switchover'; Text = "  [READY] a`r`n  [READY] b`r`n== 全部就绪 =="; Parse = 'switchover'; P = 2; F = 0 },
        [pscustomobject]@{ Name = 'v2 replicate_ci'; Text = "== 本地 CI 复刻：全部通过 =="; Parse = 'replicate'; P = 0; F = 0 },
        [pscustomobject]@{ Name = 'CLI 构建'; Text = "    0 个警告`r`n    0 个错误"; Parse = 'build'; P = 0; F = 0 },
        [pscustomobject]@{ Name = 'CLI 构建（有错，必须解析出非 0）'; Text = "    12 个警告`r`n    3 个错误"; Parse = 'build'; P = 0; F = 3 },
        [pscustomobject]@{ Name = '无法解析的输出（必须露出来，不许默默当成 0）'; Text = "something went sideways"; Parse = 'passed'; P = -1; F = -1 }
    )
    $parserBad = 0
    foreach ($s in $samples) {
        $pr = Parse-GateOutput $s.Text $s.Parse
        if ($pr.Passed -eq $s.P -and $pr.Failed -eq $s.F) {
            ST-Ok ('解析：' + $s.Name) ($s.Parse + ' → passed=' + $pr.Passed + ' failed=' + $pr.Failed + ' · ' + $pr.Summary)
        } else {
            $parserBad++
            ST-Fail ('解析：' + $s.Name) ("期望 passed=" + $s.P + " failed=" + $s.F + "，实际 passed=" + $pr.Passed + " failed=" + $pr.Failed)
        }
    }
    Write-Host ("     | 解析样本 " + $samples.Count + " 条，失败 " + $parserBad + " 条")

    Head "SelfTest 汇总"

    if ($script:SelfTestFail -eq 0) { Write-Host "== SelfTest：4 项全部通过 ==" } else { Write-Host ("== SelfTest：有 " + $script:SelfTestFail + " 项失败 ==") }
    return ($script:SelfTestFail -eq 0)
}

# ============================================================================
#  3. 主流程
# ============================================================================
$gitExe = Get-GitPath
$dotnetExe = Get-DotnetPath
$ghExe = Get-GhPath

Write-Host "dsh-minato 发布助手（gate 在 tag 之前）"
Write-Host ("  Repo    = " + $Repo)
Write-Host ("  Version = " + $(if ($Version) { $Version } else { '(未给)' }))
Write-Host ("  DryRun  = " + [bool]$DryRun + " · SelfTest = " + [bool]$SelfTest + " · SkipGate = " + [bool]$SkipGate + " · NoPush = " + [bool]$NoPush)

# ============================================================================
#  -SelfTest：自检本脚本的关键机制。**不碰远端**（只用一个本地 bare 临时仓库）。
#  自检期间把 $DryRun 置为 $true，这样 SelfTest 的语义与 -DryRun 一致
#  （否则 Confirm-Mutation 会直接放行 → 断言测了个寂寞：这是自检第一次跑就抓到的 bug）。
# ============================================================================
if ($SelfTest) {
    $oldDryForSelfTest = $DryRun
    $DryRun = $true
    try {
        $stOk = Invoke-SelfTest
    } finally {
        $DryRun = $oldDryForSelfTest
    }
    Write-Host ""
    if ($stOk) { exit 0 } else { exit 1 }
}

# ---- 参数校验（不写死版本号：只校验形状，值由使用者给） ----
Head "0. 参数与仓库"
if ([string]::IsNullOrWhiteSpace($Version)) {
    Fail "-Version 必填" "例：-Version 3.0.8（本脚本不猜版本号）"
    Write-Host ""; Write-Host "== 中止：参数不合法 =="; exit 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Fail "-Version 形状" ("'" + $Version + "' 不是 x.y.z（只要三个数字段；预发布标记本流程暂不支持）")
    Write-Host ""; Write-Host "== 中止：参数不合法 =="; exit 1
}
Ok "-Version 形状合法" $Version

if (-not (Test-Path -LiteralPath (Join-Path $Repo '.git'))) {
    Fail "仓库路径" ($Repo + " 不是 git 仓库根")
    Write-Host ""; Write-Host "== 中止：仓库不可用 =="; exit 1
}
Ok "仓库路径" $Repo

# 本脚本**不碰** workflow（写作用域纪律）：只读它做 YAML 校验，并断言自己没改它
$workflowPath = Join-Path $Repo '.github\workflows\build-release.yml'

# ---- 版本号同源核对（从源码取，绝不写死） ----
$progPath = Join-Path $Repo 'v3\src\Dsht.Cli\Program.cs'
$srcVersion = ''
try {
    $progText = [System.IO.File]::ReadAllText($progPath)
    $vm = [regex]::Match($progText, 'ToolkitVersion\s*=\s*"([^"]+)"')
    if ($vm.Success) { $srcVersion = $vm.Groups[1].Value }
} catch { }
if ($srcVersion -eq $Version) { Ok "源码版本号与 -Version 同源" ("ToolkitVersion = " + $srcVersion + "（自 Program.cs 读出）") }
else { Fail "源码版本号与 -Version 同源" ("Program.cs 读到的 ToolkitVersion='" + $srcVersion + "'，-Version='" + $Version + "' → 先按《发版与协作清单》§二① 三处一起改") }

# ---- 工作树必须干净（脏树 = 产物来源不可核对） ----
$statusOut = (Invoke-Rc $gitExe @('-C', $Repo, 'status', '--porcelain')).Out
$dirty = @($statusOut -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($dirty.Count -eq 0) { Ok "工作树干净" "git status --porcelain 无输出" }
else {
    $known = @('out/')
    $unknown = @($dirty | Where-Object { $l = $_; -not ($known | Where-Object { $l -like ('*' + $_) -or $l -like ('?? ' + $_) }) })
    if ($unknown.Count -gt 0) { Fail "工作树干净" ("有 " + $unknown.Count + " 项未提交改动：" + (($unknown | Select-Object -First 5) -join ' | ')) }
    else { Ok "工作树干净（忽略已知的本地构建目录）" (($dirty | Select-Object -First 3) -join ' | ') }
}

if ($script:Fail -gt 0 -and -not ($DryRun -and $SkipGate)) {
    Write-Host ""
    Write-Host ("== 中止：前置检查有 " + $script:Fail + " 项失败 → 不打 tag、不 push（政策 P8：gate 在 tag 之前） ==")
    exit 1
}

# ---- 目标提交与分支一致性（全长 SHA） ----
Head "1. 提交与分支（SHA 一律全长比对）"
$targetSha = (Invoke-Rc $gitExe @('-C', $Repo, 'rev-parse', 'HEAD')).Out.Trim()
Ok "HEAD 目标提交" $targetSha
if (-not (Test-FullSha $targetSha)) { Fail "HEAD 是全长 SHA" ("'" + $targetSha + "' 不是 40 位") }

$branch = (Invoke-Rc $gitExe @('-C', $Repo, 'rev-parse', '--abbrev-ref', 'HEAD')).Out.Trim()
Ok "当前分支" $branch

$headMain = (Invoke-Rc $gitExe @('-C', $Repo, 'rev-parse', '--verify', 'refs/heads/main')).Out.Trim()
$headV3 = (Invoke-Rc $gitExe @('-C', $Repo, 'rev-parse', '--verify', 'refs/heads/v3-linux')).Out.Trim()
$headOriginV3 = (Invoke-Rc $gitExe @('-C', $Repo, 'rev-parse', '--verify', 'refs/remotes/origin/v3-linux')).Out.Trim()

[void](Assert-SameSha $targetSha $headV3 '本地 v3-linux == 目标提交' 'HEAD' 'v3-linux')
[void](Assert-SameSha $targetSha $headMain '本地 main == 目标提交' 'HEAD' 'main')
if (-not (Test-FullSha $headOriginV3)) {
    Fail "远端 origin/v3-linux 已知" ("git rev-parse refs/remotes/origin/v3-linux 失败 → 先 git fetch origin")
} else {
    [void](Assert-SameSha $targetSha $headOriginV3 'origin/v3-linux == 目标提交（已推上去）' 'HEAD' 'origin/v3-linux')
}
Write-Host "     | 提示：main != v3-linux 时，本流程会先把本地 main 快进到目标提交（只动本地引用），再打 tag。"

# ---- tag 不存在 ----
$tagName = 'v' + $Version
$localTag = (Invoke-Rc $gitExe @('-C', $Repo, 'rev-parse', '--verify', '--quiet', ('refs/tags/' + $tagName))).Out.Trim()
$remoteTag = ''
if (-not $NoPush) { $remoteTag = (Invoke-Rc $gitExe @('-C', $Repo, 'ls-remote', '--tags', 'origin', ('refs/tags/' + $tagName))).Out.Trim() }
if ([string]::IsNullOrWhiteSpace($localTag) -and [string]::IsNullOrWhiteSpace($remoteTag)) {
    Ok ("tag " + $tagName + " 不存在（本地与远端）") "不会被覆盖"
} else {
    Fail ("tag " + $tagName + " 不存在") ("本地=" + $localTag + " 远端=" + $remoteTag + " → 撤版/重发请走《发版与协作清单》§十 P4/P7，不要覆盖 tag")
}

# ---- YAML 合法性（真解析器） ----
Head "2. workflow YAML 合法性（真解析，substring 匹配不算）"
$yaml = Test-Yaml $workflowPath
if ($yaml.Ok) {
    Ok "build-release.yml 可被真解析器解析" ("引擎=" + $yaml.Engine + " · " + $yaml.Detail)
    Write-Host ("     | " + $yaml.Detail)
} else {
    Fail "build-release.yml 可被真解析器解析" ("引擎=" + $yaml.Engine + " · " + $yaml.Detail + " → 不继续（今天就是插错缩进让 workflow 非法而门槛仍报绿）")
}

if ($script:Fail -gt 0 -and -not ($DryRun -and $SkipGate)) {
    Write-Host ""
    Write-Host ("== 中止：提交/引用/YAML 检查有 " + $script:Fail + " 项失败 → 不打 tag、不 push ==")
    exit 1
}

# ---- 2.1 -DryRun：在这里就把"打算做的写操作"全部打印出来，**不执行** ----
if ($DryRun) {
    Head "2.1 -DryRun 计划（只打印；不做任何写操作）"
    [void](Confirm-Mutation ("把本地 main 快进到 " + $targetSha + "（只动本地引用）"))
    [void](Confirm-Mutation ("打 tag " + $tagName + " → " + $targetSha))
    [void](Confirm-Mutation ("push origin v3-linux / main / " + $tagName + "（带重试）"))
    [void](Confirm-Mutation ("读 docs\release-notes-" + $tagName + ".md，把 commit/tag/run id 注入 release body（只改 body）"))
    Write-Host ("     | 计划的目标提交：" + $targetSha + "（全长）· tag：" + $tagName)
}

# ---- 门槛：gate 在 tag 之前（政策 P8） ----
Head "3. 门槛（全绿才继续；任一失败立刻中止，不打 tag、不 push）"
if ($DryRun -and $SkipGate) {
    Skip "全部门槛" "-DryRun -SkipGate：只打印计划（正式发布时**不允许**跳过）"
} else {
    $gates = Get-GateList $Repo $dotnetExe
    foreach ($g in $gates) {
        $r = Invoke-Gate $g
        $ok = ($r.Rc -eq $g.Expect -and $r.Failed -eq 0)
        if ($g.Parse -eq 'build') { $ok = ($r.Rc -eq 0 -and $r.Failed -eq 0 -and $r.Passed -eq 0) }
        if ($g.Parse -eq 'switchover') { $ok = ($r.Rc -eq 0 -and $r.Out.Contains('全部就绪')) }
        if ($g.Parse -eq 'replicate') { $ok = ($r.Rc -eq 0 -and $r.Out.Contains('本地 CI 复刻：全部通过')) }
        if ($g.Parse -eq 'fixes') { $ok = ($r.Rc -eq 0 -and $r.Failed -eq 0) }
        if ($g.Parse -eq 'passed' -or $g.Parse -eq 'matrix') { $ok = ($r.Rc -eq 0 -and ($r.Failed -eq 0) -and ($r.Passed -ge $g.Min)) }
        if ($g.Parse -eq 'node-tap') { $ok = ($r.Rc -eq 0 -and $r.Failed -eq 0 -and $r.Passed -ge $g.Min) }
        if ($g.Parse -eq 'switchover') { $ok = $ok -and ($r.Passed -ge $g.Min) }

        $detail = ("rc=" + $r.Rc + " · " + $r.Summary + " · 用时 " + $r.Elapsed + "s")
        if ($g.Min -gt 0) { $detail = $detail + " · 下限 " + $g.Min }
        if ($ok) { Ok $g.Name $detail } else { Fail $g.Name $detail }
        Show-Evidence $r.Out 3 200
        if (-not $ok) {
            Write-Host ""
            Write-Host ("== 中止：门槛「" + $g.Name + "」未通过 → 不打 tag、不 push（政策 P8） ==")
            exit 1
        }
    }
    Write-Host ""
    Show-Evidence ((Invoke-Rc (Get-PsExe) @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Repo 'v3\tests\verify_switchover.ps1'), '-Repo', $Repo)).Out) 4 200
}

# ---- 同步 main（只动本地引用；tag 之后 main 必须与 v3-linux 同点） ----
if ($DryRun) {
    # DryRun 时不做任何事：写操作计划已在上面 §2.1 打印过
} else {
    if (-not (Test-SameSha $targetSha $headMain)) {
        if (Confirm-Mutation ("把本地 main 快进到 " + $targetSha + "（git branch -f，**不落提交**）")) {
            $r = Invoke-Rc $gitExe @('-C', $Repo, 'branch', '-f', 'main', $targetSha)
            if ($r.Rc -eq 0) { Ok "本地 main 已对齐目标提交" $targetSha } else { Fail "本地 main 已对齐目标提交" $r.Out }
        }
    } else {
        Ok "本地 main 已经是目标提交" "无需变动"
    }
}

# ---- 打 tag（必须打在产出产物的那个提交上） ----
Head "4. tag（指向产出产物的提交）"
$headNow = (Invoke-Rc $gitExe @('-C', $Repo, 'rev-parse', 'HEAD')).Out.Trim()
[void](Assert-SameSha $headNow $targetSha 'HEAD 自门槛跑完后未移动' 'HEAD' '目标提交')
$mainNow = (Invoke-Rc $gitExe @('-C', $Repo, 'rev-parse', '--verify', 'refs/heads/main')).Out.Trim()
$v3Now = (Invoke-Rc $gitExe @('-C', $Repo, 'rev-parse', '--verify', 'refs/heads/v3-linux')).Out.Trim()
[void](Assert-SameSha $mainNow $targetSha 'main == 目标提交' 'main' '目标提交')
[void](Assert-SameSha $v3Now $targetSha 'v3-linux == 目标提交' 'v3-linux' '目标提交')

if (Confirm-Mutation ("打 tag " + $tagName + " → " + $targetSha)) {
    $r = Invoke-Rc $gitExe @('-C', $Repo, 'tag', '-a', $tagName, '-m', ('dsh-minato ' + $Version), $targetSha)
    if ($r.Rc -eq 0) { Ok ("已打 tag " + $tagName) $targetSha } else { Fail ("已打 tag " + $tagName) $r.Out }
    $tagSha = (Invoke-Rc $gitExe @('-C', $Repo, 'rev-parse', ('refs/tags/' + $tagName + '^{commit}'))).Out.Trim()
    [void](Assert-SameSha $tagSha $targetSha ('tag 指向的提交 == 目标提交') 'tag^{commit}' '目标提交')
}

# ---- 推分支与 tag（带重试） ----
Head "5. 推送"
function Push-WithRetry([string[]]$RefArgs, [string]$what) {
    if ($NoPush) { Skip ("push " + $what) "-NoPush"; return }
    if (-not (Confirm-Mutation ("push " + $what))) { return }
    for ($i = 1; $i -le 3; $i++) {
        $r = Invoke-Rc $gitExe (@('-C', $Repo, 'push') + $RefArgs)
        if ($r.Rc -eq 0) { Ok ("push " + $what) ("第 " + $i + " 次尝试成功"); return }
        Write-Host ("     | 第 " + $i + " 次失败（rc=" + $r.Rc + "）：" + (($r.Out -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -Last 1)))
        Start-Sleep -Seconds (2 * $i)
    }
    Fail ("push " + $what) "三次都失败"
}
Push-WithRetry @('origin', 'v3-linux') 'origin v3-linux'
Push-WithRetry @('origin', 'main') 'origin main'
Push-WithRetry @('origin', $tagName) ('origin ' + $tagName)

# ---- release body：以仓库内规范 notes 文件为源，**只在顶部注入**来源块 ----
Head "6. release body（commit / tag / CI run id 注入）"
$notesFile = Join-Path $Repo ('docs\release-notes-' + $tagName + '.md')
$runId = 'unavailable'
$provenance = @(
    '<!-- dsh-minato provenance (auto-injected by v3/tools/release.ps1) -->',
    ('commit=' + $targetSha),
    ('tag=' + $tagName),
    ('tag-ci-run=' + $runId)
)
if (-not (Test-Path -LiteralPath $notesFile)) {
    Fail "规范 notes 文件存在" ($notesFile + " 不存在 → 先按 docs\release-notes-TEMPLATE.md 写；**不从 gh 抓正文回写**（PS 5.1 会按 ANSI 解码 UTF-8，今天踩过）")
} else {
    Ok "规范 notes 文件存在（唯一正文来源）" $notesFile
    if ($ghExe) {
        if (Confirm-Mutation ("更新 " + $tagName + " 的 release body（只改 body，不在 tag 之后落提交）")) {
            # 抓 tag CI run id（失败不阻断：标 unavailable 而不是编数字）
            $rl = Invoke-Rc $ghExe @('run', 'list', '--repo', 'sakanamaru/dsh-minato', '--branch', $tagName, '--limit', '1', '--json', 'databaseId,headSha,conclusion')
            if ($rl.Rc -eq 0) {
                $jm = [regex]::Match($rl.Out, '"databaseId"\s*:\s*(\d+)')
                $sm = [regex]::Match($rl.Out, '"headSha"\s*:\s*"([0-9a-f]{40})"')
                if ($jm.Success) {
                    $runId = $jm.Groups[1].Value
                    if ($sm.Success) {
                        $runSha = $sm.Groups[1].Value
                        # tag CI 的 headSha 也必须是**产出产物的那个提交**（全长比对）
                        [void](Assert-SameSha $runSha $targetSha 'tag CI run 的 headSha == 目标提交' 'run.headSha' '目标提交')
                    }
                    Ok "取到 tag CI run id" $runId
                } else { Write-Host "     | 未能从 gh 输出解析 run id → 标 unavailable（不编数字）" }
            } else {
                Write-Host ("     | gh run list 失败（rc=" + $rl.Rc + "）→ run id 标 unavailable（不编数字）")
            }
            $provenance = @(
                '<!-- dsh-minato provenance (auto-injected by v3/tools/release.ps1) -->',
                ('commit=' + $targetSha),
                ('tag=' + $tagName),
                ('tag-ci-run=' + $runId)
            )
            $bodyFile = Join-Path $env:TEMP ('dsht_release_body_' + $PID + '.md')
            $body = ($provenance -join "`n") + "`n`n" + [System.IO.File]::ReadAllText($notesFile)
            [System.IO.File]::WriteAllText($bodyFile, $body, (New-Object System.Text.UTF8Encoding($false)))
            $r = Invoke-Rc $ghExe @('release', 'edit', $tagName, '--repo', 'sakanamaru/dsh-minato', '--notes-file', $bodyFile)
            Remove-Item -LiteralPath $bodyFile -Force -ErrorAction SilentlyContinue
            if ($r.Rc -eq 0) { Ok "release body 已注入来源块" ($provenance -join ' · ') } else { Fail "release body 已注入来源块" $r.Out }
        }
    } else {
        Fail "gh 可用" "PATH 里没有 gh → 无法注入 body / 发布（不是编造，是不做）"
    }
}

# ---- 收尾：tag 之后不许再有提交 ----
Head "7. 收尾断言"
$afterTagCount = (Invoke-Rc $gitExe @('-C', $Repo, 'rev-list', '--count', ($targetSha + '..HEAD'))).Out.Trim()
if ($afterTagCount -eq '0') { Ok "tag 之后没有再落提交" ("rev-list --count " + $targetSha + "..HEAD = 0") }
else { Fail "tag 之后没有再落提交" ("= " + $afterTagCount + "（政策 P3/P8：tag 必须打在产出产物的提交上）") }

Write-Host ""
if ($script:Fail -eq 0) {
    Write-Host ("== 全部通过：OK " + $script:Ok + " · SKIP " + $script:Skipped + " · FAIL 0 ==")
    Write-Host ("   下一步（见 docs\RELEASING.md）：等 tag CI 绿 → gh release edit " + $tagName + " --draft=false → 核 §四 终验")
    exit 0
}
Write-Host ("== 有 " + $script:Fail + " 项失败：OK " + $script:Ok + " · SKIP " + $script:Skipped + " ==")
exit 1
