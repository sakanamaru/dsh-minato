using System;
using System.Collections.Generic;
using System.Text;

namespace Dsht.Gui.Avalonia.Markers
{
    /// <summary>更新页（`update-center`）标记行的**解析**（2026-10-11 ✓ 纯函数 ✓ 可测 ✓ 不引用 Avalonia ✓）。
    ///
    /// ★★★ 为什么单独抽出来：**同一个 id 会有多行** —— 桥接插件**每个 profile 一行** ✓
    ///   （同一插件装在不同 profile、版本还可能不同 ✓ 都要看得见 ✓）。
    ///   ✗ 旧写法是"URL/NOTE/LOG 按**出现顺序**配给同 id 的第 1、2、3 行" ✗ ——
    ///     那是个**脆弱约定**：CLI 只要少发/多发一行，指引就串到别人身上（而且看起来还挺正常 ✗）。
    ///   ✓ 现在：**按 id 归组** ✓ —— URL/NOTE/LOG 是**这个 id 的共用信息**（与第几行无关 ✓）；
    ///     每行自己独有的东西只有一个 `next=`（该 profile 该状态下的修复指引 ✓ CLI 随该行发出 ✓）。
    ///   ★ CLI 侧对应改动：URL 只发一次、长 NOTE 只发一次（同一段说明原先重复 N 遍 ✗）、
    ///     每行多一个 `next=`（`NextOf` ✓ 空格写成 `%20` 以保持"一行一值" ✓ 见 CLI `EncodeNext` ✓）。
    ///
    /// 解析纪律（与其它 Markers 一致）：认不出的行**跳过** ✓ 字段缺了就留默认值 ✓ **不猜** ✗ 不抛 ✗。</summary>
    public static class UpdateCenterMarkers
    {
        /// <summary>一个 `UPDATECENTER_ITEM` 行（**一行 = 一个 profile 的一份实数据** ✓）。</summary>
        public sealed class Item
        {
            public string Id = "";
            public string Kind = "";
            public string Installed = "unknown";
            public string Latest = "unknown";
            public string State = "unknown";
            public string Profile = "";
            /// <summary>该行**独有**的下一步（CLI 的 `next=` ✓ 已解码 ✓ 老 CLI / 普通行 → 空串 ✓）。</summary>
            public string Next = "";
            /// <summary>该 id 的共用信息（**与本行是第几行无关** ✓ 见类注释）：多个不同值按出现序保留 ✓。</summary>
            public List<string> Urls = new List<string>();
            public List<string> Notes = new List<string>();
            public List<string> Logs = new List<string>();
        }

        /// <summary>`%20` → 空格（CLI `EncodeNext` 的逆 ✓ **只还原这一个字符** ✓）。
        /// 没有 `%20` → 原样返回 ✓；空 / `-` → 空串 ✓（CLI 用 `-` 表示"这一行没有额外指引" ✓ **不是**一句文案 ✗）。</summary>
        public static string DecodeNext(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "-") return "";
            return s.Replace("%20", " ");
        }

        /// <summary>该行是否有**可显示的**下一步（空 / `-` → 没有 ✓）。</summary>
        public static bool HasNext(string decoded)
        {
            return !string.IsNullOrEmpty(decoded) && decoded != "-";
        }

        // ================================================================ package.json 字段读取
        // ★ 2026-10-13：这一段原来长在这里 ✗ —— 但 CLI（`Program.Ops.cs`）也要用同一份判定 ✓
        //   而 **GUI 程序集不是 CLI 的依赖**（CLI 只引用 Domain / 两个 Platform ✓）→ CS0234 ✗。
        //   ✓ 已搬到 **Domain 层**：`v3/src/Dsht.Domain/Services/PackageJsonFields.cs`（纯逻辑 ✓ 零 IO ✓
        //     正是"领域逻辑可被任何平台与任何前端复用"的那一层 ✓）。
        //   ✗ 不在这里保留第二份 ✗（两份就会有两个答案 —— 这次修的 bug 就是这么来的 ✓）。

