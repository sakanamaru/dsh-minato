using System;
using System.Collections.Generic;
using Dsht.Gui.Avalonia.Markers;

namespace Dsht.Gui.LogicTests
{
    /// <summary>GUI 呈现层逻辑测试（零第三方断言）：验证标记行解析器 —— 界面能信任数据的前提。
    /// 运行：dotnet run --project v3/gui/Dsht.Gui.LogicTests -c Release</summary>
    internal static class Program
    {
        private static int _pass;
        private static int _fail;

        private static void Check(string name, bool ok)
        {
            if (ok) { _pass++; Console.WriteLine("  [PASS] " + name); }
            else { _fail++; Console.WriteLine("  [FAIL] " + name); }
        }

        /// <summary>测试里手搓一个解析层条目 ✓（只为断言合并卡那几个纯函数 ✓ —— 表格的列值测试不想先拼一遍标记行 ✓）。</summary>
        private static UpdateCenterMarkers.Item MkItem(string profile, string installed, string latest, string state, string next)
        {
            UpdateCenterMarkers.Item it = new UpdateCenterMarkers.Item();
            it.Id = "plugin:dsh-minato-bridge";
            it.Kind = "plugin";
            it.Profile = profile;
            it.Installed = installed;
            it.Latest = latest;
            it.State = state;
            it.Next = UpdateCenterMarkers.DecodeNext(next);
            return it;
        }

