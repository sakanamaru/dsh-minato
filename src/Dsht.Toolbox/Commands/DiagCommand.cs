using System;
using Dsht.Toolbox.Core.Diag;
using Dsht.Toolbox.Windows;

namespace Dsht.Toolbox.Commands
{
    /// <summary>
    /// <c>dsht diag</c>: diagnose a desktop install. Read-only by construction - there is no repair
    /// flag, because a repair this tool cannot verify is a repair it should not perform.
    /// </summary>
    internal static class DiagCommand
    {
        public static int Run(string[] args)
        {
            var options = new DiagOptions();
            if (!options.Parse(args))
            {
                Console.Error.WriteLine(options.Error);
                return ExitCodes.Usage;
            }

            string installDir = options.InstallDir ?? DesktopProbe.DefaultInstallDir();
            string updaterDir = options.UpdaterDir ?? DesktopProbe.DefaultUpdaterDir();

            DiagFacts facts = DesktopProbe.Collect(installDir, updaterDir);
            DiagPlan plan = RemedyPlanner.Plan(facts);

            Console.Write(options.Json
                ? ReportRenderer.Json(facts, plan) + Environment.NewLine
                : ReportRenderer.Human(facts, plan));

            return plan.Healthy ? ExitCodes.Ok : ExitCodes.Failed;
        }
    }

    /// <summary>
    /// Parsed options. Strict on purpose: a typo must not silently fall back to the live
    /// installation, so any unknown option or missing value is a usage error.
    /// </summary>
    internal sealed class DiagOptions
    {
        public bool Json;
        public string InstallDir;
        public string UpdaterDir;
        public string Error = string.Empty;

        public bool Parse(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--json":
                        Json = true;
                        break;

                    case "--dir":
                        if (!TryTakeValue(args, ref i, arg, out InstallDir)) return false;
                        break;

                    case "--updater":
                        if (!TryTakeValue(args, ref i, arg, out UpdaterDir)) return false;
                        break;

                    default:
                        Error = "unknown option: " + arg;
                        return false;
                }
            }

            return true;
        }

        bool TryTakeValue(string[] args, ref int index, string name, out string value)
        {
            value = null;
            int next = index + 1;
            if (next >= args.Length)
            {
                Error = name + " needs a value";
                return false;
            }

            string candidate = args[next];
            if (candidate.StartsWith("--", StringComparison.Ordinal))
            {
                Error = name + " needs a value, got the option " + candidate;
                return false;
            }

            value = candidate;
            index = next;
            return true;
        }
    }
}
