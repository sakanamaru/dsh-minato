# Changelog / 更新日志

All notable changes to **dsh-minato** (unofficial). Full release notes, assets and verification data live on the [Releases page](../../releases).

**dsh-minato**（非官方）的主要变更记录。完整发布说明、产物与校验信息见 [Releases 页面](../../releases)。

---

## v3.0.5 — 2026-10-05（自动刷新取新数据 / auto-refresh reads fresh data）

### Fixed / 修复

- **自动刷新"停了"**（用户实测："概览刷新设置后停下来了，字段刷新不生效"）：概览/看板的四个数据源
  全走 RunCached（4 秒 TTL，为切页防重复起进程 ✓）→ **3 秒档每一拍都命中缓存** → 拿回的还是上一拍的旧数据 →
  字段写回去还是同一个值 → 看起来"停了"（0.5/1/3 秒全中招；5 秒没事——所以症状是"好像还有一点问题" ✓）。
  自动刷新这一拍现在**先清缓存再跑** ✓ 每拍都是新数据 ✓；普通刷新/切页继续吃缓存 ✓；
  旁注"每一拍都起 CLI 进程"**从此真正成立**（修复前对 3 秒档是假的 ✗ 已如实更正 ✓）
- **余额回调不再整页重建**：原来开着自动刷新时**每 60 秒闪一下** + 滚动回顶 + 160ms 淡入——
  恰好是字段级刷新要保护的东西（审查点过名 ✗）。余额卡本就是活字段，到账现在只改数字 ✓
  没注册字段的页面退化为整页重建（数据不丢 ✓）

---

## v3.0.4 — 2026-10-05（四路审查修复 / fixes from a four-way review）

### Fixed / 修复

- **桥接插件丢弃 active=false** ✗✗（高）：buildSnapshot 只写 true，"挂着（dsh 内未动）"三态分支永远走不到 → 退回 15 分钟启发式。
  0.2.0 的核心功能只做了一半；JS 测试只测过 true，false 只有 C# 在测——端到端断链。补了 JS 侧断言 ✓
- **doctor --report 泄漏 balance_key** ✗✗（高）：消毒器词表没有 balance_key，32 位 hex 的 key 也不够 40+ 位规则 →
  用户外发求助报告即泄漏，与报告自称"（脱敏）"矛盾。消毒词表已补 ✓
- **体检期间排队的刷新被永久丢弃**（高）：RunDoctorNow 完成回调不处理 _refreshQueued → 页面停在旧数据还多闪一次 ✓ 已对齐
  RefreshGuardedAsync 的队列处理 ✓
- **看板 KPI 继承会话页旧快照**（高）：用过"非空"筛选再切看板，四张卡显示子集之和 + 错误的"（父会话）"标签 →
  聚合只在会话页才信 ListSource ✓
- **auto_start_target 缺 CONFIG 行**：GUI 设置页永远渲染不出"开机自启启动什么"（模型/白名单/序列化全有，就缺输出）✓
- **balance_key 三处明文露出**（中）：config-get 回显明文、设置页无掩码、保存时 toast 弹完整命令 → CLI 只报 set/unset ✓
  设置页改专用编辑器（不回显 / 留空=保持 / 清除=解绑 ✓）、toast 打码 ✓
- **备份结果弹的是上一个动作的旧文案**（中）：BACKUP_FAIL 等真实错误用户看不到 ✓
- **balance_key 允许换行**（低→堵上）：自由文本键拒绝 CR/LF，防 launcher.config 行注入 ✓

### Known / 已知（本轮未修，如实列）

- GUI 字符串拼命令行的系统性习惯（引号拼接而非 ArgumentList）未迁移——当前残余风险为低危旗标注入。
- Run 的 30 秒硬超时会杀长任务（update/install/大备份可能中途被杀，留下半截备份包）。
- 概览页状态大徽章不走字段级刷新；配置落盘无 DPAPI/0600 加固、非原子写入。

---

## v3.0.3 — 2026-10-04（连点修复、余额入口与只刷字段 / click-guard, balance entry & field-level refresh）

### Added / 新增

- **余额的入口**（未绑定 key 时）：设置页独立分组「DeepSeek 余额检测」+ 概览页一行入口（带跳转按钮）✓
  此前未绑定就连"去哪儿填 key"都无处可寻 ✗；余额数字在未绑定时**仍然不显示** ✓（原设计不变）。

### Fixed / 修复

- **概览/看板自动刷新改成「只刷字段」** ✗ 不重建整棵视觉树 ✓（原来每拍重建 → 滚动/焦点/下拉全丢 + 整页淡入 ✗✗）
- **更新可以连续点好几次** ✗✗：每次点击都会新起一个 CLI 进程 → 点三次 = **三个 `update --yes` 同时跑**
  （npm 安装互相踩 + 连做三次备份 + 回滚点互相覆盖）→ 现在**同时只允许一个 CLI 动作**：
  正在跑时的点击**如实拒绝**并说明在跑什么、已多久 ✗ **不排队** ✗；按钮禁用并显示「进行中：X …」；
  动作结束时在 `finally` 里**必定放开**闸门 ✓（否则按钮永久卡死，比重复点击更难查 ✓）
- **「立即备份」绕开闸门** ✗（走的是自己的 `async void` 路径）→ 一并纳入 ✓
  （闸门在两阶段之间放开，否则"第一次备份"的第二步会被自己的闸门拒掉 ✗✗）
- **三语 README 的截图在 GitHub 上等于看不见** ✗（3.0.1 把画廊写进了折叠块）→ 恢复为页面顶部**常显** ✓

### Changed / 变更

- **发版说明统一骨架**：v3.0.0 / v3.0.1 / v3.0.2 与本日志统一为
  「一句话 → 新增 / 修复 / 变更 → 产物 → 验证 → 已知边界 → 许可」✓
- 发布前的 main 版本标记为 `3.0.2-dev` ✗ 不冒充已发布的 `3.0.1` ✓

---

## v3.0.1 — 2026-10-03（响应性与界面整理 / responsiveness & UI tidy-up）

### Added / 新增

- **DeepSeek 余额检测**：设置里填入自己的 key（`balance_key`）后，概览页显示**充值余额 / 活动赠送余额**；
  未绑定则整卡隐藏，且**一个网络请求都不发**。CLI 新增只读命令 `balance`。
  **DeepSeek balance** (topped-up / granted) on the overview page once you set your own key; hidden and offline when unset.
- **概览自动刷新**：实时 0.5s / 快 1s / 中 3s / 慢 5s / 暂停 / 自定义（0.5–3600s），只在概览与看板生效。
- **启动页可自选**（`gui_start_page`）。
- **体检逐行出结果**：`doctor --stream` 边算边打 —— 列表先出现，结果一行一行补上。
- **桥接插件 0.2.0**：新增「真在动」信号（相邻两拍之间事件序号变过），长生成中途也判得准。

### Fixed / 修复

- **不再把「挂在 dsh 里」说成「运行中」**：`live` 只表示会话还挂在 dsh 进程内（桌面端开着时它 store 里的会话
  全是 `live`，哪怕几天没碰）→ 现在如实分三态：**运行中 / 挂着 · 最后活动 N 前 / 活动未知**，计数改名「dsh 活跃」；
  插件仍是 0.1.0 时用**最后活动时间**兜底 → 老用户同样吃到这条修复。
