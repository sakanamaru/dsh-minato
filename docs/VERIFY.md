# VERIFY.md —— 用户如何**自己**核对这个工具的每条承诺

> 这份文档不解释功能，只回答一个问题：**你（用户/审查者）怎么在不信任我的前提下，
> 亲自把每条承诺跑一遍，并判断真假。**
>
> 纪律（与项目其它文档一致）：
> · 每条 = **跑什么命令** / **看哪行标记** / **什么算通过** / **什么算失败**
> · 下面所有输出都是**真机实测粘贴**（Windows 11 · PowerShell 5.1 · `dsh-minato 3.0.7-dev`），
>   不是"应该会…" ✓
> · 所有命令都**只碰隔离目录**：`DSH_HOME` 指到一个全新临时目录，真实 `~/.dsh` 全程不动 ✓
>   （这是**本文档每条命令都设了 `$DSH_HOME`** 的结果 ✗ —— **不是**产品保证：不给 `--apply` 的 `restore`
>   会写入**生效数据根**，未设 `$DSH_HOME` 时**就是默认 `~/.dsh`** ✓ 这正是外部审查者 N2 结案的那条 ✓
>   想自核"只写隔离根"就照 §2/§4 那样带 `--apply` ✓）
> · 标记行是**对外契约**：GUI 与脚本都靠它判断 ✗ 不要靠"退出码恰好是 0"来判断成功 ✓
>   （退出码契约见 §9；在本轮修复前，所有拒绝路径都返回 0 ✗）

---

## 0. 准备：一个隔离数据根 + 找到 exe

**Windows（PowerShell 5.1，本节所有命令按这个写）**

```powershell
# 用你自己的路径替换这一行：官方发布包解压后的 dsh-minato.exe
$exe = "$env:USERPROFILE\Downloads\dsh-minato-win-x64\dsh-minato.exe"

# 每个用例都从**全新**的隔离数据根开始（重复跑不会互相污染）
$iso = Join-Path $env:TEMP ("verify-" + [guid]::NewGuid().ToString("N").Substring(0,8))
$dsh = Join-Path $iso "dsh"          # 隔离数据根
$bk  = Join-Path $iso "bk"           # 隔离备份根
$ws  = Join-Path $iso "ws"           # 隔离工作区（防止把当前目录当工作区一起备份）
New-Item -ItemType Directory -Force (Join-Path $dsh "storages"), $bk, $ws | Out-Null
Set-Content (Join-Path $dsh "storages/a.txt") "ALPHA" -Encoding UTF8
Set-Content (Join-Path $dsh "storages/b.txt") "BETA"  -Encoding UTF8
Set-Content (Join-Path $ws  "ws-note.txt")    "WS"    -Encoding UTF8

$env:DSH_HOME = $dsh

# ★ 前置（很重要，否则"隔离根"里会冒出你以前的备份）：复位备份根选择。
#   备份根是粘性配置（`backup-dir --set` 写进 <exe 目录>\.backup-dir），
#   一条命令只会删这一个文件，不动数据、不删包。
& $exe backup-dir --reset
& $exe config-set ws $ws      # 把工作区指进隔离目录（否则会把当前目录当工作区）
```

**Linux（bash；把 `$exe` 换成解压出来的 `dsh-minato`）**

```bash
iso="$(mktemp -d /tmp/verify.XXXXXX)"
export DSH_HOME="$iso/dsh"; mkdir -p "$DSH_HOME/storages" "$iso/bk" "$iso/ws"
printf 'ALPHA\n' > "$DSH_HOME/storages/a.txt"; printf 'BETA\n' > "$DSH_HOME/storages/b.txt"
printf 'WS\n' > "$iso/ws/ws-note.txt"
exe=./dsh-minato
"$exe" backup-dir --reset
"$exe" config-set ws "$iso/ws"
```

