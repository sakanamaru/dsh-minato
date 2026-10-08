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

        public ToolkitConfig Copy()
        {
            ToolkitConfig c = new ToolkitConfig();
            c.Lang = Lang; c.Host = Host; c.Workspace = Workspace; c.KeepBackups = KeepBackups;
            c.CheckUpdate = CheckUpdate; c.CheckDshUpdate = CheckDshUpdate; c.DshVersions = DshVersions;
            c.UpdateChannel = UpdateChannel; c.CloseAction = CloseAction; c.AutoStart = AutoStart; c.AutoStartTarget = AutoStartTarget; c.BrowserMode = BrowserMode; c.UiParallel = UiParallel; c.ScanChildren = ScanChildren; c.GuiStartPage = GuiStartPage; c.GuiAutoRefresh = GuiAutoRefresh; c.GuiShell = GuiShell; c.GuiStyle = GuiStyle; c.BalanceKey = BalanceKey;
            c.SessionsDefaultLevel = SessionsDefaultLevel; c.SessionsPriceInPerMTok = SessionsPriceInPerMTok; c.SessionsPriceCacheReadPerMTok = SessionsPriceCacheReadPerMTok; c.SessionsPriceCacheWritePerMTok = SessionsPriceCacheWritePerMTok; c.SessionsPriceOutPerMTok = SessionsPriceOutPerMTok;
            return c;
        }
    }
}