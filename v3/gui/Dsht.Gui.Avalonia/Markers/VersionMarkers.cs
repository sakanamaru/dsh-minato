using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dsht.Gui.Avalonia.Markers
{
    /// <summary>版本显示（2026-10-11）· 纯函数 + 只读文件探测 ✓ **不引用核心程序集** ✓ → LogicTests 直接吃 ✓。
    /// 纪律（逐字照做 ✓）：版本号只能来自**实读** —— CLI 的 `DSHT_VERSION` 行 ✓ 或插件 `package.json` 的 `version` ✓。
    /// ✗ 一个都不许写死在 GUI 里 ✗；读不到就**如实说读不到** ✓ 不编版本号 ✗。</summary>
    public static class VersionMarkers
    {
        /// <summary>CLI `version` 命令的唯一输出行 `DSHT_VERSION <x.y.z>` ✓（Program.cs:82 ✓）。
        /// 读不到 / 不是这一行 → null ✓ **不猜** ✗（v2.x 线的 `version` 是「未知参数」→ 这里同样 null ✓）。</summary>
        public static string ParseCliVersion(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string[] ls = raw.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < ls.Length; i++)
            {
                string t = ls[i] == null ? "" : ls[i].Trim();
                if (!t.StartsWith("DSHT_VERSION ", StringComparison.Ordinal)) continue;
                string v = t.Substring("DSHT_VERSION ".Length).Trim();
                if (v.Length > 0 && v.IndexOf(' ') < 0) return v;   // 多词 = 不是干净的版本号 → 不认 ✓
            }
            return null;
        }

        /// <summary>显示用的 tag ✓：`3.0.7` → `v3.0.7` ✓（CLI 的标记行里是**归一化**的 `3.0.7` ✓ 显示才加 v ✓）。
        /// **只加前缀** ✓ —— 不补零、不切段、不排序、不比较 ✓（版本比较是 CLI 的事 ✓ GUI 只显示 ✗不重算 ✓）；
        /// 已经带 v 的原样返回 ✓；空串原样返回 ✓（✗ 不凭空造一个 v ✗）。</summary>
        public static string TagOf(string v)
        {
            if (string.IsNullOrEmpty(v)) return v;
            return (v[0] == 'v' || v[0] == 'V') ? v : ("v" + v);
        }

        /// <summary>一个实读到的桥接插件 package.json。</summary>
        public sealed class BridgeHit
        {
            public string Path = "";
            public string Profile = "";    // desktop / web / 空（认不出位置）
            public string Version = "";
        }

        /// <summary>桥接插件 `package.json` 的候选路径（**按序** ✓ 找到第一个即可 ✓；返回全部候选是为了
        /// 让"同一个包装在多个 profile、版本还不一样"也**都看得见** ✓ —— 那正是最该说清的情况 ✓）。
        /// 根与 CLI 的 `IPaths.DataRoot` **同口径** ✓（WindowsPaths.DataRoot ✓ 逐条对齐）：
        /// `DSH_HOME` 优先 ✓（设了就**只用它** ✗ 不回落真实主目录 —— 隔离测试下读错人 ✓✓）；
        /// 没设才按 `~\.dsh` → `%APPDATA%\.dsh` → `%LOCALAPPDATA%\.dsh` 取**第一个存在的** ✓。</summary>
        public static List<string> BridgeCandidates(string dshHome)
        {
            List<string> roots = BridgeRoots(dshHome);
            string[] profs = new string[] { "desktop", "web" };
            List<string> list = new List<string>();
            for (int r = 0; r < roots.Count; r++)
            {
                // ① 已装进 profile 的 node_modules ✓（真机就是这个位置 ✓）—— 按序：desktop → web ✓
                for (int i = 0; i < profs.Length; i++)
                    AddUnique(list, Path.Combine(roots[r], "profiles", profs[i], "node_modules", "dsh-minato-bridge", "package.json"));
                // ② 该包**自带**的子目录（本仓库 `plugin/dsh-minato-bridge` 的布局 ✓ 真机上 desktop profile 里确实有这一层 ✓）
                for (int i = 0; i < profs.Length; i++)
                    AddUnique(list, Path.Combine(roots[r], "profiles", profs[i], "node_modules", "dsh-minato-bridge", "plugin", "dsh-minato-bridge", "package.json"));
                // ③ 数据根下的 plugin\（"装到数据根"的那种布局 ✓）
                AddUnique(list, Path.Combine(roots[r], "plugin", "dsh-minato-bridge", "package.json"));
            }
            return list;
        }

        /// <summary>数据根候选（与 CLI 的 WindowsPaths.DataRoot 同一套判定 ✓）。传了就用传的 ✓。</summary>
        public static List<string> BridgeRoots(string dshHome)
        {
            List<string> roots = new List<string>();
            if (!string.IsNullOrEmpty(dshHome)) { roots.Add(dshHome); return roots; }
            try
            {
                string env = Environment.GetEnvironmentVariable("DSH_HOME");
                if (!string.IsNullOrWhiteSpace(env)) { roots.Add(env.Trim()); return roots; }   // ★ 设了就**只用它** ✗ 不回落 ✗
            }
            catch { }
            List<string> cands = new List<string>();
            try { cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh")); } catch { }
            try { cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".dsh")); } catch { }
            try { cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ".dsh")); } catch { }
            for (int i = 0; i < cands.Count; i++)
            {
                try { if (Directory.Exists(cands[i])) { roots.Add(cands[i]); return roots; } } catch { }
            }
            if (cands.Count > 0) roots.Add(cands[0]);
            return roots;
        }

        private static void AddUnique(List<string> list, string p)
        {
            if (string.IsNullOrEmpty(p)) return;
            for (int i = 0; i < list.Count; i++) if (string.Equals(list[i], p, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(p);
        }

        /// <summary>逐个试候选 ✓ **读得到 `version` 字段的才算「装了」** ✓
        /// （文件在但读不出 / 没有 version 字段 → 不算 ✗ 不编 ✓）。</summary>
        public static List<BridgeHit> ScanBridge(List<string> candidates)
        {
            List<BridgeHit> hits = new List<BridgeHit>();
            if (candidates == null) return hits;
            for (int i = 0; i < candidates.Count; i++)
            {
                string p = candidates[i];
                try
                {
                    if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;
                    string txt = File.ReadAllText(p);
                    string v = JsonStringField(txt, "version");
                    if (string.IsNullOrEmpty(v)) continue;
                    bool dup = false;
                    for (int k = 0; k < hits.Count; k++) if (string.Equals(hits[k].Path, p, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                    if (dup) continue;
                    hits.Add(new BridgeHit { Path = p, Profile = ProfileOf(p), Version = v });
                }
                catch { }   // 读不了就跳过 ✓ 不因此崩 ✓ 也不编 ✓
            }
            return hits;
        }

        /// <summary>从路径认 profile 名（`…\profiles\<name>\node_modules\…` ✓）。认不出 → 空串 ✓ 不猜 ✗。</summary>
        public static string ProfileOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string[] parts = path.Replace('/', '\\').Split('\\');
            for (int i = 0; i + 2 < parts.Length; i++)
            {
                if (!string.Equals(parts[i], "profiles", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(parts[i + 2], "node_modules", StringComparison.OrdinalIgnoreCase)) continue;
                if (parts[i + 1].Length > 0) return parts[i + 1];
            }
            return "";
        }

        /// <summary>极小的 JSON 字符串字段读取器 ✓（只为读 package.json 的 version ✓ 不是通用解析器 ✗）。
        /// 键名必须**整词**匹配（前一个非空白字符是 `{` 或 `,` ✓）→ `dshVersion` 不会被误当 `version` ✓。
        /// 找不到 / 格式不对 → 空串 ✓ 不猜 ✗。</summary>
        public static string JsonStringField(string json, string name)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(name)) return "";
            string key = "\"" + name + "\"";
            int from = 0;
            while (true)
            {
                int at = json.IndexOf(key, from, StringComparison.Ordinal);
                if (at < 0) return "";
                from = at + key.Length;
                int j = at - 1;
                while (j >= 0 && (json[j] == ' ' || json[j] == '\t' || json[j] == '\r' || json[j] == '\n')) j--;
                if (j >= 0 && json[j] != '{' && json[j] != ',') continue;   // 不是独立的键 → 继续找 ✓
                int c = from;
                while (c < json.Length && (json[c] == ' ' || json[c] == '\t' || json[c] == '\r' || json[c] == '\n')) c++;
                if (c >= json.Length || json[c] != ':') continue;
                c++;
                while (c < json.Length && (json[c] == ' ' || json[c] == '\t' || json[c] == '\r' || json[c] == '\n')) c++;
                if (c >= json.Length || json[c] != '"') continue;
                int end = json.IndexOf('"', c + 1);
                if (end < 0) return "";
                return json.Substring(c + 1, end - c - 1);
            }
        }

        /// <summary>页脚那一句：多个 profile 分别报 ✓ 同版本合并 ✓ 一个都没实读到 → 「未安装」✓（✗ 绝不编版本号 ✗）。</summary>
        public static string DescribeBridge(List<BridgeHit> hits)
        {
            if (hits == null || hits.Count == 0) return "未安装";
            List<string> vers = new List<string>();
            List<List<string>> who = new List<List<string>>();
            for (int i = 0; i < hits.Count; i++)
            {
                int at = -1;
                for (int k = 0; k < vers.Count; k++) if (vers[k] == hits[i].Version) { at = k; break; }
                if (at < 0) { vers.Add(hits[i].Version); who.Add(new List<string>()); at = vers.Count - 1; }
                string tag = hits[i].Profile.Length > 0 ? hits[i].Profile : "";
                if (tag.Length > 0 && !who[at].Contains(tag)) who[at].Add(tag);
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < vers.Count; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(vers[i]);
                if (who[i].Count > 0) sb.Append("（").Append(string.Join("、", who[i].ToArray())).Append("）");
            }
            return sb.ToString();
        }
    }

    /// <summary>更新页的状态诊断（2026-10-11 ✓ 纯函数 ✓ 可测 ✓）。
    /// 铁律：✗ 不许把"取不到最新版本"笼统写成「未知」✗；✗ 不许编一个"最新版"✗。
    /// 四种情况分别说清 ✓：① 拿到了 → 版本号 + 是否有新版 ✓ ② 真取不到（离线/被白名单拒）→ 明说 ✓
    /// ③ CLI 本来就不查这一项（minato/插件）→ 明说"CLI 不提供" ✓ ④ 老 CLI 没有这些标记行 → 页面级文案说"版本过旧" ✓。</summary>
    public static class UpdateDiagnosis
    {
        /// <summary>状态徽章（短 ✓）。CLI 给的四态照译 ✓；`state=unknown` 按**已知事实**分情况 ✓ 不写「未知」✗。
        /// ★ 2026-10-11：插件行（**桥接插件** ✓）多了三态 ✓ —— CLI 侧基准 = **随包那一份**（工具箱自带 ✓ 离线 ✓ 零新渠道 ✓）
        ///   → `not-installed`（该 profile 没装）/ `content-mismatch`（**版本相同但内容哈希不同** = 半旧 ✓）/
        ///     `newer-than-bundled`（比随包还新 ✓ 如实说 ✓ ✗ 不硬说成「有更新」✗）。见 CLI `Program.Ops.cs` 的 BridgeStateOf ✓</summary>
        public static string StateText(string kind, string state, string latest, string installed)
        {
            if (state == "up-to-date") return "已是最新";
            if (state == "update-available") return "有更新";
            if (state == "newer-than-latest") return "比最新还新";
            if (state == "external") return "在官方页更新";
            // ★ 插件（桥接）专属三态 ✓（逐态都在 UI 上可读懂 ✓ 见 LogicTests 的断言 ✓）
            if (state == "not-installed") return "未安装";
            if (state == "content-mismatch") return "内容与随包不一致";
            if (state == "newer-than-bundled") return "比随包还新";
            // CLI 报了个 GUI **不认识**的状态值 → 把原值贴出来 ✓（✗ 不许硬塞进"取不到" ✗ 也不许吞成「未知」✗）
            if (!string.IsNullOrEmpty(state) && state != "unknown")
                return "未识别（kind=" + (string.IsNullOrEmpty(kind) ? "空" : kind) + " state=" + state + "）";
            // —— 以下是 CLI 报 `state=unknown` / 没给状态的情形：按**已知事实**分情况 ✓ ——
            if (kind == "webui") return "取不到最新版";
            if (kind == "minato") return "CLI 不查最新版";
            // ★ 插件行 state=unknown 的两种真实情形（GUI 分不出是哪种 ✓ 但两种都**不是**「CLI 不查」了 ✓）：
            //   ① 随包缺 plugin 目录（开发构建常见 ✓）/ 随包 package.json 或内容文件读不到 ✓ → CLI 打了 DIAG ✓
            //   ② **老 CLI**（3.0.x 之前那句写死的 latest=unknown state=unknown ✓）→ 它确实不比对 ✓
            //   两种都如实说成「基准不可用 ✓ 见诊断行」，✗ 不笼统写「未知」✗（诊断行由 HintText 给出 ✓）
            if (kind == "plugin") return "随包基准不可用";
            if (kind == "desktop") return installed == "unknown" ? "未检测到桌面端" : "本工具不比对";
            // GUI 不认识的 kind/state：把 CLI 的原值贴出来 ✓ 不吞 ✗ 不笼统化 ✗
            string s = string.IsNullOrEmpty(state) ? "空" : state;
            string k = string.IsNullOrEmpty(kind) ? "空" : kind;
            return "未识别（kind=" + k + " state=" + s + "）";
        }

        /// <summary>版本行（长 ✓）：把"已装什么 / 最新能不能拿到 / 为什么"说全 ✓。
        /// ★ 2026-10-11：两个版本号都按 **tag 形态**显示（`vX.Y.Z` ✓ 与 GitHub release tag 同形 ✓）
        ///   —— **只影响显示** ✓（CLI 的 `UPDATECENTER_ITEM … installed=/latest=` 里是**归一化**的 `3.0.7` ✓
        ///      字段本身一个字不改 ✓ 也**不替 CLI 编一个版本号** ✗：只有 CLI 真的给了非 unknown 的值才显示 ✓）。</summary>
        public static string VersionText(string kind, string state, string latest, string installed)
        {
            bool instKnown = !(string.IsNullOrEmpty(installed) || installed == "unknown");
            if (kind == "desktop" && !instKnown)
                return "未检测到官方桌面端（本工具只在 %LOCALAPPDATA%\\Programs\\DeepSeek Harness 下找它 ✗ 不猜版本 ✗）";
            string head = instKnown ? ("已装 " + VersionMarkers.TagOf(installed)) : "已装版本：未检测到（CLI 报 installed=unknown）";
            // ★★★ 2026-10-11 **插件行**（CLI 3.0.x 起真的给基准了 ✓）：latest = **你这份工具箱自带的那一份**的版本 ✓
            //   ✗ 不是网上的最新版 ✗（插件没有独立版本渠道 ✓ CLI 也不会拿工具箱 tag 顶替 ✓ —— 那句诚实口径在 CLI 的 NOTE 里 ✓）
            //   `content-mismatch`（版本同、哈希不同）必须**在版本行里也点出来** ✓ —— 否则「已装 v0.2.0 → 随包 v0.2.0」看着像没事 ✗
            if (kind == "plugin")
            {
                string who = instKnown ? ("已装 " + VersionMarkers.TagOf(installed)) : "未安装（CLI 报 installed=unknown ✓ 本工具按 profile 如实报 ✓）";
                if (!string.IsNullOrEmpty(latest) && latest != "unknown")
                {
                    string t = who + "　→　随包 " + VersionMarkers.TagOf(latest) + "（**你这份工具箱自带的那一份** ✓ 不是网上的最新版 ✗）";
                    if (state == "content-mismatch")
                        t += "　——　**版本号相同，但 snapshot.js / index.js 的内容哈希与随包不同** ✓（半旧 / 被改过 ✓ 只看版本号看不出来 ✓）";
                    if (state == "newer-than-bundled")
                        t += "　——　已装版本**高于**随包 ✓（如实说 ✓ ✗ 不硬说成「有更新」✗）";
                    return t;
                }
                return who + "　→　随包基准：CLI 的 ITEM 行里是 `latest=unknown`（**只有老 CLI、或随包缺 plugin 目录 / 随包内容读不到**才这样 ✓ 新 CLI 会拿随包那一份做基准 ✓ 原因见诊断行 ✓）—— 本工具**不猜**最新版 ✗";
            }
            if (!string.IsNullOrEmpty(latest) && latest != "unknown")
                return head + "　→　最新 " + VersionMarkers.TagOf(latest);
            if (kind == "webui")
                return head + "　→　最新版本：取不到最新版本（离线，或 npm 返回值未通过白名单）";
            if (kind == "minato")
                return head + "　→　最新版本：CLI 没给 latest（只有**老 CLI** 才这样 —— 新 CLI 会查 GitHub 的 /releases/latest ✓；本工具**不猜**最新版 ✗）";
            if (kind == "desktop")
                return head + "　→　版本比对：本工具不做（官方桌面端只去官方页更新）";
            return head + "　→　最新版本：CLI 的标记行里没有 latest 字段（✗ 不猜 ✗）";
        }

        /// <summary>诊断补充行（只有"本该有最新版却拿不到"才给 ✓ 其余返回空串 ✗ 不噪声 ✓）。
        /// 优先贴 CLI 自己打的**原始原因**：`UPDATECENTER_DIAG <id> <原因>`（CLI 3.0.7+ ✓ 2026-10-11 加 ✓）
        /// → 再退到 `…_FAIL <文案>` ✓ —— 都没有就如实说"CLI 这次没打原因" ✓ 不替它编 ✗。</summary>
        public static string HintText(string kind, string state, string latest, string installed, string raw)
        {
            // minato（**本工具自己** ✓）：现在 CLI 会真去查 GitHub 的 /releases/latest ✓
            //   → 取不到时它有 DIAG 行 ✓ 这里必须把它贴出来 ✓（否则用户只看到"没给 latest" ✗ 而不知道是离线还是别的原因 ✗）
            if (kind == "minato")
            {
                if (!string.IsNullOrEmpty(latest) && latest != "unknown") return "";
                string d = DiagOf(raw, kind);
                if (!string.IsNullOrEmpty(d)) return "诊断：CLI 原文里的原因 —— " + d;
                if (!string.IsNullOrEmpty(state) && state != "unknown") return "";
                return "诊断：CLI 这次没打原因行（老 CLI 不查本工具的更新 ✓）→ 本工具不替它编原因 ✗；换与新版本配套的 dsh-minato.exe 后重试 ✓";
            }
            // ★ 插件行（**桥接插件** ✓ 2026-10-11）：CLI 现在会拿**随包那一份**当基准 ✓
            //   → 于是「本该有基准却拿不到」时它才打 DIAG ✓（随包缺 plugin 目录 / package.json 或内容文件读不到 ✓）
            //   · 拿到了基准（latest 非 unknown）→ **不给诊断** ✓（不噪声 ✓）
            //   · 没有 DIAG 且 state=unknown → 只能是**老 CLI**（它那句写死的 latest=unknown state=unknown ✓）→ 如实说 ✓
            //   ✗ 不替 CLI 编原因 ✗
            if (kind == "plugin")
            {
                if (!string.IsNullOrEmpty(latest) && latest != "unknown") return "";
                string dpi = DiagOfIdPrefix(raw, "plugin:");
                if (!string.IsNullOrEmpty(dpi)) return "诊断：CLI 原文里的原因 —— " + dpi;
                if (!string.IsNullOrEmpty(state) && state != "unknown") return "";
                return "诊断：CLI 这次没打原因行（**老 CLI** 的插件行只报已装版本 ✓ 不做比对 ✓）→ 换与新版本配套的 dsh-minato.exe 后即可看到「随包」基准 ✓；本工具**不替它编原因** ✗";
            }
            if (kind != "webui") return "";
            if (!string.IsNullOrEmpty(latest) && latest != "unknown") return "";
            string why = FailReason(raw);
            if (!string.IsNullOrEmpty(why)) return "诊断：CLI 原文里的原因 —— " + why;
            if (!string.IsNullOrEmpty(raw) && raw.IndexOf("取不到更新日志", StringComparison.Ordinal) >= 0)
                return "诊断：最新版本与更新日志**都**没取到 → 基本可以断定是离线（或 npm 不在 PATH）；CLI 这次没打原因行，本工具不替它编原因 ✗";
            return "诊断：CLI 这次没打原因行（update-center 不带原因行）→ 常见原因：离线 / npm 不在 PATH / registry 返回值未通过白名单；点「检查更新」可看 CLI 的 update-info 怎么说 ✓";
        }

        /// <summary>取 CLI 自己打的诊断原因：`UPDATECENTER_DIAG <id> <原因>`（CLI 2026-10-11 起 ✓）。
        /// **按前缀取** ✓（不按 `_FAIL` 那种正则 ✓ —— 原因文案里可能有空格/冒号/中文标点 ✓ 前缀最稳 ✓）。
        /// 只认给定 id 的那一条 ✓；没有 → 空串 ✓ **不编** ✗。</summary>
        public static string DiagOf(string raw, string id)
        {
            if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(id)) return "";
            string tag = "UPDATECENTER_DIAG " + id + " ";
            string[] ls = raw.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < ls.Length; i++)
            {
                string t = ls[i] == null ? "" : ls[i].Trim();
                if (t.StartsWith(tag, StringComparison.Ordinal))
                {
                    string v = t.Substring(tag.Length).Trim();
                    if (v.Length > 0) return v;
                }
            }
            return "";
        }

        /// <summary>取第一条 `UPDATECENTER_DIAG &lt;id 以 idPrefix 开头&gt; &lt;原因&gt;` 的原因原文 ✓
        /// （插件行的 id 是 `plugin:&lt;包名&gt;` ✓ 而 `HintText` 只拿得到 kind ✓ 拿不到具体 id ✓ 所以按**前缀**取 ✓
        /// 多条时只认**第一条** ✓（同一个随包基准缺失，每个 profile 的原因文案一样 ✓）；没有 → 空串 ✓ **不编** ✗）。</summary>
        public static string DiagOfIdPrefix(string raw, string idPrefix)
        {
            if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(idPrefix)) return "";
            string tag = "UPDATECENTER_DIAG ";
            string[] ls = raw.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < ls.Length; i++)
            {
                string t = ls[i] == null ? "" : ls[i].Trim();
                if (!t.StartsWith(tag, StringComparison.Ordinal)) continue;
                string rest = t.Substring(tag.Length);
                int sp = rest.IndexOf(' ');
                if (sp <= 0) continue;
                if (!rest.Substring(0, sp).StartsWith(idPrefix, StringComparison.Ordinal)) continue;
                string v = rest.Substring(sp + 1).Trim();
                if (v.Length > 0) return v;
            }
            return "";
        }

        /// <summary>取 CLI 原始的失败原因：第一条形如 `<全大写标记词>_FAIL <文案>` 的行 ✓。
        /// 普通文本里的 `_FAIL ` 不算（左边必须全是 A-Z 与下划线 ✓）→ 不误报 ✓；没有 → 空串 ✓ 不编 ✗。</summary>
        public static string FailReason(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string[] ls = raw.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < ls.Length; i++)
            {
                string t = ls[i] == null ? "" : ls[i].Trim();
                int at = t.IndexOf("_FAIL ", StringComparison.Ordinal);
                if (at <= 0) continue;
                string head = t.Substring(0, at);
                bool ok = true;
                for (int k = 0; k < head.Length; k++) { char c = head[k]; if (!((c >= 'A' && c <= 'Z') || c == '_')) { ok = false; break; } }
                if (!ok) continue;
                string v = t.Substring(at + "_FAIL ".Length).Trim();
                if (v.Length > 0) return v;
            }
            return "";
        }

        /// <summary>更新页读不到任何 `UPDATECENTER_ITEM` 时的**标题**（纯函数 ✓ 可测 ✓）。
        /// ★ 第三种情况（老 CLI 没有对应标记行）就在这里说清 ✗ 不许笼统写「未知」✗。</summary>
        public static string EmptyTitle(string raw, bool loading)
        {
            if (loading) return "正在读取更新信息…";
            if (string.IsNullOrEmpty(raw) || raw.Trim().Length == 0) return "还没读到更新信息";
            if (raw.IndexOf("UPDATECENTER_", StringComparison.Ordinal) < 0) return "该 CLI 版本过旧，无法检查更新";
            return "CLI 支持更新检查，但这次一个组件都没报";
        }

        /// <summary>上面那条空态的**下一步说明**（同样纯函数 ✓）。</summary>
        public static string EmptyDetail(string raw, bool loading)
        {
            if (loading) return "正在跑 update-center --stream（要查 npm / GitHub，是最慢的一项；完成后会自动补上）。";
            if (string.IsNullOrEmpty(raw) || raw.Trim().Length == 0) return "点上面的「检查更新」试试（只读，不动任何东西）。";
            if (raw.IndexOf("UPDATECENTER_", StringComparison.Ordinal) < 0)
                return "读到的原文里没有 UPDATECENTER_ITEM 标记行 —— 更新检查靠 CLI 3.x 的 update-center 命令，本 GUI 只解析它的标记行（不引用核心程序集）。下一步：换成与新版本配套的 dsh-minato.exe（或用环境变量 DSHT_CLI 指向它），再点「刷新」。";
            return "原文里有 UPDATECENTER_ 行但没有 UPDATECENTER_ITEM —— 这不太正常，可点「刷新」重试；若一直如此，请在「原始输出」里看原文。";
        }
    }
}
