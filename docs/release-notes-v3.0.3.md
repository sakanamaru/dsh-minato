<p align="center">
  <img src="https://raw.githubusercontent.com/sakanamaru/dsh-minato/main/logo.png" alt="dsh-minato" width="180">
</p>

# dsh-minato 3.0.3 — 2026-10-04（连点修复、余额入口与只刷字段 / click-guard, balance entry & field-level refresh）

> ⚠️ 非官方工具，由社区独立开发，与 DeepSeek 官方无关。本工具**不是 dsh 插件**：它是独立进程，不注入 dsh，dsh 没装也能用。

**一句话**：修掉两个实际使用中踩到的问题（「更新能连点好几次」「余额配置入口找不到」），并把概览/看板的自动刷新从**整页重建**改成**只刷会变的那几个字段**。

**In one line**: two real usability bugs are fixed — repeated clicks launched concurrent updates, and the balance
key had no discoverable entry point.

---

## 新增 / Added

- **余额的入口**（未绑定 key 时）：设置页新增**独立分组**「DeepSeek 余额检测」，概览页也给出**一行入口**并带
  一键跳转 —— 此前未绑定就**连"去哪填 key"都无处可寻** ✗。余额数字在未绑定时**仍然一个都不显示** ✓（原设计不变）。
  **A discoverable entry for the balance key** (own Settings group + one-line pointer on the overview page).
  Balance figures stay hidden while unbound, as before.

- **概览 / 看板改成「只刷字段」**（用户要求："自动刷新回整页刷新，可以只字段刷新吗"）：原来每一拍都**重建整棵视觉树** ✗
  —— 滚动位置回到顶部、焦点与展开的下拉全丢、整页还淡入 160 毫秒 ✗✗（观感就是"整页闪一下"）。
  现在页面把**会变的字段**注册进来 ✓ 自动刷新那一拍**只重算并改文字** ✗ 不重建 ✓✓；
  取值走**每次重算的取值函数** ✓（不是建树那一刻的快照 ✗ —— 传快照就永远显示旧值 ✓）；未注册任何字段的页面**退化为整页重建** ✓
  （免得"刷新了却什么都没动"更难查 ✓）。**体检卡保持普通卡** ✓（它只在点按钮后变，而那次本来就整页重建 ✓ 顺带保住红/黄配色信号 ✓）。
  **Field-level refresh**: auto-refresh now updates only the registered fields instead of rebuilding the whole tree.
## 修复 / Fixed

- **更新可以连续点好几次** ✗✗：每次点击都会**新起一个 CLI 进程**，点三次就是**三个 `update --yes` 同时跑**
  （npm 安装互相踩 + 连做三次备份 + 回滚点互相覆盖）。
  现在**同时只允许一个 CLI 动作**：正在跑时的点击**如实拒绝**并说明在跑什么、已经跑了多久 ✗ **不排队** ✗
  （排队会把看不见的活堆在长任务后面）。按钮同时**禁用并显示「进行中：X …」**，更新页也写明为什么灰；
  动作结束时在 `finally` 里**必定放开**闸门 ✓（否则按钮会永久卡死，比重复点击更难查）。
  **Repeated clicks no longer launch concurrent updates** — one CLI action at a time, refused with an explanation.
- **「立即备份」走的是自己的 `async void` 路径，绕开了闸门** ✗ → 一并纳入 ✓
  （闸门在它的两个阶段之间放开，否则"第一次备份"的第二步会被自己的闸门拒掉 ✗✗）。
  **The backup button had the same hole** through its own async path; now gated too.
- **概览页余额未绑定时连入口都没有** ✗ → 补一行入口 ✓。
- **三语 README 的截图"消失"** ✗：3.0.1 里把画廊写进了折叠块，在 GitHub 上默认收起 = 看不见 →
  恢复为**页面顶部常显**（6 张主图 + 其余一次点击）。
  **The README screenshots are visible again at the top** (3.0.1 had them collapsed, which reads as "gone").

## 变更 / Changed

- `v3.0.0` 与 `v3.0.1` 的发版说明**重排为同一骨架**（一句话 → 新增 / 修复 / 变更 → 产物 → 验证 → 已知边界 → 许可），
  `docs/CHANGELOG.md` 也合并为同一结构。
  **Release notes now share one skeleton** across versions.
- 开发态版本标记：本次修复在发布前把 main 标为 `3.0.2-dev` ✗ 不冒充已发布的 `3.0.1` ✓。

---

## 产物 / Assets

| 文件 | 说明 |
|---|---|
| `dsh-minato-3.0.3-win-x64-setup.exe` (+`.sha256`) | Windows 安装器（双击即装） |
| `dsh-minato-win-x64.zip` (+`.sha256`) | Windows 免安装包 |
| `dsh-minato-linux-x64.tar.gz` (+`.sha256`) | Linux x64 包（`./install.sh`） |
| `hashes.txt` / `hashes.txt.asc` | 全部产物的 SHA-256，及其 GPG 签名 |

## 验证 / Verification

- **本地就绪度门槛 13 项全绿**；契约测试 **355** · GUI 逻辑 **68** · 桥接插件自测 **25** · v2 单元测试 **318**
- **CI 两平台全绿**（windows-latest / ubuntu-latest）；Windows 包内含 **GUI 窗口冒烟**（真的启动并创建窗口）
- **人工复现与复测**：问题在第二台机器上被用户实测发现（连点更新、找不到余额入口），修复后同一路径复测通过
- Linux 真机（虚拟机）安装验证仍停留在 **3.0.0** —— 本版只有 CI 的打包与冒烟，**没有重跑真机**，如实写明

## 已知边界 / Known limits

- **闸门是"拒绝"不是"排队"**：长任务进行中，其它动作会被明确拒绝 ✗ 不会替你排队 ✗（这是刻意的：排队会隐藏
  "其实还没开始"的状态）。
- Windows 产物**没有数字签名** —— 请用公布的 `.sha256` 与 GPG 签名核对。
- 桥接插件需要 pnpm；`desktop` profile 由官方桌面端管理。
- 图形界面基于 Avalonia，因此不是零依赖 —— 项目其余部分是。

## 许可 / License

[MIT](https://github.com/sakanamaru/dsh-minato/blob/main/LICENSE)（代码）。**图标不是 MIT** —— 见 `docs/ASSETS.md`。
GitHub：[@sakanamaru](https://github.com/sakanamaru)
