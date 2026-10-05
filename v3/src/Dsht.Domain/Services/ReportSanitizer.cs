using System.Text.RegularExpressions;

namespace Dsht.Domain.Services
{
    /// <summary>报告/日志出口统一消毒（纯函数）。逐字对齐 v2.x 的 SanitizeForReport：
    ///   URL 查询参数 token=/key=/auth=/session=…；独立密钥键的 key:value / key=value；40+ 位十六进制串。</summary>
    public static class ReportSanitizer
    {
        public static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try
            {
                s = Regex.Replace(s, @"([?&](?:token|key|api[_-]?key|auth|session|password|passwd|secret|cookie)[=])([^&#\s]+)", "$1[REDACTED]", RegexOptions.IgnoreCase);
                s = Regex.Replace(s, @"(?i)\b(api[_-]?key|balance[_-]?key|password|passwd|secret|cookie|token|session)\b\s*[=:]\s*[^,\s;]+", "$1=[REDACTED]");
                s = Regex.Replace(s, @"\b[0-9a-fA-F]{40,}\b", "[REDACTED]");
            }
            catch { }
            return s;
        }
    }
}