<p align="center">
  <img src="https://raw.githubusercontent.com/sakanamaru/dsh-minato/main/logo.png" alt="dsh-minato" width="220">
</p>

# dsh-minato 3.1.0 — 2026-10-13（更新中心修正 + 看板/更新页打磨 / update-centre fixes and polish）

> **非官方**：本项目与 DeepSeek 官方无关，是非官方社区工具。**只读本机**：不注入 dsh、不上传任何数据。
> 本工具**不是** dsh 插件：它是独立进程，dsh 没装也能用。

**一句话**：更新中心不再报错误的仓库地址（`repository` 对象形态）、同一个已装插件在页脚与更新页显示成同一个版本形态，
并把 5 处仍带施工语气的用户文案改成短、平、事实。

**In one line**: the update centre no longer prints a wrong repository URL for the object form of
`repository`, the same installed plugin now shows one consistent version form in the footer and on the
update page, and five user-visible lines that still read like a build log were rewritten into short,
plain statements.

```
commit=d8284f8fc0fcc3eababe0ad8cba41972719b9a3c
tag=v3.1.0
tag-ci-run=38354333374
```

> 上面三行是**来源块**：`commit` 是产出本版产物的提交（全长 SHA），`tag-ci-run` 是该 tag 的 CI 构建 run id（本版：success）。


## 用户需要采取行动吗？

**是** —— 请升级到 3.1.0 再使用「更新」页。

- **为什么**：3.0.7 的「更新」页在插件的 `package.json` 把 `repository` 写成**对象**时（`{"type":"git","url":"…","directory":"…"}`，
  npm 两种写法都合法；本仓库自带的桥接插件就是这一形态）会把这个对象里的第一把**键** `type` 当成仓库地址显示出来。
  升级后显示的是对象里的 `.url`；取不到就显示 `unknown`（**不猜**）。
- **不升级会怎样**：只是**显示**不对，不影响备份/恢复/数据 —— 本工具所有命令都是只读或显式确认后才写，
  没有"不升级就会丢数据"的风险。
- **怎么做**：从本版 release 资产里取 Windows 安装器/便携包或 Linux 包安装（见下「产物」）；
  已经装好的用户**替换可执行文件**即可（配置、备份、`~/.dsh` 数据都不动）。升级后第一次打开「更新」页按「刷新/检查更新」。
- **插件**：`dsh-minato-bridge` 随工具箱发布（0.2.1）。需要「运行中」那一列的 profile，请按更新页给出的那一行命令重装/更新插件；
  **不装插件也能用**，只是那一列显示 `unknown`。

## 本版改了什么（3~5 行）

- **更新中心**：`repository` 三种形态（对象 / 字符串 / 缺失）各按事实取值，对象取 `.url`；判定只有**一份**代码（Domain 层），CLI 与 GUI 单测共用。
- **本体自查**：`update-center` 用 GitHub 的 `/releases/latest` 查本工具自己的最新**正式**版（天然排除预发布与草稿）。
- **同一个版本号只显示成一个样子**：页脚与更新页统一为 `vX.Y.Z`；离线取值路径复用同一个函数。
- **用户文案**：5 组仍带 `✓`/`✗` 与内部旁白的字符串改成短、平、事实；可执行指引与退出码一字未变。
- **版本**：工具箱 `3.1.0` · 桥接插件 `0.2.1`。

<details>
<summary><b>详细内容 / Details</b>（完整清单 · 验证证据 · 已知边界 · 许可）</summary>

## 完整清单 / Full changelog

### 看板 / Board

- **三档口径**：`global`（全局）/ `parents`（仅父会话）/ `parents_sub`（父会话 + 子代理）——后两档需要桥接插件的血缘段；
  缺血缘时看板**如实提示降级**，不假装有数据。
- **图表**：随筛选口径联动重算。
- **自定义区间**：看板可按自定义时间区间取数（不再只有固定几档）。
- **内联口径说明**：口径差异的说明从浮层改成筛选条下方的**内联展开块**（不再点开才看得见）。
- **页脚版本**：页脚分两行显示，第二行给出**工具箱版本**与**桥接插件版本**（都是实读，读不到就说不读到）。

### 更新中心 / Update centre

- **本体（minato）自查最新正式版**：查 GitHub `/releases/latest`；该端点天然排除预发布，正合"stable 只提示正式版"。
  取不到时把**原始原因**（HTTP 状态码 / 超时 / 连不上）单独打成 `UPDATECENTER_DIAG minato <原因>`，
  绝不把"取不到"糊成一个 `unknown`；一条查询失败**不影响**其它组件任何一行。
