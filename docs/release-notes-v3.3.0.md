<p align="center">
  <img src="https://raw.githubusercontent.com/sakanamaru/dsh-minato/main/logo.png" alt="dsh-minato" width="220">
</p>

# dsh-minato 3.3.0 — 2026-10-30（看板/悬浮窗/视觉 + 退出码契约 / dashboard, overlay, visual language & exit-code contract）

> **非官方**：本项目与 DeepSeek 官方无关，是非官方社区工具。**只读本机**：不注入 dsh、不上传任何数据。
> 本工具**不是** dsh 插件：它是独立进程，dsh 没装也能用。

**一句话**：这一版补完「看板—悬浮窗—视觉」这条使用体验，并把 CLI 退出码按政策 A 全量补齐（失败=1 / 用法=2 / 降级=0），同时修掉 Node 下载未校验、多工作区备份静默半截、GUI 首帧白屏与“超时当成功”等一批真实缺陷。

**In one line**: completes the dashboard / edge status-light overlay / visual language work, and ships a full exit-code contract (failure=1 / usage=2 / degraded=0) together with real fixes for unverified Node downloads, silently-truncated multi-workspace backups, a blank first frame, and timeouts reported as success.

```
commit=<待补>
tag=v3.3.0
```

## 用户需要采取行动吗？

**是** —— 两类用户需要看一眼：

1. **脚本/自动化用户**：退出码契约变了（这是有意的安全收紧）。`sessions` / `cost` 在空数据根或参数错时由 `0` 变 `1`/`2`；`verify-install` 无法核对时由 `0` 变 `1`；`stop`/`start`/`import`/`profilepatch`/`bootdiag`/`ui`/`bridge-install` 等失败路径现在返回非零。请同时检查标记行（`*_FAIL`），不要只看 rc。完整 26 族裁决见 `docs/退出码裁决-2026-10-30.md`。
2. **用 `ui` 命令或依赖 v2 界面的用户**：`ui` 现在只启动本包自带 `dsht-gui`；找不到就打印 `UI_FAIL` 并返回 `1`，**不再悄悄拉起 v2 旧界面**。

其余用户：GUI 与数据都不需要迁移；升级后界面与启动行为有可见变化（见下）。**不升级没有数据风险**，但拿不到本版的退出码/备份/启动修复。

## 新增 / Added

- **看板层级重整**：状态胶囊（在动/停驻/无活动/未知，标来源）/ 今日用量进度 / 最近活动流 / 24 小时分布（整会话累计、按最后活动小时归属，带脚注）· Top5 默认屏蔽会话标题 · 一屏可读。
- **悬边状态灯悬浮窗**（默认关闭，设置页开启）：四色一色一信号（灰=无信号）、三款灯样式（`line` / `dot` 默认 / `capsule`）、右键两档关闭（本次隐藏 / 永久关闭）、灯外点击穿透、不抢焦点；数据与看板同源（15 秒兜底取数）。
- **视觉规范落地**：唯一设计 token 表（5 语义色 × 明暗、4px 网格、3 档字号、圆角/边框/阴影各一档）；页题先于页签、单选项死页签不再渲染；顶栏/页脚/Toast 降噪；说明页卡片化；设置页 32 卡改分组行；KPI「未知」改中性色；窗口背景轻微渐变。
- **退出码契约（政策 A）**：26 族 / 100 个失败打印点逐族裁决——成功/计划/只读预览 `0`、执行失败/拒绝 `1`、用法错误 `2`、自完整性拒绝 `3`；明确降级/诊断行（`*_SKIPPED`、`BRIDGE_FAIL_RAW`、`*_PRE_BACKUP_FAILED`）保持 `0`。`verify_exit_codes` 断言 46 → **119**（17 条 not-covered 显式登记原因）。
- **命令注册表**：`CommandTable` + `CommandContext` 取代 39 条 `if (cmd == …)` 与静态 `_cfg`；命令面仍 38 条、标记行零改动；`verify_fixes` 命令面自检改为从注册行解析并加 39 行下限。
- **GUI 启动首帧不再等 CLI**：构造期两次同步 CLI 调用移出首帧（先画骨架，窗口出现后后台读偏好）；假 CLI 每次 sleep 10s 时窗口 1022ms 出现。
- **统一 CliRunner**：三份进程运行器合一——stderr 逐行前缀、超时按档（长命令 600s / 流式 120s）并结束进程树、超时/起不来判 `error`、缓存 key 含 CLI 路径且失败不入缓存（≤128 条）。
- **平台层去重**：6 对 Windows/Linux 同名实现上移 `Dsht.Platform.Shared`（重复行 410 → 40，−90.2%）。
- **配置写盘加固**：原子写（tmp + Replace，失败写 `launcher.log`）、Linux 0600、认不出的键打 `CONFIGWARN`。
- **新门槛**：`verify_ui_visual.ps1`（26 条源码/视觉断言）、`verify_exit_codes.ps1` 扩至 119 条；契约测试 525 → 547。

