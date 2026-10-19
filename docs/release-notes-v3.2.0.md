<p align="center">
  <img src="https://raw.githubusercontent.com/sakanamaru/dsh-minato/main/logo.png" alt="dsh-minato" width="220">
</p>

# dsh-minato 3.2.0 — 2026-10-19（成本面板 / cost panel）

> **非官方**：本项目与 DeepSeek 官方无关，是非官方社区工具。**只读本机**：不注入 dsh、不上传任何数据。
> 本工具**不是** dsh 插件：它是独立进程，dsh 没装也能用。

**一句话**：新增**只读**的 `cost` 命令与看板「指标」页成本卡——金额**全部是估算**，只来自**你自己填的单价**；
并顺带修掉一个**既有数据缺陷**：会话 id 前缀不归一导致同一会话被重复计入。

**In one line**: a new read-only `cost` command plus a cost card on the dashboard's Metrics page. Every
amount is an **estimate** derived only from unit prices **you** enter. This release also fixes an existing
data defect where session ids were not normalised, so some sessions were counted twice.

```
commit=<待补>
tag=v3.2.0
tag-ci-run=<待补>
```

> 上面三行是**来源块**：`commit` 是产出本版产物的提交（全长 SHA），`tag-ci-run` 是该 tag 的 CI 构建 run id（由发版流程注入）。

## 用户需要采取行动吗？

**是** —— 想用成本面板就升级到 3.2.0；不升级**没有任何数据风险**（其余命令行为不变）。

- **为什么**：`cost` 命令与看板成本卡是本版新增。金额**只在你填了单价之后**才显示：
  四个价格键 `sessions_price_{in,out,cache_read,cache_write}_per_mtok`（**美元/百万 token**，`0` = 未填），
  可选每日预算键 `sessions_budget_usd_per_day`（`0` = 未设）。填法与口径见 `docs/COST.md`。
- **不升级会怎样**：一切照旧——本版**没有**改 `sessions` 等既有命令的输出契约（见下「验证证据」的逐字节 A/B）。
- **怎么做**：从本版 release 资产里取 Windows 安装器 / 免安装包，或 Linux 包（见下「产物」）；
  已装用户**替换可执行文件**即可（配置、备份、`~/.dsh` 数据都不动）。
- **一个数字会变小（与升级无关的修正）**：本版修复的 id 归一缺陷会让 `sessions` / `cost` 里**同一会话只出现一次**
  ⇒ 与旧版相比，会话行数与合计会**变小**（当时实测：`sessions --days 7` 317 → **309**、`cost` 36 → **30**）。
  这是**修正**，不是丢数据。
- **插件**：`dsh-minato-bridge` 随工具箱发布，本版**插件零改动**（仍 `0.2.1`）；不装插件也能用（只是"运行中"那一列显示 `unknown`）。

## 本版改了什么（3~5 行）

- **成本面板**：`cost` 命令（只读）＋ 看板「指标」页成本卡（今日 / 当前窗口 / 会话明细 + **最后使用模型**近似）＋ 设置页四个单价键与每日预算键。
- **金额口径**：只用 `output=` 那一桶计价（✗ 不用 `decodeTokens`，它会低估）· **未配价一律不显示任何金额** ·
  金额是**估算**，以官方账单为准；✗ 不预置任何价格 ✗ 不读第三方价表。
- **数据正确性修复**：会话 id 前缀不归一 ⇒ 同一会话被计两次 · `SESSION_CHILD` 联表与血缘查表失败 ·
  非 `global` 档带前缀的**活会话在聚合层被整条排除**（`global` **高估** / 非 `global` **低估**）。
- **金额精度**：`cost=` 由固定 6 位改**往返精度** ⇒ 非零**永不**渲染成 `0` / `0.000000`（真值 `1.18e-7` 如实显示为 `1.18E-07`）。
- **版本**：工具箱 `3.2.0` · 桥接插件 `0.2.1`（未变）。

