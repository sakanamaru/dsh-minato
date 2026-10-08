# V3 底层重构 · 开发者说明

> 本目录是 **v2.x 的平行树**：v2.x 的 `v2/dsh_v2.cs` + `v2/src/**` + `v2/verify.ps1` + 16 项发布清单
> **保持不变、随时可发布**（v2 整棵树已移入 `v2/`）；V3 在这里独立演进，达到切换门槛后再接管。
> 设计与决策依据：`设计说明`。
>
> **关于分支与提交历史**：V3 工作全部在 `v3-linux` 分支上，且已 **rebase 到 `main` 之上**——
> 所以两线是 **`main` + V3 增量**的线性关系（`git diff main..v3-linux` 就是纯 V3 工作量），切换时是快进或一次干净合并。
> 
> 这些提交**现已全部推送**到 `v3-linux`（main 不受影响，`main` 上只有已发布的 v2.x）。

---

## 1. 目录结构

```
v3/
  src/
    Dsht.Domain/            纯领域：零 IO、零平台、零时钟耦合（有守卫强制）
      Model/                AppKind / ServiceState / ServiceReport / DocItem / ProfileFinding /
                            IntegrityVerdict / BackupEntry / BackupKind / DirSnapshot / ProfileFile ...
      Abstractions/         IServiceTarget / IPortProbe / IHttpProbe / IProcessQuery / IFileSystemQuery /
                            IToolchainQuery / IIntegritySource / IProfileSource / IBackupSource / IPaths /
                            IConfigSource / ILogSource
      Services/             ServiceJudge / UptimeFormatter / BackupRetention / BackupPackage /
                            ProfileScanner / ManifestParser / IntegrityJudge / DoctorSummary /
                            SizeFormatter / ReportSanitizer / BackupAge / RestoreApplyPolicy /
                            DoctorReport / ConfigSummaryBuilder / LogSummaryBuilder
      Targets/              WebTarget / UnknownTarget / ReservedTarget / CompositeServiceTarget
    Dsht.Platform.Windows/  Windows 实现（netstat / Get-CimInstance / where / taskkill / %APPDATA%）
    Dsht.Platform.Linux/    Linux 实现（ss / /proc/<pid>/cmdline / ps / PATH 扫描 / $XDG_* / $DSH_HOME）
    Dsht.Cli/               组合根（自写 ServiceRegistry，零第三方 DI）+ 命令面 + 平台装配
  tests/
    Dsht.Contracts.Tests/   契约测试宿主（零第三方断言，334 项）
    verify_domain_pure.ps1  领域层纯净度守卫（扫描前剥离注释）
    compare_markers.ps1     与 v2.x 的标记行契约比对（可 -Fixtures 造受控备份）
    verify_restore_apply.ps1 真实 restore 的端到端验证（隔离数据根 + --apply，含"零越界"证明）
    verify_release.ps1      发布物校验（v2.x `v2/verify.ps1` 等价物，含校验器自证）
```

---

## 2. 本地怎么构建与验证（**不需要 .NET SDK**）

V3 的代码刻意保持 **C#5 兼容**，因此可以用现役的 `csc`（.NET Framework 4.x）把**整个 V3 树**编成一个 exe 在本地跑：

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = @(Get-ChildItem v3\src -Recurse -Filter *.cs | ForEach-Object FullName)
& $csc /nologo /target:exe /warn:4 /out:"$env:TEMP\dsht_v3.exe" $src
& "$env:TEMP\dsht_v3.exe" status --detail
& "$env:TEMP\dsht_v3.exe" doctor
& "$env:TEMP\dsht_v3.exe" describe
```

三道门禁（都在 `v3/tests/`）：

```powershell
# ① 领域层纯净度（零 IO / 零平台 / 零时钟耦合）
powershell -ExecutionPolicy Bypass -File v3\tests\verify_domain_pure.ps1 -Repo .

# ② 与 v2.x 的标记行契约比对（需要仓库根已构建 v2.x exe；-Fixtures 造受控备份覆盖 detail 分支）
powershell -ExecutionPolicy Bypass -File v3\tests\compare_markers.ps1 -Repo .
powershell -ExecutionPolicy Bypass -File v3\tests\compare_markers.ps1 -Repo . -Fixtures
powershell -ExecutionPolicy Bypass -File v3\tests\compare_markers.ps1 -Repo . -Heavy   # 追加真实 backup 比对（每次约写 400MB，跑完自动清理）

