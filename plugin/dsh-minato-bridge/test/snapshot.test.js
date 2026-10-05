/**
 * 零依赖自测（只用 node 内置 assert / fs / os / path）。
 * 运行：node test/snapshot.test.js   （在 plugin/dsh-minato-bridge 目录下）
 *
 * ★★ 2026-09-30 子代理审计后的重写（F10：「**测试绿但测的是错的契约**」✗）：
 *   旧测试的四个问题，都会让"绿"变成假绿 ✗：
 *     ① mock 了**同步**的 `listSessions` ✗ —— 而真实 dsh 返回 **Promise** ✓
 *        （正因如此，F2 那个"真实环境永远不写文件"的缺陷，测试**完全测不出来** ✓✓）
 *     ② mock 的记录形状是 `{ id, live, session }` ✗ —— 而真实是
 *        `SessionRecord = { header, live, persisted }` ✓ / `Session = { header, id, surface, seq }` ✓
 *     ③ 断言时间戳是**数字** ✗ —— 而 C# 的 `Str()` 只认**字符串** ✓ → 数字会被丢成空串 ✓
 *     ④ 断言缺字段 → **0** ✗ —— 而工具箱的诚实边界是"**缺字段就不假装 0**" ✓
 *        → 全 0 快照会**遮蔽**好的磁盘投影 ✗✗
 *   现在：**按 C# 侧真正读的字段与类型做契约测试** ✓✓
 *   （字段表逐条对应 `v3/src/Dsht.Domain/Services/SessionStats.cs` 的 `ParseSnapshot` ✓）
 */
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { buildSnapshot, collectSessions, defaultOutFile, writeSnapshot, apply, markActivity, SNAPSHOT_FORMAT_VERSION } from "../snapshot.js";
import { name as pluginName, inject as pluginInject } from "../index.js";

let pass = 0;
let fail = 0;
const failures = [];
async function check(name, fn) {
	try {
		await fn();
		pass++;
		console.log("  [PASS] " + name);
	} catch (e) {
		fail++;
		failures.push(name);
		console.log("  [FAIL] " + name + " -> " + e.message);
		process.exitCode = 1;
	}
}

console.log("== dsh-minato-bridge 自测（零依赖）==");

// ---- ① 契约：C# 侧 `ParseSnapshot` 读的每个字段与类型 ✓✓ ----
// 逐条对应 SessionStats.cs:86-106（字段名、大小写、类型都必须一致 ✓）
const CONTRACT = {
	id: "string",
	live: "boolean",
	title: "string",
	cwd: "string",
	createdAt: "string",        // ✗ 不是数字：C# 用 Str() 读，数字会变空串 ✓
	lastPromptAt: "string",     // ✗ 同上
	blank: "boolean",
	turns: "number",
	steps: "number",
	llmMs: "number",
	toolMs: "number",
	ttftMs: "number",
	decodeMs: "number",
	decodeTokens: "number",
	uncachedInputTokens: "number",
	outputTokens: "number",
	cacheReadTokens: "number",
	cacheWriteTokens: "number",
	contextWindow: "number",
	pressureTokens: "number",
	surfaceTokens: "number",
};

/** 真实形状 ①：`sessionQuery.listSessions()` → **Promise**`<SessionRecord[]>` ✓
 *  `SessionRecord = { header, live, persisted }` ✓（F2b） */
function realRecordCtx() {
	const rec = makeRecord("sess-1", "D:\\work", 1788517824758);
	const live = makeLiveSession("sess-1", "D:\\work", 1788517824758);
	return {
		// sessionQuery gives the LIST (records, with live flags) ...
		sessionQuery: { listSessions: async () => [rec] },
		// ... and sessions.list() gives the LIVE objects the projection registry needs.
		sessions: { list: async () => [live] },
		sessionProjections: projectionStub({
			sessionStats: { turns: 2, steps: 9, llmMs: 1000, toolMs: 500, ttftMs: 300, decodeMs: 2000, decodeTokens: 400 },
			// P2 FIX: the registry hands back the WIRE VIEW, i.e. state.totals - not { totals: ... }.
			tokenUsage: { uncachedInputTokens: 100, outputTokens: 50, cacheReadTokens: 900, cacheWriteTokens: 10 },
			contextPressure: { surfaceTokens: 500, contextWindow: 1000, pressureTokens: 250 },
			sessionListMetadata: { blank: false, lastPromptAt: 1788517999999 },
			title: "hello",
		}),
	};
}

