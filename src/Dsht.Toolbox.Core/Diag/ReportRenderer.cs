using System;
using System.Collections.Generic;
using System.Text;

namespace Dsht.Toolbox.Core.Diag
{
    /// <summary>把事实与计划渲染成人类文本 / JSON（纯函数）。</summary>
    public static class ReportRenderer
    {
        public static string Human(DiagFacts f, DiagPlan p)
        {
            var sb = new StringBuilder();
            sb.AppendLine("dsht diag");
            sb.AppendLine("  install : " + Show(f.InstallDir));
            sb.AppendLine("  version : " + Show(f.ProductVersion) + "   (electron " + Show(f.ElectronVersion) + ")");
            sb.AppendLine("  updater : " + f.Updater + (string.IsNullOrEmpty(f.PendingVersion) ? "" : "  pending=" + f.PendingVersion));
            sb.AppendLine();
            foreach (var x in p.Findings)
            {
                sb.AppendLine("  [" + Tag(x.Level) + "] " + x.Code + "  " + x.Title);
                if (!string.IsNullOrEmpty(x.Detail)) sb.AppendLine("           " + x.Detail);
            }
            sb.AppendLine();
            if (p.Actions.Count == 0) sb.AppendLine("  计划：无动作");
            else
            {
                sb.AppendLine("  计划：");
                for (int i = 0; i < p.Actions.Count; i++)
                {
                    var a = p.Actions[i];
                    sb.AppendLine("    " + (i + 1) + ". " + a.Kind + (a.Dangerous ? "  [需要 --yes]" : "") + "  — " + a.Why);
                }
            }
            sb.AppendLine();
            int blocks = 0, warns = 0;
            foreach (var x in p.Findings) { if (x.Level == FindingLevel.Block) blocks++; else if (x.Level == FindingLevel.Warn) warns++; }
            string verdict;
            if (blocks > 0) verdict = "结论：有阻塞项（见 [XX]）";
            else if (warns > 0) verdict = "结论：有 " + warns + " 项待修（加 --fix 执行计划）";
            else verdict = "结论：干净";
            sb.AppendLine("  " + verdict);
            return sb.ToString();
        }

        public static string Json(DiagFacts f, DiagPlan p)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"installDir\":").Append(Q(f.InstallDir));
            sb.Append(",\"installDirExists\":").Append(f.InstallDirExists ? "true" : "false");
            sb.Append(",\"productVersion\":").Append(Q(f.ProductVersion));
            sb.Append(",\"electronVersion\":").Append(Q(f.ElectronVersion));
            sb.Append(",\"updater\":").Append(Q(f.Updater.ToString()));
            sb.Append(",\"pendingVersion\":").Append(Q(f.PendingVersion));
            sb.Append(",\"pendingExe\":").Append(Q(f.PendingExeName));
            sb.Append(",\"holders\":").Append(f.Holders.Count);
            sb.Append(",\"unreadableProcesses\":").Append(f.UnreadableProcesses);
            sb.Append(",\"healthy\":").Append(p.Healthy ? "true" : "false");
            sb.Append(",\"findings\":[");
            for (int i = 0; i < p.Findings.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var x = p.Findings[i];
                sb.Append("{\"level\":").Append(Q(x.Level.ToString()))
                  .Append(",\"code\":").Append(Q(x.Code))
                  .Append(",\"title\":").Append(Q(x.Title))
                  .Append(",\"detail\":").Append(Q(x.Detail)).Append('}');
            }
            sb.Append("],\"actions\":[");
            for (int i = 0; i < p.Actions.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var a = p.Actions[i];
                sb.Append("{\"kind\":").Append(Q(a.Kind.ToString()))
                  .Append(",\"dangerous\":").Append(a.Dangerous ? "true" : "false")
                  .Append(",\"why\":").Append(Q(a.Why)).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string Tag(FindingLevel l)
        {
            switch (l)
            {
                case FindingLevel.Ok: return "ok";
                case FindingLevel.Info: return "..";
                case FindingLevel.Warn: return "!!";
                default: return "XX";
            }
        }

        static string Show(string s) { return string.IsNullOrEmpty(s) ? "(未知)" : s; }

        static string Q(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}