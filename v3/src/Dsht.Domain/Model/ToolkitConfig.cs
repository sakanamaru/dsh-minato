namespace Dsht.Domain.Model
{
    /// <summary>工具箱配置（纯数据）。字段与默认值逐条对齐 v2.x 的 cfg* 静态字段。</summary>
    public sealed class ToolkitConfig
    {
        public string Lang = "auto";              // zh | en | auto
        public string Host = "127.0.0.1";         // 127.0.0.1 | localhost
        public string Workspace = null;           // 手动工作区；null/空 = 自动探测
        public int KeepBackups = 10;              // 自动类备份保留份数（最小 3）
        public bool CheckUpdate = true;
        public bool CheckDshUpdate = true;
        public string DshVersions = "";           // 历史版本（逗号分隔）
        public string UpdateChannel = "stable";   // stable | rc
        public string CloseAction = "";           // ask | tray | exit | 空（未询问）
        public bool AutoStart = true;
        /// <summary>开机自启时启动什么：auto（按平台 ✓ Win/Mac→官方桌面端 · Linux→dsh web）/ desktop / web ✓✓
        /// 用户要求："按平台选 + 做成设置项让你选" ✓</summary>
        public string AutoStartTarget = "auto";

        // —— 排障开关（用户要求："给可能会发生可能不会发生的问题提供解决的选项" ✓✓）——
        // 每个都**真的接线** ✓（不是摆设 ✗）；设置页会显示它们对应的"出现什么问题时试哪个" ✓
        /// <summary>浏览器打开方式：auto / snap / direct / xdg ✓（Linux 上浏览器打不开时切换 ✓）</summary>
        public string BrowserMode = "auto";
        /// <summary>界面是否并行取数据：on（快 ✓）/ off（串行 ✓ 老行为 ✓）</summary>
        public bool UiParallel = true;
        /// <summary>是否扫会话文件得出主/子代理归类：on / off（关掉更快 ✓ 但子代理统计为空 ✓）</summary>
        public bool ScanChildren = true;

        // —— 界面偏好（2026-10-02 用户要求："启动默认打开页面设置里可自选" ✓✓）——
        /// <summary>GUI 启动时默认打开的页面（0=概览 … 9=日志；索引即 GUI 的 NavItems ✓）。
        /// 默认 1（看板）。**纯 GUI 偏好** ✓：CLI 自己不消费它，只是替 GUI 存（与 browser_mode 同类 ✓）。</summary>
        public int GuiStartPage = 1;

        /// <summary>概览/看板的**自动刷新间隔**（2026-10-02 用户要求 ✓）：
        /// "off"（暂停 ✓ 默认）或秒数的文本形式（0.5–3600 ✓，如 "0.5"=实时 "1"=快 "3"=中 "5"=慢）。
        /// **纯 GUI 偏好** ✓ 同上；文本保存与其它键一致（`config-set gui_auto_refresh 3` ✓）。</summary>
        public string GuiAutoRefresh = "off";

        /// <summary>侧栏布局选择（0..4 = Shells.Sidebar..Hybrid）。纯 GUI 偏好 ✓ 2026-10-06 起持久化 ✓。</summary>
        public int GuiShell = 4;
        /// <summary>视觉风格（0..3 = Palette A..D）。纯 GUI 偏好 ✓ 2026-10-06 起持久化 ✓。</summary>
        public int GuiStyle = 0;

        /// <summary>DeepSeek 平台 API key（**余额检测用** ✓ 2026-10-02 用户要求"自己填写 key" ✓）。
        /// 空 = 未绑定 → 概览页不显示余额卡 ✓（用户要求"未绑定隐藏" ✓）。
        /// ⚠ **明文**保存在本机配置文件里 —— 只在用户自己的机器上 ✓ 不上传 ✓（CONFIGNOTE 如实写明 ✓）。
        /// 不强制格式 ✗（不猜 sk- 前缀 ✓ 兼容平台将来换形式 ✓）。</summary>
        public string BalanceKey = "";

        // —— 看板（2026-10-08 · 看板第一批 ✓）：口径默认 + 单价键 ✓（费用显示是第三批 ✓ 现在只落**存储 + 校验** ✓）——
        /// <summary>sessions 的默认口径：global / parents / parents_sub。
        /// ★ 第一批**只实现 global** ✓：选 parents / parents_sub 时 CLI 如实拒绝（SESSIONS_FAIL + stderr 注明待第二批 ✓✓）——
        ///   投影侧没有可靠血缘数据（catalog 漏 26 个真子代理 + 18 条 fork 边无投影来源 ✓ 实测证据见施工规格 §11.2）。</summary>
        public string SessionsDefaultLevel = "global";
        /// <summary>输入单价（美元/百万 token）。0 = 未填 → 不显示费用 ✓（绝不硬编码价格 ✗✓）。</summary>
        public decimal SessionsPriceInPerMTok = 0m;
        /// <summary>缓存读单价（美元/百万 token）。0 = 未填。</summary>
        public decimal SessionsPriceCacheReadPerMTok = 0m;
        /// <summary>缓存写单价（美元/百万 token）。0 = 未填。</summary>
        public decimal SessionsPriceCacheWritePerMTok = 0m;
        /// <summary>输出单价（美元/百万 token）。0 = 未填。</summary>
        public decimal SessionsPriceOutPerMTok = 0m;
        /// <summary>每日预算上限（**美元/天** ✓ 与四个单价同币种 ✓ 2026-10-16 成本面板 v3.2 · 维护者已确认键名 ✓）。
        /// 0 = 未设 → **不作超限判定**（`COST_BUDGET limit=none` ✓ 绝不猜默认额度 ✗✓）。
        /// ★ 口径：只跟**今日**（本地日）窗口的估算金额比较 ✓ —— 键名是 per_day ✓ 拿多日窗口总额比单日上限没有意义 ✗。
        /// ★ **零写入**：每次由会话重算 ✓ 不记累计 ✓ 不写任何运行时状态 ✓（勘察 §7.2 ✓）。</summary>
        public decimal SessionsBudgetUsdPerDay = 0m;

        // —— 悬边状态灯悬浮窗（2026-10-26 · 提案 §11 ✓✓ 维护者定案：「悬边状态灯 + 按需展开」）——
        /// <summary>是否显示置顶悬浮窗。**默认 off** ✓（提案 §9：突然冒出个悬浮窗很烦 ✗ 不默认开 ✓）。
        /// 纯 GUI 偏好 ✓（CLI 自己不消费它，只替 GUI 存 —— 与 gui_style 同类 ✓）。</summary>
        public bool OverlayEnabled = false;
        /// <summary>贴屏幕哪一边：left / right / top / bottom（默认 right ✓ 右侧竖条 ✓）。</summary>
        public string OverlayEdge = "right";
        /// <summary>沿那条边离屏幕起点的比例 0~1（默认 0.5 = 居中 ✓）。**相对位置** ⇒ 换分辨率也落在同一相对处 ✓。</summary>
        public decimal OverlayOffset = 0.5m;
        /// <summary>尺寸档：0=紧凑 / 1=标准（默认）/ 2=宽松（只缩放灯与面板的字号/厚度 ✓ 不改信息层级 ✓）。</summary>
        public int OverlayScale = 1;
        /// <summary>记住的显示器（`Screen.DisplayName` ✓ 拿不到就存 `#<序号>` ✓）。空 = 主屏 ✓。
        /// ★ 显示器拔掉 / 认不出 ⇒ **回落主屏** ✓ 且位置一律**钳进该屏工作区** ✗ 不许跑到屏幕外 ✗（提案 §4 ✓）。</summary>
        public string OverlayScreen = "";
        /// <summary>展开列表里是否显示**会话标题**。**默认 off** ✗✓（提案 §11 红线 A ✓）——
        /// 标题是 LLM 从对话内容派生的 ✓ 属提示词派生数据 ✓ 开启时界面必须**明示**这一点 ✓
        /// 且**绝不写盘/写日志**（本键只存开关本身 ✓ 不存任何标题文本 ✓）。</summary>
        public bool OverlayShowTitle = false;
        /// <summary>悬边灯的**画法样式**（2026-10-28 维护者拍板：默认 = B 圆点 + 轻环）。
        /// 取值**语义化**（✗ 不用 a/b/c 这种位置代号 ✗）：
        /// <c>line</c> = A 极简细条 · <c>dot</c> = B 圆点 + 轻环（默认）· <c>capsule</c> = C 双端胶囊。
        /// 纯 GUI 偏好 ✓（CLI 自己不消费它，只替 GUI 存 —— 与 gui_style 同类 ✓）。
        /// ★ 只改**画法**：颜色含义 / 优先级 / 点击穿透都不变 ✓（✗ 不是数据口径 ✗）。
        /// ★ 非法值 ⇒ 回落默认 + 一句明确提示（✗ 不崩 ✗ 不静默当默认 ✓）。</summary>
        public string OverlayLightStyle = "dot";

        public ToolkitConfig Copy()
        {
            ToolkitConfig c = new ToolkitConfig();
            c.Lang = Lang; c.Host = Host; c.Workspace = Workspace; c.KeepBackups = KeepBackups;
            c.CheckUpdate = CheckUpdate; c.CheckDshUpdate = CheckDshUpdate; c.DshVersions = DshVersions;
            c.UpdateChannel = UpdateChannel; c.CloseAction = CloseAction; c.AutoStart = AutoStart; c.AutoStartTarget = AutoStartTarget; c.BrowserMode = BrowserMode; c.UiParallel = UiParallel; c.ScanChildren = ScanChildren; c.GuiStartPage = GuiStartPage; c.GuiAutoRefresh = GuiAutoRefresh; c.GuiShell = GuiShell; c.GuiStyle = GuiStyle; c.BalanceKey = BalanceKey;
            c.SessionsDefaultLevel = SessionsDefaultLevel; c.SessionsPriceInPerMTok = SessionsPriceInPerMTok; c.SessionsPriceCacheReadPerMTok = SessionsPriceCacheReadPerMTok; c.SessionsPriceCacheWritePerMTok = SessionsPriceCacheWritePerMTok; c.SessionsPriceOutPerMTok = SessionsPriceOutPerMTok; c.SessionsBudgetUsdPerDay = SessionsBudgetUsdPerDay;
            c.OverlayEnabled = OverlayEnabled; c.OverlayEdge = OverlayEdge; c.OverlayOffset = OverlayOffset; c.OverlayScale = OverlayScale; c.OverlayScreen = OverlayScreen; c.OverlayShowTitle = OverlayShowTitle; c.OverlayLightStyle = OverlayLightStyle;
            return c;
        }
    }
}