# ③ 发布物校验（-Build 本地造发布物；-SelfTest 篡改一字节证明校验器有效）
powershell -ExecutionPolicy Bypass -File v3\tests\verify_release.ps1 -Repo . -Build -SelfTest

# ④ 真实 restore 的端到端验证（隔离数据根 + --apply；含"真实数据根零写入"证明）
powershell -ExecutionPolicy Bypass -File v3\tests\verify_restore_apply.ps1 -Repo .
```

> 注意：`-ExecutionPolicy Bypass` 不能省——默认执行策略常禁止直接运行 `.ps1`（会报 UnauthorizedAccess）。

一键就绪度（把下面五项 + 三条不变量一起跑，切换前看这一个就行）：

```powershell
powershell -ExecutionPolicy Bypass -File v3\tests\verify_switchover.ps1 -Repo .
# gate1 标记行契约 21/21（含受控备份模式）  gate2 契约测试 334/334
# gate3 Win/Linux 双跑：已在 CI 真跑通过（run 36385480118）  gate4 发布物校验（含篡改自证）
# gate5 真实写操作可验证 25/25（隔离根真实写盘 + 零越界）
# 不变量：发布链未动（v2/verify.ps1 按内容比对 / 16 项清单 / csc 步骤）· v2.x 发布构建可编译 · 含非 ASCII 的 .ps1 都带 BOM · 领域层纯净度
```

`dotnet`（net8.0）路径由 CI 负责：`.github/workflows/build-release.yml` 的 `v3-contracts` job
在 **windows-latest + ubuntu-latest** 双平台跑 `dotnet build` + 契约测试。

---

## 3. 已实现的命令面（标记行与 v2.x 逐字对齐）

| 命令 | 标记 |
|---|---|
| `status` / `status --detail` | `STATUS_UP` / `STATUS_STARTING` / `STATUS_DOWN` + `STATUS_PID` / `STATUS_START` / `STATUS_UPTIME` |
| `profilecheck [--dir X] [--file Y] [--vendor] [--abs]` | `PROFILECHK_WARN` / `_TOTAL` / `_SKIPPED_VENDOR` / `_FIX` / `_OK` |
| `profiles`（V3 独有） | `PROFILES_OK <n>` + `PROFILE <name> form=<web\|headless\|acp\|unknown\|unparsed> bundles=<n> thirdparty=<m>` + `BUNDLE <profile> <bundle-id> <official\|thirdparty>` / `PROFILES_FAIL <原因>`；**只读** `profiles/<name>/package.json` 的 `dsh.profile.bundles` → 给出**配置形态**与**插件清单（含第三方）**。**注意：这是配置形态，不是运行形态**——"dsh 在跑"仍由端口/进程等运行时事实判断 |
| `sessions [--days 7\|14\|30] [--level global]`（V3 独有） | `SESSIONS_OK <n>` / `SESSIONS_NONBLANK <n>` / `SESSIONS_SOURCE <snapshot\|disk\|aggregate>` / `SESSIONS_ROOT <dir>` + 每会话 `SESSION <id> created= last= turns= steps= in= out= cacheRead= hit=<%\|unknown> decode=<tok/s\|unknown> ttft=<ms\|unknown> ctx=<%\|unknown> blank=0\|1` + `SESSIONS_TOTAL …` / `SESSIONS_FAIL <原因>`；**只读** dsh 的会话投影（明文 JSON，持续更新）。**诚实边界**：不读对话正文；字段缺失打印 `unknown`（不假装 0）；"有几个会话在跑"这里只能给最后活动时间——运行态是进程内事实，需要插件。**窗口筛选（2026-10-08 起）**：`--days` 只认 7/14/30（省略 = 总计档；其他值 ⇒ 明说的 `SESSIONS_FAIL`），`--level` 第一批只实现 `global`（非 global ⇒ `SESSIONS_FAIL` + stderr 明说「含子代理统计须读原始日志，随解码器决策后移第二批」）。窗口元数据 `SESSWIN_META days=<7\|14\|30\|unknown> from=<本地日\|-> to=<本地日\|-> tz=<平台原生时区名> truth=0 level=global source=<…> scanned=<n> eligible=<n> unknown_last=<n>`（`truth=0` = 整会话累计、非窗口增量）；库级恒定 `SESSAGG_TOTAL scope=store …`（**不随筛选变**；`first_day`/`last_day` 是本地日，无 ⇒ `unknown`）；命中窗口的每会话 `SESSAGG_SESSION <id> bucket=own turns= steps= uncached= cacheRead= cacheWrite= output= children=unknown depth=unknown`（第一批只打 own 行；血缘字段打 `unknown` 不打 0 冒充）。逐键行数门禁只进 stderr：`SESSAGG_UNKNOWN_LAST n=` / `SESSAGG_ROWDROP key=<名> ver=<期望版本> n=<条数>` |
| `backup-list [--detail]` | `BACKUP_LIST_OK` + 裸路径行 + `BACKUP_ITEM` |
| `doctor [--report <file>]` | `DOCTOR_OK` / `DOCTOR_WARN` / `DOCTOR_ERROR` + `[级别] 类别 描述`；`--report` 另写完整诊断报告（分类小节 + 配置/日志摘要，全部脱敏）→ `DOCTOR_REPORT <路径>` / `DOCTOR_WRITE_FAIL <原因>` |
| `restore --dry-run [--path <dir>]` | `DRYRUN_OK` + `DRYRUN_SRC` + 每个作用域 `DRYRUN_SCOPE`/`_NEW`/`_OVERWRITE`/`_KEEP`/`_BYTES` + `DRYRUN_TOTAL` + `DRYRUN_NOTE`（失败 `DRYRUN_FAIL 原因`） |
| `config-get` | `CONFIGGET_OK` + `CONFIG <key> <value>` × 24（2026-10-08 起含看板键：`sessions_default_level`（第一批只有 `global` 生效）+ 4 个 `sessions_price_*_per_mtok` 单价键——**只存与校验，费用显示属第三批，当前不算不显示**） |
| `config-set <key> <value>` | `CONFIGSET_OK <key>` / `CONFIGSET_FAIL <reason>` |
| `bootdiag --from <file>` | `BOOTDIAG_OK`/`_FAIL` + `_KIND`/`_PLUGIN`/`_ENTRY`/`_FILE`/`_LINE`/`_HINT`（未识别时 `_FIRST`） |
| `backup` | `BACKUP_OK <路径>` / `BACKUP_FAIL <原因>`（真实写盘；比对需 `-Heavy`，目录名含时间戳会归一化） |
| `restore` / `restore --path <dir>`（非 dry-run） | 校验 + **安全闸门**（无有效备份 / 运行中拒绝 / 恢复前自动备份失败）→ `RESTORE_FAIL <原因>`；闸门通过后**真实合并恢复**：`RESTORE_PRE_BACKUP <回滚锚点>` + `RESTORE_OK <备份目录>`（工作区另有 `RESTORE_WS_*`）。**`--apply` 显式开关**：只允许写入隔离数据根（见 §7） |
| `backup-delete --path <bk>` | **真实删除**（含只读属性清理）→ `BKDEL_OK <名字>` / `BKDEL_FAIL <原因>`；隔离根下已验证（见 §7） |
| `backup-export --path <bk> --to <dir>` | **真实导出副本**（只读源）→ `BKEXPORT_OK <目标路径>` / `BKEXPORT_FAIL <原因>`；隔离根下已验证 |
| `check` | 横幅 + `Node.js`/`npm`/`dsh`/`dsh 版本`/`dsh 最新`/`Web 服务`/`UI 语言` 七行（GUI 检查页数据源） |
| `selftest [<report>]` | 写自检报告并打印 `report -> <路径>`（报告正文 11 行与 v2.x 一致；产品标识行本就不同） |
| `describe`（V3 独有） | 说明"考虑过哪些形态、为什么暂时观测不到" |
| `version`（V3 独有） | `DSHT_VERSION <版本>` |

比对工具当前结论：**21/21 对齐**（另有 2 项 `backup` / 真实 `restore` 需 `-Heavy` 与运行中的服务，届时 23/23）。按规则忽略的行：`doctor` 的 Integrity 条目、`doctor --report` 的报告头三行（时间戳/版本/系统）与结果行、报告里的日志摘要行（两次运行之间日志会增长）、以及 **V3 有意新增的两个标记**（`BACKUP_ITEM_INVALID` 无效条目提示、`BACKUP_NEEDS_DIR` 首次备份需选目录）与 exe 自身的 `INTEGRITY_SKIPPED` 诊断——理由都写在脚本注释里。

---

## 4. 服务模型：为什么"未识别形态"是一等公民

驱动事件是 **dsh 可能出桌面端**：桌面端未必监听 3080，甚至可能走 stdio/命名管道。
因此 V3 不把"dsh 在跑"等同于"3080 有监听"，而是：

```csharp
enum AppKind { Unknown, Web, Headless, Acp, Desktop }
interface IServiceTarget { AppKind Kind; bool IsAvailable(); ServiceReport Probe(); int FindPid(); string Describe(); }
```

- `WebTarget`：真实观测（端口 → HTTP → 监听进程身份，懒求值）
- `ReservedTarget`：**形态已承认但暂无可观测事实** → 报 Down + 说明原因，**绝不假装 Ready**
- `CompositeServiceTarget`：按 `Ready > Listening > Down`、同状态"已知形态优先于 Unknown"确定性择一；
  全 Down 时报 Unknown 并列出"已尝试的形态"

待 dsh 桌面端/ACP 的真实形态可观测（进程名、IPC 通道）后，把对应的 `ReservedTarget` 换成真实实现即可，
**上层判定、报告与 CLI 都不需要改**。

---

## 5. 诚实边界（不要高估当前进度）

| 项 | 现状 |
|---|---|
| Linux 实现 | **只到"编译过 + 纯逻辑有单测"**（ss 解析、DSH_HOME 路径解析等已验）；真机运行需 ubuntu CI job，而 CI 需推送才能触发 |
| `doctor --report <file>` | ✅ **已移植**（2026-09-28）：报告正文与 v2.x 逐字对齐（分类小节 / 条目 / 配置摘要 / 日志摘要 / 结果行），条目与摘要都过 `ReportSanitizer`；写入用 UTF-8 **带 BOM**（与 v2.x 一致）。比对时按规则忽略报告头三行与结果行（时间戳/版本/被忽略的完整性条目），日志摘要行做掩码（内容含时间戳）——格式本身由契约测试的 `LogSummaryBuilder`/`ConfigSummaryBuilder` 覆盖 |
| `Environment.OSVersion.VersionString` | .NET Framework 与 net8 下字符串不同 → 将来 V3 真正用 net8 发布时需要归一化 |
| headless / acp / desktop | **预留**，无可观测事实前不实现猜测逻辑 |
| macOS | 未开始（设计稿决策：Linux 优先，macOS 视需求后补） |
| 命令面广度 | 已覆盖 GUI 消费的主要命令（含 `restore --dry-run` 预览、`selftest`、`check`、真实 `backup`）；**真实 `restore` 的数据写入已移植**（合并语义、恢复前自动备份、自身完整性闸门、`_workspace` 工作区恢复；隔离根下端到端验证 24/24）。`backup`/`backup-delete`/`backup-export`/`restore` 均已真实实现并在隔离根下验证 |
| 工作区自动探测 | ✅ 已移植（2026-09-28）：Windows 侧取 **exe 所在目录的上两级**并用 `WorkspaceJudge` 做合理性判定（盘根 / 各盘根保留名 / 用户主目录 / `C:\Users` / Windows / ProgramData / Program Files ×2 一律拒绝）；`ws=` **配置优先**——配置了但目录不存在 → 返回 null 且**不回退探测**（避免误备份/误恢复）。dry-run 与真实恢复走同一个 `WorkspaceResolver`，目标一致。**Linux 侧仍诚实返回 null**（v2.x 的 Linux 接缝同样如此），但 `ws=` 配置在 Linux 上同样生效 |
| 真实 restore 的写入范围 | **写入目标始终是生效数据根，与 `--apply` 无关** ✓：不给 `--apply` 时按 **v2.x 兼容语义**恢复到该根（运行中拒绝 → 恢复前自动备份 → 恢复）——未设 `$DSH_HOME` 时它就是**默认数据根**（`~/.dsh` 等），Linux 真机端到端实测确认（`~/.dsh/.anonymous-user-id` / `.credentials.yaml` 的 mtime 被刷新 ✓）。`--apply` **不是**"允许写盘"的开关，而是"**跳过运行中闸门 + 要求隔离数据根**"：必须设置 `$DSH_HOME` 且生效数据根不等于任何默认候选，否则 `RESTORE_FAIL` 拒绝 —— 因此**带 `--apply` 时**永远不可能写进 `~/.dsh`（v2.x 没有这个开关，也没有这层保护） |
| `RESTORE_OK` 的时机 | **有意比 v2.x 更严格**：v2.x 在恢复失败（异常/完整性不匹配）时也会打印 `RESTORE_OK`；V3 只在真正成功时打印，失败打印 `RESTORE_FAIL <原因>` |
| `restore --dry-run --path <相对路径>` | **v2.x 的已知缺陷 —— 已在 v2.7.3 修复发布**：v2.7.2 的 `P()` 给相对路径加 `\\?\` 前缀（`\\?\.\backup\x` 是非法 Win32 路径）→ 源侧遍历被 try/catch 静默吞掉，预览报 `DRYRUN_NEW 0 / OVERWRITE 0`。V3 用相对路径能正常遍历（数字正确）。`compare_markers.ps1` 因此统一把 `-Repo` 转绝对路径，否则会比对出**假差异**（这条已在脚本注释里写明原因） |
| GUI | Windows-only WinForms 保持不变；跨平台 GUI 只留架构能力（见设计稿 §7） |
| **`DSH_HOME` 环境变量** | **唯一一处刻意偏离 v2.x 的行为**：Windows 侧也优先读 `$DSH_HOME`（Linux 侧本就支持）→ 便于在隔离数据根下安全测试写操作与多环境部署；未设置时与 v2.x 完全一致 |
| 含子代理统计 | **未实现（待第二批；需读原始日志）**：投影全文扫描证实**没有**血缘字段（283 个投影 `origin`/`parentSession` 零命中），catalog 并集实测漏 26 个真子代理（13%）+ 18 条 fork 边无投影来源——纯投影血缘必然缺数 ⇒ 第一批 `sessions --level` 只认 `global`（其余明说拒绝），GUI 口径选择器「仅父会话」「父会话+子代理」灰显（角标「第二批」），随 zstd 解码器决策一起做 |
---

## 6. 门槛③（Win/Linux 双跑）—— **已变绿**（2026-09-28）

> **2026-09-28 追加：真机 Ubuntu 证据（VMware VM）** —— 契约测试 **296/296**；隔离 DSH_HOME 生效；
> `backup` 真实写盘 → `BACKUP_LIST_OK 1`（补了 `settings.yaml` 后按有效性规则计入）→ `restore --dry-run` 报 `DRYRUN_OK`（含合并语义）；
> `doctor` 报 `[OK] System Linux: …` —— 并据此修掉了硬编码的 "Windows" 标签（提交 `8044fbb`）。
> 也就是说：门槛③ 不再只有 CI runner 的证据，**真机 Linux 也跑通了完整读写链路**。

CI run **36385480118**（分支 `v3-linux`）：`V3 contracts (windows-latest)` 与 `V3 contracts (ubuntu-latest)` 各 **220/220**，
外加 `unit + integration tests` 绿。也就是说 V3 契约测试现在**在真实 Linux 上跑过**，不再只是"编译过"。
（此后又加了 16 项 `doctor --report` 契约测试（236/236）与 16 项工作区判定/解析契约测试（334/334）；每次推送 v3-linux 都会再跑一次 CI。）

第一次真跑（run 36385248055）在**两个平台同时失败**，暴露了两个本地永远看不到的问题（本地只用 `csc` 全量编译，完全绕过 csproj）：

| 失败 | 根因 | 修法 |
|---|---|---|
| `CS0579 Duplicate 'System.Reflection.Assembly*Attribute'` | `Program.cs` 里写了程序集属性（零 SDK 的 csc 路径需要它们），而 SDK 又自动生成 `obj/.../AssemblyInfo.cs` | `Dsht.Cli.csproj` 加 `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` |
| `CS0234 'Linux' does not exist in the namespace 'Dsht.Platform'` | `PlatformComposition.cs` 用了 `Dsht.Platform.Linux`，但 csproj 只引用了 Domain + Platform.Windows | 补 `<ProjectReference ... Dsht.Platform.Linux.csproj />`（组合根运行时选平台，两个实现都要引用） |

**自己复现**（推分支 + 看 CI）：

```powershell
cd "<repo>"
git switch -c v3-linux              # 建分支，不动 main
git push -u origin v3-linux         # 推送即触发（workflow 的 push 分支已含 'v3*'）
gh run watch                        # windows-latest + ubuntu-latest
```

> 注意：workflow 的 `push.branches` 原本只有 `main`，所以"推 v3-linux 就会跑 CI"曾经**不成立**
> （推了也不会触发）——现已加入 `'v3*'`。若你只想跑一次、不想再推：`gh workflow run build-release.yml --ref v3-linux`
> （手动触发会连 `build` job 一起跑，并在分支上提交一次 CI 生成的 `v2/hashes.txt`）。

跑完后删分支即可（不影响 main）：

```powershell
git push origin --delete v3-linux
git branch -D v3-linux
```

**或者**：你放行让我推送（我会推同样的分支，不碰 main），我负责跑通并把结果写回文档。

> 说明：`v3-linux` 分支上是**未合并的 41 个本地提交**（含 v2.8 阶段的拆分/接缝/Linux 实现 + V3 全部工作）。
> main 上仍是 `8f885ce`，`v2/verify.ps1` 与 16 项发布清单完好，**随时可发布**。
---

## 7. 怎么安全地测试写操作（真实 restore/export/delete）

V3 的 Windows 路径解析支持 **`$DSH_HOME`**（唯一一处刻意偏离 v2.x 的行为），因此可以在**隔离数据根**下真实测试写操作，完全不碰你的 `~/.dsh`：

```powershell
$iso = Join-Path $env:TEMP ("v3_iso_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path "$iso\data" -Force | Out-Null
'x' | Set-Content "$iso\data\settings.yaml"        # 造一个假数据根
Copy-Item "$env:TEMP\dsht_v3.exe" $iso -Force      # exe 与数据根同处隔离目录

$env:DSH_HOME = "$iso\data"
& "$iso\dsht_v3.exe" doctor                        # 应看到隔离数据根与 1 B 大小
& "$iso\dsht_v3.exe" backup                        # 备份应写进 $iso\backup
Remove-Item Env:\DSH_HOME; Remove-Item $iso -Recurse -Force
```

实测结论（本轮）：`doctor` 显示隔离数据根（1 B）· `backup` 写出 1 个文件的备份 · **真实 `~/.dsh` 未被触碰** · 不设该变量时标记行契约仍 **22/22**（零回归）。

### 7.1 一条命令跑完全部真实 restore 验证

```powershell
powershell -ExecutionPolicy Bypass -File v3\tests\verify_restore_apply.ps1 -Repo .
# 24 项：备份 → 篡改 → restore --apply（真实写盘）→ 合并语义 → 恢复前自动备份内容 →
#        不给 --apply 的边界行为 → 未设 $DSH_HOME 时拒绝 → 真实默认数据根快照未变 + %TEMP%\backup 无残留
```

### 7.2 `--apply` 的规则（为什么要它）

真实恢复会**覆盖用户数据**，所以 V3 把它拆成两级：

| 情形 | 行为 |
|---|---|
| 不给 `--apply` | 与 v2.x 同序：运行中拒绝 → 恢复前自动备份 → 恢复。写入目标是**生效数据根**（未设 `$DSH_HOME` 时**就是默认数据根**，Linux 真机实测 ✓）；服务在跑就**不会**写盘 |
| `--apply` + 已设 `$DSH_HOME` + 数据根 ≠ 任何默认候选 | **真实写盘**；并打印 `RESTORE_APPLY_ACK`（把观测到的服务状态原样留证）与 `RESTORE_APPLY_ROOT` |
| `--apply` + 未设 `$DSH_HOME` | `RESTORE_FAIL … 需要先设置 $DSH_HOME`（零写入） |
| `--apply` + 数据根就是默认位置 | `RESTORE_FAIL … 生效数据根就是默认位置`（零写入） |

`--apply` 是**人类可问责的断言**（"我确认没有 dsh 正在使用这个数据根"），而不是绕过闸门的后门：
它无法指向默认数据根，因此**带 `--apply` 时**不可能写坏你的 `~/.dsh`；同时它把"跳过闸门"这件事与观测到的事实一起打出来，不静默。
（⚠️ 不带 `--apply` 是 **v2.x 兼容路径**：服务不在跑时会照常恢复到**生效数据根** —— 未设 `$DSH_HOME` 时就是默认 `~/.dsh` ✓）
`apply-not-isolated` 这条分支**故意不做端到端测试**——把"应当拒绝"的用例指向真实数据根，一旦判定有 bug 就会真写用户数据；
它由纯领域契约测试覆盖（`RestoreApplyPolicy`，见 §2 的契约测试 334 项）。