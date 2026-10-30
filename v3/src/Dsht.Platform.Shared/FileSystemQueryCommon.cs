using System;
using System.Collections.Generic;
using System.IO;
using Dsht.Domain.Abstractions;

namespace Dsht.Platform.Shared
{
    /// <summary>只读文件系统查询（Windows/Linux 共用实现 · B-05 平台层去重）。
    /// 两平台原本逐行相同 ⇒ 抽到共享源目录，由两端 csproj 以 Compile Include + Link 编译同一份
    /// （与 MarkerText.cs / BackupSourceCommon.cs 同一手法）。行为与去重前逐字不变：
    /// DirSize 与 v2.x 的 DirSize 同实现（迭代栈累加）。</summary>
    public abstract class FileSystemQueryCommon : IFileSystemQuery
    {
        public bool DirectoryExists(string path)
        {
            try { return !string.IsNullOrEmpty(path) && Directory.Exists(path); } catch { return false; }
        }

        public bool CanEnumerate(string path)
        {
            try { Directory.GetFiles(path); return true; } catch { return false; }
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

        public string[] ListDirectories(string path)
        {
            try { return Directory.GetDirectories(path); } catch { return new string[0]; }
        }

        public bool IsReparse(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; } catch { return false; }
        }

        public bool FileExists(string path)
        {
            try { return File.Exists(path); } catch { return false; }
        }

        /// <summary>复现 v2.x 的 WalkFiles(root, dir, acc, copyRules)：先递归子目录、再收文件；键为相对 root 的路径。</summary>
        private static void Walk(string root, string dir, System.Collections.Generic.Dictionary<string, long> acc, bool copyRules)
        {
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { subs = new string[0]; }
            foreach (string d in subs)
            {
                string name = Path.GetFileName(d.TrimEnd('\\', '/'));
                bool rep = false;
                try { rep = (File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0; } catch { }
                if (copyRules) { if (Dsht.Domain.Services.SkipRules.SkipDir(name, rep)) continue; }
                else { if (rep) continue; }
                Walk(root, d, acc, copyRules);
            }
            string[] files;
            try { files = Directory.GetFiles(dir); } catch { files = new string[0]; }
            string rootPrefix = root;
            foreach (string f in files)
            {
                try
                {
                    if ((File.GetAttributes(f) & FileAttributes.ReparsePoint) != 0) continue;
                    FileInfo fi = new FileInfo(f);
                    string rel = fi.FullName.Length > rootPrefix.Length ? fi.FullName.Substring(rootPrefix.Length).TrimStart('\\', '/') : fi.Name;
                    acc[rel] = fi.Length;
                }
                catch { }
            }
        }

        public System.Collections.Generic.Dictionary<string, long> WalkSource(string src, string skipTopDir, string skipTopFile, bool topRules)
        {
            System.Collections.Generic.Dictionary<string, long> map = new System.Collections.Generic.Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string d in Directory.GetDirectories(src))
                {
                    string name = Path.GetFileName(d.TrimEnd('\\', '/'));
                    if (skipTopDir != null && name.Equals(skipTopDir, StringComparison.OrdinalIgnoreCase)) continue;
                    bool rep = false;
                    try { rep = (File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0; } catch { }
                    if (topRules && Dsht.Domain.Services.SkipRules.SkipDir(name, rep)) continue;
                    Walk(src, d, map, true);
                }
                foreach (string f in Directory.GetFiles(src))
                {
                    string name = Path.GetFileName(f.TrimEnd('\\', '/'));
                    if (skipTopFile != null && name.Equals(skipTopFile, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        if ((File.GetAttributes(f) & FileAttributes.ReparsePoint) != 0) continue;
                        map[name] = new FileInfo(f).Length;
                    }
                    catch { }
                }
            }
            catch { }
            return map;
        }

        public System.Collections.Generic.Dictionary<string, long> WalkDestination(string dst)
        {
            System.Collections.Generic.Dictionary<string, long> map = new System.Collections.Generic.Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try { if (Directory.Exists(dst)) Walk(dst, dst, map, false); } catch { }
            return map;
        }
    }
}