        private static int Main()
        {
            Console.WriteLine("== GUI 呈现层逻辑测试（标记行解析）==");

            string real =
                "SESSIONS_OK 2\n" +
                "SESSIONS_NONBLANK 1\n" +
                "SESSIONS_LIVE 1\n" +
                "SESSIONS_SOURCE snapshot\n" +
                "SESSIONS_ROOT C:\\Users\\x\\.dsh\\storages\\session_projcache\\sessions\n" +
                "SESSION 0052ed1f-1b9a-4cac-a27a-310abb951edc title=会话%20A%25B created=2026-09-23T01:48:28Z last=2026-09-23T01:48:28Z turns=1 steps=50 in=5161243 out=68886 cacheRead=4964096 hit=96.2 decode=195.7 ttft=187588 ctx=0.0 blank=0 live=1 active=1 lastActive=2026-09-23T01:48:30Z\n" +
                "SESSION abc12345 created=unknown last=unknown turns=0 steps=0 in=0 out=0 cacheRead=0 hit=unknown decode=unknown ttft=unknown ctx=unknown blank=1 live=0\n" +
                "SESSIONS_TOTAL in=5161243 out=68886 cacheRead=4964096 hit=97.1 decode=108.6";

            SessionsSnapshot s = SessionsMarkers.Parse(real);
            Check("解析成功标志与计数", s.Ok && s.Count == 2 && s.NonBlank == 1 && s.Live == 1);
            Check("数据来源与投影根", s.Source == "snapshot" && s.Root.Contains("session_projcache"));
            Check("来源说明文字区分 snapshot/disk", s.SourceText.Contains("桥接插件快照") && SessionsMarkers.Parse("SESSIONS_SOURCE disk").SourceText.Contains("磁盘投影"));
            Check("会话行数", s.Rows.Count == 2);
            SessionRow a = s.Rows[0];
            Check("行：id 与短 id", a.Id == "0052ed1f-1b9a-4cac-a27a-310abb951edc" && a.ShortId == "0052ed1f");
            Check("行：标题解析并解码（%20 空格 / %25 百分号）", a.Title == "会话 A%B" && a.TitleText == "会话 A%B");
            Check("行：无标题 → 人话兜底（不留空白）", s.Rows[1].Title == "" && s.Rows[1].TitleText == "（未命名会话）");
            Check("解码：- 与空 → 空串；非法 % 原样保留", SessionsMarkers.Decode("-") == "" && SessionsMarkers.Decode("") == "" && SessionsMarkers.Decode("50%") == "50%");
            Check("行：turns/steps/in/out/cacheRead", a.Turns == 1 && a.Steps == 50 && a.In == 5161243 && a.Out == 68886 && a.CacheRead == 4964096);
            Check("行：命中率/速度/压力解析为数值", Math.Abs(a.HitPercent - 96.2) < 0.001 && Math.Abs(a.DecodeTps - 195.7) < 0.001 && Math.Abs(a.CtxPercent - 0.0) < 0.001);
            Check("行：ttft 原值", a.TtftMs == 187588);
            Check("行：快照来源下 live 可信（夹具来源是 snapshot）", a.Live && !a.Blank && a.LiveText == "运行中" && a.LiveKnown);
            // ★ 2026-10-02：`live` ≠ 在动 ✓（用户实测"没在运行显示 运行6"：桌面端开着 → store 里全是 live ✗）
            //   三个新状态的契约 ✓✓
            Check("行：active=1 解析 + lastActive 保留", a.Active && a.ActiveKnown && a.LastActive == "2026-09-23T01:48:30Z");
            Check("行：live=1 active=0 → 挂着（不是运行中 ✓ 也不是未知 ✓）", SessionsMarkers.Parse(real.Replace("active=1", "active=0")).Rows[0].ActiveKnown && !SessionsMarkers.Parse(real.Replace("active=1", "active=0")).Rows[0].Active && SessionsMarkers.Parse(real.Replace("active=1", "active=0")).Rows[0].LiveText == "挂着（dsh 内未动）");
            // ★ 兜底档（**绝大多数现有用户**：插件还是 0.1.0 ✗ 没有 active 字段 ✓）→ 按最后活动时间判断 ✓ 并明说依据 ✓
            string noActive = real.Replace(" active=1 lastActive=2026-09-23T01:48:30Z", "");
            SessionRow na = SessionsMarkers.Parse(noActive).Rows[0];
            Check("行：无 active + 最后活动很久以前 → 「挂着 · 最后活动 N 天前」（不冒充精确 ✓）", na.Live && !na.ActiveKnown && na.LiveText.StartsWith("挂着 · 最后活动") && na.LastAgeMinutes > 60 * 24);
            string freshLast = noActive.Replace("last=2026-09-23T01:48:28Z", "last=" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            Check("行：无 active + 15 分钟内有过活动 → 「运行中（按最后活动判断）」", SessionsMarkers.Parse(freshLast).Rows[0].LiveText == "运行中（按最后活动判断）");
            string noActiveNoLast = noActive.Replace("last=2026-09-23T01:48:28Z", "last=unknown");
            SessionRow nn = SessionsMarkers.Parse(noActiveNoLast).Rows[0];
            Check("行：无 active 也无最后活动 → 「dsh 内挂着（活动未知）」（不猜 ✓✓）", !nn.ActiveKnown && nn.LastAgeMinutes == -1 && nn.LiveText == "dsh 内挂着（活动未知）");
            Check("行：磁盘来源下 live 不可信（显示运行态未知，不谎报已结束）", !SessionsMarkers.Parse(real.Replace("SESSIONS_SOURCE snapshot", "SESSIONS_SOURCE disk")).Rows[0].LiveKnown && SessionsMarkers.Parse(real.Replace("SESSIONS_SOURCE snapshot", "SESSIONS_SOURCE disk")).Rows[0].LiveText == "运行态未知");
            Check("行：时间戳保留 ISO 文本", a.Created == "2026-09-23T01:48:28Z" && a.Last == "2026-09-23T01:48:28Z");
            Check("行：token 用人读格式（K/M，InvariantCulture）", a.InText == "5.2M" && a.OutText == "68.9K" && a.CacheReadText == "5.0M");
            SessionRow b = s.Rows[1];
            Check("行：unknown 一律为 -1 / 空串（不假装 0）", b.HitPercent < 0 && b.DecodeTps < 0 && b.CtxPercent < 0 && b.TtftMs == -1 && b.Last == "" && b.LastShort == "unknown");
            Check("行：unknown 的显示文本", b.HitText == "unknown" && b.DecodeText == "unknown" && b.TtftText == "unknown" && b.CtxText == "unknown");
            Check("行：空会话标记与文案", b.Blank && !b.Live && b.LiveText == "空会话");
            Check("行：短时间与状态语义", a.LastShort == "09-23 01:48" && a.StatusKind == 0 && b.StatusKind == 2);
            Check("行：等级与条形（命中 96.2 → 高/96；压力 0 → 低/0）", a.HitLevel == 3 && a.HitBar == 96 && a.CtxLevel == 1 && a.CtxBar == 0);
            Check("行：首 token 超 1 秒按秒显示", a.TtftText == "187.6 s");
            Check("行：unknown 的等级/条形不假装", b.HitLevel == 0 && b.HitBar == 0 && b.CtxLevel == 0);
            List<SessionRow> f1 = SessionsView.Filter(s.Rows, SessionsView.FilterNonBlank);
            List<SessionRow> f2 = SessionsView.Filter(s.Rows, SessionsView.FilterLive);
            Check("过滤：非空 1 条 / 运行中 1 条 / 全部 2 条", f1.Count == 1 && f2.Count == 1 && SessionsView.Filter(s.Rows, SessionsView.FilterAll).Count == 2);
            Check("排序：按输入 token 降序时第一条是量大的那条", SessionsView.Sort(s.Rows, 1)[0].In == 5161243);
            Check("排序：解码速度降序（未知排最后）", SessionsView.Sort(s.Rows, 4)[0].DecodeTps == 195.7);
            Check("排序：缓存命中率升序时未知排最后", SessionsView.Sort(s.Rows, 2)[0].HitPercent == 96.2);
            SessionsView.AttachBars(s.Rows);
            Check("条形：最大者为 100，未知/零不越界", s.Rows[0].TokenBar == 100 && s.Rows[1].TokenBar == 0);
            Check("汇总：总量与加权命中率/速度", s.TotalIn == 5161243 && s.TotalOut == 68886 && s.TotalCacheRead == 4964096 && Math.Abs(s.TotalHitPercent - 97.1) < 0.001 && Math.Abs(s.TotalDecodeTps - 108.6) < 0.001);

            SessionsSnapshot fail = SessionsMarkers.Parse("SESSIONS_FAIL 没有可读的会话投影（dsh 未初始化）");
            Check("失败行：Ok=false 且带原因", !fail.Ok && fail.FailReason.Contains("没有可读的会话投影") && fail.Rows.Count == 0);

            Check("空输入 / null → 空快照（不抛）", SessionsMarkers.Parse("").Rows.Count == 0 && SessionsMarkers.Parse(null).Rows.Count == 0);
            Check("脏数据：无法解析的行被跳过，其它行照常", SessionsMarkers.Parse("随便一行垃圾\nSESSIONS_OK 1\nSESSION onlyid\nSESSION x1 turns=2\nSESSIONS_TOTAL in=oops").Rows.Count == 2);
            Check("脏数据：数值非法 → 回退（不抛、不假装）", SessionsMarkers.Parse("SESSION x1 turns=abc hit=zzz blank=9").Rows[0].Turns == 0 && SessionsMarkers.Parse("SESSION x1 hit=zzz").Rows[0].HitPercent < 0);
            Check("CRLF 与前后空白也能解析", SessionsMarkers.Parse("  SESSIONS_OK 1  \r\nSESSION  a1  turns=3  \r\n").Rows[0].Turns == 3);
            Check("仅 SESSIONS_OK 时 Ok=true 且无行", SessionsMarkers.Parse("SESSIONS_OK 0").Ok && SessionsMarkers.Parse("SESSIONS_OK 0").Rows.Count == 0);

            Console.WriteLine();
            string prof = "PROFILES_OK 2\nPROFILE web form=web bundles=3 thirdparty=1\nBUNDLE web @deepseek-ai/dsh-base official\nBUNDLE web @deepseek-ai/dsh-web-app official\nBUNDLE web example-search-plugin thirdparty version=0.1.0\nDISABLED web example-search-plugin\nPROFILE bare form=unknown bundles=0 thirdparty=0";
            ProfilesSnapshot ps = ProfilesMarkers.Parse(prof);
            Check("profiles：解析成功与计数", ps.Ok && ps.Count == 2 && ps.Profiles.Count == 2);
            Check("profiles：形态文案与色号", ps.Profiles[0].FormText.Contains("Web") && ps.Profiles[0].FormKind == 0 && ps.Profiles[1].FormKind == 3);
            Check("profiles：组合包与第三方计数", ps.Profiles[0].Bundles == 3 && ps.Profiles[0].ThirdParty == 1 && ps.Profiles[0].CountText.Contains("第三方 1"));
            Check("profiles：插件归属（官方 2 / 第三方 1）", ps.Profiles[0].Items.Count == 3 && ps.Profiles[0].Items[2].KindText == "第三方" && ps.Profiles[0].Items[0].Official);
            Check("profiles：失败与空输入不抛", !ProfilesMarkers.Parse("PROFILES_FAIL 找不到 profiles 目录").Ok && ProfilesMarkers.Parse("").Profiles.Count == 0);
            Check("profiles：脏行跳过", ProfilesMarkers.Parse("garbage\nPROFILES_OK 1\nPROFILE x form=web\nBUNDLE nobody a official").Profiles.Count == 1);
            StatusSnapshot st = StatusMarkers.Parse("STATUS_UP\nSTATUS_PID 16748\nSTATUS_START 2026-09-28 11:53:14\nSTATUS_UPTIME 6 小时 3 分");
            Check("status：运行中 + PID/启动/运行时长", st.Ok && st.State == 0 && st.StateText == "运行中" && st.Pid == "16748" && st.Start == "2026-09-28 11:53:14" && st.Uptime == "6 小时 3 分");
            Check("status：启动中 / 未运行", StatusMarkers.Parse("STATUS_STARTING").State == 1 && StatusMarkers.Parse("STATUS_DOWN").State == 2);
            Check("status：未识别标记原样收进 Extras（对未来版本友好）", StatusMarkers.Parse("STATUS_UP\nSTATUS_FUTURE 42").Extras.Count == 1 && StatusMarkers.Parse("STATUS_UP\nSTATUS_FUTURE 42").Extras[0].Value == "42");
            Check("status：空输入不抛且 !Ok", !StatusMarkers.Parse("").Ok && !StatusMarkers.Parse(null).Ok);
            Check("status：含空格的值整行保留（启动时间）", StatusMarkers.Parse("STATUS_START 2026-09-28 11:53:14").Start.Split(' ').Length == 2);
            Check("profiles：BUNDLE 带版本号（version= → v0.1.0）", ps.Profiles[0].Items[2].Version == "0.1.0" && ps.Profiles[0].Items[2].VersionText == "v0.1.0");
            Check("profiles：DISABLED 行归到对应 profile 且文案可读", ps.Profiles[0].Disabled.Count == 1 && ps.Profiles[0].Disabled[0] == "example-search-plugin" && ps.Profiles[0].DisabledText.Contains("已隔离 1 项"));
            Check("profiles：无版本号的条目 → 空串（不假装有版本）", ps.Profiles[0].Items[0].Version == "" && ps.Profiles[0].Items[0].VersionText == "");
            Check("profiles：DISABLED 指向不存在的 profile 时忽略（不抛）", ProfilesMarkers.Parse("PROFILES_OK 0\nDISABLED nobody x").Profiles.Count == 0);
            List<ConfigItem> cfg = ConfigMarkers.Parse("CONFIGGET_OK\nCONFIG lang auto\nCONFIG keep_backups 10\nCONFIG ws \nCONFIG dsh_versions 1.11.0,1.10.0");
            Check("配置：解析 4 项且空值保留", cfg.Count == 4 && cfg[0].Key == "lang" && cfg[0].Value == "auto" && cfg[2].Value == "");
            Check("配置：说明文案与只读/开关判定", cfg[0].Desc.Contains("界面语言") && cfg[3].ReadOnly && cfg[0].IsSwitch == false && ConfigMarkers.Parse("CONFIG check_update on")[0].IsSwitch);
            Check("配置：脏行不炸（裸 CONFIG 不算条目）", ConfigMarkers.Parse("garbage\nCONFIG\nCONFIG x").Count == 1);
            Check("配置：ui_parallel / scan_children 是开关（2026-10-02 补上 ✗ 原来当自由文本 ✗）",
                ConfigMarkers.Parse("CONFIG ui_parallel off")[0].IsSwitch && ConfigMarkers.Parse("CONFIG scan_children off")[0].IsSwitch);
            // ★ DeepSeek 余额（2026-10-02 用户要求"余额检测（自己填写 key）" ✓✓）
            BalanceSummary bb = BalanceMarkers.Parse("BALANCE_STATE bound\nBALANCE_CURRENCY CNY\nBALANCE_TOPUP 100.00\nBALANCE_GRANTED 10.00\nBALANCE_TOTAL 110.00\nBALANCE_NOTE 账户当前不可用");
            Check("余额：绑定时充值/赠送/总计/币种逐字读到 ✓", bb.Ok && bb.Bound && bb.Topup == "100.00" && bb.Granted == "10.00" && bb.Total == "110.00" && bb.Currency == "CNY" && bb.Note.Contains("不可用"));
            Check("余额：未绑定 → Bound=false（GUI 整卡隐藏 ✓ 用户要求 ✓）", BalanceMarkers.Parse("BALANCE_STATE unbound").Ok && !BalanceMarkers.Parse("BALANCE_STATE unbound").Bound);
            Check("余额：取不到 → Unavailable 带原因 ✗ 绝不冒充数字 ✓✓", BalanceMarkers.Parse("BALANCE_STATE bound\nBALANCE_UNAVAILABLE 网络不通").Unavailable.Contains("网络不通") && !BalanceMarkers.Parse("BALANCE_STATE bound\nBALANCE_UNAVAILABLE 网络不通").HasNumbers);
            Check("余额：空输入不抛", !BalanceMarkers.Parse("").Ok && !BalanceMarkers.Parse(null).Ok);
            List<BackupItem> bi = BackupItems.Parse("BACKUP_LIST_OK 2\n/path/a\nBACKUP_ITEM dsh-data-1 Manual 16 2026-09-28 19:01:30\nBACKUP_ITEM dsh-data-2 Auto 2048 2026-09-27 10:00:00");
            Check("备份：解析 2 条（类型/大小/时间）", bi.Count == 2 && bi[0].KindText == "手动" && bi[0].Bytes == 16 && bi[1].KindText == "自动" && bi[1].Time == "2026-09-27 10:00:00");
            Check("备份：人读大小", bi[1].SizeText == "2.0K" && bi[0].SizeText == "16");
            DoctorSummary ds = SummaryMarkers.ParseDoctor("DOCTOR_WARN 1\n[OK] System Linux\n[WARN] System npm 不可用\n[ERROR] Harness dsh 未安装");
            Check("体检：计数以条目行为准（1 通过 / 1 提醒 / 1 错误）", ds.Ok && ds.Error == 1 && ds.Warn == 1 && ds.Pass == 1 && ds.Headline.Contains("1 个错误"));
            Check("体检：条目按级别归类", ds.ErrorLines.Count == 1 && ds.WarnLines.Count == 1 && ds.ErrorLines[0].Contains("dsh 未安装"));
            Check("体检：空输入不抛", !SummaryMarkers.ParseDoctor("").Ok && SummaryMarkers.ParseDoctor(null).Error == 0);
            BackupSummary bs = SummaryMarkers.ParseBackups("BACKUP_LIST_OK 3\nBACKUP_ITEM dsh-data-x Manual 10 2026-09-28 19:00:00");
            Check("备份摘要：数量", bs.Ok && bs.Count == 3);   // the Latest field was removed as dead state; assert the count only
            // ★ overview 聚合（2026-10-06 性能 #3）：一份输出四个解析器各取所需（前缀扫描、互不干扰 ✓）
            string ov = "STATUS_UP\nSTATUS_PID 42\n" +
                "SESSIONS_OK 1\nSESSION x1 turns=3\nSESSIONS_TOTAL in=10 out=2\n" +
                "BACKUP_LIST_OK 0\n" +
                "PROFILES_OK 1\nPROFILE web form=web bundles=1 thirdparty=0\nBUNDLE web @deepseek-ai/dsh-web-app official";
            Check("overview：status/sessions/backups/profiles 四个解析器各取所需",
                StatusMarkers.Parse(ov).Pid == "42" &&
                SessionsMarkers.Parse(ov).Rows.Count == 1 &&
                SummaryMarkers.ParseBackups(ov).Count == 0 &&
                ProfilesMarkers.Parse(ov).Count == 1);

            // —— 看板第一批（2026-10-08 ✓✓ 规格 §11.1/§11.5 验收⑤ ✓）——
            string b1 = "SESSIONS_OK 3\nSESSIONS_SOURCE disk\n" +
                "SESSWIN_META days=7 from=2026-10-02 to=2026-10-09 tz=China%20Standard%20Time truth=0 level=global source=disk scanned=283 eligible=12 unknown_last=0\n" +
                "SESSAGG_TOTAL scope=store sessions=283 nonblank=180 uncached=1000 cacheRead=2000 cacheWrite=300 output=400 first_day=2026-09-01 last_day=2026-10-08 truth=0\n" +
                "SESSAGG_SESSION aaa111 bucket=own turns=3 steps=9 uncached=100 cacheRead=200 cacheWrite=30 output=40 children=unknown depth=unknown\n" +
                "SESSAGG_SESSION bbb222 bucket=own turns=unknown steps=unknown uncached=unknown cacheRead=unknown cacheWrite=unknown output=unknown children=unknown depth=unknown\n";
            SessionsSnapshot w = SessionsMarkers.Parse(b1);
            Check("看板：SESSWIN_META 全字段（含 tz %20 解码）", w.HasWinMeta && w.WinDays == 7 && w.WinFrom == "2026-10-02" && w.WinTo == "2026-10-09"
                && w.WinTz == "China Standard Time" && w.WinTruth0 && w.WinScanned == 283 && w.WinEligible == 12 && w.WinUnknownLast == 0);
            Check("看板：SESSAGG_TOTAL 全字段", w.HasAggTotal && w.AggSessions == 283 && w.AggNonBlank == 180 && w.AggUncached == 1000
                && w.AggCacheRead == 2000 && w.AggCacheWrite == 300 && w.AggOutput == 400 && w.AggFirstDay == "2026-09-01" && w.AggLastDay == "2026-10-08");
            Check("看板：SESSAGG_SESSION 已知行（own ✓ In=uncached+cacheRead ✓）", w.AggRows.Count == 2 && w.AggRows[0].Id == "aaa111" && w.AggRows[0].Bucket == "own"
                && w.AggRows[0].HasStats && w.AggRows[0].HasTokens && w.AggRows[0].Turns == 3 && w.AggRows[0].Steps == 9 && w.AggRows[0].In == 300 && w.AggRows[0].Output == 40);
            Check("看板：unknown 数值 → HasStats/HasTokens=false ✗ 绝不假装 0 ✓✓",
                !w.AggRows[1].HasStats && !w.AggRows[1].HasTokens && w.AggRows[1].Turns == 0 && w.AggRows[1].Uncached == 0);
            Check("看板：AggIdSet = eligible 集合（含 aaa111/bbb222）", w.AggIdSet().Contains("aaa111") && w.AggIdSet().Contains("bbb222") && w.AggIdSet().Count == 2);
            SessionsSnapshot tot = SessionsMarkers.Parse("SESSIONS_OK 1\nSESSWIN_META days=unknown from=- to=- tz=Asia/Hong_Kong truth=0 level=global source=snapshot+disk scanned=5 eligible=5 unknown_last=2");
            Check("看板：days=unknown → WinDays=-1（总计档内存哨兵 ✓ from/to=- ✓ unknown_last=2 ✓ tz 斜杠不转义原样过 ✓）",
                tot.HasWinMeta && tot.WinDays == -1 && tot.WinFrom == "-" && tot.WinTo == "-" && tot.WinTz == "Asia/Hong_Kong" && tot.WinUnknownLast == 2);
            Check("看板：老 CLI（无新标记行）→ HasWinMeta=false + AggIdSet()=null（走不过滤回退 ✓✓）",
                !SessionsMarkers.Parse("SESSIONS_OK 1\nSESSION x1 turns=3").HasWinMeta && SessionsMarkers.Parse("SESSIONS_OK 1").AggIdSet() == null);
            Check("看板：总计档也有 eligible id 集（days=unknown 时 CLI 全量打 SESSAGG_SESSION ✓ 窗口卡==总计卡 ✓）",
                SessionsMarkers.Parse("SESSIONS_OK 1\nSESSWIN_META days=unknown from=- to=- tz=- truth=0 level=global source=disk scanned=1 eligible=1 unknown_last=0\nSESSAGG_SESSION z9 bucket=own turns=1 steps=1 uncached=1 cacheRead=1 cacheWrite=1 output=1 children=unknown depth=unknown").AggIdSet().Count == 1);
            Check("看板：C.4 脚注逐字符 == 规格 §4.2 :342（一字不许改 ✓✓ LogicTests 是唯一能拦住改字的地方 ✓）",
                SessionsMarkers.TruthFootnote == "本口径按会话最后活动时间筛选，用量为整会话累计，非窗口内增量。");

            // —— 看板第二批（2026-10-09 ✓✓ 规格 §11.6 ✓✓）——
            string b2 = b1 +
                "SESSTIME_SESSION aaa111 llmMs=1000 toolMs=500 ttftMs=300 decodeMs=200\n" +
                "SESSTIME_SESSION bbb222 llmMs=unknown toolMs=unknown ttftMs=unknown decodeMs=unknown\n" +
                "SESSCTX_SESSION aaa111 system=1000 tools=2000 message=3000\n" +
                "SESSCTX_SESSION bbb222 system=unknown tools=unknown message=unknown\n";
            SessionsSnapshot w2 = SessionsMarkers.Parse(b2);
            Check("看板2：SESSTIME_SESSION 已知行（四件全读出 ✓ Has ✓）",
                w2.TimeRows.Count == 2 && w2.TimeRows[0].Id == "aaa111" && w2.TimeRows[0].Has
                && w2.TimeRows[0].LlmMs == 1000 && w2.TimeRows[0].ToolMs == 500 && w2.TimeRows[0].TtftMs == 300 && w2.TimeRows[0].DecodeMs == 200);
            Check("看板2：SESSTIME_SESSION unknown → Has=false ✗ 绝不假装 0 ✓✓",
                !w2.TimeRows[1].Has && w2.TimeRows[1].LlmMs == 0);
            Check("看板2：SESSCTX_SESSION 已知行（三桶 ✓ message 一桶不拆 D3 ✓）",
                w2.CtxRows.Count == 2 && w2.CtxRows[0].Has && w2.CtxRows[0].System == 1000 && w2.CtxRows[0].Tools == 2000 && w2.CtxRows[0].Message == 3000);
            Check("看板2：SESSCTX_SESSION unknown → Has=false ✓", !w2.CtxRows[1].Has && w2.CtxRows[1].System == 0);
            Check("看板2：老 CLI（无新行）→ TimeRows/CtxRows 为空 → 卡片如实显示「未提供」✗ 不画空图 ✓",
                SessionsMarkers.Parse("SESSIONS_OK 1\nSESSAGG_SESSION z9 bucket=own turns=1 steps=1 uncached=1 cacheRead=1 cacheWrite=1 output=1 children=unknown depth=unknown").TimeRows.Count == 0
                && SessionsMarkers.Parse("SESSIONS_OK 1").CtxRows.Count == 0);
            // 耗时分解纯函数（llm 其他 = llmMs−ttft−decode ≥0 ✓ 勘察 §5 恒等式 ✓；unknown 行不计入 ✓）
            int kn;
            long[] ts = SessionsView.TimeSplit(w2.TimeRows, out kn);
            Check("看板2：TimeSplit 四桶（ttft=300 / decode=200 / 其他=1000−300−200=500 / tool=500）+ known=1（unknown 行不计 ✓）",
                kn == 1 && ts[0] == 300 && ts[1] == 200 && ts[2] == 500 && ts[3] == 500);
            List<SessTimeRow> neg = new List<SessTimeRow>();
            neg.Add(new SessTimeRow { Id = "x", Has = true, LlmMs = 100, TtftMs = 80, DecodeMs = 60, ToolMs = 7 });
            long[] tn = SessionsView.TimeSplit(neg, out kn);
            Check("看板2：TimeSplit 防御钳（其他 = max(0, −40) = 0 ✗ 不反向伪造 ✓）", kn == 1 && tn[2] == 0 && tn[0] == 80 && tn[1] == 60 && tn[3] == 7);
            long[] cx = SessionsView.CtxTotals(w2.CtxRows, out kn);
            Check("看板2：CtxTotals 三桶（1000/2000/3000）+ known=1", kn == 1 && cx[0] == 1000 && cx[1] == 2000 && cx[2] == 3000);
            // 本地日归桶纯函数：本地时间 → UTC ISO → 回到同一本地日 ✓（时区无关的确定性断言 ✓）；垃圾 → null ✓
            System.DateTime loc = new System.DateTime(2026, 3, 5, 8, 30, 0, System.DateTimeKind.Local);
            string iso = loc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffK", System.Globalization.CultureInfo.InvariantCulture);
            Check("看板2：LocalDayOfIso 本地往返 == 2026-03-05（图1/图3/热力图同一归日口径 ✓）", SessionsView.LocalDayOfIso(iso) == "2026-03-05");
            Check("看板2：LocalDayOfIso 垃圾/空 → null（不猜日期 ✓）", SessionsView.LocalDayOfIso("not-a-date") == null && SessionsView.LocalDayOfIso("") == null && SessionsView.LocalDayOfIso(null) == null);
            // 口径文案逐字钉死（规格 §11.6-C ✓✓ 一字不许改 ✓✓）
            Check("看板2：耗时分解标题逐字", SessionsMarkers.TimeTitle == "耗时分解（窗口内会话 · 整会话累计）");
            Check("看板2：耗时分解脚注逐字（ttft/decode 是 llmMs 子集 + tool 不相交 + 缺不计 ✓）",
                SessionsMarkers.TimeFootnote == "llmMs = 等首 token + 流式解码 + llm 其他（dsh 源码口径：ttft/decode 是 llmMs 的子集）；工具执行与 llmMs 不相交。缺 sessionStats 的会话不计入。");
            Check("看板2：Token 分类标题逐字（当前快照合计 ✓）", SessionsMarkers.CtxTitle == "上下文构成 · Token 分类（窗口内会话 · 当前快照合计）");
            Check("看板2：Token 分类脚注逐字（启发式折算必标「估算」✓ 非计费 ✓ 不拆 ✓）",
                SessionsMarkers.CtxFootnote == "上下文构成是 dsh 启发式折算（估算）口径（4 字符 ≈ 1 token），取各会话当前上下文快照，非提供方计费数字，也非整会话累计；messageTokens 一桶不拆。缺 contextBreakdown 的会话不计入。");
            Check("看板2：热力图标题模板逐字", SessionsMarkers.HeatTitle == "近 {0} 天会话最后活动日 · 会话数（本地日）");
            Check("看板2：热力图脚注模板逐字（最后活动日 ≠ 逐日活跃轨迹 ✓ 逐日真值须读原始日志 ✓）",
                SessionsMarkers.HeatFootnote == "按会话最后活动时间归本地日；投影只含最后活动时间，这不是逐日活跃轨迹（逐日真值须读原始日志）；缺失 {0} 个不计入。");

            // —— 看板第三批（2026-10-09 ✓✓ 规格 §11.7 ✓✓）——
            string b3 = "SESSIONS_OK 2\n" +
                "SESSWIN_META days=7 from=2026-10-02 to=2026-10-09 tz=Asia%2FHong_Kong truth=0 level=parents_sub source=disk scanned=283 eligible=10 unknown_last=1 lineage=ok fork_sessions=18\n" +
                "SESSAGG_SESSION p1 bucket=own turns=2 steps=4 uncached=100 cacheRead=50 cacheWrite=10 output=20 children=2 depth=0\n" +
                "SESSAGG_SESSION p1 bucket=sub turns=6 steps=12 uncached=300 cacheRead=150 cacheWrite=30 output=60 children=2 depth=0 members=3 sub_unknown=1 sub_unknown_stats=1\n" +
                "SESSAGG_SESSION solo bucket=own turns=1 steps=1 uncached=7 cacheRead=3 cacheWrite=1 output=2 children=0 depth=0\n" +
                "SESSAGG_SESSION solo bucket=sub turns=1 steps=1 uncached=7 cacheRead=3 cacheWrite=1 output=2 children=0 depth=0 members=1 sub_unknown=0 sub_unknown_stats=0\n";
            SessionsSnapshot w3 = SessionsMarkers.Parse(b3);
            Check("看板3：SESSWIN_META 三新键（level=parents_sub ✓ lineage=ok ✓ fork_sessions=18 ✓ tz %2F 解码 ✓）",
                w3.WinLevel == "parents_sub" && w3.LineageOk && w3.ForkSessions == 18 && w3.WinTz == "Asia/Hong_Kong");
            Check("看板3：own/sub 分流（AggRows 只装 own=2 ✓ SubRows=2 ✓ SubById 可双查 ✓）",
                w3.AggRows.Count == 2 && w3.SubRows.Count == 2 && w3.SubById.Count == 2 && w3.SubById.ContainsKey("p1") && w3.SubById.ContainsKey("solo"));
            Check("看板3：sub 行新键全读出（children=2 depth=0 members=3 sub_unknown=1 ✓）",
                w3.SubById["p1"].HasChildren && w3.SubById["p1"].Children == 2 && w3.SubById["p1"].HasDepth && w3.SubById["p1"].Depth == 0
                && w3.SubById["p1"].Members == 3 && w3.SubById["p1"].SubUnknown == 1 && w3.SubById["p1"].SubUnknownStats == 1);
            Check("看板3：AggIdSet 仍 = eligible own 集（sub 行不污染 ✓✓）",
                w3.AggIdSet().Count == 2 && w3.AggIdSet().Contains("solo"));
            Check("看板3：血缘键 unknown → Has=false ✗ 不假装 0 ✓✓",
                !w.AggRows[0].HasChildren && !w.AggRows[0].HasDepth);
            SessionsSnapshot w3old = SessionsMarkers.Parse("SESSIONS_OK 1\nSESSWIN_META days=7 from=2026-10-02 to=2026-10-09 tz=- truth=0 level=global source=disk scanned=1 eligible=1 unknown_last=0\nSESSAGG_SESSION z9 bucket=own turns=1 steps=1 uncached=1 cacheRead=1 cacheWrite=1 output=1 children=unknown depth=unknown");
            Check("看板3：老 CLI → lineage=none（LineageOk=false ✓ WinLevel 仍读出 ✓ fork=-1=unknown ✓ SubRows 空 ✓）",
                !w3old.LineageOk && w3old.WinLevel == "global" && w3old.ForkSessions == -1 && w3old.SubRows.Count == 0);
            Check("看板3：fork_sessions=unknown 字面值 → -1（✗ 不假装 0 ✓✓）",
                SessionsMarkers.Parse("SESSWIN_META days=7 from=- to=- tz=- truth=0 level=global source=disk scanned=0 eligible=0 unknown_last=0 lineage=none fork_sessions=unknown").ForkSessions == -1);
            // B.5 差值行文案逐字钉死（规格 §B.5 :285-293 ✓✓ 一字不许改 ✓✓）；D-7：0 照显 ✓ unknown 不装 0 ✓
            Check("看板3：ForkDiffText(n=18) 逐字 == §B.5 模板 ✓✓",
                SessionsMarkers.ForkDiffText(18) == "「全局」与「父会话+子代理」相差 18 个会话 = 它们的 parentSession 指向别的会话，但 origin 不是 subagent（dsh 的 fork 复制出来的会话）。它们算血缘后代（用量计入父的「本会话+子代理」），但不是子代理。");
            // ★ 弃用 Flyout 改内联展开块（2026-10-11 ✓✓）：上面那条按整串比对 —— **换行处吞字查不出来** ✗
            //   （弹层那次丢的正是「话，但」✓；截断串与整串不等，但定位不到消失的是哪几个字 ✓）
            //   这里把**换行点两侧的原文**再钉一次 ✓ 内联块与弹层用同一份文案 ✓ 布局改动 ✗ 不许动文案 ✓
            Check("看板3：ForkDiffText(18) 含换行点原文「会话，但 origin 不是 subagent」（吞字回归钉 ✓✓）",
                SessionsMarkers.ForkDiffText(18).IndexOf("会话，但 origin 不是 subagent", StringComparison.Ordinal) >= 0
                && SessionsMarkers.ForkDiffText(18).IndexOf("话，但", StringComparison.Ordinal) >= 0);
            Check("看板3：ForkDiffText(0) = 「相差 0 个会话」（0 是真实值 ✓ D-7 ✓）",
                SessionsMarkers.ForkDiffText(0).IndexOf("相差 0 个会话") >= 0);
            Check("看板3：ForkDiffText(-1) = 「相差 unknown 个会话」（✗ 不打 0 ✓✓）",
                SessionsMarkers.ForkDiffText(-1).IndexOf("相差 unknown 个会话") >= 0);
            // 含子代理小字行（B.4 ✓ §11.7-E-3 ✓✓ 唯一出处 SubLineText ✓✓）
            Check("看板3：SubLineText(null) = 空串（该行不显示 ✓）", SessionsMarkers.SubLineText(null) == "");
            Check("看板3：SubLineText(sub 有子孙+未知成员) = 「含子代理：输入 450 · 输出 60（其中 1 个会话用量未知，未计入）」",
                SessionsMarkers.SubLineText(w3.SubById["p1"]) == "含子代理：输入 450 · 输出 60（其中 1 个会话用量未知，未计入）");
            Check("看板3：SubLineText(children=0) = 同数 + 「（无子会话 ⇒ 两口径相等）」（B.4 ✓✓）",
                SessionsMarkers.SubLineText(w3.SubById["solo"]) == "含子代理：输入 10 · 输出 2（无子会话 ⇒ 两口径相等）");
            SessAggRow subUnk = new SessAggRow(); subUnk.Bucket = "sub";
            Check("看板3：SubLineText(token 未知) 如实「用量未知」（不含自我辩解措辞 ✓）",
                SessionsMarkers.SubLineText(subUnk).IndexOf("用量未知") >= 0);
            // D4 自定义区间本地校验（§11.7-D/E-4 ✓✓ ✗ 不猜 ✗ 不补端点 ✓）
            string cf, ct, ce;
            Check("看板3：TryParseCustomRange 空格分隔通过（from/to 原样回 ✓）",
                SessionsMarkers.TryParseCustomRange("2026-10-01 2026-10-08", out cf, out ct, out ce) && cf == "2026-10-01" && ct == "2026-10-08");
            Check("看板3：TryParseCustomRange 逗号/全角逗号也认 ✓",
                SessionsMarkers.TryParseCustomRange("2026-10-01，2026-10-08", out cf, out ct, out ce) && ct == "2026-10-08");
            Check("看板3：TryParseCustomRange 倒序拒绝（err 中文 ✗ 不猜 ✓）",
                !SessionsMarkers.TryParseCustomRange("2026-10-08 2026-10-01", out cf, out ct, out ce) && ce.Length > 0);
            Check("看板3：TryParseCustomRange 坏格式/单日期/空 全拒绝 ✗ 不默认补端点 ✓✓",
                !SessionsMarkers.TryParseCustomRange("2026/10/01 2026-10-08", out cf, out ct, out ce)
                && !SessionsMarkers.TryParseCustomRange("2026-10-01", out cf, out ct, out ce)
                && !SessionsMarkers.TryParseCustomRange("", out cf, out ct, out ce));

            // ============ 看板体验修复（2026-10-11 ✓✓）：BoardMetrics 纯函数 ============
            // —— LevelSum：三档对比表的一行（Σ bucket=own ✓ HasTokens=false 不进求和 ✗ 不假装 0 ✓）——
            SessionsSnapshot lv = new SessionsSnapshot();
            lv.WinEligible = 3;
            lv.AggRows.Add(new SessAggRow { Id = "a", Bucket = "own", HasTokens = true, HasStats = true, Uncached = 100, CacheRead = 50, CacheWrite = 7, Output = 40, Turns = 2 });
            lv.AggRows.Add(new SessAggRow { Id = "b", Bucket = "own", HasTokens = true, HasStats = true, Uncached = 10, CacheRead = 0, CacheWrite = 0, Output = 5, Turns = 1 });
            lv.AggRows.Add(new SessAggRow { Id = "c", Bucket = "own", HasTokens = false, HasStats = false, Turns = 9 });
            lv.AggRows.Add(new SessAggRow { Id = "a", Bucket = "sub", HasTokens = true, HasStats = true, Uncached = 999, CacheRead = 999, CacheWrite = 999, Output = 999, Turns = 99 });
            BoardMetrics.LevelSum sm = BoardMetrics.SumOwnRows(lv);
            Check("体验修复·三档表：LevelSum 只加 bucket=own 且 HasTokens 的行（sub 行/未知行不进 ✓✓ 会话数=WinEligible ✓）",
                sm.Sessions == 3 && sm.Uncached == 110 && sm.CacheRead == 50 && sm.CacheWrite == 7 && sm.Output == 45 && sm.In == 160);
            Check("体验修复·三档表：turns 只加 HasStats 的行；token 未知行数单列如实标注（✗ 不假装 0 ✓）",
                sm.Turns == 3 && sm.TokUnknown == 1);
            Check("体验修复·三档表：null/空快照 → 全 0（✗ 不猜 ✓「未提供」文案归界面层 ✓）",
                BoardMetrics.SumOwnRows(null).Sessions == 0 && BoardMetrics.SumOwnRows(new SessionsSnapshot()).In == 0);

            // —— Top5：输入(未命中+缓存)降序 · 只进「数值完整」(HasTokens && HasStats)的会话 · 并列按 id 字典序（确定性 ✓）——
            SessionsSnapshot tp = new SessionsSnapshot();
            tp.Rows.Add(new SessionRow { Id = "s-big", Title = "大会话" });
            tp.Rows.Add(new SessionRow { Id = "s-tie-a" });   // 无标题 → TitleText 人话兜底 ✓
            tp.Rows.Add(new SessionRow { Id = "s-tie-b", Title = "并列B" });
            tp.AggRows.Add(new SessAggRow { Id = "s-big", Bucket = "own", HasTokens = true, HasStats = true, Uncached = 900, CacheRead = 100, Output = 901, Turns = 5 });
            tp.AggRows.Add(new SessAggRow { Id = "s-tie-b", Bucket = "own", HasTokens = true, HasStats = true, Uncached = 500, CacheRead = 0, Output = 60, Turns = 1 });
            tp.AggRows.Add(new SessAggRow { Id = "s-tie-a", Bucket = "own", HasTokens = true, HasStats = true, Uncached = 500, CacheRead = 0, Output = 70, Turns = 2 });
            tp.AggRows.Add(new SessAggRow { Id = "s-mid", Bucket = "own", HasTokens = true, HasStats = true, Uncached = 200, CacheRead = 0, Output = 21, Turns = 1 });
            tp.AggRows.Add(new SessAggRow { Id = "s-unknown", Bucket = "own", HasTokens = false, HasStats = false, Uncached = 99999, Output = 99999, Turns = 99 });
            tp.AggRows.Add(new SessAggRow { Id = "s-six", Bucket = "own", HasTokens = true, HasStats = true, Uncached = 100, CacheRead = 0, Output = 11, Turns = 1 });
            tp.AggRows.Add(new SessAggRow { Id = "s-seven", Bucket = "own", HasTokens = true, HasStats = true, Uncached = 50, CacheRead = 0, Output = 6, Turns = 1 });
            SessAggRow tpSub = new SessAggRow { Id = "s-big", Bucket = "sub", HasTokens = true, HasStats = true, Uncached = 1, CacheRead = 1, Output = 1, Turns = 1 };
            tp.AggRows.Add(tpSub);
            List<BoardMetrics.TopRow> top = BoardMetrics.Top5ByInput(tp);
            Check("体验修复·Top5：输入降序取 5；token/轮次未知与 sub 行不进榜 ✓✓",
                top.Count == 5 && top[0].Id == "s-big" && top[0].In == 1000 && top[3].Id == "s-mid" && top[4].Id == "s-six");
            Check("体验修复·Top5：并列按会话 id 字典序（Ordinal ✓ 两次运行同序 ✓）",
                top[1].Id == "s-tie-a" && top[2].Id == "s-tie-b");
            Check("体验修复·Top5：行带 输出/轮次（表格列数据源 ✓）",
                top[0].Output == 901 && top[0].Turns == 5 && top[1].Output == 70 && top[1].Turns == 2);
            Check("体验修复·Top5：标题取 SESSION 行 TitleText（无标题 → 人话兜底 ✓）；SESSION 行缺失 → 短 id 兜底（不空白 ✗ 不猜 ✓）",
                top[0].Title == "大会话" && top[1].Title == "（未命名会话）" && top[4].Title == "s-six");
            Check("体验修复·Top5：null/空快照 → 空榜 ✓",
                BoardMetrics.Top5ByInput(null).Count == 0 && BoardMetrics.Top5ByInput(new SessionsSnapshot()).Count == 0);

            // —— ChartSignature：图表重画签名（同输入同签名 = 「值未变 → 不重画」的依据 ✓✓）——
            DateTime day1 = new DateTime(2026, 10, 11);
            string sigA = BoardMetrics.ChartSignature(SessionsMarkers.Parse(real), 7, null, day1);
            Check("体验修复·签名：同一份数据两次解析 → 签名相同（✓✓ 纯函数短路重画的依据 ✓✓）",
                sigA == BoardMetrics.ChartSignature(SessionsMarkers.Parse(real), 7, null, day1));
            Check("体验修复·签名：仅 chartDays 变 → 签名变（切图表范围档要重画 ✓）",
                BoardMetrics.ChartSignature(SessionsMarkers.Parse(real), 14, null, day1) != sigA);
            Check("体验修复·签名：跨天（today 变）→ 签名变（按日分桶要重画 ✓ 且不读时钟、today 由调用方传 ✓）",
                BoardMetrics.ChartSignature(SessionsMarkers.Parse(real), 7, null, day1.AddDays(1)) != sigA);
            Check("体验修复·签名：行内输入 token 变 1 → 签名变 ✓",
                BoardMetrics.ChartSignature(SessionsMarkers.Parse(real.Replace("in=5161243", "in=5161244")), 7, null, day1) != sigA);
            Check("体验修复·签名：命中率变 → 签名变（分布图输入 ✓）",
                BoardMetrics.ChartSignature(SessionsMarkers.Parse(real.Replace("hit=96.2", "hit=95.2")), 7, null, day1) != sigA);
            Check("体验修复·签名：最后活动时间变 → 签名变（热力图输入 ✓）",
                BoardMetrics.ChartSignature(SessionsMarkers.Parse(real.Replace("last=2026-09-23T01:48:28Z", "last=2026-09-24T01:48:28Z")), 7, null, day1) != sigA);
            Check("体验修复·签名：体检计数变 → 签名变（体检分布图输入 ✓）",
                BoardMetrics.ChartSignature(SessionsMarkers.Parse(real), 7, new DoctorSummary { Ok = true, Pass = 3, Warn = 1 }, day1)
                != BoardMetrics.ChartSignature(SessionsMarkers.Parse(real), 7, new DoctorSummary { Ok = true, Pass = 3, Warn = 2 }, day1));
            SessionsSnapshot cAgg = SessionsMarkers.Parse(real);
            cAgg.AggRows.Add(new SessAggRow { Id = "new-eligible", Bucket = "own" });
            Check("体验修复·签名：eligible 集变（AggRows 增员）→ 签名变（winFilter 输入 ✓）",
                BoardMetrics.ChartSignature(cAgg, 7, null, day1) != sigA);
            SessionsSnapshot cTime = SessionsMarkers.Parse(real);
            cTime.TimeRows.Add(new SessTimeRow { Id = "x", Has = true, LlmMs = 1 });
            Check("体验修复·签名：耗时行集变 → 签名变（耗时图输入 ✓）",
                BoardMetrics.ChartSignature(cTime, 7, null, day1) != sigA);
            SessionsSnapshot cCtx = SessionsMarkers.Parse(real);
            cCtx.CtxRows.Add(new SessCtxRow { Id = "x", Has = true, System = 1 });
            Check("体验修复·签名：上下文行集变 → 签名变（Token 分类图输入 ✓）",
                BoardMetrics.ChartSignature(cCtx, 7, null, day1) != sigA);
            Check("体验修复·签名：null 快照 → 固定串（✗ 不抛 ✓）；失败快照与 null 不同（失败原因进签名 ✓）",
                BoardMetrics.ChartSignature(null, 7, null, day1) == BoardMetrics.ChartSignature(null, 7, null, day1)
                && BoardMetrics.ChartSignature(SessionsMarkers.Parse("SESSIONS_FAIL test_x"), 7, null, day1) != BoardMetrics.ChartSignature(null, 7, null, day1));

            // —— 版本显示 + 更新状态诊断（2026-10-11 ✓✓）——
            //   铁律（逐条断言 ✓）：版本号只能来自**实读** ✗ 不许写死 ✓；"取不到最新版本"✗ 不许笼统写成「未知」✗。
            Check("版本·CLI：`DSHT_VERSION 3.0.7` 逐字解析 ✓",
                VersionMarkers.ParseCliVersion("DSHT_VERSION 3.0.7") == "3.0.7");
            Check("版本·CLI：前后有其它行也认（GUI 只挑这一行 ✓）；多词 / 空值不认（✗ 不猜 ✓）",
                VersionMarkers.ParseCliVersion("noise\n  DSHT_VERSION 1.2.3-rc.1  \ntail") == "1.2.3-rc.1"
                && VersionMarkers.ParseCliVersion("DSHT_VERSION 1.2 3") == null
                && VersionMarkers.ParseCliVersion("DSHT_VERSION ") == null);
            Check("版本·CLI：v2.x 的「未知参数：version」原文 → null（旧 CLI 如实说读不到 ✓ 不编 ✗）",
                VersionMarkers.ParseCliVersion("未知参数：version\n  DeepSeek Harness Toolkit V2.7.3") == null
                && VersionMarkers.ParseCliVersion("") == null && VersionMarkers.ParseCliVersion(null) == null);
            Check("版本·JSON：只认整词 version 字段（dshVersion / versions 都不算 ✓）",
                VersionMarkers.JsonStringField("{\"dshVersion\":\"9.9.9\",\"version\":\"0.2.0\"}", "version") == "0.2.0"
                && VersionMarkers.JsonStringField("{\"versions\":{\"a\":1}}", "version") == ""
                && VersionMarkers.JsonStringField("{}", "version") == "" && VersionMarkers.JsonStringField(null, "version") == "");
            // ★★★ 2026-10-13 尾①**URL 取值三态**（真机 bug：`repository` 是对象时更新页打出字面值 `type` ✗）：
            //   ① 对象（真实 `plugin/dsh-minato-bridge/package.json` 就是这一形态 ✓）→ 取**对象内**的 url ✓
            //      ✗ 绝不许把对象的第一把**键**（`type`）当成地址 ✗ —— 这一条就是那个 bug 的回归守卫 ✓
            //   ② 字符串（npm 也允许 ✓）→ **原样** ✓（不补 .git、不改写 ✓）
            //   ③ 缺失 / 形态不对 / 对象里没有 url → **空串** ✓（上层写 `unknown` ✓ ✗ 不编 ✗）
            //   判定住在 Domain 层（`Dsht.Domain.Services.PackageJsonFields` ✓）—— CLI 与 GUI 单测**同一份** ✓
            Check("URL·① repository 是**对象** → 取 `.url`（✗ 不许拿 `type` 顶 ✗ —— 真机 bug 就是它 ✓）",
                Dsht.Domain.Services.PackageJsonFields.RepoUrl(
                    "{\"name\":\"dsh-minato-bridge\",\"version\":\"0.2.0\",\"repository\":{\"type\":\"git\",\"url\":\"https://github.com/sakanamaru/dsh-minato.git\",\"directory\":\"plugin/dsh-minato-bridge\"}}")
                    == "https://github.com/sakanamaru/dsh-minato.git"
                && Dsht.Domain.Services.PackageJsonFields.RepoUrl(
                    "{\"repository\":{\"type\":\"git\",\"url\":\"https://example.invalid/x\"}}") == "https://example.invalid/x"
                && Dsht.Domain.Services.PackageJsonFields.RepoUrl("{\"repository\":{\"type\":\"git\"}}") == "");
            Check("URL·② repository 是**字符串** → 原样（不改写 ✓ 不补后缀 ✓ 空串不造 ✓）",
                Dsht.Domain.Services.PackageJsonFields.RepoUrl("{\"repository\":\"https://github.com/a/b\"}") == "https://github.com/a/b"
                && Dsht.Domain.Services.PackageJsonFields.RepoUrl("{\"repository\":\"a/b\"}") == "a/b"
                && Dsht.Domain.Services.PackageJsonFields.RepoUrl("{\"repository\":\"\"}") == "");
            Check("URL·③ repository **缺失 / 形态不对** → 空串（上层写 unknown ✓ ✗ 不猜 ✗ 不崩 ✗）",
                Dsht.Domain.Services.PackageJsonFields.RepoUrl("{\"name\":\"x\",\"version\":\"1.0.0\"}") == ""
                && Dsht.Domain.Services.PackageJsonFields.RepoUrl("{\"repository\":123}") == ""
                && Dsht.Domain.Services.PackageJsonFields.RepoUrl("{\"repository\":[\"a\"]}") == ""
                && Dsht.Domain.Services.PackageJsonFields.RepoUrl("") == ""
                && Dsht.Domain.Services.PackageJsonFields.RepoUrl(null) == ""
                && Dsht.Domain.Services.PackageJsonFields.RepoUrl("{\"repositoryUrl\":\"https://example.invalid/wrong\"}") == "");
            Check("版本·桥接：候选路径**按序**（desktop → web → 数据根 plugin\\ ✓ 找到第一个即可 ✓）",
                VersionMarkers.BridgeCandidates("C:\\h").Count >= 3
                && VersionMarkers.BridgeCandidates("C:\\h")[0] == System.IO.Path.Combine("C:\\h", "profiles", "desktop", "node_modules", "dsh-minato-bridge", "package.json")
                && VersionMarkers.BridgeCandidates("C:\\h")[1] == System.IO.Path.Combine("C:\\h", "profiles", "web", "node_modules", "dsh-minato-bridge", "package.json"));
            Check("版本·桥接：profile 名从路径实认（认不出 → 空串 ✗ 不猜 ✓）",
                VersionMarkers.ProfileOf("C:\\h\\profiles\\web\\node_modules\\dsh-minato-bridge\\package.json") == "web"
                && VersionMarkers.ProfileOf("C:\\h\\plugin\\dsh-minato-bridge\\package.json") == "");
            // 隔离不变式：显式数据根 / DSH_HOME **绝不许回落**真实 ~\.dsh ✗（否则隔离测试里会读到真实主目录的插件 ✓ 实测踩过 ✓）
            Check("版本·桥接：显式数据根只用它自己（✗ 不回落真实主目录 ✗ 候选 5 个 ✓）",
                VersionMarkers.BridgeRoots("C:\\h").Count == 1 && VersionMarkers.BridgeRoots("C:\\h")[0] == "C:\\h"
                && VersionMarkers.BridgeCandidates("C:\\h").Count == 5);
            string envSaved = Environment.GetEnvironmentVariable("DSH_HOME");
            Environment.SetEnvironmentVariable("DSH_HOME", "C:\\fake-home");
            bool envOnlyOk = VersionMarkers.BridgeRoots(null).Count == 1 && VersionMarkers.BridgeRoots(null)[0] == "C:\\fake-home";
            Environment.SetEnvironmentVariable("DSH_HOME", envSaved);
            Check("版本·桥接：设了 DSH_HOME → 只用它（与 CLI 的 DataRoot 同口径 ✓ 隔离下不读错人 ✓）", envOnlyOk);
            Check("版本·桥接：**真机实测**——按候选扫一遍，命中项必须真的存在、且文件里真是那个 version（✗ 不编版本号 ✓）",
                BridgeRealReadOk());
            Check("版本·桥接：一个都没实读到 → 「未安装」（✗ 绝不编一个版本号 ✗）",
                VersionMarkers.DescribeBridge(null) == "未安装"
                && VersionMarkers.DescribeBridge(new List<VersionMarkers.BridgeHit>()) == "未安装");
            Check("版本·桥接：多 profile 版本不同 → 分别报；同版本 → 合并（诚实 ✓ 不取第一个就完事 ✓）",
                VersionMarkers.DescribeBridge(new List<VersionMarkers.BridgeHit> {
                    new VersionMarkers.BridgeHit { Version = "0.1.0", Profile = "desktop" },
                    new VersionMarkers.BridgeHit { Version = "0.2.0", Profile = "web" } }) == "v0.1.0（desktop） / v0.2.0（web）"
                && VersionMarkers.DescribeBridge(new List<VersionMarkers.BridgeHit> {
                    new VersionMarkers.BridgeHit { Version = "0.2.0", Profile = "desktop" },
                    new VersionMarkers.BridgeHit { Version = "0.2.0", Profile = "web" } }) == "v0.2.0（desktop、web）");
            // ★★★ 2026-10-13 尾②**前缀统一**（用户实测：同一个已装插件，页脚写 `0.2.0`、更新页写 `v0.2.0` ✗）：
            //   纪律 = 一个版本号的**显示形态只有一个出口** ✓（`VersionMarkers.TagOf` ✓）；
            //   而且**在线/离线两条取值路径**（页脚 `DescribeBridge` ✓ 更新页 `UpdateDiagnosis.VersionText` ✓）
            //   在同一个输入上必须给出**同一个形态** ✓ —— 这条断言就是钉这个不变式 ✓。
            Check("版本·★同一个版本号在**页脚**与**更新页**显示成**同一个形态**（都是 vX.Y.Z ✓ 不再一个带 v 一个不带 ✗）",
                VersionMarkers.DescribeBridge(new List<VersionMarkers.BridgeHit> {
                    new VersionMarkers.BridgeHit { Version = "0.2.0", Profile = "web" } }) == "v0.2.0（web）"
                && UpdateDiagnosis.VersionText("plugin", "up-to-date", "0.2.0", "0.2.0").StartsWith("已装 v0.2.0")
                && VersionMarkers.DescribeBridge(new List<VersionMarkers.BridgeHit> {
                    new VersionMarkers.BridgeHit { Version = "v0.2.0", Profile = "web" } }) == "v0.2.0（web）"
                && VersionMarkers.DisplayVersion("v0.2.0") == "v0.2.0"
                && VersionMarkers.DisplayVersion("") == "" && VersionMarkers.DisplayVersion(null) == null);
            Check("版本·★页脚要显示的版本号走**同一个取数函数**（空/读不到 = 不显示 ✓ ✗ 不凭空造一个 v ✗）",
                VersionMarkers.DisplayVersion("0.2.0") == VersionMarkers.TagOf("0.2.0")
                && VersionMarkers.DisplayVersion("0.2.0") == "v0.2.0"
                // 一个 package.json 都没实读到 → 页脚说「未安装」✓（✗ 不走到"给版本号加 v"那一步 ✗
                // 所以 `unknown` 这种占位串**永远不会**被当成版本号显示出来 ✓）
                && VersionMarkers.DescribeBridge(new List<VersionMarkers.BridgeHit>()) == "未安装"
                && VersionMarkers.DescribeBridge(null) == "未安装");

            Check("诊断·四态照译：已是最新 / 有更新 / 比最新还新 / 在官方页更新",
                UpdateDiagnosis.StateText("webui", "up-to-date", "1.0.0", "1.0.0") == "已是最新"
                && UpdateDiagnosis.StateText("webui", "update-available", "1.0.0", "0.9.0") == "有更新"
                && UpdateDiagnosis.StateText("webui", "newer-than-latest", "1.0.0", "1.1.0") == "比最新还新"
                && UpdateDiagnosis.StateText("desktop", "external", "unknown", "0.2.0-rc.2") == "在官方页更新");
            Check("诊断·① 真取不到（webui state=unknown）→ 「取不到最新版」✗ 不再是笼统的「未知」✗",
                UpdateDiagnosis.StateText("webui", "unknown", "unknown", "0.1.5-rc.2") == "取不到最新版");
            Check("诊断·② CLI 本来就不查（minato）→ 明说「CLI 不查最新版」（跟真取不到**分开** ✓）",
                UpdateDiagnosis.StateText("minato", "unknown", "unknown", "3.0.7") == "CLI 不查最新版");
            // ★★ 2026-10-11 **这一条改了**：插件行不再走「CLI 不查最新版」✗ —— CLI 现在**真的**拿
            //    「随包那一份」（工具箱自带 ✓ 离线 ✓）当基准做**双比对** ✓；state=unknown 只剩两种情形：
            //    ① 老 CLI（写死的 unknown ✓）② 随包缺 plugin 目录 / 随包内容读不到 ✓ → 都如实说「基准不可用」+ 诊断行 ✓
            Check("诊断·②b 插件行（桥接）**已由 CLI 真比对** → unknown 时不再说「CLI 不查最新版」✗ 而是「工具箱自带基准不可用」（原因见诊断行 ✓）",
                UpdateDiagnosis.StateText("plugin", "unknown", "unknown", "0.2.0") == "工具箱自带基准不可用"
                && UpdateDiagnosis.StateText("plugin", "unknown", "unknown", "0.2.0") != "CLI 不查最新版");
            // ★★ 插件五态（维护者要求「每种都在 UI 上可读懂」✓）：触发条件由 CLI 决定 ✓ GUI 只照译 ✓
            Check("诊断·插件五态：未安装 / 已是最新 / 有更新 / **内容与工具箱自带不一致**（版本同但哈希不同 ✓）/ 比工具箱自带还新",
                UpdateDiagnosis.StateText("plugin", "not-installed", "0.2.0", "unknown") == "未安装"
                && UpdateDiagnosis.StateText("plugin", "up-to-date", "0.2.0", "0.2.0") == "已是最新"
                && UpdateDiagnosis.StateText("plugin", "update-available", "0.2.0", "0.1.0") == "有更新"
                && UpdateDiagnosis.StateText("plugin", "content-mismatch", "0.2.0", "0.2.0") == "内容与工具箱自带不一致"
                && UpdateDiagnosis.StateText("plugin", "newer-than-bundled", "0.2.0", "0.3.0") == "比工具箱自带还新");
            Check("诊断·插件五态的**版本行**：基准来自「你这份工具箱里的那一份」✓（✗ 不说成网上的最新版 ✗）",
                UpdateDiagnosis.VersionText("plugin", "up-to-date", "0.2.0", "0.2.0") == "已装 v0.2.0　→　工具箱自带 v0.2.0（你这份工具箱里的那一份，不是网上的最新版）"
                && UpdateDiagnosis.VersionText("plugin", "not-installed", "0.2.0", "unknown").StartsWith("未安装（CLI 报 installed=unknown")
                && UpdateDiagnosis.VersionText("plugin", "update-available", "0.2.0", "0.1.0").Contains("工具箱自带 v0.2.0"));
            Check("诊断·★**版本同但哈希不同**那一态：版本行必须把它点出来 ✗（否则「已装 v0.2.0 → 工具箱自带 v0.2.0」看着像没事 ✗）",
                UpdateDiagnosis.VersionText("plugin", "content-mismatch", "0.2.0", "0.2.0").Contains("版本号相同，但 snapshot.js / index.js 的内容哈希与工具箱自带的不同")
                && UpdateDiagnosis.VersionText("plugin", "newer-than-bundled", "0.2.0", "0.3.0").Contains("高于"));
            Check("诊断·插件行拿不到工具箱自带基准（老 CLI / 自带的一份缺目录）→ 版本行不编最新版 ✓ 也不再说「CLI 不提供」✗",
                UpdateDiagnosis.VersionText("plugin", "unknown", "unknown", "0.2.0").Contains("latest=unknown")
                && UpdateDiagnosis.VersionText("plugin", "unknown", "unknown", "0.2.0").Contains("工具箱自带基准")
                && !UpdateDiagnosis.VersionText("plugin", "unknown", "unknown", "0.2.0").Contains("最新")
                && !UpdateDiagnosis.VersionText("plugin", "unknown", "unknown", "0.2.0").Contains("CLI 不提供"));
            Check("诊断·插件行**贴 CLI 的 DIAG 原文**（工具箱自带的一份缺 plugin 目录（源码构建常见）✓ 按 id 前缀取 ✓ 第一条 ✓ ✗ 不编 ✗）",
                UpdateDiagnosis.HintText("plugin", "unknown", "unknown", "0.1.0",
                    "UPDATECENTER_ITEM plugin:dsh-minato-bridge kind=plugin installed=0.1.0 latest=unknown state=unknown profile=desktop\nUPDATECENTER_DIAG plugin:dsh-minato-bridge 工具箱自带的一份缺 plugin 目录（源码构建常见）：C:\\x\\plugin\\dsh-minato-bridge\n")
                    .Contains("工具箱自带的一份缺 plugin 目录（源码构建常见）")
                && UpdateDiagnosis.DiagOfIdPrefix("UPDATECENTER_DIAG plugin:x 原因一\nUPDATECENTER_DIAG plugin:y 原因二", "plugin:") == "原因一"
                && UpdateDiagnosis.DiagOfIdPrefix("UPDATECENTER_DIAG minato 别的", "plugin:") == ""
                && UpdateDiagnosis.DiagOfIdPrefix(null, "plugin:") == "" && UpdateDiagnosis.DiagOfIdPrefix("x", "") == "");
            Check("诊断·③ 桌面端没找到 → 「未检测到桌面端」（不是取不到，也不是未知 ✓）",
                UpdateDiagnosis.StateText("desktop", "unknown", "unknown", "unknown") == "未检测到桌面端");
            Check("诊断·CLI 报了个 GUI 不认识的状态 → **把原值贴出来** ✓ 不吞成「未知」✗",
                UpdateDiagnosis.StateText("webui", "weird-state", "1.0.0", "1.0.0").Contains("weird-state")
                && UpdateDiagnosis.StateText("mystery", "", "", "").Contains("空"));
            Check("诊断·**任何**输入都不再产出「未知」两个字（用户报的那条 ✓✓）",
                !UpdateDiagnosis.StateText("webui", "unknown", "unknown", "1").Contains("未知")
                && !UpdateDiagnosis.StateText("minato", "unknown", "unknown", "1").Contains("未知")
                && !UpdateDiagnosis.StateText("plugin", "", "", "").Contains("未知")
                && !UpdateDiagnosis.StateText("desktop", "unknown", "unknown", "unknown").Contains("未知")
                && !UpdateDiagnosis.StateText("mystery", "", "", "").Contains("未知"));
            Check("诊断·拿到了 → 版本号（已装 vX → 最新 vY ✓ 只加 v 前缀 ✗ 不改数值 ✗ 已带 v 不重复 ✗）",
                UpdateDiagnosis.VersionText("webui", "update-available", "0.2.0-rc.2", "0.1.5-rc.2") == "已装 v0.1.5-rc.2　→　最新 v0.2.0-rc.2"
                && UpdateDiagnosis.VersionText("minato", "up-to-date", "3.0.7", "3.0.7") == "已装 v3.0.7　→　最新 v3.0.7"
                && VersionMarkers.TagOf("v3.0.7") == "v3.0.7" && VersionMarkers.TagOf("") == "" && VersionMarkers.TagOf(null) == null);
            Check("诊断·webui 取不到 → 明说离线/白名单；本工具的老 CLI → 明说 CLI 没给（✗ 不再写 unknown ✗）",
                UpdateDiagnosis.VersionText("webui", "unknown", "unknown", "0.1.5").Contains("取不到（离线，或 npm 返回值未通过白名单）")
                && UpdateDiagnosis.VersionText("minato", "unknown", "unknown", "3.0.7").Contains("CLI 没给 latest"));
            Check("诊断·installed=unknown 不许原样给用户看 unknown（→ 未检测到 ✓）",
                UpdateDiagnosis.VersionText("webui", "unknown", "unknown", "unknown").Contains("未检测到")
                && UpdateDiagnosis.VersionText("desktop", "unknown", "unknown", "unknown").Contains("未检测到官方桌面端"));
            Check("诊断·原因：贴 CLI 原始的 `…_FAIL <文案>`（全大写标记词才认 ✓ 普通文本不误报 ✓）",
                UpdateDiagnosis.FailReason("x\nUPDATEINFO_FAIL 拿不到可信的最新版本（离线，或 npm 返回值未通过白名单）\ny") == "拿不到可信的最新版本（离线，或 npm 返回值未通过白名单）"
                && UpdateDiagnosis.FailReason("普通文本里出现 _FAIL 这种词") == ""
                && UpdateDiagnosis.FailReason("lower_FAIL x") == "" && UpdateDiagnosis.FailReason(null) == "");
            Check("诊断·原文里有 _FAIL → 诊断行贴它（✗ 不替 CLI 编原因 ✓）；没有 → 如实说「这次没打原因行」✓",
                UpdateDiagnosis.HintText("webui", "unknown", "unknown", "1", "UPDATEINFO_FAIL 离线").Contains("离线")
                && UpdateDiagnosis.HintText("webui", "unknown", "unknown", "1", "UPDATECENTER_ITEM webui kind=webui").Contains("没打原因行"));
            Check("诊断·诊断行只在「本该有最新版却拿不到」时出现（拿到了 → 空串 ✓ 不噪声 ✓；插件行 state=unknown 且无 DIAG → 明说是**老 CLI** ✓）",
                UpdateDiagnosis.HintText("webui", "update-available", "1.0.0", "0.9.0", "x") == ""
                && UpdateDiagnosis.HintText("plugin", "up-to-date", "0.2.0", "0.2.0", "x") == ""
                && UpdateDiagnosis.HintText("plugin", "update-available", "0.2.0", "0.1.0", "x") == ""
                && UpdateDiagnosis.HintText("plugin", "unknown", "unknown", "0.2.0", "x").Contains("老 CLI"));
            Check("诊断·★本工具取不到最新版 → 把 CLI 的 `UPDATECENTER_DIAG minato <原因>` **逐字贴出来**（✗ 不吞原因 ✗ 不替它编 ✗）",
                UpdateDiagnosis.HintText("minato", "unknown", "unknown", "3.0.7",
                    "UPDATECENTER_ITEM minato kind=minato installed=3.0.7 latest=unknown state=unknown\nUPDATECENTER_DIAG minato 没取到最新版（只报告，因此无法判断是否有更新）：超时（6 秒内没有回应，网络慢或不通）\n")
                    .Contains("超时（6 秒内没有回应，网络慢或不通）")
                && UpdateDiagnosis.DiagOf("UPDATECENTER_DIAG minato 超时（6 秒）", "minato") == "超时（6 秒）"
                && UpdateDiagnosis.DiagOf("", "minato") == "" && UpdateDiagnosis.DiagOf("UPDATECENTER_DIAG minato x", "webui") == "");
            Check("诊断·★老 CLI（原文里没有任何 UPDATECENTER_ 行）→「该 CLI 版本过旧，无法检查更新」✓",
                UpdateDiagnosis.EmptyTitle("未知参数：update-center\n  DeepSeek Harness Toolkit V2.7.3", false) == "该 CLI 版本过旧，无法检查更新"
                && UpdateDiagnosis.EmptyDetail("未知参数：update-center", false).Contains("UPDATECENTER_ITEM"));
            Check("诊断·空态三态分开：还在读 / 还没有输出 / CLI 支持但一个组件都没报",
                UpdateDiagnosis.EmptyTitle("", true).StartsWith("正在读取")
                && UpdateDiagnosis.EmptyTitle("", false) == "还没读到更新信息"
                && UpdateDiagnosis.EmptyTitle("UPDATECENTER_OK 0", false) == "CLI 支持更新检查，但这次一个组件都没报");

            // ================================================================ 更新页：同 id 多 profile 合并（2026-10-11）
            // ★ 真机反馈：桥接插件被报了三遍 ✗（每个 profile 一条 ITEM ✓ 数据是对的 ✓ 但界面读起来像重复 ✗）
            //   → CLI：ITEM 每 profile 一行**不动** ✓，URL/NOTE 只发一次 ✓，每行多一个该行独有的 `next=` ✓
            //   → GUI：**按 id 归组**成一张卡 ✓（✗ 不再靠"第 n 条 NOTE 配第 n 行"这种脆弱约定 ✗）
            //   下面这些断言盯的就是这两件事：**一条都不丢** ✓ 且**合并成一张卡 ✓ 三行都在** ✓
            string pluginRaw =
                "UPDATECENTER_ITEM plugin:dsh-minato-bridge kind=plugin installed=0.1.0 latest=0.2.0 state=update-available profile=desktop next=在桌面端插件机制里更新\n" +
                "UPDATECENTER_ITEM plugin:dsh-minato-bridge kind=plugin installed=unknown latest=0.2.0 state=not-installed profile=headless next=未安装\n" +
                "UPDATECENTER_ITEM plugin:dsh-minato-bridge kind=plugin installed=0.2.0 latest=0.2.0 state=up-to-date profile=web next=bridge-install%20--profile%20web%20--yes\n" +
                "UPDATECENTER_URL plugin:dsh-minato-bridge https://github.com/sakanamaru/dsh-minato\n" +
                "UPDATECENTER_NOTE plugin:dsh-minato-bridge 基准 = 你这份工具箱自带的那一份插件，离线可判（判定方式与路径见 GUI「说明」页）。\n" +
                "UPDATECENTER_ITEM webui kind=webui installed=0.1.5-rc.2 latest=0.2.0-rc.2 state=update-available profile=-\n" +
                "UPDATECENTER_URL webui https://github.com/deepseek-ai/deepseek-harness\n";
            List<UpdateCenterMarkers.Item> pu = UpdateCenterMarkers.Parse(pluginRaw);
            Check("更新页·解析：ITEM 每 profile 一行**都收进来**（3 行 ✓ 顺序 = 出现序 ✓ ✗ 不合并成一条带多值的行 ✗）",
                pu.Count == 4 && pu[0].Profile == "desktop" && pu[1].Profile == "headless" && pu[2].Profile == "web"
                && pu[3].Id == "webui" && pu[0].State == "update-available" && pu[2].State == "up-to-date");
            Check("更新页·解析：URL / NOTE **按 id 归组** → 同一个 id 的每一行都看得到那份共用信息（✗ 不按出现顺序配 ✗）",
                pu[0].Urls.Count == 1 && pu[1].Urls.Count == 1 && pu[2].Urls.Count == 1
                && pu[1].Urls[0] == "https://github.com/sakanamaru/dsh-minato" && pu[1].Notes.Count == 1 && pu[1].Notes[0].StartsWith("基准"));
            Check("更新页·解析：每行**自己**的指引 `next=`（空格按 `%20` 编 ✓ 解回来是人话 ✓ ✗ 不拿别人的 NOTE 顶 ✗）",
                pu[0].Next == "在桌面端插件机制里更新" && pu[1].Next == "未安装"
                && pu[2].Next == "bridge-install --profile web --yes"
                && UpdateCenterMarkers.DecodeNext("a%20b") == "a b" && UpdateCenterMarkers.DecodeNext("-") == "" && UpdateCenterMarkers.DecodeNext(null) == "");
            Check("更新页·归组：同一个 id 的多行进**同一组** ✓ 顺序可复现 ✓ 别的组件不掺进来 ✓",
                UpdateCenterMarkers.GroupById(pu).Count == 2
                && UpdateCenterMarkers.GroupById(pu)[0].Count == 3 && UpdateCenterMarkers.GroupById(pu)[1].Count == 1);
            Check("更新页·共用信息只出一份：一个 URL ✓ 一段 NOTE ✓（三条完全相同的 NOTE 会被去重 ✓ 不同的则一条不丢 ✓）",
                UpdateCenterMarkers.UrlOf(UpdateCenterMarkers.GroupById(pu)[0]) == "https://github.com/sakanamaru/dsh-minato"
                && UpdateCenterMarkers.NoteOf(UpdateCenterMarkers.GroupById(pu)[0]).Split('\n').Length == 1
                && UpdateCenterMarkers.Parse("UPDATECENTER_ITEM plugin:x kind=plugin installed=1 latest=1 state=up-to-date profile=a\nUPDATECENTER_NOTE plugin:x 甲\nUPDATECENTER_NOTE plugin:x 乙\nUPDATECENTER_NOTE plugin:x 甲\n")[0].Notes.Count == 2);
            // ★★★ 这一条就是维护者要的"合并成一张卡、profile 分行"——**三行都要渲染出来** ✓（✗ 不许只显示第一条 ✗）
            //   ★★ 2026-10-11（可读性）：正文改成了**固定五列的小表** ✓（profile ｜ 已装 ｜ 工具箱自带 ｜ 状态 ｜ 下一步 ✓）
            string rows3 = UpdateCenterMarkers.RowsText(UpdateCenterMarkers.GroupById(pu)[0]);
            string[] rows3l = rows3.Replace("\r\n", "\n").Split('\n');
            Check("更新页·合并卡：三个 profile **各一行都在** ✓（desktop / headless / web ✓ ✗ 只显示第一条 ✗）",
                rows3l.Length == 4
                && rows3l[0] == "profile\t已装\t工具箱自带\t状态\t下一步"
                && rows3l[1].StartsWith("desktop\t") && rows3l[2].StartsWith("headless\t")
                && rows3l[3].StartsWith("web\t"));
            Check("更新页·表格：表头**逐字固定五列** ✓（profile ｜ 已装 ｜ 工具箱自带 ｜ 状态 ｜ 下一步 ✓ ✗ 列语义不许乱 ✗）",
                UpdateCenterMarkers.TableHeaders.Length == 5
                && UpdateCenterMarkers.TableHeaders[0] == "profile" && UpdateCenterMarkers.TableHeaders[1] == "已装"
                && UpdateCenterMarkers.TableHeaders[2] == "工具箱自带" && UpdateCenterMarkers.TableHeaders[3] == "状态"
                && UpdateCenterMarkers.TableHeaders[4] == "下一步");
            Check("更新页·表格·每行的**状态**都在那一行上：已装 v0.1.0 / 工具箱自带 v0.2.0 / 有更新；未安装；已是最新",
                rows3l[1] == "desktop\tv0.1.0\tv0.2.0\t有更新\t在桌面端插件机制里更新"
                && rows3l[2] == "headless\t未安装\tv0.2.0\t未安装\t未安装"
                && rows3l[3] == "web\tv0.2.0\tv0.2.0\t已是最新\tbridge-install --profile web --yes");
            Check("更新页·表格：每格都**短** ✓（状态 ≤ 6 字 ✓ 下一步 = 一条命令或 ≤ 20 字短句 ✓）",
                UpdateCenterMarkers.CellState(new UpdateCenterMarkers.Item { State = "update-available" }) == "有更新"
                && UpdateCenterMarkers.CellState(new UpdateCenterMarkers.Item { State = "up-to-date" }) == "已是最新"
                && UpdateCenterMarkers.CellState(new UpdateCenterMarkers.Item { State = "not-installed" }) == "未安装"
                && UpdateCenterMarkers.CellState(new UpdateCenterMarkers.Item { State = "content-mismatch" }) == "内容不一致"
                && UpdateCenterMarkers.CellState(new UpdateCenterMarkers.Item { State = "newer-than-bundled" }) == "比自带新"
                && UpdateCenterMarkers.CellState(new UpdateCenterMarkers.Item { State = "unknown" }) == "未知"
                && "内容不一致".Length <= 6 && "比自带新".Length <= 6
                && UpdateCenterMarkers.CellNext(MkItem("web", "0.2.0", "0.2.0", "up-to-date", "bridge-install%20--profile%20web%20--yes")) == "bridge-install --profile web --yes"
                && UpdateCenterMarkers.CellNext(MkItem("desktop", "0.1.0", "0.2.0", "update-available", "在桌面端插件机制里更新")) == "在桌面端插件机制里更新"
                && rows3l[1].Length < 200 && rows3l[2].Length < 200 && rows3l[3].Length < 200);
            Check("更新页·表格：卡片里那句共用说明也已**压短**（< 120 字 ✓ 读得完 ✓）",
                UpdateCenterMarkers.NoteOf(UpdateCenterMarkers.GroupById(pu)[0]).Length < 120
                && UpdateCenterMarkers.NoteOf(UpdateCenterMarkers.GroupById(pu)[0]).StartsWith("基准"));
            Check("更新页·卡头小汇总：逐字 `有更新 1 · 未安装 1 · 已是最新 1` ✓（同状态合并计数 ✓ 最需要看一眼的在前 ✓ ✗ 不写「共 N 行」✗）",
                UpdateCenterMarkers.SummaryText(UpdateCenterMarkers.GroupById(pu)[0]) == "有更新 1 · 未安装 1 · 已是最新 1"
                && UpdateCenterMarkers.SummaryText(new List<UpdateCenterMarkers.Item>()) == ""
                && UpdateCenterMarkers.SummaryText(null) == "");
            Check("更新页·表格·每行的**指引**都在那一行上：web = bridge-install ✓ desktop = 桌面端机制 ✓（✗ 不串行 ✗）",
                rows3l[1].Contains("桌面端插件机制") && !rows3l[1].Contains("bridge-install")
                && rows3l[3].Contains("bridge-install --profile web --yes") && !rows3l[3].Contains("桌面端"));
            // ★★ 2026-10-11（可读性）：从卡片**搬去「说明」页**的那段机制说明 —— 直接断言它**真的还在** ✓
            //   ✗ 不留一堆只有截图才能证明的文字 ✗（这段文字是纯函数 ✓ 说明页只是把它交给一个会换行的 TextBlock ✓）
            string aboutTxt = UpdateBaselineExplain.Text();
            Check("说明页·搬走的机制说明**还在**：基准 = 工具箱自带那一份 + 离线可判 + 零新渠道 ✓",
                UpdateBaselineExplain.Title() == "插件更新的基准与判定"
                && aboutTxt.Contains("工具箱自带") && aboutTxt.Contains("离线") && aboutTxt.Contains("新的版本渠道")
                && aboutTxt.Contains("plugin\\dsh-minato-bridge\\"));
            Check("说明页·双比对（版本号 + snapshot.js / index.js 的 SHA-256）与「半旧」那一态逐条可读 ✓",
                aboutTxt.Contains("VersionComparer") && aboutTxt.Contains("SHA-256")
                && aboutTxt.Contains("snapshot.js") && aboutTxt.Contains("index.js")
                && aboutTxt.Contains("半旧") && aboutTxt.Contains("latest"));
            Check("说明页·desktop 的完整口径（桌面端独占管理 / 命令行装不进也卸不掉 / 它接受本地目录路径）✓",
                aboutTxt.Contains("桌面端独占管理") && aboutTxt.Contains("命令行装不进也卸不掉") && aboutTxt.Contains("本地目录路径"));
            Check("说明页·风险与备份、作者维护不担保（卡片里也不再重复）✓",
                aboutTxt.Contains("风险") && aboutTxt.Contains("备份") && aboutTxt.Contains("不替它担保"));
            Check("说明页·长路径那一节：给出基准目录（本程序旁边的 plugin\\dsh-minato-bridge\\）与本机候选位置 ✓",
                UpdateBaselineExplain.PathsTitle().Contains("完整路径")
                && UpdateBaselineExplain.PathsText().Contains("plugin")
                && UpdateBaselineExplain.PathsText().Contains("dsh-minato-bridge"));
            Check("说明页·★长路径**真的搬走了**：更新页那张卡里既没有盘符路径也没有哈希/版本比较术语 ✓",
                !rows3.Contains(":\\") && !rows3.Contains("SHA-256") && !rows3.Contains("VersionComparer")
                && !rows3.Contains("package.json"));
            Check("更新页·合并卡·整体状态取**最需要看一眼**的那一行（有更新 > 未安装 > 已是最新 ✓ 半旧 > 有更新 ✓）",
                UpdateCenterMarkers.OverallState(UpdateCenterMarkers.GroupById(pu)[0]) == "update-available"
                && UpdateCenterMarkers.OverallState(new List<UpdateCenterMarkers.Item> {
                    new UpdateCenterMarkers.Item { State = "content-mismatch" }, new UpdateCenterMarkers.Item { State = "update-available" } }) == "content-mismatch"
                && UpdateCenterMarkers.OverallState(new List<UpdateCenterMarkers.Item> {
                    new UpdateCenterMarkers.Item { State = "not-installed" }, new UpdateCenterMarkers.Item { State = "up-to-date" } }) == "not-installed");
            // ★ 老 CLI（每个 profile 各发一条 URL/NOTE ✓ 没有 next=）→ 共用信息**一条都不丢** ✓ 且**不当成每行的指引** ✓
            string oldRaw =
                "UPDATECENTER_ITEM plugin:dsh-minato-bridge kind=plugin installed=0.1.0 latest=unknown state=unknown profile=desktop\n" +
                "UPDATECENTER_URL plugin:dsh-minato-bridge https://example.invalid/a\n" +
                "UPDATECENTER_NOTE plugin:dsh-minato-bridge 甲说明\n" +
                "UPDATECENTER_ITEM plugin:dsh-minato-bridge kind=plugin installed=0.1.0 latest=unknown state=unknown profile=web\n" +
                "UPDATECENTER_URL plugin:dsh-minato-bridge https://example.invalid/b\n" +
                "UPDATECENTER_NOTE plugin:dsh-minato-bridge 乙说明\n";
            List<UpdateCenterMarkers.Item> oldg = UpdateCenterMarkers.Parse(oldRaw);
            Check("更新页·老 CLI（每个 profile 各发 URL/NOTE ✓ 没有 next=）：两条不同说明**都不丢** ✓ URL 只取第一条可用地址 ✓",
                oldg.Count == 2 && UpdateCenterMarkers.NoteOf(oldg).Contains("甲说明") && UpdateCenterMarkers.NoteOf(oldg).Contains("乙说明")
                && UpdateCenterMarkers.UrlOf(oldg) == "https://example.invalid/a"
                && UpdateCenterMarkers.GroupById(oldg).Count == 1
                && UpdateCenterMarkers.RowsText(oldg).Split('\n').Length == 3);   // ★ 表头 1 行 + 两个 profile 各 1 行 ✓
            Check("更新页·脏数据：没有 ITEM 的 URL/NOTE 行安全忽略 ✓ 认不出的标记行不掺进条目 ✓ 不抛 ✓",
                UpdateCenterMarkers.Parse("UPDATECENTER_URL nobody https://x\nUPDATECENTER_OK 0\nUPDATECENTER_BEGIN\n垃圾\n").Count == 0
                && UpdateCenterMarkers.Parse(null).Count == 0 && UpdateCenterMarkers.RowsText(null) == "" && UpdateCenterMarkers.UrlOf(null) == "");
            // ★★★ 真机截图抓到的一条：NOTE 自由文本换行后，**正文的一行**恰好以 `UPDATECENTER_ITEM ` 开头
            //   → 旧的"只看前缀"写法把它当成**新条目** → 更新页上凭空多出一张一模一样的 webui 卡 ✗✗
            //   ✓ 现在要求 `标记词<空格>id<空格>字段…` ✓（ITEM 行末尾永远跟着 ` kind=` ✓ 所以不会漏真行 ✓）
            string wrapped =
                "UPDATECENTER_URL webui https://github.com/deepseek-ai/deepseek-harness\n" +
                "UPDATECENTER_NOTE webui 更新前会自动备份数据根并保留回滚点\n" +
                "UPDATECENTER_ITEM webui kind=webui installed=0.1.5-rc.2 latest=0.2.0-rc.2 state=update-available profile=-\n";
            List<UpdateCenterMarkers.Item> wr = UpdateCenterMarkers.Parse(wrapped);
            Check("更新页·★续行不是新条目：正文换行后恰好以 `UPDATECENTER_ITEM ` 开头的那一行**不许**变成第二个条目 ✓（真机多出一张 webui 卡的根因 ✓）",
                wr.Count == 1 && wr[0].Id == "webui" && wr[0].Kind == "webui" && wr[0].Installed == "0.1.5-rc.2"
                && UpdateCenterMarkers.Parse("UPDATECENTER_ITEM webui").Count == 0
                && UpdateCenterMarkers.Parse("UPDATECENTER_ITEM ").Count == 0);
            // ★★ 真机上的**确切**那一行（NOT 长文换行后的正文 ✓ 后面什么都没有 ✓）—— 它曾经被收成第二张卡 ✗
            Check("更新页·★真机那一行逐字复现：`UPDATECENTER_ITEM webui`（NOT 长文的续行）**必须**被跳过 ✓ 而真 ITEM 行（带 kind=）仍照收 ✓",
                UpdateCenterMarkers.Parse("UPDATECENTER_ITEM webui\nUPDATECENTER_ITEM plugin:x kind=plugin state=up-to-date installed=1 latest=1 profile=a\n").Count == 1
                && UpdateCenterMarkers.Parse("UPDATECENTER_ITEM webui").Count == 0);

            Console.WriteLine("== " + _pass + "/" + (_pass + _fail) + " passed, " + _fail + " failed ==");
            return _fail == 0 ? 0 : 1;
        }

        /// <summary>真机实测（2026-10-11）：按 GUI 用的同一套候选路径扫一遍桥接插件，
        /// 每个命中项都必须**真的**在那个文件里（复读一遍原文核对 ✓）。
        /// 意图：证明界面上的版本是**读来的** ✗ 不是写死的 ✓；顺带把实测结果打进测试日志 ✓。</summary>
        private static bool BridgeRealReadOk()
        {
            List<VersionMarkers.BridgeHit> hits = VersionMarkers.ScanBridge(VersionMarkers.BridgeCandidates(null));
            Console.WriteLine("  [INFO] 桥接插件实测：" + VersionMarkers.DescribeBridge(hits)
                + "（候选 " + VersionMarkers.BridgeCandidates(null).Count + " 个）");
            for (int i = 0; i < hits.Count; i++)
            {
                Console.WriteLine("  [INFO]   ← " + hits[i].Path);
                if (!System.IO.File.Exists(hits[i].Path)) return false;
                string txt = System.IO.File.ReadAllText(hits[i].Path);
                if (VersionMarkers.JsonStringField(txt, "version") != hits[i].Version) return false;
            }
            return true;
        }
    }
}
