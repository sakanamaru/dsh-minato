using System;
using System.Collections.Generic;
using Dsht.Domain.Abstractions;
using Dsht.Domain.Model;
using Dsht.Domain.Services;
using Dsht.Domain.Targets;

// 契约测试：先用假观测跑通判定与形态语义；V3-4 会把同一套断言跑在真实平台实现上。
sealed class FakePort : IPortProbe
{
    public bool Open;
    public bool Throw;
    public bool IsOpen(int port, int timeoutMs) { if (Throw) throw new Exception("boom"); return Open; }
}
sealed class FakeHttp : IHttpProbe
{
    public bool Ready;
    public bool Throw;
    public bool IsReady(string url, int timeoutMs) { if (Throw) throw new Exception("boom"); return Ready; }
    public bool Responds(string url, int timeoutMs) { return Ready; }
}
sealed class FakeProc : IProcessQuery
{
    // 域测试替身：没有真实进程可查 → 如实返回 false ✓（不猜 ✓）
    public bool AnyProcessNamed(string name) { return false; }
    public int PidOfNamed(string name) { return 0; }   // 替身没有真实进程 ✓
    public int Pid;
    public bool IsDsh;
    public bool Throw;
    public int PidListeningOn(int port) { if (Throw) throw new Exception("boom"); return Pid; }
    public bool IsDshCommandLine(int pid) { return IsDsh; }
    public System.DateTime? StartTime(int pid) { return pid > 0 ? new System.DateTime(2026, 9, 28, 11, 53, 14) : (System.DateTime?)null; }
    public string CommandLine(int pid) { return ""; }
}

static class ContractTests
{
    static int _pass, _fail;
        /// <summary>数**顶层**补丁行（行首无缩进）—— insert 块里的同名条目有缩进，不该算进来。</summary>
        static int CountTopRows(string s)
        {
            int n = 0;
            string[] lines = s.Split('\n');
            for (int i = 0; i < lines.Length; i++) { if (lines[i].StartsWith("- id: demo", StringComparison.Ordinal)) n++; }
            return n;
        }

    static void Check(string name, bool ok)
    {
        if (ok) { _pass++; Console.WriteLine("  [PASS] " + name); }
        else { _fail++; Console.WriteLine("  [FAIL] " + name); }
    }

    static WebTarget Make(FakePort p, FakeHttp h, FakeProc q)
    {
        return new WebTarget(p, h, q, new WebTargetOptions(3080, "http://127.0.0.1:3080/", 800, 800));
    }

    static bool NoFile(string p) { return false; }
    static bool ExistsTrue(string p) { return true; }
    static EntryLocation Loc7(string path, string entry) { return new EntryLocation("C:\\x\\y.yml", 7); }
    static bool AnyFile(string p) { return true; }