- **点击后零反馈**：原来进页要等 CLI 跑完才重画（实测 `doctor` 6.5 秒、`update-center` 7.9 秒里屏幕一动不动）
  → 现在点击**立刻换页**（先显示旧数据）+ 加载指示（如实写为什么等）+ 数据到账淡入 160 毫秒。
- **排队的刷新会提前熄灯** → 改为「还有排队就继续亮」。
- **发布清单名字与包内实际条目对不上**（清单写 `.github/SECURITY.md`，zip 里是 `SECURITY.md`）。
- **两处自检漏检**：`verify_fixes` 的命令面清单、`compare_markers` 的忽略规则（空值键在行尾 Trim 后匹配不到）。
- **`.dsh_launcher_root` 曾被取消跟踪**（发布清单与 zip 仍按路径取它 → `build` job 会失败、发布包会丢卸载闸门）→ 已恢复跟踪。

### Changed / 变更

- **GUI 体积 −33%**：启用 `PublishTrimmed` + `TrimMode=partial`（敢开的原因：界面全部由 C# 构建、**零反射式绑定**）。
  Windows 自包含包 **约 74 MB → 约 50 MB**；GUI 自身压缩包 43.7 → 19.3 MB。刻意**不做** `PublishSingleFile`
  （只再多省 3.5 MB，代价是首次运行要把原生库解包到临时目录）。顺手删掉从未被引用的 `Avalonia.Xaml.Behaviors`。
- **CI 新增 GUI 窗口冒烟**：真的启动打包后的 GUI，要求活过 10 秒**且真的创建了窗口**（此前没有任何 job 会启动 GUI）。
- **设置页分类 + 选择框**：「界面与启动（含排障开关）」「dsh · 更新 · 数据 · 余额」「其它」；枚举型配置一律下拉选择。
- **体检不再自动运行**：进页只显示上次结果，点「运行体检」才跑。
- **形态与插件页的安装教程移到底部。**
- **v2 整棵树移入 `v2/`**：仓库根条目 **24 → 14**；不影响任何已发布产物（v2 发布包自带 `verify.ps1`，
  公钥回退按不变的 **tag** 下载）✓
- **文档统一**：三语 README 重写为同一套骨架（动机 / 功能 / 安装 / 快速上手 / 图形界面 / 命令行 / 安全与隐私 /
  已知限制 / 可选插件 / 许可与致谢）；发版说明与本日志统一为 Added / Fixed / Changed 骨架。

---

## v3.0.0 — 2026-10-02（首个正式版 / first stable release）

> 这个标题原先写的是「未发布 / Unreleased」—— 实际上 v3.0.0 已正式发布
> （GitHub 上 `isPrerelease = false`，且标记为 Latest）。

### Added / 新增（V3 工具箱线 · 2026-09-29）

- **V3 命令行（`dsh-minato`）**：31 个具名命令 + 无参数数字菜单 —— 安装/更新/卸载、起停、状态、体检、会话、profile 扫描与处方、备份/恢复/导出/删除、配置读写、引导诊断、自检、快捷方式、`about`，以及新增的 `log`、`update-info`、`import`、`wipe`、`verify-install`。
  **V3 CLI (`dsh-minato`)**: 31 named commands plus a no-argument numeric menu, including the new `log`, `update-info`, `import`, `wipe` and `verify-install`.
- **Linux 支持（x86-64）**：CLI 全命令可用；免 sudo 一键装 Node（curl/wget/python3 任一，按 CPU 架构选包）；一键起停按**可观测事实**判定；`shortcut` 写应用菜单项；备份/恢复**含工作区**。
  **Linux support (x86-64)**: every CLI command works; one-click Node bootstrap without sudo (architecture aware); start/stop judged by observation; `shortcut` writes an application-menu entry; backup/restore include the workspace.
- **跨平台一致性**：在**同一夹具**下逐命令核对标记行键集合，Windows 与 Linux **完全一致**（方法与结果见 `docs/LINUX-TEST.md`）。
  **Cross-platform parity**: marker key sets verified command by command against the same fixture, identical on Windows and Linux.
- **发布链**：CI 新增 `v3-linux` job（两份 CLI + 自包含 GUI + `.desktop` + 图标 + 冒烟脚本 + sha256），对产物跑冒烟与 `verify-linux.sh` 校验，Windows 发布改为**依赖该 job**（Linux 失败即拦住发布）；Linux 产物同样进入 attestation。
  **Release chain**: a new `v3-linux` CI job publishes the Linux artifacts, smoke-tests and verifies them, and the Windows release now depends on it; Linux artifacts are attested too.
- **诚实性修复（本版重点）**：`profilecheck` 读不到目录时**不再谎报没有问题**（改为如实报不完整，诊断行在 `--diag` 下给出）；更新**失败不再被报成成功**（判据改为观测到的版本 == 请求的版本）；`status` 拿不到 PID 时说明原因而非静默 0；`--dry-run` 预览不再把工作区指向数据根。
  **Honesty fixes (the theme of this release)**: `profilecheck` no longer reports "no problems" when it could not read the files; a failed update is no longer reported as success; `status` explains a missing PID instead of printing a silent 0; the `--dry-run` preview no longer aims workspaces at the data root.
- **更新安全网**：更新前自动做 `-pre-update` 备份（被替换的版本记在同级 `.version` 旁挂文件），失败自动回滚；`--version` 指定版本、`--list` 列出版本；`update_channel`（stable/rc）**真正生效**（stable 无非预发布版时**如实说明回退**，不静默交付 rc）。
  **Update safety net**: an automatic `-pre-update` backup (with the replaced version in a sibling `.version` file) and rollback on failure; `--version` and `--list`; `update_channel` is honoured, and a stable channel with no non-prerelease version says so instead of silently handing over an rc.
- **跨机迁移**：`import` 把外部备份包导入本机备份根（先做 `-pre-import` 安全备份），`backup-export` 产出的包在另一台机器上**可直接恢复**。
  **Migration**: `import` brings an external package into the local backups root, so a package from `backup-export` can be restored on another machine.

### Changed / 变更

- **写操作一律先计划后执行**：`backup-export` / `backup-delete` / `import` / `wipe` / `restore --apply` / `profilepatch` / `start` / `stop` / `shortcut` 都需要显式 `--yes`；`wipe` 另有五重闸门（含没备份就不清、备份根在数据根内则拒绝）。
  **Every write prints a plan first and needs `--yes`**; `wipe` carries five gates, including no backup, no wipe.
- `keep_backups` 配置**真正生效**（此前硬编码为 3）；`.sh` 脚本行尾由 `.gitattributes` 锁定为 LF；经典 `v2.x` 线未改动（发布链不变量全绿）。
  The `keep_backups` setting is actually honoured; `.gitattributes` pins shell scripts to LF; the classic v2.x line is untouched.

### Known limitations / 已知限制

- **GUI 图形面板**：Avalonia 面板可编译并运行，但**一键启动/停止、运行检查、隔离/恢复**四个动作仍调用 Windows 专有的经典核心 —— 底层 CLI 在 Linux 上已可用，属**接线**待办。
  **GUI panel**: the Avalonia panel builds and runs, but four actions still call the Windows-only classic core; the CLI underneath already works on Linux.
