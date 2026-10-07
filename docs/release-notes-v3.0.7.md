# dsh-minato 3.0.7 — 2026-10-07（外部审查修复 + 审查回应 / external-review fixes and our response）

> ⚠️ 非官方工具，由社区独立开发，与 DeepSeek 官方无关。本工具**不是 dsh 插件**：它是独立进程，不注入 dsh，dsh 没装也能用。

**一句话**：一个外部审查者逐行审了备份/恢复链，我们复现了他的每一条、修掉六条、并第一次把「审查回应」写进发版说明；
随后又用 **Linux 真机端到端运行**结案了他的 N2 悬案（**改正的是注释与文档，代码行为一行未改** ✓）、并修掉一个安装器退出码假红。

**In one line**: an external reviewer audited the backup/restore chain line by line; we reproduced
every finding, fixed six of them, and for the first time ship a point-by-point review response.
A real Linux end-to-end run then closed his N2 open question (comments and docs were corrected;
**not a single line of behaviour changed**) and fixed a false-red installer exit code.

---

## 审查回应 / Review response

> 口径：**复现过的写复现、复现不出的直说、没测的标未测** ✓ 每一条都指向测试或端到端命令 ✓
> 完整往来留档：`docs/审查报告-2026-10-07-审核意见.md` · `docs/对审查报告的回应-2026-10-07.md` · `docs/结案函-2026-10-07.md`

| 编号 | 外部发现 | 我们的复现 | 修法 | 现在靠什么守住 |
|---|---|---|---|---|
| **D1** | `backup-export` 对正常包**恒失败**，并留下无完成标记的副本 | **运行时复现**：`BKEXPORT_FAIL` + 导出目录 401 文件 / manifest 0 ✗ | `CopyTree` 返回值语义修正（返回真实复制数）✓ | `verify_restore_apply.ps1` 端到端 + 命令矩阵里的 `BKEXPORT_OK` 成功路径 ✓ |
| **D2** | 失败路径退出码仍是 0（`return 0` 174 处 / 非零 3 处） | **计数复现**（与审查数字逐字一致 ✓） | 失败路径改 `return 1`（成功仍 0；用法 2；启动自完整性 3）✓ | 命令矩阵断言**退出码** ✓ |
| **D3** | 完成性闸门三处 fail-open（`want<0` 放行、哈希核对在其后） | 复现 ✓ | 全程 fail-closed + 哈希核对前置，与 `backup-list --verify` 结论对齐 ✓ | 契约测试：截断包必须被拒收 ✓ |
| **D4** | 恢复的顶层文件缺目标端 reparse 守卫 | 复现 ✓ | 顶层文件加守卫（三处）+ 跳过规则统一 `SkipRules.SkipDir` ✓ | 契约测试：`mklink /J` 端到端，回滚即红 ✓ |
| **D5** | `IsSubPath` 对文件系统根恒 false | 复现 ✓ | 根路径直接前缀比对 ✓ | 契约测试：根 / 盘根 / `..` 断言 ✓ |
| **D7** | 自哈希缓存键缺路径分量 | 复现 ✓ | 文件名改 `SHA256(路径|长度|时间戳).txt`（两端同构）✓ | 契约测试：投毒缓存后仍读出真值 ✓ |
| **C1/C2/C3** | **"文档超出证据"**：把回滚 / symlink / `..` 写成"已验证" | 抽核证实（`..` 测试 0 处、`ROLLED_BACK` 仅产品源码 1 处 ✓） | 在 `docs/release-notes-v3.0.0.md` 逐条加「2026-10-07 复核修订」注记、收回"真机双向验证"措辞；README 双语 Known limits +3 ✓ | 发版清单新增硬规矩：**每个"已验证"必须指向一条会失败的测试或一次真实端到端运行** ✓ |
| **N2** | **"不带 `--apply` 的真实恢复永远不会写进用户默认数据根"**：`Program.cs` 的这条注释与同文件 `dstRoot = paths.DataRoot` 自相矛盾 | **真机端到端复现**（v3.0.7 官方包 · Ubuntu 26.04 VM）：`unset DSH_HOME` + `restore --path <包>`（**不带 `--apply`**）→ `RESTORE_OK`、exit 0，并从 `~/.dsh` 抓了 `RESTORE_PRE_BACKUP`；事后 `~/.dsh/.anonymous-user-id` 与 `.credentials.yaml` 的 **mtime 被刷新**（sha256 未变 —— 写入的是相同字节，所以摘要证明不了"没写" ✗） | **外部审查者说对了 ✓ 我们用真机端到端运行结案（mtime 证据 ✓）**；不带 `--apply` 恢复到生效数据根是**本工具的 v2 兼容核心用途** → **不改行为** ✗ 改正的是**注释与文档的说法**：`Program.cs` · `RestoreApplyPolicy.cs` · `v3/README.md` · `PRIVACY.md` · `VERIFY.md` · `LINUX-TEST.md` ✓ | `--apply` 的真实语义现在写明为"**跳过运行中闸门 + 要求隔离数据根**"（**不是**"允许写盘"）✓；`verify_restore_apply.ps1` 端到端 + 契约测试继续守住 `--apply` 准入（未设 `$DSH_HOME` → 拒绝、默认位置 → 拒绝）✓ 证据：`docs/Linux真机验证-2026-10-07-官方包.md` §3 ✓ |
| **N3** | 本次 **Linux 真机自测**发现的**安装器假红**（非外部审查）：`install.sh` 正常安装后**退出码 3** | **真机复现**：干净安装 → `! **清单只覆盖 103 / 106 个文件** ✗ 覆盖不全 → **不算通过**` + `exit 3`；根因 = 自检的**分母**把安装器**自建**的 `.dsh-minato-files` / `.dsh-minato-install` 与**清单自身** `hashes.txt`（无法含自身摘要）也算成"应被清单覆盖"；同一目录里 `sha256sum -c hashes.txt` 是 **103/103 全 OK** | 自检分母改为"**我们的载荷**"（安装清单 `.dsh-minato-files` 里记录、清单应当覆盖的文件）✓ 覆盖率判据**保留**（清单项 + 缺失 ≥ 载荷数）✗ 未削弱 | **真机**：修复前 `exit 3` → 修复后 `exit 0`（`✓ 指纹全部一致（103 项，覆盖 103/103 项载荷）`）✓；并**故意篡改一个载荷文件 → `指纹不符` + `exit 3`** ✓（断言仍会红 ✓）证据：真机报告 §4 + 本版修复记录 ✓ |

