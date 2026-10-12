<#
  同步开发运行时：把 CLI 整套 + 随包插件目录 拷进 GUI 输出目录，然后重启界面。

  为什么需要它（陷阱 19）：GUI 的 CliPath() **优先用自己目录里的 CLI**，
  而 .exe 只是 apphost、真代码在同目录的 dsh-minato.dll / Dsht.*.dll 里 ——
  只换 exe 或忘了随包 plugin\ 目录，界面就会静默降级（"当前数据源未提供"/"工具箱自带基准不可用"）。
  本脚本 = 关界面 → 整套同步 → 校验同目录 CLI → 重启界面。

  用法：powershell -ExecutionPolicy Bypass -File v3\tools\sync-gui-dev.ps1
  安全：只动仓库 bin 输出目录里的文件；只关"路径就在该输出目录里"的 dsht-gui 进程；
        不碰 ~\.dsh，不碰 git，不 push。
#>
param(
  [string]$Repo = 'D:\dsh-minato\repo',
  [string]$Config = 'Release'
)
$ErrorActionPreference = 'Stop'
function Info([string]$m) { Write-Host ('[sync] ' + $m) }

$cliDir = Join-Path $Repo ('v3\src\Dsht.Cli\bin\' + $Config + '\net8.0')
$guiDir = Join-Path $Repo ('v3\gui\Dsht.Gui.Avalonia\bin\' + $Config + '\net8.0')
$guiExe = Join-Path $guiDir 'dsht-gui.exe'
if (-not (Test-Path $guiExe)) { throw ('GUI 未构建：' + $guiExe) }
if (-not (Test-Path (Join-Path $cliDir 'dsh-minato.dll'))) { throw ('CLI 未构建：' + $cliDir) }

# 1) 只关"路径正好等于该输出目录里的 dsht-gui.exe"的进程（不误伤别处启动的界面）
$running = @(Get-Process -Name 'dsht-gui' -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -eq $guiExe } catch { $false } })
if ($running.Count -gt 0) {
  Info ('关掉正在运行的界面 PID=' + (($running | ForEach-Object { $_.Id }) -join ','))
  $running | Stop-Process -Force
  Start-Sleep -Seconds 2
}

# 2) CLI 运行时整套（7 个文件）—— 必须整套一起换
$files = @('dsh-minato.dll', 'dsh-minato.exe', 'Dsht.Domain.dll', 'Dsht.Platform.Windows.dll', 'Dsht.Platform.Linux.dll', 'dsh-minato.deps.json', 'dsh-minato.runtimeconfig.json')
$synced = 0
foreach ($f in $files) {
  $s = Join-Path $cliDir $f
  if (-not (Test-Path $s)) { continue }
  $t = Join-Path $guiDir $f
  $same = (Test-Path $t) -and ((Get-FileHash $s).Hash -eq (Get-FileHash $t).Hash)
  if (-not $same) { Copy-Item $s $t -Force; $synced++; Info ('已同步 ' + $f) }
}

# 3) 随包插件目录 —— GUI 里的 CLI 以 <exe 目录>\plugin\ 作为"工具箱自带"基准
$ps = Join-Path $Repo 'plugin\dsh-minato-bridge'
$pt = Join-Path $guiDir 'plugin\dsh-minato-bridge'
if (Test-Path $ps) {
  New-Item -ItemType Directory -Path $pt -Force | Out-Null
  foreach ($f in (Get-ChildItem $ps -File)) {
    $t = Join-Path $pt $f.Name
    $same = (Test-Path $t) -and ((Get-FileHash $f.FullName).Hash -eq (Get-FileHash $t).Hash)
    if (-not $same) { Copy-Item $f.FullName $t -Force; Info ('已同步 plugin\' + $f.Name) }
  }
} else {
  Info '仓库里没有 plugin\dsh-minato-bridge（跳过）'
}
Info ('CLI 文件更新 ' + $synced + ' 个（加起来看上面清单）')

# 4) 校验同目录 CLI 真报得出版本（证明它加载的是新 DLL，而不是旧版残留）
$sib = Join-Path $guiDir 'dsh-minato.exe'
# ★ 开发构建的 CLI 会往 stderr 打 INTEGRITY_SKIPPED 提示；PowerShell 5.1 把 stderr 当 NativeCommandError，
#   若全局是 Stop 就会在这里误判"跑不起来"（本脚本第一版就这么错过一次）→ 这一段临时降级为 Continue。
$prevEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
  $v = (& $sib version 2>&1 | Out-String)
  $line = (($v -split "`r?`n") | Where-Object { $_ -match 'DSHT_VERSION' } | Select-Object -First 1)
  if ($line) { Info ('同目录 CLI 版本：' + $line.Trim()) } else { Info '同目录 CLI 没打出 DSHT_VERSION（自己确认一下）' }
} catch {
  Info '同目录 CLI 跑不起来（自己确认一下）'
} finally {
  $ErrorActionPreference = $prevEap
}

# 5) 重启界面
$p = Start-Process -FilePath $guiExe -PassThru
Start-Sleep -Seconds 6
Info ('界面已启动 PID=' + $p.Id + ' 存活=' + (-not $p.HasExited))
Info '完成'