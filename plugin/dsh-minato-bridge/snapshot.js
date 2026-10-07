/**
 * dsh-minato-bridge —— **纯逻辑**部分（零依赖：只用 node 内置模块，**不 import 任何 dsh 包**）。
 *
 * 这样拆分的原因与 C# 侧"领域层 / 平台层"一致：把不依赖宿主的逻辑单独放，就能用普通 `node` 直接自测，
 * 不需要装 dsh、也不需要它的 peer 依赖。
 *
 * ★★ 2026-09-30 子代理审计后的修正（四条真缺陷，都会让桥接**完全失效或给出错数据**）：
 *   F2 ✗✗ **`ctx.sessionQuery.listSessions()` 是 async**（`Promise<SessionRecord[]>`）✗
 *      → 原来 `raw = q.listSessions() || []` 拿到的是 Promise ✗
 *      → `for (const item of raw)` **在 try/catch 外** → `TypeError: raw is not iterable` ✗
 *      → `tick` 吞掉 → **真实 dsh 里永远不写文件** ✗✗（实测确认 ✓）
 *      ✓ 现在：**同步与异步两种形状都认** ✓（`await` ✓ 且 `for...of` 挪进 try ✓）
 *   F2b ✗ **记录形状也读错了**：`SessionRecord = { header, live, persisted }` ✗
 *      而原来读 `item.id || session.id || session.sessionId` → **一个都不存在** → 收集 0 条 ✓
 *      ✓ 现在：**`header` 优先** ✓
 *   F3 ✗ 时间戳：原来写**数字** ✗ 而 C# 的 `Str()` 只在**字符串**时返回 → **数字被丢成空串** ✓
 *      → 面板显示 `created=unknown last=unknown` ✓ 且 **GUI 默认排序退化** ✗✗
 *      ✓ 现在：**写 ISO 字符串** ✓（与 `ParseSessionProjection` 的产物一致 ✓）
 *   F4 ✗ `live` / `identity` 读的属性 dsh 根本没有（`Session` 只有 `header/id/surface/seq`）✗
 *      → `ctx.sessions.list()` 里的会话**按定义都是活的** ✓ 而原来报 `live:false` ✗✗
 *      → 面板会显示「**已结束**」（错误断言 ✗ 而不是"未知" ✓）
 *      ✓ 现在：**`record.live` 优先 ✓ store 路径恒为 true** ✓
 *   F5 ✗ 缺失值强转 0 ✗ → **摧毁了工具箱"绝不假装 0"的诚实边界** ✓
 *      → 而且全 0 快照会**遮蔽**好的磁盘投影 ✗✗
 *      ✓ 现在：**缺字段就不写那个字段** ✓（C# 侧 `Has*` 由"存在性"判定 ✓ 正是为此设计的 ✓）
 *   F6 ✗ 非字符串 `outFile` 会让 `apply()` **同步抛**（插件加载期崩 ✗）✓ 现在类型检查 ✓
 *   F8 ✗ 陈旧快照被永久信任（dsh 退出后仍显示"运行中"）✓ 现在 dispose 时**清掉 live 标记** ✓
 */
import fs from "node:fs";
import path from "node:path";

/** 快照格式版本（工具箱侧 Dsht.Domain.Services.SessionStats.SnapshotFormatVersion 必须一致）。 */
export const SNAPSHOT_FORMAT_VERSION = 2;

/** 默认快照路径：<DSH_HOME>/shio-bridge/sessions.json（DSH_HOME 未设则退回 ~/.dsh）。
 *  ★★ 这个目录名**必须与 C# 侧 `SnapshotPath` 完全一致** ✗✗
 *  （曾经不一致：CLI 读 `toolkit-bridge/` 而这里写 `shio-bridge/` → **快照永远读不到** ✓ 已修 ✓） */
export function defaultOutFile(env) {
	const home = (env && env.DSH_HOME) || "";
	const base =
		home && home.trim().length > 0
			? home.trim()
			: path.join(process.env.HOME || process.env.USERPROFILE || ".", ".dsh");
	return path.join(base, "shio-bridge", "sessions.json");
}

/** 取投影单元的值：注册表快照给的是值本身，磁盘投影是 { ver, seq, val } —— 两种形状都认。 */
function unitValue(values, key) {
	if (!values) return undefined;
	const u = values[key];
	if (u === undefined || u === null) return undefined;
	return u.val !== undefined ? u.val : u;
}