- GUI 未内嵌 CJK 字体（Linux 上可能显示方块）；日志/更新中心、托盘与快捷键目前是 CLI 形态，GUI 页面尚未接上。
  No embedded CJK font in the GUI; the log/update centre, tray and shortcuts exist as CLI commands but are not wired into GUI pages yet.
- 经典 v2.x 线若在 Linux 上从源码构建，`start` / `stop` / `shortcut` 接缝不可用；Linux 上请使用 V3 CLI。
  The classic v2.x line's start/stop/shortcut seams do not work if it is built from source on Linux; use the V3 CLI there.

## v2.8.0 — 未发布 / Unreleased

### Docs / 文档（2026-09-28）

- **README 补上「插件加载失败」一节**（EN/zh）：把 `profilepatch --disable`（v2.7.2 的通用隔离处方）写进文档——此前只在发行说明里提过，README 一直缺；同时把「工具箱不是 dsh 插件（独立进程、不注入 dsh、dsh 没装也能用）」写清楚，避免被误当成插件安装。
  **README gained a "plugin failed to load" section** (EN/zh) documenting `profilepatch --disable` (the v2.7.2 generic quarantine prescription, previously only in the release notes) and stating plainly that the toolkit is **not** a dsh plugin.
- 仓库 description/topics 更新为**排障/数据保护**向关键词（diagnostics / troubleshooting / backup-restore / disaster-recovery / data-integrity / checksum-verification / profile / cordis）。
  Repository description/topics now use troubleshooting and data-protection keywords.

### Changed / 变更（GUI 呈现层基座 · 2026-09-28）

- **任务即信号**：GUI 新增全局信号总线（`SignalBus` / `Sig` / `SigRouting`）。任务开始/成功/失败/超时、服务状态变化、日志追加都广播为信号，"哪个信号刷新什么"变成一张**路由表**（`SigRouting.ActionsFor`），不再散落在各处直接调用（原来 `OnCaptureDone` 里硬编码了"更新按钮 + 刷新备份列表 + 弹托盘气泡"）；`SetStatus` 也不再直接驱动按钮可用性。
  **Tasks as signals**: the GUI gained a global signal bus (`SignalBus` / `Sig` / `SigRouting`). Task start/ok/fail/timeout, service-state changes and log appends are broadcast as signals, and "which signal refreshes what" is now a **routing table** (`SigRouting.ActionsFor`) instead of hard-coded calls scattered around.
- **设置项卡片化**：设置页由数据渲染（`SettingsCards.Default()`：4 张卡片 / 9 个设置项，含配置键、文案键、控件形态与取值）。加设置项只改模型，不碰布局代码。
  **Settings as cards**: the settings page renders from data (`SettingsCards.Default()`: 4 cards / 9 items with config key, label key, control kind and options).
- **GUI 逻辑测试 + i18n 强制检查**（新文件 `tests\gui_logic_tests.cs`，零第三方依赖，CI 每次推送都跑）：标记行解析、信号总线（含"一个订阅者抛异常不影响其他订阅者"）、路由表、设置卡片模型，以及**源码级 i18n 强制检查**（`L10N._()` 用到的键必须有定义；定义过的键不能是死键；中英文都不能为空；**L10N 字典之外不允许出现中文字符串字面量**）。
  **GUI logic tests + i18n enforcement** (new `tests\gui_logic_tests.cs`, zero third-party deps, run by CI on every push): marker parsing, the signal bus (incl. "one throwing subscriber must not break the others"), the routing table, the settings card model, and **source-level i18n enforcement**.

### Fixed / 修复（GUI）

- **导出/删除备份、保存设置后界面一直显示"失败"** —— 标记行名单漏了 `BKEXPORT_OK` / `BKDEL_OK` / `CONFIGSET_OK`（以及 `DRYRUN_*` / `DOCTOR_*` 等），`FindMarker` 找不到标记 → 判定为失败，托盘成功气泡也不会弹。现在已知标记行集中成一张表（`Markers`），并加了回归测试。
  **Export/delete backup and saving settings always reported "failed"** — the marker list was missing `BKEXPORT_OK` / `BKDEL_OK` / `CONFIGSET_OK` (and `DRYRUN_*` / `DOCTOR_*`), so `FindMarker` returned nothing and the UI treated it as a failure. Known markers now live in one table (`Markers`) with regression tests.
- **两处硬编码中文**：关于页的署名两行、以及"已在运行。"提示在英文界面下仍是中文 → 已补 `about.credits` / `app.alreadyrunning` 两个 L10N 键（由上面的 i18n 强制检查兜住）。
  **Two hard-coded Chinese strings**: the About page credits and the "already running" message stayed Chinese in the English UI → new `about.credits` / `app.alreadyrunning` keys (now guarded by the i18n enforcement test).

### Changed / 变更（重构 · 阶段 1：分层，已完成 · 2026-09-21）

- **单文件核心拆成 Core / Platform / Cli 三层**（落地：`dsh_v2.cs` 4212 行 → **1344 行** + `src/**` 9 个文件共约 3050 行；提交 `ee36ac0`，CI 绿）（`dsh_v2.cs` → `dsh_v2.cs` + `src/**`）。这是**只搬不改**的重构：用 `partial class Program` 把同一个类分散到多个文件，**不改变任何调用点、签名、字符串、注释或行为**，也不引入任何依赖或 Unix 代码。
  **The single-file core is split into Core / Platform / Cli layers** (`dsh_v2.cs` → `dsh_v2.cs` + `src/**`). This is a **move-only** refactor: `partial class Program` spreads the same class across files, **without changing a single call site, signature, string, comment or behaviour**, and without adding any dependency or any Unix code.

  分层（阶段 1 目标布局）：`src/Core/Program.{Config,Backup,Doctor,Profile,Integrity,Update,Util}.cs`（平台无关）、`src/Platform/Windows/Program.Platform.cs`（P/Invoke、端口/进程探测、桌面与快捷方式、StateDir/DataRoot、控制台辅助）、`src/Cli/Program.Cli.cs`（`Main`、菜单、各 `*Cli` 非交互命令）。`dsh_v2.cs` 保留文件头、`using`、程序集属性与 `#if UNIT` 测试代理块；**没被归类命中的成员一律留在 `dsh_v2.cs`**（宁可少搬，不可搬坏）。
  Layering: platform-agnostic `src/Core/**`, Windows-specific `src/Platform/Windows/**`, entry points in `src/Cli/**`. `dsh_v2.cs` keeps the header, `using`s, assembly attributes and the `#if UNIT` test proxies; **any member not matched by the mapping stays in `dsh_v2.cs`**.

  门禁（全部必须通过才算完成）：四形态 `csc /warn:4` 编译 0 错误 0 新增警告 · 单元测试 **297/297** · 集成测试 **33/33** · 成员签名多重集与字符串字面量多重集与原文件**完全一致**（机器证明「只搬不改」）· 每个 `.cs` 保持 **UTF-8 无 BOM + CRLF**。
  Gates: four build variants compile with 0 errors / 0 new warnings; unit tests **297/297**; integration **33/33**; member-signature and string-literal multisets **identical** to the original (machine proof of move-only); every `.cs` stays **UTF-8 without BOM, CRLF**.