<details>
<summary><b>详细内容 / Details</b>（完整清单 · 验证证据 · 已知边界 · 产物 · 许可）</summary>

## 完整清单 / Full changelog

### 成本面板（CLI）/ Cost panel (CLI)

- **`cost` 命令（只读）**：`cost [--days 7|14|30 | --from <yyyy-MM-dd> --to <yyyy-MM-dd>] [--level global|parents|parents_sub]`
  —— 窗口参数与 `sessions` **逐字同一套**（含成对 / 互斥 / 格式校验与失败面）。
- **标记行（v3 专有 · 冻结契约）**：`COST_META` / `COST_TOTAL`（`scope=today|window` **两块制**）·
  `COST_SESSION`（`scope=window`，每会话一行）· `COST_BUDGET` · `COST_NOTE`（**多值**，出现即累加）· `COST_FAIL`。
- **为什么是两块**：一张卡要同时说清「今日」与「当前窗口」，而 `COST_META` 的 `days/from/to` 只能描述**一个**窗口；
  ⇒ 用 `scope=` 区分两块（✗ 不往 META 里塞 `today_*` 键——两处真相源=坑）。窗口恰好是今天时两块数值相同（无害）。
- **预算行**：`COST_BUDGET` 恒按「今日 · `level=global`」比较（✗ 不随 `--level` 变：`parents_sub` 会把父与子都算进来 ⇒ 假超限）；
  命令带了非 `global` 档时追加 `COST_NOTE budget-scope-global`。**超限只是提示，不阻止任何操作**——这不是收费/配额系统。
- **金额公式**：`(uncached×输入价 + cacheRead×缓存读价 + cacheWrite×缓存写价 + output×输出价) ÷ 1,000,000`（USD），
  四桶 1:1 直取 dsh 会话投影的 `tokenUsage.val.totals.*`。
- **同一次输出**：`cost` 与看板共用同一次 `overview` 聚合（✗ 不为成本再起一个进程 ✗）。实测：加成本块后
  `overview --days 7` 为 2047–2370 ms，旧版（无成本块）2099–2374 ms——同一拍，未翻倍。

### 看板成本卡 / Dashboard cost card

- 「指标」页**顶部**：**今日** / **当前窗口** / **会话明细**（token + 估算金额（若配价）+ **最后使用模型**近似，并给 unknown 计数）。
- **未配价**：只显示 token ＋一句「未配置单价 → 不显示金额（设置页可填）」；✗ 绝不显示 `$0` 假装成"没有花费"。
- **金额显示四段**：`≥$0.01` 两位小数 · `<$0.01` 六位 · `<$0.0000005` 字面 `<$0.000001` · **真零不给 `$`**（另配"不可算/未配价"分句）。
- **今日卡脚注三要素**（必带）：整会话累计、非今日增量 · 按最后提问归属 · N 个无锚点会话被排除（N 只取 CLI 的 `COST_META unknown_last`）。
- **设置页**：四个单价键 + 每日预算键接线（✗ 不新造键 ✗ 不改后端校验规则）。

### 数据正确性修复 / Data correctness（既有缺陷）

- **机制**：桥接快照里的会话 id **原样保留**（可能带 `session-` 前缀），而磁盘投影文件名 / 投影总表键 / `childId` / 血缘边
  **本来就都是裸 UUID** ⇒ 同一会话两种形态 ⇒ 去重失败。（插件只是**转述** dsh 给的 id，不是插件缺陷；本版未动 `plugin/**`。）
- **双向伤害**：`global` 档**高估**（同一会话两条都进账）；非 `global` 档**低估**（带前缀那份走层级判定时**整条被排除**，
  活会话的实时数字丢失）。另有 `SESSION_CHILD` **联表失败**与血缘查表失败（那条永远 `children=unknown depth=unknown`）。
