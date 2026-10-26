using System;
using System.Collections.Generic;
using System.Globalization;
using Dsht.Gui.Avalonia.Design;

namespace Dsht.Gui.Avalonia.Markers
{
    /// <summary>★ 悬边状态灯的**四色 = 一色一信号**（2026-10-26 · 提案 §11 ✓✓ 维护者定案）。
    /// 优先级（提案 §11 逐字）：**红 &gt; 黄 &gt; 蓝 &gt; 绿 &gt; 灰** ⇒ 枚举值即优先级 ✓。
    /// <para>★★ **本文件是"每盏灯指到它的信号来源"这条硬规则的唯一执行点** ✗：</para>
    /// <para>· 每一盏灯都由 <see cref="Lit"/> 产出 ✓ 而 <see cref="Lit"/> **拒绝没有来源的灯** ——
    ///   来源为空 ⇒ 强制返回灰（未知）✓✓ ⇒ **"指不到信号来源的灯"在代码层面亮不起来** ✗。</para>
    /// <para>· 五色各自的信号（逐条可指 ✓ 见下）；**没有信号就是灰** ✓ ✗ 绝不用颜色猜 ✗。</para>
    /// <para>🔴 红（异常，三条真信号）：① dsh 未运行 ✓ ② 桥接快照陈旧 ✓ ③ 插件与随包不一致 ✓
    ///   —— 提案 §11 表里的第 ④ 条（投影 `status` 失败态）**形状待验** ⇒ **不接这一路** ✓；
    ///   第 ⑤ 条（会话格式损坏）我们解不了 zstd 多帧 ⇒ **暂不接** ✓（两条都在此写明 ✗ 不假装有 ✗）。</para>
    /// <para>🟡 黄（询问）：投影 `questions.active` **非空** ✓✓（磁盘投影直接可读 ✓ 不需要桥接 ✓）。</para>
    /// <para>🔵 蓝（进行中）：桥接快照 `live=true` ✓（标「来自桥接快照 · 3 秒拍」✓）；
    ///   **无桥接 ⇒ 灰/未知** ✗ 绝不许猜成进行中 ✗。</para>
    /// <para>🟢 绿（完成）：投影 `todos` **全部 completed**（形状先验 ✓）· 或「由在动转停驻」（**推断** ✓
    ///   **必须在界面上标注是推断** ✗✓ —— 见 <see cref="SrcInferredParked"/>）。</para>
    /// <para>⚪ 灰（未知）：**无信号** ✓ 这是默认态 ✓ 不是失败 ✓。</para>
    /// <para>★ 本文件是**纯函数** ✓ 不碰 IO ✗ 不读时钟 ✗（时刻/记忆由调用方传入 ⇒ LogicTests 直接钉死 ✓）。</para>
    /// </summary>
    public enum LightColor
    {
        /// <summary>⚪ 未知 —— **无信号**（默认态 ✓ 不是失败 ✓）。</summary>
        Unknown = 0,
        /// <summary>🟢 完成。</summary>
        Done = 1,
        /// <summary>🔵 进行中。</summary>
        Running = 2,
        /// <summary>🟡 询问（有待回答）。</summary>
        Asking = 3,
        /// <summary>🔴 异常。</summary>
        Bad = 4
    }

    /// <summary>一盏灯 = 颜色 + **它的信号来源**（来源是唯一出处文案 ✓ 界面直接显示 ✓ 断言逐字钉 ✓）。</summary>
    public sealed class Light
    {
        public LightColor Color = LightColor.Unknown;
        /// <summary>信号来源（人话 ✓ 可核对 ✓）。灰灯的来源恒为空串 ✓ —— 灰 = **没有信号可指** ✓。</summary>
        public string Source = "";
        public bool IsLit { get { return Color != LightColor.Unknown; } }
    }