- 阶段 1 **不动** `gui_v2.cs`、`tests/unit_tests.cs`、`verify.ps1`；构建脚本与 CI 的源文件列表同步更新（`build_exe.cmd`、`.github/workflows/build-release.yml`）。
  Stage 1 leaves `gui_v2.cs`, `tests/unit_tests.cs` and `verify.ps1` untouched; the build script and the CI source lists are updated to match.

### Tests / 测试

- 本阶段**不新增**测试用例：单元测试仍是 **297** 项，作为「行为未变」的验收网。
  No new test cases in this stage: the suite stays at **297** and acts as the regression net proving behaviour did not change.

---

## v2.7.2 — 2026-09-20 —（新增：`profilepatch --disable` 手动隔离出问题的插件 / Manual Quarantine of a Failing Plugin）

### Added / 新增

- **`profilepatch --disable`：手动隔离出问题的插件（第二条处方）** — 在 profile 的 `cordis.patch.yml` **末尾追加**一个顶层补丁项（只追加，不改动任何已有字符）：
  ```yaml
  - id: <条目 id>
    disabled: true
  ```
  与 `maxDepth` 处方的区别：那条让插件**继续可用**（外科修复），这条是**通用兜底**——任何坏插件都能先隔离掉再排查。**只做用户显式触发的手动操作，绝不自动执行**；同样遵守既有证据链：幂等（已隔离则 `PROFILEPATCH_NOOP`）→ 预览（不带 `--yes` 时 `PROFILEPATCH_DRYRUN`，零写入）→ 先备份 → 写入 → 写后复检 → 失败自动回滚（`PROFILEPATCH_ROLLBACK`，逐字节还原）。id 做字符集白名单（`[A-Za-z0-9._@/-]`），带换行/冒号的 id 一律 `bad-id` 拒绝，防止往补丁文件里注入 YAML。
  **`profilepatch --disable` — manually quarantine a failing plugin (second prescription)** — appends a top-level patch item to the end of the profile's `cordis.patch.yml` (append-only; not one existing character is modified). Unlike the `maxDepth` prescription (which keeps the plugin working — a surgical fix), this is the generic fallback: any broken plugin can be quarantined first and diagnosed after. It is **manual-only and never runs automatically**, and it follows the same evidence chain: idempotent (`PROFILEPATCH_NOOP`) → preview (`PROFILEPATCH_DRYRUN`, zero writes without `--yes`) → backup first → write → re-check → automatic byte-identical rollback on failure (`PROFILEPATCH_ROLLBACK`). The entry id is charset-whitelisted (`[A-Za-z0-9._@/-]`); ids containing newlines or colons are refused as `bad-id`, which prevents YAML injection into the patch file.

  依据（不使用任何第三方项目的实现或配置格式）：`disabled` 是 **dsh 补丁层自身的一等能力** —— `@deepseek-ai/cordis-plugin-include` 的 `PatchOptions.disabled?: boolean | null`，且 dsh 自身（`profile-boot` 的 `resolveTelemetryPatch`）就用它关闭遥测行。同一补丁列表里 `insert:` 插入的行会被索引，因此后续的 `id` 补丁能命中它，必须追加在文件末尾。
  Basis (no third-party implementation or config format is reused): `disabled` is a **first-class capability of dsh's own patch layer** — `PatchOptions.disabled?: boolean | null` in `@deepseek-ai/cordis-plugin-include`, and dsh itself uses it (see `resolveTelemetryPatch` in `profile-boot`) to switch off its telemetry row. Rows inserted by `insert:` are indexed within the same patch list so a later `id` patch can target them, which is why the item is appended at the end of the file.

### Tests / 测试

- 单元测试 **284 → 297**：隔离处方的规划（只追加、末尾形态正确、写入后可检出、幂等 NOOP、未知 id 拒绝、id 注入拒绝、字符集白名单）与写入路径（验证失败回滚且逐字节还原、写入后磁盘可见、二次执行为 NOOP）。
  Unit tests **284 → 297**: the quarantine prescription's planning (append-only, correct trailing form, detectable after write, idempotent NOOP, unknown id refused, injected id refused, charset whitelist) and its write path (rollback restores bytes exactly, visible on disk after apply, second run is a NOOP).

---

## v2.7.1 — 2026-09-18 —（修复：GUI 桌面快捷方式指向 GUI · 版本号对齐 / GUI Desktop Shortcut Fix & Version Alignment）

### Fixed / 修复

- **GUI 的「桌面快捷方式」建出来的是 CLI 的快捷方式** — `CreateDesktopShortcut` 此前固定指向核心 exe、固定命名 `dsh-minato.lnk`，所以在 GUI 里点这个按钮得到的是命令行程序的快捷方式。现在 `shortcut` 支持 `--exe <目标>` / `--name <基名>` / `--desc <描述>`，GUI 传自己的 exe 与 `dsh-minato GUI` 基名：GUI 建出的快捷方式指向 GUI 自己，且与 CLI 的快捷方式**并存不互相覆盖**。核心 CLI 的默认行为不变（仍指向核心自己）。
  **GUI's "Desktop Shortcut" created a CLI shortcut** — `CreateDesktopShortcut` was hardcoded to the core exe and to the name `dsh-minato.lnk`, so clicking that button in the GUI produced a shortcut to the command-line program. `shortcut` now accepts `--exe <target>` / `--name <base name>` / `--desc <description>`, and the GUI passes its own exe plus the `dsh-minato GUI` base name, so the GUI shortcut points at the GUI and the two coexist without overwriting each other. The core CLI's default behaviour is unchanged (still points at the core).
  - 顺带加固：目标必须是**存在的 .exe 文件**（否则 `SHORTCUT_FAIL` 且不落文件）；快捷方式基名做净化（只取文件名部分、剔除 `\ / : * ? " < > |` 与控制字符、最长 80 字），**路径分隔符一律剔除**，防止写出桌面目录之外。
  - Hardening along the way: the target must be an existing `.exe` (otherwise `SHORTCUT_FAIL` and nothing is written), and the base name is sanitized (file-name part only, `\ / : * ? " < > |` and control characters stripped, max 80 chars) so a crafted name cannot escape the desktop directory.

### Tests / 测试

- 单元测试 **277 → 284**：新增自定义目标/基名、GUI 快捷方式与 CLI 快捷方式互不覆盖、基名净化（`..\..\evil` → `evil`、非法字符剔除）、空名拒绝、目标不存在拒绝。
  Unit tests **277 → 284**: custom target/base name, GUI vs CLI shortcut coexistence, base-name sanitizing (`..\..\evil` → `evil`, invalid characters stripped), blank name rejected, missing target rejected.

---

## v2.7.0 — 2026-09-18 —（托盘 · 关闭行为 · 状态栏 · 快捷键 · 验证此安装 / Tray, Close Behavior, Status Bar, Shortcuts & Verify This Install）

### Added / 新增