        /// <summary>按 `UPDATECENTER_` 前缀扫全部行 ✓ **按 id 归组** ✓ 返回的 Item 顺序 = ITEM 行出现序 ✓。</summary>
        public static List<Item> Parse(string raw)
        {
            List<Item> list = new List<Item>();
            if (string.IsNullOrEmpty(raw)) return list;
            string[] ls = raw.Replace("\r\n", "\n").Split('\n');
            Dictionary<string, Item> byId = new Dictionary<string, Item>(StringComparer.Ordinal);
            for (int i = 0; i < ls.Length; i++)
            {
                string t = ls[i] == null ? "" : ls[i].Trim();
                if (!t.StartsWith("UPDATECENTER_", StringComparison.Ordinal)) continue;
                string[] parts = t.Split(' ');
                // ★ 标记行 = `UPDATECENTER_<词> <id> <值…>` ✓ 少于 3 段 = **不是**一条标记行 ✓
                //   ★★ 而且 id 后面**必须还有内容** ✗ —— NOTE 是自由文本 ✓ 它换行后的某一行正文
                //   可能恰好以 `UPDATECENTER_ITEM ` 开头（真机实测 ✓ 会凭空多出一张卡 ✗）→ 只认
                //   `标记词<空格>id<空格>…` 这一种形状 ✓ 长度正好等于 `UPDATECENTER_ITEM <id>` 的那一行
                //   不是 ITEM 行 ✓（ITEM 行末尾永远跟着 ` kind=` 字段 ✓ 见 CLI ✓）。
                if (parts.Length < 3) continue;
                if (parts[1].Length == 0 || parts[2].Length == 0) continue;
                string tag = parts[0];
                string id = parts[1];
                // ★★ 真机实测：NOTE 长文换行后的一行正文可以是 `UPDATECENTER_ITEM webui`（只有标记词 + id ✓）
                //   → 它会被当成**第二个 webui 条目** ✗✗（截图上就是两张一模一样的卡 ✓）
                //   ✓ ITEM 行永远带 `kind=` 字段（CLI 侧字段名与 kind 逐字不变 ✓）→ 认不出字段 = 续行 ✓ 跳过 ✓
                //   ✗ 不丢真行 ✗：CLI 的每一条 ITEM 都带 kind= ✓（`verify_plugin_update_baseline.ps1` 也这么断言行 ✓）
                if (tag == "UPDATECENTER_ITEM" && t.IndexOf(" kind=", StringComparison.Ordinal) < 0) continue;
                if (tag == "UPDATECENTER_ITEM")
                {
                    Item it = new Item();
                    it.Id = id;
                    for (int k = 2; k < parts.Length; k++)
                    {
                        int eq = parts[k].IndexOf('=');
                        if (eq <= 0) continue;
                        string kk = parts[k].Substring(0, eq);
                        string vv = parts[k].Substring(eq + 1);
                        if (kk == "kind") it.Kind = vv;
                        else if (kk == "installed") it.Installed = vv;
                        else if (kk == "latest") it.Latest = vv;
                        else if (kk == "state") it.State = vv;
                        else if (kk == "profile") it.Profile = vv;
                        else if (kk == "next") it.Next = DecodeNext(vv);
                    }
                    list.Add(it);
                    if (!byId.ContainsKey(id)) byId[id] = it;   // 归组用**第一条**（只用来挂共用的 URL/NOTE/LOG ✓）
                    continue;
                }
                if (tag == "UPDATECENTER_URL" || tag == "UPDATECENTER_NOTE" || tag == "UPDATECENTER_LOG")
                {
                    // 值 = 标记词与 id 之后的**整段原文**（说明里本来就有空格 ✓ 不能按空格切 ✓）
                    string val = t.Substring(tag.Length + 1 + id.Length).Trim();
                    Item host;
                    if (!byId.TryGetValue(id, out host) || host == null) continue;   // 没有 ITEM 行 → 安全忽略 ✓
                    List<string> into = tag == "UPDATECENTER_URL" ? host.Urls : (tag == "UPDATECENTER_NOTE" ? host.Notes : host.Logs);
                    AddDistinct(into, val);
                    continue;
                }
                // UPDATECENTER_DIAG / _OK / _BEGIN / 以后新增的标记行：**不在这里处理** ✓
                // （DIAG 由 UpdateDiagnosis 从整段原文里取 ✓ 这里只解析 ITEM/URL/NOTE/LOG ✓）
            }
            SharePerId(list);
            return list;
        }

