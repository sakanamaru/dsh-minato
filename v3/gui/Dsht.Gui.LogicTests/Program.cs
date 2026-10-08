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

            Console.WriteLine("== " + _pass + "/" + (_pass + _fail) + " passed, " + _fail + " failed ==");
            return _fail == 0 ? 0 : 1;
        }
    }
}
