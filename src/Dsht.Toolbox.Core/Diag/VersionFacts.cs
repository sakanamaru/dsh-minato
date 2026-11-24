using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Dsht.Toolbox.Core.Diag
{
    /// <summary>版本解析与比较（纯函数，可测）。
    /// 支持 `0.2.2`、`0.2.2-rc.5`、以及从文件名里取版本（`deepseek-harness-0.2.2-rc.5-win-x64.exe`）。
    /// 预发行后缀只接受字母/数字/点：遇到连字符即止（否则会把 `-win-x64.exe` 当成后缀）。</summary>
    public static class VersionFacts
    {
        static readonly Regex Rx = new Regex(
            @"(?<![0-9.])(?<num>\d+(?:\.\d+){1,3})(?:-(?<pre>[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*))?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>取版本核心（如 0.2.2-rc.5）。取不到返回 null（不猜）。</summary>
        public static string Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            Match m = Rx.Match(text);
            if (!m.Success) return null;
            string num = TrimZeros(m.Groups["num"].Value);
            string pre = m.Groups["pre"].Success ? m.Groups["pre"].Value : null;
            return pre == null ? num : num + "-" + pre;
        }

        /// <summary>比较：-1 / 0 / 1；任一不可解析返回 null（不猜）。注意 rc.10 &gt; rc.9。</summary>
        public static int? Compare(string a, string b)
        {
            string pa = Parse(a), pb = Parse(b);
            if (pa == null || pb == null) return null;

            string coreA = Core(pa), coreB = Core(pb);
            string[] na = coreA.Split('.'), nb = coreB.Split('.');
            int len = Math.Max(na.Length, nb.Length);
            for (int i = 0; i < len; i++)
            {
                int x = i < na.Length ? Int(na[i]) : 0;
                int y = i < nb.Length ? Int(nb[i]) : 0;
                if (x != y) return x < y ? -1 : 1;
            }

            string preA = Pre(pa), preB = Pre(pb);
            if (preA == null && preB == null) return 0;
            if (preA == null) return 1;   // 正式版 > 预发行版
            if (preB == null) return -1;
            return ComparePre(preA, preB);
        }

        /// <summary>两个版本的核心数字是否相同（忽略预发行后缀）。任一不可解析返回 false。</summary>
        public static bool SameCore(string a, string b)
        {
            string pa = Parse(a), pb = Parse(b);
            if (pa == null || pb == null) return false;
            return string.Equals(Core(pa), Core(pb), StringComparison.Ordinal);
        }

        static int ComparePre(string a, string b)
        {
            string[] pa = a.Split('.'), pb = b.Split('.');
            int len = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < len; i++)
            {
                string x = i < pa.Length ? pa[i] : null;
                string y = i < pb.Length ? pb[i] : null;
                if (x == null) return -1;
                if (y == null) return 1;
                int nx, ny;
                bool ix = int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out nx);
                bool iy = int.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out ny);
                if (ix && iy) { if (nx != ny) return nx < ny ? -1 : 1; continue; }
                int c = string.Compare(x, y, StringComparison.Ordinal);
                if (c != 0) return c < 0 ? -1 : 1;
            }
            return 0;
        }

        /// <summary>去掉尾部 .0（.NET 会把 ProductVersion 规范成 4 段：0.2.2 → 0.2.2.0）。</summary>
        static string TrimZeros(string num)
        {
            if (string.IsNullOrEmpty(num)) return num;
            while (num.EndsWith(".0", StringComparison.Ordinal)) num = num.Substring(0, num.Length - 2);
            return num;
        }

        static string Core(string v)
        {
            int i = v.IndexOf('-');
            return i >= 0 ? v.Substring(0, i) : v;
        }

        static string Pre(string v)
        {
            int i = v.IndexOf('-');
            return i >= 0 ? v.Substring(i + 1) : null;
        }

        static int Int(string s)
        {
            int n;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }
    }
}