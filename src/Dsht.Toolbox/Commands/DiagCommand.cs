using System;
using System.IO;
using Dsht.Toolbox.Core.Diag;
using Dsht.Toolbox.Core.Remedy;
using Dsht.Toolbox.Windows;

namespace Dsht.Toolbox.Commands
{
    /// <summary>
    /// <c>dsht diag</c>: diagnose a desktop install, optionally repair it.
    /// Read-only unless <c>--fix</c> is passed, and destructive steps additionally need
    /// <c>--yes</c>, so an accidental run cannot close a user's session.
    /// </summary>
    internal static class DiagCommand
    {
        public static int Run(string[] args)
        {
            bool json = Flags.Has(args, "--json");
            bool fix = Flags.Has(args, "--fix");
            bool yes = Flags.Has(args, "--yes");

            string installDir = Flags.Value(args, "--dir") ?? DesktopProbe.DefaultInstallDir();
            string updaterDir = Flags.Value(args, "--updater") ?? DesktopProbe.DefaultUpdaterDir();

            DiagFacts facts = DesktopProbe.Collect(installDir, updaterDir);
            DiagPlan plan = RemedyPlanner.Plan(facts);

            Console.Write(json ? ReportRenderer.Json(facts, plan) + Environment.NewLine : ReportRenderer.Human(facts, plan));

            if (!fix) return plan.Healthy ? ExitCodes.Ok : ExitCodes.Failed;

            if (NeedsConsent(plan) && !yes)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("This plan closes processes and/or runs the installer. Re-run with --fix --yes to apply it.");
                return ExitCodes.Usage;
            }

            Console.WriteLine();
            RemedyReport report = RemedySession.Run(
                facts, plan, new WindowsRemedyExecutor(),
                delegate { return DesktopProbe.Collect(installDir, updaterDir); },
                Console.Out);

            if (report.Outcome == RemedyOutcome.Failed)
            {
                Console.Error.WriteLine("Repair did not complete" +
                    (string.IsNullOrEmpty(report.FailedStep) ? "." : " at step '" + report.FailedStep + "'."));
                return ExitCodes.Failed;
            }

            return ExitCodes.Ok;
        }

        static bool NeedsConsent(DiagPlan plan)
        {
            foreach (RemedyAction action in plan.Actions)
                if (action.Dangerous) return true;
            return false;
        }
    }

    /// <summary>Tiny argument reader; every command shares it so flag behaviour cannot drift.</summary>
    internal static class Flags
    {
        public static bool Has(string[] args, string name)
        {
            foreach (string arg in args)
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string Value(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