**我们上报的三条"未测"**（不装 ✓）：包内单文件截断的自证能力（D6）· `File.Copy` 对目标端符号链接的运行时语义（D4 的行为面）· `cp -R` 源内符号链接与 `readlink` 引号（需 Linux 真机）—— Linux 真机验证本版**已推进到 v3.0.7 官方包**（见下节 ✓），但这三条**仍未在真机上构造用例**，仍是"已实现；测试未覆盖" ✓

## 新增 / Added

- **`overview` 命令**（只读）：一个进程给出概览页所需全部标记行 → GUI 每拍从 4 个进程降到 1 个 ✓
- **两个界面偏好键** `gui_shell` / `gui_style`（布局与风格可持久化 ✓ 静默落盘、走动作闸门 ✓）
- **`close_action` / `host` 两个键真正生效**（`host` 带白名单，仅 `127.0.0.1` / `localhost`，默认行为不变 ✓）
- **`v3/tests/verify_command_matrix.ps1`**：命令 × 隔离数据根**冒烟矩阵**（14 条命令/行为，断言标记行 + **退出码** + 越界自证 ✓）并接进 CI（新 job `v3-smoke-matrix`，windows-latest ✓）
- **`docs/VERIFY.md`**：用户如何**独立核对每条承诺**（跑什么命令 / 看哪行标记 / 什么算通过 ✓）
- token 口径拆开：`SESSIONS_TOTAL` 增加 `uncached=`，GUI 显示为「新输入 · 缓存命中 · 生成」✓

## 修复 / Fixed

- 上表六条备份链缺陷（D1/D2/D3/D4/D5/D7）✓
- 页眉自动刷新条遮挡标题 · 会话页排序下拉统一 `SlimCombo` + 指标块最小宽度（回归）
- 契约测试 **355 → 385** 项（本版新增：截断包拒收 / junction 穿透 / 根路径 / 投毒缓存 / 导出失败不留半成品等断言 ✓）

## 变更 / Changed

- ⚠️ **行为变化**：`host` / `close_action` 现在真的生效；**失败路径退出码变为非零**（此前所有拒绝都返回 0 ✗ → 脚本现在可以 `|| echo failed` 了 ✓）
- **超时放宽**：写操作 10 分钟封顶（不再杀半截 npm / 备份 ✓）· Windows 进程探测去 shell-out（status 1275→894ms ✓）
- **GUI 信息架构重整**：概览/看板去重、`Shells.cs` 拆 partial、新增 `ConfirmDialog` ✓
- `linux_smoke.sh`：**26 行 0 断言 → 20 项断言** ✓（此前它挂在 CI 里只能因崩溃变红 ✗）· `verify_backup_chain.sh` 加 `EXPECTED_MIN` ✓