/** 有限数 → 整数；**取不到就返回 undefined** ✓（F5：不假装 0 ✓ 由 buildSnapshot 决定写不写 ✓）。 */
function numOrUndef(v) {
	if (v === undefined || v === null) return undefined;
	const n = Number(v);
	return Number.isFinite(n) ? Math.trunc(n) : undefined;
}

/** 非空字符串，否则 undefined ✓（F5 同上 ✓）。 */
function strOrUndef(v) {
	return typeof v === "string" && v.length > 0 ? v : undefined;
}

/** epoch 毫秒（数字或数字字符串）→ **ISO 字符串** ✓（F3：C# 的 Str() 只认字符串 ✓）。
 *  取不到返回 undefined ✓ —— **绝不写 0** ✗（0 会被显示成"1970 年"✓ 比 unknown 更糟 ✓）。 */
function epochToIso(v) {
	const n = numOrUndef(v);
	if (n === undefined || n <= 0) return undefined;
	try {
		const d = new Date(n);
		return Number.isNaN(d.getTime()) ? undefined : d.toISOString();
	} catch {
		return undefined;
	}
}

/** 只把**有值**的字段放进去 ✓（F5：C# 侧用"字段存在性"判定 Has* ✓ 正是为此设计 ✓）。 */
function put(out, key, value) {
	if (value !== undefined && value !== null) out[key] = value;
}

/**
 * 组装快照（**纯函数**，不碰 ctx/磁盘/时钟 → 可单测）。
 * @param {Array<{id:string, live:boolean, values:object, header?:object}>} sessions
 * @param {string} generatedAt ISO 时间戳（由调用方传入）
 */
export function buildSnapshot(sessions, generatedAt, intervalMs) {
	const out = [];
	for (const s of sessions || []) {
		if (!s) continue;
		const v = s.values || {};
		const stats = unitValue(v, "sessionStats") || {};
		// P2 FIX (plugin audit MAJOR): the registry hands back the unit's WIRE VIEW, and for
	// tokenUsage that view is already state.totals - not the state object. Reading .totals
	// on the view yielded {} and all four counters were dropped. Accept either shape.
	const tu = unitValue(v, "tokenUsage") || {};
	const totals = tu.totals || tu;
		const pressure = unitValue(v, "contextPressure") || {};
		const meta = unitValue(v, "sessionListMetadata") || {};
		const header = s.header || {};
		const row = { id: strOrUndef(s.id) || "", live: s.live === true };
		// ★ 2026-10-02：实时活动三件（由 apply 按 seq 差值算出后**传进来** ✓ 这里只透传 ✓ 纯函数 ✓）
		if (s.active === true || s.active === false) row.active = s.active;   // true 与 false 都落盘（3.0.4 修复）：false 让"挂着(dsh 内未动)"分支可达，不退回 15 分钟启发式 ✓
		put(row, "lastActiveAt", strOrUndef(s.lastActiveAt));
		put(row, "seq", numOrUndef(s.seq));
		// 标题可能来自投影单元，也可能来自 header ✓（两种都试 ✓）
		put(row, "title", strOrUndef(unitValue(v, "title")) || strOrUndef(header.title));
		put(row, "cwd", strOrUndef(header.cwd) || strOrUndef(s.cwd));
		put(row, "createdAt", epochToIso(header.createdAt !== undefined ? header.createdAt : s.createdAt));
		put(row, "lastPromptAt", epochToIso(meta.lastPromptAt));
		if (meta.blank === true) row.blank = true;   // ✓ 只有真为 true 才写 ✓（false 时省略 → C# 默认 false ✓）
		put(row, "turns", numOrUndef(stats.turns));
		put(row, "steps", numOrUndef(stats.steps));
		put(row, "llmMs", numOrUndef(stats.llmMs));
		put(row, "toolMs", numOrUndef(stats.toolMs));
		put(row, "ttftMs", numOrUndef(stats.ttftMs));
		put(row, "decodeMs", numOrUndef(stats.decodeMs));
		put(row, "decodeTokens", numOrUndef(stats.decodeTokens));
		put(row, "uncachedInputTokens", numOrUndef(totals.uncachedInputTokens));
		put(row, "outputTokens", numOrUndef(totals.outputTokens));
		put(row, "cacheReadTokens", numOrUndef(totals.cacheReadTokens));
		put(row, "cacheWriteTokens", numOrUndef(totals.cacheWriteTokens));
		put(row, "contextWindow", numOrUndef(pressure.contextWindow));
		put(row, "pressureTokens", numOrUndef(pressure.pressureTokens));
		put(row, "surfaceTokens", numOrUndef(pressure.surfaceTokens));
		out.push(row);
	}
	const outObj = { formatVersion: SNAPSHOT_FORMAT_VERSION, generatedAt: strOrUndef(generatedAt) || "", sessions: out };
	// ★ 第 2 轮审查抓到：CLI 用**写死的 30 秒**判新鲜度 ✗ 而 intervalMs 是用户可配的 ✓
	//   → intervalMs 调到 30 秒以上时，**活着的 dsh 会被判成已结束** ✗✗（这是我上一版引入的回归 ✓）
	// ✓ 现在：**把生效的间隔写进快照** ✓✓ 让 CLI 按它推算阈值 ✓（拿不到就不写 ✓ 不猜 ✓）
	const iv = Number(intervalMs);
	if (Number.isFinite(iv) && iv > 0) outObj.intervalMs = iv;
	return outObj;
}