## 修复 / Fixed

- **Linux Node 下载未校验（安全）**：`install --install-node` 的 SHA-256 校验块原是不可达死代码（CS0162）→ 现在是解压前真校验，拿不到官方 `SHASUMS256.txt` 或哈希不符即拒绝（fail-closed）；顺带修掉“解压前删目录会把刚下载的包一起删掉”的相邻缺陷。CS0162 已设为构建错误。
- **`verify-install` 无法核对时仍返回 0** → 现在 `1`（安全收紧）。
- **多工作区备份失败被静默吞**：失败不再报 `BACKUP_OK`（改 `BACKUP_INCOMPLETE` + rc=1 + 半截目录清理）；恢复侧缺 `.dshws` 的包默认拒绝（`--force` 可越过）。
- **GUI 超时/CLI 崩溃被当成功**：超时/起不来现在红色失败，且超时结束整棵进程树；stderr 多行诊断不再被当标记行；流式超时保留已出结果。
- **标记行缺键被当 0**：数据不可用时显示「未知」而不是 0（本版先修 GUI 侧读取口径）。
- **`NpmRepoOf` 串行读死锁形态**：改并发读 + 有界等待 + 超时杀树。
- **`ui` 找不到 GUI 时悄悄拉起 v2 旧界面**：现在只启动本包自带 GUI；CI 的 v2 测试改为仅 v2 tag/手动 dispatch 触发。
- **Linux CI 假红**：白名单路径比较按分隔符归一（`Design\…` vs `Design/…`）。

## 变更 / Changed（用户需注意）

- **退出码行为变化**：见上「用户需要采取行动吗」。外部脚本请同时检查标记行。
- **`ui` 不再回退 v2**：Windows/macOS 上“找不到自带 GUI”现在是明确失败。
- **多工作区备份**：打包失败不再报成功；缺 `.dshws` 的包恢复需 `--force`。
- **Node 引导（Linux）**：拿不到官方校验清单时拒绝安装（可先手动装 Node）。
- **GUI 观感**：字号/间距/卡片层级统一，页题在页签上方；背景轻微渐变；设置页分组行。

## 产物 / Assets

| 文件 | 说明 |
|---|---|
| `dsh-minato-3.3.0-win-x64-setup.exe` | Windows 安装器（含卸载） |
| `dsh-minato-3.3.0-win-x64.zip` | Windows 便携包 |
| `dsh-minato-3.3.0-linux-x64.tar.gz` | Linux 包（`install.sh` + `hashes.txt`） |
| `*.sha256` | 上述文件各自的摘要 |

## 验证 / Verification

> 规矩：每条“已验证”都指向一条会失败的测试或一次真实端到端运行。

