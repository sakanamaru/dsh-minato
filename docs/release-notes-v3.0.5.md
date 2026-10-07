<p align="center">
  <img src="https://raw.githubusercontent.com/sakanamaru/dsh-minato/main/logo.png" alt="dsh-minato" width="180">
</p>

# dsh-minato 3.0.5 — 2026-10-05（自动刷新取新数据 / auto-refresh reads fresh data）

> ⚠️ 非官方工具，由社区独立开发，与 DeepSeek 官方无关。本工具**不是 dsh 插件**：它是独立进程，不注入 dsh，dsh 没装也能用。

**一句话**：修掉 3.0.4 后用户实测报的自动刷新问题——**4 秒 CLI 缓存把刷新数据也缓存了**，导致 ≤4 秒的档位"看起来停了"；顺带把余额回调的 60 秒整页闪也消掉。

**In one line**: auto-refresh intervals of 4 s or less were silently serving cached
data, so the fields never changed; fixed, and the 60-second balance callback no
longer rebuilds the page.

**需要你做什么 / Action required**：**是，请升级** —— 自动刷新在 ≤4 秒档位一直读到缓存里的旧数据（每一拍都命中 4 秒 TTL 缓存，刷新等于失效）；同时余额回调每 60 秒整页闪一下。

**本版改了什么（概述）**

- 修复**自动刷新「停了」**：概览/看板的四个数据源全走 4 秒 TTL 缓存，选 3 秒档（以及 0.5 / 1 秒）**每一拍都命中缓存**、拿回上一拍旧数据，字段写回去还是同一个值。现在**自动刷新这一拍先清缓存再跑**；普通刷新与切页继续吃缓存。
- 修复**余额回调不再整页重建**：原来开着自动刷新时每 60 秒整页闪一下 + 滚动回顶 + 160ms 淡入；现在只改数字，未注册字段的页面退化为整页重建（数据不丢）。
- 顺带一个**诚实更正**：选择器旁「每一拍都要起一个 CLI 进程、有真实成本」在修复前对 3 秒档是假的（实际吃缓存），现在才真正成立。
- 变更：选择器旁那句「每一拍都要起一个 CLI 进程、有真实成本」改为**如实成立**的表述（此前对 3 秒档是假的）。

`commit=2d175f26967c64c2086fbf7e317898d15aa47ec9` · `tag=v3.0.5`

<details>
<summary><b>完整发版说明（点开展开）</b></summary>

---

## 修复 / Fixed

- **自动刷新"停了"（用户实测）**：概览/看板的四个数据源（`status --detail` / `profiles` / `sessions` /
  `backup-list`）全部走 `RunCached` —— 那是给"切页瞬间别重复起进程"加的 **4 秒 TTL** 缓存 ✓。
  选 3 秒档 → **每一拍都命中缓存**（3 < 4）→ 拿回的还是上一拍的旧数据 → 字段写回去还是同一个值 →
  看起来"停了" ✗✗（0.5 / 1 / 3 秒全中招；5 秒档没事——所以症状是"好像还有一点问题"）。
  **现在自动刷新这一拍先清缓存再跑** ✓ 每拍都是新数据 ✓；普通刷新/切页继续吃缓存 ✓。
  顺带一个诚实更正：选择器旁"每一拍都要起一个 CLI 进程 ✓ 有真实成本"——**修复前对 3 秒档是假的**（实际吃
  缓存），现在才真正成立。
  **Auto-refresh ticks now invalidate the cache before fetching**; ordinary
  refreshes and page switches keep the cache.
- **余额回调不再整页重建**：原来开着自动刷新时**每 60 秒整页闪一下** + 滚动回顶 + 160ms 淡入——恰好是
  字段级刷新要保护的东西（审查点过名）。余额卡本来就是活字段，到账现在**只改数字** ✓；
  没注册字段的页面退化为整页重建（数据不丢 ✓）。
  **The balance callback updates fields in place** instead of rebuilding the page.

## 已知边界 / Known limits（与 3.0.4 相同，仍未修，如实列）

- 概览页的状态大徽章（hero）不走字段级刷新——它是建树时的快照，数字卡是活的，两者可能短暂不一致。
- GUI"字符串拼命令行"的习惯（`ArgumentList` 迁移）未做；`Run` 的 30 秒硬超时会杀长任务
  （update / install / 大备份可能中途被杀）。
- 配置落盘无 DPAPI（Windows）/ 0600（Linux）加固、非原子写入。
- 自动刷新仍**只在概览/看板页生效**（设计如此，非缺陷）。

## 产物 / Assets

| 文件 | 说明 |
|---|---|
| `dsh-minato-3.0.5-win-x64-setup.exe` (+`.sha256`) | Windows 安装器（双击即装） |
| `dsh-minato-win-x64.zip` (+`.sha256`) | Windows 免安装包 |
| `dsh-minato-linux-x64.tar.gz` (+`.sha256`) | Linux x64 包（`./install.sh`） |
| `hashes.txt` / `hashes.txt.asc` | 全部产物的 SHA-256，及其 GPG 签名 |

## 验证 / Verification

- **本地就绪度门槛 13 项全绿**；契约测试 **355** · GUI 逻辑 **68** · 桥接插件自测 **25 块** · v2 单元测试 **318**
- **CI 两平台全绿**（windows-latest / ubuntu-latest）；Windows 包内含 **GUI 窗口冒烟**
- 病因由代码定位 + 常量核实（TTL=4 秒 ✓），修复路径经上述全部测试；Linux 真机验证仍停留在 **3.0.0**，
  本版只有 CI 的打包与冒烟，**没有重跑真机**，如实写明。

## 许可 / License

[MIT](https://github.com/sakanamaru/dsh-minato/blob/main/LICENSE)（代码）。**图标不是 MIT** —— 见 `docs/ASSETS.md`。
GitHub：[@sakanamaru](https://github.com/sakanamaru)

</details>