**怎么读"标记行"**：工具把结论打成 `大写前缀 参数…` 的行（例如 `BACKUP_OK <包路径>`）。
标记是**英文前缀 + 中文说明**（`lang=en` 时说明变英文）→ 你只匹配前缀即可，别匹配说明文字 ✗。

---

## 1. 承诺：备份完成时会写一份**完成标记**，里面有文件数与内容哈希

```powershell
& $exe backup --to $bk
```

**看哪行**：`BACKUP_OK <包目录>`；再看标记文件 `<包目录>.manifest`（注意：**标记在包目录之外**，同级旁挂）。

```
BACKUP_TO C:\Users\…\verify-1a2b3c4d\bk 本次备份写到这个目录 ✓（放在安装目录之外才稳妥 ✓）
BACKUP_OK C:\Users\…\verify-1a2b3c4d\bk\dsh-data-20261007-112924540-23780
```
```
files=3
bytes=26
failed=0
finished=2026-10-07 11:29:24
sha256=5e24865c9bd5516ad3877a1364df1349bdbc7494d7f8c2251914ec663d632e08
```

**通过**：出现 `BACKUP_OK` ✓；标记里有 `files=` `failed=0` `sha256=` ✓；用
`(Get-ChildItem <包目录> -Recurse -File).Count` 数出来的文件数与 `files=` **相等** ✓。

**失败**：没有 `BACKUP_OK` ✗；标记缺失 ✗；`files=` 与实际数不符 ✗；`failed=` 大于 0
（说明源里有读不到的东西 —— 这是**如实报告**，不是崩溃 ✓ 见 §7）✗。

---

## 2. 承诺：**缺完成标记**的包会被拒收（"中断的备份不能当备份用"）

```powershell
Remove-Item "$pkg.manifest" -Force        # $pkg 取自 §1 的 BACKUP_OK 行
& $exe backup-list --verify
& $exe restore --path $pkg --apply --yes
```

```
BACKUP_VERIFY dsh-data-20261007-112924893-20792 incomplete 未完成（无完成标记 —— 备份可能被中断）
BACKUP_VERIFY_TOTAL 1 complete=0 incomplete=1 mismatch=0
```
```
RESTORE_FAIL 备份未完成（缺少完成标记 ✓ 可能是中断/磁盘满）：dsh-data-…-20792；确认要用它恢复请加 --force
```

**通过**：`--verify` 报 `incomplete` ✓ **且** `restore` 报 `RESTORE_FAIL`（退出码非 0，见 §9）✓
两边结论**一致** ✓（"同一个包两套结论"是曾经的缺陷 ✗）。

**失败**：`restore` 打印 `RESTORE_OK` ✗✗（那就是把中断的包当完整包恢复了）；
或 `--verify` 说 `complete` 而 `restore` 拒绝（两套结论 ✗）。

> 逃生门是有意留的：`restore … --force` 会**明确 WARN 后**照做 ✓ 这是人类的显式决定，不是静默放行 ✓。

---

## 3. 承诺：**标记被截断**（只剩 `bytes=/failed=/finished=`）的包会被拒收

```powershell
# 只删掉 files= 与 sha256= 两行 → 标记"看起来"还在，但已无法核对
$keep = Get-Content "$pkg.manifest" | Where-Object { $_ -notmatch '^files=|^sha256=' }
[System.IO.File]::WriteAllText("$pkg.manifest", (($keep -join "`n") + "`n"))
Get-Content "$pkg.manifest"        # 现在只有 bytes= / failed= / finished=
& $exe backup-list --verify
& $exe restore --path $pkg --apply --yes
```

```
bytes=26
failed=0
finished=2026-10-07 11:29:25
```
```
BACKUP_VERIFY dsh-data-20261007-112925183-6900 unreadable 标记无法解析
BACKUP_VERIFY_TOTAL 1 complete=0 incomplete=0 mismatch=1
```
```
RESTORE_FAIL 完成标记无法解析（缺少 files= 行）—— 无法核对完整性，按不完整处理：dsh-data-…-6900；确认要用它恢复请加 --force
```

