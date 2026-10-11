# dsh-minato-bridge

> ⚠️ **This is not the toolkit — it is an optional, read-only bridge. Installing it on its own does nothing useful.**
> **The toolkit is a separate program: <https://github.com/sakanamaru/dsh-minato>** (Windows + Linux · CLI + GUI).
> Nothing consumes this plugin's output unless that toolkit is installed.
> **Installed only this plugin?** Uninstall it — steps below.

**中文**：⚠️ **这不是工具箱本体。** 它是 **DeepSeek Harness Toolkit（`dsh-minato`）的可选只读桥接件** ——
**单独安装它没有任何用途**（它只把 dsh 的会话状态写出一份快照，而没有任何东西来读它）。
完整工具在仓库根：**<https://github.com/sakanamaru/dsh-minato>**。只装了这个插件？→ 见下面「只装了这个插件？请卸载」。

---

## What it is / 它是什么（一句话）

dsh 进程里有一个**只有它自己知道**的事实：**哪些会话此刻还活着** —— 它不落盘 ✗。
本插件每 3 秒（可配）把这份事实，连同一部分 token / 统计数字，写成一份小 JSON ✓ —— **只读、只写这一份文件** ✓。

装了它，工具箱的「会话 / token」面板能多显示 **运行中 / 挂着** 这一列 ✓，
看板的两档血缘口径（仅父会话 / 父会话+子代理）也会**解锁** ✓；
不装，那一列显示 `unknown` ✓、血缘口径只留「全局」（chips 灰显「需桥插件血缘」）✓，**其余一切照常** ✓（工具箱改用磁盘投影）。

## Uninstall / 只装了这个插件？请卸载

> 下面的每一步都是**读代码确认**的（不是凭记忆 ✓），出处标在括号里 ✓。
> 我只在一台机器的两个 profile 上**只读观察**过实际文件 ✓，**没有实测卸载** ✓。

### ① 先确认它装进了哪个 profile

- 工具箱装的默认是 **`web`**（`v3/src/Dsht.Cli/Program.App.cs:97`）。
- **`desktop` profile 由 dsh 桌面应用独占管理**：命令行**装不进、也卸不掉** ✗
  （dsh 会直接拒绝：`profile "desktop" is managed exclusively by the Electron application`，`dsh/lib/bin.js:29`）
  → 去**桌面端的插件对话框**里删掉它。

### ② 卸掉包（会把它从 profile 的 `dsh.profile.bundles` 一并摘掉）

```bash
dsh plugin --profile web remove dsh-minato-bridge
```

`dsh plugin` 只是把参数**原样转发给 pnpm**（`dsh/lib/bin.js:105-110`）→ **需要 pnpm 在 PATH 上**
（`npm i -g pnpm`；dsh 不会替你装）。成功后它会把 `dsh-minato-bridge` 从
`<profile>/package.json` 的 `dsh.profile.bundles` 列表里移除（`dsh/lib/plugin-Ddi42qoW.js:46-78`）。

### ③ 从 profile 的补丁文件里**删掉 `shio-bridge` 那一块**

```text
文件：<DSH_HOME 或 ~/.dsh>/profiles/<profile>/cordis.patch.yml
```

工具箱的 `bridge-install` 发现缺行时会**自动把这块合并进去**（`Program.App.cs:240-326`，
合并时在同目录留一份 `cordis.patch.yml.bak-before-bridge-<时间戳>`）。
而 **`dsh plugin remove` 不碰这个文件** ✗（它只改 profile 的 `package.json`）→ 所以这一步要手动做。
把下面整块（连同上面那行注释）删掉：

```yaml
    # dsh-minato-bridge: read-only snapshot (merged into this insert list, not a new one)
    - id: shio-bridge
      name: 'dsh-minato-bridge'
      config:
        enabled: true
        outFile: ''
        intervalMs: 3000
```

⚠️ 不删的后果：dsh 启动时会去加载一个**已经不存在的包** ✗ —— 本插件真机验证时，一个没解析成功的
导入就让 dsh **启动直接失败**（`plugin tree failed to load`，见文末记录）→ 别省这一步。

**不想手改 YAML？** 用工具箱自己把这一行关掉（只加 `disabled: true`、**不删行**、会先备份）：

```bash
dsh-minato profilepatch --profile web --id shio-bridge --yes
```

（读码确认：`v3/src/Dsht.Cli/Program.Doctor.cs:267-313`；**未实测**。）

### ④ 重启 dsh

