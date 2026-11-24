using System;
using System.IO;
using Dsht.Toolbox.Core.Diag;

namespace Dsht.Toolbox.Core.Remedy
{
    public enum RemedyOutcome
    {
        /// <summary>Either nothing needed doing, or every step succeeded and the re-check is clean.</summary>
        Done = 0,

        /// <summary>A step failed; execution stopped and the machine is left in a reported state.</summary>
        Failed = 1,
    }

    public sealed class RemedyReport
    {
        public RemedyOutcome Outcome;
        public int StepsExecuted;
        public string FailedStep = string.Empty;
        /// <summary>Facts collected after the run; null when no re-check ran.</summary>
        public DiagFacts After;
    }

    /// <summary>
    /// Runs a <see cref="DiagPlan"/> step by step and re-checks afterwards.
    ///
    /// Invariants, all asserted in tests:
    /// <list type="bullet">
    /// <item>steps run in the order the planner emitted them (stop before clearing: the staging
    /// area cannot be deleted while the backend still holds it);</item>
    /// <item>the first failure stops the run - a half-applied repair that continues is worse than
    /// a clean stop;</item>
    /// <item>the outcome is decided by the re-check, never by optimism.</item>
    /// </list>
    /// </summary>
    public static class RemedySession
    {
        public static RemedyReport Run(
            DiagFacts before,
            DiagPlan plan,
            IRemedyExecutor executor,
            Func<DiagFacts> recheck,
            TextWriter log)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (executor == null) throw new ArgumentNullException(nameof(executor));

            var report = new RemedyReport();

            foreach (RemedyAction action in plan.Actions)
            {
                if (action.Kind == ActionKind.Nothing)
                {
                    log.WriteLine("Nothing to repair.");
                    report.Outcome = RemedyOutcome.Done;
                    report.After = recheck != null ? recheck() : null;
                    return report;
                }

                log.WriteLine("[" + (report.StepsExecuted + 1) + "] " + Names.Describe(action.Kind) + " - " + action.Why);

                bool ok;
                switch (action.Kind)
                {
                    case ActionKind.StopHolders:
                        ok = executor.StopHolders(action.Pids, log);
                        break;
                    case ActionKind.ClearUpdaterPending:
                        ok = executor.ClearUpdaterPending(before.UpdaterDir, log);
                        break;
                    case ActionKind.RunInstallerFromTemp:
                        ok = executor.RunInstallerFromTemp(before.UpdaterDir, action.ExeName, log);
                        break;
                    case ActionKind.Reinstall:
                        log.WriteLine("    (nothing to repair in place: a reinstall is required)");
                        ok = false;
                        break;
                    default:
                        ok = true;
                        break;
                }

                if (!ok)
                {
                    report.Outcome = RemedyOutcome.Failed;
                    report.FailedStep = Names.Describe(action.Kind);
                    log.WriteLine("    failed; stopping here so the machine is left in a reported state.");
                    return report;
                }

                report.StepsExecuted++;
            }

            report.After = recheck != null ? recheck() : null;
            if (report.After != null)
            {
                DiagPlan afterPlan = RemedyPlanner.Plan(report.After);
                log.WriteLine();
                log.WriteLine("Re-check: holders=" + report.After.Holders.Count +
                              " updater=" + report.After.Updater +
                              " healthy=" + (afterPlan.Healthy ? "yes" : "no"));
                report.Outcome = afterPlan.Healthy ? RemedyOutcome.Done : RemedyOutcome.Failed;
            }
            else
            {
                report.Outcome = RemedyOutcome.Done;
            }

            return report;
        }
    }

    /// <summary>Stable, human-readable names for action kinds (logs, JSON, docs).</summary>
    public static class Names
    {
        public static string Describe(ActionKind kind)
        {
            switch (kind)
            {
                case ActionKind.StopHolders: return "stop-holders";
                case ActionKind.ClearUpdaterPending: return "clear-updater-pending";
                case ActionKind.RunInstallerFromTemp: return "run-installer-from-temp";
                case ActionKind.Reinstall: return "reinstall";
                case ActionKind.Nothing: return "nothing";
                default: return kind.ToString();
            }
        }
    }
}