/** 原子写（先写临时文件再 rename）——工具箱可能正好在读到一半，不能让它看到半截 JSON。
 *  F7 已核对：临时文件与目标**同目录** ✓（同卷 ✓ rename 才原子 ✓）· 同步写 ✓ 不会交错 ✓
 *  注意：**没有 fsync** ✓（掉电时可能 rename 先落盘 ✓ 但**不会损坏上一份好的** ✓） */
export function writeSnapshot(file, snapshot) {
	fs.mkdirSync(path.dirname(file), { recursive: true });
	const tmp = file + ".tmp-" + process.pid;
	try {
		fs.writeFileSync(tmp, JSON.stringify(snapshot), "utf8");
		fs.renameSync(tmp, file);
	} catch (e) {
		try { fs.unlinkSync(tmp); } catch { /* F7c：清掉自己留下的临时文件 ✓ */ }
		throw e;
	}
}

/** 把一条 dsh 记录/会话对象归一化成 { id, live, header } ✓（F2b/F4 的防御式读取 ✓）。
 *  两种真实形状：
 *    · `sessionQuery.listSessions()` → `SessionRecord = { header, live, persisted }`
 *    · `sessions.list()` → 活的 `Session = { header, id, surface, seq }` —— **没有 live 字段** ✓
 *      但它在 store 里**按定义就是活着的** ✓ → live 视为 true ✓（F4 ✓） */
function normalize(item, fromStore) {
	if (!item || typeof item !== "object") return null;
	const header = item.header || item.meta || item.identity || {};
	const id =
		strOrUndef(header.id) ||
		strOrUndef(item.id) ||
		strOrUndef(item.sessionId) ||
		strOrUndef(item.session && item.session.id) ||
		"";
	if (!id) return null;
	const live = fromStore ? true : item.live === true;
	return { id, live, header, session: item.session || item };
}

/** 从 ctx 收集会话 ✓。**可能是异步的** ✓（F2：真实 `listSessions` 返回 Promise ✓）
 *  → 所以本函数是 `async` ✓，**调用方必须 await** ✓（index.js / apply 都已 await ✓）。
 *  防御式：服务名/形状不同就少收集 ✓ **绝不抛** ✓（`for...of` 也挪进了 try ✓）。 */
