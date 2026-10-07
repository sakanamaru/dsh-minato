# dsh-minato 3.0.7 — 2026-10-07（外部审查修复 + 审查回应 / external-review fixes and our response）

> ⚠️ 非官方工具，由社区独立开发，与 DeepSeek 官方无关。本工具**不是 dsh 插件**：它是独立进程，不注入 dsh，dsh 没装也能用。

**一句话**：一个外部审查者逐行审了备份/恢复链，我们复现了他的每一条、修掉六条、并第一次把「审查回应」写进发版说明。

**In one line**: an external reviewer audited the backup/restore chain line by line; we reproduced
every finding, fixed six of them, and for the first time ship a point-by-point review response.

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

**我们上报的三条"未测"**（不装 ✓）：包内单文件截断的自证能力（D6）· `File.Copy` 对目标端符号链接的运行时语义（D4 的行为面）· `cp -R` 源内符号链接与 `readlink` 引号（需 Linux 真机；Linux 真机验证仍停在 3.0.0 ✓）

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
- 契约测试 **355 → 376** 项（新增截断包拒收 / junction 穿透 / 根路径 / 投毒缓存等断言 ✓）

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

- 本地：CLI 构建 0 错误 · **GUI 构建 0 错误** · 契约 **376/376** · GUI 逻辑 **69/69** · 插件 **25** ·
  `verify_fixes` **72/72**（命令面一一对应 ✓）· `verify_restore_apply` **41/41** · 命令矩阵 **44/44** ·
  v2 复刻全部通过 · **13 项就绪度门槛 `== 全部就绪 ==`**
- CI 两平台 + 新 job `v3-smoke-matrix` ✓（Windows 包内含 GUI 窗口冒烟 ✓）
- **数量不等于保证**：上述测试数**不能**用来证明安全承诺；每条承诺的证据见 `docs/VERIFY.md` ✓

## 已知边界 / Known limits

- **布局观感类改动测试覆盖不到** ✗ 需人工过 10 页 × 4 风格
- 退出码契约：`1`=失败 · `2`=用法/未知命令 · `3`=启动自完整性 ✓（成功 0 ✓）
- `MainWindow.axaml.cs` 的历史折行重排已隔离成独立提交，忽略空白后仍有残差；该文件行尾为 LF（仓库多数 `.cs` 为 CRLF）——blob 经 autocrlf 归一，**不影响产物** ✓
- 工作树 32 个文件混行尾（卫生项）· 配置落盘无 DPAPI/0600 加固 · GUI 仍用字符串拼命令行（`ArgumentList` 迁移未做）
- **Linux 真机验证仍停在 3.0.0**；本版只有 CI 打包与冒烟
- `backup-export` 会自动创建不存在的 `--to` 目录 ✓；`--to` 在执行前即持久化 ✓（外部审查后新记录的行为）

## 许可 / License

[MIT](https://github.com/sakanamaru/dsh-minato/blob/main/LICENSE)（代码）。**图标不是 MIT** —— 见 `docs/ASSETS.md`。
GitHub：[@sakanamaru](https://github.com/sakanamaru)