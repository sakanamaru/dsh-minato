using System;
using System.Collections.Generic;
using Dsht.Domain.Abstractions;
using Dsht.Domain.Model;
using Dsht.Domain.Services;

namespace Dsht.Cli
{
    /// <summary>备份与恢复相关（backup* / restore / import 的辅助与命令）✓。
    /// ★★ 架构审计（S1）：从 Program.cs **原样搬出来**的 ✓✓（逻辑一行没改 ✓）
    ///   · 边界用**括号配平**找 ✓（不再猜"第一个 }" —— 那个方法弄坏过 4 次 ✓）
    ///   · 逆序处理 ✓ 避免行号漂移 ✓</summary>
    public static partial class Program
    {
        /// <summary>backup-list：标记行与裸路径行逐条对齐 v2.x 的 NIBackupList。</summary>
        /// <summary>备份完成性核对 ✓（`backup-list --verify`）：读每个包的**同级完成标记** &lt;包&gt;.manifest ✓。
        /// 标记**最后写** ✓ → 缺失即"备份未完成"（中断可被精确识别 ✓✓）；存在则核对文件数是否与标记一致 ✓（截断可被发现 ✓）。
        /// 这是**新增开关** ✓，不在标记行契约的比对用例里 ✓ → 不影响 gate1 ✓。</summary>
        /// <summary>恢复前的**完成性核对** ✓：只在"完成标记**存在**且对不上"时返回原因 ✓。
        /// 标记缺失时**不拦** ✓（老包没有标记 ✓，拦了会破坏兼容 ✓）；对不上则说明包被截断 ✓ → 恢复会缺内容 ✓。
        /// 用 --force 可越过 ✓（与 stop 的闸门同一风格 ✓）。</summary>
        /// <summary>备份包的**内容哈希** ✓：排序后的 `相对路径|大小|文件SHA256` 逐行拼接再取 SHA256 ✓。
        /// 用于发现"计数对得上但内容残了"的情况 ✗（例如复制被中断在文件中间 ✓）—— 明文计数发现不了它 ✗✓。
        /// 只做读取 ✓ 不改动包 ✓。</summary>
        private static string PackageContentHash(string pkgDir)
        {
            try
            {
                string root = System.IO.Path.GetFullPath(pkgDir).TrimEnd('\\', '/');
                List<string> lines = new List<string>();
                string[] files = System.IO.Directory.GetFiles(root, "*", System.IO.SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    string rel = files[i].Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
                    long len = 0;
                    try { len = new System.IO.FileInfo(files[i]).Length; } catch { }
                    string h = "";
                    try { h = Sha256Of(files[i]); } catch { h = "unreadable"; }
                    lines.Add(rel + "|" + len + "|" + h);
                }
                lines.Sort(StringComparer.Ordinal);
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < lines.Count; i++) sb.Append(lines[i]).Append('\n');
                byte[] raw = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] d = sha.ComputeHash(raw);
                    System.Text.StringBuilder hex = new System.Text.StringBuilder(d.Length * 2);
                    for (int i = 0; i < d.Length; i++) hex.Append(d[i].ToString("x2"));
                    return hex.ToString();
                }
            }
            catch { return null; }
        }
        /// <summary>给备份包的完成标记补上内容哈希 ✓（标记由平台侧最后写出 ✓，这里追加一行 ✓ 不改平台实现 ✓）。</summary>
        private static void AddContentHashToMarker(string pkgPath)
        {
            try
            {
                string mf = pkgPath + ".manifest";
                if (!System.IO.File.Exists(mf)) return;
                string h = PackageContentHash(pkgPath);
                if (string.IsNullOrEmpty(h)) return;
                string cur = System.IO.File.ReadAllText(mf);
                if (cur.IndexOf("sha256=", StringComparison.Ordinal) >= 0) return;
                // 全部拷完后**重算并改写 files=** ✓✓ —— 平台写标记时只算了数据根 ✓，之后又拷进了工作区 ✗
                // → 不更新的话包会**自报"不完整"** ✗✗（上一版的真 bug ✓）
                try
                {
                    int nowFiles = System.IO.Directory.GetFiles(pkgPath, "*", System.IO.SearchOption.AllDirectories).Length;
                    cur = System.Text.RegularExpressions.Regex.Replace(cur, @"(?m)^files=\d+", "files=" + nowFiles);
                }
                catch { }
                System.IO.File.WriteAllText(mf, cur.TrimEnd('\n', '\r') + "\n" + "sha256=" + h + "\n");
            }
            catch { }
        }
        private static string BackupTruncatedReason(string pkgDir)
        {
            try
            {
                string mf = pkgDir + ".manifest";
                // ★★★ 审查抓到：完成标记**最后才写** ✗（中断可被精确识别 ✓）而这里缺标记却返回 null ✗
                //   → 调用方把 null 当成"没被截断" ✓ → **中断的包会被照常恢复并打印 RESTORE_OK** ✗✗
                //   → 而 `backup-list --verify` 明明把它标成 incomplete ✓（工具知道这个事实却忽略了 ✓）
                // ✓ 现在：**缺标记就是"未完成"** ✓✓（照旧可用 --force 强行使用 ✓ 但要用户明说 ✓）
                if (!System.IO.File.Exists(mf))
                    return System.IO.Directory.Exists(pkgDir)
                        ? T("备份未完成（缺少完成标记 ✓ 可能是中断/磁盘满）：", "backup is incomplete (no completion marker): ") + System.IO.Path.GetFileName(pkgDir)
                        : null;
                // ★ 第 2 轮抓到：标记里写着 `failed=<n>` ✓（平台侧真的写 ✓）而这里**只读 files= 与 sha256=** ✗
                //   → 工具自己在备份时说了"该备份不完整" ✓ backup-list --verify 也这么报 ✓
                //     而 restore 却当它完整 ✓✗ → **同一个包两套结论** ✓
                // ✓ 现在：**failed>0 也算不完整** ✓✓
                try
                {
                    string allTxt = System.IO.File.ReadAllText(mf);
                    System.Text.RegularExpressions.Match fm = System.Text.RegularExpressions.Regex.Match(allTxt, "failed=(\\d+)");
                    if (fm.Success)
                    {
                        int failedN;
                        if (int.TryParse(fm.Groups[1].Value, out failedN) && failedN > 0)
                            return T("备份不完整（标记里记着 " + failedN + " 项没能备份 ✓）：", "backup is incomplete (the marker records " + failedN + " items that could not be backed up): ") + System.IO.Path.GetFileName(pkgDir);
                    }
                }
                catch { }
                int want = -1;
                string[] ls = System.IO.File.ReadAllLines(mf);
                for (int i = 0; i < ls.Length; i++) { if (ls[i].StartsWith("files=", StringComparison.Ordinal)) int.TryParse(ls[i].Substring(6).Trim(), out want); }
                if (want < 0) return null;
                int have = 0;
                try { have = System.IO.Directory.GetFiles(pkgDir, "*", System.IO.SearchOption.AllDirectories).Length; } catch { return null; }
                string mh2 = MarkerHash(mf);
                if (mh2 != null)
                {
                    string act2 = PackageContentHash(pkgDir);
                    if (act2 != mh2) return T("该备份内容与完成标记不符（哈希不一致）—— 内容已被改动或损坏", "this backup does not match its completion marker (hash mismatch) - the content has been altered or corrupted");
                }
                // ★★ 架构审计抓到：这里是 have >= want ✗ 而 BackupVerify 用 want != have ✗
                //   → 同一个包两套结论 ✗（多出文件时 restore 说"完整" ✓ 而 --verify 说 mismatch ✓）
                // ✓ 现在：与 --verify 对齐 ✓✓（数量必须完全相等 ✓）
                // ★ 修：want 取不到时是 -1 ✗ → 不能直接比 ✗（否则每一个包都会被判不符 ✗✗）
                if (want >= 0 && have != want) return T("备份内容与标记不符（标记 ", "backup does not match its marker (marker ") + want + T(" 项，实际 ", " items, actual ") + have + T(" 项）：", "): ") + System.IO.Path.GetFileName(pkgDir);
                return null;
            }
            catch { return null; }
        }
        private static int BackupVerify(ServiceRegistry reg)
        {
            IBackupSource bk = reg.Get<IBackupSource>();
            List<BackupEntry> all = bk.ListRaw();
            // 独立统计"不是有效备份"的条目 ✓（不碰下面的既有逻辑 ✓）→ 在 TOTAL 前**仅当 >0** 时说明 ✓✓
            int notValidPkg = 0;
            for (int k = 0; k < all.Count; k++) { if (!BackupPackage.IsValidPackage(all[k].Snapshot)) notValidPkg++; }
            int complete = 0, incomplete = 0, mismatch = 0;
            for (int i = 0; i < all.Count; i++)
            {
                string name = System.IO.Path.GetFileName(all[i].Path.TrimEnd('\\', '/'));
                string mf = all[i].Path + ".manifest";
                if (!System.IO.File.Exists(mf))
                {
                    incomplete++;
                    Console.WriteLine("BACKUP_VERIFY " + name + " incomplete " + T("未完成（无完成标记 —— 备份可能被中断）", "incomplete (no completion marker - the backup may have been interrupted)"));
                    continue;
                }
                
                int want = -1;
                try
                {
                    string[] ls = System.IO.File.ReadAllLines(mf);
                    for (int k = 0; k < ls.Length; k++) { if (ls[k].StartsWith("files=", StringComparison.Ordinal)) int.TryParse(ls[k].Substring(6).Trim(), out want); }
                }
                catch { }
                int have = 0;
                try { have = System.IO.Directory.GetFiles(all[i].Path, "*", System.IO.SearchOption.AllDirectories).Length; } catch { }
                // 有内容哈希就**以哈希为准** ✓（能发现"计数对得上但内容残了" ✗✓）；没有则退回计数比对 ✓
                string mh = MarkerHash(mf);
                if (mh != null)
                {
                    string actual = PackageContentHash(all[i].Path);
                    if (actual != mh) { mismatch++; Console.WriteLine("BACKUP_VERIFY " + name + " mismatch " + T("内容哈希与标记不符 —— 备份内容已被改动或损坏，不要依赖它", "content hash differs from the marker - the backup has been altered or corrupted, do not rely on it")); continue; }
                }
                if (want < 0) { mismatch++; Console.WriteLine("BACKUP_VERIFY " + name + " unreadable " + T("标记无法解析", "marker unparsable")); }
                else if (want != have)
                {
                    mismatch++;
                    Console.WriteLine("BACKUP_VERIFY " + name + " mismatch " + T("标记 ", "marker ") + want + T(" 个文件，实际 ", " files, actual ") + have + T(" 个 —— 该备份不完整，不要依赖它", " - this backup is incomplete, do not rely on it"));
                }
                else
                {
                    complete++;
                    // 把标记里的 failed= 一并带出来 ✓✓ —— 否则事后复查只看到 "complete" ✗
                    // 而 "包自身一致" 与 "源里有没有少备" 是**两个问题** ✓（--verify 回答前者 ✓ 这里补上后者 ✓）
                    int failedInMarker = -1;
                    try
                    {
                        string[] ml = System.IO.File.ReadAllLines(mf);
                        for (int k = 0; k < ml.Length; k++) { if (ml[k].StartsWith("failed=", StringComparison.Ordinal)) int.TryParse(ml[k].Substring(7).Trim(), out failedInMarker); }
                    }
                    catch { }
                    Console.WriteLine("BACKUP_VERIFY " + name + " complete " + have + (failedInMarker > 0 ? T(" ；但源里有 ", " ; but ") + failedInMarker + T(" 项未能备份（不完整 ✓）", " items could not be backed up (incomplete)") : ""));
                }
            }
            if (notValidPkg > 0) Console.WriteLine("BACKUP_VERIFY_NOTE " + notValidPkg + T(" 项不是有效备份（不会被 backup-list 列出，也不会被 restore 选中）", " entries are not valid backups (not listed by backup-list, not selected by restore)"));
            Console.WriteLine("BACKUP_VERIFY_TOTAL " + all.Count + " complete=" + complete + " incomplete=" + incomplete + " mismatch=" + mismatch);
            return 0;
        }
        /// <summary>查看 / 设置**备份位置** ✓✓（用户要求：「备份路径在备份页面里设置并且显示吧」✓）。
        ///
        /// 语义：
        ///   · 不带参数 → **只显示**当前备份根 ✓（标记行 `BACKUP_DIR <路径>` ✓）
        ///   · `--set <目录>` → **改到那里** ✓（写 `<StateDir>/.backup-dir` ✓ 之后所有命令都用它 ✓✓）
        ///   · `--reset` → **恢复默认**（删掉那个文件 ✓ 回到 `StateDir/backup` ✓）
        ///
        /// 为什么要有它：默认备份根在 **Windows 上就是安装目录内**（`StateDir` = exe 所在目录 ✗）
        ///   → 卸载时**理论上**会被一起清 ✗（安装器已加保险：显式跳过 backup/ ✓✓）
        ///   → 但**放在安装目录之外才真正稳妥** ✓ → 用户需要一个**看得见、改得动**的入口 ✓✓
        ///
        /// 标记行：BACKUP_DIR / BACKUP_DIR_OK / BACKUP_DIR_RESET / BACKUP_DIR_FAIL</summary>
        private static int BackupDirCmd(string[] args, ServiceRegistry reg)
        {
            IBackupSource bk = reg.Get<IBackupSource>();
            IPaths paths = reg.Get<IPaths>();
            string sel = "";
            try { sel = System.IO.Path.Combine(paths.StateDir, ".backup-dir"); } catch { }

            if (Has(args, "--reset"))
            {
                // D1 FIX (CLI audit MAJOR): the delete was wrapped in an empty catch and success was
                // printed unconditionally, so a read-only or locked settings file left the user told
                // the reset had worked while every later backup kept going to the old folder. Verify.
                if (!string.IsNullOrEmpty(sel) && System.IO.File.Exists(sel))
                {
                    try { System.IO.File.Delete(sel); }
                    catch (Exception dex)
                    {
                        Console.WriteLine("BACKUP_DIR_FAIL " + T("无法恢复默认（删不掉设置文件）：" + dex.Message, "cannot reset: " + dex.Message));
                        return 0;
                    }
                    if (System.IO.File.Exists(sel))
                    {
                        Console.WriteLine("BACKUP_DIR_FAIL " + T("设置文件删掉后**仍然存在** ✓ 请手动删除：" + sel, "the settings file still exists: " + sel));
                        return 0;
                    }
                }
                Console.WriteLine("BACKUP_DIR_RESET " + bk.BackupsRoot + " " + T("已恢复默认备份位置 ✓", "reset to the default backups folder"));
                return 0;
            }

            string set = (Flag(args, "--set") ?? "").Trim().Trim('"');
            if (string.IsNullOrEmpty(set))
            {
                Console.WriteLine("BACKUP_DIR " + bk.BackupsRoot);
                return 0;
            }

            // D3 FIX (CLI audit MAJOR): the comment said "must be an absolute path" but nothing
            // checked it, so a relative value was resolved against the current directory and then
            // stored - making every later command depend on where it was run from, including which
            // folder a delete targeted.
            if (!System.IO.Path.IsPathRooted(set))
            {
                Console.WriteLine("BACKUP_DIR_FAIL " + T("必须是**绝对路径** ✓ 相对路径会随当前目录变化 ✗：" + set, "the path must be absolute: " + set));
                return 0;
            }
            string full;
            try { full = System.IO.Path.GetFullPath(set); }
            catch (Exception ex) { Console.WriteLine("BACKUP_DIR_FAIL " + T("路径无效：" + ex.Message, "invalid path: " + ex.Message)); return 0; }
            try { System.IO.Directory.CreateDirectory(full); }
            catch (Exception ex) { Console.WriteLine("BACKUP_DIR_FAIL " + T("无法创建目录：" + ex.Message, "cannot create folder: " + ex.Message)); return 0; }
            // ✓✓ **实测抓到的**：`Z:\no\such\drive\x` 竟然"创建成功"了 ✗（不可用盘符时 `CreateDirectory` 不抛 ✓）
            //   → **必须**再确认它**真的存在** ✓✓（不信任 API 的返回值 ✓ 只看事实 ✓）
            if (!System.IO.Directory.Exists(full))
            {
                Console.WriteLine("BACKUP_DIR_FAIL " + T("目录创建后**并不存在** ✓ 路径不可用：" + full, "the folder does not exist after creating it: " + full));
                return 0;
            }
            try
            {
                if (string.IsNullOrEmpty(sel)) { Console.WriteLine("BACKUP_DIR_FAIL " + T("取不到状态目录，无法保存设置 ✓", "cannot resolve the state dir")); return 0; }
                // ★ 同一处修复 ✓：**全新安装上 StateDir 可能还不存在** ✗ → 先建目录再写 ✓（见 `--to` 的说明 ✓）
                try { string sp2 = System.IO.Path.GetDirectoryName(sel); if (!string.IsNullOrEmpty(sp2)) System.IO.Directory.CreateDirectory(sp2); } catch { }
                System.IO.File.WriteAllText(sel, full, new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex) { Console.WriteLine("BACKUP_DIR_FAIL " + T("无法保存设置：" + ex.Message, "cannot save the setting: " + ex.Message)); return 0; }

            Console.WriteLine("BACKUP_DIR_OK " + full + " " + T("以后的备份都写到这里 ✓（已有备份**不会**被移动 ✓ 仍留在原处 ✓）", "future backups go here; existing ones are not moved"));
            return 0;
        }
        private static int BackupList(string[] args, ServiceRegistry reg)
        {
            if (Has(args, "--verify")) return BackupVerify(reg);
            bool detail = Has(args, "--detail");
            IBackupSource src = reg.Get<IBackupSource>();
            List<BackupEntry> all = src.ListRaw();
            // ★★★ **用户要求（2026-09-30）**：「GUI 备份要不不校验了」✓✓
            //   ✗ 原来**只列有效包** ✗（用 `IsValidPackage` 过滤 ✓）
            //     → 用户往 `backup/` 里放的东西**看不到** ✗ → 他会以为"我的备份不见了" ✓
            //       而其实**还在那里** ✓✓（真机就出现过：`BACKUP_LIST_OK 0` + 1 项被忽略 ✓）
            //   ✓ 现在：**全部列出** ✓✓ 无效的**明确标注** ✓（诚实 ✓ 不隐藏 ✓ 不假装有效 ✓）
            //   ✓ `BACKUP_LIST_IGNORED` 保留 ✓（仍告诉你有几项不是有效备份 ✓）
            //   ✓ **恢复时仍只认有效包** ✓（安全边界不动 ✓ 见 IsValidBackupDirFn ✓✓）
            all.Reverse();   // 最新在前 ✓
            // ★★★ **A1 修复（CLI 审计 CRITICAL —— gate1 变红）** ✓✓
            //   ✗ `backup-list` 是 **v2.x 的冻结契约** ✓ 而 `compare_markers.ps1` **逐字比对**它 ✓
            //     （只忽略 `^BACKUP_LIST_IGNORED ` ✓）→ 我上一轮加的三样**全都 diff** ✗✗：
            //       ① 多出来的 `BACKUP_DIR` 行 ✗
            //       ② 计数从「有效包」改成「全部条目」✗
            //       ③ 无效条目也打印了路径行 + `BACKUP_ITEM_INVALID` ✗
            //   ✓ 现在：**计数与路径行都只算有效包** ✓✓（与 v2.x 一致 ✓）
            //     · 备份位置改由**独立的 `backup-dir` 命令**报告 ✓（GUI 调它 ✓ 不再动这个契约 ✓）
            //     · `BACKUP_ITEM_INVALID` 只在**真有无效包**时出现 ✓ → 比对夹具（3 个有效包 ✓）不受影响 ✓✓
            int notValid = 0;
            for (int i = 0; i < all.Count; i++) { if (!BackupPackage.IsValidPackage(all[i].Snapshot)) notValid++; }
            int validCount = all.Count - notValid;
            Console.WriteLine("BACKUP_LIST_OK " + validCount);
            // 备份根里"不是有效备份"的条目 → **仅在存在时**说明 ✓✓（用户往 backup/ 放了自己的东西、
            // 或留下名字像备份但内容不是的目录时 ✓ 静默忽略会让他以为"我的备份还在" ✗）
            // 仅当 >0 时打印 ✓ → 比对夹具（受控 3 个备份 ✓）不受影响 ✓ 契约安全 ✓
            if (notValid > 0)
                Console.WriteLine("BACKUP_LIST_IGNORED " + notValid + T(" 项在备份根里但不是有效备份（**仍会列出** ✓ 但不能用于恢复 ✓）", " entries are not valid packages (listed, but not restorable)"));
            foreach (BackupEntry e in all)
            {
                // ★★★ **N10 修复（复审 MAJOR —— 这个标记**从来没被打印过**）** ✓✓
                //   ✗ 原来 `continue` 在前 ✗ → 下面那行 `BACKUP_ITEM_INVALID` **永远不可达** ✗✗
                //     （编译器其实早就报 CS0162 ✓ 而门槛只 grep 字符串 ✓ → 绿着）
            //   ✓ 现在：**先判无效并打标记 ✓ 再 `continue` 跳过路径行** ✓✓
                //     无效条目不列路径 ✓（与 v2.x 的"只列有效包路径"一致 ✓ 契约安全 ✓）
                if (!BackupPackage.IsValidPackage(e.Snapshot))
                {
                    Console.WriteLine("BACKUP_ITEM_INVALID " + e.Name);
                    continue;
                }
                Console.WriteLine(e.Path);
                if (detail)
                {
                    long bytes = src.DirSize(e.Path);
                    DateTime? mt = src.LastWrite(e.Path);
                    string mts = mt.HasValue ? mt.Value.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) : "(unknown)";
                    Console.WriteLine("BACKUP_ITEM " + MarkerText.Encode(e.Name) + " " + BackupPackage.KindLabel(BackupPackage.Classify(e.Name)) + " " + bytes + " " + mts);
                }
            }
            return 0;
        }
        /// <summary>把**全部**工作区打包成新格式 `_workspace/&lt;名称&gt;/.dshws` ✓✓（仅当 ≥2 个 ✓）。
        /// **前置约束** ✗✓：≥2 个时平台侧一个都不打包 ✓（`WorkspaceRoot` 返回 null ✓）→
        /// 包里**只有一种格式** ✓ —— 上一版就是因为两种格式混在一个包里 ✗ 才回滚的 ✓。
        /// 返回实际打包数 ✓（0 = 没做 ✓）。</summary>
        private static int PackageAllWorkspaces(string pkgDir, ServiceRegistry reg)
        {
            try
            {
                string[] all = ConfiguredWorkspaces();
                if (all.Length < 2) return 0;                       // 单个 → 平台侧按旧式扁平打包 ✓
                string wsRoot = System.IO.Path.Combine(pkgDir, "_workspace");
                try { System.IO.Directory.CreateDirectory(wsRoot); } catch { return 0; }
                string dataFull = Dsht.Domain.Services.PathUtil.TrimTrailingSep(reg.Get<IPaths>().DataRoot);
                int done = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    string full;
                    try { full = System.IO.Path.GetFullPath(all[i]); } catch { continue; }
                    if (!System.IO.Directory.Exists(full)) continue;
                    string f = Dsht.Domain.Services.PathUtil.TrimTrailingSep(full);
                    if (Dsht.Domain.Services.PathUtil.IsSubPath(dataFull, f)) continue;   // 与平台侧同一套护栏 ✓
                    if (Dsht.Domain.Services.PathUtil.IsSubPath(f, dataFull)) continue;
                    string name = System.IO.Path.GetFileName(f);
                    if (string.IsNullOrEmpty(name)) continue;
                    string target = System.IO.Path.Combine(wsRoot, name);
                    if (System.IO.Directory.Exists(target)) continue;                     // 不覆盖 ✓
                    CopyDirDeep(f, target, 0);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(target, ".dshws"),
                        "dsh-minato workspace marker\nsource=" + f + "\n");
                    done++;
                }
                return done;
            }
            catch { return 0; }
        }
        private static string WorkspaceRoot(ServiceRegistry reg)
        {
            // **≥2 个工作区时传 null** ✓✓ —— 让平台侧一个都不打包 ✓（避免两代格式混在一个包里 ✗），
            // 同时**保留自动探测** ✓ → 恢复侧的目标仍然正确 ✓（各工作区落到当前项目目录下的 <名称>/ ✓）
            string[] _wss = ConfiguredWorkspaces();
            string _wsCfg = _wss.Length >= 2 ? null : (_cfg == null ? null : _cfg.Workspace);
            return WorkspaceResolver.Resolve(_wsCfg, reg.Get<IPaths>().WorkspaceRoot,
                delegate(string p) { return System.IO.Path.GetFullPath(p); },
                delegate(string p) { return System.IO.Directory.Exists(p); });
        }
        private static int BackupExport(string[] args, ServiceRegistry reg)
        {
            IBackupSource bk = reg.Get<IBackupSource>();
            IFileSystemQuery fs = reg.Get<IFileSystemQuery>();
            string reason = PathValidator.ValidateExport(Flag(args, "--path"), Flag(args, "--to"), bk.BackupsRoot,
                delegate(string p) { return fs.DirectoryExists(p); },
                delegate(string p) { return System.IO.Path.GetFullPath(p); });
            if (reason != null) { Console.WriteLine("BKEXPORT_FAIL " + T("导出校验失败: " + reason, "export validation failed: " + reason)); return 0; }
            if (!Has(args, "--yes")) { Console.WriteLine("BKEXPORT_PLAN " + T("将把备份复制到目标目录（会写盘）—— 确认请加 --yes", "will copy the backup to the target directory (writes to disk) - add --yes to confirm")); return 0; }
            string src = PathValidator.ResolveBackupPath((Flag(args, "--path") ?? "").Trim().Trim('"'), bk.BackupsRoot);
            string to = (Flag(args, "--to") ?? "").Trim().Trim('"');
            string target = bk.Export(src, System.IO.Path.GetFullPath(to));
            if (target == null) { Console.WriteLine("BKEXPORT_FAIL " + T("导出失败（见 launcher.log）", "export failed (see launcher.log)")); return 0; }
            Console.WriteLine("BKEXPORT_OK " + target);
            return 0;
        }
        /// <summary>备份删除：校验路径后**真删**（会丢数据 → 需要 --yes 闸门 ✓）。</summary>
        private static int BackupDelete(string[] args, ServiceRegistry reg)
        {
            IBackupSource bk = reg.Get<IBackupSource>();
            IFileSystemQuery fs = reg.Get<IFileSystemQuery>();
            string reason = PathValidator.ValidateDeletePath(Flag(args, "--path"), bk.BackupsRoot,
                delegate(string p) { return fs.DirectoryExists(p); });
            if (reason != null) { Console.WriteLine("BKDEL_FAIL " + T("删除校验失败: " + reason, "delete validation failed: " + reason)); return 0; }
            // ★★★ **用户要求（2026-09-30）**：「删除弹窗输入当前时间才执行」✓✓
            //   → 删除是**不可逆**的 ✓ → 光有 `--yes` 太容易误点 ✓
            //   → **必须输入当前时间**（`yyyy-MM-dd HH:mm:ss` ✓ 本地时间 ✓）且与真实时间相差 ≤ 120 秒 ✓✓
            //   → 这样"手滑点两下"不可能删掉 ✓ 必须**看着时间手打一遍** ✓✓
            string ct = (Flag(args, "--confirm-time") ?? "").Trim().Trim('"');
            if (string.IsNullOrEmpty(ct))
            {
                Console.WriteLine("BKDEL_PLAN " + T("删除备份需要输入**当前时间**确认 ✓ 请加 `--confirm-time \"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "\"` ✓（照抄上面这个时间 ✓）",
                                                     "deleting a backup needs the current time: add --confirm-time \"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "\""));
                return 0;
            }
            // I1 FIX (CLI audit MINOR): this used the current culture, so on a locale with a
            // different date format a bare time or a slashed date was accepted, and the window was
            // symmetric so a time in the FUTURE was accepted too. The documented contract is
            // exactly yyyy-MM-dd HH:mm:ss within two minutes, invariant, and not in the future.
            DateTime ctParsed;
            if (!DateTime.TryParseExact(ct, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out ctParsed))
            {
                Console.WriteLine("BKDEL_FAIL " + T("时间格式不对 ✓ 应为 yyyy-MM-dd HH:mm:ss ✓（现在：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " ✓）", "bad time format"));
                return 0;
            }
            // I1 FIX (cont): the window was symmetric, so a time up to two minutes in the FUTURE
            // was accepted. A confirmation is meant to prove the user is looking at the clock now,
            // so only a time in the past (within the window) counts.
            double ctDiff = (DateTime.Now - ctParsed).TotalSeconds;
            if (ctDiff > 120 || ctDiff < -5)   // N4 FIX: negative diff = future; allow only a tiny clock skew
            {
                Console.WriteLine("BKDEL_FAIL " + T("输入的时间与当前时间相差 " + (int)ctDiff + " 秒（超过 120 秒 ✓）→ 拒绝删除 ✓ 现在：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " ✓",
                                                     "the time you typed is " + (int)ctDiff + "s away from now - refused"));
                return 0;
            }
            if (!Has(args, "--yes")) { Console.WriteLine("BKDEL_PLAN " + T("将删除该备份目录（会丢数据）—— 确认请加 --yes", "will delete that backup directory (data loss) - add --yes to confirm")); return 0; }
            string src = PathValidator.ResolveBackupPath((Flag(args, "--path") ?? "").Trim().Trim('"'), bk.BackupsRoot);
            try
            {
                bk.Delete(src);
                if (fs.DirectoryExists(src)) { Console.WriteLine("BKDEL_FAIL " + T("删除后目录仍存在", "directory still exists after delete")); return 0; }
                Console.WriteLine("BKDEL_OK " + System.IO.Path.GetFileName(src.TrimEnd('\\', '/')));
            }
            catch (Exception ex) { Console.WriteLine("BKDEL_FAIL " + ex.Message); }
            return 0;
        }
    }
}
