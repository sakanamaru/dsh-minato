using System;

namespace Dsht.Domain.Services
{
    /// <summary>路径判定（纯函数）。
    /// TrimTrailingSep 逐条对齐 v2.x；IsSubPath 在 v2.x 里只认反斜杠，这里同时认正反斜杠
    /// （Linux 上路径用 /）——对 Windows 输入结果完全一致。</summary>
    public static class PathUtil
    {
        /// <summary>去尾部分隔符，但保留盘根语义（D:\ 不变成 D:，UNC 共享根保留尾部斜杠）。</summary>
        public static string TrimTrailingSep(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            while (p.Length > 1 && (p.EndsWith("\\", StringComparison.Ordinal) || p.EndsWith("/", StringComparison.Ordinal)))
            {
                // 盘根：X:\ 保留
                if (p.Length == 3 && p[1] == ':' && (p[2] == '\\' || p[2] == '/')) break;
                // UNC 共享根：\\server\share 保留尾部斜杠（此处保守：只保留形如 \\a\b 的最小形态）
                string t = p.TrimEnd('\\', '/');
                int sep = t.IndexOf('\\');
                if (p.StartsWith("\\\\", StringComparison.Ordinal) && t.Length - t.LastIndexOf('\\') >= 0 && t.IndexOf('\\', sep + 2 < t.Length ? sep + 2 : t.Length) < 0) break;
                p = t;
            }
            return p;
        }

        /// <summary>纯词法规范化：解析 `.` 与 `..`，统一分隔符。**不做 IO、不查磁盘、不看当前目录** ✓（领域纯净 ✓）。
        /// 返回 null = 路径试图逃逸到根之上 ✓（调用方必须按"不安全"处理 ✓）。</summary>
        public static string NormalizeLexical(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            char sep = p.IndexOf('\\') >= 0 ? '\\' : '/';
            string[] parts = p.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            string prefix = null;
            int start = 0;
            int floor = 0;   // ★ C10：栈底这几段**不能被 `..` 弹掉**（UNC 的 server\share 就是根 ✓）
            bool unc = p.Length >= 2 && (p[0] == '\\' || p[0] == '/') && (p[1] == '\\' || p[1] == '/');
            if (parts.Length > 0 && parts[0].Length == 2 && parts[0][1] == ':') { prefix = parts[0]; start = 1; }   // 盘符 C:
            else if (p.Length > 0 && (p[0] == '\\' || p[0] == '/')) { prefix = ""; start = 0; }                     // 根 / 或 \
            var stack = new System.Collections.Generic.List<string>();
            // ★ C10：UNC 形式 `\\server\share\...` 的**前两段是根** ✓ → 记进 floor ✓ 并跳过 ✓
            //   （架构审计：原来 `\\server\share\..\..\x` 会把 share 和 server 都弹掉 ✗
            //     → 返回一个"看起来像根路径"的东西 ✗ —— 与"逃逸必须返回 null"的契约不符 ✓
            //     虽然 IsSubPath 那边仍然 fail-closed ✓ 但**契约本身必须成立** ✓✓）
            if (unc && parts.Length >= 2)
            {
                stack.Add(parts[0]);
                stack.Add(parts[1]);
                floor = 2;
                start = 2;
            }
            for (int i = start; i < parts.Length; i++)
            {
                string s = parts[i];
                if (s == ".") continue;
                if (s == "..")
                {
                    if (stack.Count <= floor) return null;   // ★ 逃逸 → 不安全 ✓✓（UNC 根段不许被弹 ✓）
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }
                stack.Add(s);
            }
            string body = string.Join(sep.ToString(), stack.ToArray());
            if (prefix == null) return body;
            if (prefix.Length == 0) return (unc ? sep.ToString() + sep : sep.ToString()) + body;   // ★ C10：UNC 保留两个前导分隔符 ✓
            return prefix + sep + body;
        }

        /// <summary>child 是否位于 parent 子树内（含相等）；大小写不敏感。
        /// ★ **先做纯词法规范化再比** ✓✓ —— 否则 `&lt;根&gt;\..\..\重要目录` 会因为字符串前缀相同而被当成"在根内" ✗
        ///   （真机审查抓到：`backup-delete` 能借此递归删除根外的任意同名目录 ✗✗）
        /// 任一侧含逃逸（`..` 跑到根之上）→ **一律返回 false** ✓（fail-closed ✓）。</summary>
        public static bool IsSubPath(string parent, string child)
        {
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(child)) return false;
            string na = NormalizeLexical(parent);
            string nb = NormalizeLexical(child);
            if (na == null || nb == null) return false;   // 有逃逸 → 不在子树内 ✓✓
            // ★★ 架构审计抓到（C9）：原来**无条件转小写** ✗
            //   → 而 Linux 上 `/bk` 与 `/BK` 是**两个不同目录** ✓
            //     → 于是"包含性判定"比文件系统**更宽松** ✗ → 接受了 OS 认为在根外的路径 ✗
            //   ✓ 现在：**只在 Windows 风格路径上做大小写折叠** ✓✓
            //     · 判据：含 `\` 或盘符（`X:`）→ Windows 语义（不区分大小写 ✓）
            //     · 其余（`/` 分隔 ✓）→ **保持大小写敏感** ✓（与 POSIX 一致 ✓）
            //   · 纯函数 ✓ 不看平台 API ✓ 领域纯净不破 ✓
            bool caseFold = na.IndexOf('\\') >= 0 || nb.IndexOf('\\') >= 0
                            || (na.Length >= 2 && na[1] == ':') || (nb.Length >= 2 && nb[1] == ':');
            string a = TrimTrailingSep(na);
            string b = TrimTrailingSep(nb);
            if (caseFold) { a = a.ToLowerInvariant(); b = b.ToLowerInvariant(); }
            if (b == a) return true;
            // ★★★ 备份链审查抓到（2026-10-07，D5）：根路径做父级时前缀比对会多拼一个分隔符 ✗✗
            //   → TrimTrailingSep **特意保留**盘根尾部（`D:\` 不变成 `D:` ✓ 上方注释 ✓）
            //     → 这里 `a + "\\"` 变成 `D:\\` ✗ → `D:\foo` 永远"不在根内" ✗（POSIX 根 `/` → `//` 同病 ✗）
            //   → 后果：备份根设在**盘根**（如 `D:\`）时 ✓ backup-export / backup-delete 的
            //     "必须在备份根内"校验把**根里每一个包**都判成"在根外" ✗✗ → 导出/删除全灭 ✗
            // ✓ 现在：父级本身以分隔符结尾（即根）→ 直接比 `b.StartsWith(a)` ✓✓（不再多拼 ✓）
            if (a.EndsWith("\\", StringComparison.Ordinal) || a.EndsWith("/", StringComparison.Ordinal))
                return b.StartsWith(a, StringComparison.Ordinal);
            return b.StartsWith(a + "\\", StringComparison.Ordinal) || b.StartsWith(a + "/", StringComparison.Ordinal);
        }
    }
}