// ---- buildSnapshot：契约字段与类型 ----
const built = buildSnapshot(
	[{ id: "s1", live: true, header: { cwd: "D:\\work", createdAt: 1788517824758, title: "hello" },
	   values: { sessionStats: { turns: 2, steps: 9 }, tokenUsage: { totals: { outputTokens: 50 } },
	             contextPressure: { contextWindow: 1000 }, sessionListMetadata: { lastPromptAt: 1788517999999 } } }],
	"2026-09-28T00:00:00Z"
);
const row = built.sessions[0];

await check("格式版本为 2", () => assert.equal(built.formatVersion, SNAPSHOT_FORMAT_VERSION));
await check("generatedAt 由调用方传入（纯函数不读时钟）", () => assert.equal(built.generatedAt, "2026-09-28T00:00:00Z"));
await check("**契约字段类型正确** ✓（存在时必须类型对 ✓；缺字段是**设计** ✓）", () => {
	// ✗ 第一版断言"所有字段都必须存在" ✗ —— **太严** ✓：
	//   `buildSnapshot` 只在**有值**时才写那个字段 ✓（F5：不假装 0 ✓）
	//   而 C# 侧正是用「字段存在性」判 `HasStats/HasTokens/HasPressure` ✓✓ **设计如此** ✓
	//   → 所以缺字段**不是**缺陷 ✓ 缺字段却写错类型才是 ✓
	for (const [k, t] of Object.entries(CONTRACT)) {
		if (!(k in row)) continue;   // ✓ 缺字段 = 数据确实没有 ✓ 合法 ✓
		assert.equal(typeof row[k], t, "类型不符：" + k + " 应为 " + t + " 实为 " + typeof row[k]);
	}
	// id 与 live 是**结构必需** ✓（C# 靠它们标识与判运行态 ✓）
	assert.ok("id" in row, "id 必须有 ✓");
	assert.ok("live" in row, "live 必须有 ✓（哪怕是 false ✓）");
});
await check("**时间戳是 ISO 字符串** ✓（不是数字 —— 数字会被 C# 丢成空串 ✗）", () => {
	assert.equal(row.createdAt, new Date(1788517824758).toISOString());
	assert.equal(row.lastPromptAt, new Date(1788517999999).toISOString());
	assert.ok(!Number.isFinite(row.createdAt), "不能是数字 ✓");
});
await check("数值字段的值正确", () => {
	assert.equal(row.turns, 2);
	assert.equal(row.steps, 9);
	assert.equal(row.outputTokens, 50);
	assert.equal(row.contextWindow, 1000);
});
await check("**缺字段就省略** ✓（不写 0 —— C# 用「存在性」判 Has* ✓ 写 0 会假装有数据 ✗）", () => {
	const thin = buildSnapshot([{ id: "s3", live: false, values: {} }], "t").sessions[0];
	assert.ok(!("turns" in thin), "turns 不该出现 ✗");
	assert.ok(!("createdAt" in thin), "createdAt 不该出现 ✗");
	assert.ok(!("outputTokens" in thin), "outputTokens 不该出现 ✗");
	assert.equal(thin.live, false);
});
await check("兼容磁盘投影的 {ver,seq,val} 形状", () => {
	const s = buildSnapshot([{ id: "s2", live: false, values: { sessionStats: { ver: 1, seq: 2, val: { turns: 5 } }, tokenUsage: { val: { totals: { outputTokens: 7 } } } } }], "t").sessions[0];
	assert.equal(s.turns, 5);
	assert.equal(s.outputTokens, 7);
});
await check("null/空列表 → 空 sessions", () => {
	assert.equal(buildSnapshot(null, "t").sessions.length, 0);
	assert.equal(buildSnapshot([null, undefined], "t").sessions.length, 0);
});

