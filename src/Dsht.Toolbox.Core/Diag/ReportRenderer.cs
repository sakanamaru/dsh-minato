using System.Text;

namespace Dsht.Toolbox.Core.Diag
{
    /// <summary>
    /// The only place that turns facts and findings into text or JSON, so the two formats cannot
    /// drift and the command layer never composes strings of its own.
    /// </summary>
    public static class ReportRenderer
    {
        public static string Human(DiagFacts facts, DiagPlan plan)
        {
            var text = new StringBuilder();
            text.AppendLine("dsht diag");
            text.AppendLine("  install : " + Value(facts.InstallDir));
            text.AppendLine("  version : " + Value(InstalledVersion(facts)) + "  (electron " + Value(facts.ElectronVersion) + ")");
            text.AppendLine("  updater : " + facts.Updater +
                            (string.IsNullOrEmpty(facts.PendingVersion) ? string.Empty : "  staged=" + facts.PendingVersion));
            text.AppendLine();

            foreach (Finding finding in plan.Findings)
            {
                text.AppendLine("  [" + Tag(finding.Level) + "] " + finding.Code + "  " + finding.Title);
                if (!string.IsNullOrEmpty(finding.Detail)) text.AppendLine("           " + finding.Detail);
                if (!string.IsNullOrEmpty(finding.Advice)) text.AppendLine("           -> " + finding.Advice);
            }

            int blocks = 0, warnings = 0;
            foreach (Finding finding in plan.Findings)
            {
                if (finding.Level == FindingLevel.Block) blocks++;
                else if (finding.Level == FindingLevel.Warn) warnings++;
            }

            text.AppendLine();
            if (blocks > 0) text.AppendLine("  verdict: blocked (" + blocks + " item(s) need a decision; see the advice lines)");
            else if (warnings > 0) text.AppendLine("  verdict: " + warnings + " item(s) need attention (see the advice lines)");
            else text.AppendLine("  verdict: no problems detected (diagnosis only; start the app to confirm it works)");

            return text.ToString();
        }

        public static string Json(DiagFacts facts, DiagPlan plan)
        {
            var json = new StringBuilder();
            json.Append('{');
            json.Append("\"installDir\":").Append(Quote(facts.InstallDir));
            json.Append(",\"installDirPresence\":").Append(Quote(facts.InstallDirPresence.ToString()));
            json.Append(",\"appVersion\":").Append(Quote(facts.AppVersion));
            json.Append(",\"productVersion\":").Append(Quote(facts.ProductVersion));
            json.Append(",\"electronVersion\":").Append(Quote(facts.ElectronVersion));
            json.Append(",\"updater\":").Append(Quote(facts.Updater.ToString()));
            json.Append(",\"pendingVersion\":").Append(Quote(facts.PendingVersion));
            json.Append(",\"pendingExe\":").Append(Quote(facts.PendingExeName));
            json.Append(",\"pendingSha512\":").Append(Quote(facts.PendingSha512));
            json.Append(",\"holders\":").Append(facts.Holders.Count);
            json.Append(",\"processEnumerationFailed\":").Append(Facts.Bool(facts.ProcessEnumerationFailed));
            json.Append(",\"payloadGaps\":[");
            for (int i = 0; i < facts.PayloadGaps.Count; i++)
            {
                if (i > 0) json.Append(',');
                json.Append(Quote(facts.PayloadGaps[i]));
            }
            json.Append(']');
            json.Append(",\"healthy\":").Append(Facts.Bool(plan.Healthy));
            json.Append(",\"needsAttention\":").Append(Facts.Bool(plan.NeedsAttention));

            json.Append(",\"findings\":[");
            for (int i = 0; i < plan.Findings.Count; i++)
            {
                if (i > 0) json.Append(',');
                Finding finding = plan.Findings[i];
                json.Append("{\"level\":").Append(Quote(finding.Level.ToString()))
                    .Append(",\"code\":").Append(Quote(finding.Code))
                    .Append(",\"title\":").Append(Quote(finding.Title))
                    .Append(",\"detail\":").Append(Quote(finding.Detail))
                    .Append(",\"advice\":").Append(Quote(finding.Advice)).Append('}');
            }

            json.Append("]}");
            return json.ToString();
        }

        static string InstalledVersion(DiagFacts facts)
        {
            return !string.IsNullOrEmpty(facts.AppVersion) ? facts.AppVersion : facts.ProductVersion;
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

        static string Value(string text)
        {
            return string.IsNullOrEmpty(text) ? "(unknown)" : text;
        }

        static string Quote(string text)
        {
            return Facts.Quote(text);
        }
    }

    /// <summary>Small shared primitives for the renderer; kept separate so they can be tested alone.</summary>
    public static class Facts
    {
        public static string Bool(bool value) { return value ? "true" : "false"; }

        /// <summary>Escapes a string for JSON. Returns the literal null token for a null value.</summary>
        public static string Quote(string text)
        {
            if (text == null) return "null";
            var quoted = new StringBuilder("\"");
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': quoted.Append("\\\\"); break;
                    case '\"': quoted.Append("\\\""); break;
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
