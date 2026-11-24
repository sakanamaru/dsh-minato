using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Dsht.Toolbox.Core.Diag
{
    /// <summary>
    /// Parses and compares dsh versions such as <c>0.2.2</c> and <c>0.2.2-rc.5</c>.
    ///
    /// Two behaviours matter for correctness:
    /// <list type="bullet">
    /// <item>a file name like <c>deepseek-harness-0.2.2-rc.5-win-x64.exe</c> must not treat
    /// <c>-win-x64</c> as a pre-release tag, so the tag stops at the first hyphen;</item>
    /// <item>.NET normalises assembly versions to four parts (<c>0.2.2.0</c>), which loses
    /// <c>-rc.5</c>. Trailing zeros are stripped so the numeric core still compares, and
    /// callers use <see cref="SameCore"/> instead of inventing an ordering they cannot prove.</item>
    /// </list>
    /// </summary>
    public static class VersionFacts
    {
        static readonly Regex VersionPattern = new Regex(
            @"(?<![0-9.])(?<core>\d+(?:\.\d+){1,3})(?:-(?<pre>[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*))?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Extracts a version from arbitrary text. Returns null when nothing parses.</summary>
        public static string Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            Match match = VersionPattern.Match(text);
            if (!match.Success) return null;

            string core = TrimTrailingZeros(match.Groups["core"].Value);
            string pre = match.Groups["pre"].Success ? match.Groups["pre"].Value : null;
            return pre == null ? core : core + "-" + pre;
        }

        /// <summary>
        /// Orders two versions: -1, 0 or 1. Returns null when either side cannot be parsed,
        /// because an unparsable version is unknown, not older.
        /// </summary>
        public static int? Compare(string left, string right)
        {
            string a = Parse(left);
            string b = Parse(right);
            if (a == null || b == null) return null;

            int coreOrder = CompareCore(Core(a), Core(b));
            if (coreOrder != 0) return coreOrder;

            string preA = Pre(a);
            string preB = Pre(b);
            if (preA == null && preB == null) return 0;
            if (preA == null) return 1;   // a release outranks its pre-release
            if (preB == null) return -1;
            return ComparePre(preA, preB);
        }

        /// <summary>
        /// True when both sides share the same numeric core, ignoring any pre-release tag.
        /// Used where the installed side can only expose an assembly version and an ordering
        /// claim would therefore be unprovable.
        /// </summary>
        public static bool SameCore(string left, string right)
        {
            string a = Parse(left);
            string b = Parse(right);
            if (a == null || b == null) return false;
            return string.Equals(Core(a), Core(b), StringComparison.Ordinal);
        }

        static int CompareCore(string a, string b)
        {
            string[] left = a.Split('.');
            string[] right = b.Split('.');
            int length = Math.Max(left.Length, right.Length);
            for (int i = 0; i < length; i++)
            {
                int x = i < left.Length ? ToInt(left[i]) : 0;
                int y = i < right.Length ? ToInt(right[i]) : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }

        /// <summary>Numeric segments compare as numbers (rc.10 &gt; rc.9); others ordinal.</summary>
        static int ComparePre(string a, string b)
        {
            string[] left = a.Split('.');
            string[] right = b.Split('.');
            int length = Math.Max(left.Length, right.Length);
            for (int i = 0; i < length; i++)
            {
                if (i >= left.Length) return -1;
                if (i >= right.Length) return 1;

                int x, y;
                bool numericX = int.TryParse(left[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out x);
                bool numericY = int.TryParse(right[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
                if (numericX && numericY)
                {
                    if (x != y) return x < y ? -1 : 1;
                    continue;
                }
                int order = string.Compare(left[i], right[i], StringComparison.Ordinal);
                if (order != 0) return order < 0 ? -1 : 1;
            }
            return 0;
        }

        static string Core(string version)
        {
            int dash = version.IndexOf('-');
            return dash >= 0 ? version.Substring(0, dash) : version;
        }

        static string Pre(string version)
        {
            int dash = version.IndexOf('-');
            return dash >= 0 ? version.Substring(dash + 1) : null;
        }

        static string TrimTrailingZeros(string core)
        {
            while (core.EndsWith(".0", StringComparison.Ordinal)) core = core.Substring(0, core.Length - 2);
            return core;
        }

        static int ToInt(string value)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }
    }
}