export async function collectSessions(ctx) {
	const list = [];
	try {
		const fromStore = !(ctx && ctx.sessionQuery && typeof ctx.sessionQuery.listSessions === "function");
		const q = (ctx && (ctx.sessionQuery || ctx.sessions)) || null;
		if (!q) return list;
		let raw = [];
		try {
			if (typeof q.listSessions === "function") raw = (await q.listSessions()) || [];
			else if (typeof q.list === "function") raw = (await q.list()) || [];
		} catch {
			raw = [];
		}
		// ✓ F2：`for...of` **必须在 try 内** ✗（原来在外面 → Promise 不可迭代 → 抛 → 静默 ✓）
		if (!raw || typeof raw[Symbol.iterator] !== "function") return list;

		// ★★★ **P1 修复（插件审计 CRITICAL —— 装了插件反而更糟）** ✓✓
		//   ✗✗ `sessionProjections.snapshot()` **需要一个**活的 Session** ✗ ——
		//     它内部调用 `session.snapshotEvents()` / `session.seq`（dsh 自己的实现 ✓）
		//     而 `sessionQuery.listSessions()` 给的是 **`SessionRecord = { header, live, persisted }`** ✗
		//     → `snapshot(record)` **抛 TypeError** ✗ → 被下面的 try/catch 吞掉 → `values = {}` ✗✗
		//     → **真实 dsh 里所有统计值全丢** ✗（只有 id/live/cwd/createdAt ✓）
		//     → 而工具箱**优先用快照**（非空就不用磁盘投影 ✗）→ **token 面板全 unknown** ✗✗
		//        **比不装插件更糟** ✓（审计实测确认 ✓）
		//   ✓ 现在：**从 `ctx.sessions.list()` 建一份「id → 活 Session」索引** ✓✓
		//     投影只喂**索引里的活 Session** ✓；纯持久化（不在 store 里）的会话**没有投影单元** ✓
		//     → **如实留空** ✓（不是错误 ✓ 也不是假装 0 ✓✓）
		const liveById = new Map();
		try {
			if (ctx.sessions && typeof ctx.sessions.list === "function") {
				const live = (await ctx.sessions.list()) || [];
				if (live && typeof live[Symbol.iterator] === "function") {
					for (const s of live) {
						const sid =
							strOrUndef(s && s.header && s.header.id) || strOrUndef(s && s.id) || "";
						if (sid) liveById.set(sid, s);
					}
				}
			}
		} catch {
			/* 取不到活会话 → 索引为空 ✓ 投影留空 ✓ 绝不抛 ✓ */
		}

		for (const item of raw) {
			const n = normalize(item, fromStore);
			if (!n) continue;
			let values = {};
			let seqv;   // ★ 2026-10-02：活 Session 的 seq（"真在动"的证据 ✓）—— 声明在 try 外 ✗ 作用域 ✗ 上一版就是在块外引用块内变量 → ReferenceError 被吞 → 空列表 → 不写快照 ✗✗（测试抓到 ✓）
			try {
				// ✓ P1：**只把活 Session 交给 snapshot()** ✓（record 不行 ✗）
				const target = liveById.get(n.id) || (fromStore ? n.session : null);
				seqv = target ? numOrUndef(target.seq) : undefined;
				const snap =
					target && ctx.sessionProjections && typeof ctx.sessionProjections.snapshot === "function"
						? await ctx.sessionProjections.snapshot(target)
						: null;
				values = (snap && snap.values) || {};
			} catch {
				values = {};
			}
			// ★★★ **N6 修复（复审 MAJOR —— "装了插件更糟"对历史会话仍成立）** ✓✓
			//   ✗ 原来**无条件**发行 ✗ → 而纯持久化会话（不在 store 里 ✓ 没有投影单元 ✓）
			//     `values` 是空对象 `{}` ✓ → **仍然发了一行** ✗
			//     而工具箱**只要快照非空就完全不用磁盘投影** ✗✗（Program.cs:426 ✓）
			//     → **历史会话原本好好的磁盘统计被空值覆盖** ✗ → 面板打 `turns=0 steps=0` ✗✗
			//   ✓ 现在：**没有投影值、而且不是活跃会话 → 干脆不发行** ✓✓
			//     → 工具箱那一份快照就**不非空** → 回退到磁盘投影 ✓ → **历史数据保住** ✓✓
			//     · 活跃会话即使还没有投影值也发行 ✓（`live:true` 是磁盘投影拿不到的事实 ✓）
			const hasValues = values && Object.keys(values).length > 0;
			if (hasValues || n.live === true) {
				list.push({ id: n.id, live: n.live, values, header: n.header, seq: seqv });
			}
		}
	} catch {
		/* 只读桥：任何异常都降级为空列表 ✓ 绝不打断 dsh ✓ */
	}
	return list;
}