- **Tray icon & close-behavior memory (GUI)** — a tray icon (Show Window / Start dsh / Stop dsh / Exit) and one-time close prompting: the first time you close the window it asks whether to minimize to tray or exit directly and remembers the answer in `close_action` (`ask | tray | exit`; empty = never asked), changeable later on the Settings page.
  **托盘图标与关闭行为记忆（GUI）**——托盘菜单（显示主窗口 / 启动 dsh / 停止 dsh / 退出）；首次关窗只问一次「最小化到托盘 / 直接退出」并把答案记入 `close_action`（`ask | tray | exit`，空=还没问过），之后可在设置页修改。
- **Bottom status bar & keyboard shortcuts (GUI)** — the status bar shows service state, PID, uptime, current theme and language plus shortcut hints; `Ctrl+1`~`Ctrl+7` switch pages, `F5` refreshes status, `Ctrl+B` runs a backup (text-input fields keep their own keys).
  **底部状态栏与快捷键（GUI）**——状态栏显示服务状态 / PID / 运行时长 / 当前主题 / 语言与快捷键提示；`Ctrl+1`~`Ctrl+7` 切页、`F5` 刷新状态、`Ctrl+B` 立即备份（文本输入框内按键不受影响）。
- **New config keys `close_action` / `auto_start`** — both whitelisted in `config-set` and reported by `config-get`; `auto_start=off` turns off the interactive menu's 5-second auto-start countdown (the menu then waits for a manual choice and says so).
  **新配置键 `close_action` / `auto_start`**——两者均加入 `config-set` 白名单、由 `config-get` 报告；`auto_start=off` 关闭交互菜单的 5 秒自动启动倒计时（改为明确提示、等待手动选择）。
- **Read-only `status --detail`** — still prints the three-state marker line (`STATUS_UP` / `STATUS_STARTING` / `STATUS_DOWN`) and adds `STATUS_PID`, `STATUS_START`, `STATUS_UPTIME`; it feeds the GUI status bar and writes nothing.
  **只读 `status --detail`**——仍输出三态标记行（`STATUS_UP` / `STATUS_STARTING` / `STATUS_DOWN`），并追加 `STATUS_PID` / `STATUS_START` / `STATUS_UPTIME` 三行；作为 GUI 状态栏数据源，全程不写任何东西。
- **Doctor: 7th category `Integrity`** — compares the running exe against the bundled `hashes.txt`: match / mismatch (reported as an error) / no manifest found (normal for a single copied exe).
  **体检新增第 7 类 `Integrity`**——把运行中的 exe 与随包 `hashes.txt` 比对：一致 / 不一致（按错误报告）/ 未找到清单（单独复制 exe 属正常）。
- **About page: "Verify This Install"** — downloads the official `hashes.txt` (plain text: no JSON parsing, no third-party dependency) and compares the SHA-256 of the core exe and the GUI exe against it, with three states (match / mismatch / could not verify). It is explicitly a **hash-consistency** check, not signature verification.
  **关于页「验证此安装」**——下载官方 `hashes.txt`（纯文本：不解析 JSON、无第三方依赖），比对核心 exe 与 GUI exe 的 SHA-256，三种结果（一致 / 不一致 / 未能验证）；明确标注为「哈希一致性」比对，**不是**签名验证。
- **GUI startup & first-run checks** — every launch does a local consistency check against the bundled `hashes.txt` (local only, no network) and detects the "Mark of the Web" (downloaded-from-internet) marker, which only writes a log line; the very first launch runs an integrity self-check **only** — deliberately no environment inventory and no backup nagging, since a fresh machine has none of that yet.
  **GUI 启动检查与首次启动**——每次启动都做随包 `hashes.txt` 的本地一致性检查（纯本地、不联网），并检测「来自网络」标记（Mark of the Web），只记一行日志；**首次**启动只做完整性自检——刻意不列环境清单、不谈备份（新机器上这些本来就不存在）。
- **Toast feedback & unified restore flow (GUI)** — backup / restore / export / delete results are reported as tray balloon toasts; the "Restore" action switches to the Backup page (select → Dry-Run preview → confirm) instead of opening a second picker dialog, and restoring while the service is running is blocked up front with a clear reason.
  **气泡反馈与恢复流程统一（GUI）**——备份 / 恢复 / 导出 / 删除结果改用托盘气泡提示；「恢复」不再弹第二个选择框，而是切到备份页（选择 → Dry-Run 预演 → 确认）；服务运行中恢复会在开始前直接阻止并说明原因。
- **Home & Settings page updates (GUI)** — Home's Start and Install/Repair buttons swapped so Start is top-left, and the install button label follows detection ("Install dsh" when dsh is missing, "Repair dsh" when it is present); the Settings page gained two dropdowns (menu auto-start countdown; close-window behavior), both in the Toolkit group.
  **首页与设置页调整（GUI）**——首页「启动」与「安装/修复」按钮对调，「启动」位于左上；安装按钮文案跟随检测结果（未装 dsh 显示「安装 dsh」，已装显示「修复 dsh」）；设置页新增两个下拉项（菜单倒计时自动启动 / 关闭主窗口时），同属「工具箱」组。
- **Fewer processes, less dead code (GUI)** — the dsh version is cached and re-read only every 10th poll (plus right after operations) instead of spawning a process every 3 seconds; the now-redundant restore dialogs were removed (−348 lines) along with 18 unused localization keys.
  **少起进程、清理死代码（GUI）**——dsh 版本改为缓存，每 10 次轮询才重读一次（操作后立即重读），不再每 3 秒起一个进程；移除已冗余的恢复对话框（−348 行）与 18 个无用本地化键。