- **插件以「工具箱自带那一份」为基准**：基准 = `<exe 目录>\plugin\dsh-minato-bridge\`（离线、零新渠道）。
  判定是**双比对**：先比版本号（`VersionComparer`），再比内容哈希（`snapshot.js` 与 `index.js` 各一份 SHA-256）。
  于是「版本号相同但内容不同」（半旧/被改过）能被单独报成 `content-mismatch`，只看版本号抓不出来。
  基准缺失（源码构建常见）→ 如实报 `latest=unknown state=unknown` 并打 `UPDATECENTER_DIAG`。
- **多 profile 合并成一张卡**：同一个插件在多个 profile 上的行**一条都不丢**，但 URL 与那段说明只发一次；
  每行独有的修复指引随该行发出（`next=`）。
- **表格化与文案精简**：卡身是固定五列的小表 `profile ｜ 已装 ｜ 工具箱自带 ｜ 状态 ｜ 下一步`；
  长路径与整段机制说明搬去 GUI「说明」页（**内容一个字没丢**，并有断言钉住它真的还在）。
- **URL 取值修正（本版）**：`repository` 是对象时取对象内的 `url`（此前会取到键名 `type`）；字符串原样用；取不到 `unknown`。

### 插件 / Bridge plugin 0.2.1

- **血缘首帧桥 + 启动提示**：暴露"哪一列会话在跑"（运行态是进程内事实，磁盘投影给不了），并给出启动期提示。
- **兼容性声明**：见 `plugin/dsh-minato-bridge/README.md` 的验证记录表 —— **只写已被验证的组合**，
  未验证的版本明写「未验证」，判定不兼容的版本明写「不兼容」（`engines` 与 README 同源）。

### 开发者 / Developer

- **同步脚本**：`v3/tools/sync-gui-dev.ps1` —— 开发运行时把 GUI 与同目录 CLI 对齐的一步操作（避开"改了 GUI 却在跑旧 CLI"的坑）。

## 验证证据 / Verification

> 规矩：每条"已验证"必须指向**一条会失败的测试**或**一次真实端到端运行**；指不到就写"已实现；测试未覆盖"。
> 下面所有数字都是 **2026-10-13 本批改动后本地实跑**的结果（非预估）。

| 门槛 | 结果 |
|---|---|
| CLI 构建 | **0 错误**（13 警告，均为既有的 SYSLIB0014 / CS0162 等） |
| **GUI 构建** | **0 错误 / 0 警告** |
| `v3/tests/Dsht.Contracts.Tests`（契约） | **446/446 passed, 0 failed**（此前 443 → 本版 +3 条 URL 三态断言） |
| `v3/gui/Dsht.Gui.LogicTests`（GUI 逻辑） | **192/192 passed, 0 failed**（此前 187 → +5 条：URL 三态 3 条 + 版本形态统一 2 条） |
| `plugin/dsh-minato-bridge` `node test/snapshot.test.js` | **41 passed, 0 failed** |
| `v3/tests/verify_fixes.ps1` | **76/76**（`[SELF]` 命令面：代码 37 个 / 检查表 37 个，一一对应；命令面数字未变） |
| `v3/tests/verify_restore_apply.ps1` | **41/41**（隔离数据根真实写盘 + 零越界） |
| `v3/tests/verify_command_matrix.ps1` | **80/80 passed, 0 failed（XFAIL 0）** |
| `v3/tests/verify_plugin_update_baseline.ps1` | **32/32 passed, 0 failed**（此前 31 → +1 条：对象形态 `repository` 的 URL 行；行数断言由 6 改 7） |
| **`v3/tests/compare_markers.ps1`（标记行契约）** | **21/21 对齐**（纯模式；退出码 0） |
| **`v3/tests/verify_switchover.ps1`（就绪度门槛）** | **`== 全部就绪 ==`**（13 项全 `[READY]`，退出码 0） |

逐条"已验证"指向什么：

- 已验证：`repository` 是**对象**时取到对象内的 `.url` —— 证据：真机 `update-center` 原文里该插件的 URL 行是
  `UPDATECENTER_URL plugin:probe-objrepo https://example.invalid/probe-objrepo.git`（**不是**字面值 `type`），
  由 `verify_plugin_update_baseline.ps1` 的 fixture 端到端断言钉住；同一判定的三态由
  `Dsht.Contracts.Tests`（3 条）与 `Dsht.Gui.LogicTests`（3 条）分别钉住。
- 已验证：`repository` 是**字符串** → 原样返回；**缺失/形态不对** → 空串（上层写 `unknown`）—— 证据：上面两组单测。
- 已验证：同一个已装插件版本在**页脚**与**更新页**显示成同一个形态（都带 `v`）—— 证据：
  `Dsht.Gui.LogicTests` 的"页脚与更新页形态一致"断言（`VersionMarkers.DescribeBridge` 与
  `UpdateDiagnosis.VersionText` 在同一输入上必须相同）。