/**
 * 纯函数 ✓（不碰 ctx/磁盘/时钟 → 可单测）：按"**相邻两拍之间 seq 是否变过**"给行打实时活动标记 ✓✓
 *
 * ★★ 动机（2026-10-02 用户实测反馈"没有在运行，却显示 运行 6"）✗✗
 *   `live` 只表示"**还在 dsh 进程里挂着**"——桌面端开着时它 store 里的 6 个会话**全是 live** ✗
 *   哪怕其中 5 个已经几天没碰 ✓✓。而"真在动"唯一可靠的进程内证据是 **seq（事件序号）变了** ✓：
 *   long 生成中途 lastPromptAt 不更新 ✗ 但 seq 一直在加 ✓。
 *
 * 规则（诚实边界 ✓✓）：
 *   · 有基线、seq 变了      → `active: true`（这一拍在动 ✓）
 *   · 有基线、seq 没变      → `active: false`（**明确知道没动** ✓ 不是"不知道" ✓）
 *   · 没有基线 / 取不到 seq → 不写字段（**未知** ✗ 绝不假装 ✗）
 *   · `lastActiveAt` 一旦观测到活动就**粘性**带上（之后每拍都带着最近活动时间 ✓）
 *
 * @param {Array} sessions collectSessions 的行（含可选 seq）
 * @param {Map} prevSeq id → 上一拍的 seq（**就地更新**为这一拍的值 ✓）
 * @param {Map} prevActiveAt id → 最近活动 ISO（粘性 ✓）
 * @param {string} nowIso 本拍时间
 */
export function markActivity(sessions, prevSeq, prevActiveAt, nowIso) {
	const rows = [];
	for (const s of sessions || []) {
		if (!s) continue;
		const row = Object.assign({}, s);   // 浅拷贝 ✓ 不改入参 ✓
		const seq = numOrUndef(s.seq);
		if (seq !== undefined) {
			const prev = prevSeq.get(s.id);
			if (prev !== undefined) {
				if (prev !== seq) {
					row.active = true;
					prevActiveAt.set(s.id, nowIso);
				} else {
					row.active = false;   // ★ 明确知道没动 ✓（写 false → C# 侧 HasActive=true ✓）
				}
			}
			prevSeq.set(s.id, seq);
		}
		const la = prevActiveAt.get(s.id);
		if (la) row.lastActiveAt = la;
		rows.push(row);
	}
	return rows;
}

/* ─────────── ★ T3（2026-10-07）：被单独安装时的运行期一次性提示 ───────────
 * 背景（真实痛点 ✓）：第三方插件目录 / 爬虫站把 `plugin/dsh-minato-bridge` 当成一个**独立插件**收录 ✗ ——
 *   从那里进来的访客只看得到本插件的 README 与 package.json ✓ → **很可能只装它** ✓
 *   而它单独存在时**没有任何产出** ✗（只做只读注入，等工具箱来读 ✓）→ 坏的第一印象 ✓
 * 所以：加载本插件后，若**找不到工具箱**，只打印**一次**一行提示 ✓（工具箱在 → 完全静默 ✓）。
 * 硬约束（与插件其余部分一致 ✓）：
 *   · **只读**：不写任何文件、不改 dsh 状态 —— 只有 `fs.existsSync` 探测 + stdout 一行 ✓
 *   · **只提示一次**：模块级内存标志 ✓ 不落盘 ✓
 *   · **任何失败/异常静默吞掉**：提示逻辑绝不能影响 dsh 启动 ✓
 *   · 决策是**纯函数** ✓（「是否找到工具箱」→「是否提示 + 文案」✓）→ 可单测 ✓
 *   · **零新依赖** ✓：只用已有的 node 内置 fs/path ✓（连一个 import 都没新增 ✓）
 */

/** 提示文案 ✓（**一行** ✓ 英文在前、中文关键词在后 —— 目录站访客多半只读到这一屏 ✓）。 */
export const TOOLKIT_MISSING_NOTICE =
	"dsh-minato-bridge: OPTIONAL, READ-ONLY bridge / 可选只读桥接件 for DeepSeek Harness Toolkit (dsh-minato). " +
	"Toolkit not found / 未检测到工具箱 -> installing this plugin alone has no use / 单独安装无用途. " +
	"Toolkit: https://github.com/sakanamaru/dsh-minato | uninstall steps / 卸载步骤: see this plugin's README.";

/** 工具箱可执行名 ✓（与 GUI 的 `CliPath` 同一套 ✓ 见 v3/gui/Dsht.Gui.Avalonia/MainWindow.axaml.cs:1835）。 */
const TOOLKIT_EXE_NAMES = [
	"dsh-minato.exe", "dsht.exe", "dsht_v3.exe", "DeepSeek Harness Toolkit.exe",
	"dsh-minato", "dsht", "dsht_v3", "DeepSeek Harness Toolkit"
];