**通过**：`--verify` 报 `unreadable`/`mismatch` ✓ **且** `restore` 报 `RESTORE_FAIL` 并说明
"无法解析 / 按不完整处理" ✓（这条是本轮 A3 修复的**fail-closed** 方向 ✓）。

**失败**：`restore` 放行并报 `RESTORE_OK` ✗✗（曾经的 fail-open：`want < 0 → return null → 放行`）。

**同理可验"内容被篡改"**（只改一个字节、文件名与数量都不动）：

```powershell
Set-Content (Join-Path $pkg "storages/a.txt") "TAMPERED" -Encoding UTF8
& $exe backup-list --verify
& $exe restore --path $pkg --apply --yes
```
```
BACKUP_VERIFY dsh-data-… mismatch 内容哈希与标记不符 —— 备份内容已被改动或损坏，不要依赖它
BACKUP_VERIFY_TOTAL 1 complete=0 incomplete=0 mismatch=1
```
```
RESTORE_FAIL 该备份内容与完成标记不符（哈希不一致）—— 内容已被改动或损坏；确认要用它恢复请加 --force
```

---

## 4. 承诺：恢复失败会**自动回滚**到恢复前的状态

怎么造一个"恢复到一半必然失败"的场景：把目标文件**独占锁住**（`FileShare.None`）。
恢复前工具会先做一个 pre-restore 备份（回滚锚点）→ 合并到这个文件时必定失败 →
它必须**自动用锚点打回去**，而不是留下半合并的数据根 ✓。

```powershell
# 先把数据根改脏，制造"值得恢复"的差异
Set-Content (Join-Path $dsh "storages/a.txt") "CHANGED" -Encoding UTF8
# 用独占句柄锁住目标文件（恢复一定写不进去；Close 之前一直有效）
$lock = [System.IO.File]::Open((Join-Path $dsh "storages/a.txt"), "Open", "Read", "None")
& $exe restore --path $pkg --apply --yes
$lock.Close()
Get-Content (Join-Path $dsh "storages/a.txt")     # 回滚成功 → 应仍是 CHANGED
```

```
RESTORE_APPLY_ACK 已按 --apply 跳过「运行中」闸门；观测到的服务状态：Down
RESTORE_APPLY_ROOT C:\Users\…\verify-…\dsh
RESTORE_PRE_BACKUP C:\Users\…\verify-…\bk\dsh-data-20261007-112941638-15908-pre-restore
RESTORE_FAIL 恢复失败：The process cannot access the file '…\dsh\storages\a.txt' because it is being used by another process.
RESTORE_ROLLED_BACK C:\Users\…\verify-…\bk\dsh-data-20261007-112941638-15908-pre-restore 已自动回滚到恢复前的状态 ✓
```

**通过**：先 `RESTORE_PRE_BACKUP`（锚点位置**必须**先告诉你 ✓）→ `RESTORE_FAIL`
（失败**不许**报成功 ✗）→ `RESTORE_ROLLED_BACK` ✓ → 目标文件内容仍是失败前的样子 ✓
（这里 `CHANGED` 是"恢复没写进去、回滚把原样打回来"的结果 ✓）。

**失败**：只打印 `RESTORE_FAIL` 而没有回滚行 ✗；或打印 `RESTORE_NO_ROLLBACK` 而数据根
**原先存在**（说明锚点没建 ✗ —— 只有"数据根原先不存在"时才允许没有锚点 ✓）。

> 回滚**也**失败时工具会打印 `RESTORE_ROLLBACK_FAILED <锚点包>`（并告诉你用哪个包手工恢复 ✓）——
> 那是**最后的诚实**，不是崩溃 ✓。

---

## 5. 承诺：恢复**不写穿**符号链接 / junction（目标端一个链接不能把数据写到根外）