        /// <summary>把每个 id 的**共用信息**（URL/NOTE/LOG ✓）铺给该 id 的**每一行** ✓
        /// —— 于是"取某一行的 Urls[0] / Notes[0]"永远拿到的是**这个 id 的**那一份 ✓
        ///   ✗ 与"它排在第几行"无关 ✗（那正是旧的脆弱约定 ✗ 见类注释）。只在本组内共享 ✓ 不跨 id 串 ✗。</summary>
        private static void SharePerId(List<Item> list)
        {
            Dictionary<string, List<Item>> map = new Dictionary<string, List<Item>>(StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++)
            {
                List<Item> g;
                if (!map.TryGetValue(list[i].Id, out g)) { g = new List<Item>(); map[list[i].Id] = g; }
                g.Add(list[i]);
            }
            foreach (KeyValuePair<string, List<Item>> kv in map)
            {
                List<Item> g = kv.Value;
                if (g.Count < 2) continue;
                string url = UrlOf(g), note = NoteOf(g), log = LogOf(g);
                for (int i = 0; i < g.Count; i++)
                {
                    g[i].Urls = SplitLines(url);
                    g[i].Notes = SplitLines(note);
                    g[i].Logs = SplitLines(log);
                }
            }
        }

        /// <summary>把拼好的多行文本拆回列表 ✓（空 → 空表 ✓ 命令行只有一条时就是单元素 ✓）。</summary>
        private static List<string> SplitLines(string joined)
        {
            List<string> outp = new List<string>();
            if (string.IsNullOrEmpty(joined)) return outp;
            string[] ls = joined.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < ls.Length; i++) AddDistinct(outp, ls[i]);
            return outp;
        }

        /// <summary>把同一个 id 的各行合成**一张卡**要先分出来的组 ✓（顺序 = 组内首次出现的顺序 ✓ 可复现 ✓）。
        /// 只有一个成员的组也照常返回 ✓（调用方决定要不要走合并路径 ✓）。</summary>
        public static List<List<Item>> GroupById(List<Item> items)
        {
            List<List<Item>> groups = new List<List<Item>>();
            if (items == null) return groups;
            Dictionary<string, List<Item>> map = new Dictionary<string, List<Item>>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                Item it = items[i];
                List<Item> g;
                if (!map.TryGetValue(it.Id, out g)) { g = new List<Item>(); map[it.Id] = g; groups.Add(g); }
                g.Add(it);
            }
            return groups;
        }

        /// <summary>某个 id 的**共用 URL** ✓：取**第一个非空且不是 unknown** 的（CLI 现在只发一条 ✓
        /// 老 CLI 的多条也一样对 ✓ —— 同一个仓库地址 ✓ 后两条重复没意义 ✗）；都没有 → 空串 ✓ 不编 ✗。</summary>
        public static string UrlOf(List<Item> group)
        {
            if (group == null) return "";
            for (int i = 0; i < group.Count; i++)
            {
                List<string> u = group[i].Urls;
                for (int k = 0; k < u.Count; k++)
                {
                    string v = u[k] == null ? "" : u[k].Trim();
                    if (v.Length > 0 && v != "unknown") return v;
                }
            }
            return "";
        }

        /// <summary>某个 id 的**共用说明** ✓：所有**不同**的 NOTE 按出现序用换行拼起来 ✓。
        /// CLI 现在只发一条 → 原样返回 ✓；老 CLI 每个 profile 各发一条（内容可能不同）→ **一条都不丢** ✓
        /// （完全相同的重复会去重 ✓ 那正是这次要消掉的冗余 ✓）。没有 → 空串 ✓。</summary>
        public static string NoteOf(List<Item> group)
        {
            List<string> all = new List<string>();
            if (group == null) return "";
            for (int i = 0; i < group.Count; i++)
            {
                List<string> n = group[i].Notes;
                for (int k = 0; k < n.Count; k++) AddDistinct(all, n[k]);
            }
            return string.Join("\n", all.ToArray());
        }

        /// <summary>某个 id 的**共用的更新日志**（同上 ✓ 通常只有 webui 一条 ✓）。</summary>
        public static string LogOf(List<Item> group)
        {
            List<string> all = new List<string>();
            if (group == null) return "";
            for (int i = 0; i < group.Count; i++)
            {
                List<string> n = group[i].Logs;
                for (int k = 0; k < n.Count; k++) AddDistinct(all, n[k]);
            }
            return string.Join("\n", all.ToArray());
        }

        // ================================================================ 每个 profile 一行（★ 2026-10-11 起 = 一张小表的行）

        /// <summary>★★ 2026-10-11（维护者实测「读起来费劲 ✗」）：合并卡的每一行**不再是一句长话** ✗，
        ///   而是一张**固定五列的小表** ✓：`profile ｜ 已装 ｜ 工具箱自带 ｜ 状态 ｜ 下一步` ✓。
        ///   为什么把列名做成常量、把行做成字符串数组：
        ///   · 列名与每格的值都是**纯函数**产出 → LogicTests 能直接断言"列语义" ✓（✗ 不是断言"某处 UI 像在循环" ✗）
        ///   · 渲染侧（`Shells.Pages.Operations.cs`）照这五列建 Grid ✓（✗ 不新造控件 ✗ 用现有 Grid ✓）
        ///   ★ 每格一律短 ✓：状态 ≤ 6 字 ✓、下一步 = 一条命令或 ≤20 字短句 ✓；
        ///   ★ 长路径与整段机制说明**已搬去「说明」页** ✓（✗ 一个字都没丢 ✗ 见 `Shells.cs` 的说明章节 ✓）。</summary>
        public static readonly string[] TableHeaders = new string[] { "profile", "已装", "工具箱自带", "状态", "下一步" };

        /// <summary>表格「已装」那一格 ✓（认不出 → 「未安装」✓ ✗ 不编一个版本号 ✗）。</summary>
        public static string CellInstalled(Item it)
        {
            if (it == null) return "";
            bool ok = !string.IsNullOrEmpty(it.Installed) && it.Installed != "unknown";
            return ok ? VersionMarkers.TagOf(it.Installed) : "未安装";
        }

        /// <summary>表格「工具箱自带」那一格 ✓（基准 = 你这份工具箱自带的那一份 ✓ 不是网上的最新版 ✓）。
        /// 基准读不到（CLI 报 `latest=unknown`）→ 「未知」✓（✗ 不编一个版本号 ✗；原因在诊断行 ✓）。</summary>
        public static string CellBundled(Item it)
        {
            if (it == null) return "";
            bool ok = !string.IsNullOrEmpty(it.Latest) && it.Latest != "unknown";
            return ok ? VersionMarkers.TagOf(it.Latest) : "未知";
        }

        /// <summary>表格「状态」那一格 ✓（短标签 ≤ 6 字 ✓ 语义与 `UpdateDiagnosis.StateText` 同一套 ✓）。</summary>
        public static string CellState(Item it)
        {
            if (it == null) return "";
            return UpdateDiagnosis.ShortState(it.State);
        }

        /// <summary>表格「下一步」那一格 ✓ —— **该行独有** ✓（CLI 的 `next=` ✓ 已解码 ✓）。
        /// ★ 短：CLI 现在给的是一条命令（web ✓）或一句 ≤20 字的短句（desktop / 未安装 / 半旧 ✓）；
        ///   ★ **老 CLI 没有 `next=`** → 如实写一句短的兜底 ✓（✗ 不留空让人以为"没有下一步" ✗
        ///     也✗ 不把别人的指引借过来 ✗）。</summary>
        public static string CellNext(Item it)
        {
            if (it == null) return "";
            string n = DecodeNext(it.Next);
            if (HasNext(n)) return n;
            if (it.State == "content-mismatch") return "内容哈希不一致";
            if (it.State == "not-installed") return "未安装";
            if (it.State == "unknown") return "见诊断行";
            return "在对应客户端插件机制里更新";
        }

        /// <summary>一张表的**一行**（五格 ✓ 顺序 = `TableHeaders` ✓）。</summary>
        public static string[] TableRow(Item it)
        {
            return new string[] { ProfileOf(it), CellInstalled(it), CellBundled(it), CellState(it), CellNext(it) };
        }

        /// <summary>合并卡的**正文 = 整张表** ✓：`表头` ＋ 每个 profile 一行 ✓（每行五格用 `\t` 分隔 ✓）。
        /// ★ 这就是"同一插件三行都要渲染出来、列语义不乱"的那段文本本身 ✓（LogicTests 直接断言它 ✓）。</summary>
        public static string RowsText(List<Item> group)
        {
            if (group == null || group.Count == 0) return "";
            StringBuilder sb = new StringBuilder();
            sb.Append(string.Join("\t", TableHeaders));
            for (int i = 0; i < group.Count; i++)
            {
                sb.Append('\n');
                sb.Append(string.Join("\t", TableRow(group[i])));
            }
            return sb.ToString();
        }

        /// <summary>某一行"已装 → 基准"的**版本段** ✓（基准 = 工具箱自带那一份 ✓ 不是网上的最新版 ✓）。
        /// ⚠ 2026-10-11（表格化）：**合并卡已经不用它了** ✗（合并卡走五列表格 ✓ 短格 ✓）；
        ///   它还留着是因为**单行的插件卡**（只有一个 profile 的插件 ✓ 走 `RenderUpdCard` ✓）
        ///   与 LogicTests 仍按它给出完整的版本句 ✓ —— **口径与措辞一字未改** ✓。
        /// ★ 2026-10-13：**勘误** —— 单行插件卡其实走的是 `UpdateDiagnosis.VersionText` ✓，本方法当时没有活的
        ///   调用方 ✗；它与 `VersionText` 的插件分支**逐字相同**（两处各写一遍 = 漂移风险 ✗）。
        ///   ✓ 现在**委托**给那一个函数 ✓✓（"离线路径复用同一取数函数" ✓ 只剩一处措辞 ✗ 不许再各写一遍 ✗）。</summary>
        public static string RowVersionText(string installed, string latest, string state)
        {
            return UpdateDiagnosis.VersionText("plugin", state, latest, installed);
        }

        /// <summary>某一行**显示的整句** ✓（版本段 · 状态 · 该行独有的下一步 ✓ 三样都要看得见 ✓）。
        /// ⚠ 2026-10-11（表格化）：**合并卡已经不用它了** ✗（合并卡走五列表格 ✓）；
        ///   单行的插件卡仍用它（那里有整行宽度 ✓ 长句读得下 ✓）。</summary>
        public static string RowText(Item it)
        {
            if (it == null) return "";
            StringBuilder sb = new StringBuilder();
            sb.Append(RowVersionText(it.Installed, it.Latest, it.State));
            string st = UpdateDiagnosis.StateText(it.Kind, it.State, it.Latest, it.Installed);
            if (!string.IsNullOrEmpty(st)) sb.Append("　·　").Append(st);
            string next = DecodeNext(it.Next);
            if (HasNext(next)) sb.Append("　　").Append(next);
            return sb.ToString();
        }

        /// <summary>卡头右侧的小汇总 ✓（例：`有更新 1 · 已是最新 1 · 未安装 1` ✓）。
        /// 语法：只列**这个 id 真实出现过的**状态 ✓；同一个状态的几行**合成一个计数** ✓（✗ 不每行报一次 ✗）；
        /// 顺序 = 固定优先级（最需要你看一眼的在前 ✓ 与 `OverallState` 同一套优先级 ✓ 可复现 ✓）；
        /// 一个状态都没有（理论上不会 ✓）→ 空串 ✓（✗ 不编一个 ✗）。</summary>
        public static string SummaryText(List<Item> group)
        {
            if (group == null || group.Count == 0) return "";
            string[] order = new string[] { "unknown", "content-mismatch", "update-available", "newer-than-bundled", "not-installed", "up-to-date" };
            List<string> parts = new List<string>();
            for (int k = 0; k < order.Length; k++)
            {
                int n = 0;
                for (int i = 0; i < group.Count; i++) if (group[i].State == order[k]) n++;
                if (n > 0) parts.Add(UpdateDiagnosis.ShortState(order[k]) + " " + n);
            }
            // GUI 不认识的状态值（CLI 以后新增的 ✓）也要出现在汇总里 ✓（✗ 不吞 ✗ 排在最后 ✓）
            for (int i = 0; i < group.Count; i++)
            {
                bool known = false;
                for (int k = 0; k < order.Length; k++) if (group[i].State == order[k]) { known = true; break; }
                if (known) continue;
                string label = UpdateDiagnosis.ShortState(group[i].State);
                int n = 0;
                for (int j = 0; j < group.Count; j++) if (group[j].State == group[i].State) n++;
                bool dup = false;
                for (int p = 0; p < parts.Count; p++) if (parts[p].StartsWith(label + " ", StringComparison.Ordinal)) { dup = true; break; }
                if (!dup) parts.Add(label + " " + n);
            }
            return string.Join(" · ", parts.ToArray());
        }

        /// <summary>这一行的 profile 名 ✓（认不出 → 如实写出来 ✓ 不编一个名字 ✗）。</summary>
        public static string ProfileOf(Item it)
        {
            if (it == null || string.IsNullOrEmpty(it.Profile)) return "（未报 profile）";
            return it.Profile;
        }

        /// <summary>合并卡的整体状态 ✓：取**最需要你看一眼**的那一行的 state ✓（逐行状态在正文里都有 ✓）。
        /// 排序（前者优先）：unknown（基准不可用 → 诊断行要贴出来 ✓）&gt; content-mismatch（半旧 ✓ 只看版本号抓不住 ✓）
        /// &gt; update-available &gt; newer-than-bundled &gt; not-installed &gt; up-to-date ✓。认不出的 state → 排在最后 ✓ 不吞 ✗。</summary>
        public static string OverallState(List<Item> group)
        {
            if (group == null || group.Count == 0) return "unknown";
            string[] known = new string[] { "unknown", "content-mismatch", "update-available", "newer-than-bundled", "not-installed", "up-to-date" };
            for (int k = 0; k < known.Length; k++)
            {
                for (int i = 0; i < group.Count; i++) if (group[i].State == known[k]) return known[k];
            }
            // 一个**已知**状态都没有 → 全是 GUI 不认识的值 ✓ 原样贴第一个 ✓（✗ 不吞成「未知」✗）
            return group[0].State;
        }

        private static void AddDistinct(List<string> list, string v)
        {
            if (v == null) return;
            string s = v.Trim();
            if (s.Length == 0) return;
            for (int i = 0; i < list.Count; i++) if (string.Equals(list[i], s, StringComparison.Ordinal)) return;
            list.Add(s);
        }
    }
}