// ---- defaultOutFile：目录名必须与 C# 的 SnapshotPath 一致 ✓✓ ----
await check("**快照目录名与 C# 侧一致** ✓（曾经 CLI 读 toolkit-bridge 而这里写 shio-bridge ✗✗）", () => {
	const p = defaultOutFile({ DSH_HOME: path.join("X:", "iso", "home") });
	assert.equal(p, path.join("X:", "iso", "home", "shio-bridge", "sessions.json"));
	assert.ok(p.includes("shio-bridge"), "必须是 shio-bridge ✓（插件的 cordis id 也是它 ✓）");
	assert.ok(!p.includes("toolkit-bridge"), "不能是 toolkit-bridge ✗");
});
await check("defaultOutFile：DSH_HOME 为空 → 退回主目录 .dsh", () => {
	assert.ok(defaultOutFile({ DSH_HOME: "   " }).endsWith(path.join(".dsh", "shio-bridge", "sessions.json")));
});

// ---- 原子写 ----
await check("writeSnapshot 写文件且不留 .tmp", () => {
	const dir = fs.mkdtempSync(path.join(os.tmpdir(), "bridge-test-"));
	const file = path.join(dir, "sub", "sessions.json");
	writeSnapshot(file, built);
	assert.equal(JSON.parse(fs.readFileSync(file, "utf8")).formatVersion, SNAPSHOT_FORMAT_VERSION);
	assert.equal(fs.readdirSync(path.dirname(file)).filter((n) => n.includes(".tmp-")).length, 0);
	fs.rmSync(dir, { recursive: true, force: true });
});

// ---- P5 FIX (plugin audit MAJOR): the old mock accepted ANY argument, so the suite could not
// notice that a real projection registry needs a live Session (it calls session.snapshotEvents()
// and reads session.seq). It now refuses anything that is not a live Session, which is what the
// P1 fix relies on. ----
// A live Session looks like { header, id, surface, seq, snapshotEvents() } - the registry's own
// code calls those methods, so a record cannot stand in for one.
function makeLiveSession(id, cwd, createdAt) {
	return {
		header: { id: id, cwd: cwd, createdAt: createdAt },
		id: id,
		surface: "web",
		seq: 7,
		snapshotEvents: function () { return []; }
	};
}
// A SessionRecord, as sessionQuery.listSessions() returns it: { header, live, persisted }.
function makeRecord(id, cwd, createdAt) {
	return { header: { id: id, cwd: cwd, createdAt: createdAt }, live: true, persisted: true };
}
function projectionStub(values) {
	return {
		snapshot: async function (s) {
			if (!s || typeof s.snapshotEvents !== "function") {
				throw new TypeError("session.snapshotEvents is not a function");
			}
			return { values: values };
		}
	};
}
await check("**collectSessions：异步 listSessions + 真实 SessionRecord 形状** ✓✓（F2/F2b）", async () => {
	const list = await collectSessions(realRecordCtx());
	assert.equal(list.length, 1, "异步路径必须收集到 1 条 ✗（旧版在这里静默失败 ✓）");
	assert.equal(list[0].id, "sess-1");
	assert.equal(list[0].live, true);
	assert.equal(list[0].header.cwd, "D:\\work");
	assert.equal(list[0].values.sessionStats.turns, 2);
});
await check("**collectSessions：store 路径（Session 没有 live 字段）→ live 视为 true** ✓（F4）", async () => {
	const sess = { header: { id: "sess-2", cwd: "/x", createdAt: 1789000000000 }, id: "sess-2", surface: "web", seq: 5 };
	const list = await collectSessions({ sessions: { list: () => [sess] }, sessionProjections: { snapshot: () => ({ values: {} }) } });
	assert.equal(list.length, 1);
	assert.equal(list[0].live, true, "store 里的会话按定义都是活的 ✓（报 false 会让面板显示「已结束」✗）");
});
await check("collectSessions：服务缺失/抛异常 → 空数组（不抛）", async () => {
	assert.equal((await collectSessions({})).length, 0);
	assert.equal((await collectSessions({ sessionQuery: { listSessions: async () => { throw new Error("boom"); } } })).length, 0);
	assert.equal((await collectSessions({ sessionQuery: { listSessions: async () => [{ header: { id: "" } }] } })).length, 0);
	assert.equal((await collectSessions({ sessionQuery: { listSessions: () => Promise.reject(new Error("rej")) } })).length, 0);
});