| 门槛 | 结果（全部 exit 0） |
|---|---|
| CLI 构建 | 0 errors / 11 warnings（既有 SYSLIB0014 + CS0168 基线，无 CS0162） |
| GUI 构建 | 0 errors / 0 warnings |
| 契约测试 | **547/547 passed** |
| GUI 逻辑测试 | **384/384 passed** |
| 插件自测 | **41 passed** |
| `verify_fixes.ps1` | **78/78**（命令面自检改为注册表解析，下限 39） |
| `verify_command_matrix.ps1` | **119/119 passed（XFAIL 0）** |
| `verify_exit_codes.ps1` | **119/119 passed（SKIP 17 显式 not-covered）** |
| `compare_markers.ps1` | **21/21 对齐** |
| `verify_overlay_wiring.ps1` | **53/53 passed** |
| `verify_ui_visual.ps1` | **26/26 passed** |
| `verify_switchover.ps1` | **== 全部就绪 ==**（13 项 READY） |
| v2 本地 CI 复刻 | unit **318/318**、v2 GUI **52/52**、PASS 25 FAIL 0 |
| `verify_plugin_update_baseline.ps1` | **40/40 passed** |

- 已验证：Linux 下载的 Node tarball 现在只有通过官方 SHA-256 才会被解压 —— 证据：`verify_exit_codes.ps1` 内 start/stop/import 等端到端断言 + 代码路径 `LinuxToolchainQuery.cs`（CS0162 已设为构建错误，写入不可达的安全校验会直接构建失败）。
- 已验证：GUI 首帧不再被 CLI 阻塞 —— 证据：假 CLI 每次调用 sleep 10s，窗口 **1022ms** 出现（真机实测，见开发记录）。
- 已验证：多工作区备份失败不再报成功 —— 证据：隔离目录 e2e（锁住一个工作区文件 ⇒ `BACKUP_INCOMPLETE` + rc=1；缺 `.dshws` 的包 dry-run/restore 拒绝、`--force` 放行）+ 契约测试 `[42]` 新增 9 条。
- 已实现；测试未覆盖：Linux 真机 0600 权限、B-01 真实网络下载链路（本版开发机为 Windows；已由 `verify_exit_codes` 的文件权限分支与代码审查覆盖，非真机）。

## 已知边界 / Known limits

- `verify_exit_codes` 的 17 条 not-covered（真实 dsh 环境/写盘故障/会污染本机的分支）已显式登记，未做端到端断言。
- `verify_exit_codes` / `verify_overlay_wiring` / `verify_ui_visual` 仍未直接接入 CI（exit codes 仅经本地 switchover gate9 跑到）。
- Linux 真机 0600 权限、`install --install-node` 真实网络下载链路未在真机验证。
- 4 种风格 × 5 种外壳未逐一手工截图；配置原子写无 fsync。
- 与官方 dsh 功能重合的取舍（会话统计/导出/插件市场/余额等）已出重合矩阵，下一步按“修订 1”收敛；本版不涉及删功能。

## 合并叙述（自 3.2.0 起）

从 3.2.0（成本面板）到 3.3.0 这一轮主要做了四件事：

1. **看板与悬浮窗**：把指标页重整为“状态—今日进度—最近活动—24h 分布”，并新增默认关闭的悬边状态灯悬浮窗（三灯样式、右键两档、点击穿透、不抢焦点）。
2. **视觉规范化**：建立唯一设计 token 表，四风格共用一套字号/间距/圆角/边框/阴影；界面从“每个文件各写一套”收敛到“一套规范 + 可证伪断言”；两轮 UI 打磨 + 背景轻微渐变。
3. **契约与运行时**：退出码政策 A 全量补齐（26 族/119 断言）、命令注册表取代 39 条 if 链、CliRunner 统一三份进程运行器、首帧不再等 CLI、超时杀树/失败不再假成功。
4. **平台与数据**：平台层去重 −90%、配置原子写 + Linux 0600、多工作区备份 fail-closed、Node 下载校验 fail-closed、`ui` 不再回退 v2。

与上一 minor 的差别：3.2.0 新增的是“看成本”，3.3.0 新增的是“看得舒服 + 行为可脚本化 + 失败可观察”；对脚本用户唯一必须行动的是退出码契约（见上）。

## 许可 / License

- MIT License —— 见仓库 [LICENSE](https://github.com/sakanamaru/dsh-minato/blob/main/LICENSE)。图标不适用 MIT，见 `docs/ASSETS.md`。