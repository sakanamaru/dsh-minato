<p align="center">
  <img src="https://raw.githubusercontent.com/sakanamaru/dsh-minato/main/logo.png" alt="dsh-minato" width="180">
</p>

# dsh-minato 3.0.6 — 2026-10-06（审查修复 8 条 + 性能与界面重整 / review fixes, performance and a GUI reshuffle）

> ⚠️ 非官方工具，由社区独立开发，与 DeepSeek 官方无关。本工具**不是 dsh 插件**：它是独立进程，不注入 dsh，dsh 没装也能用。

**一句话**：一轮独立审查的 8 条必修 + 一批真实的性能收益（一次进程替代四次、Windows 进程探测去掉 shell-out、超时不再杀半截任务）叠加一次 GUI 信息架构重整（概览/看板去重）。

**In one line**: eight audited fixes, real performance wins (one process instead of four, no more
shell-outs for process probing, no more half-killed long tasks), and a deduplicated GUI.

---

## 新增 / Added

- **`overview` 命令**（只读）：一个进程给出概览页所需的**全部**标记行
  （`status --detail` + `sessions` + `backup-list` + `profiles`）→ GUI 每拍从 **4 个进程降到 1 个**
  ✓（0.5 秒实时档的成本大头正是进程启动）。各子块输出逐字不变 ✓，任一子块失败不打断其余块 ✓
  **A new read-only `overview` command** aggregating four marker families in one process.
- **两个界面偏好键**：`gui_shell`（0..4 侧栏布局）/ `gui_style`（0..3 视觉风格）——
  顶栏切换即时**静默落盘**（走动作闸门、忙时排队合并 ✓ 不弹 toast）、启动回读 ✓
- **`close_action` / `host` 两个键真正生效**（此前只回显 ✗）：`host` 带白名单，仅接受 `127.0.0.1` / `localhost`，
  默认值下行为完全不变 ✓

## 修复 / Fixed

- **审查必修 8 条**（`docs/审查发现-2026-10-06.md` + 其审核/复核意见）：
  - **C1 备份日志与事实相反**：完整备份**不写日志**、不完整反而写 `backup OK` ✗ →
    现在无条件写，不完整用 `WARN` + `backup INCOMPLETE`（已双向实测 ✓）
  - **C3** 不可达 `return`（CS0162 归零）· **C8** 插件注释与代码不符 · **H1** 恒空控件 ·
    **H2** 死 `[stderr]` 分支 · **U3** 概览重复渲染"原始输出"
  - **U1** 设置页四个在 V3 无效的项（`close_action`/`host`/`check_update`/`lang`）——其中两个已在本次**真正接线** ✓
  - **C6** `docs/PRIVACY.md` 写入声明不实（现在如实列出"主动确认后才修改 profile 插件配置"）+ 网络表补全为权威清单；
    `.github/SECURITY.md` 范围句更新为跨平台 CLI + GUI + 插件
- 页眉自动刷新条遮挡标题（`PageHeader` 缺 `Grid.SetColumn`）· 会话页排序下拉统一 `SlimCombo` + 指标块最小宽度（回归修复）

## 变更 / Changed

- ⚠️ **行为变化**：`host` 与 `close_action` 现在真的生效（此前选了没反应 ✗）
- **超时放宽**：写操作（update / install / uninstall / backup / backup-export / backup-delete / restore /
  import / bridge-install）**10 分钟封顶**，只读命令仍 30 秒 —— 不再中途杀掉半截 npm 安装或备份包 ✓
- **Windows 进程探测去掉 shell-out**：`tasklist` ×2 + CIM `CommandLine` 换成直连查询
  （`status` 实测 **1275ms → 894ms**；去掉的约 950ms 为真实收益，余下为沙箱环境 artifact；
  `stop` 的身份判定改为保守方向：HTTP 探测失败即判非 dsh → **拒绝停止**，不会误杀 ✓）
- **GUI 信息架构重整**：概览 ⇄ 看板**去重**（重复卡片、5 张跳转卡删除，图表只在"图表"子标签出现一次）、
  `Shells.cs` 拆为 4 个 partial、新增 `ConfirmDialog`；下拉与按钮同高、看板并页、概览填充等观感收口

## 产物 / Assets

| 文件 | 说明 |
|---|---|
| `dsh-minato-3.0.6-win-x64-setup.exe` (+`.sha256`) | Windows 安装器（双击即装） |
| `dsh-minato-win-x64.zip` (+`.sha256`) | Windows 免安装包 |
| `dsh-minato-linux-x64.tar.gz` (+`.sha256`) | Linux x64 包（`./install.sh`） |
| `hashes.txt` / `hashes.txt.asc` | 全部产物的 SHA-256，及其 GPG 签名 |

## 验证 / Verification

- **本地就绪度门槛 13 项全绿** · 契约测试 **355** · GUI 逻辑 **69**（+1 overview 聚合回归）·
  插件自测 **25** · v2 单元测试 **318**
- **`verify_fixes.ps1` 72/72 + 命令面 37/37 一一对应** ✓（含 CI 才跑的那部分，本次由维护者独立复跑）
- **CI 两平台全绿**（windows-latest / ubuntu-latest）；Windows 包内含 **GUI 窗口冒烟**
- 送审与复核链条留档：`docs/审查发现-2026-10-06.md` → `-审核意见.md` → `修复记录-…` → `-复核意见.md`；
  优化与 GUI 重整的复核见 `docs/复核意见-2026-10-06-GUI重构批次.md`

## 已知边界 / Known limits

- **布局观感类改动测试覆盖不到** ✗：构建/测试/门槛只能证明"没坏"，去重后的界面**需人工过 10 页 × 4 风格** ✓
- `MainWindow.axaml.cs` 的折行重排已隔离成独立提交，但忽略空白后仍有 24+/80−；该文件行尾为 LF（仓库多数 `.cs` 为 CRLF）
  —— blob 经 autocrlf 归一，**不影响产物** ✓
- 工作树 32 个文件混行尾（卫生项，不影响发布）· 插件两个时间戳/形状存疑项仍按"不猜"处理 ✓
- GUI 仍用字符串拼命令行（`ArgumentList` 迁移未做）· 配置落盘无 DPAPI/0600 加固（后续版本）
- Linux 真机（虚拟机）验证仍停留在 **3.0.0**；本版只有 CI 的打包与冒烟，**没有重跑真机**，如实写明

## 许可 / License

[MIT](https://github.com/sakanamaru/dsh-minato/blob/main/LICENSE)（代码）。**图标不是 MIT** —— 见 `docs/ASSETS.md`。
GitHub：[@sakanamaru](https://github.com/sakanamaru)
