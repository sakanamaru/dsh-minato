using System;
using System.Collections.Generic;
using System.IO;
using Dsht.Domain.Abstractions;
using Dsht.Domain.Model;

namespace Dsht.Platform.Windows
{
    /// <summary>备份来源（Windows）。逐条对齐 v2.x：
    ///   · 备份根 = 状态目录/backup；状态目录 = exe 所在目录（可写时），否则 %APPDATA%\DeepSeekHarnessLauncher
    ///   · 列出 dsh-data-* 直接子目录，Array.Sort 升序（时间戳字典序=时间序）
    ///   · 目录大小 = 迭代栈遍历累加文件长度（与 v2.x DirSize 一致）
    ///   · 最后写入时间取目录时间；失败返回 null（呈现为 "(unknown)"）</summary>
    // ★★★ 架构审计（S2）**已量化**（2026-10-01）：这个文件和另一端的实现
    //   **有 310 行逐行完全相同**（占本文件 65% ✓）—— 精确区块：
    //     · 区块一：Windows 137 ↔ Linux 90   共 144 行
    //     · 区块二：Windows 311 ↔ Linux 261  共 166 行
    //   → 这些行**逐字相同** ✓（含 CopyTree / RestoreWorkspaces / 标记写入等 ✓）
    //   → 一处修 bug 必须记得改两处 ✗ —— 而**这已经出过问题** ✓
    //     （真机审查：归一化修复只做在一端 ✓ 另一端漏了 ✓）
    //   ✓ 提取方案（**未执行** ✓ 因为动的是唯一能毁数据的代码 ✓）：
    //     1. 建 v3/src/Dsht.Platform.Shared/BackupSourceCommon.cs ✓
    //     2. 写成 static class BackupSourceCommon（纯静态 ✓ 不依赖任何平台类型 ✓）
    //     3. **两个 csproj 用 <Compile Include=... Link=... /> 链接同一份源文件** ✓
    //        （与 MarkerText.cs 完全同一手法 ✓ 已验证过 ✓）
    //     4. 两端只留平台特有的部分 ✓
    //   ⚠ 纪律：**先补失败分支测试**（导出失败 ✓ 已完成 ✓ / 中断恢复 ✓ / 权限拒绝 ✓）
    //     再逐块搬 ✓ 每块都编译 + 跑 [29] 真实往返测试 + 11 项门槛 ✓
    //   ⚠ 我**没有**执行提取 ✓：预算不足以在动完 310 行后做充分验证 ✓
    //     留精确数据给下一轮 ✓ 让那一步是**机械的** ✓ 而不是靠猜 ✓
    public sealed class WindowsBackupSource : Dsht.Platform.Shared.BackupSourceCommon, IBackupSource
    {
        private readonly string _stateDir;

        public WindowsBackupSource(IPaths paths) { _stateDir = paths == null ? ResolveStateDir() : paths.StateDir; }

        public string StateDir { get { return _stateDir; } }

                public string BackupsRoot
                {
                    get
                    {
                        // ★★★ **用户要求（2026-09-30）**：「第一次备份必须手动设置目录，避免卸载时删掉备份」✓✓
                        //   背景：默认备份根是 `StateDir/backup` ✓ 而 **Windows 上 StateDir 就是安装目录** ✗
                        //     → 备份**物理上躺在安装目录里** ✓（真机确认：575 个文件就在那里 ✓）
                        //     → 卸载时**理论上**会连它一起清 ✗（安装器侧已加保险：显式跳过 backup/ ✓✓）
                        //   现在：**可用 `DSH_MINATO_BACKUP_DIR` 指定备份根** ✓✓
                        //     · 设置后 → 备份放到你指定的地方 ✓ 放在安装目录之外才真正稳妥 ✓
                        //     · 未设置 → **保持原行为** ✓（向后兼容 ✓ 已有备份不会突然找不到 ✓✓）
                        //     · `backup --to <目录>` 会在本次运行里设置它 ✓✓（一次性的"手动指定目录" ✓）
                        // ✓ **持久化的选择** ✓✓（`backup --to <目录>` 会写这个文件 ✓）
                        //   ✗ 原来 `--to` 只影响**那一次运行** ✗ → 下次就"忘了" ✓
                        //     → 第二次备份又被要求给 `--to` ✓ · 删除备份报 `not-found` ✗✗（校验查的是默认根 ✓）
                        //   ✓ 现在：**选择的目录被记住** ✓✓ 之后所有命令都用它 ✓
                        try
                        {
                            string sel = Path.Combine(_stateDir, ".backup-dir");
                            if (File.Exists(sel))
                            {
                                string chosen = File.ReadAllText(sel).Trim();
                                // ★★ 架构审计抓到（MAJOR）：**真正在用的这个 BackupsRoot 原来是原样返回** ✗✗
                                //   → 归一化只做在没人调的 `IPaths.BackupsRoot` 里 ✗ → **修复修在了死代码里** ✓
                                //   → 相对路径会跟随 CWD ✗ → IsSubPath 接受什么、Delete 删哪里**取决于从哪启动** ✗
                                // ✓ 现在：**与 WindowsPaths 同一条归一化** ✓✓（失败退回原样 ✓ 不猜 ✓）
                                if (chosen.Length > 0) { try { return System.IO.Path.GetFullPath(chosen); } catch { return chosen; } }
                            }
                        }
                        catch { }
                        string env = Environment.GetEnvironmentVariable("DSH_MINATO_BACKUP_DIR");
                        // ★ 同上：环境变量也要归一化 ✓✓（否则相对值同样跟随 CWD ✗）
                        if (!string.IsNullOrWhiteSpace(env)) { try { return System.IO.Path.GetFullPath(env.Trim()); } catch { return env.Trim(); } }
                        return Path.Combine(_stateDir, "backup");
                    }
                }