- **Boot-failure triage CLI — `profilecheck` / `bootdiag` / `profilepatch`** — three zero-dependency commands for the "dsh will not start" case (aliases `pc` / `bdiag` / `pp`). `profilecheck` statically scans `~/.dsh/profiles/**/*.yaml|*.yml` and reports the profile entries that make dsh fail to boot (`PROFILECHK_WARN <file> <line> <id> <key> <hint>`, `PROFILECHK_TOTAL <warnings> <files>`, `PROFILECHK_SKIPPED_VENDOR <n>`, `PROFILECHK_OK`, plus a machine-readable `PROFILECHK_FIX` line with `--abs`); `--dir` / `--file` pick another target, and `--vendor` also scans `node_modules` (skipped by default — those are package-shipped patch files). It also flags `@deepseek-ai/dsh-mcp-client` entries with `failOnStartupError: true` whose `command:` points at a missing file (report-only, never auto-fixed). `bootdiag --from <captured.txt>` parses captured dsh startup output and extracts the innermost cause from the error chain (`BOOTDIAG_OK` / `BOOTDIAG_KIND` / `BOOTDIAG_PLUGIN` / `BOOTDIAG_ENTRY` / `BOOTDIAG_FILE` / `BOOTDIAG_LINE` / `BOOTDIAG_HINT`); an unknown error prints `BOOTDIAG_KIND unknown` plus the first error line — it never guesses. `profilepatch --file <yaml> --id <entry> --set maxDepth=provider-managed [--yes]` is the controlled write: a one-line plan (`PROFILEPATCH_PLAN`), no write without `--yes` (`PROFILEPATCH_DRYRUN`), a backup into `<toolkit dir>\backup\bootdiag-<timestamp>\` before writing, exactly one inserted line with matching indentation, idempotent on a second run (`PROFILEPATCH_NOOP`), and a rescan with automatic rollback if verification fails (`PROFILEPATCH_ROLLBACK`). It accepts that single key/value pair only — deliberately not a general YAML editor.
  **启动失败诊断与修复命令 `profilecheck` / `bootdiag` / `profilepatch`**——三个零依赖命令，专治「dsh 起不来」（别名 `pc` / `bdiag` / `pp`）。`profilecheck` 静态扫描 `~/.dsh/profiles/**/*.yaml|*.yml`，报出会让 dsh 启动失败的 profile 条目（`PROFILECHK_WARN <文件> <行> <id> <键> <提示>`、`PROFILECHK_TOTAL <告警数> <文件数>`、`PROFILECHK_SKIPPED_VENDOR <n>`、`PROFILECHK_OK`；带 `--abs` 时另给机器可读的 `PROFILECHK_FIX` 行）；`--dir` / `--file` 可换扫描目标，`--vendor` 才一并扫描 `node_modules`（默认跳过——那是包自带的补丁文件）。它还会标记 `@deepseek-ai/dsh-mcp-client` 中 `failOnStartupError: true` 但 `command:` 指向不存在文件的条目（**只报不修**）。`bootdiag --from <捕获的启动输出.txt>` 解析启动输出、从错误链里取最内层病灶（`BOOTDIAG_OK` / `BOOTDIAG_KIND` / `BOOTDIAG_PLUGIN` / `BOOTDIAG_ENTRY` / `BOOTDIAG_FILE` / `BOOTDIAG_LINE` / `BOOTDIAG_HINT`）；识别不了时报 `BOOTDIAG_KIND unknown` 并附第一条错误行——绝不猜测。`profilepatch --file <yaml> --id <条目> --set maxDepth=provider-managed [--yes]` 是受控写入：先给一行计划（`PROFILEPATCH_PLAN`），没有 `--yes` 绝不落盘（`PROFILEPATCH_DRYRUN`），写入前先备份到 `<工具箱目录>\backup\bootdiag-<时间戳>\`，只按同级缩进插入一行，二次运行幂等（`PROFILEPATCH_NOOP`），写入后复扫、校验不过自动回滚（`PROFILEPATCH_ROLLBACK`）。它只接受这一个键值对——刻意不做通用 YAML 改写器。
- **Doctor page: "Config Check / 配置自检" (GUI)** — a new Doctor-page button runs `profilecheck`; when fixable risks are found it lists them and asks for confirmation, then backs them up and fixes them through `profilepatch` and rescans (result reported as a toast plus log lines). The scan is read-only, and the fix backs up first and rolls back automatically if verification fails.
  **体检页新增「配置自检」按钮（GUI）**——体检页新按钮运行 `profilecheck`；发现可修风险时先列出警告并弹框确认，确认后经 `profilepatch` 先备份再修复，随后复扫（结果以气泡 + 日志反馈）。扫描全程只读；修复先备份、校验失败自动回滚。

### Fixed / 修复

- **GitHub HTTPS requests failed on .NET Framework** — the default `SecurityProtocol` did not include TLS 1.2, so any HTTPS request to GitHub (update check, integrity check) failed with "could not create SSL/TLS secure channel"; TLS 1.2 is now enabled explicitly.
  **.NET Framework 下 GitHub HTTPS 请求失败**——默认 `SecurityProtocol` 不含 TLS 1.2，导致所有发往 GitHub 的 HTTPS 请求（更新检查、完整性检查）报「无法创建 SSL/TLS 安全通道」；现已显式启用 TLS 1.2。
- **Status bar text ghosting** — on some themes the transparent status label picked up pixels from the title bar and left artifacts; it is now drawn with an opaque surround colour.
  **状态栏文字残影**——部分主题下透明标签会拾取标题栏像素、留下残影；改为不透明底色绘制。
- **Toolkit version wording drift** — the version the GUI displays is now kept aligned with the core version (the two had drifted apart in wording).
  **工具箱版本文案漂移**——GUI 显示的版本与核心版本对齐（此前两者措辞已漂移不一致）。

### Tests / 测试

- Unit tests **225 → 244** — `close_action` / `auto_start` configuration whitelist (accepted values, empty `close_action`, case-insensitive key name, rejected values) and status-bar uptime formatting (`FormatUptime` across the second / minute / hour / day boundaries). Integration tests unchanged at **33**; CI still runs both plus the three-variant GUI compile guard.
  单元测试 **225 → 244**——`close_action` / `auto_start` 配置白名单（合法值、`close_action` 空值、键名大小写不敏感、非法值拒绝）与状态栏运行时长格式化（`FormatUptime` 的秒 / 分 / 时 / 天边界）。集成测试维持 **33**；CI 仍跑两套测试与三形态 GUI 编译守卫。
- Unit tests **244 → 277** — profile block scanning (a missing `maxDepth`, an `mcp-client` entry whose `command` file does not exist, the package-patch skip, a nested `- id:` not mistaken for a new entry), boot-output parsing (`file:///…#entry` → path + line, the innermost cause of a nested error chain, unknown errors reported as `unknown` with the first error line), and the controlled patch path (plan / idempotent NOOP / unknown entry refused, backup taken before writing, exactly one inserted line at sibling indentation, rescan + rollback when verification fails, BOM preserved). Integration tests unchanged at **33**; CI still runs both plus the three-variant GUI compile guard.
  单元测试 **244 → 277**——profile 块扫描（缺 `maxDepth`、`mcp-client` 条目 `command` 指向不存在文件、包内补丁跳过、嵌套 `- id:` 不被误判为新条目）、启动输出解析（`file:///…#entry` → 路径 + 行号、多层错误链取最内层病灶、未知错误报 `unknown` 并附首条错误行）与受控写入（计划 / 幂等 NOOP / 未知条目拒绝、写入前备份、只插一行且缩进同级、复扫失败回滚、BOM 保留）。集成测试维持 **33**；CI 仍跑两套测试与三形态 GUI 编译守卫。

> ⚠️ 非官方工具，与 DeepSeek 官方无关。Unofficial community tool, not affiliated with DeepSeek.

---

## v2.6.0 — 2026-09-15 —（备份管理器 · Dry-Run · 更新中心 · 设置 · 日志中心 / Backup Manager, Dry-Run, Update Center, Settings & Log Center）

### Added / 新增

- **Seven-page GUI** — navigation is now Home / Backups / Update / Settings / Log / Doctor / About (v2.5.0 had four pages).
  **七页导航**——首页 / 备份 / 更新 / 设置 / 日志 / 体检 / 关于（v2.5.0 为四页）。
- **Backup Manager (GUI "Backups" page)** — every backup listed as a row (time / kind / size / validity) with Restore / Export / Delete for the selection and one-click Backup Now; list auto-refreshes after changes.
  **备份管理器（GUI「备份」页）**——每条备份一行（时间/类型/大小/有效性）+ 恢复/导出/删除 + 立即备份；变更后自动刷新。