插件只在**启动时**加载 → 改完补丁文件必须重启才生效。

### ⑤（可选）删掉快照文件夹

```text
<DSH_HOME 或 ~/.dsh>/shio-bridge/     # 里面只有本插件写的 sessions.json
```

删掉不影响 dsh；工具箱读不到它时自动退回磁盘投影（`v3/src/Dsht.Cli/Program.Sessions.cs:45-96`）。

## Get the toolkit / 完整工具怎么装

| | |
|---|---|
| 仓库（README · 截图 · 全部文档） | **<https://github.com/sakanamaru/dsh-minato>** |
| 下载（Windows 安装包 · Linux 压缩包） | **<https://github.com/sakanamaru/dsh-minato/releases>** |
| 装本插件（工具箱自带命令） | `dsh-minato bridge-install --profile web --yes` |

- **Windows**：下载 `dsh-minato-<version>-win-x64-setup.exe`，双击。
- **Linux**：下载 `dsh-minato-linux-x64.tar.gz` → `tar -xzf … && cd … && ./install.sh`。

## What it reads / writes（能力与只读承诺）

**读什么**（dsh 的公开插件接口 + 磁盘原始日志的**首帧 header**，逐项防御式取值）：

- `ctx.sessions` / `ctx.sessionQuery.listSessions()` → 会话清单 + `live` 标记（**唯一"进程内才有"的事实**）
- `ctx.sessionProjections.snapshot(session)` → `tokenUsage` / `sessionStats` / `contextPressure` / `sessionListMetadata` / `title`
- **`~/.dsh/sessions/` 下原始会话日志的首帧 header**（血缘用，2026-10-09 第三批）——
  递归扫描 `session.vN.jsonl.zstd` 与旧式 `session.jsonl.zstd`，每个文件**只读前 64 KiB**，
  用 `node:zlib.zstdDecompressSync` **只解首帧**取 `parentSession` / `origin` / `createdAt` → 快照 `lineage` 段；
  **正文帧一帧不解** ✓ · 解码失败计入 `lineage.errors` 并如实降级，绝不猜 ✓

**绝不读**：会话正文（消息内容）✗ —— 原始日志也只解**首帧 header**、正文帧一帧不解；其余只读计数、时间与元数据（id / 标题 / cwd / 用量）。

**写什么**（**唯一**的写入）：

- `<DSH_HOME>/shio-bridge/sessions.json` —— 一份小 JSON，**原子写**（同目录 `.tmp-<pid>` + `rename`）

**绝不写**：dsh 的状态 / 配置 / 会话文件（一个字节都不写）✗ · 不发模型请求 ✗ · 不联网 ✗ · 不阻塞 dsh ✗
（定时器 + 全 try/catch，任何失败静默忽略 —— 插件坏了不能影响 dsh）。

**运行期一次性提示**：找不到工具箱时，加载后**只打印一次**一行提示
（内存标志 · 不落盘 · 工具箱存在时完全静默 · 任何异常静默吞掉）。
它**也不写任何文件** —— 可核对：`test/snapshot.test.js` 里的
「T3：提示路径一个文件都不写」（apply 跑完临时目录仍为空）。
决策本身是**纯函数**（`startupNotice` / `noticeOnce`），单测直接覆盖。

**可核对性**：本插件只 `import` node 内置的 `node:fs` / `node:path` / `node:zlib`（`snapshot.js` 头几行即可核对）
—— 没有网络、没有第三方包。

## 兼容性与验证 / Compatibility & verification

### 兼容性（只写**实测过**的；未验证的版本不写范围 ✗）