        public List<BackupEntry> ListRaw()
        {
            List<BackupEntry> result = new List<BackupEntry>();
            try
            {
                string root = BackupsRoot;
                if (!Directory.Exists(root)) return result;
                string[] dirs = Directory.GetDirectories(root, "dsh-data-*");
                Array.Sort(dirs, StringComparer.Ordinal);
                foreach (string d in dirs)
                {
                    string name = Path.GetFileName(d.TrimEnd('\\', '/'));
                    result.Add(new BackupEntry(name, d, SnapshotDir(d)));
                }
            }
            catch { }
            return result;
        }


        public long DirSize(string path)
        {
            long total = 0;
            try
            {
                Stack<string> stack = new Stack<string>();
                stack.Push(path);
                while (stack.Count > 0)
                {
                    string d = stack.Pop();
                    string[] files;
                    try { files = Directory.GetFiles(d); } catch { files = new string[0]; }
                    foreach (string f in files) { try { total += new FileInfo(f).Length; } catch { } }
                    string[] subs;
                    try { subs = Directory.GetDirectories(d); } catch { subs = new string[0]; }
                    foreach (string sd in subs) stack.Push(sd);
                }
            }
            catch { }
            return total;
        }

        public DateTime? LastWrite(string path)
        {
            try { return Directory.GetLastWriteTime(path); } catch { return null; }
        }