/**
 * **纯函数** ✓：给定「是否找到工具箱」→ 返回「是否提示 + 文案」。
 * 不碰磁盘 / 环境 / 时钟 ✓（探测在 `findToolkit` ✓ 决策在这里 ✓）→ 可直接单测 ✓
 * @param {boolean} toolkitFound
 * @returns {{notify:boolean, text:string}}
 */
export function startupNotice(toolkitFound) {
	if (toolkitFound === true) return { notify: false, text: "" };
	return { notify: true, text: TOOLKIT_MISSING_NOTICE };
}

/**
 * **纯函数** ✓：在 `startupNotice` 之上叠加「**一个进程只提示一次**」这条硬约束 ✓。
 * 状态由调用方持有（**内存** ✓ 不落盘 ✓）；**不改入参** ✓，返回新状态 ✓。
 * @param {{shown:boolean}} state
 * @param {boolean} toolkitFound
 * @returns {{state:{shown:boolean}, notify:boolean, text:string}}
 */
export function noticeOnce(state, toolkitFound) {
	if (state && state.shown === true) return { state: { shown: true }, notify: false, text: "" };
	const d = startupNotice(toolkitFound);
	return { state: { shown: true }, notify: d.notify, text: d.text };
}

/**
 * **只读探测**工具箱是否已安装 ✓（`fs.existsSync` 而已 ✓ —— 不执行它、不写任何东西、不联网 ✓）。
 * 顺序与仓库既有实现一致 ✓：
 *   ① `DSHT_CLI` 环境变量 ✓（GUI 与 CLI 都认它 ✓ MainWindow.axaml.cs:1831）
 *   ② PATH 里按可执行名找 ✓（GUI `CliPath` 的兜底 ✓）
 *   ③ 常见安装位置 ✓：
 *      · Windows `%LOCALAPPDATA%\Programs\dsh-minato\`（安装器默认位置 ✓ v3/tools/installer.cs:269；
 *        稳定入口 `bin\dsh-minato.exe` ✓ installer.cs:445）
 *      · Linux `~/.local/share/dsh-minato/dsh-minato` 与 `~/.local/bin/`（✓ v3/tools/install.sh:27-28,510）
 * 找不到 → `""` ✓（**不猜** ✓）；任何异常 → 也 `""` ✓（绝不抛 ✓）。
 * @param {object} env 环境变量表（默认 `process.env` ✓）
 * @param {(p:string)=>boolean} exists 存在性判定（默认 `fs.existsSync` ✓ **可注入以便单测**：
 *   注入后本函数不碰真实文件系统 ✓ 这也让"探测是只读的"可被证明 ✓）
 * @returns {string} 工具箱可执行路径，找不到为 `""`
 */
export function findToolkit(env, exists) {
	try {
		const e = env || {};
		const has = typeof exists === "function"
			? exists
			: (p) => { try { return fs.existsSync(p); } catch { return false; } };
		const str = (v) => (typeof v === "string" && v.trim().length > 0 ? v.trim() : "");
		// ① 显式覆盖（GUI / CLI 都认它 ✓）
		const direct = str(e.DSHT_CLI);
		if (direct && has(direct)) return direct;
		// ② PATH
		const pathVar = str(e.PATH);
		if (pathVar) {
			for (const dir of pathVar.split(path.delimiter)) {
				const d = dir.trim();
				if (!d) continue;
				for (const n of TOOLKIT_EXE_NAMES) {
					const p = path.join(d, n);
					if (has(p)) return p;
				}
			}
		}
		// ③ 常见安装位置（Windows 安装器默认位置 ✓ / Linux prefix 与 ~/.local/bin ✓）
		const local = str(e.LOCALAPPDATA);
		if (local) {
			for (const p of [
				path.join(local, "Programs", "dsh-minato", "dsh-minato.exe"),
				path.join(local, "Programs", "dsh-minato", "bin", "dsh-minato.exe")
			]) if (has(p)) return p;
		}
		const home = str(e.HOME) || str(e.USERPROFILE);
		if (home) {
			for (const p of [
				path.join(home, ".local", "share", "dsh-minato", "dsh-minato"),
				path.join(home, ".local", "bin", "dsh-minato"),
				path.join(home, ".local", "bin", "dsht")
			]) if (has(p)) return p;
		}
	} catch {
		/* 探测失败 = 当作没找到 ✓ 绝不抛 ✓ */
	}
	return "";
}

/** T3 提示的**内存**状态 ✓（模块级 = 一个 dsh 进程最多提示一次 ✓ 不落盘 ✓）。 */
const noticeState = { shown: false };

