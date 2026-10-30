using System;
using System.Collections.Generic;
using System.Text;
using Dsht.Domain.Model;

namespace Dsht.Domain.Services
{
    /// <summary>诊断报告组装（纯函数）。
    /// 与 v2.x 的 Doctor(--report) 逐行同格式：头部三行 → 各分类小节 → 配置摘要 → 日志摘要 → 结果行。
    /// **时间戳与系统字符串由调用方传入**：领域层不读时钟、不读环境（由 verify_domain_pure.ps1 守卫）。
    /// 每个条目文本都过 ReportSanitizer（脱敏后才写文件）。</summary>
    public static class DoctorReport
    {
        /// <summary>报告正文（含末尾换行；平台侧用 UTF-8 BOM 写文件，与 v2.x 一致）。</summary>
        public static string Build(string generatedAt, string toolkitVersion, string system, List<DocItem> items,
                                   string configSummary, string logSummary, string summary)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("== DeepSeek Harness Toolkit 诊断报告 ==");   // 契约比对覆盖此表头 → 不改；W4：**仅历史/契约基线**（不是 v2 运行时依赖）
            sb.AppendLine("生成时间: " + generatedAt);
            sb.AppendLine("Toolkit : " + toolkitVersion);
            sb.AppendLine("系统    : " + system);
            sb.AppendLine();
            string lastCat = "";
            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    DocItem it = items[i];
                    if (it.Cat != lastCat) { sb.AppendLine("-- " + it.Cat + " --"); lastCat = it.Cat; }
                    sb.AppendLine("  [" + DoctorSummary.Level(it.Level) + "] " + ReportSanitizer.Sanitize(it.Text));
                }
            }
            sb.AppendLine();
            sb.AppendLine("-- 配置摘要（脱敏） --");
            sb.AppendLine("  " + ReportSanitizer.Sanitize(configSummary));
            sb.AppendLine("-- 日志摘要（脱敏） --");
            sb.AppendLine("  " + ReportSanitizer.Sanitize(logSummary));
            sb.AppendLine();
            sb.AppendLine("结果: " + summary);
            return sb.ToString();
        }
    }
}