**Windows 注意事项（先看，否则你会得到"假通过"）**
* 本机创建**文件/目录符号链接**需要管理员权限或开发者模式 ✗（实测：`New-Item -ItemType SymbolicLink`
  报 `此操作需要管理员权限`）→ **junction（目录联接）不需要管理员权限** ✓ 下面用 junction。
* 用 csc / `dotnet run` 编出来的 exe，其"工作区自动探测"会落到仓库目录 → 恢复**根本不会碰**
  数据根 → 看起来"没写穿"，其实**什么都没恢复** ✗✗（本文件第一版就踩了这个 ✓）
  → 所以 §0 的 `config-set ws <隔离目录>` **不是可选项** ✓ 本节的输出是在设了 ws 之后测的 ✓

```powershell
$outside = Join-Path $iso "OUTSIDE"; New-Item -ItemType Directory -Force $outside | Out-Null
Set-Content (Join-Path $outside "OUTSIDE.txt") "OUTSIDE-ORIGINAL" -Encoding UTF8
# 把数据根里的 storages 换成指向根外的 junction（先备份，让包里有内容）
cmd /c rmdir "$dsh\storages"
cmd /c mklink /J "$dsh\storages" "$outside"
& $exe backup --to $bk                                   # 包内应只有 3 个 fixture 文件（含 OUTSIDE.txt）
Set-Content (Join-Path $outside "OUTSIDE.txt") "TARGET-SIDE-AFTER" -Encoding UTF8
& $exe restore --path $pkg --apply --yes
Get-Content (Join-Path $outside "OUTSIDE.txt")           # ★ 根外文件必须**没被改**
```

```
   package entries: storages\a.txt, storages\b.txt, _workspace\ws-note.txt
--- restore --path --apply --yes (junction to outside)  rc=0
   RESTORE_APPLY_ACK …
   RESTORE_APPLY_ROOT …\verify-…\dsh
   RESTORE_PRE_BACKUP …\bk\dsh-data-20261007-112942797-14324-pre-restore
   RESTORE_OK …\bk\dsh-data-20261007-112941855-8496
   OUTSIDE\OUTSIDE.txt = TARGET-SIDE-AFTER      ← 根外文件原样 ✓（没写穿 ✓）
   junction still a reparse point = False        ← 恢复过程把 junction 换成了真目录 ✓
```

**通过**：根外那个文件的内容**一点没变** ✓；且恢复链路报的是 `RESTORE_OK` 或明确的
`RESTORE_FAIL …reparse point…`，**两种情况都不算写穿** ✓。

**失败**：根外文件被写成了包里的内容 ✗✗（那就是写穿了）。

**Linux**：`ln -s "$outside" "$DSH_HOME/storages"` 不需要特权，直接用同一条流程 ✓。

> 顺带记一条**本轮的额外发现**（不在本批次修 ✗）：恢复时**目标端目录若是链接，
> 整棵子树会被静默跳过**（不报 WARN）→ 结果可以 `RESTORE_OK` 但那一部分其实没恢复。
> 详见 `docs/修复记录-2026-10-07-批次E.md` 的"额外发现"段 ✓

---

## 6. 承诺：`..` 逃逸与根外路径会被拒绝（路径逃逸 fail-closed）

```powershell
$escape = Join-Path $bk "..\..\dsh"                # 指向数据根（在备份根之外）
& $exe restore       --path $escape --apply --yes
& $exe backup-export --path $escape --to (Join-Path $iso "esc") --yes
Test-Path (Join-Path $iso "esc")                   # 失败 → 连目录都不该被建出来
```

```
RESTORE_FAIL 备份目录不在备份根内                    （rc=1）
BKEXPORT_FAIL 导出校验失败: outside                  （rc=1）
False
```

**通过**：两条都**拒绝** ✓（`RESTORE_FAIL` / `BKEXPORT_FAIL`）✓ 退出码非 0 ✓
并且失败之后**没有**建出目标目录 ✓。