    /// <summary>悬浮窗做判定所需的**外部事实**（全部由调用方从**已有取数**里填 ✓ 不为它再起进程 ✗）。</summary>
    public sealed class OverlayContext
    {
        /// <summary>本机运行态**取到了**（status 数据可用 ⇒ 红灯①才有信号 ✓ 否则不亮 ✗）。</summary>
        public bool DshRunningKnown;
        /// <summary>dsh 在运行（`STATUS_UP` / 官方桌面端在跑 ✓）。</summary>
        public bool DshRunning = true;
        /// <summary>CLI 打了 `SESSIONS_SNAPSHOT_STALE`（桥接快照陈旧 ✓ 红灯②的唯一信号 ✓）。</summary>
        public bool SnapshotStale;
        /// <summary>插件状态**取过**（GUI 跑过 `update-center` ⇒ 才有 `content-mismatch` 可判 ✓）。</summary>
        public bool PluginStateKnown;
        /// <summary>插件与随包不一致（`state=content-mismatch` ✓ 红灯③的唯一信号 ✓）。</summary>
        public bool PluginMismatch;
        /// <summary>有桥接快照（`SESSIONS_SOURCE` = snapshot / snapshot+disk ✓）—— 蓝灯的前提 ✓。</summary>
        public bool BridgePresent;
    }

    public static class OverlayLight
    {
        // ================================================================ 信号来源文案（唯一出处 ✓）

        /// <summary>🔴① 本机状态：dsh 未在运行。</summary>
        public const string SrcDshDown = "本机状态：dsh 未在运行";
        /// <summary>🔴② 桥接快照陈旧。</summary>
        public const string SrcSnapshotStale = "桥接快照陈旧（CLI 的诊断行说插件已不在，快照里的运行态已被清掉）";
        /// <summary>🔴③ 插件与随包不一致。</summary>
        public const string SrcPluginMismatch = "插件与工具箱自带的那一份不一致（版本号相同但内容哈希不同）";
        /// <summary>🟡 投影 `questions.active` 非空。</summary>
        public const string SrcQuestions = "有待回答（会话投影里的待答问题列表非空）";
        /// <summary>🔵 桥接快照 `live=true`（逐字带「来自桥接快照 · 3 秒拍」✓ 提案 §11 硬要求 ✓）。</summary>
        public const string SrcBridgeLive = "来自桥接快照 · 3 秒拍（该会话还挂在 dsh 进程里）";
        /// <summary>🟢 投影 `todos` 全部完成。</summary>
        public const string SrcPlanDone = "计划全部完成（会话投影里的待办全部为已完成）";
        /// <summary>🟢 「由在动转停驻」—— **推断**（必须在界面上标注 ✓ 提案 §11 硬要求 ✓）。</summary>
        public const string SrcInferredParked = "推断：上一次取数时它还在动（桥接快照 active=1），这一次已经停驻";

        /// <summary>没有桥接快照时**如实说明**（✗ 不许猜成"进行中" ✗ 也不许猜成"完成" ✗）。</summary>
        public const string NoBridgeText = "未装桥接插件：本机拿不到「在动 / 挂着」这项事实，所以进行中这一档一律按未知处理。";
        /// <summary>桥接在、但快照没有 `active` 字段（旧版插件）。</summary>
        public const string OldBridgeText = "桥接快照里没有「在动」字段（插件版本较旧）：在动 / 挂着分不出来，这一档按未知处理。";

        /// <summary>★ 标题开关开启时的**明示**（提案 §11 红线 A ✓ 逐字含「标题可能由你的对话内容派生」✓）。
        /// <para>★★ **这一份是全项目唯一出处** ✓（2026-10-26 接线统一）：悬边状态灯面板与看板 Top 5 卡
        /// 显示的是同一句 ✓ —— `BoardHierarchy.TitleNotice` 直接引用本常量 ✗ 不许两处各写一份 ✗
        /// （两处文案一旦不一致，LogicTests 的等值断言立刻红 ✓ 可证伪 ✓）。</para></summary>
        public const string TitleNotice = "标题可能由你的对话内容派生（由模型根据对话自动生成）；本工具不会把标题写进任何文件或日志。";
        /// <summary>默认态（标题开关关）的名称口径说明（数据最小化 ✓）。</summary>
        public const string NamePolicyText = "名称 = 项目（会话工作目录）名 + 会话短码，来自路径，不含对话内容。";

        // ================================================================ 造灯（唯一入口 ✓）