- **修法（单一源头）**：新增 `SessionStats.NormalizeSessionId`（剥**一个**前导 `session-`，`Ordinal` 大小写敏感），
  由快照解析 / 投影总表 / 血缘 id+parent / CLI 磁盘循环 / `SESSION_CHILD` 父 id 共用 ⇒ 输出即规范形态。
- **当时实测**（库是活的，用**不变量**表述更稳）：`SESSION 行数 == 归一后不同 id 数`（修复前差 8）·
  `COST_SESSION 行数 == 归一后不同 id 数`（修复前差 6）· 输出里带 `session-` 前缀的行 = 0；
  行数对照 `sessions --days 7` **317 → 309**、`SESSAGG_SESSION bucket=own` **36 → 30**、`cost` **36 → 30**。

### 金额精度 / Precision

- **问题**：原实现把金额四舍五入到 6 位 ⇒ 真值 `1.18e-7` 被写成 `0.000000` ⇒ 界面只能说"金额为 0"，但那**不是 0**。
- **修法**：`Amount` 不再舍入（保留完整十进制值）＋ `Format` 用往返精度（`G29`）⇒ 形态如 `0.00075` / `1.18E-07` /
  `24755.699751`（大额精度不减）；**真零**写 `0`；**`none` 只表示"未配价 / 不可算"**（✗ 绝不用 0 冒充）。
- 图形界面用 `decimal.TryParse(NumberStyles.Float)` 解析，定点与科学计数两种写法都认。

### 文档与文案 / Docs and copy

- **`docs/COST.md`（新建）**：怎么填价 · 口径 · 为什么不预置价 · 为什么不读第三方价表（三条理由）· 已知边界。
- 三个语言版 README 补 `cost` 命令行与离线命令清单（`cost` 全程**不联网**）。
- 四个价格键的 `CONFIGNOTE` 去掉 v3.2 之前的旧话（「费用显示的前置是多帧解码；当前只存不算」）；
  `v3/README.md` 的标记行说明同步。

## 验证证据 / Verification

| 门槛 | 结果 |
|---|---|
| CLI 构建（`Dsht.Cli`） | **0 个错误** |
| GUI 构建（`Dsht.Gui.Avalonia`） | **0 个错误** |
| `v3/tests/Dsht.Contracts.Tests`（契约） | **495/495 passed, 0 failed** |
| `v3/gui/Dsht.Gui.LogicTests`（GUI 逻辑） | **238/238 passed, 0 failed** |
| 插件自测（`plugin/dsh-minato-bridge`） | **41 passed, 0 failed** |
| `v3/tests/verify_fixes.ps1` | **77/77**（`[SELF]` 命令面：代码 **38** / 检查表 **38**，一一对应） |
| `v3/tests/verify_restore_apply.ps1` | **41/41 passed, 0 failed**（rc=0） |
| `v3/tests/verify_command_matrix.ps1` | **119/119 passed, 0 failed（XFAIL 0）**（rc=0） |
| `v3/tests/compare_markers.ps1` | **21/21 对齐**（rc=0；v2.x 差分行未破） |
| `v3/tests/verify_switchover.ps1` | **`== 全部就绪 ==`**（13 项全 `[READY]`，rc=0） |

逐条"已验证"指向什么：

- **已验证**：金额按四桶分别计价，且**只用 `output=`**（不是 `decodeTokens`）——证据：契约测试的手算用例
  （`100×1 + 200×2 + 30×3 + 40×4 ÷ 1e6 = 0.00075`）＋ 命令矩阵 §1d 的精确整行断言 ＋ 反例断言
  （按 `decodeTokens` 会得 `0.0002`，断言必须**不等**于 `0.00075`）。
- **已验证**：**未配价不出现任何金额**——证据：矩阵 §1d（断言全输出无 `cost=<数字>`，且 `COST_NOTE price-missing` 在）。
- **已验证**：窗口集合与 `sessions` 的 `SESSAGG_SESSION bucket=own` **逐 id 一致**——证据：矩阵 §1d 的逐 id 比对。
- **已验证**：极小非零**不渲染成零**——证据：矩阵 §1f（118 token × 0.001 USD/Mtok ⇒ `cost=1.18E-07`，并复刻 GUI 的
  `decimal.TryParse(NumberStyles.Float)` 证明该字符串在界面侧可解析且 > 0）。