| 依赖 | 已实测 ✓ | 说明 |
|---|---|---|
| DSH | **0.2.0-rc.2**（nightly 桌面版） | 装载 + 启动：2026-09-28 隔离 `$DSH_HOME` + 端口 3999 ✓（见文末验证记录）；端到端血缘快照：2026-10-09 真实桌面实例，与 node 独立扫描器对拍 MATCH ✓。其它版本**未验证**；`0.1.5-rc.2` 判定**不兼容**（见下） |
| Node.js | **v24.19.0**（本插件测试机）· **24.21.0**（DSH 0.2.0-rc.2 桌面版内嵌运行时） | `engines`：`>=22.15.0 <23.0.0 \|\| >=23.8.0` —— 下限 = 血缘段使用的 `node:zlib.zstdDecompressSync` 的引入版本（官方 API 文档「Added in: v22.15.0 / v23.8.0」：[v22 文档](https://nodejs.org/docs/latest-v22.x/api/zlib.html#zlibzstddecompresssyncbuffer-options) · [v23 文档](https://nodejs.org/docs/latest-v23.x/api/zlib.html#zlibzstddecompresssyncbuffer-options)；v20 无此 API，23.0–23.7 也没有，所以范围故意绕开这段空窗）。更老的 Node **不是装不上，而是血缘段会降级**（见下），其余功能照常 |

**`0.1.5-rc.2`（旧版全局 CLI）：不兼容（静默不工作）。** 2026-10-11 探针（临时 `$DSH_HOME` + 端口 3997，只读，未改动任何安装）：插件**装得上、宿主起得来、无报错**，但 0.1.5 的依赖树里**找不到 `sessionQuery` / `sessionProjections` 的 provide 方**（只有 `sessions`）→ 插件声明的 inject 凑不齐 → `apply()` 不执行 → 永远不写快照、也不报错。这正是「Known limits」说的「dsh 改内部接口时静默降级」—— 所以判定不兼容、不计入已实测。

**降级行为（按代码核实，`snapshot.js`）**：宿主 Node 没有 `zstdDecompressSync` 时**不会崩** ——
逐会话解码失败 ⇒ 该会话无血缘条目、`lineage.errors` +1（`:280-293`）；整段扫描失败 ⇒ 快照不写 `lineage` 键、
工具侧如实显示「血缘未知」（`:618-628`）。快照其余部分（会话清单 / token / 统计）照常。

### 验证记录（谁、何时、怎么复验）

| 日期 | 验证 | 结果 |
|---|---|---|
| 每次改动（任何人可复跑） | `node test/snapshot.test.js` | **41 passed, 0 failed** |
| 2026-09-28 | 隔离 `$DSH_HOME` 装载 + 启动（端口 3999） | ✓（第一次因 schemastery 导入失败 → 改零依赖后通过，见文末记录） |
| 2026-10-08 | 全库复验：283 投影 + 319 原始日志 | header `origin==="subagent"` ⟺ 投影 `identity` 非空 **283/283 完全一致** |
| 2026-10-08 | 最大 v4 日志（43,756,264 B）整文件切帧 | **9,859 帧 / 0 失败**，帧边界扫描与全量解压逐帧 MATCH（差 0） |
| 2026-10-09 | 真实桌面端到端：血缘边 283 = 199 subagent + 18 fork + 66 root | errors=0；node 独立扫描器 vs 桥插件**双引擎对拍 MATCH ×2** |
| 2026-10-11 | 真实库血缘不变量 I1–I9（只读脚本复跑） | **全 PASS**（290 投影；26 条未认领按既有降级方案走） |
| 2026-10-11 | 真实库血缘扫描（插件自身 `scanSessionsLineage`，只读） | 293 个会话目录 / 326 个 zstd 文件 / **290 个 header 成功 / 3 个无 header 如实计 / 0 解码错误** / 83 ms |

### 不涉及（只读承诺，与上文「What it reads / writes」一致）

**不调用模型 ✗ · 不联网 ✗ · 不写 dsh 状态 / 配置 / 会话文件 ✗ · 不读凭据、不读会话正文 ✗**
（原始日志也只解首帧 header，正文帧一帧不解）。唯一写入 = 每 3 秒一份小 JSON 快照。

## Snapshot format (v2，工具箱侧 `SessionStats.ParseSnapshot` 按此解析)

```json
{
  "formatVersion": 2,
  "generatedAt": "2026-09-28T00:00:00.000Z",
  "sessions": [
    {
      "id": "…", "live": true, "title": "…", "cwd": "D:\\work",
      "createdAt": "2026-09-10T00:26:40.000Z", "lastPromptAt": "2026-09-10T00:27:40.000Z", "blank": false,
      "turns": 2, "steps": 9, "llmMs": 1000, "toolMs": 500, "ttftMs": 300,
      "decodeMs": 2000, "decodeTokens": 400,
      "uncachedInputTokens": 100, "outputTokens": 50, "cacheReadTokens": 900, "cacheWriteTokens": 10,
      "contextWindow": 1000, "pressureTokens": 250, "surfaceTokens": 500
    }
  ],
  "lineage": {
    "errors": 0,
    "sessions": {
      "<会话 id>": { "origin": "subagent", "parent": "<父会话 id>", "createdAt": 1735689600000 }
    }
  }
}
```

- 时间戳是 **ISO 8601 字符串**（如 `2026-09-10T00:26:40.000Z`）—— **不是数字** ✗
  （C# 侧的 `Str()` 只认字符串；写数字会被丢成空串 → 面板显示 `unknown`，默认排序也会退化）
- 缺数据时**整个字段不写**（工具箱用「字段存在性」判 `Has*`）—— 不写 `0`，不假装有数据
- `formatVersion` 不匹配时工具箱**不解析**（诚实降级，不猜）
- `lineage` 段（2026-10-09 第三批，**纯增量** —— `formatVersion` 仍是 2）：由原始日志**首帧 header** 解码而来
  （只解首帧、正文帧一帧不解）；`origin`/`parent`/`createdAt` 缺哪个就省哪个；纯根会话是**空对象 `{}`**
  （= 血缘**已知**且无父，别与「无血缘」混淆）；段缺失/字段不符 ⇒ 工具箱血缘口径如实降级，绝不猜

## Self-test / 自测（零依赖，不需要 dsh）

```bash
cd plugin/dsh-minato-bridge
node test/snapshot.test.js     # 打印 "== N passed, 0 failed =="（2026-10-09：41 passed）
```

覆盖纯函数：字段映射、两种投影形状、防御式收集、原子写、`DSH_HOME` 语义、`apply` 首帧与
`enabled:false`、实时活动标记、运行期提示的全部决策分支，以及**血缘首帧解码**（递归目录遍历、
`session.vN.jsonl.zstd` 与旧式 `session.jsonl.zstd` 两种文件名、64 KiB 读上限、解码失败计数与降级）。

## Known limits / 已知限制

- **ctx 服务名与调用形状是依据 dsh 已发布包的 README 写的**
  （`ctx.sessions.list()` / `ctx.sessionQuery.listSessions()` / `ctx.sessionProjections.snapshot(session)`），
  所有取值都做了防御（服务缺失或形状不同 → 跳过该字段，绝不猜）。
  **dsh 改内部接口时它会静默降级**（不报错、也没有数据）—— 需要时看 dsh 的输出。
- **"快照真的写出来了"这一步在隔离实例里尚未验证**：隔离根里一个会话都没有，而插件设计上
  **只在有会话时才写**（避免用空数据覆盖上一份好的数据）。要验证，需在隔离实例里真的发一条消息，
  再看 `<DSH_HOME>/shio-bridge/sessions.json`（命令见文末）。
- 它**不是**工具箱的必需组件：没有它，面板数据来自磁盘投影（`storages/session_projcache/`）。

## License

MIT（与本仓库一致）。本插件不包含任何第三方代码，只使用 dsh 的公开插件接口。

## Verification log / 真机验证记录（2026-09-28，隔离 `$DSH_HOME` + 临时 profile + 端口 3999）

**第一次：失败 ✗（真实缺陷，已修）**

```text
dsh: plugin tree failed to load: failed to import loader entry shio-bridge (dsh-minato-bridge):
Cannot find package '@deepseek-ai/schemastery' imported from .../plugin/dsh-minato-bridge/index.js
```

原因：`index.js` 里 `import z from "@deepseek-ai/schemastery"`（想按 dsh 官方插件做法声明配置 schema）——
从**本地路径**安装时该导入从插件源码目录解析 → `ERR_MODULE_NOT_FOUND` → **dsh 启动直接失败**。
**教训：可选插件绝不能因为一个未解析的导入就拖垮 dsh 的启动** → 已改为**零依赖**（不 import 任何 dsh 包，
配置直接取 patch 行里的值，不做 schema 校验）。

**第二次：通过 ✓**

```text
index.js 语法检查 ✓ · 自测全绿 ✓ · index.js 不再含任何 @deepseek-ai 导入 ✓
dsh --profile bridgetest --port 3999 启动成功（3999 监听）✓ · 输出无 "plugin tree failed" ✓
用户正在跑的 3080 实例（PID 16748）全程未被触碰 ✓
```

**给你的复现命令**（隔离根已经建好并装好插件了，直接复用）：

```bash
export DSH_HOME=/tmp/dsht_plug_iso        # Windows: $env:DSH_HOME="$env:TEMP\dsht_plug_iso"
dsh --profile bridgetest --port 3999
# 浏览器打开 http://127.0.0.1:3999 发一条消息，然后：
cat "$DSH_HOME/shio-bridge/sessions.json"     # 应出现快照，live 会随会话启停变化
```