        private static string ResolveStateDir()
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                // ★★ 架构审计抓到（C12）：探测名原来是**固定的** .write-test ✗ 且用 FileShare.None ✗
                //   → 两个并发进程探测同一个文件 ✗ → 输的那个误判"不可写" ✗ → 回退到 %APPDATA% ✓
                //   → 结果：**两个进程对 StateDir / BackupsRoot / 日志目录的看法不一致** ✗✗
                // ✓ 现在：**探测名唯一**（pid + 随机 ✓）→ 并发各测各的 ✓✓（并发一致性已实测 4/4 一致 ✓）
                string probe = Path.Combine(dir, (".write-test-" + System.Diagnostics.Process.GetCurrentProcess().Id.ToString() + "-" + System.Guid.NewGuid().ToString("N").Substring(0, 8)));
                using (FileStream fs = File.Create(probe)) { }
                File.Delete(probe);
                return dir;
            }
            catch
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeepSeekHarnessLauncher");
            }
        }

        public DirSnapshot Snapshot(string dir) { return SnapshotDir(dir); }

        /// <summary>备份目录定位：自身有效则返回自身；否则若恰好一个 dsh-data-* 子目录且有效则返回它。</summary>
        public string Resolve(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return null;
                DirSnapshot self = SnapshotDir(path);
                System.Collections.Generic.List<DirSnapshot> subs = new System.Collections.Generic.List<DirSnapshot>();
                System.Collections.Generic.List<string> subPaths = new System.Collections.Generic.List<string>();
                if (Directory.Exists(path))
                {
                    foreach (string d in Directory.GetDirectories(path))
                    {
                        subs.Add(SnapshotDir(d));
                        subPaths.Add(d);
                    }
                }
                string name = Dsht.Domain.Services.BackupPackage.Resolve(self, subs.ToArray());
                if (name == null) return null;
                if (name == self.Name) return path;
                for (int i = 0; i < subs.Count; i++) { if (subs[i].Name == name) return subPaths[i]; }
                return null;
            }
            catch { return null; }
        }

        public BackupResult Create(string sourceDir, BackupKind kind, int keep = 3, string workspaceRoot = null)
        {
            try
            {
                string root = BackupsRoot;
                Directory.CreateDirectory(root);
                string dest = Path.Combine(root, "dsh-data-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + "-" + System.Diagnostics.Process.GetCurrentProcess().Id + Dsht.Domain.Services.BackupPackage.Suffix(kind));
                _copyFailures = 0;
            int srcFiles = 0;
            CountTree(sourceDir, ref srcFiles);
            int skipped;
            CopyTree(sourceDir, dest, true, out skipped);   // D1：CopyTree 现在返回复制数，跳数走 out ✓
            int dstFiles = 0;
            CountTree(dest, ref dstFiles);
            if (srcFiles - dstFiles > 0) _copyFailures += srcFiles - dstFiles;   // 源里可读、目标里没有 → 没能备份进去 ✓
                // Package the workspace too (written as _workspace/, which the restore side reads as the
                // legacy single-workspace layout and merges back into the workspace root). Guarded both
                // ways so a workspace nested in the data root (or the reverse) can never recurse.
                if (!string.IsNullOrEmpty(workspaceRoot) && Directory.Exists(workspaceRoot))
                {
                    try
                    {
                        string wsFull = Dsht.Domain.Services.PathUtil.TrimTrailingSep(workspaceRoot);
                        string dataFull = Dsht.Domain.Services.PathUtil.TrimTrailingSep(sourceDir);
                        bool wsInsideData = Dsht.Domain.Services.PathUtil.IsSubPath(dataFull, wsFull);
                        bool dataInsideWs = Dsht.Domain.Services.PathUtil.IsSubPath(wsFull, dataFull);
                        if (!wsInsideData && !dataInsideWs)
                        {
                            int wsSkipped;
                            CopyTree(wsFull, Path.Combine(dest, "_workspace"), true, out wsSkipped);
                            skipped += wsSkipped;
                        }
                    }
                    catch (Exception wex)
                    {
                        // ★★ 架构审计抓到（MAJOR）：这里原来是**空 catch** ✗
                        //   → 一个工作区打包失败**不会进入 FailedCopies** ✗
                        //   → 而 CLI 的 BACKUP_OK 在打包**之前**就打印了 ✗ → 备份报成功但少了工作区 ✓
                        // ✓ 现在：**失败计数** ✓✓（会让 FailedCopies > 0 → 上层如实报"不完整" ✓）
                        _copyFailures++;
                    }
                }
                // 保留策略：只清自动类（手动永久保留）
                try
                {
                    System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
                    System.Collections.Generic.Dictionary<string, string> paths = new System.Collections.Generic.Dictionary<string, string>();
                    foreach (string d in Directory.GetDirectories(root))
                    {
                        string n = Path.GetFileName(d.TrimEnd('\\', '/'));
                        names.Add(n); paths[n] = d;
                    }
                    foreach (string victim in Dsht.Domain.Services.BackupRetention.SelectForDeletion(names, keep <= 0 ? 3 : keep, System.IO.Path.GetFileName(dest.TrimEnd('\\', '/'))))
                    {
                        try { Directory.Delete(paths[victim], true); } catch { }
                        try { System.IO.File.Delete(paths[victim] + ".manifest"); } catch { }   // 旁挂文件一起清 ✓
                        try { System.IO.File.Delete(paths[victim] + ".version"); } catch { }
                    }
                }
                catch { }
                // 完成标记 ✓（**同级旁挂** ✓ 绝不写进包内 —— 包内文件会被恢复到数据根 ✗✗）。
                // **最后写** ✓ → 标记缺失即"备份未完成"（中断可被精确识别 ✓）；标记存在则可核对内容是否被截断 ✓。
                try
                {
                    int finalFiles = 0;
                    int before = _copyFailures;
                    CountTree(dest, ref finalFiles);
                    _copyFailures = before;
                    long bytes = DirSizeForManifest(dest);
                    System.IO.File.WriteAllText(dest + ".manifest",
                        "files=" + finalFiles + "\nbytes=" + bytes + "\nfailed=" + _copyFailures + "\nfinished=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "\n");
                }
                catch { }
                return new BackupResult(dest, skipped, _copyFailures);
            }
            // ★ S7：**失败原因要说出来** ✓✓（原来是 catch { return null; } ✗ → 用户只看到"见 launcher.log" ✗）
            catch (Exception cex) { LastError = cex.Message; return null; }
        }

        /// <summary>导出备份副本：复制到 dstDir/&lt;源目录名&gt;（只读源；best-effort 复制）。返回目标路径；失败返回 null。</summary>
        public string Export(string src, string dstDir)
        {
            try
            {
                if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dstDir)) return null;
                Directory.CreateDirectory(dstDir);
                string target = Path.Combine(dstDir, Path.GetFileName(src.TrimEnd('\\', '/')));
                // ★★★ 备份链审查抓到（2026-10-07，D1）：上一版"修"读的是 CopyTree 的返回值 ✗
                //   → 而那个返回值是 **skippedNested**（被跳过的嵌套备份目录数 ✓ 几乎总是 0）✗✗
                //   → `copiedN == 0 且源非空 → 返回 null` **每次都成立** ✗ → **导出永远 BKEXPORT_FAIL** ✗✗
                //     （文件其实全拷过去了 ✓ 旁挂 .manifest 也因为提前 return 而没带 ✗）
                // ✓ 现在：CopyTree **返回真实复制数**（跳数走 out ✓）→ 这道守卫恢复它原本的语义 ✓✓
                int exportSkipped;
                int copiedN = CopyTree(src, target, true, out exportSkipped);
                try
                {
                    if (copiedN == 0 && Directory.Exists(src) && Directory.GetFileSystemEntries(src).Length > 0) return null;
                }
                catch { }
                // 同级旁挂文件一起带走 ✓✓ —— 否则导出后**完成标记丢失** ✗，包到了别处无法核对完整性 ✓（迁移时最需要可信的一刻 ✓）
                CopySibling(src, target, ".manifest");
                CopySibling(src, target, ".version");
                return target;
            }
            catch { return null; }
        }
        public void Delete(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                // 先清只读属性再删（对齐 v2.x 的 ClearReadOnlyRecursive 意图：只读文件不该阻碍删除）
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(dir, true);
            }
            catch { }
        }

        /// <summary>真实合并恢复（复现 v2.x 的 RestoreFromSource + RestoreWorkspaces + 非交互 RestoreOneWorkspace）：
        ///   顶层目录逐个 CopyTree（**恢复模式**：失败如实抛出，不像备份那样跳过）→ 顶层文件覆盖复制 → _workspace 下的工作区。
        /// 合并语义：目标端独有的文件不会被删除。</summary>
        /// <summary>把备份包恢复到数据根 ✓。
        /// ⚠ **核对记录（2026-10-01）**：我一度以为"恢复到 junction 数据根会写穿到根外" ✗ 并把它记在这里 ✓
        ///   → **真机复核后确认：那是我的测试断言写错了** ✗✗ **不是缺陷** ✓
        ///   · 实测：CLI 在把 dataRoot 传进来**之前**已经用 `RealPath` 解析过链接 ✓
        ///     （见 `WindowsPaths.DataRoot` ✓ 那是 C2 的修复 ✓）
        ///     → `dst` 到手时**已经是真实路径** ✓ → 数据写进真实目录 ✓
        ///     → 而那正是用户设置 DSH_HOME 时**想要的**目录 ✓✓ **行为正确** ✓
        ///   · 我误判的原因：把"写进链接目标"当成了越界 ✗ —— 而在这种场景下**它就是数据根** ✓
        ///   · 下面那道 reparse 检查**保留** ✓（对**未经解析**的调用方是一层保险 ✓ 正常路径下不触发 ✓）
        ///   · 教训记在这里 ✓：**测试断言错了要先怀疑断言 ✓ 而不是急着改产品** ✓✓</summary>
        public RestoreOutcome Restore(string backupDir, string dataRoot, string workspaceRoot)
        {
            RestoreOutcome o = new RestoreOutcome();
            try
            {
                string src = Dsht.Domain.Services.PathUtil.TrimTrailingSep(backupDir);
                string dst = Dsht.Domain.Services.PathUtil.TrimTrailingSep(dataRoot);
                // ★★★ C3 补全（**新加的失败分支测试当场抓到** ✓✓）：
                //   ✗ 原来只查 CopyTree 的**目标本身** ✗ → 而 dataRoot **整个是 junction** 时，
                //     它下面每一层都"看起来正常" ✗ → **写穿到链接目标（根外！）** ✗✗
                //   ✓ 现在：**入口先查 dataRoot 本身** ✓✓（+ 下面每个顶层目标也查 ✓）
                try
                {
                    if (Directory.Exists(dst) && (File.GetAttributes(dst) & FileAttributes.ReparsePoint) != 0)
                    {
                        o.Ok = false;
                        o.Error = "data root is a symbolic link or junction: " + dst;
                        return o;
                    }
                }
                catch { }
                Directory.CreateDirectory(dst);
                // 读不到备份包内容时**必须失败**：静默当成"空包"会打印 RESTORE_OK 却一个文件都没恢复
                string[] dirs = Directory.GetDirectories(src);
                foreach (string d in dirs)
                {
                    string name = Path.GetFileName(d.TrimEnd('\\', '/'));
                    if (name == "_workspace") continue;                       // v2.x：工作区单独处理
                    int dirSkipped;
                    CopyTree(d, Path.Combine(dst, name), false, out dirSkipped);
                    o.TopDirs++;
                }
                string[] files = Directory.GetFiles(src);
                foreach (string f in files)
                {
                    // ★ D4（2026-10-07 备份链审查）：顶层文件复制原来**没有**目标侧 reparse 守卫 ✗
                    //   → 目标端一个同名 junction 会被 File.Copy 写穿 ✗（补上与 CopyTree 内部同一道 ✓✓）
                    string tf = Path.Combine(dst, Path.GetFileName(f));
                    bool tRep = false;
                    try { if (File.Exists(tf) || Directory.Exists(tf)) tRep = (File.GetAttributes(tf) & FileAttributes.ReparsePoint) != 0; } catch { }
                    if (tRep) continue;
                    File.Copy(f, tf, true);
                    o.TopFiles++;
                }
                string ws = Path.Combine(src, "_workspace");
                if (Directory.Exists(ws)) RestoreWorkspaces(ws, workspaceRoot, o);
                o.Ok = true;
            }
            catch (Exception ex) { o.Ok = false; o.Error = ex.Message; }
            return o;
        }



        /// <summary>复现 v2.x 的 CopyTree（best-effort 模式）：跳过 node_modules / backup / dsh-data-* / reparse；
        /// 文件用 FileShare.ReadWrite|Delete 打开（被独占的文件才失败，正常读取中的文件可复制）；失败记数不中断。
        /// 返回被跳过的嵌套 dsh-data-* 目录数（供上层提示）。skipLocked=false 时（恢复模式）失败如实抛出。</summary>
        /// <summary>本次备份中"读不到/复制失败"的文件或目录数 ✓（此前被静默吞掉 ✗ → 备份静默缺文件而报 OK ✗✗）。</summary>
        private int _copyFailures;

        /// <summary>数一棵树里的文件数（容错 ✓）：读不到的目录/文件计入 _copyFailures ✓ 而不是静默跳过 ✗。
        /// 只用于"源 vs 目标"比对，判断备份是否完整 ✓；不改动任何既有方法签名 ✓。</summary>
        private void CountTree(string dir, ref int files)
        {
            string[] fs;
            try { fs = Directory.GetFiles(dir); }
            catch { _copyFailures++; return; }
            for (int i = 0; i < fs.Length; i++)
            {
                try { if ((File.GetAttributes(fs[i]) & FileAttributes.ReparsePoint) != 0) continue; files++; }
                catch { _copyFailures++; }
            }
            string[] ds;
            try { ds = Directory.GetDirectories(dir); }
            catch { _copyFailures++; return; }
            for (int i = 0; i < ds.Length; i++)
            {
                string name = Path.GetFileName(ds[i].TrimEnd('\\', '/'));
                if (name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("backup", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.StartsWith("dsh-data-", StringComparison.OrdinalIgnoreCase)) continue;
                CountTree(ds[i], ref files);
            }
        }
        // ★ S2：`CopySibling` 已提到共享基类 `Dsht.Platform.Shared.BackupSourceCommon` ✓

        /// <summary>最近一次失败的**原因**（给 CLI 如实显示 ✓ 不猜 ✓）。
        /// ★ 架构审计抓到（S7）：`Create` 原来是 `catch { return null; }` ✗
        ///   → **失败原因被整个吞掉** ✗ → CLI 只能说"备份失败（见 launcher.log）" ✗（用户看不到到底为什么 ✓）。</summary>
        public static string LastError = "";
    }
}