/**
 * 定时把快照写到磁盘。**放在这里而不是 index.js**：它不 import 任何 dsh 包（只用到 ctx 传进来的对象），
 * 因此可以脱离 dsh 自测。index.js 只负责 cordis 声明并重新导出它。
 * 任何失败静默——插件绝不能打断 dsh ✓。
 */
export function apply(ctx, config) {
	const cfg = config || {};
	if (cfg.enabled === false) return;
	// ★ T3：被单独安装时的**一次性**提示 ✓（纯函数决策 ✓ 内存标志 ✓ 只读 ✓ 异常静默 ✓）
	if (!noticeState.shown) {
		let decision = { notify: false, text: "" };
		try { decision = noticeOnce(noticeState, findToolkit(process.env) !== ""); } catch { /* 探测/决策异常 → 按"不提示"处理 ✓ */ }
		noticeState.shown = true;   // 无论成败都只试一次 ✓（内存 ✓ 不落盘 ✓）
		try { if (decision.notify) console.log(decision.text); } catch { /* stdout 不可用也不影响 dsh ✓ */ }
	}
	// ✓ F6：非字符串 outFile 会让 `.trim()` 抛 → **插件加载期崩** ✗（正是头注释说绝不能发生的 ✓）
	const cfgOut = typeof cfg.outFile === "string" ? cfg.outFile.trim() : "";
	const outFile = cfgOut.length > 0 ? cfgOut : defaultOutFile(process.env);
	// P4 FIX (plugin audit MINOR): Node clamps a delay above 2 to the 31st to 1 ms, so a
	// config of 1e21 or Infinity (YAML .inf) made the bridge write about a thousand
	// snapshots per second. Clamp to a sane range instead.
	const rawInterval = Number(cfg.intervalMs);
	const interval = Number.isFinite(rawInterval) ? Math.min(Math.max(1000, rawInterval), 3600000) : 3000;
	let disposed = false;
	// ★ 2026-10-02：seq 差值的记忆（"真在动"的唯一实时证据 ✓ 见 markActivity ✓）
	const lastSeq = new Map();
	const lastActiveAt = new Map();
	const tick = async () => {
		// P3 FIX (plugin audit MAJOR): disposed was only checked on entry, so a tick already
		// awaiting listSessions resumed after the dispose handler cleared the live flags and
		// wrote live:true back - the exact stale state that handler exists to prevent.
		// It is re-checked after the await below.
		if (disposed) return;
		try {
			const sessions = await collectSessions(ctx);
			if (disposed) return;   // P3 FIX: re-check after the await
			// ✓ 零会话**不写** ✓（避免用空数据覆盖上一份好的 ✓）
			if (sessions.length === 0) return;
			const now = new Date().toISOString();
			// ★ 先按 seq 差值打"真在动"标记 ✓ 再组装 ✓（`live` ≠ 在动 ✗ 见 markActivity 注释 ✓）
			writeSnapshot(outFile, buildSnapshot(markActivity(sessions, lastSeq, lastActiveAt, now), now, interval));
		} catch {
			/* 只读桥：静默降级 ✓ */
		}
	};
	const timer = setInterval(() => { void tick(); }, interval);
	if (timer && typeof timer.unref === "function") timer.unref(); // 不要因为它而拖住进程退出
	void tick();   // 首帧 ✓（F2：现在是异步的 ✓ 用 void 显式丢弃 ✓ 内部已 try/catch ✓）
	try {
		if (ctx && typeof ctx.on === "function") {
			ctx.on("dispose", () => {
				disposed = true;
				clearInterval(timer);
				// ✓ F8：dsh 退出后**陈旧快照不能继续被信任** ✗
				//   （否则 `live:true` 留在盘上 → 面板永远显示"运行中" ✓）
				//   → 把所有 live 置 false 并刷新 generatedAt ✓（保留其余数据 ✓ 只去掉会撒谎的那一位 ✓）
				try {
					const old = JSON.parse(fs.readFileSync(outFile, "utf8"));
					if (old && Array.isArray(old.sessions)) {
						for (const s of old.sessions) s.live = false;
						old.generatedAt = new Date().toISOString();
						writeSnapshot(outFile, old);
					}
				} catch {
					/* 没有快照 / 读不动 → 没什么可清的 ✓ */
				}
			});
		}
	} catch {
		/* 没有 dispose 钩子也不影响功能 */
	}
}