// ---- N6 契约（插件复审：这条修复**没有测试覆盖** ✗ → 现在有了 ✓✓）----
// 为什么重要：工具箱只要快照**非空**就完全不用磁盘投影（Program.cs）。
// 所以"发一行空值"会把历史会话**好好的磁盘统计覆盖成 0** ✗✗ —— 装了插件反而更糟 ✓。
await check("**N6：纯持久化会话（没有投影值）必须不发空行** ✓✓", async () => {
	const rec = { header: { id: "persisted-only", cwd: "/x", createdAt: "2026-09-30T00:00:00Z" }, live: false, persisted: true };
	const list = await collectSessions({
		sessionQuery: { listSessions: async () => [rec] },
		sessions: { list: async () => [] }, // 不在 store 里 → 没有投影单元 → values 为空 ✓
		sessionProjections: { snapshot: async () => ({ values: {} }) }
	});
	assert.equal(list.length, 0, "空值 + 非活跃 → 不能发行（否则遮蔽磁盘投影 ✗✗）");
});

await check("**N6：活跃会话即使没有投影值也必须发行** ✓✓", async () => {
	const rec = { header: { id: "live-1", cwd: "/x", createdAt: "2026-09-30T00:00:00Z" }, live: true, persisted: true };
	const sess = { header: { id: "live-1" }, snapshotEvents: function () { return []; } };
	const list = await collectSessions({
		sessionQuery: { listSessions: async () => [rec] },
		sessions: { list: async () => [sess] },
		sessionProjections: { snapshot: async () => ({ values: {} }) }
	});
	assert.equal(list.length, 1, "live:true 是磁盘投影拿不到的事实 → 必须发行 ✓");
	assert.equal(list[0].live, true);
	assert.equal(list[0].id, "live-1");
});

await check("**N6：有投影值的会话照常发行（值原样带上）** ✓✓", async () => {
	const rec = { header: { id: "p2", cwd: "/x", createdAt: "2026-09-30T00:00:00Z" }, live: false, persisted: true };
	const sess = { header: { id: "p2" }, snapshotEvents: function () { return []; } };
	const list = await collectSessions({
		sessionQuery: { listSessions: async () => [rec] },
		sessions: { list: async () => [sess] },
		sessionProjections: { snapshot: async () => ({ values: { sessionStats: { turns: 3 } } }) }
	});
	assert.equal(list.length, 1);
	assert.equal(list[0].values.sessionStats.turns, 3);
});

// ---- index.js 的 cordis 声明（旧测试完全没覆盖 ✗）----
await check("**index.js 的 cordis 声明** ✓（name 与 patch 的 id 一致 ✓ inject 是真实服务 ✓）", () => {
	assert.equal(pluginName, "shio-bridge");
	assert.deepEqual(pluginInject, ["sessions", "sessionQuery", "sessionProjections"]);
});