        /// <summary>造一盏灯 ✓★ **来源为空 ⇒ 强制灰** ✓✓ —— 这就是"指不到信号来源的灯不亮"的机器执行者 ✗。</summary>
        public static Light Lit(LightColor color, string source)
        {
            Light l = new Light();
            if (color == LightColor.Unknown || string.IsNullOrEmpty(source)) return l;   // ✗ 没有来源 ⇒ 不亮 ✗
            l.Color = color;
            l.Source = source;
            return l;
        }

        /// <summary>灰灯（未知 ✓ 默认态 ✓ 来源为空 ✓ 没有任何断言）。</summary>
        public static Light None() { return new Light(); }

        // ================================================================ 判定

        /// <summary>**系统灯**（红：① dsh 未运行 ② 桥接快照陈旧 ③ 插件与随包不一致）——
        /// 三条都是"整机级"事实 ✓ 与具体会话无关 ✓；一条都不成立 ⇒ 灰（未知 ✓ 不是"正常"✗）。</summary>
        public static Light SystemLight(OverlayContext c)
        {
            if (c == null) return None();
            if (c.DshRunningKnown && !c.DshRunning) return Lit(LightColor.Bad, SrcDshDown);
            if (c.SnapshotStale) return Lit(LightColor.Bad, SrcSnapshotStale);
            if (c.PluginStateKnown && c.PluginMismatch) return Lit(LightColor.Bad, SrcPluginMismatch);
            return None();
        }

        /// <summary>**一行会话的灯**（一色一信号 ✓ 按优先级取它**有的**最高那一档）：
        /// 黄（`questions.active` 非空）→ 蓝（桥接 `live=true`）→ 绿（`todos` 全完成 · 或"由在动转停驻"的**推断**）→ 灰。
        /// <paramref name="wasActiveEarlier"/> = **更早的某一拍**里这个会话 active=1（推断态的唯一依据 ✓ 由调用方持有 ✓）。</summary>
        public static Light RowLight(SessionRow r, OverlayContext c, bool wasActiveEarlier)
        {
            if (r == null) return None();
            if (r.QuestionKnown && r.QuestionActive > 0) return Lit(LightColor.Asking, SrcQuestions);
            if (c != null && c.BridgePresent && r.Live) return Lit(LightColor.Running, SrcBridgeLive);
            if (r.PlanKnown && r.PlanTotal > 0 && r.PlanDone == r.PlanTotal) return Lit(LightColor.Done, SrcPlanDone);
            // ★ 推断态：必须**两个条件同时成立**（更早拍到过 active=1 ✓ 且这一拍 active=0 是**已知的** ✓）——
            //   ✗ 不许拿"active 未知"来凑推断 ✗（那正是"用颜色猜"✗）
            if (wasActiveEarlier && r.ActiveKnown && !r.Active) return Lit(LightColor.Done, SrcInferredParked);
            return None();
        }