- **Dry-Run before destructive operations** — `restore [--path X] --dry-run` prints a machine-readable merge plan (`DRYRUN_NEW/OVERWRITE/KEEP/BYTES/TOTAL`; merge semantics: destination-only files are never deleted); GUI restore shows the plan in a confirm dialog before doing anything; interactive wipe previews delete counts (files / dirs / total size) before the two-step confirmation. The preview shares the execution-side skip rules (node_modules / nested backups / symlink·junction not followed), so the numbers are what actually happens — including the restore-side distinction between a top-level and a nested skipped directory.
  **破坏性操作前 Dry-Run**——`restore --dry-run` 输出机器可读合并计划（合并语义：仅目标端文件不删）；GUI 恢复先弹预演确认；交互清除在两步确认前预演删除量。预演与执行共用同一套跳过规则（node_modules / 嵌套备份 / symlink·junction 不跟随），**所见即所得**（含恢复侧顶层与嵌套被跳过目录的区别）。
- **Update Center (GUI "Update" page)** — read-only visualization of the whole update picture: current dsh version, latest stable / latest rc (npm), update channel (`update_channel=stable|rc`), most recent pre-update backup, rollback candidates (valid backup count), dsh release-notes link. Network failures degrade to `unknown`, never block. "Update dsh…" still routes through the interactive flow (version list + destructive-action double confirmation): **checks may be automatic, updates never are**.
  **更新中心（GUI「更新」页）**——只读可视化：当前版本 / 最新 stable / 最新 rc / 通道 / 更新前备份 / 回滚候选 / 发布说明链接；网络失败降级不阻断；更新仍走交互双确认（检查可自动，更新永不自动）。
- **Settings page (GUI "Settings")** — four groups (Harness / Backup / Update / Toolkit): web host, workspace path, auto-backup retention (≥3), startup update check, dsh update detection, update channel, UI language. **Save submits only changed keys** (invalid values are refused core-side); the config file stays plain `key=value` (cross-platform friendly).
  **设置页（GUI「设置」）**——四组控件（Harness / 备份 / 更新 / 工具箱）：Web 主机、工作区路径、自动备份保留份数（≥3）、启动更新检查、dsh 更新检测、更新通道、界面语言；**保存只提交变化项**（非法值核心侧拒绝）；配置保持 `key=value`（跨平台友好）。
- **Log Center (GUI "Log" page)** — the Log page is now a structured operation log: every entry carries a level (`INFO / WARN / ERROR`) and timestamp; one-click level filters, live search, **Export** (UTF-8 file) / **Copy**; failures (timeouts, refused operations, missing core) are logged as WARN/ERROR.
  **日志中心（GUI「日志」页）**——结构化操作日志：级别（INFO/WARN/ERROR）+ 时间戳、级别筛选、实时搜索、导出（UTF-8）/复制；失败（超时、被拒绝的操作、核心缺失）记 WARN/ERROR。
- **New CLI** — `backup-list --detail` (kind / size / mtime per backup as `BACKUP_ITEM` lines), `backup-export --path <bk> --to <dir>` (copy-out), `backup-delete --path <bk>` (restricted to backups root `dsh-data-*`, audit-logged), `restore … --dry-run` (read-only merge preview), `update-info` (`UPDATEINFO_*` read-only data source), `config-get` / `config-set <key> <value>` (whitelisted read/write).
  **新命令**——`backup-list --detail`、`backup-export`、`backup-delete`（限备份根内、写审计日志）、`restore … --dry-run`、`update-info`、`config-get` / `config-set`（白名单读写）。

### Tests / 测试

- Unit tests **183 → 225** — dry-run merge/delete planning (incl. restore-side skip-rule fidelity for top-level vs nested `node_modules`), backup kind parsing, export & delete validation, rollback-candidate lookup, configuration whitelist. Integration tests unchanged at **33**; CI runs both plus the three-variant GUI compile guard.
  单元测试 **183 → 225**——Dry-Run 合并/删除计划（含恢复侧顶层 vs 嵌套 `node_modules` 跳过规则一致性）、备份类型解析、导出与删除校验、回滚候选查询、配置白名单。集成测试维持 **33**；CI 另跑两套测试与三形态 GUI 编译守卫。

---

## v2.5.0 — 2026-09-14 —（体检 / Doctor + 防篡改加固 / Diagnostics & supply-chain hardening）

### Added / 新增

- **Doctor / health check** — `doctor` CLI command and a GUI **Doctor** page (nav: Home / Log / Doctor / About). Read-only six-category check: System (Windows / Node / npm), Harness (installed & version), Service (port, listener identity, HTTP, 3-state), Workspace (path, permissions, size), Backup (dir, latest, age), Network (registry reachability). Machine-readable verdict line `DOCTOR_OK 0 | DOCTOR_WARN n | DOCTOR_ERROR n`; `doctor --report <file>` exports a full diagnostic report with API keys / tokens / cookies / passwords **redacted**.
  **体检 / Doctor**：`doctor` 命令 + GUI 体检页。只读六类检查（系统 / Harness / 服务 / 工作区 / 备份 / 网络），机器可读结论行；`--report` 导出**脱敏**诊断报告。
- **Self-integrity gate** — uninstall (incl. wipe), restore and dsh-update now refuse to run when a `hashes.txt` ships beside the executable and the executable's SHA-256 does not match it (tamper protection); builds without a manifest are not blocked.
  **自身完整性闸门**：卸载（含清除）/恢复/更新前自检 SHA-256，与随包 manifest 不符即拒绝；无 manifest 不阻断。
- **Supply-chain hardening** — immutable releases enabled; tag & main rulesets (no delete / no history rewrite, bypass never); CI least-privilege permissions; actions pinned to commit SHAs; build-provenance attestations on tag builds; release flow switched to draft → attach → publish; signed tags (`git tag -s`) from this version on; README/SECURITY official-distribution statements.
  **供应链加固**：不可变发布、tag/main ruleset、CI 最小权限、Actions 固定 SHA、构建溯源证明、draft→attach→publish 发布流、本版起签名 tag、官方渠道声明。

### Fixed / 修复

- **Service readiness misjudgment** carry-over verified end-to-end (GUI green *running*, CLI monitor, auto-open) — see v2.4.2.
- Backup/restore now **skip reparse points** (symlink / junction) with an audit log line instead of following them.
  备份/恢复遇 Symlink/Junction 跳过并记审计日志，不再跟随。
- `stop` re-verifies the :3080 listener identity immediately before killing (TOCTOU hardening).
  `stop` 终止前最后一刻复检监听身份（TOCTOU 加固）。

### Tests / 测试

- Unit tests **146 → 183** (doctor summary/sanitize/size + manifest parsing); integration suite unchanged (33 cases).

---

## v2.4.2 — 2026-09-13

### Fixed / 修复

- **Service readiness was permanently misjudged as “starting”.** dsh 0.1.5+ answers unauthenticated HTTP requests with **401**, so the old “HTTP 2xx = ready” check never saw a running service: the GUI showed a yellow *starting* state forever, the CLI monitor carried a bogus warning, and the browser was never auto-opened. Readiness now also accepts “the :3080 listener is a dsh process (command line verified)” — the same evidence the `stop` guard already used — while `stop` keeps its strict check.
  **服务就绪被永久误判为“启动中”。** dsh 0.1.5+ 对未授权 HTTP 请求统一返回 **401**，旧的“HTTP 2xx 才算就绪”判定因此永远看不到已在运行的服务：GUI 一直黄灯「启动中」、CLI 监控页带无意义的警告、浏览器也不再自动打开。现在改为「端口开 + 监听进程确为 dsh（校验命令行）」兜底判定——与 `stop` 防护同源；而 `stop` 仍保留严格校验。
