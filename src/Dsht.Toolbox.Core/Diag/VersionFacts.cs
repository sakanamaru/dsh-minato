using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Dsht.Toolbox.Core.Diag
{
    /// <summary>
    /// Parses and compares versions such as <c>0.2.2</c> and <c>0.2.2-rc.5</c>, including versions
    /// embedded in installer file names.
    ///
    /// Two details decide correctness:
    /// - a platform/architecture suffix must be removed before parsing, otherwise a stable release
    ///   named <c>deepseek-harness-0.2.2-win-x64.exe</c> reads as the pre-release <c>0.2.2-win</c>;
    /// - .NET normalises assembly versions to four parts and drops the pre-release tag, so callers
    ///   use <see cref="SameCore"/> instead of inventing an ordering they cannot prove.
    /// </summary>
    public static class VersionFacts
    {
        static readonly Regex SuffixPattern = new Regex(
            @"-(?:win|windows|linux|mac|macos|darwin|osx)(?:32|64)?(?:-x64|-arm64|-armv7l|-ia32)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static readonly Regex VersionPattern = new Regex(
            @"(?<![0-9.])(?<core>\d+(?:\.\d+){1,3})(?:-(?<pre>[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*))?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Extracts a version from text or an installer file name. Null when nothing parses.</summary>
        public static string Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string candidate = StripExtension(text);
            candidate = StripPlatformSuffix(candidate);

            Match match = VersionPattern.Match(candidate);
            if (!match.Success) return null;

            string core = TrimTrailingZeros(match.Groups["core"].Value);
            string pre = match.Groups["pre"].Success ? match.Groups["pre"].Value : null;
            return pre == null ? core : core + "-" + pre;
        }

        /// <summary>-1, 0 or 1. Null when either side cannot be parsed: unknown is not older.</summary>
        public static int? Compare(string left, string right)
        {
            string a = Parse(left), b = Parse(right);
            if (a == null || b == null) return null;

            int coreOrder = CompareCore(Core(a), Core(b));
            if (coreOrder != 0) return coreOrder;

            string preA = Pre(a), preB = Pre(b);
            if (preA == null && preB == null) return 0;
            if (preA == null) return 1;
            if (preB == null) return -1;
            return ComparePre(preA, preB);
        }

        /// <summary>True when both share a numeric core, ignoring any pre-release tag.</summary>
        public static bool SameCore(string left, string right)
        {
            string a = Parse(left), b = Parse(right);
            if (a == null || b == null) return false;
            return string.Equals(Core(a), Core(b), StringComparison.Ordinal);
        }

        static readonly Regex InstallerExtension = new Regex(
            @"\.(exe|msi|msix|zip|7z|dmg|pkg|appimage|tar\.gz|tar\.xz)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Removes only a known installer extension; a bare version keeps its dots.</summary>
        static string StripExtension(string name)
        {
            return InstallerExtension.Replace(name, string.Empty);
        }

        static string StripPlatformSuffix(string name)
        {
            return SuffixPattern.Replace(name, string.Empty);
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