- **已验证**：会话 id 归一（去重 / 联表 / 血缘）——证据：矩阵 §1e（磁盘裸 id ＋ 快照同一会话**两种 id 形态各跑一遍**：
  `SESSION` 行数 == 不同 id 数 · `SESSION_CHILD` 的父 id 能找回 · `children=1 depth=0` · 且两种形态的
  sessions/COST 段**逐字相同**）＋ 契约测试 11 条（含大小写敏感、只剥一个、空 id）。
- **已验证**：`sessions` 输出**未被本轮改动**——证据：冻结夹具上 `dd68ef5`（改动前）与本版 exe 的
  **10 组参数 stdout SHA-256 逐字节相同**（由独立复核方复跑）。
- **已实现；测试未覆盖**：见下「已知边界」。

## 已知边界 / Known limits

- **GUI 首帧像素未核验**：成本卡的布局 / 折行只有人工过页面与源码探针（`LiveText("")`、`IsVisible = false` 机械核对），
  **没有**首屏像素级验证；本仓库对观感类改动一贯没有自动化断言。
- **`budget-scope-global` 未在真机验证**：本机桥接插件快照是 `lineage=none` ⇒ 非 `global` 档在真机**如实 FAIL**（降级文案）；
  该提示行由夹具覆盖（矩阵 §1d 的 `--level parents` 用例）。
- **真机 lineage 快照未验**：同上（`lineage=none`）⇒ `parents` / `parents_sub` 的**真机**取值路径未走通（夹具态已验证）。
- **小数单价的端到端对拍未做**：门槛里用的是整数价（1/2/3/4）与 `0.001`；`0.14` 这类小数单价 × 大 token 的手算对拍**未做**。
- **观感与截图无自动化测试**：界面外观只有人工查看，无视觉回归断言。
- **CI 只覆盖 Windows / Ubuntu**：macOS 与其它发行版未在 CI 跑；本地门槛也跑不到 6 个 shell 验证脚本与 Linux 平台实现
  （`verify_switchover.ps1` 会如实列出这些盲区）。
- **口径（不是缺陷）**：金额是**估算**，单价由**用户自填**（`0` = 未填）；✗ 不预置任何价格 ✗ 不读第三方价表
  （① 路径与安装方式强耦合 ② 覆盖缺口正中主力模型 ③ dsh 自己从不读它——详见 `docs/COST.md` §4）；
  **以官方账单/平台计费为准**，本工具**不做**对账。每日预算恒按「今日 · `global`」比较（避免父子重复计数造成假超限）。

## 产物 / Assets

| 文件 | 说明 |
|---|---|
| `dsh-minato-3.2.0-win-x64-setup.exe` (+`.sha256`) | Windows 安装器 |
| `dsh-minato-win-x64.zip` (+`.sha256`) | Windows 免安装包 |
| `dsh-minato-linux-x64.tar.gz` (+`.sha256`) | Linux x64 包（`install.sh` + `hashes.txt`） |

> 资产的实际名称与数量以 tag 之后 Release 上的清单为准（本文件不预填未产出的文件名）。
> `commit` 与 `tag-ci-run` 同样由发版流程注入（见顶部来源块）。

## 许可 / License

- 代码：[MIT](https://github.com/sakanamaru/dsh-minato/blob/main/LICENSE)。**图标不是 MIT** —— 见 `docs/ASSETS.md`。
- **非官方**：与 DeepSeek 官方无关，是非官方社区工具。
- **只读**：不注入 dsh、不改 dsh 状态、**不上传任何数据**。
- GitHub：[@sakanamaru](https://github.com/sakanamaru)

</details>