- **The GUI operation log was always blank.** The log text box was docked *before* the header panel, and WinForms lays out later-added controls first — the fill area took the whole page and the first lines of text were painted behind the header. Dock order fixed; log lines emitted before the log page was built are now buffered instead of dropped (e.g. “embedded core extracted”).
  **GUI 操作日志永远是空白。** 日志文本框比顶部标题栏先加入，而 WinForms 的 Dock 按“后加入的先排”计算——填充区占满整页、首行文字被标题栏盖住。已修正 Dock 顺序；日志页构建前产生的日志行改为暂存而非丢弃（例如「已自动解出内嵌核心」）。

### Added / 新增

- README screenshots (GUI light/dark, log, about + CLI live monitor) and this changelog. / README 截图（GUI 浅/深色、日志、关于 + CLI 实时监控）与本更新日志。
- Repository topics for discoverability. / 仓库 topics 便于被检索到。

> ⚠️ 非官方工具，与 DeepSeek 官方无关。Unofficial community tool, not affiliated with DeepSeek.

## v2.4.1 — 2026-09-13 —（GUI 三版本 / GUI in Three Variants）

- **Three GUI variants** from one source: CLI core (A), GUI attached (B, needs the sibling core), GUI standalone (C, embeds the core and extracts it next to itself on first launch — temp file + atomic rename, so an interrupted extraction never leaves a broken exe). / **三种形态**：命令行核心（A）、GUI 附加版（B，依赖同目录核心）、GUI 单文件集成版（C，内嵌核心并在首次启动解出；先写临时文件再原子改名）。
- **Misuse guard**: launching from Desktop/Downloads now warns — GUI confirmation dialog (Cancel quits) and CLI yellow notice; Downloads detection follows redirected profiles (registry `User Shell Folders`). / **防误用**：桌面/下载目录直跑会提醒——GUI 弹确认框（取消即退出）、CLI 黄字警告；下载目录检测支持重定向路径（读注册表）。
- GUI attached variant detects a missing core at startup (status area + one-time log notice). / GUI 附加版启动即检测核心缺失（状态区 + 日志一次性提示）。
- `verify.ps1`: `-Tag` mode uses fixed download URLs and never calls the GitHub API (immune to anonymous rate limits); UTF-8 BOM so PowerShell 5.1 parses it correctly; retry + `-UseBasicParsing`. / `verify.ps1`：`-Tag` 模式走固定链接、不调 API；改存 UTF-8 BOM 以兼容 PS 5.1 解析；加重试与 `-UseBasicParsing`。
- Logo/icon orientation restored to the orientation used since v2.0. / logo 与图标方向恢复为 v2.0 以来的正确方向。

## v2.4.0-gui-alpha — 2026-09-01 —（GUI 正式版 / GUI Edition）

- First official GUI release: three pages (home status LED + actions · log · about), dark/light theme, bilingual, restore picker with confirmation. / 首个 GUI 正式版：三页界面、深浅主题、中英双语、恢复选择框。
- **Trust mechanism**: `verify.ps1` one-click verification, CI-generated `hashes.txt` **GPG-signed** (`hashes.txt.asc`), `SECURITY.md` boundaries; “unofficial” notices in the banner and About page. / **信任机制**：一键核验 + CI 自动 GPG 签名 + 安全边界声明 + 非官方标示。
- **Safety fix**: `stop` no longer kills a foreign process holding :3080 (the listener's command line must verify as dsh). / **安全修复**：`stop` 不再误杀占用 3080 的其他程序。
- Core CLI additions: `backup-list`, `restore --path <dir>`. / 核心新增 `backup-list`、`restore --path`。
- Full CI release pipeline (build from source, generate + sign hashes, upload assets). / 发布流水线全自动化。

## v2.4.0 — 2026-08-31 —（CLI 核心 · GUI 地基 / CLI core, GUI foundation）

- Five non-interactive CLI subcommands with single-line machine-readable markers (`backup` / `restore` / `status` / `start --bg` / `stop`) — the foundation the GUI is built on. / 五个非交互 CLI 子命令（单行机器标记），GUI 的地基。

## v2.3.0 — 2026-08-31 —（安全加固 + 更新健壮性 / Security & update hardening）

- Update failure now auto-rolls back and re-verifies the version; timeouts kill the whole process tree; unified update guard; numeric pre-release ordering (`rc.1 < rc.2 < rc.10`); symmetric long-path restore; wipe marker anchored to the exe directory; manual workspace blacklist. / 更新失败自动回滚并复验版本、超时终止进程树、更新守卫统一、rc 数字序、恢复长路径对称、marker 锚定 exe 目录、手动工作区黑名单。

## v2.1.4 — 2026-08-24 —（桌面快捷方式 + 管道死锁修复 / Desktop shortcut & pipe-deadlock fix）

- Fixed the classic pipe-buffer deadlock in `RunVisible` (npm/winget output is drained on background threads); added the desktop shortcut feature (CLI `shortcut`, monitor-page `I`). / 修复 `RunVisible` 管道缓冲死锁；新增桌面快捷方式。

## v2.1.3 — 2026-08-23 —（安全加固 / Security hardening）

- The root marker is never self-created (a stray exe is permanently refused regardless of neighbouring files); wipe trusts the root marker only; strict backup-directory validation; SemVer release > rc. / 根标记永不自建（单独复制的 exe 永久拒绝清除）；清除只认根标记；备份目录严格校验；正式版 > rc。

## v2.1.2 — 2026-08-23 —（更新管理 / Update management）

- dsh update management (menu 8 / `update`): version list incl. rc, pre-update backup, double confirm, local version history. ⚠️ Superseded by v2.1.3 for security reasons. / dsh 更新管理。⚠️ 因安全问题已被 v2.1.3 取代。

## v2.1.0 — 2026-08-17 —（备份保留策略 / Backup retention）

- Backup retention (manual backups kept forever, auto/protection backups pruned), 3-state service detection (TCP + HTTP), restore/import refused while running, silent update check, log rotation, product-level single-instance lock. / 备份保留策略、服务三态检测、运行中禁止恢复/导入、静默更新检查、日志轮转、产品级单实例锁。

## v2.0.0 — 2026-08-15 —（多工作区备份 / Multi-workspace backup）

- Multi-workspace backup (`_workspace\name\`), old-format import, long-path support (`\\?\`), persistent workspace path and entry memory. / 多工作区备份、旧格式导入、长路径支持、工作区路径与入口记忆。

---

## Notes / 说明

- This project is an **unofficial** community tool and is not affiliated with DeepSeek. / 本项目为社区**非官方**工具，与 DeepSeek 官方无关。
- Every release is built from source by GitHub Actions; `hashes.txt` + `hashes.txt.asc` let you verify before running (`verify.ps1 -Tag <tag>`). / 每个发布物均由 CI 从源码构建，可用 `hashes.txt` 与 GPG 签名核验。