// ---- ★★★ 回归护栏：inject 必须覆盖 snapshot.js 真正用到的每个 ctx 服务 ----
// 起因（2026-10-01 真机 dsh 实测抓到）：`inject` 里漏了 `sessionQuery` ✗
//   → cordis 不给未声明的服务 → `ctx.sessionQuery` 是 undefined ✗
//   → `collectSessions` 内部 catch 吞掉 → 返回空 → `tick()` 直接 return → **永不写快照** ✗✗
//   → 而**单元测试天然测不出来** ✗（它自己喂 ctx，所以"少声明一个依赖"永远是绿的 ✓）
//   → 真机表现：插件装上了、`--dump-config` 里配置也注入了 ✓ **但什么都不发生** ✓
// 这条测试把"声明与使用必须一致"变成一次就报出来的失败 ✓✓
await check("**inject 覆盖 snapshot.js 用到的每个 ctx 服务** ✓✓（真机 bug 的护栏）", () => {
	const src = fs.readFileSync(new URL("../snapshot.js", import.meta.url), "utf8");
	const used = new Set();
	for (const m of src.matchAll(/ctx\.([A-Za-z_][A-Za-z0-9_]*)/g)) used.add(m[1]);
	used.delete("on");   // cordis 的通用事件接口 ✓ 不是服务 ✓ 不用声明 ✓
	const missing = [...used].filter((u) => !pluginInject.includes(u));
	assert.deepEqual(missing, [],
		"这些 ctx 服务没写进 inject → 真 dsh 里会是 undefined → 插件静默什么都不做 ✗: " + missing.join(", "));
});

// ---- apply ----
await check("apply：首帧写快照（**异步** ✓）；enabled:false 不写", async () => {
	const dir = fs.mkdtempSync(path.join(os.tmpdir(), "bridge-apply-"));
	const file = path.join(dir, "sessions.json");
	const ctx = realRecordCtx();
	ctx.on = () => {};
	apply(ctx, { outFile: file, intervalMs: 100000 });
	await new Promise((r) => setTimeout(r, 400));   // ✓ 首帧现在是异步的 ✓ 等一下 ✓
	assert.ok(fs.existsSync(file), "首帧应已写出快照");
	assert.equal(JSON.parse(fs.readFileSync(file, "utf8")).sessions.length, 1);
	const off = path.join(dir, "off.json");
	apply(ctx, { outFile: off, enabled: false, intervalMs: 100000 });
	assert.equal(fs.existsSync(off), false, "enabled:false 不应写文件");
	fs.rmSync(dir, { recursive: true, force: true });
});
await check("**apply：非字符串 outFile 不抛** ✓（F6：加载期崩会拖垮 dsh ✗）", () => {
	apply({ sessions: { list: () => [] } }, { enabled: true, outFile: 123, intervalMs: 100000 });
});
await check("**P3: dispose 在 tick 进行中时，不会再写回 live:true** ✓✓", async () => {
	const dir = fs.mkdtempSync(path.join(os.tmpdir(), "bridge-p3-"));
	const file = path.join(dir, "sessions.json");
	let disposeFn = null;
	let resolveList;
	const gate = new Promise((r) => { resolveList = r; });
	const rec = makeRecord("sess-p3", "/w", 1789000000000);
	const live = makeLiveSession("sess-p3", "/w", 1789000000000);
	const ctx = {
		sessionQuery: { listSessions: () => gate.then(() => [rec]) },   // resolves only when we say so
		sessions: { list: async () => [live] },
		sessionProjections: projectionStub({ sessionStats: { turns: 1 } }),
		on: (ev, fn) => { if (ev === "dispose") disposeFn = fn; }
	};
	apply(ctx, { outFile: file, intervalMs: 100000 });
	await new Promise((r) => setTimeout(r, 50));   // the first tick is now awaiting listSessions
	disposeFn();                                    // dispose clears the live flags
	resolveList();                                  // ... and only now does the tick resume
	await new Promise((r) => setTimeout(r, 300));
	const txt = fs.existsSync(file) ? fs.readFileSync(file, "utf8") : "";
	assert.ok(txt.indexOf('"live":true') < 0, "an in-flight tick must not write live:true after dispose");
	fs.rmSync(dir, { recursive: true, force: true });
});

