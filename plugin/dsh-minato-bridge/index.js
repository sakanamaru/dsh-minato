/**
 * dsh-minato-bridge —— cordis 插件入口。
 *
 * **零依赖**：本文件只 import 同目录的 snapshot.js，不 import 任何 dsh 包。
 *
 * 为什么（真机验证教训，2026-09-28）：最初这里 `import z from "@deepseek-ai/schemastery"` 想按 dsh 官方插件
 * 的做法声明配置 schema —— 但从**本地路径**安装时，该导入会从插件源码目录解析 → `ERR_MODULE_NOT_FOUND`
 * → **dsh 启动直接失败**（plugin tree failed to load ✗）。可选插件绝不该有这种能力，所以：
 *   · 不 import 任何 dsh 包（配置直接用 patch 行里的 config，不做 schema 校验）
 *   · 所有取值仍然防御式（见 snapshot.js）
 *
 * 纪律（三条硬约束）：
 *   ① 只读：不写 dsh 状态、不发模型请求、不读会话正文、不联网；
 *   ② 不阻塞：定时器异步写，任何异常都吞掉（插件坏了不能影响 dsh 的运行）；
 *   ③ 可卸载：整行由 cordis.patch.yml 注册，`dsh plugin remove` 或工具箱的 `profilepatch --disable`
 *      都能一键还原；插件缺失时工具箱只用磁盘投影，功能降级但不报错。
 *
 * 注意：**导入期错误会阻止 dsh 启动**（dsh 的行为，不是本插件能控制的）——
 * 所以先在临时 profile 里装一次确认能起来，再装到你日常用的 profile。
 */
import { apply, buildSnapshot, collectSessions, defaultOutFile, defaultSessionsRoot, decodeFirstFrameHeader, extractLineageEntry, findToolkit, noticeOnce, scanSessionsLineage, startupNotice, TOOLKIT_MISSING_NOTICE, writeSnapshot, SNAPSHOT_FORMAT_VERSION } from "./snapshot.js";

export { apply, buildSnapshot, collectSessions, defaultOutFile, defaultSessionsRoot, decodeFirstFrameHeader, extractLineageEntry, findToolkit, noticeOnce, scanSessionsLineage, startupNotice, TOOLKIT_MISSING_NOTICE, writeSnapshot, SNAPSHOT_FORMAT_VERSION };

/** cordis 插件名（与 cordis.patch.yml 里的 id 对应）。 */
export const name = "shio-bridge";

/**
 * 依赖的 ctx 服务：会话注册表 + 会话查询 + 会话投影注册表（cordis DI 会等它们就绪）。
 *
 * ★★★ **真机 dsh 测试抓到的 bug（2026-10-01）** ✓✓
 *   ✗ 原来只有 `["sessions","sessionProjections"]` ✗ —— 而 `snapshot.js` 的 `collectSessions`
 *     用的是 **`ctx.sessionQuery.listSessions()`** ✗✗（`sessionQuery` 没被声明 ✓）
 *   → cordis 不保证未声明的服务存在 ✓ → `ctx.sessionQuery` 是 undefined ✓
 *     → `collectSessions` 内部 `catch` 吞掉 ✓ → **返回空数组** ✓ → `apply` 的 `tick()`
 *       因 `sessions.length === 0` **直接 return** ✓ → **永不写快照** ✗✗
 *   → 表现：插件**装上了、配置也注入了**（`--dump-config` 能看到 `- id: shio-bridge` ✓
 *     和 `config: {enabled:true, intervalMs:3000}` ✓）**但什么都不发生** ✗
 *   → 而**单元测试永远测不出来** ✗ —— 它自己喂 `ctx`（`{sessionQuery:{listSessions}}` ✓）
 *     所以"少声明一个依赖"这种错在单测里**天然是绿的** ✓✓
 *   ✓ 现在：**把 `sessionQuery` 声明上** ✓✓（dsh 里由 `session-query-sqlite` 提供 ✓）
 *   ✓ 并加了一条**单测**：断言 `inject` **覆盖** `snapshot.js` 真正用到的每个 `ctx.*` ✓✓
 */
export const inject = ["sessions", "sessionQuery", "sessionProjections"];