**失败**：任何一条照做 ✗；或失败却留下半个目录/文件 ✗。

**证据边界（如实说）**：这里验的是"不在备份根内的路径被拒" ✓。
`PathUtil.IsSubPath` 的**根路径**用例（`D:\`、`/`、`C:\bk` vs `C:\bkx`）由**契约测试**
覆盖 ✓ —— 你可以在 §10 的契约测试里看到它的名字 ✓。

---

## 7. 承诺：`wipe` **只打印路径，不删任何东西**

```powershell
& $exe wipe
Test-Path (Join-Path $dsh "storages/a.txt")        # ★ 必须仍是 True
```

```
WIPE_PLAN 数据根内有 2 个文件、1 个目录 — C:\Users\…\verify-…\dsh
WIPE_PLAN_NOTE **本工具不再执行清除** ✓ 请**手动**删除上面那个目录 ✓ 删前请先备份 ✓（备份目录：…）
WIPE_MANUAL C:\Users\…\verify-…\dsh
```
```
True
```

**通过**：打印 `WIPE_PLAN` / `WIPE_MANUAL` 且**路径正确** ✓；跑完数据**一个字节没少** ✓。

**失败**：数据被删 ✗✗；或只打印了"成功"但没有路径（用户无法手动删）✗。

---

## 8. 承诺：`uninstall` **只移除程序，不动数据目录与备份**

```powershell
& $exe uninstall                       # 不给 --yes → 只打印计划
Test-Path (Join-Path $dsh "storages/a.txt")     # ★ 必须仍是 True
```

```
UNINSTALL_PLAN 将执行：npm uninstall -g @deepseek-ai/dsh（只移除 dsh 程序，**不动**你的数据目录与备份）
UNINSTALL_DRYRUN （确认请加 --yes）
UNINSTALL_OBSERVED 0.1.5-rc.2
```
```
True
```

**通过**：打印 `UNINSTALL_PLAN` + 明确"不动数据目录与备份" ✓；**没有** `UNINSTALL_OK`（没确认就不执行 ✓）；
数据根原样 ✓。

**失败**：未确认就执行 ✗；数据目录/备份被动过 ✗。

> 想真的执行卸载：`uninstall --yes`（会调用 `npm uninstall -g @deepseek-ai/dsh` ✓）
> —— 那会改动你的机器，**本文件不代你做这件事** ✓。

---

## 9. 退出码契约（自动化能不能用 `&&` 判断）

**通过**：**拒绝/失败**路径退出码非 0 ✓；**成功**与**只读预览**路径为 0 ✓。

| 命令 | 期望退出码 | 实测 |
|---|---|---|
| `version` / `about` / `status` / `sessions` / `doctor` / `config-get` / `backup-list` | 0 | 0 ✓ |
| `backup --to <隔离目录>` | 0 | 0 ✓ |
| `backup-export … --yes`（成功） | 0 | 0 ✓ |
| `restore --dry-run` | 0 | 0 ✓ |
| `restore --path <缺标记/截断/篡改的包> --apply --yes` | **非 0** | 1 ✓ |
| `restore --path <备份根外>` | **非 0** | 1 ✓ |
| `backup-export --path <备份根外> --yes` | **非 0** | 1 ✓ |
| `backup`（全新安装、没给 `--to`） | **非 0** | 1（`BACKUP_NEEDS_DIR`）✓ |
| `wipe` / `uninstall`（未确认） | 0（不改机器） | 0 ✓ |

**失败**：拒绝路径返回 0 ✗✗（那样 `dsh-minato restore … || echo 失败` 会误判成功 ——
这正是本轮 A2 修的缺陷 ✓）。

---

## 10. 一次跑完全部（本仓库自带的自动化，推荐）

```powershell
# ① 命令 × 隔离数据根 冒烟矩阵（本批次新增；含上面 §1/§2/§6/§7/§8 的断言）
pwsh -NoProfile -File v3/tests/verify_command_matrix.ps1 -Repo .

