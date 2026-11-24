using System.Text;

namespace Dsht.Toolbox.Core.Diag
{
    /// <summary>
    /// Renders facts and a plan as text or JSON. Pure, and deliberately the only place that
    /// decides what a user sees: the command layer never formats strings of its own.
    /// </summary>
    public static class ReportRenderer
    {
        public static string Human(DiagFacts facts, DiagPlan plan)
        {
            var text = new StringBuilder();
            text.AppendLine("dsht diag");
            text.AppendLine("  install : " + Value(facts.InstallDir));
            text.AppendLine("  version : " + Value(facts.ProductVersion) + "  (electron " + Value(facts.ElectronVersion) + ")");
            text.AppendLine("  updater : " + facts.Updater +
                            (string.IsNullOrEmpty(facts.PendingVersion) ? string.Empty : "  pending=" + facts.PendingVersion));
            text.AppendLine();

            foreach (Finding finding in plan.Findings)
            {
                text.AppendLine("  [" + Tag(finding.Level) + "] " + finding.Code + "  " + finding.Title);
                if (!string.IsNullOrEmpty(finding.Detail)) text.AppendLine("           " + finding.Detail);
            }

            text.AppendLine();
            if (plan.Actions.Count == 0)
            {
                text.AppendLine("  plan: no action");
            }
            else
            {
                text.AppendLine("  plan:");
                for (int i = 0; i < plan.Actions.Count; i++)
                {
                    RemedyAction action = plan.Actions[i];
                    text.AppendLine("    " + (i + 1) + ". " + Remedy.Names.Describe(action.Kind) +
                                    (action.Dangerous ? "  [needs --yes]" : string.Empty) +
                                    "  - " + action.Why);
                }
            }

            int blocks = 0, warnings = 0;
            foreach (Finding finding in plan.Findings)
            {
                if (finding.Level == FindingLevel.Block) blocks++;
                else if (finding.Level == FindingLevel.Warn) warnings++;
            }

            text.AppendLine();
            if (blocks > 0) text.AppendLine("  verdict: blocked (see [XX])");
            else if (warnings > 0) text.AppendLine("  verdict: " + warnings + " item(s) to repair (run with --fix)");
            else text.AppendLine("  verdict: clean");

            return text.ToString();
        }

        public static string Json(DiagFacts facts, DiagPlan plan)
        {
            var json = new StringBuilder();
            json.Append('{');
            json.Append("\"installDir\":").Append(Quote(facts.InstallDir));
            json.Append(",\"installDirExists\":").Append(Bool(facts.InstallDirExists));
            json.Append(",\"productVersion\":").Append(Quote(facts.ProductVersion));
            json.Append(",\"electronVersion\":").Append(Quote(facts.ElectronVersion));
            json.Append(",\"updater\":").Append(Quote(facts.Updater.ToString()));
            json.Append(",\"pendingVersion\":").Append(Quote(facts.PendingVersion));
            json.Append(",\"pendingExe\":").Append(Quote(facts.PendingExeName));
            json.Append(",\"holders\":").Append(facts.Holders.Count);
            json.Append(",\"unreadableProcesses\":").Append(facts.UnreadableProcesses);
            json.Append(",\"healthy\":").Append(Bool(plan.Healthy));

            json.Append(",\"findings\":[");
            for (int i = 0; i < plan.Findings.Count; i++)
            {
                if (i > 0) json.Append(',');
                Finding finding = plan.Findings[i];
                json.Append("{\"level\":").Append(Quote(finding.Level.ToString()))
                    .Append(",\"code\":").Append(Quote(finding.Code))
                    .Append(",\"title\":").Append(Quote(finding.Title))
                    .Append(",\"detail\":").Append(Quote(finding.Detail)).Append('}');
            }

            json.Append("],\"actions\":[");
            for (int i = 0; i < plan.Actions.Count; i++)
            {
                if (i > 0) json.Append(',');
                RemedyAction action = plan.Actions[i];
                json.Append("{\"kind\":").Append(Quote(Remedy.Names.Describe(action.Kind)))
                    .Append(",\"dangerous\":").Append(Bool(action.Dangerous))
                    .Append(",\"why\":").Append(Quote(action.Why)).Append('}');
            }

            json.Append("]}");
            return json.ToString();
        }

        static string Tag(FindingLevel level)
        {
            switch (level)
            {
                case FindingLevel.Ok: return "ok";
                case FindingLevel.Info: return "..";
                case FindingLevel.Warn: return "!!";
                default: return "XX";
            }
        }

        static string Bool(bool value) { return value ? "true" : "false"; }

        static string Value(string text) { return string.IsNullOrEmpty(text) ? "(unknown)" : text; }

        static string Quote(string text)
        {
            if (text == null) return "null";
            var quoted = new StringBuilder("\"");
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': quoted.Append("\\\\"); break;
                    case '"': quoted.Append("\\\""); break;
                    case '\n': quoted.Append("\\n"); break;
                    case '\r': quoted.Append("\\r"); break;
                    case '\t': quoted.Append("\\t"); break;
                    default:
                        if (c < 32) quoted.Append("\\u").Append(((int)c).ToString("x4"));
                        else quoted.Append(c);
                        break;
                }
            }
            return quoted.Append('"').ToString();
        }
    }
}