## 产物 / Assets

| 文件 | 说明 |
|---|---|
| `dsh-minato-3.0.7-win-x64-setup.exe` (+`.sha256`) | Windows 安装器 |
| `dsh-minato-win-x64.zip` (+`.sha256`) | Windows 免安装包 |
| `dsh-minato-linux-x64.tar.gz` (+`.sha256`) | Linux x64 包 |
| `hashes.txt` / `hashes.txt.asc` | 全部产物 SHA-256 及 GPG 签名 |

## 验证 / Verification

- 本地：CLI 构建 0 错误 · **GUI 构建 0 错误** · 契约 **385/385** · GUI 逻辑 **69/69** · 插件 **25** ·
  `verify_fixes` **72/72**（命令面一一对应 ✓）· `verify_restore_apply` **41/41** · 命令矩阵 **44/44** ·
  v2 复刻全部通过 · **13 项就绪度门槛 `== 全部就绪 ==`**
  （以上为 **2026-10-07 本批改动后复跑** 的实测数字 ✓）
- CI 两平台 + 新 job `v3-smoke-matrix` ✓（Windows 包内含 GUI 窗口冒烟 ✓）
- **数量不等于保证**：上述测试数**不能**用来证明安全承诺；每条承诺的证据见 `docs/VERIFY.md` ✓

## Linux 真机验证（2026-10-07 · **首次验官方包** v3.0.7）/ Linux real-machine verification

> 完整证据（可复现命令 + 真实输出 + 行号）：`docs/Linux真机验证-2026-10-07-官方包.md`（项目根留档，未进仓库 ✓）
> 环境：Ubuntu 26.04.1 LTS 真机 VM（2 核 / 3.3 GB / 根分区余 4.0 GB）· 对象：draft release `v3.0.7` 的 `dsh-minato-linux-x64.tar.gz`
> 此前多版发版说明都写着"Linux 真机验证仍停在 3.0.0" ✗ —— **本版推进到 v3.0.7 官方包** ✓ 但**不是"全部已验证"** ✗（未测项见下 ✓）

| 项目 | 结论 | 靠什么（真实端到端运行 / 会失败的断言） |
|---|---|---|
| 产物摘要 | ✅ 宿主 `Get-FileHash` = 资产 `.sha256` = 客人 `sha256sum -c`，三处 `7a0bc34e…bfdb`，字节数 48383868 一致 | 三处独立计算 ✓ |
| 包形态 · 清单 | ✅ `hashes.txt` 在（9650 B / 103 条）；**`sha256sum -c hashes.txt` = 103/103 OK**；无任何清单项缺失（唯一未覆盖的是清单自身 ✓ 正常设计） | 解包后 `sha256sum -c hashes.txt`（exit 0）✓ |
| 包形态 · 执行位 | ✅ 包内 `*.sh` **2/2** `-rwxr-xr-x`（不可执行计数 0）；`dsh-minato` / `cli-small/dsh-minato` / `gui/dsht-gui`（ELF apphost）均可执行 | 逐项 `ls -l` + 逐脚本执行位测试 ✓ |
| 自检闸门**真的生效** | ✅ 从解包目录跑 `./dsh-minato version` → 输出只有 `DSHT_VERSION 3.0.7`，**没有 `INTEGRITY_SKIPPED`**；**对照实验**：移走 `hashes.txt` 后该行立刻出现，放回即消失 | §2 对照实验 + `doctor` 的 `[OK] Integrity 自身 exe 与随包 hashes.txt 一致` ✓ |
| 真实数据根 + `restore`（**N2 结案**） | ❌→✅ **不带 `--apply` 会写默认数据根**（mtime 物证）；结论：行为是 **v2 兼容设计**，改正的是注释与文档 ✓ | §3：基线 tar + 逐文件 sha256 + mtime 三重对照；`--apply` 对照实验（未设 `$DSH_HOME` → 拒绝 exit 1）✓ |
| 安装器 | ✅ 正常安装可用（`~/.local/share/dsh-minato` · 106 文件 · 命令链接 + 菜单项正确 · CLI 报 `3.0.7`）；**退出码假红已修**（本版 N3）：修复前 `exit 3` → 修复后 `exit 0` | 真机跑 `install.sh`（修复前 / 修复后）+ **篡改载荷对照**（→ `指纹不符` + `exit 3`）✓ |
| 卸载 | ✅ 干净：自建 106 项 + 命令链接 + 菜单项全删，**`~/.dsh` 仍 26 文件**（不动数据 ✓） | 真机 `install.sh --uninstall` + 前后文件数 ✓ |
| **GUI 首次在 Linux 起来** | ✅ 窗口映射（`dsh-minato — V3` 1213×768）· 截图 `shots/gui-work/linux-vm-3.0.7.png`（2289 色）· 界面显示**真机真实数据**（会话总数 4） | `xwininfo` + `xwd` 抓窗口（Wayland 下根窗口截图全黑，是失败对照 ✓）✓ |
| 包内 `linux_smoke.sh` | ✅ `PASS=22 FAIL=0`，exit 0 | 真机 `timeout 600 ./linux_smoke.sh` ✓ |