# ② 真实恢复端到端（隔离根真写盘 + 零越界证明）
pwsh -NoProfile -File v3/tests/verify_restore_apply.ps1 -Repo .

# ③ 契约测试（含根路径 / `..` 逃逸 / 符号链接等纯领域用例）
dotnet run --project v3/tests/Dsht.Contracts.Tests/Dsht.Contracts.Tests.csproj -c Release
```

实测（2026-10-07，`3.0.7-dev`）：

```
== 矩阵：44/44 passed, 0 failed（XFAIL 0）==
== 26/26 passed, 0 failed ==            ← verify_restore_apply.ps1
== 355/355 passed, 0 failed ==          ← 契约测试
```

矩阵自己也会**证明没越界**：它比对真实数据根（`%USERPROFILE%\.dsh` 等）跑前跑后的快照，
不一致就报 `[FAIL] 真实数据根未变: …` ✓。

该矩阵**能因缺陷变红**（本批次实测过）：把 version 断言临时改成
`^DSHT_VERSION_XX [0-9]` → 输出 `[FAIL] version 打印标记行` +
`== 矩阵：43/44 passed, 1 failed ==`，退出码 **1** ✓；改回去 → 恢复 `44/44`、退出码 0 ✓。

---

## 11. 这份文档**没有**覆盖的（别把它当全量保证）

* **GUI 观感/布局**：只能人眼看 ✓ 不计入"已验证" ✗（见 `docs/release-notes-*.md` 的"已知边界"）。
* **安装/卸载产物**：**Windows** 侧 `v3-windows` 打包 job 会跑 setup.exe 的装 → 卸，并断言退出码、残留文件与注册表都清干净 ✓
  —— 那需要 CI 跑打包（tag 或手动触发）✓ 本文件不重复它 ✓。
  **Linux** 侧 `install.sh` 目前**只是被打包进 tarball，CI 不执行它** ✗（`verify-linux.sh` 只验包形态与 CLI 能否跑 ✓）
  —— 本版改由**真机**执行核对：修复前 `exit 3`（假红）→ 修复后 `exit 0`，并做了"篡改载荷 → `exit 3`"的反向验证 ✓
  （见 `docs/release-notes-v3.0.7.md` 的「Linux 真机验证」小节 ✓）· **建议把安装器接进 CI** ✓
* **联网命令**（`check` / `update-info` / `update-center` / `balance`）：结果随上游变 ✓ 不适合当承诺核对 ✓。
* **Linux 上的 shell 验证脚本**（`v3/tools/linux_smoke.sh`、`verify_backup_chain.sh`、`verify_failure_paths.sh`）：
  依赖 POSIX 工具（`mktemp -d /tmp`、`timeout`、`ss`、可执行位）✗ → 在 Linux 或 CI 上跑 ✓；
  Windows 上跑会得到与产品无关的失败 ✓（如实说明，不假装 ✓）。
* **崩溃/断电下的原子性**：完成标记"最后才写"保证你能**识别**未完成的包 ✓，
  但它不保证文件系统层面的事务性 ✓。
* **不带 `--apply` 的 `restore` 会写哪儿**：写入**生效数据根** —— 未设 `$DSH_HOME` 时就是默认 `~/.dsh`
  （[v2.x 兼容行为](release-notes-v3.0.7.md) ✓，Linux 真机端到端实测确认：`~/.dsh/.anonymous-user-id` 与
  `.credentials.yaml` 的 mtime 被刷新 ✓）。本文档所有 `restore` 用例都带 `--apply` + `$DSH_HOME` ✓，
  因此**没有**在这里覆盖"不带 `--apply` 写默认根"的那条路径 ✗（如实写明，不当已验证 ✓）。