await check("**dispose 时清掉 live 标记** ✓（F8：否则 dsh 退出后面板永远显示运行中 ✗）", async () => {
	const dir = fs.mkdtempSync(path.join(os.tmpdir(), "bridge-dispose-"));
	const file = path.join(dir, "sessions.json");
	let disposeFn = null;
	const ctx = realRecordCtx();
	ctx.on = (ev, fn) => { if (ev === "dispose") disposeFn = fn; };
	apply(ctx, { outFile: file, intervalMs: 100000 });
	await new Promise((r) => setTimeout(r, 400));
	assert.equal(JSON.parse(fs.readFileSync(file, "utf8")).sessions[0].live, true);
	assert.ok(disposeFn, "应注册 dispose 钩子");
	disposeFn();
	const after = JSON.parse(fs.readFileSync(file, "utf8"));
	assert.equal(after.sessions[0].live, false, "dispose 后 live 应为 false ✓");
	assert.equal(after.sessions.length, 1, "其余数据应保留 ✓");
	fs.rmSync(dir, { recursive: true, force: true });
});

// ---- ⑩ 实时活动（2026-10-02 用户实测反馈"没有在运行，却显示 运行 6"的根治 ✓✓）----
await check("**markActivity：seq 差值 = 真在动**（首拍无基线 → 未知 ✗ 不假装 ✗）", async () => {
	const prevSeq = new Map();
	const prevActiveAt = new Map();
	const rows1 = markActivity([{ id: "a", live: true, seq: 5 }, { id: "b", live: true, seq: 9 }], prevSeq, prevActiveAt, "2026-10-02T00:00:00Z");
	assert.equal("active" in rows1[0], false, "首拍没有基线 → 不写字段（未知 ✓）");
	const rows2 = markActivity([{ id: "a", live: true, seq: 6 }, { id: "b", live: true, seq: 9 }], prevSeq, prevActiveAt, "2026-10-02T00:00:03Z");
	assert.equal(rows2[0].active, true, "seq 5→6 变了 → active=true ✓");
	assert.equal(rows2[1].active, false, "seq 9→9 没变 → active=false（明确知道没动 ✓ 不是未知 ✓）");
	assert.equal(rows2[0].lastActiveAt, "2026-10-02T00:00:03Z", "观测到活动就记时间 ✓");
	const rows3 = markActivity([{ id: "a", live: true, seq: 6 }], prevSeq, prevActiveAt, "2026-10-02T00:00:06Z");
	assert.equal(rows3[0].active, false, "之后安静 → active=false ✓");
	assert.equal(rows3[0].lastActiveAt, "2026-10-02T00:00:03Z", "lastActiveAt 粘性 ✓ 之后每拍都带 ✓");
});

await check("**buildSnapshot 透传活动三件（C# ParseSnapshot 按键取值 ✓）+ 格式版本不变（老工具箱兼容 ✓）", async () => {
	const snap = buildSnapshot([{ id: "a", live: true, active: true, lastActiveAt: "2026-10-02T00:00:03Z", seq: 7 }], "2026-10-02T00:00:03Z", 3000);
	const txt = JSON.stringify(snap);
	assert.ok(txt.indexOf('"active":true') >= 0, "active=true 必须写出 ✓");
	assert.ok(txt.indexOf('"lastActiveAt":"2026-10-02T00:00:03Z"') >= 0, "lastActiveAt 必须写出 ✓");
// 审查修复回归（3.0.4）：active=false 必须穿过 buildSnapshot 落盘 ✗
// 0.2.0 曾只有 ===true 才写 → false 被丢 → C# 永远等不到"明确没动" → 退回 15 分钟启发式 ✗✗
const snapF = buildSnapshot([{ id: "x", live: true, active: false, seq: 5 }], "2026-10-02T00:00:03Z", 3000);
assert.ok(JSON.stringify(snapF).indexOf('"active":false') >= 0, "active=false 必须写出 ✓（端到端：C# 侧三态的『明确没动』分支等着它）");
	assert.ok(txt.indexOf('"seq":7') >= 0, "seq 必须写出 ✓");
	assert.equal(SNAPSHOT_FORMAT_VERSION, 2, "版本仍是 2（可选字段追加 ✓ v1/v2 解析器都认 ✓）");
});

console.log("\n== " + pass + " passed, " + fail + " failed ==");