        /// <summary>**悬边灯** = 所有灯里优先级最高的那一盏（红 &gt; 黄 &gt; 蓝 &gt; 绿 &gt; 灰 ✓ 提案 §11 逐字）。
        /// 并列时**先到先得** ✓（系统灯先于行灯 ✓ 行内按传入顺序 ✓ 确定性 ✓）；一个信号都没有 ⇒ 灰 ✓。</summary>
        public static Light EdgeLight(OverlayContext c, IList<SessionRow> rows, ParkedMemory memory)
        {
            Light best = SystemLight(c);
            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    SessionRow r = rows[i];
                    if (r == null) continue;
                    bool was = memory != null && memory.PriorActive(r.Id);
                    Light one = RowLight(r, c, was);
                    if ((int)one.Color > (int)best.Color) best = one;
                }
            }
            return best;
        }

        // ================================================================ 名称 / 计划 / 排序（纯函数 ✓）

        /// <summary>项目名 = 会话工作目录（`identity.cwd`）的**末段** ✓（路径派生 ✓ 非对话内容 ✓）。
        /// 分隔符按 `/` 与 `\` **两种都切**（同一份投影在 Windows / Linux 上都能读 ✓）；末尾分隔符先去掉 ✓；
        /// 取不到 / 空 / 只有盘符 ⇒ 空串 ✓（**绝不编一个名字** ✗ 界面退化成只有短码 ✓）。</summary>
        public static string ProjectName(string cwd)
        {
            if (string.IsNullOrEmpty(cwd)) return "";
            string t = cwd.Trim().TrimEnd('/', '\\');
            if (t.Length == 0) return "";
            int at = t.LastIndexOfAny(new char[] { '/', '\\' });
            string name = at >= 0 ? t.Substring(at + 1) : t;
            name = name.Trim();
            // 只有盘符（`C:`）或只有点 ⇒ 不算名字 ✓ 不编 ✓
            if (name.Length == 0 || name == "." || name == ".." || name.EndsWith(":", StringComparison.Ordinal)) return "";
            return name;
        }

        /// <summary>一行会话的**名称**：开关关（默认）⇒「项目名 · 短码」✓；开关开且投影确实有标题 ⇒ 标题 ✓。
        /// ★ 默认态**绝不出现标题** ✗（提案 §11 红线 A ✓ 有断言 ✓）；项目名拿不到 ⇒ 只给短码 ✓ 不编 ✗。</summary>
        public static string RowName(SessionRow r, bool showTitle)
        {
            if (r == null) return "";
            if (showTitle && !string.IsNullOrEmpty(r.Title)) return r.Title;
            string proj = ProjectName(r.Cwd);
            return proj.Length > 0 ? (proj + " · " + r.ShortId) : r.ShortId;
        }

        /// <summary>计划数字文案：拿得到 ⇒「计划 3/7」✓；**拿不到 ⇒「计划未知」**（✗ 一个数字都不显示 ✗
        /// ✗ 绝不 `0/0` ✓ 绝不编 ✓ —— 提案 §11 硬要求 ✓ 有断言 ✓）。</summary>
        public static string PlanText(SessionRow r)
        {
            if (r == null || !r.PlanKnown || r.PlanTotal <= 0) return "计划未知";
            return "计划 " + r.PlanDone.ToString(CultureInfo.InvariantCulture) + "/" + r.PlanTotal.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>`lastPromptAt` 的 ISO 文本 → epoch 毫秒（解析不了 ⇒ 0 = **未知** ✗ 不猜 ✓）。</summary>
        public static long LastPromptMs(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return 0;
            DateTime t;
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out t)) return 0;
            return t.Ticks / TimeSpan.TicksPerMillisecond;
        }

        /// <summary>★ 排序 = **`lastPromptAt` 倒序**（= 先后唤醒 ✓ 提案 §11 逐字）。
        /// 时间未知的排最后 ✓（✗ 不丢 ✗ 不猜时间 ✓）；并列按 id 字典序 ⇒ 两次运行同序 ✓ 确定性 ✓。
        /// **不改入参** ✓ 返回新列表 ✓（可测 ✓）。</summary>
        public static List<SessionRow> SortedByLastPrompt(IList<SessionRow> rows)
        {
            List<SessionRow> all = new List<SessionRow>();
            if (rows != null) for (int i = 0; i < rows.Count; i++) if (rows[i] != null) all.Add(rows[i]);
            all.Sort(delegate (SessionRow x, SessionRow y)
            {
                long a = LastPromptMs(x.Last), b = LastPromptMs(y.Last);
                if (a != b) return b.CompareTo(a);                       // 倒序 ✓
                return string.CompareOrdinal(x.Id ?? "", y.Id ?? "");    // 并列 → id 字典序 ✓
            });
            return all;
        }

        /// <summary>一行右侧的**数据时刻**（提案 §11 硬要求：每行都带数据时刻 ✓）：
        /// `最后提问 MM-dd HH:mm` ✓；拿不到 ⇒ `最后提问 未知` ✗ 绝不 `00:00:00` ✓ 绝不编 ✓。</summary>
        public static string RowClockText(SessionRow r)
        {
            long ms = r == null ? 0 : LastPromptMs(r.Last);
            if (ms <= 0) return "最后提问 未知";
            DateTime t = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime();
            return "最后提问 " + t.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>全局数据时刻（HH:mm:ss ✓ 未知 ⇒「未知」✗ 不用 0 顶替 ✓）—— 与看板顶部那一行**同一口径** ✓。</summary>
        public static string ClockText(DateTime? at)
        {
            if (!at.HasValue) return "未知";
            return at.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>★ 四色灯 → **token 表的语义色**（"这一色是什么意思"那一步 ✓ 一色一信号的口径 ✓）。
        /// <para>🔴 异常 → <see cref="DesignTokens.Semantic.Abnormal"/> · 🟡 询问 → <see cref="DesignTokens.Semantic.Attention"/>
        /// · 🔵 进行中 → <see cref="DesignTokens.Semantic.Accent"/>（四风格里都是冷色主色 ✓）
        /// · 🟢 完成 → <see cref="DesignTokens.Semantic.Normal"/> · ⚪ 未知 → <see cref="DesignTokens.Semantic.Unknown"/>
        /// （**中性灰** ✓ ✗ 绝不用"绿=正常"的颜色去替一个我们并不知道的状态背书 ✗）。</para>
        /// <para>★ 这里只回答"是哪个语义色" ✗ 不回答"是什么色值" ✗ —— 色值由 token 表给
        /// （<see cref="DesignTokens.StatusBase"/> / <see cref="DesignTokens.StatusHalo"/> /
        /// <see cref="DesignTokens.StatusRing"/> ✓）；**画法只有 `Design/StatusLight.cs` 一处** ✓。</para>
        /// <para>纯函数 ✓ 不碰 IO ✗ 不读时钟 ✗（LogicTests 直接钉死 ✓ 改坏即红 ✓）。</para></summary>
        public static DesignTokens.Semantic SemanticOf(LightColor c)
        {
            if (c == LightColor.Bad) return DesignTokens.Semantic.Abnormal;
            if (c == LightColor.Asking) return DesignTokens.Semantic.Attention;
            if (c == LightColor.Running) return DesignTokens.Semantic.Accent;
            if (c == LightColor.Done) return DesignTokens.Semantic.Normal;
            return DesignTokens.Semantic.Unknown;
        }

        /// <summary>四色灯的中文名（界面用 ✓ 唯一出处 ✓）。</summary>
        public static string ColorName(LightColor c)
        {
            if (c == LightColor.Bad) return "异常";
            if (c == LightColor.Asking) return "询问";
            if (c == LightColor.Running) return "进行中";
            if (c == LightColor.Done) return "完成";
            return "未知";
        }

        /// <summary>一盏灯的一句话（`颜色 · 信号来源`）✓。
        /// 灰灯**没有来源可指** ⇒ 只说「未知（没有可用的信号）」✗ 不编一个原因 ✓。</summary>
        public static string Describe(Light l)
        {
            if (l == null || !l.IsLit) return ColorName(LightColor.Unknown) + "（没有可用的信号）";
            return ColorName(l.Color) + " · " + l.Source;
        }

        /// <summary>展开面板顶部那一行：悬边灯的颜色名 + 它指到的信号来源 ✓（灰 ⇒ 如实说没有信号 ✓）。</summary>
        public static string EdgeSummary(Light edge)
        {
            return Describe(edge);
        }
    }

    /// <summary>「由在动转停驻」这条**推断**的记忆（内存 ✓ 不落盘 ✓ 纯数据 ✓）。
    /// 语义：<see cref="PriorActive"/> 只回答"**更早的某一拍**里见过它 active=1 吗" ✓；
    /// 所以调用顺序固定为：**先判定（读 PriorActive）→ 再 <see cref="Remember"/> 记下这一拍** ✓
    /// （否则本拍的 active=1 会立刻被当成"更早"⇒ 同拍就亮绿灯 ✗ 那是假推断 ✓）。</summary>
    public sealed class ParkedMemory
    {
        private readonly HashSet<string> _wasActive = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>更早的某一拍里见过这个 id active=1（空 id ⇒ 恒 false ✓ 不猜 ✓）。</summary>
        public bool PriorActive(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return _wasActive.Contains(id);
        }

        /// <summary>把这一拍 `active=1` 的会话记下来 ✓（幂等 ✓ 只增 ✓ 不删 —— 会话消失也不影响"更早见过"这个事实 ✓）。</summary>
        public void Remember(IList<SessionRow> rows)
        {
            if (rows == null) return;
            for (int i = 0; i < rows.Count; i++)
            {
                SessionRow r = rows[i];
                if (r == null || string.IsNullOrEmpty(r.Id)) continue;
                if (r.ActiveKnown && r.Active) _wasActive.Add(r.Id);
            }
        }

        public int Count { get { return _wasActive.Count; } }
    }
}