**仍未测（照报告 §9 如实列，别当"已验证" ✗）**：Windows 包真机 · `install.sh` 的 `--prefix` / `--no-desktop` / `DSH_MINATO_BINDIR` 等其余开关 ·
卸载后再装与幂等反复跑（本次只跑到"同一 prefix 重装"）· GUI **交互**功能（点各导航页 / 按钮行为）· 断网 / 代理 / 只读文件系统 / 磁盘满 ·
`linux_smoke.sh` 在"服务未运行"的干净机器上的分支 · 我们上报的三条"未测"（截断自证 D6 · `File.Copy` 对目标端链接 · `cp -R` / `readlink`）的真机用例 ✓

> **建议接进 CI**：仓库现在**没有**任何 CI 步骤**执行** `v3/tools/install.sh` ✗（它只被 `cp` 进包 + `chmod +x` ✓，
> `verify-linux.sh` 也不跑它 ✓）—— 本次的退出码假红（N3）正是"没人拿安装器退出码当判据"漏掉的 ✓
> 建议在 `v3-linux` 的 Linux job 加一条：装一次（断言 **exit 0** 且清单 103/103）→ 篡改一个载荷文件（断言 **exit 3**）✓

## 已知边界 / Known limits

- **布局观感类改动测试覆盖不到** ✗ 需人工过 10 页 × 4 风格
- 退出码契约：`1`=失败 · `2`=用法/未知命令 · `3`=启动自完整性 ✓（成功 0 ✓）
  —— 安装器 `install.sh` 另有自己的契约：`0`=装成功 · `1`=拒绝安装 · `3`=指纹/覆盖率不符 ✓（本版修掉"正常安装也给 3"的假红 ✓ 见 N3 ✓）
- `MainWindow.axaml.cs` 的历史折行重排已隔离成独立提交，忽略空白后仍有残差；该文件行尾为 LF（仓库多数 `.cs` 为 CRLF）——blob 经 autocrlf 归一，**不影响产物** ✓
- 工作树 32 个文件混行尾（卫生项）· 配置落盘无 DPAPI/0600 加固 · GUI 仍用字符串拼命令行（`ArgumentList` 迁移未做）
- **Linux 真机验证已从 3.0.0 推进到本版官方包 v3.0.7** ✓（Ubuntu 26.04 真机 VM，见上节）——但**不是"全部已验证"** ✗：
  未覆盖 Windows 包真机 · `install.sh` 其余开关 · 卸载后再装 / 幂等反复跑 · GUI 交互点击 · 断网 / 只读 fs / 磁盘满等异常路径 ·
  `linux_smoke.sh` 的"服务未运行"分支 · 以及我们上报的三条"未测"（截断自证 D6 / `File.Copy` 对目标端链接 / `cp -R` 与 `readlink`）✓
- **`restore` 不带 `--apply` 会写入生效数据根**（未设 `$DSH_HOME` 时即默认 `~/.dsh`）—— 这是 **v2 兼容的有意行为** ✓
  不是缺陷；唯一额外守卫是"服务不在运行"。需要"只写隔离根"时用 `--apply`（未设 `$DSH_HOME` 或数据根等于默认位置 → `RESTORE_FAIL`）✓
- `backup-export` 会自动创建不存在的 `--to` 目录 ✓；`--to` 在执行前即持久化 ✓（外部审查后新记录的行为）

## 许可 / License

[MIT](https://github.com/sakanamaru/dsh-minato/blob/main/LICENSE)（代码）。**图标不是 MIT** —— 见 `docs/ASSETS.md`。
GitHub：[@sakanamaru](https://github.com/sakanamaru)