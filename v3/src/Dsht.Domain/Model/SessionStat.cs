namespace Dsht.Domain.Model
{
    /// <summary>一个会话的投影统计。**全部来自 dsh 自己的投影（只读）**：只有计数、时间与元数据，
    /// **不含任何对话正文**（正文在 zstd 压缩的 `session.jsonl.zstd` 里，本工具不读）。
    /// 字段存在性用 Has* 标记：缺字段时**不假装 0**（0 与"没有这个数据"是两件事）。</summary>
    public sealed class SessionStat
    {
        public string Id = "";              // 会话 id（来自文件名或投影总表的键）
        public string Title = "";           // 投影里的标题（可能为空）
        public string Cwd = "";             // identity.cwd
        public string CreatedAt = "";       // identity.createdAt（数字 → UTC ISO-8601；空 = 没有该字段）
        public long CreatedAtEpochMs;       // 原值（epoch 毫秒；0 = 未知）
        public string LastPromptAt = "";    // sessionListMetadata.lastPromptAt（同上）
        public long LastPromptEpochMs;      // 原值（epoch 毫秒；0 = 未知）
        public bool Blank;                  // 空会话（dsh 自己标的）

        /// <summary>**最后使用**的模型 id（`modelSelection.val.lastUsed.model` ✓ 成本面板 v3.2 · 2026-10-16）。
        /// ★ 语义边界（规格 §2 / 勘察 §1.3）：dsh 每个会话**只留最后一次请求的模型**（`request/header` 覆盖 `pending` ✓），
        ///   会话内换过模型 ⇒ 整体失真 ⇒ 面板必须标「**近似**」并给 unknown 计数 ✓ 不假装精确 ✗。
        /// ★ 空串 = 该来源没有这个事实（旧投影无 `modelSelection` / 桥快照没有该段）⇒ 标记行写 `unknown` ✗ 不猜 ✓。
        /// ★ 只在投影行（磁盘）里有；token 四桶**没有** model 维度 ⇒ ✗ 不做按模型分摊（数据不存在 ✓ 规格 §1 不做项）。</summary>
        public string LastUsedModel = "";

        /// <summary>是否**正在运行**（来自桥接插件的快照：dsh `listSessions()` 的 `live` 标记）。
        /// 磁盘投影拿不到这个事实，所以插件缺失时恒为 false —— 这是"面板少了实时部分"的诚实体现。
        /// ★ 语义边界（2026-10-02 实测定稿 ✓）：`live` 只表示"**dsh 进程内还挂着这个会话**"
        ///   —— 桌面端开着时它 store 里的 6 个会话**全是 live** ✗ 哪怕 5 个几天没碰 ✓✓
        ///   所以"真在动"要看 <see cref="Active"/>（seq 在插件相邻两拍之间变过 ✓）。</summary>
        public bool Live;

        /// <summary>**真在动**：插件的相邻两拍（默认 3 秒）之间该会话的 `seq` 变过 ✓✓
        /// 这是"有事件在发生"的进程内实时事实 —— long 生成中途 lastPromptAt 不会变 ✗ 但 seq 会 ✓。
        /// 存在性用 <see cref="HasActive"/> 判定（插件太老/首拍无基线 → 未知 ✗ 不假装 false ✓）。</summary>
        public bool Active;
        /// <summary>插件有没有给出 `active` 字段（没有 = 该事实未知 ✓）。</summary>
        public bool HasActive;
        /// <summary>最近一次观测到活动的时间（ISO；插件按 seq 变化更新 ✓ 空串 = 未知）。</summary>
        public string LastActiveAt = "";

        public long Turns, Steps;
        public long LlmMs, ToolMs, TtftMs, DecodeMs, DecodeTokens, TtftSteps;
        public long UncachedInputTokens, OutputTokens, CacheReadTokens, CacheWriteTokens;
        public long SurfaceTokens, ContextWindow, PressureTokens;
        public long SystemTokens, ToolsTokens, MessageTokens;

        public bool HasStats;      // sessionStats 存在
        public bool HasTokens;     // tokenUsage.totals 存在
        public bool HasPressure;   // contextPressure 存在
        public bool HasBreakdown;  // contextBreakdown 存在

        // ——— 悬边状态灯（2026-10-26 · 提案 §11 ✓）：计划 `完成/总数` 与「有待回答」 ———
        //   ★ **形状先验**（提案 §11 硬要求 ✓）：只有投影行**形状认得**才算有信号 ✓
        //     形状不认 / 字段缺失 / 列表为空 ⇒ Has*=false ⇒ 界面**不显示数字**（✗ 绝不 `0/0` ✓ 不编 ✓）。
        /// <summary>计划已完成项数（`todos.val[].status == "completed"` 的计数 ✓ 逐项真值 ✓）。</summary>
        public long PlanDone;
        /// <summary>计划总项数（`todos.val` 数组长度 ✓）。</summary>
        public long PlanTotal;
        /// <summary>`todos` 行的形状被认出且**总项数 &gt; 0** ⇒ 计划数字可显示 ✓；
        /// 否则界面**一个数字都不显示** ✗（✗ 不显示 `0/0` ✓ 不编 ✓）。</summary>
        public bool HasPlan;

        /// <summary>`userQuestions.val.questions.active` 数组里**非空条目数**（有待回答的提问数 ✓ 逐项真值 ✓）。</summary>
        public long QuestionActive;
        /// <summary>`questions.active` 的形状被认出（是个数组）⇒ 「询问」这一路**有信号** ✓；
        /// 形状不认 / 字段缺失 ⇒ false ⇒ 黄灯**不亮** ✗（灰 ✓ 绝不猜成"没有问题"✗）。</summary>
        public bool HasQuestions;

        /// <summary>输入侧总 token（未命中缓存 + 命中缓存）。</summary>
        public long TotalInputTokens { get { return UncachedInputTokens + CacheReadTokens; } }
    }

    /// <summary>多会话汇总（合计 + 加权速度/命中率）。</summary>
    public sealed class SessionTotals
    {
        public int Count;                  // 参与汇总的会话数
        public int NonBlankCount;          // 其中非空会话数
        public long UncachedInputTokens, OutputTokens, CacheReadTokens, CacheWriteTokens;
        public long DecodeMs, DecodeTokens;

        /// <summary>缓存命中率（%）；分母为 0 → -1 表示**未知**（不假装 0%）。</summary>
        public double CacheHitPercent = -1;

        /// <summary>解码速度（tokens/s）；decodeMs 为 0 → -1 表示未知。</summary>
        public double DecodeTokensPerSec = -1;
    }
}
