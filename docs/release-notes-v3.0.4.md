<p align="center">
  <img src="https://raw.githubusercontent.com/sakanamaru/dsh-minato/main/logo.png" alt="dsh-minato" width="180">
</p>

# dsh-minato 3.0.4 — 2026-10-05（四路审查修复 / fixes from a four-way review）

> ⚠️ 非官方工具，由社区独立开发，与 DeepSeek 官方无关。本工具**不是 dsh 插件**：它是独立进程，不注入 dsh，dsh 没装也能用。

**一句话**：一轮"从零重读"的四路代码审查（正确性 / 安全面 / 跨层契约 / 插件）抓出 4 个高危问题，全部修复——其中包括"0.2.0 的活动检测其实只做了一半"和"体检报告会泄漏余额 key"。

**In one line**: a fresh four-way review found four high-severity bugs — all fixed, including a plugin
feature that was only half-wired and a health report that leaked the balance key.

---

## 修复 / Fixed

- **桥接插件丢弃 `active=false`（高危）**：`buildSnapshot` 只写 `true`，"挂着（dsh 内未动）"这个三态分支
  **永远走不到** → 退回 15 分钟启发式 → 停止活动后 15 分钟内仍显示"运行中"——正是 0.2.0 要消灭的误报，
  核心功能只做了一半。JS 测试只测过 `true`，`false` 只有 C# 在测，**端到端断链** ✗ 现两侧都钉住 ✓
  **The bridge dropped `active:false`** — the "parked (confirmed idle)" tier was unreachable.
- **`doctor --report` 泄漏 balance_key（高危）**：消毒器词表没有 `balance_key`，32 位 hex 的 key 也够不到
  40+ 位的 hex 规则 → 用户把报告发出去求助 = **key 外泄**，与报告自称"（脱敏）"直接矛盾。消毒词表已补 ✓
  **The doctor report leaked the balance key** past the sanitizer.
- **体检期间排队的刷新被永久丢弃（高危）**：体检那 ~6.5 秒里触发的刷新只会入队，体检结束的完成回调
  不处理队列 → 页面停在旧数据，还会多闪一次加载指示。已对齐统一收尾路径 ✓
- **看板 KPI 继承会话页的旧快照（高危）**：用过"非空"筛选再切到看板，四张卡显示**子集之和**
  且挂着错误的"（父会话）"标签。聚合现在只在会话页才采信列表快照 ✓
- **`auto_start_target` 缺 CONFIG 行**：配置模型、白名单、序列化全有，唯独 config-get 没输出 →
  GUI 设置页**永远渲染不出"开机自启启动什么"**（CLI 还在提示你去改它 ✗）。已补 ✓
- **balance_key 三处明文露出（中）**：config-get 回显明文（进终端回滚缓冲）、设置页无掩码、
  保存时 toast 把含 key 的完整命令弹 8 秒。现在：CLI 只报 `set`/空 ✓；设置页专用编辑器——
  **不回显、留空=保持不变、一键清除** ✓；toast 打码 ✓
- **备份结果弹的是上一个动作的旧文案（中）**：`BACKUP_FAIL` 等真实错误用户根本看不到 ✗ 已改为弹本次结果 ✓
- **balance_key 拒绝换行（低）**：自由文本键含 CR/LF 可向 launcher.config 注入任意配置行 ✗ 已拒收 ✓

## 审查来源 / how these were found

四个独立上下文的审查（各自从零重读代码，不带主对话记忆）：新代码正确性、安全面、跨层契约、JS 插件。
审查同时确认了：balance 命令确实只读（key 只进 Authorization 头）、GUI 全程 `ProcessStartInfo` 无 shell
（经典命令注入不可行）、动作闸门占用/释放路径本身无问题、插件定时器安全。

## 已知边界 / Known limits（本轮如实未修）

- GUI"字符串拼命令行"的系统性习惯（引号拼接而非 `ArgumentList`）**未迁移**——当前残余为低危旗标注入。
- `Run` 的 **30 秒硬超时**会杀长任务：update / install / 大目录备份可能中途被杀，留下半截备份包。
- 概览页的状态大徽章不走字段级刷新；配置落盘无 DPAPI（Windows）/ 0600（Linux）加固、非原子写入。
- 插件两个"存疑"项（时间戳类型、sessionQuery 形状）取决于 dsh 实际行为，标疑未改。

## 产物 / Assets

| 文件 | 说明 |
|---|---|
| `dsh-minato-3.0.4-win-x64-setup.exe` (+`.sha256`) | Windows 安装器（双击即装） |
| `dsh-minato-win-x64.zip` (+`.sha256`) | Windows 免安装包 |
| `dsh-minato-linux-x64.tar.gz` (+`.sha256`) | Linux x64 包（`./install.sh`） |
| `hashes.txt` / `hashes.txt.asc` | 全部产物的 SHA-256，及其 GPG 签名 |

## 验证 / Verification

- **本地就绪度门槛 13 项全绿**；契约测试 **355** · GUI 逻辑 **68** · 桥接插件自测 **25 块**（新增 active=false 端到端断言）· v2 单元测试 **318**
- **CI 两平台全绿**（windows-latest / ubuntu-latest）；Windows 包内含 **GUI 窗口冒烟**
- **审查复核**：每条修复都经主会话按锚点逐字核实后才落盘（一个锚点因缩进差异打歪，被"OK/FAIL 逐条报告"当场抓住重打 ✓）
- Linux 真机（虚拟机）验证仍停留在 **3.0.0** —— 本版只有 CI 的打包与冒烟，**没有重跑真机**，如实写明

## 许可 / License

[MIT](https://github.com/sakanamaru/dsh-minato/blob/main/LICENSE)（代码）。**图标不是 MIT** —— 见 `docs/ASSETS.md`。
GitHub：[@sakanamaru](https://github.com/sakanamaru)