    static int Main()
    {
        Console.WriteLine("== V3 契约测试（领域判定 + 形态语义）==");

        Console.WriteLine("[1] ServiceJudge 真值表（逐条对齐 v2.x JudgeState3）");
        Check("端口关 → Down", ServiceJudge.Judge(false, false, null) == ServiceState.Down);
        Check("端口关+HTTP就绪 → Down（端口优先）", ServiceJudge.Judge(false, true, null) == ServiceState.Down);
        Check("端口开+HTTP就绪 → Ready", ServiceJudge.Judge(true, true, null) == ServiceState.Ready);
        Check("端口开+HTTP未就绪+监听是dsh → Ready", ServiceJudge.Judge(true, false, delegate { return true; }) == ServiceState.Ready);
        Check("端口开+HTTP未就绪+监听非dsh → Listening", ServiceJudge.Judge(true, false, delegate { return false; }) == ServiceState.Listening);
        Check("端口开+HTTP未就绪+委托为null → Listening", ServiceJudge.Judge(true, false, null) == ServiceState.Listening);
        Check("监听判定抛异常 → 按false（Listening）", ServiceJudge.Judge(true, false, delegate { throw new Exception("boom"); }) == ServiceState.Listening);
        Check("两信号版：端口开+HTTP未就绪 → Listening", ServiceJudge.Judge(true, false) == ServiceState.Listening);

        Console.WriteLine("[2] WebTarget 形态语义");
        FakePort p = new FakePort(); FakeHttp h = new FakeHttp(); FakeProc q = new FakeProc();
        WebTarget t = Make(p, h, q);
        Check("Kind=Web", t.Kind == AppKind.Web);
        Check("Describe 提到端口", t.Describe().IndexOf("3080") >= 0);

        ServiceReport r1 = t.Probe();
        Check("端口关 → Down/无PID", r1.State == ServiceState.Down && r1.Pid == 0 && r1.StatusMarker == "STATUS_DOWN");
        Check("端口关 → Basis 说明无监听", r1.Basis.IndexOf("无监听") >= 0);

        p.Open = true; h.Ready = true;
        ServiceReport r2 = t.Probe();
        Check("端口开+HTTP就绪 → Ready/STATUS_UP", r2.State == ServiceState.Ready && r2.StatusMarker == "STATUS_UP");

        h.Ready = false; q.Pid = 4242; q.IsDsh = true;
        ServiceReport r3 = t.Probe();
        Check("端口开+HTTP未就绪+监听是dsh → Ready", r3.State == ServiceState.Ready);
        Check("Ready 时带出 PID", r3.Pid == 4242);

        q.IsDsh = false;
        ServiceReport r4 = t.Probe();
        Check("端口开+HTTP未就绪+监听非dsh → Listening/STATUS_STARTING", r4.State == ServiceState.Listening && r4.StatusMarker == "STATUS_STARTING");

        FakePort pt = new FakePort(); pt.Throw = true;
        Check("端口探测抛异常 → 按 Down 处理", Make(pt, h, q).Probe().State == ServiceState.Down);

        Console.WriteLine("[2b] 观测抛异常时的降级（v2.x 的 try/catch 语义）");
        FakePort p2 = new FakePort(); p2.Open = true;
        FakeHttp h2 = new FakeHttp(); h2.Throw = true;
        FakeProc q2 = new FakeProc(); q2.Pid = 7; q2.IsDsh = true;
        Check("HTTP 探测抛异常 → 按未就绪，但监听是 dsh 仍判 Ready", Make(p2, h2, q2).Probe().State == ServiceState.Ready);
        q2.IsDsh = false;
        Check("HTTP 抛异常 + 监听非 dsh → Listening", Make(p2, h2, q2).Probe().State == ServiceState.Listening);
        FakeProc q3 = new FakeProc(); q3.Throw = true;
        ServiceReport r5 = Make(p2, new FakeHttp(), q3).Probe();
        Check("进程查询抛异常 → 非 dsh → Listening 且 PID=0", r5.State == ServiceState.Listening && r5.Pid == 0);
        Console.WriteLine("[3] UnknownTarget 诚实性");
        UnknownTarget u = new UnknownTarget("测试注入");
        ServiceReport ru = u.Probe();
        Check("Kind=Unknown", u.Kind == AppKind.Unknown);
        Check("State=Down（不假装 Ready）", ru.State == ServiceState.Down && ru.StatusMarker == "STATUS_DOWN");
        Check("IsAvailable=false", !u.IsAvailable());
        Check("Describe 明确说明未识别", u.Describe().IndexOf("未识别") >= 0);
        Check("Basis 明确说明未识别", ru.Basis.IndexOf("未识别") >= 0);

        Console.WriteLine("[4] BackupRetention 保留策略（逐条对齐 v2.x）");
        Check("自动类：-auto", BackupRetention.IsAutoBackupName("dsh-data-20260921-193000-auto"));
        Check("自动类：-pre-restore/-pre-import/-pre-wipe/-pre-update",
            BackupRetention.IsAutoBackupName("x-pre-restore") && BackupRetention.IsAutoBackupName("x-pre-import")
            && BackupRetention.IsAutoBackupName("x-pre-wipe") && BackupRetention.IsAutoBackupName("x-pre-update"));
        Check("手动备份不算自动类", !BackupRetention.IsAutoBackupName("dsh-data-20260921-193000"));
        Check("非备份目录名被排除", !BackupRetention.IsBackupDirName("some-other-dir"));
        Check("保底 3 份：keep=1 → 3", BackupRetention.EffectiveKeep(1) == 3);
        Check("keep=10 → 10", BackupRetention.EffectiveKeep(10) == 10);

        System.Collections.Generic.List<string> autos5 = new System.Collections.Generic.List<string>();
        autos5.Add("dsh-data-20260901-100000-auto");
        autos5.Add("dsh-data-20260902-100000-auto");
        autos5.Add("dsh-data-20260903-100000-auto");
        autos5.Add("dsh-data-20260904-100000-auto");
        autos5.Add("dsh-data-20260905-100000-auto");
        System.Collections.Generic.List<string> del5 = BackupRetention.SelectForDeletion(autos5, 3);
        Check("5 份自动 + keep=3 → 删 2 份最旧", del5.Count == 2);
        Check("删除顺序为最旧在前", del5.Count == 2 && del5[0].IndexOf("20260901") > 0 && del5[1].IndexOf("20260902") > 0);
        Check("keep 超出总数 → 不删", BackupRetention.SelectForDeletion(autos5, 10).Count == 0);
        Check("空输入 → 不删", BackupRetention.SelectForDeletion(new System.Collections.Generic.List<string>(), 3).Count == 0);

        System.Collections.Generic.List<string> mixed = new System.Collections.Generic.List<string>();
        mixed.Add("dsh-data-20260901-100000");                 // 手动（永久保留）
        mixed.Add("dsh-data-20260902-100000");                 // 手动
        mixed.Add("dsh-data-20260903-100000-auto");
        mixed.Add("dsh-data-20260904-100000-auto");
        mixed.Add("dsh-data-20260905-100000-pre-wipe");
        mixed.Add("dsh-data-20260906-100000-auto");
        mixed.Add("dsh-data-20260907-100000-auto");
        mixed.Add("not-a-backup-auto");
        System.Collections.Generic.List<string> delMixed = BackupRetention.SelectForDeletion(mixed, 3);
        Check("混合：5 份自动 + keep=3 → 删 2 份最旧，手动永不删", delMixed.Count == 2 && delMixed[0].IndexOf("20260903") > 0 && delMixed[1].IndexOf("20260904") > 0);
        Check("混合：手动备份未被选中", delMixed.IndexOf("dsh-data-20260901-100000") < 0 && delMixed.IndexOf("dsh-data-20260902-100000") < 0);
        Check("非备份前缀不参与", delMixed.IndexOf("not-a-backup-auto") < 0);
        Console.WriteLine("[5] ProfileScanner 块级扫描（逐条对齐 v2.x ProfileCheckText）");
        Check("需要 maxDepth 的插件名（含引号）", ProfileScanner.NeedsMaxDepthPlugin("'@deepseek-ai/dsh-tool-subagent'") && ProfileScanner.NeedsMaxDepthPlugin("@deepseek-ai/dsh-subagent-acp"));
        Check("普通插件不需要 maxDepth", !ProfileScanner.NeedsMaxDepthPlugin("@deepseek-ai/dsh-tool-web"));
        Check("YAML 标量：去引号+去行尾注释", ProfileScanner.CleanYamlScalar("'a-b' # note") == "a-b");
        Check("YAML 标量：引号内 # 不当注释", ProfileScanner.CleanYamlScalar("\"a#b\"") == "a#b");
        Check("像路径判定", ProfileScanner.LooksLikeCommandPath("C:\\x\\y.exe") && !ProfileScanner.LooksLikeCommandPath("justtext"));

        string dashNoMax = "- id: subagent-acp\n  name: '@deepseek-ai/dsh-subagent-acp'\n  config:\n    someKey: 1\n";
        System.Collections.Generic.List<Dsht.Domain.Model.ProfileFinding> fs1 = ProfileScanner.Scan(dashNoMax, "profiles/web/cordis.patch.yml", NoFile);
        Check("dash 形式缺 maxDepth → 1 条发现", fs1.Count == 1 && fs1[0].Missing == "maxDepth");
        Check("行号与 id 正确", fs1.Count == 1 && fs1[0].Line == 1 && fs1[0].Id == "subagent-acp");
        Check("可自动修（对应 FIX 行）", fs1.Count == 1 && fs1[0].AutoFixable);
        Check("Hint 指明 provider-managed", fs1.Count == 1 && fs1[0].Hint.IndexOf("provider-managed") >= 0);

        string withMax = "- id: subagent-acp\n  name: '@deepseek-ai/dsh-subagent-acp'\n  config:\n    maxDepth: 'provider-managed'\n";
        Check("config 块内有 maxDepth → 无发现", ProfileScanner.Scan(withMax, "f", NoFile).Count == 0);

        string maxElsewhere = "- id: subagent-acp\n  name: '@deepseek-ai/dsh-subagent-acp'\n  maxDepth: 8\n";
        Check("maxDepth 出现在别处（非 config 下）→ 无发现（与 v2.x 一致）", ProfileScanner.Scan(maxElsewhere, "f", NoFile).Count == 0);

        string otherPlugin = "- id: web\n  name: '@deepseek-ai/dsh-tool-web'\n";
        Check("非目标插件 → 无发现", ProfileScanner.Scan(otherPlugin, "f", NoFile).Count == 0);

        string topForm = "id: subagent-acp\nname: '@deepseek-ai/dsh-tool-subagent'\n";
        System.Collections.Generic.List<Dsht.Domain.Model.ProfileFinding> fs2 = ProfileScanner.Scan(topForm, "f", NoFile);
        Check("顶层 id: 形式也能识别", fs2.Count == 1 && fs2[0].Id == "subagent-acp");

        string mcpMissing = "- id: mcp\n  name: '@deepseek-ai/dsh-mcp-client'\n  failOnStartupError: true\n  command: C:\\nope\\missing.exe\n";
        System.Collections.Generic.List<Dsht.Domain.Model.ProfileFinding> fs3 = ProfileScanner.Scan(mcpMissing, "f", NoFile);
        Check("mcp command 不存在 → 1 条 command 发现", fs3.Count == 1 && fs3[0].Missing == "command");
        Check("command 类不可自动修（只报不修）", fs3.Count == 1 && !fs3[0].AutoFixable);
        Check("mcp command 存在 → 无发现", ProfileScanner.Scan(mcpMissing, "f", AnyFile).Count == 0);
        Check("mcp 未开 failOnStartupError → 无发现", ProfileScanner.Scan("- id: mcp\n  name: '@deepseek-ai/dsh-mcp-client'\n  command: C:\\nope\\x.exe\n", "f", NoFile).Count == 0);
        Check("空文本 → 无发现", ProfileScanner.Scan("", "f", NoFile).Count == 0);
        Console.WriteLine("[6] 完整性判定与 manifest 解析（逐条对齐 v2.x）");
        string hex64 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        string manifest = "# 头部注释\n\n" + hex64 + "  DeepSeek Harness Toolkit.exe\n" + hex64 + "  README.md\n";
        Check("解析命中（文件名忽略大小写）", ManifestParser.ParseHash(manifest, "deepseek harness toolkit.exe") == hex64);
        Check("解析未命中 → null", ManifestParser.ParseHash(manifest, "other.exe") == null);
        Check("跳过 # 注释与空行", ManifestParser.ParseHash("#x\n\ny\n", "z") == null);
        Check("hash 长度非 64 → null（视为非法）", ManifestParser.ParseHash("abc  f.exe\n", "f.exe") == null);
        Check("非十六进制 → null", ManifestParser.ParseHash(new string('z', 64) + "  f.exe\n", "f.exe") == null);
        Check("空输入 → null", ManifestParser.ParseHash(null, "f.exe") == null && ManifestParser.ParseHash("x", "") == null);

        Check("期望缺失 → Unknown", IntegrityJudge.Judge(null, hex64) == IntegrityVerdict.Unknown);
        Check("实际缺失 → Unknown", IntegrityJudge.Judge(hex64, null) == IntegrityVerdict.Unknown);
        Check("相等 → Match", IntegrityJudge.Judge(hex64, hex64) == IntegrityVerdict.Match);
        Check("忽略大小写 → Match", IntegrityJudge.Judge(hex64.ToUpperInvariant(), hex64) == IntegrityVerdict.Match);
        Check("不等 → Mismatch", IntegrityJudge.Judge(hex64, new string('0', 64)) == IntegrityVerdict.Mismatch);
        Check("只有 Mismatch 拦截高风险操作", IntegrityJudge.ShouldBlock(IntegrityVerdict.Mismatch)
            && !IntegrityJudge.ShouldBlock(IntegrityVerdict.Match) && !IntegrityJudge.ShouldBlock(IntegrityVerdict.Unknown));
        Console.WriteLine("[7] BackupPackage 备份包判定（逐条对齐 v2.x）");
        Check("名字前缀（忽略大小写）", BackupPackage.IsValidBackupName("dsh-data-20260921-193000-auto") && BackupPackage.IsValidBackupName("DSH-DATA-x") && !BackupPackage.IsValidBackupName("other"));
        Check("数据特征：settings.yaml", BackupPackage.HasDshData(new DirSnapshot("d", new string[] { "settings.yaml" })));
        Check("数据特征：credentials/sessions/profiles/storages", BackupPackage.HasDshData(new DirSnapshot("d", new string[] { "credentials.yaml" })) && BackupPackage.HasDshData(new DirSnapshot("d", new string[] { "sessions" })) && BackupPackage.HasDshData(new DirSnapshot("d", new string[] { "profiles" })) && BackupPackage.HasDshData(new DirSnapshot("d", new string[] { "storages" })));
        Check("无特征 → 非数据目录", !BackupPackage.HasDshData(new DirSnapshot("d", new string[] { "readme.txt" })));

        Check("有效包：名字+数据", BackupPackage.IsValidPackage(new DirSnapshot("dsh-data-1", new string[] { "settings.yaml" })));
        Check("有效包：名字+仅 _workspace", BackupPackage.IsValidPackage(new DirSnapshot("dsh-data-1", new string[] { "_workspace" })));
        Check("仅目录存在不算数（空目录）", !BackupPackage.IsValidPackage(new DirSnapshot("dsh-data-1", new string[] { })));
        Check("名字不符 → 无效（即使有数据）", !BackupPackage.IsValidPackage(new DirSnapshot("backup-1", new string[] { "settings.yaml" })));

        DirSnapshot self = new DirSnapshot("mydir", new string[] { "readme.txt" });
        Check("Resolve：自身有效 → 自身", BackupPackage.Resolve(new DirSnapshot("dsh-data-1", new string[] { "sessions" }), null) == "dsh-data-1");
        Check("Resolve：父目录下恰好一个有效备份 → 下探", BackupPackage.Resolve(self, new DirSnapshot[] { new DirSnapshot("dsh-data-1", new string[] { "profiles" }) }) == "dsh-data-1");
        Check("Resolve：两个备份子目录 → null（不猜）", BackupPackage.Resolve(self, new DirSnapshot[] { new DirSnapshot("dsh-data-1", new string[] { "profiles" }), new DirSnapshot("dsh-data-2", new string[] { "sessions" }) }) == null);
        Check("Resolve：唯一子目录但无效 → null", BackupPackage.Resolve(self, new DirSnapshot[] { new DirSnapshot("dsh-data-1", new string[] { "readme.txt" }) }) == null);
        Check("Resolve：无子目录 → null", BackupPackage.Resolve(self, new DirSnapshot[] { }) == null);

        Check("类型判定：手动为默认", BackupPackage.Classify("dsh-data-20260921-193000") == Dsht.Domain.Model.BackupKind.Manual);
        Check("类型判定：-auto", BackupPackage.Classify("dsh-data-x-auto") == Dsht.Domain.Model.BackupKind.Auto);
        Check("类型判定：四种 pre-*", BackupPackage.Classify("x-pre-restore") == Dsht.Domain.Model.BackupKind.PreRestore && BackupPackage.Classify("x-pre-import") == Dsht.Domain.Model.BackupKind.PreImport && BackupPackage.Classify("x-pre-wipe") == Dsht.Domain.Model.BackupKind.PreWipe && BackupPackage.Classify("x-pre-update") == Dsht.Domain.Model.BackupKind.PreUpdate);
        Check("保护性备份（严格模式）仅限 pre-*", BackupPackage.IsProtective(Dsht.Domain.Model.BackupKind.PreWipe) && !BackupPackage.IsProtective(Dsht.Domain.Model.BackupKind.Auto) && !BackupPackage.IsProtective(Dsht.Domain.Model.BackupKind.Manual));
        Console.WriteLine("[8] UptimeFormatter 运行时长（逐字对齐 v2.x FormatUptime）");
        Check("负数 → 0 秒（时钟回拨保护）", UptimeFormatter.Format(TimeSpan.FromSeconds(-5)) == "0 秒");
        Check("30 秒", UptimeFormatter.Format(TimeSpan.FromSeconds(30)) == "30 秒");
        Check("59 秒", UptimeFormatter.Format(TimeSpan.FromSeconds(59)) == "59 秒");
        Check("60 秒 → 1 分", UptimeFormatter.Format(TimeSpan.FromSeconds(60)) == "1 分");
        Check("59 分", UptimeFormatter.Format(TimeSpan.FromMinutes(59)) == "59 分");
        Check("1 小时 30 分", UptimeFormatter.Format(new TimeSpan(1, 30, 0)) == "1 小时 30 分");
        Check("23 小时 59 分", UptimeFormatter.Format(new TimeSpan(0, 23, 59, 0)) == "23 小时 59 分");
        Check("25 小时 → 1 天 1 小时", UptimeFormatter.Format(new TimeSpan(1, 1, 0, 0)) == "1 天 1 小时");
        Check("3 天 5 小时", UptimeFormatter.Format(new TimeSpan(3, 5, 0, 0)) == "3 天 5 小时");
        Console.WriteLine("[9] vendor 路径判定（平台无关）");
        Check("Windows 反斜杠路径命中", ProfileScanner.IsVendorPath("C:\\x\\node_modules\\pkg\\a.yml"));
        Check("Linux 正斜杠路径命中", ProfileScanner.IsVendorPath("/home/u/.dsh/profiles/node_modules/pkg/a.yml"));
        Check("普通路径不命中", !ProfileScanner.IsVendorPath("C:\\x\\profiles\\web\\cordis.patch.yml"));
        Check("大小写不敏感", ProfileScanner.IsVendorPath("C:\\x\\NODE_MODULES\\a.yml"));
        Check("空/空串不命中", !ProfileScanner.IsVendorPath("") && !ProfileScanner.IsVendorPath(null));
        Console.WriteLine("[10] 备份类型显示标签（逐字对齐 v2.x BackupKindName）");
        Check("Manual", BackupPackage.KindLabel(Dsht.Domain.Model.BackupKind.Manual) == "Manual");
        Check("Auto", BackupPackage.KindLabel(Dsht.Domain.Model.BackupKind.Auto) == "Auto");
        Check("PreRestore/PreImport/PreUpdate/PreWipe",
            BackupPackage.KindLabel(Dsht.Domain.Model.BackupKind.PreRestore) == "PreRestore"
            && BackupPackage.KindLabel(Dsht.Domain.Model.BackupKind.PreImport) == "PreImport"
            && BackupPackage.KindLabel(Dsht.Domain.Model.BackupKind.PreUpdate) == "PreUpdate"
            && BackupPackage.KindLabel(Dsht.Domain.Model.BackupKind.PreWipe) == "PreWipe");
        Check("标签与 Classify 往返一致", BackupPackage.KindLabel(BackupPackage.Classify("dsh-data-x-pre-update")) == "PreUpdate");
        Console.WriteLine("[11] doctor 领域纯函数（汇总/级别/大小/脱敏/备份天数）");
        List<DocItem> di = new List<DocItem>();
        Check("空列表 → DOCTOR_OK 0", DoctorSummary.Summary(di) == "DOCTOR_OK 0");
        di.Add(new DocItem("System", 0, "x"));
        Check("全 OK → DOCTOR_OK 0", DoctorSummary.Summary(di) == "DOCTOR_OK 0");
        di.Add(new DocItem("Backup", 1, "y"));
        Check("有 WARN → DOCTOR_WARN 1", DoctorSummary.Summary(di) == "DOCTOR_WARN 1");
        di.Add(new DocItem("Harness", 2, "z"));
        Check("有 ERROR → ERROR 优先", DoctorSummary.Summary(di) == "DOCTOR_ERROR 1");
        Check("级别名", DoctorSummary.Level(0) == "OK" && DoctorSummary.Level(1) == "WARN" && DoctorSummary.Level(2) == "ERROR");

        Check("大小：B", SizeFormatter.Human(512) == "512 B");
        Check("大小：KB 一位小数", SizeFormatter.Human(2048) == "2.0 KB");
        Check("大小：MB 一位小数", SizeFormatter.Human(3 * 1024L * 1024) == "3.0 MB");
        Check("大小：GB 两位小数", SizeFormatter.Human(2 * 1024L * 1024 * 1024) == "2.00 GB");

        Check("脱敏：URL token", ReportSanitizer.Sanitize("http://x/?token=abc123&b=1").IndexOf("abc123") < 0);
        Check("脱敏：key: value", ReportSanitizer.Sanitize("api_key: sk-abcdef").IndexOf("sk-abcdef") < 0);
        Check("脱敏：40+ 位 hex", ReportSanitizer.Sanitize("hash 0123456789abcdef0123456789abcdef01234567").IndexOf("0123456789abcdef") < 0);
        Check("脱敏：普通文本不动", ReportSanitizer.Sanitize("dsh 已安装: C:\\npm\\dsh.cmd") == "dsh 已安装: C:\\npm\\dsh.cmd");

        DateTime now = new DateTime(2026, 9, 28, 12, 0, 0);
        Check("备份天数：3 天前", BackupAge.DaysSince("dsh-data-20260925-120000000-auto", now) == 3);
        Check("备份天数：名字前缀不符 → null", BackupAge.DaysSince("other-20260925-120000000", now) == null);
        Check("备份天数：时间戳非法 → null", BackupAge.DaysSince("dsh-data-notatimestamp", now) == null);
        Console.WriteLine("[12] Linux 平台侧纯逻辑（ss 解析 / 路径解析）");
        string ss = "State  Recv-Q Send-Q Local Address:Port Peer Address:Port Process\n" +
                    "LISTEN 0      511          127.0.0.1:3080      0.0.0.0:*    users:((\"node\",pid=4242,fd=22))\n" +
                    "LISTEN 0      511              [::]:3080         [::]:*    users:((\"node\",pid=4242,fd=23))\n";
        Check("ss 解析命中 pid", Dsht.Platform.Linux.LinuxProcessQuery.ParseSsOutput(ss, 3080) == 4242);
        Check("ss 解析：端口不符 → 0", Dsht.Platform.Linux.LinuxProcessQuery.ParseSsOutput(ss, 9999) == 0);
        Check("ss 解析：空输入 → 0", Dsht.Platform.Linux.LinuxProcessQuery.ParseSsOutput("", 3080) == 0);
        Check("ss 解析：无 pid= → 0", Dsht.Platform.Linux.LinuxProcessQuery.ParseSsOutput("LISTEN 0 511 127.0.0.1:3080 0.0.0.0:*", 3080) == 0);
        Check("Linux 命令行判定（含 dsh）", Dsht.Platform.Linux.LinuxProcessQuery.IsDshCommandLineText("node /usr/lib/node_modules/@deepseek-ai/dsh/lib/bin.js"));
        Check("Linux 命令行判定（无关进程）", !Dsht.Platform.Linux.LinuxProcessQuery.IsDshCommandLineText("nginx: worker process"));

        string oldDshHome = Environment.GetEnvironmentVariable("DSH_HOME");
        try
        {
            Environment.SetEnvironmentVariable("DSH_HOME", "/tmp/dsh-home-probe");
            Check("Linux DataRoot 优先取 DSH_HOME", new Dsht.Platform.Linux.LinuxPaths().DataRoot == System.IO.Path.GetFullPath("/tmp/dsh-home-probe"));
            Environment.SetEnvironmentVariable("DSH_HOME", null);
            string ldr2 = new Dsht.Platform.Linux.LinuxPaths().DataRoot;
            Check("Linux DataRoot 回退到 <home>/.dsh", ldr2 != null && ldr2.EndsWith(".dsh"));
            // ★ 架构审计（S3）：这里原来断言的是**死代码** `IPaths.BackupsRoot` ✗（已随接口成员删除 ✓）
            // ★★ 架构审计抓到（G3）：上面那条测的是 **IPaths.BackupsRoot** ✗ —— 而它**没有任何生产调用点** ✗
            //   → 真正在用的是 `IBackupSource.BackupsRoot` ✓ 而它**从来没被测过** ✗✗
            //   → 这正是"我那次归一化修复修在了死代码里"**没有任何门槛能发现**的原因 ✓
            // ✓ 现在：**测真正在用的那个** ✓✓（相对环境变量必须被归一化成绝对路径 ✓）
            {
                string oldBkProbe = Environment.GetEnvironmentVariable("DSH_MINATO_BACKUP_DIR");
                try
                {
                    Environment.SetEnvironmentVariable("DSH_MINATO_BACKUP_DIR", "rel-bk-probe");
                    string liveBk = new Dsht.Platform.Linux.LinuxBackupSource(new Dsht.Platform.Linux.LinuxPaths()).BackupsRoot;
                    Check("Linux IBackupSource.BackupsRoot：相对环境变量被归一化成绝对路径（**真正在用的那个** ✓）",
                        !string.IsNullOrEmpty(liveBk) && System.IO.Path.IsPathRooted(liveBk));
                }
                finally { Environment.SetEnvironmentVariable("DSH_MINATO_BACKUP_DIR", oldBkProbe); }
            }
        }
        finally { Environment.SetEnvironmentVariable("DSH_HOME", oldDshHome); }
        Console.WriteLine("[13] 组合服务目标与预留形态（桌面端驱动的核心语义）");
        FakePort cp = new FakePort(); FakeHttp ch = new FakeHttp(); FakeProc cq = new FakeProc();
        WebTarget web = Make(cp, ch, cq);
        ReservedTarget hd = new ReservedTarget(AppKind.Headless, "test");
        ReservedTarget dt = new ReservedTarget(AppKind.Desktop, "test");
        CompositeServiceTarget comp = new CompositeServiceTarget(new IServiceTarget[] { web, hd, dt });

        cp.Open = true; ch.Ready = true;
        ServiceReport cr1 = comp.Probe();
        Check("web 就绪 → 组合选 web", cr1.Kind == AppKind.Web && cr1.State == ServiceState.Ready);
        Check("组合 Kind 反映所选形态", comp.Kind == AppKind.Web);

        ch.Ready = false; cq.Pid = 9; cq.IsDsh = true;
        Check("web Listening/Ready 优先于预留形态", comp.Probe().Kind == AppKind.Web);

        cp.Open = false;
        ServiceReport cr2 = comp.Probe();
        Check("web 停 + 预留形态全 Down → 报 Unknown/Down（不假装 Ready）", cr2.Kind == AppKind.Unknown && cr2.State == ServiceState.Down);
        Check("组合 Basis 说明已尝试的形态", cr2.Basis.IndexOf("Headless") >= 0 && cr2.Basis.IndexOf("Desktop") >= 0);
        Check("组合 Describe 列出各形态", comp.Describe().IndexOf("dsh web") >= 0 && comp.Describe().IndexOf("Desktop") >= 0);

        Check("预留形态永不 Ready", hd.Probe().State == ServiceState.Down && !hd.IsAvailable());
        Check("预留形态 Basis 说明原因", hd.Probe().Basis.IndexOf("预留") >= 0);
        Check("空组合 → Unknown/Down", new CompositeServiceTarget(new IServiceTarget[0]).Probe().Kind == AppKind.Unknown);
        Console.WriteLine("[14] 配置解析/序列化/校验（逐条对齐 v2.x）");
        System.Func<string,string> canon = delegate(string wsPath) { if (wsPath.IndexOf('<') >= 0) throw new Exception("bad"); return wsPath; };
        ToolkitConfig def = ConfigCodec.Parse(null, canon);
        Check("空配置 → 默认值", def.Lang == "auto" && def.Host == "127.0.0.1" && def.KeepBackups == 10 && def.CheckUpdate && def.AutoStart && def.UpdateChannel == "stable" && def.CloseAction == "");

        ToolkitConfig c1 = ConfigCodec.Parse("lang=zh\nhost=localhost\nws=C:\\ws\nkeep_backups=2\ncheck_update=off\ncheck_dsh_update=off\nupdate_channel=rc\nclose_action=tray\nauto_start=off\ndsh_versions=1.0,1.1\n", canon);
        Check("逐键解析", c1.Lang == "zh" && c1.Host == "localhost" && c1.Workspace == "C:\\ws" && c1.KeepBackups == 3 && !c1.CheckUpdate && !c1.CheckDshUpdate && c1.UpdateChannel == "rc" && c1.CloseAction == "tray" && !c1.AutoStart && c1.DshVersions == "1.0,1.1");
        Check("非法 lang → auto", ConfigCodec.Parse("lang=xx\n", canon).Lang == "auto");
        Check("非法 close_action → 空（回到未询问）", ConfigCodec.Parse("close_action=whatever\n", canon).CloseAction == "");
        Check("非法 update_channel → stable", ConfigCodec.Parse("update_channel=beta\n", canon).UpdateChannel == "stable");
        Check("非法 host 保留默认", ConfigCodec.Parse("host=evil.com\n", canon).Host == "127.0.0.1");
        Check("ws 规范化失败 → null", ConfigCodec.Parse("ws=a<b\n", canon).Workspace == null);

        string ser = ConfigCodec.Serialize(new ToolkitConfig());
        Check("序列化键顺序与 v2.x 一致", ser.StartsWith("lang=auto\r\nhost=127.0.0.1\r\nws=\r\nkeep_backups=10\r\ncheck_update=on\r\ncheck_dsh_update=on\r\ndsh_versions=\r\nupdate_channel=stable\r\nclose_action=\r\nauto_start=on\r\n"));
        ToolkitConfig rt = ConfigCodec.Parse(ser, canon);
        Check("序列化→解析往返一致", rt.Lang == "auto" && rt.Host == "127.0.0.1" && rt.KeepBackups == 10 && rt.CheckUpdate && rt.AutoStart);

        Check("校验：空键 → no-key", ConfigValidator.Validate("", "x", canon) == "no-key");
        Check("校验：未知键 → unknown-key", ConfigValidator.Validate("dsh_versions", "x", canon) == "unknown-key");
        Check("校验：lang 合法/非法", ConfigValidator.Validate("lang", "en", canon) == null && ConfigValidator.Validate("lang", "fr", canon) == "bad-value");
        Check("校验：keep_backups 必须 ≥3", ConfigValidator.Validate("keep_backups", "2", canon) == "bad-value" && ConfigValidator.Validate("keep_backups", "3", canon) == null);
        Check("校验：ws 走注入的规范化", ConfigValidator.Validate("ws", "a<b", canon) == "bad-value" && ConfigValidator.Validate("ws", "C:\\ok", canon) == null);
        ToolkitConfig ap = ConfigValidator.ApplyTo(new ToolkitConfig(), "keep_backups", "2", canon);
        Check("应用：keep_backups 夹到 3", ap.KeepBackups == 3);
        Console.WriteLine("[15] bootdiag（启动失败堆栈解析，逐条对齐 v2.x）");
        string frag2;
        Check("file:/// URL → 路径 + 片段", Dsht.Domain.Services.FileUrlConverter.ToPath("file:///C:/a/b.yml#ent", out frag2) == "C:\\a\\b.yml" && frag2 == "ent");
        Check("URL 解码 %20", Dsht.Domain.Services.FileUrlConverter.ToPath("file:///C:/a%20b/c.yml", out frag2) == "C:\\a b\\c.yml");
        Check("空 URL → 空", Dsht.Domain.Services.FileUrlConverter.ToPath(null, out frag2) == "" && frag2 == "");
        Check("Linux 路径**不**被转成反斜杠（共享代码审计抓到的高严重度 bug ✗）", Dsht.Domain.Services.FileUrlConverter.ToPath("file:///home/u/a.yml#x", out frag2) == "/home/u/a.yml" && frag2 == "x");
        Check("Linux 路径的 %20 解码后仍是正斜杠", Dsht.Domain.Services.FileUrlConverter.ToPath("file:///home/u/a%20b/c.yml", out frag2) == "/home/u/a b/c.yml");

        string yml = "insert:\n  - id: subagent-acp-demo-suffix\n    name: '@deepseek-ai/dsh-subagent-acp'\n";
        Check("EntryLocator 命中行号", Dsht.Domain.Services.EntryLocator.FindLine(yml, "subagent-acp-demo-suffix") == 2);
        Check("EntryLocator 未命中 → 0", Dsht.Domain.Services.EntryLocator.FindLine(yml, "nope") == 0);

        string fail = "Error: plugin tree failed to load\n  failed to apply loader entry include (cordis:include)\n  failed to apply loader entry subagent-acp-demo-suffix (@deepseek-ai/dsh-subagent-acp)\n  provider \"demo-suffix\" cannot enforce maxDepth\n  at file:///C:/x/y.yml#subagent-acp-demo-suffix\n  set maxDepth: 'provider-managed'\n";
        BootDiagResult br = Dsht.Domain.Services.BootDiagParser.Parse(fail, Loc7, ExistsTrue);
        Check("识别 + Kind=maxDepth-missing", br.Recognized && br.Kind == "maxDepth-missing");
        Check("取带 @ 的包名（最内层）", br.Plugin == "@deepseek-ai/dsh-subagent-acp" && br.Entry == "subagent-acp-demo-suffix");
        Check("Hint 取自输出", br.Hint == "set maxDepth: 'provider-managed'");
        Check("FILE/LINE 来自定位结果", br.File == "C:\\x\\y.yml" && br.Line == 7);

        string unknown = "some random crash\nError: boom\n";
        BootDiagResult bu = Dsht.Domain.Services.BootDiagParser.Parse(unknown, null, null);
        Check("未识别 → Recognized=false 且 KIND unknown", !bu.Recognized && bu.Kind == "unknown");
        Check("未识别时 FirstError 被首个 Error 行覆盖（与 v2.x 两阶段行为一致）", bu.FirstError == "Error: boom");

        string pkgOnly = "plugin tree failed to load\n  failed to apply loader entry e1 (plain-plugin)\n";
        BootDiagResult bp = Dsht.Domain.Services.BootDiagParser.Parse(pkgOnly, null, null);
        Check("无 @ 包名 → 退回最后一个匹配", bp.Recognized && bp.Plugin == "plain-plugin" && bp.Entry == "e1");
        Check("无 set maxDepth → 默认提示", bp.Hint == "set maxDepth: 'provider-managed'");
        Check("TextClipper 短串不变", Dsht.Domain.Services.TextClipper.Clip("abc", 200) == "abc");
        Console.WriteLine("[16] dryrun 领域纯函数（路径判定 / 跳过规则 / 合并计划）");
        Check("TrimTrailingSep：普通目录", Dsht.Domain.Services.PathUtil.TrimTrailingSep("C:\\a\\b\\") == "C:\\a\\b");
        Check("TrimTrailingSep：盘根保留", Dsht.Domain.Services.PathUtil.TrimTrailingSep("D:\\") == "D:\\");
        Check("IsSubPath：子树内", Dsht.Domain.Services.PathUtil.IsSubPath("C:\\bk", "C:\\bk\\dsh-data-1"));
        Check("IsSubPath：相等算在内", Dsht.Domain.Services.PathUtil.IsSubPath("C:\\bk\\", "C:\\bk"));
        Check("IsSubPath：外部不算", !Dsht.Domain.Services.PathUtil.IsSubPath("C:\\bk", "C:\\other"));
        Check("IsSubPath：大小写不敏感", Dsht.Domain.Services.PathUtil.IsSubPath("C:\\BK", "c:\\bk\\x"));

        Check("跳过：node_modules", Dsht.Domain.Services.SkipRules.SkipDir("node_modules", false));
        Check("跳过：backup（防自嵌套）", Dsht.Domain.Services.SkipRules.SkipDir("backup", false));
        Check("跳过：dsh-data-*（防嵌套备份）", Dsht.Domain.Services.SkipRules.SkipDir("dsh-data-20260101-000000-auto", false));
        Check("跳过：reparse point", Dsht.Domain.Services.SkipRules.SkipDir("normal", true));
        Check("不跳过：普通目录", !Dsht.Domain.Services.SkipRules.SkipDir("sessions", false));

        System.Collections.Generic.Dictionary<string, long> sm = new System.Collections.Generic.Dictionary<string, long>();
        sm["a.txt"] = 10; sm["sub/b.txt"] = 20; sm["sub/c.txt"] = 30;
        System.Collections.Generic.Dictionary<string, long> dm = new System.Collections.Generic.Dictionary<string, long>();
        dm["a.txt"] = 5; dm["only-here.txt"] = 99;
        long[] pl = Dsht.Domain.Services.MergePlanner.Plan(sm, dm);
        Check("计划：新增 2 覆盖 1 保留 1 字节 60", pl[0] == 2 && pl[1] == 1 && pl[2] == 1 && pl[3] == 60);
        long[] pl2 = Dsht.Domain.Services.MergePlanner.Plan(null, dm);
        Check("计划：空源 → 全保留、0 字节", pl2[0] == 0 && pl2[1] == 0 && pl2[2] == 2 && pl2[3] == 0);
        long[] pl3 = Dsht.Domain.Services.MergePlanner.Plan(sm, null);
        Check("计划：空目标 → 全新", pl3[0] == 3 && pl3[1] == 0 && pl3[2] == 0 && pl3[3] == 60);
        Console.WriteLine("[17] 版本号处理（对齐 v2.x；含命令注入白名单）");
        Check("Core：去预发布段", Dsht.Domain.Services.VersionComparer.Core("0.1.1-rc.2") == "0.1.1");
        Check("IsClean：1-3 段数字", Dsht.Domain.Services.VersionComparer.IsClean("2.7.2") && Dsht.Domain.Services.VersionComparer.IsClean("2") && !Dsht.Domain.Services.VersionComparer.IsClean("2.7.2.1") && !Dsht.Domain.Services.VersionComparer.IsClean("2.x"));
        Check("白名单：合法版本", Dsht.Domain.Services.VersionComparer.IsValidNpmVersion("0.1.5-rc.2") && Dsht.Domain.Services.VersionComparer.IsValidNpmVersion("2.7.2"));
        Check("白名单：拒绝命令注入字符", !Dsht.Domain.Services.VersionComparer.IsValidNpmVersion("1.0.0 & del x") && !Dsht.Domain.Services.VersionComparer.IsValidNpmVersion("1.0.0;rm") && !Dsht.Domain.Services.VersionComparer.IsValidNpmVersion("1.0.0|x") && !Dsht.Domain.Services.VersionComparer.IsValidNpmVersion("1.0.0$(x)") && !Dsht.Domain.Services.VersionComparer.IsValidNpmVersion("1.0.0\"x"));
        Check("白名单：只允许一个 -", !Dsht.Domain.Services.VersionComparer.IsValidNpmVersion("1.0.0-rc-2"));
        Check("Sanitize：去前导 v", Dsht.Domain.Services.VersionComparer.SanitizeLatest("v2.7.2") == "2.7.2");
        Check("Sanitize：非法 → null", Dsht.Domain.Services.VersionComparer.SanitizeLatest("1.0.0 && x") == null && Dsht.Domain.Services.VersionComparer.SanitizeLatest("") == null);
        Check("比较：核心段", Dsht.Domain.Services.VersionComparer.Compare("2.7.2", "2.7.1") > 0 && Dsht.Domain.Services.VersionComparer.Compare("2.7.2", "2.7.2") == 0);
        Check("比较：正式版高于预发布", Dsht.Domain.Services.VersionComparer.Compare("2.1.2", "2.1.2-rc") > 0);
        Check("比较：rc.1 < rc.2 < rc.10（数字段数值序）", Dsht.Domain.Services.VersionComparer.Compare("0.1.5-rc.1", "0.1.5-rc.2") < 0 && Dsht.Domain.Services.VersionComparer.Compare("0.1.5-rc.2", "0.1.5-rc.10") < 0);
        Check("比较：缺段更低（rc < rc.1）", Dsht.Domain.Services.VersionComparer.Compare("1.0.0-rc", "1.0.0-rc.1") < 0);
        Check("比较：段数不齐按 0 补", Dsht.Domain.Services.VersionComparer.Compare("1.0", "1.0.0") == 0);
        Console.WriteLine("[18] 路径校验（restore/export/delete，对齐 v2.x 原因键）");
        System.Func<string,bool> valid = delegate(string vp) { return true; };
        System.Func<string,bool> invalid = delegate(string vp) { return false; };
        System.Func<string,bool> exists = delegate(string vp) { return true; };
        System.Func<string,string> full = delegate(string vp) { return vp; };
        Check("restore：空 → no-path", Dsht.Domain.Services.PathValidator.ValidateRestorePath("", "C:\\bk", valid) == "no-path");
        Check("restore：根外 → outside", Dsht.Domain.Services.PathValidator.ValidateRestorePath("C:\\other", "C:\\bk", valid) == "outside");
        Check("restore：无效 → invalid", Dsht.Domain.Services.PathValidator.ValidateRestorePath("C:\\bk\\x", "C:\\bk", invalid) == "invalid");
        Check("restore：通过 → null", Dsht.Domain.Services.PathValidator.ValidateRestorePath("C:\\bk\\dsh-data-1", "C:\\bk", valid) == null);
        Check("restore：去引号", Dsht.Domain.Services.PathValidator.ValidateRestorePath("\"C:\\bk\\dsh-data-1\"", "C:\\bk", valid) == null);
        Check("export：无目标 → no-to", Dsht.Domain.Services.PathValidator.ValidateExport("C:\\bk\\dsh-data-1", "", "C:\\bk", exists, full) == "no-to");
        Check("export：嵌套目标 → nested", Dsht.Domain.Services.PathValidator.ValidateExport("C:\\bk\\dsh-data-1", "C:\\bk\\dsh-data-1\\sub", "C:\\bk", exists, full) == "nested");
        Check("export：目标等于源 → nested", Dsht.Domain.Services.PathValidator.ValidateExport("C:\\bk\\dsh-data-1", "C:\\bk\\dsh-data-1", "C:\\bk", exists, full) == "nested");
        Check("delete：非备份名 → not-backup", Dsht.Domain.Services.PathValidator.ValidateDeletePath("C:\\bk\\other", "C:\\bk", exists) == "not-backup");
        Check("delete：不存在 → not-found", Dsht.Domain.Services.PathValidator.ValidateDeletePath("C:\\bk\\dsh-data-1", "C:\\bk", invalid) == "not-found");
        Check("delete：通过 → null", Dsht.Domain.Services.PathValidator.ValidateDeletePath("C:\\bk\\dsh-data-1", "C:\\bk", exists) == null);
        Console.WriteLine("[19] Windows 数据根支持 DSH_HOME（隔离测试与多环境部署的前提）");
        // ★★★ **CI 红修复（2026-10-01 实测：ubuntu-latest 上 333/334 · 唯一失败就是这里）** ✓✓
        //   ✗ 这一段**没有平台门控** ✗ —— 而它测的是 `Dsht.Platform.Windows.WindowsPaths` ✓
        //     → 在 Linux 上 `C:\tmp\v3-win-home` **不是绝对路径** ✗
        //       → `Path.GetFullPath` 会把它接到**当前工作目录**后面 ✓ → 断言必然失败 ✗
        //     → 于是**每次推 v3-linux/main，ubuntu 那个 job 都是红的** ✗✗
        //       （windows 那个 job 照样绿 ✓ —— 所以本地在 Windows 上跑永远看不到 ✓）
        //   ✓ 现在：**非 Windows 上如实跳过** ✓✓（并说明由 windows-latest job 覆盖 ✓ 不是掩盖 ✓）
        //   ✓ 用 `Environment.OSVersion.Platform` 而不是 `OperatingSystem.IsWindows()` ✓
        //     —— 因为这份源码**同时**被 csc（.NET Framework 4.x）和 dotnet（net8）编译 ✓
        bool onWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        if (!onWindows)
        {
            Console.WriteLine("  [SKIP] WindowsPaths 用例在非 Windows 上跳过（Windows 专属 ✓ 由 windows-latest job 覆盖 ✓）");
        }
        else
        {
        string oldWinHome = Environment.GetEnvironmentVariable("DSH_HOME");
        try
        {
            Environment.SetEnvironmentVariable("DSH_HOME", @"C:\tmp\v3-win-home");
            Check("Windows DataRoot 优先取 DSH_HOME", new Dsht.Platform.Windows.WindowsPaths().DataRoot == @"C:\tmp\v3-win-home");
            // ★ 架构审计（S3）：这里原来断言的是**死代码** `IPaths.BackupsRoot` ✗（已随接口成员删除 ✓）
            Environment.SetEnvironmentVariable("DSH_HOME", null);
            string wdr = new Dsht.Platform.Windows.WindowsPaths().DataRoot;
            Check("未设置时回退到 <home>/.dsh（与 v2.x 一致）", wdr != null && wdr.EndsWith(".dsh"));
        }
        finally { Environment.SetEnvironmentVariable("DSH_HOME", oldWinHome); }
        }
        Console.WriteLine("[20] restore --apply 准入（V3 独有：真实写盘只允许隔离数据根）");
        string[] defs = new string[] { @"C:\Users\u\.dsh", @"C:\Users\u\AppData\Roaming\.dsh", @"C:\Users\u\AppData\Local\.dsh" };
        Check("未给 --apply → 判定不介入（放行）", Dsht.Domain.Services.RestoreApplyPolicy.Judge(false, null, null, defs) == null);
        Check("--apply + 未设 $DSH_HOME → apply-needs-dsh-home", Dsht.Domain.Services.RestoreApplyPolicy.Judge(true, null, @"D:\iso\data", defs) == Dsht.Domain.Services.RestoreApplyPolicy.NeedsDshHome);
        Check("--apply + $DSH_HOME 空白 → apply-needs-dsh-home", Dsht.Domain.Services.RestoreApplyPolicy.Judge(true, "   ", @"D:\iso\data", defs) == Dsht.Domain.Services.RestoreApplyPolicy.NeedsDshHome);
        Check("--apply + 数据根为空 → apply-needs-dsh-home", Dsht.Domain.Services.RestoreApplyPolicy.Judge(true, @"D:\iso\data", "  ", defs) == Dsht.Domain.Services.RestoreApplyPolicy.NeedsDshHome);
        Check("--apply + 数据根=默认位置 → apply-not-isolated", Dsht.Domain.Services.RestoreApplyPolicy.Judge(true, @"C:\Users\u\.dsh", @"C:\Users\u\.dsh", defs) == Dsht.Domain.Services.RestoreApplyPolicy.NotIsolated);
        Check("--apply + 默认位置（大小写/尾分隔符不敏感）→ apply-not-isolated", Dsht.Domain.Services.RestoreApplyPolicy.Judge(true, @"C:\USERS\U\.DSH\", @"c:\users\u\.dsh", defs) == Dsht.Domain.Services.RestoreApplyPolicy.NotIsolated);
        Check("--apply + 默认位置的子目录 → 放行（不等于默认位置本身）", Dsht.Domain.Services.RestoreApplyPolicy.Judge(true, @"C:\Users\u\.dsh\sandbox", @"C:\Users\u\.dsh\sandbox", defs) == null);
        Check("--apply + 隔离数据根 → 放行", Dsht.Domain.Services.RestoreApplyPolicy.Judge(true, @"D:\iso\data", @"D:\iso\data", defs) == null);
        Check("--apply + 候选表为 null → 放行（无默认位置可判）", Dsht.Domain.Services.RestoreApplyPolicy.Judge(true, @"D:\iso\data", @"D:\iso\data", null) == null);
        Check("文案：中文 needs-dsh-home 提到 DSH_HOME", Dsht.Domain.Services.RestoreApplyPolicy.Message(Dsht.Domain.Services.RestoreApplyPolicy.NeedsDshHome, true).IndexOf("DSH_HOME") >= 0);
        Check("文案：英文 not-isolated 提到 isolated", Dsht.Domain.Services.RestoreApplyPolicy.Message(Dsht.Domain.Services.RestoreApplyPolicy.NotIsolated, false).IndexOf("isolated") >= 0);
        Check("文案：未知原因码有兜底", Dsht.Domain.Services.RestoreApplyPolicy.Message("whatever", true).Length > 0 && Dsht.Domain.Services.RestoreApplyPolicy.Message("whatever", false).Length > 0);
        string[] wdefs = Dsht.Platform.Windows.WindowsPaths.DefaultDataRoots();
        bool wdefsOk = wdefs.Length == 3;
        for (int i = 0; i < wdefs.Length; i++) if (!wdefs[i].EndsWith(".dsh")) wdefsOk = false;
        Check("Windows 默认数据根候选：3 个且都以 .dsh 结尾", wdefsOk);
        string[] ldefs = Dsht.Platform.Linux.LinuxPaths.DefaultDataRoots();
        Check("Linux 默认数据根候选：至少 1 个且以 .dsh 结尾", ldefs.Length >= 1 && ldefs[0].EndsWith(".dsh"));
        Console.WriteLine("[21] doctor --report 组装（纯函数，格式对齐 v2.x 的 ConfigSummary/LogSummary/报告）");
        Check("配置摘要：null → (无配置文件)", Dsht.Domain.Services.ConfigSummaryBuilder.Build(null) == "(无配置文件)");
        Check("配置摘要：只有注释与空行 → (空配置)", Dsht.Domain.Services.ConfigSummaryBuilder.Build("# c\r\n\r\n   \r\n") == "(空配置)");
        Check("配置摘要：非注释行以 ' ; ' 连接", Dsht.Domain.Services.ConfigSummaryBuilder.Build("# c\nlang=auto\nhost=127.0.0.1\n") == "lang=auto ; host=127.0.0.1");
        Check("配置摘要：行首尾空白去掉", Dsht.Domain.Services.ConfigSummaryBuilder.Build("  a=1  \n") == "a=1");
        Check("配置摘要：空文本 → (空配置)", Dsht.Domain.Services.ConfigSummaryBuilder.Build("") == "(空配置)");
        Check("日志摘要：null → (无日志)", Dsht.Domain.Services.LogSummaryBuilder.Build(null) == "(无日志)");
        Check("日志摘要：空文本 → 共 0 行（对齐 File.ReadAllLines）", Dsht.Domain.Services.LogSummaryBuilder.Build("") == "共 0 行；最近: ");
        Check("日志摘要：末尾换行不产生额外空行", Dsht.Domain.Services.LogSummaryBuilder.Build("a\nb\n") == "共 2 行；最近: a | b");
        Check("日志摘要：只保留最近 3 行", Dsht.Domain.Services.LogSummaryBuilder.Build("1\n2\n3\n4\n") == "共 4 行；最近: 2 | 3 | 4");
        Check("日志摘要：CRLF 也按行拆", Dsht.Domain.Services.LogSummaryBuilder.Build("x\r\ny\r\n") == "共 2 行；最近: x | y");
        List<DocItem> ritems = new List<DocItem>();
        ritems.Add(new DocItem("A", 0, "ok"));
        ritems.Add(new DocItem("A", 2, "bad"));
        ritems.Add(new DocItem("B", 1, "warn"));
        string rep = Dsht.Domain.Services.DoctorReport.Build("2026-09-28 12:00:00", "3.0.0-dev", "SYS", ritems, "cfg", "log", "DOCTOR_WARN 1");
        Check("报告：头部三行（时间/Toolkit/系统）", rep.IndexOf("生成时间: 2026-09-28 12:00:00") >= 0 && rep.IndexOf("Toolkit : 3.0.0-dev") >= 0 && rep.IndexOf("系统    : SYS") >= 0);
        Check("报告：分类小节标题只在换类时出现一次", rep.Split(new string[] { "-- A --" }, StringSplitOptions.None).Length - 1 == 1 && rep.IndexOf("-- B --") >= 0);
        Check("报告：条目带 [级别] 前缀", rep.IndexOf("  [OK] ok") >= 0 && rep.IndexOf("  [ERROR] bad") >= 0 && rep.IndexOf("  [WARN] warn") >= 0);
        Check("报告：含配置/日志摘要小节与结果行", rep.IndexOf("-- 配置摘要（脱敏） --") >= 0 && rep.IndexOf("-- 日志摘要（脱敏） --") >= 0 && rep.IndexOf("结果: DOCTOR_WARN 1") >= 0);
        Check("报告：条目与摘要都脱敏", Dsht.Domain.Services.DoctorReport.Build("t", "v", "s", null, "token=SECRET", "token=SECRET", "x").IndexOf("SECRET") < 0);
        Check("报告：items 为 null 也不炸", Dsht.Domain.Services.DoctorReport.Build("t", "v", "s", null, "c", "l", "x").IndexOf("-- 配置摘要（脱敏） --") >= 0);
        Console.WriteLine("[22] 工作区自动探测判定与解析（对齐 v2.x 的 LooksLikeWorkspace / WorkspaceRoot）");
        string uhome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] forb = Dsht.Platform.Windows.WindowsPaths.ForbiddenWorkspaceRoots();
        Check("拒绝：盘根 C:\\", !Dsht.Domain.Services.WorkspaceJudge.LooksLike(@"C:\", forb));
        Check("拒绝：用户主目录", !Dsht.Domain.Services.WorkspaceJudge.LooksLike(uhome, forb));
        Check("拒绝：主目录子树（Desktop）", !Dsht.Domain.Services.WorkspaceJudge.LooksLike(System.IO.Path.Combine(uhome, "Desktop"), forb));
        Check("拒绝：C:\\Users 整级", !Dsht.Domain.Services.WorkspaceJudge.LooksLike(System.IO.Path.GetDirectoryName(uhome), forb));
        Check("拒绝：Program Files", !Dsht.Domain.Services.WorkspaceJudge.LooksLike(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), forb));
        Check("拒绝：盘根保留名 $Recycle.Bin（大小写不敏感）", !Dsht.Domain.Services.WorkspaceJudge.LooksLike(@"D:\$RECYCLE.BIN", forb));
        Check("拒绝：盘根保留名 users", !Dsht.Domain.Services.WorkspaceJudge.LooksLike(@"D:\Users", forb));
        Check("接受：普通工作区目录", Dsht.Domain.Services.WorkspaceJudge.LooksLike(@"D:\work\myproj", forb));
        Check("接受：UNC 共享下的目录", Dsht.Domain.Services.WorkspaceJudge.LooksLike(@"\\nas\share\team", forb));
        Check("接受：UNC 根（第一段是服务器名，不误伤）", Dsht.Domain.Services.WorkspaceJudge.LooksLike(@"\\server\share", forb));
        Check("拒绝：空串与 null", !Dsht.Domain.Services.WorkspaceJudge.LooksLike("", forb) && !Dsht.Domain.Services.WorkspaceJudge.LooksLike(null, forb));
        System.Func<string, string> ident = delegate(string vp) { return vp; };
        System.Func<string, string> prefix = delegate(string vp) { return @"D:\abs\" + vp; };
        System.Func<string, string> boom = delegate(string vp) { throw new Exception("boom"); };
        System.Func<string, bool> yes = delegate(string vp) { return true; };
        System.Func<string, bool> no = delegate(string vp) { return false; };
        Check("解析：未配置 → 用自动探测结果", Dsht.Domain.Services.WorkspaceResolver.Resolve(null, @"D:\detected", ident, yes) == @"D:\detected");
        Check("解析：配置为空串 → 用自动探测结果", Dsht.Domain.Services.WorkspaceResolver.Resolve("", @"D:\detected", ident, yes) == @"D:\detected");
        Check("解析：配置了且存在 → 用配置（经绝对化）", Dsht.Domain.Services.WorkspaceResolver.Resolve(@"ws\sub", @"D:\detected", prefix, yes) == @"D:\abs\ws\sub");
        Check("解析：配置了但不存在 → null（不回退探测）", Dsht.Domain.Services.WorkspaceResolver.Resolve(@"D:\nope", @"D:\detected", ident, no) == null);
        Check("解析：绝对化抛异常 → null", Dsht.Domain.Services.WorkspaceResolver.Resolve(@"bad", @"D:\detected", boom, yes) == null);
        Console.WriteLine("[23] JsonLite + profile manifest 形态推断（零注入读 dsh 落盘事实）");
        Dsht.Domain.Services.JNode jn = Dsht.Domain.Services.JsonLite.Parse("{\"a\":1,\"b\":[true,null,\"x\"],\"c\":{\"d\":\"y\"}}");
        Check("JSON：对象/数组/数字/布尔/null/字符串", jn != null && jn.IsObject && jn.Get("a").AsNumber(0) == 1 && jn.Get("b").IsArray && jn.Get("b").Items.Count == 3);
        Check("JSON：Path 逐层取值", jn.Path("c", "d").AsString("") == "y");
        Check("JSON：字符串转义", Dsht.Domain.Services.JsonLite.Parse("\"a\\nb\\\"c\\\\d\\u4e2d\"").AsString("") == "a\nb\"c\\d中");
        Check("JSON：StringArray 跳过非字符串", Dsht.Domain.Services.JsonLite.Parse("[\"a\",1,\"b\",true]").AsStringArray().Length == 2);
        Check("JSON：缺括号 → null", Dsht.Domain.Services.JsonLite.Parse("{\"a\":1") == null);
        Check("JSON：尾逗号 → null", Dsht.Domain.Services.JsonLite.Parse("{\"a\":1,}") == null);
        Check("JSON：尾部垃圾 → null", Dsht.Domain.Services.JsonLite.Parse("{\"a\":1} x") == null);
        Check("JSON：空/空白 → null", Dsht.Domain.Services.JsonLite.Parse("") == null && Dsht.Domain.Services.JsonLite.Parse("   ") == null);
        Check("JSON：取不存在的成员/路径 → null", jn.Get("zzz") == null && jn.Path("c", "zzz") == null);
        string realManifest = "{\"name\":\"web\",\"private\":true,\"dependencies\":{},\"dsh\":{\"profile\":{\"bundles\":[\"@deepseek-ai/dsh-base\",\"@deepseek-ai/dsh-web-app\",\"example-search-plugin\"]}}}";
        Dsht.Domain.Services.ProfileManifestInfo pm = Dsht.Domain.Services.ProfileManifest.Parse(realManifest);
        Check("manifest：解析成功且取到 bundles", pm.Parsed && pm.Bundles.Length == 3 && pm.Name == "web");
        Check("manifest：形态推断 = web", pm.ConfiguredForm == AppKind.Web && Dsht.Domain.Services.ProfileManifest.FormName(pm.ConfiguredForm) == "web");
        Check("manifest：官方包与第三方插件分开", pm.OfficialBundles.Length == 2 && pm.ThirdPartyPlugins.Length == 1 && pm.ThirdPartyPlugins[0] == "example-search-plugin");
        Check("manifest：headless bundle → Headless", Dsht.Domain.Services.ProfileManifest.FromBundles(new string[] { "@deepseek-ai/dsh-base", "@deepseek-ai/dsh-headless" }) == AppKind.Headless);
        Check("manifest：acp bundle → Acp", Dsht.Domain.Services.ProfileManifest.FromBundles(new string[] { "@deepseek-ai/dsh-acp-app" }) == AppKind.Acp);
        Check("manifest：sdk bundle 不是可管理形态 → Unknown", Dsht.Domain.Services.ProfileManifest.FromBundles(new string[] { "@deepseek-ai/dsh-base", "@deepseek-ai/dsh-sdk-app" }) == AppKind.Unknown);
        Check("manifest：多个 app bundle 时最后一个生效（顺序叠加语义）", Dsht.Domain.Services.ProfileManifest.FromBundles(new string[] { "@deepseek-ai/dsh-web-app", "@deepseek-ai/dsh-headless" }) == AppKind.Headless);
        Check("manifest：没有 app bundle → Unknown", Dsht.Domain.Services.ProfileManifest.FromBundles(new string[] { "@deepseek-ai/dsh-base" }) == AppKind.Unknown);
        Check("manifest：缺 dsh.profile → 未解析（不猜）", !Dsht.Domain.Services.ProfileManifest.Parse("{\"name\":\"x\"}").Parsed);
        Check("manifest：格式不认 → 未解析", !Dsht.Domain.Services.ProfileManifest.Parse("{oops").Parsed && !Dsht.Domain.Services.ProfileManifest.Parse(null).Parsed);
        Check("manifest：bundles 非数组 → 未解析", !Dsht.Domain.Services.ProfileManifest.Parse("{\"dsh\":{\"profile\":{\"bundles\":\"x\"}}}").Parsed);
        Check("manifest：未知 bundle 名 → Unknown（不猜）", Dsht.Domain.Services.ProfileManifest.KindOfBundle("some-random-plugin") == AppKind.Unknown);
        Console.WriteLine("[24] 会话投影解析与派生指标（token / 缓存命中 / 速度 / 上下文压力）");
        string proj = "{\"version\":7,\"record\":{\"identity\":{\"createdAt\":1788517824758,\"cwd\":\"D:\\\\work\"},\"rows\":{"
            + "\"sessionStats\":{\"ver\":1,\"seq\":2,\"val\":{\"turns\":2,\"steps\":9,\"llmMs\":1000,\"toolMs\":500,\"ttftMs\":300,\"decodeMs\":2000,\"decodeTokens\":400}},"
            + "\"tokenUsage\":{\"val\":{\"totals\":{\"uncachedInputTokens\":100,\"outputTokens\":50,\"cacheReadTokens\":900,\"cacheWriteTokens\":10}}},"
            + "\"contextPressure\":{\"val\":{\"surfaceTokens\":500,\"contextWindow\":1000,\"pressureTokens\":250}},"
            + "\"contextBreakdown\":{\"val\":{\"systemTokens\":10,\"toolsTokens\":20,\"messageTokens\":30}},"
            + "\"sessionListMetadata\":{\"val\":{\"blank\":false,\"lastPromptAt\":1788517999999}},"
            + "\"title\":{\"val\":\"hello\"}}}}";
        Dsht.Domain.Model.SessionStat ps = Dsht.Domain.Services.SessionStats.ParseSessionProjection(proj, "abc");
        Check("投影：解析成功且 id 透传", ps != null && ps.Id == "abc");
        Check("投影：会话统计（turns/steps/title/cwd）", ps.Turns == 2 && ps.Steps == 9 && ps.Title == "hello" && ps.Cwd == @"D:\work");
        Check("投影：token 总量（输入 1000 / 输出 50 / 写缓存 10）", ps.UncachedInputTokens == 100 && ps.CacheReadTokens == 900 && ps.TotalInputTokens == 1000 && ps.OutputTokens == 50 && ps.CacheWriteTokens == 10);
        Check("投影：Has* 标记都置位", ps.HasStats && ps.HasTokens && ps.HasPressure && ps.HasBreakdown && !ps.Blank);
        Check("指标：缓存命中率 90.0%", Math.Abs(Dsht.Domain.Services.SessionStats.CacheHitPercent(ps) - 90.0) < 0.001);
        Check("指标：解码速度 200 tok/s", Math.Abs(Dsht.Domain.Services.SessionStats.DecodeTokensPerSec(ps) - 200.0) < 0.001);
        Check("指标：上下文压力 25.0%", Math.Abs(Dsht.Domain.Services.SessionStats.ContextPressurePercent(ps) - 25.0) < 0.001);
        Check("投影：epoch 毫秒 → UTC ISO", ps.CreatedAtEpochMs == 1788517824758L && ps.CreatedAt.EndsWith("Z") && ps.LastPromptEpochMs == 1788517999999L && ps.LastPromptAt.EndsWith("Z"));
        Check("epoch 转换：0/负数 → 空串（不假装时间）", Dsht.Domain.Services.SessionStats.EpochMsToIso(0) == "" && Dsht.Domain.Services.SessionStats.EpochMsToIso(-5) == "");
        Check("投影：缺 rows / 格式不认 / null → null", Dsht.Domain.Services.SessionStats.ParseSessionProjection("{\"record\":{}}", "x") == null && Dsht.Domain.Services.SessionStats.ParseSessionProjection("{oops", "x") == null && Dsht.Domain.Services.SessionStats.ParseSessionProjection(null, "x") == null);
        string agg = "{\"tables\":{\"sessions\":{"
            + "\"session-abc\":{\"identity\":{\"createdAt\":1,\"cwd\":\"C:\\\\w\"},\"rows\":{\"tokenUsage\":{\"val\":{\"totals\":{\"cacheReadTokens\":5,\"uncachedInputTokens\":5}}}}},"
            + "\"bareid\":{\"rows\":{\"tokenUsage\":{\"val\":{\"totals\":{\"outputTokens\":7}}}}}"
            + "}}}";
        Dsht.Domain.Model.SessionStat[] ag = Dsht.Domain.Services.SessionStats.ParseAggregate(agg);
        Check("总表：解析出 2 个会话且去掉 session- 前缀", ag.Length == 2 && ag[0].Id == "abc" && ag[1].Id == "bareid");
        Check("总表：token 字段读到（5/5 与 7）", ag.Length == 2 && ag[0].CacheReadTokens == 5 && ag[0].UncachedInputTokens == 5 && ag[1].OutputTokens == 7);
        Check("总表：格式不认 → 空数组（不抛）", Dsht.Domain.Services.SessionStats.ParseAggregate("{oops").Length == 0 && Dsht.Domain.Services.SessionStats.ParseAggregate(null).Length == 0);
        string snap = "{\"formatVersion\":1,\"sessions\":[{\"id\":\"s1\",\"title\":\"t\",\"turns\":3,\"steps\":4,\"uncachedInputTokens\":10,\"outputTokens\":20,\"cacheReadTokens\":30,\"decodeMs\":100,\"decodeTokens\":50,\"ttftMs\":60,\"contextWindow\":200,\"pressureTokens\":100,\"blank\":true}]}";
        Dsht.Domain.Model.SessionStat[] sn = Dsht.Domain.Services.SessionStats.ParseSnapshot(snap);
        Check("快照：解析成功（id/title/turns/blank）", sn.Length == 1 && sn[0].Id == "s1" && sn[0].Title == "t" && sn[0].Turns == 3 && sn[0].Blank);
        Check("快照：指标可算（命中 75%、解码 500 tok/s、压力 50%）", Math.Abs(Dsht.Domain.Services.SessionStats.CacheHitPercent(sn[0]) - 75.0) < 0.001 && Math.Abs(Dsht.Domain.Services.SessionStats.DecodeTokensPerSec(sn[0]) - 500.0) < 0.001 && Math.Abs(Dsht.Domain.Services.SessionStats.ContextPressurePercent(sn[0]) - 50.0) < 0.001);
        Check("快照：v1（无 live）仍被接受且 live=false", sn.Length == 1 && sn[0].Live == false);
        Dsht.Domain.Model.SessionStat[] s2 = Dsht.Domain.Services.SessionStats.ParseSnapshot(snap.Replace("\"formatVersion\":1", "\"formatVersion\":2").Replace("\"blank\":true", "\"blank\":true,\"live\":true"));
        Check("快照：v2（含 live）被接受且 live 生效", s2.Length == 1 && s2[0].Live);
        // ★ 2026-10-02：live ≠ 在动（用户实测"没在运行显示 运行6"→ 桌面端 store 里全是 live ✗）
        //   active/lastActiveAt 是 v2 的**可选追加** ✓ 老插件没有 → HasActive=false（未知 ✗ 不假装 ✗✓）
        Dsht.Domain.Model.SessionStat[] s3 = Dsht.Domain.Services.SessionStats.ParseSnapshot(
            snap.Replace("\"formatVersion\":1", "\"formatVersion\":2").Replace("\"blank\":true", "\"blank\":true,\"live\":true,\"active\":true,\"lastActiveAt\":\"2026-10-02T00:00:03Z\""));
        Check("快照：active=true 读到 + lastActiveAt 保留", s3.Length == 1 && s3[0].Active && s3[0].HasActive && s3[0].LastActiveAt == "2026-10-02T00:00:03Z");
        Check("快照：没有 active 字段 → HasActive=false（未知 ✓ 不是 false ✓✓）", s2.Length == 1 && !s2[0].HasActive && !s2[0].Active);
        Dsht.Domain.Model.SessionStat[] s4 = Dsht.Domain.Services.SessionStats.ParseSnapshot(
            snap.Replace("\"formatVersion\":1", "\"formatVersion\":2").Replace("\"blank\":true", "\"blank\":true,\"live\":true,\"active\":false"));
        Check("快照：active=false 读到（明确知道没动 ✓）", s4.Length == 1 && s4[0].HasActive && !s4[0].Active);
        // ★ DeepSeek 余额解析（2026-10-02 用户要求"余额检测" ✓ 纯函数 ✓ 形状不认 → 不认 ✗ 绝不冒充数字 ✓✓）
        string bj = "{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"110.00\",\"granted_balance\":\"10.00\",\"topped_up_balance\":\"100.00\"}]}";
        Dsht.Domain.Services.BalanceInfo b0 = Dsht.Domain.Services.BalanceJson.Parse(bj);
        Check("余额：充值/赠送/总计/币种逐字读到 ✓", b0.Parsed && b0.IsAvailable && b0.Currency == "CNY" && b0.Topup == "100.00" && b0.Granted == "10.00" && b0.Total == "110.00");
        Check("余额：垃圾/空 → 不认（绝不抛 ✗ 绝不假数字 ✓）",
            !Dsht.Domain.Services.BalanceJson.Parse("{\"oops\":1}").Parsed
            && !Dsht.Domain.Services.BalanceJson.Parse("").Parsed
            && !Dsht.Domain.Services.BalanceJson.Parse(null).Parsed
            && !Dsht.Domain.Services.BalanceJson.Parse("{\"is_available\":true,\"balance_infos\":[]}").Parsed);
        Check("余额：is_available 没给 → 当可用 ✗ 不因缺字段断言停用 ✗✓", Dsht.Domain.Services.BalanceJson.Parse("{\"balance_infos\":[{\"currency\":\"CNY\"}]}").IsAvailable);
        Check("余额：is_available=false 如实读出 ✓", !Dsht.Domain.Services.BalanceJson.Parse(bj.Replace("\"is_available\":true", "\"is_available\":false")).IsAvailable);
        Check("余额：多币种只取第一条 ✗ 不加总 ✗✓",
            Dsht.Domain.Services.BalanceJson.Parse("{\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"1\"},{\"currency\":\"USD\",\"total_balance\":\"2\"}]}").Total == "1");
        Check("快照：formatVersion 不认（v3）→ 空数组（诚实降级）", Dsht.Domain.Services.SessionStats.ParseSnapshot(snap.Replace("\"formatVersion\":1", "\"formatVersion\":3")).Length == 0);
        Check("快照：无 sessions 字段 → 空数组", Dsht.Domain.Services.SessionStats.ParseSnapshot("{\"formatVersion\":1}").Length == 0);
        List<Dsht.Domain.Model.SessionStat> tl = new List<Dsht.Domain.Model.SessionStat>();
        tl.Add(ps); tl.Add(sn[0]);
        Dsht.Domain.Model.SessionTotals tt = Dsht.Domain.Services.SessionStats.Aggregate(tl);
        Check("汇总：计数 + 合计 token（110/70/930）", tt.Count == 2 && tt.NonBlankCount == 1 && tt.UncachedInputTokens == 110 && tt.OutputTokens == 70 && tt.CacheReadTokens == 930);
        Check("汇总：命中率/速度按合计加权", Math.Abs(tt.CacheHitPercent - (930 * 100.0 / 1040)) < 0.001 && Math.Abs(tt.DecodeTokensPerSec - (450 * 1000.0 / 2100)) < 0.001);
        Dsht.Domain.Model.SessionTotals te = Dsht.Domain.Services.SessionStats.Aggregate(new List<Dsht.Domain.Model.SessionStat>());
        Check("汇总：空列表 → 0 且指标为未知（-1）", te.Count == 0 && te.CacheHitPercent < 0 && te.DecodeTokensPerSec < 0);
        Dsht.Domain.Model.SessionStat zero = new Dsht.Domain.Model.SessionStat();
        Check("指标：分母为 0 → 未知（-1，不假装 0）", Dsht.Domain.Services.SessionStats.CacheHitPercent(zero) < 0 && Dsht.Domain.Services.SessionStats.DecodeTokensPerSec(zero) < 0 && Dsht.Domain.Services.SessionStats.ContextPressurePercent(zero) < 0);
        Console.WriteLine();
        Console.WriteLine("[25] start/stop 判定（纯函数：只有可观测事实能判定成功）");
        Check("启动前：已在运行 → 不重复启动（Ready）", Dsht.Domain.Services.ServiceControlPolicy.BeforeStart("Ready", 123) == Dsht.Domain.Services.StartDecision.AlreadyRunning);
        Check("启动前：Listening 也算在运行", Dsht.Domain.Services.ServiceControlPolicy.BeforeStart("Listening", 123) == Dsht.Domain.Services.StartDecision.AlreadyRunning);
        Check("启动前：Down → 应该启动", Dsht.Domain.Services.ServiceControlPolicy.BeforeStart("Down", 0) == Dsht.Domain.Services.StartDecision.ShouldLaunch);
        Check("启动后：观测到 Ready → Started", Dsht.Domain.Services.ServiceControlPolicy.AfterLaunch("Ready", 9, 8) == Dsht.Domain.Services.StartOutcome.Started);
        Check("启动后：只发出命令、未观测到就绪 → NotObserved（不算成功）", Dsht.Domain.Services.ServiceControlPolicy.AfterLaunch("Down", 0, 8) == Dsht.Domain.Services.StartOutcome.NotObserved);
        Check("启动后：未知状态 → NotObserved（不猜）", Dsht.Domain.Services.ServiceControlPolicy.AfterLaunch("Unknown", 0, 8) == Dsht.Domain.Services.StartOutcome.NotObserved);
        Check("停止前：无 PID → 没什么可停", Dsht.Domain.Services.ServiceControlPolicy.BeforeStop("Ready", 0) == Dsht.Domain.Services.StopDecision.NothingToStop);
        Check("停止前：状态 Down（即使有 PID）→ 没什么可停", Dsht.Domain.Services.ServiceControlPolicy.BeforeStop("Down", 77) == Dsht.Domain.Services.StopDecision.NothingToStop);
        Check("停止前：在运行 → 应该停", Dsht.Domain.Services.ServiceControlPolicy.BeforeStop("Listening", 77) == Dsht.Domain.Services.StopDecision.ShouldStop);
        Check("停止后：观测到 Down → Stopped", Dsht.Domain.Services.ServiceControlPolicy.AfterStop("Down") == Dsht.Domain.Services.StopOutcome.Stopped);
        Check("停止后：仍能观测到 → StillListening（不谎报已停）", Dsht.Domain.Services.ServiceControlPolicy.AfterStop("Ready") == Dsht.Domain.Services.StopOutcome.StillListening);
        Check("IsRunning：大小写敏感（状态名是契约，不做宽松匹配）", Dsht.Domain.Services.ServiceControlPolicy.IsRunning("ready") == false && Dsht.Domain.Services.ServiceControlPolicy.IsRunning("Ready"));
        Console.WriteLine("[26] profilepatch 编辑计划（纯函数；语义对齐 v2.x）");
        string y1 = "insert:\n  - id: a\n    config:\n      x: 1\n";
        Dsht.Domain.Services.PatchPlan pd = Dsht.Domain.Services.PatchPlanner.PlanDisable(y1, "a");
        Check("禁用：计划有效且追加顶层行", pd.Valid && !pd.Noop && pd.NewText.Contains("- id: a") && pd.NewText.Contains("disabled: true"));
        Check("禁用：行号 = 末尾之后", pd.Line == 5);
        Check("禁用：写回后 HasDisabled 为真（自证）", Dsht.Domain.Services.PatchPlanner.HasDisabled(pd.NewText, "a"));
        Check("禁用：幂等（已禁用 → Noop）", Dsht.Domain.Services.PatchPlanner.PlanDisable(pd.NewText, "a").Noop);
        Check("禁用：条目不存在 → entry-not-found（不写无效补丁）", Dsht.Domain.Services.PatchPlanner.PlanDisable(y1, "nope").Reason == "entry-not-found");
        Check("禁用：空 id → empty-id", Dsht.Domain.Services.PatchPlanner.PlanDisable(y1, "  ").Reason == "empty-id");
        Check("禁用：保留原换行风格（CRLF 文件不混入 LF）", Dsht.Domain.Services.PatchPlanner.PlanDisable("insert:\r\n  - id: a\r\n", "a").NewText.Contains("\r\n- id: a\r\n"));
        Check("禁用：文件末尾无换行时先补一个", Dsht.Domain.Services.PatchPlanner.PlanDisable("- id: a", "a").NewText.StartsWith("- id: a\n- id: a"));
        Dsht.Domain.Services.PatchPlan pe = Dsht.Domain.Services.PatchPlanner.PlanEnable(pd.NewText, "a");
        Check("恢复：把该行的 disabled 改成 false（不删行）", pe.Valid && !pe.Noop && pe.NewText.Contains("disabled: false") && pe.NewText.Contains("- id: a"));
        Check("恢复：改完不再算禁用（往返一致）", !Dsht.Domain.Services.PatchPlanner.HasDisabled(pe.NewText, "a"));
        Check("恢复：未禁用 → Noop(not-disabled)", Dsht.Domain.Services.PatchPlanner.PlanEnable(y1, "a").Reason == "not-disabled");
        Check("恢复：条目不存在 → entry-not-found", Dsht.Domain.Services.PatchPlanner.PlanEnable(y1, "nope").Reason == "entry-not-found");
        Check("恢复：不误伤别的条目的 disabled 行", Dsht.Domain.Services.PatchPlanner.PlanEnable("- id: a\n  disabled: true\n- id: b\n  disabled: true\n", "a").NewText.Contains("- id: b\n  disabled: true"));
        Check("HasDisabled：只有属于该 id 的 disabled 才算", Dsht.Domain.Services.PatchPlanner.HasDisabled("- id: a\n- id: b\n  disabled: true\n", "a") == false && Dsht.Domain.Services.PatchPlanner.HasDisabled("- id: a\n- id: b\n  disabled: true\n", "b"));
        Console.WriteLine("[27] npm 返回值白名单（命令注入面）");
        Check("白名单：正常版本号通过", Dsht.Domain.Services.NpmVersionGuard.IsSafe("1.2.3") && Dsht.Domain.Services.NpmVersionGuard.IsSafe("0.1.5-rc.2"));
        Check("白名单：去空白与引号", Dsht.Domain.Services.NpmVersionGuard.Normalize("  1.2.3\n") == "1.2.3" && Dsht.Domain.Services.NpmVersionGuard.Normalize("\"1.2.3\"") == "1.2.3");
        Check("白名单：拒绝命令注入字符", !Dsht.Domain.Services.NpmVersionGuard.IsSafe("1.2.3; rm -rf /") && !Dsht.Domain.Services.NpmVersionGuard.IsSafe("1.2.3 && whoami") && !Dsht.Domain.Services.NpmVersionGuard.IsSafe("$(x)"));
        Check("白名单：拒绝空/超长/以 - 开头", !Dsht.Domain.Services.NpmVersionGuard.IsSafe("") && !Dsht.Domain.Services.NpmVersionGuard.IsSafe(null) && !Dsht.Domain.Services.NpmVersionGuard.IsSafe(new string('1', 65)) && !Dsht.Domain.Services.NpmVersionGuard.IsSafe("-1.2.3"));
        Check("白名单：必须含数字", !Dsht.Domain.Services.NpmVersionGuard.IsSafe("abc") && Dsht.Domain.Services.NpmVersionGuard.IsSafe("v1"));
        Check("白名单：npm 的错误输出被拒", !Dsht.Domain.Services.NpmVersionGuard.IsSafe("npm ERR! code E404"));
        Console.WriteLine("[28] profilepatch 往返：不堆积重复行（真机测试抓到的缺口）");
        string rt0 = "insert:\n  - id: demo\n    name: demo\n";
        string rt1 = Dsht.Domain.Services.PatchPlanner.PlanDisable(rt0, "demo").NewText;
        string rt2 = Dsht.Domain.Services.PatchPlanner.PlanEnable(rt1, "demo").NewText;
        string rt3 = Dsht.Domain.Services.PatchPlanner.PlanDisable(rt2, "demo").NewText;
        Check("往返：disable→enable→disable 后仍只有 1 行 - id: demo（不堆积）", CountTopRows(rt3) == 1);
        Check("往返：第三次 disable 是翻转而非追加（行数不变）", rt3.Split('\n').Length == rt2.Split('\n').Length);
        Check("往返：最终状态为 disabled: true 且无 disabled: false 残留", rt3.Contains("disabled: true") && !rt3.Contains("disabled: false"));
        Check("往返：enable→disable 再 enable 仍不堆积", CountTopRows(Dsht.Domain.Services.PatchPlanner.PlanEnable(rt3, "demo").NewText) == 1);
        // ============================================================================
        // [29] 备份 / 恢复 / 删除 **真实写盘往返**（架构审计 G2：唯一能毁数据的引擎原本零覆盖 ✗）
        //   为什么必须有：Create / Restore / Delete 是**唯一会动用户数据**的代码 ✓
        //   而在此之前**没有任何门槛**调用过它们 ✗（只在 CLI 真机跑过 ✓）
        //   安全：全程在 %TEMP% 的随机目录里 ✓ 备份根用 DSH_MINATO_BACKUP_DIR 指到那里 ✓
        //         **绝不触碰真实数据根** ✓ 结束即清理 ✓
        // ============================================================================
        Console.WriteLine("[29] 备份/恢复/删除：真实写盘往返（隔离目录）");
        {
            string bkRootTmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsht-bk-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string dataSrc = System.IO.Path.Combine(bkRootTmp, "data");
            string bkDirTmp = System.IO.Path.Combine(bkRootTmp, "bk");
            string dataDst = System.IO.Path.Combine(bkRootTmp, "restored");
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataSrc, "storages"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(dataSrc, "storages", "a.txt"), "ALPHA");
                System.IO.File.WriteAllText(System.IO.Path.Combine(dataSrc, "storages", "b.txt"), "BETA");
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataSrc, "sessions"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(dataSrc, "sessions", "s1.json"), "{\"x\":1}");
                System.IO.Directory.CreateDirectory(bkDirTmp);
                string oldEnv = Environment.GetEnvironmentVariable("DSH_MINATO_BACKUP_DIR");
                Environment.SetEnvironmentVariable("DSH_MINATO_BACKUP_DIR", bkDirTmp);

                Dsht.Domain.Abstractions.IBackupSource bks;
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    bks = new Dsht.Platform.Windows.WindowsBackupSource(new Dsht.Platform.Windows.WindowsPaths());
                else
                    bks = new Dsht.Platform.Linux.LinuxBackupSource(new Dsht.Platform.Linux.LinuxPaths());

                Check("备份根：相对/环境变量值被归一化成绝对路径", !string.IsNullOrEmpty(bks.BackupsRoot) && System.IO.Path.IsPathRooted(bks.BackupsRoot));

                Dsht.Domain.Model.BackupResult bkRes = bks.Create(dataSrc, Dsht.Domain.Model.BackupKind.Manual, 3, null);
                Check("备份：返回了包路径", bkRes != null && !string.IsNullOrEmpty(bkRes.Path));
                Check("备份：完成标记（.manifest）存在", bkRes != null && System.IO.File.Exists(bkRes.Path + ".manifest"));
                Check("备份：内容都在（storages + sessions）",
                    bkRes != null && System.IO.Directory.Exists(System.IO.Path.Combine(bkRes.Path, "storages"))
                    && System.IO.File.Exists(System.IO.Path.Combine(bkRes.Path, "sessions", "s1.json")));

                Dsht.Domain.Model.RestoreOutcome rsOut = bks.Restore(bkRes.Path, dataDst, null);
                Check("恢复：报告成功", rsOut != null && rsOut.Ok);
                Check("恢复：文件内容一致",
                    System.IO.File.Exists(System.IO.Path.Combine(dataDst, "storages", "a.txt"))
                    && System.IO.File.ReadAllText(System.IO.Path.Combine(dataDst, "storages", "a.txt")) == "ALPHA"
                    && System.IO.File.ReadAllText(System.IO.Path.Combine(dataDst, "storages", "b.txt")) == "BETA");
                Check("恢复：嵌套目录也回来了", System.IO.File.Exists(System.IO.Path.Combine(dataDst, "sessions", "s1.json")));

                string manifestTxt = System.IO.File.ReadAllText(bkRes.Path + ".manifest");
                // ★ 我的第一个断言写错了 ✓（实测抓到 ✓）：平台侧的 Create 只写 files=/bytes=/failed=/finished= ✓
                //   而 sha256= 是 **CLI 层** AddContentHashToMarker 补的 ✓（只有走 CLI 才有 ✓ 不是平台职责 ✓）
                Check("标记：含 files= 与 finished=（平台侧职责）", manifestTxt.Contains("files=") && manifestTxt.Contains("finished="));

                bks.Delete(bkRes.Path);
                Check("删除：包目录真的没了", !System.IO.Directory.Exists(bkRes.Path));

                Environment.SetEnvironmentVariable("DSH_MINATO_BACKUP_DIR", oldEnv);
            }
            catch (Exception ex)
            {
                Check("备份/恢复往返：未抛异常（" + ex.GetType().Name + ": " + ex.Message + "）", false);
            }
            finally
            {
                try { System.IO.Directory.Delete(bkRootTmp, true); } catch { }
            }
        }
        // ---- [30] 标记行自由文本往返（架构审计 G1：这条 CLI↔GUI 契约原本**没有任何测试** ✗）----
        Console.WriteLine("[30] MarkerText 往返：CLI 打印与 GUI 解析必须逐字一致");
        {
            string[] samples = new string[] {
                "会话 A%B", "a b c", "带\t制表符", "带\n换行", "带\r回车", "100% 完成", "%-not-a-code", "中文·标点，。！", "", "plain"
            };
            int roundTripOk = 0;
            for (int si = 0; si < samples.Length; si++)
            {
                string enc = Dsht.Domain.Services.MarkerText.Encode(samples[si]);
                string dec = Dsht.Domain.Services.MarkerText.Decode(enc);
                if (dec == samples[si]) roundTripOk++;
            }
            Check("往返：全部样例 encode→decode 回到原值（" + roundTripOk + "/" + samples.Length + "）", roundTripOk == samples.Length);
            Check("转义：空格 → %20 且百分号 → %25", Dsht.Domain.Services.MarkerText.Encode("a b") == "a%20b" && Dsht.Domain.Services.MarkerText.Encode("100%") == "100%25");
            Check("转义：空值 → -（标记行不用空串）", Dsht.Domain.Services.MarkerText.Encode("") == "-" && Dsht.Domain.Services.MarkerText.Encode(null) == "-");
            Check("解码：- 与空 → 空串；非法 % 原样保留",
                Dsht.Domain.Services.MarkerText.Decode("-") == "" && Dsht.Domain.Services.MarkerText.Decode("") == ""
                && Dsht.Domain.Services.MarkerText.Decode("100%") == "100%");
            // ★ 关键：**钉住 GUI 夹具里那个串** ✓ —— 若哪天只改了一边的规则 ✓ 这条会立刻红 ✓
            Check("契约：GUI 夹具串 %20/%25 解出来就是它期望的标题",
                Dsht.Domain.Services.MarkerText.Decode("会话%20A%25B") == "会话 A%B");
        }
        // ============================================================================
        // [31] 备份链审查（2026-10-07，D5/B1）：IsSubPath **根路径做父级**回归
        //   为什么必须有：TrimTrailingSep 特意保留盘根尾部（`D:\` ✓ 不变成 `D:` ✓）
        //   → 旧实现 `a + "\\"` 拼出 `D:\\` ✗ → 备份根设在盘根时根里**每一个包**都被判"在根外" ✗✗
        //   → backup-export / backup-delete 全灭 ✗（真机复现过 ✓ 修复记录有对照 ✓）
        // ============================================================================
        Console.WriteLine("[31] IsSubPath：根路径父级（备份链审查 D5 回归）");
        Check("盘根：D:\\ 包含 D:\\foo", PathUtil.IsSubPath("D:\\", "D:\\foo"));
        Check("盘根：D:\\ 不含 E:\\foo", !PathUtil.IsSubPath("D:\\", "E:\\foo"));
        Check("POSIX 根：/ 包含 /bk", PathUtil.IsSubPath("/", "/bk"));
        Check("POSIX 根：/ 等于自身", PathUtil.IsSubPath("/", "/"));
        Check("常规父级仍对：C:\\bk 包含 C:\\bk\\x", PathUtil.IsSubPath("C:\\bk", "C:\\bk\\x"));
        Check("前缀陷阱仍挡住：C:\\bk 不含 C:\\bkx", !PathUtil.IsSubPath("C:\\bk", "C:\\bkx"));
        Check("逃逸仍 fail-closed：C:\\..\\x 判不安全", !PathUtil.IsSubPath("C:\\bk", "C:\\..\\x"));
        Check("UNC 共享根：\\\\srv\\share 包含子目录", PathUtil.IsSubPath("\\\\srv\\share", "\\\\srv\\share\\dir"));
        Check("UNC 不同共享：\\\\srv\\share 不含 \\\\srv\\other", !PathUtil.IsSubPath("\\\\srv\\share", "\\\\srv\\other"));
        // ============================================================================
        // [32] 备份链审查（2026-10-07，D1/A1）：Export **真实导出**回归
        //   为什么必须有：旧 CopyTree 返回的是"跳过的嵌套包数"（几乎总是 0）而不是拷贝文件数 ✗
        //   → Export 守卫 `copiedN==0 && entries>0 → null` 对**每一个非空包**都开火 ✗✗
        //   → backup-export 自 v3.0.0 起 100% BKEXPORT_FAIL ✗（文件其实已拷 ✓ 但无旁挂 .manifest ✗）
        // ============================================================================
        Console.WriteLine("[32] Export：真实导出往返（备份链审查 D1 回归）");
        {
            string exRootTmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsht-ex-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                string exData = System.IO.Path.Combine(exRootTmp, "data");
                string exBk = System.IO.Path.Combine(exRootTmp, "bk");
                string exOut = System.IO.Path.Combine(exRootTmp, "out");
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(exData, "storages"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(exData, "storages", "a.txt"), "ALPHA");
                System.IO.File.WriteAllText(System.IO.Path.Combine(exData, "settings.yaml"), "theme: dark");
                System.IO.Directory.CreateDirectory(exBk);
                System.IO.Directory.CreateDirectory(exOut);
                string exOldEnv = Environment.GetEnvironmentVariable("DSH_MINATO_BACKUP_DIR");
                Environment.SetEnvironmentVariable("DSH_MINATO_BACKUP_DIR", exBk);
                Dsht.Domain.Abstractions.IBackupSource exbks;
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    exbks = new Dsht.Platform.Windows.WindowsBackupSource(new Dsht.Platform.Windows.WindowsPaths());
                else
                    exbks = new Dsht.Platform.Linux.LinuxBackupSource(new Dsht.Platform.Linux.LinuxPaths());
                Dsht.Domain.Model.BackupResult exRes = exbks.Create(exData, Dsht.Domain.Model.BackupKind.Manual, 3, null);
                Check("导出前置：备份包已建", exRes != null && !string.IsNullOrEmpty(exRes.Path));
                string exTarget = exbks.Export(exRes.Path, exOut);
                Check("导出：返回非 null（修复前非空包永远 null ✗✗）", exTarget != null);
                Check("导出：文件真的到目标",
                    exTarget != null
                    && System.IO.File.Exists(System.IO.Path.Combine(exTarget, "storages", "a.txt"))
                    && System.IO.File.ReadAllText(System.IO.Path.Combine(exTarget, "storages", "a.txt")) == "ALPHA"
                    && System.IO.File.Exists(System.IO.Path.Combine(exTarget, "settings.yaml")));
                Check("导出：旁挂 .manifest 存在（审查 D1 核心症状 ✓）", exTarget != null && System.IO.File.Exists(exTarget + ".manifest"));
                Environment.SetEnvironmentVariable("DSH_MINATO_BACKUP_DIR", exOldEnv);
            }
            catch (Exception ex)
            {
                Check("导出往返：未抛异常（" + ex.GetType().Name + ": " + ex.Message + "）", false);
            }
            finally
            {
                try { System.IO.Directory.Delete(exRootTmp, true); } catch { }
            }
        }
        // ============================================================================
        // [33] 备份链审查（2026-10-07，D4/B2）：恢复时目标侧 **junction 不穿透**回归
        //   为什么必须有：顶层文件循环旧代码只查**源**侧重解析点 ✗ 不查目标侧 ✗
        //   → 目标预置同名 junction 时 File.Copy 直接炸（修复前实测 ✓）/ 目录 junction 会被穿透污染 ✗✗
        //   安全：junction（目录联接）**不需要管理员** ✓ 全程在 %TEMP% 随机目录 ✓ 结束即清理 ✓
        // ============================================================================
        Console.WriteLine("[33] 恢复守卫：目标侧 junction（备份链审查 D4 回归）");
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            string jRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsht-jn-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                string jData = System.IO.Path.Combine(jRoot, "data");
                string jBk = System.IO.Path.Combine(jRoot, "bk");
                string jDst = System.IO.Path.Combine(jRoot, "dst");
                string jOutside = System.IO.Path.Combine(jRoot, "outside");   // junction 的真实目标 —— 绝不能被写入 ✗✗
                System.IO.Directory.CreateDirectory(jData);
                System.IO.File.WriteAllText(System.IO.Path.Combine(jData, "a.txt"), "TOP-LEVEL-FILE");
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(jData, "sub"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(jData, "sub", "b.txt"), "NESTED");
                System.IO.Directory.CreateDirectory(jBk);
                System.IO.Directory.CreateDirectory(jDst);
                System.IO.Directory.CreateDirectory(jOutside);
                string jOldEnv = Environment.GetEnvironmentVariable("DSH_MINATO_BACKUP_DIR");
                Environment.SetEnvironmentVariable("DSH_MINATO_BACKUP_DIR", jBk);
                Dsht.Platform.Windows.WindowsBackupSource jbks = new Dsht.Platform.Windows.WindowsBackupSource(new Dsht.Platform.Windows.WindowsPaths());
                Dsht.Domain.Model.BackupResult jRes = jbks.Create(jData, Dsht.Domain.Model.BackupKind.Manual, 3, null);
                Check("junction 前置：备份包已建", jRes != null && !string.IsNullOrEmpty(jRes.Path));
                // ★ 在恢复目标预置**同名 junction**：`dst\a.txt` → `outside\` ✓（mklink /J 无需管理员 ✓）
                System.Diagnostics.Process jmk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c mklink /J \"" + System.IO.Path.Combine(jDst, "a.txt") + "\" \"" + jOutside + "\"",
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
                });
                jmk.WaitForExit();
                Check("junction 前置：预置成功", (System.IO.File.GetAttributes(System.IO.Path.Combine(jDst, "a.txt")) & System.IO.FileAttributes.ReparsePoint) != 0);
                Dsht.Domain.Model.RestoreOutcome jOut = jbks.Restore(jRes.Path, jDst, null);
                Check("恢复：junction 守卫下整体成功（修复前 File.Copy 炸 ✗）", jOut != null && jOut.Ok);
                Check("恢复：junction 目标未被穿透（D4 核心 ✗✗）",
                    System.IO.Directory.GetFiles(jOutside).Length == 0 && System.IO.Directory.GetDirectories(jOutside).Length == 0);
                Check("恢复：其余文件正常落位",
                    System.IO.File.Exists(System.IO.Path.Combine(jDst, "sub", "b.txt"))
                    && System.IO.File.ReadAllText(System.IO.Path.Combine(jDst, "sub", "b.txt")) == "NESTED");
                Environment.SetEnvironmentVariable("DSH_MINATO_BACKUP_DIR", jOldEnv);
            }
            catch (Exception ex)
            {
                Check("junction 用例：未抛异常（" + ex.GetType().Name + ": " + ex.Message + "）", false);
            }
            finally
            {
                try
                {
                    string jlink = System.IO.Path.Combine(jRoot, "dst", "a.txt");
                    if (System.IO.Directory.Exists(jlink)) System.IO.Directory.Delete(jlink);   // 先拆 junction（不递归 ✓ 只摘链接 ✓）
                }
                catch { }
                try { System.IO.Directory.Delete(jRoot, true); } catch { }
            }
        }
        else Check("恢复守卫：非 Windows 平台跳过 junction 用例（Linux 由同构符号链接守卫覆盖 ✓）", true);
        // ============================================================================
        // [34] 备份链审查（2026-10-07，D7/B3）：自哈希缓存**预置投毒免疫**回归
        //   为什么必须有：旧缓存文件名 = `<长度>-<时间戳>.txt` —— **路径没进文件名** ✗
        //   → %TEMP% 人人可写 ✓ → 攻击者预知文件名、提前写入 64 位假哈希 ✗✗ → 完整性闸门被喂假值 ✗
        //   修复后文件名 = SHA256(路径|长度|时间戳) → 可枚举的投毒文件名从此不再被读取 ✓✓
        // ============================================================================
        Console.WriteLine("[34] 自哈希缓存：投毒免疫（备份链审查 D7 回归）");
        {
            Dsht.Domain.Abstractions.IIntegritySource integ;
            string selfExe;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                Dsht.Platform.Windows.WindowsIntegritySource wsrc = new Dsht.Platform.Windows.WindowsIntegritySource();
                integ = wsrc; selfExe = wsrc.SelfPath();
            }
            else
            {
                Dsht.Platform.Linux.LinuxIntegritySource lsrc = new Dsht.Platform.Linux.LinuxIntegritySource();
                integ = lsrc; selfExe = lsrc.SelfPath();
            }
            if (string.IsNullOrEmpty(selfExe) || !System.IO.File.Exists(selfExe))
            {
                Check("自哈希：能定位自身可执行文件", false);
            }
            else
            {
                System.IO.FileInfo sfi = new System.IO.FileInfo(selfExe);
                string shCacheDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-minato-selfhash");
                System.IO.Directory.CreateDirectory(shCacheDir);
                string poisonFile = System.IO.Path.Combine(shCacheDir, sfi.Length.ToString() + "-" + sfi.LastWriteTimeUtc.Ticks.ToString() + ".txt");
                string poisonHex = new string('a', 64);
                System.IO.File.WriteAllText(poisonFile, poisonHex);   // ★ 修复前：这个名字会被**直接命中** ✗✗
                try
                {
                    string h1 = integ.SelfHash();
                    Check("自哈希：不读预置投毒（修复前返回 64 个 a ✗✗）", h1 != null && h1 != poisonHex);
                    // 独立现算期望值（不复用实现代码 ✓）
                    string expect;
                    using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                    using (System.IO.FileStream sfs = System.IO.File.OpenRead(selfExe))
                    {
                        byte[] hh = sha.ComputeHash(sfs);
                        System.Text.StringBuilder ssb = new System.Text.StringBuilder();
                        foreach (byte bb in hh) ssb.Append(bb.ToString("x2"));
                        expect = ssb.ToString();
                    }
                    Check("自哈希：等于独立现算值", h1 == expect);
                    string hSecond = integ.SelfHash();
                    Check("自哈希：第二次调用同值（新缓存命中路径正常 ✓）", hSecond == h1);
                }
                finally
                {
                    try { System.IO.File.Delete(poisonFile); } catch { }
                }
            }
        }
        // ============================================================================
        // [35] 备份链审查（2026-10-07，D13/C1）：Export 对「0 文件 + ≥1 目录条目」的真实包
        //   真机最小复现（Linux VM，实测 BINARY_RC=1 ✓）：
        //     DSH_HOME 里只有一个空目录 → backup **成功**（包内 0 文件、1 个目录条目 ✓）
        //     → backup-export → **BKEXPORT_FAIL rc=1** ✗ 且 exp/ 下**没有 .manifest** ✗
        //   根因（读码）：Export 的守卫读 CopyTree 的返回值，而那个返回值**只数文件** ✗
        //     → `copied==0 && 源里"有条目"` 对"0 文件 + 空目录"的包**恒成立** → 误判失败 ✗
        //   次生（D1 同族：上次修得不彻底）：那个 return null 在 CopySibling(.manifest) **之前** ✗
        //     → 目标目录已被拷了一半（空目录建了出来）却没有完成标记 ✗ → --verify 只能判 incomplete ✓
        //   本用例走**真实 IBackupSource + 真实临时目录**（不用 mock ✓，与前几个用例同一惯例 ✓），
        //   并复刻 `backup-list --verify` 的判定（Program.Backup.cs:148-176：标记在 + files= 等于实际文件数 → complete）
        //   安全：全程在 %TEMP% 随机目录 ✓ 备份根用 DSH_MINATO_BACKUP_DIR 指到那里 ✓ 结束即清理 ✓
        // ============================================================================
        Console.WriteLine("[35] Export：0 文件 + ≥1 目录条目（D13 回归；真机实测抓到的误判 ✗）");
        {
            string d13Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsht-d13-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string d13OldEnv = Environment.GetEnvironmentVariable("DSH_MINATO_BACKUP_DIR");
            try
            {
                string d13Data = System.IO.Path.Combine(d13Root, "data");
                string d13Bk = System.IO.Path.Combine(d13Root, "bk");
                string d13Out = System.IO.Path.Combine(d13Root, "exp");
                // 数据根：**0 个文件** + 空目录。
                //   `empty` 就是真机复现里的那个目录 ✓；
                //   `sessions` 是 dsh 数据特征名（BackupPackage.HasDshData ✓）→ 让这个包同时是"有效备份包"
                //   → --verify 不会额外打 BACKUP_VERIFY_NOTE（与真机 --verify 输出对齐 ✓）
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(d13Data, "empty"));
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(d13Data, "sessions"));
                System.IO.Directory.CreateDirectory(d13Bk);
                System.IO.Directory.CreateDirectory(d13Out);
                Environment.SetEnvironmentVariable("DSH_MINATO_BACKUP_DIR", d13Bk);
                Dsht.Domain.Abstractions.IBackupSource d13bks;
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    d13bks = new Dsht.Platform.Windows.WindowsBackupSource(new Dsht.Platform.Windows.WindowsPaths());
                else
                    d13bks = new Dsht.Platform.Linux.LinuxBackupSource(new Dsht.Platform.Linux.LinuxPaths());

                Dsht.Domain.Model.BackupResult d13Res = d13bks.Create(d13Data, Dsht.Domain.Model.BackupKind.Manual, 3, null);
                Check("D13 前置：备份成功，且包内 0 文件 + ≥1 个目录条目（缺陷触发条件 ✓）",
                    d13Res != null && System.IO.Directory.Exists(d13Res.Path)
                    && System.IO.Directory.GetFiles(d13Res.Path, "*", System.IO.SearchOption.AllDirectories).Length == 0
                    && System.IO.Directory.GetFileSystemEntries(d13Res.Path).Length > 0);

                string d13Target = d13bks.Export(d13Res.Path, d13Out);
                Check("D13 导出：返回非 null（修复前恒 null → 真机 BKEXPORT_FAIL ✗✗）", d13Target != null);
                Check("D13 导出：空目录条目也到了目标",
                    d13Target != null && System.IO.Directory.Exists(System.IO.Path.Combine(d13Target, "empty"))
                    && System.IO.Directory.Exists(System.IO.Path.Combine(d13Target, "sessions")));
                Check("D13 导出：旁挂 .manifest 存在（修复前提前 return 丢掉 → 半成品 ✗）",
                    d13Target != null && System.IO.File.Exists(d13Target + ".manifest"));

                // ---- 复刻 backup-list --verify：把备份根切到**导出目录**，用真实 ListRaw 遍历（就是 --verify 的入口 ✓）----
                Environment.SetEnvironmentVariable("DSH_MINATO_BACKUP_DIR", d13Out);
                string d13Listed = null;
                List<BackupEntry> d13All = d13bks.ListRaw();
                for (int i = 0; i < d13All.Count; i++)
                {
                    if (d13Target != null
                        && System.IO.Path.GetFullPath(d13All[i].Path).TrimEnd('\\', '/') == System.IO.Path.GetFullPath(d13Target).TrimEnd('\\', '/'))
                        d13Listed = d13All[i].Path;
                }
                Check("D13 verify：导出目录当备份根时能列出这个包（--verify 的遍历入口 ✓）", d13Listed != null);
                int d13Want = -1;
                if (d13Listed != null)
                {
                    string[] d13Ml = System.IO.File.ReadAllLines(d13Listed + ".manifest");
                    for (int k = 0; k < d13Ml.Length; k++)
                    {
                        if (d13Ml[k].StartsWith("files=", StringComparison.Ordinal)) int.TryParse(d13Ml[k].Substring(6).Trim(), out d13Want);
                    }
                }
                int d13Have = d13Listed == null ? -1 : System.IO.Directory.GetFiles(d13Listed, "*", System.IO.SearchOption.AllDirectories).Length;
                // 这就是 --verify 判 complete 的两个条件（标记可解析 + files= 与实际相等 ✓ 见 Program.Backup.cs:171-179）
                Check("D13 verify：标记可解析且 files=0 == 实际 0 文件 → --verify 判 complete",
                    d13Listed != null && d13Want == 0 && d13Have == 0);
            }
            catch (Exception ex)
            {
                Check("D13 往返：未抛异常（" + ex.GetType().Name + ": " + ex.Message + "）", false);
            }
            finally
            {
                Environment.SetEnvironmentVariable("DSH_MINATO_BACKUP_DIR", d13OldEnv);
                try { System.IO.Directory.Delete(d13Root, true); } catch { }
            }
        }
        // ============================================================================
        // [36] D13 的次生症状（D1 同族）：导出**真失败**时不许留半成品
        //   触发：源里只有被跳过规则排除的条目（node_modules ✓ SkipRules）→ 一个条目都复制不过去 ✓
        //   断言：Export 返回 null（如实报失败 ✓）**且**目标目录与标记都没留下 ✓
        //     ✗ 修复前：CopyTree 已经 Directory.CreateDirectory(target) → **空壳目录留着却没有标记** ✗
        //       → 外部工具/用户看到"有个包目录"会以为导出了一半 ✓（--verify 只能事后判 incomplete ✓）
        // ============================================================================
        Console.WriteLine("[36] Export：真失败时不留半成品（D13 次生症状回归）");
        {
            string d13bRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsht-d13b-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                const string d13bPkgName = "dsh-data-20260101-000000000-1";
                string d13bSrc = System.IO.Path.Combine(d13bRoot, "src", d13bPkgName);
                string d13bOut = System.IO.Path.Combine(d13bRoot, "exp");
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(d13bSrc, "node_modules", "left-pad"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(d13bSrc, "node_modules", "left-pad", "index.js"), "module.exports=1;");
                System.IO.Directory.CreateDirectory(d13bOut);
                Dsht.Domain.Abstractions.IBackupSource d13bbks;
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                    d13bbks = new Dsht.Platform.Windows.WindowsBackupSource(new Dsht.Platform.Windows.WindowsPaths());
                else
                    d13bbks = new Dsht.Platform.Linux.LinuxBackupSource(new Dsht.Platform.Linux.LinuxPaths());
                string d13bTarget = d13bbks.Export(d13bSrc, d13bOut);
                Check("D13b 导出：确实一个条目都没复制过去 → 如实返回 null", d13bTarget == null);
                Check("D13b 失败后：目标包目录**没有被留下**（修复前留空壳 ✗）",
                    !System.IO.Directory.Exists(System.IO.Path.Combine(d13bOut, d13bPkgName)));
                Check("D13b 失败后：也没有留下 .manifest（不许把失败包装成完整 ✗）",
                    !System.IO.File.Exists(System.IO.Path.Combine(d13bOut, d13bPkgName) + ".manifest"));
            }
            catch (Exception ex)
            {
                Check("D13b 失败路径：未抛异常（" + ex.GetType().Name + ": " + ex.Message + "）", false);
            }
            finally
            {
                try { System.IO.Directory.Delete(d13bRoot, true); } catch { }
            }
        }
        Console.WriteLine("== " + _pass + "/" + (_pass + _fail) + " passed, " + _fail + " failed ==");
        return _fail == 0 ? 0 : 1;
    }
}