- 已验证：本体自查走 `/releases/latest`，取不到时**逐字给出原始原因**且**不假装**有最新版 —— 证据：本机实跑
  `update-center` 的原文是 `UPDATECENTER_ITEM minato kind=minato installed=3.1.0 latest=unknown state=unknown`
  紧接 `UPDATECENTER_DIAG minato 没取到最新版（只报告，因此无法判断是否有更新）：超时（6 秒内没有回应，网络慢或不通）`。
  **注意**：这台机器上 GitHub API 本次**超时**了，所以"查到最新版并判成 has-update / newer-than-latest"那条路径
  本次**没有真机复现**；它的判定与文案由 `Dsht.Gui.LogicTests` 的 minato 断言
  （`LatestState` 两态 + `HintText` 贴 DIAG 原文）覆盖。
- 已验证：`wipe` **只打印路径、不删任何东西** —— 证据：`verify_switchover.ps1` 的 gate8（隔离根里目标原样还在）。
- 已验证：v2.x 发布/校验链**逐字未被动过** —— 证据：`verify_switchover.ps1` 的 `invariant release chain`
  （`verify.ps1` 与 `origin/main` 逐字一致）+ `invariant v2.x release build`（csc 编译 16 个源文件通过）。
- 已验证：`check` 的标记行契约与 v2.x 逐字一致 —— 证据：`compare_markers.ps1` 的 `check` 用例 21/21。
- 已实现；测试未覆盖：见下「已知边界」。

## 已知边界 / Known limits

- **`content-mismatch` 态只有单测与 fixture 覆盖**：仓库里没有"真机上装着一个半旧插件"的样本，
  因此这一态在真机的触发**未被观察到**；能证明的是 fixture 端到端（`verify_plugin_update_baseline.ps1`
  造一个"版本相同、`snapshot.js` 多一行"的包 → 断言 `content-mismatch`）+ 单测。
- **「有新版本」分支未在真机触发**：本次实跑时 GitHub API **超时**（见上「验证证据」最后一条），
  所以真机看到的是 `latest=unknown` + DIAG 原因，**不是** `update-available`；该分支的判定逻辑由单测覆盖。
- **英文界面未逐条审校**：本版改动的用户文案是中文；对应的英文串保持原意同步改了，
  但没有做逐条英文润色审校（未验证）。
- **CI 只覆盖 Windows / Ubuntu**：macOS 与其它发行版未在 CI 里跑（也未在真机验证）；
  本地门槛跑不到 6 个 shell 验证脚本与 Linux 平台实现（`verify_switchover.ps1` 会如实列出这些盲区）。
- **GUI 观感类改动无自动化测试**：布局/配色/折行这类改动只有人工过页面，没有自动化断言；
  逻辑层（标记行解析、表格取值、版本形态）有纯函数单测。
- **`check` 命令里 `dsh : <路径> ✓` 那一行仍带 `✓`**：它是 v2.x 的**逐字比对契约**（`compare_markers.ps1` 的
  `check` 用例，且该行不在那条用例的忽略规则内），因此**逐字保留**；要动它得先改契约与 v2.x 侧，本版不做。
  （用户可见文案里其余残留的 `✓`/`✗` 都在**源码注释**里，不在输出里 —— 本版已逐条核对过 CLI 输出。）
- 本工具**只读本机**：不注入 dsh、不联网上传任何数据；会联网的命令只有
  `check` / `update-info` / `update-center` / `doctor` / `install` / `update` / `verify-install --url` / `balance`（仅当填了 key）。

## 产物 / Assets

| 文件 | 说明 |
|---|---|
| `dsh-minato-3.1.0-win-x64-setup.exe` (+`.sha256`) | Windows 安装器 |
| `dsh-minato-win-x64.zip` (+`.sha256`) | Windows 免安装包 |
| `dsh-minato-linux-x64.tar.gz` (+`.sha256`) | Linux x64 包（`install.sh` + `hashes.txt`） |

> 资产的实际名称与数量以 Lead 打 tag 后 GitHub Release 上的清单为准（本文件不预填未产出的文件名）。
> `tag-ci-run` 与 `commit` 同样由 Lead 回填（见顶部来源块）。

## 许可 / License

- 代码：[MIT](https://github.com/sakanamaru/dsh-minato/blob/main/LICENSE)。**图标不是 MIT** —— 见 `docs/ASSETS.md`。
- **非官方**：与 DeepSeek 官方无关，是非官方社区工具。
- **只读**：不注入 dsh、不改 dsh 状态、**不上传任何数据**。
- GitHub：[@sakanamaru](https://github.com/sakanamaru)

</details>
