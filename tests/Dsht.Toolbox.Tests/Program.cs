using System;
using System.Collections.Generic;
using System.IO;
using Dsht.Toolbox.Core;
using Dsht.Toolbox.Core.Diag;
using Dsht.Toolbox.Core.Remedy;

namespace Dsht.Toolbox.Tests
{
    /// <summary>
    /// Dependency-free assertion runner. Each check fails if the corresponding behaviour is
    /// removed, and the names say what breaks when they do.
    /// </summary>
    internal static class Program
    {
        static int passed;
        static int failed;

        static void Check(string name, bool ok)
        {
            if (ok) { passed++; Console.WriteLine("  [PASS] " + name); }
            else { failed++; Console.WriteLine("  [FAIL] " + name); }
        }

        static int Main()
        {
            Console.WriteLine("== dsht tests ==");

            VersionTests();
            PlannerTests();
            RendererTests();
            RemedySessionTests();
            SurfaceTests();

            Console.WriteLine();
            Console.WriteLine("== " + passed + " passed, " + failed + " failed ==");
            return failed == 0 ? 0 : 1;
        }

        static void VersionTests()
        {
            Console.WriteLine("-- VersionFacts");
            Check("version parsed out of an installer file name", VersionFacts.Parse("deepseek-harness-0.2.2-rc.5-win-x64.exe") == "0.2.2-rc.5");
            Check("plain version", VersionFacts.Parse("0.2.2") == "0.2.2");
            Check("text without a version yields null", VersionFacts.Parse("no-version-here") == null);
            Check("a bare integer is not a version", VersionFacts.Parse("v5") == null);
            Check("rc.5 outranks rc.4", VersionFacts.Compare("0.2.2-rc.5", "0.2.2-rc.4") == 1);
            Check("rc.10 outranks rc.9 (numeric, not lexical)", VersionFacts.Compare("0.2.2-rc.10", "0.2.2-rc.9") == 1);
            Check("a release outranks its pre-release", VersionFacts.Compare("0.2.2", "0.2.2-rc.5") == 1);
            Check("identical versions compare equal", VersionFacts.Compare("0.2.1-rc.3", "0.2.1-rc.3") == 0);
            Check("unparsable input yields null instead of a guess", VersionFacts.Compare("x", "0.2.2") == null);
            Check("assembly normalisation: 0.2.2.0 equals 0.2.2", VersionFacts.Compare("0.2.2.0", "0.2.2") == 0);
            Check("SameCore ignores the pre-release tag", VersionFacts.SameCore("0.2.2-rc.5", "0.2.2.0"));
            Check("SameCore rejects different cores", !VersionFacts.SameCore("0.2.1", "0.2.2"));
        }

        static void PlannerTests()
        {
            Console.WriteLine("-- RemedyPlanner");

            var missing = new DiagFacts { InstallDirExists = false };
            DiagPlan missingPlan = RemedyPlanner.Plan(missing);
            Check("missing install blocks", HasFinding(missingPlan, "install-missing", FindingLevel.Block));
            Check("missing install is not healthy", !missingPlan.Healthy);
            Check("missing install recommends reinstall", HasAction(missingPlan, ActionKind.Reinstall));

            DiagFacts held = Healthy();
            held.Holders.Add(new HolderFact { Pid = 11, Name = "DeepSeek Harness", Path = held.InstallDir + "\\DeepSeek Harness.exe", InsideInstallDir = true });
            held.Holders.Add(new HolderFact { Pid = 12, Name = "node", Path = held.InstallDir + "\\resources\\runtime\\node.exe", InsideInstallDir = true, IsBackend = true });
            DiagPlan heldPlan = RemedyPlanner.Plan(held);
            Check("holders are reported as a warning", HasFinding(heldPlan, "holders", FindingLevel.Warn));
            Check("closing holders is marked dangerous", HasAction(heldPlan, ActionKind.StopHolders) && IsDangerous(heldPlan, ActionKind.StopHolders));
            Check("every install-dir holder is targeted", PidsOf(heldPlan, ActionKind.StopHolders).Count == 2);
            Check("a warning never counts as healthy", !heldPlan.Healthy);

            DiagFacts sameVersion = Healthy();
            sameVersion.Updater = UpdaterState.PendingKnown;
            sameVersion.PendingVersion = "0.2.2-rc.5";
            sameVersion.ProductVersion = "0.2.2-rc.5";
            Check("a duplicate staging is reported as equal", HasFinding(RemedyPlanner.Plan(sameVersion), "version-equal", FindingLevel.Info));

            DiagFacts newer = Healthy();
            newer.Updater = UpdaterState.PendingKnown;
            newer.PendingVersion = "0.2.3";
            newer.ProductVersion = "0.2.2";
            DiagPlan newerPlan = RemedyPlanner.Plan(newer);
            Check("a newer staging is reported", HasFinding(newerPlan, "updater-pending", FindingLevel.Warn));
            Check("a staged update is cleared before anything else", HasAction(newerPlan, ActionKind.ClearUpdaterPending));

            DiagFacts ambiguous = Healthy();
            ambiguous.Updater = UpdaterState.PendingKnown;
            ambiguous.PendingVersion = "0.2.2-rc.5";
            ambiguous.ProductVersion = "0.2.2.0";
            DiagPlan ambiguousPlan = RemedyPlanner.Plan(ambiguous);
            Check("same core but unprovable ordering is reported as such", HasFinding(ambiguousPlan, "version-same-core", FindingLevel.Info));
            Check("no ordering is invented for an ambiguous version", !HasFinding(ambiguousPlan, "version-older", FindingLevel.Info));

            DiagFacts suspicious = Healthy();
            suspicious.Updater = UpdaterState.Suspicious;
            Check("leftover updater artefacts are cleared", HasAction(RemedyPlanner.Plan(suspicious), ActionKind.ClearUpdaterPending));

            DiagPlan clean = RemedyPlanner.Plan(Healthy());
            Check("a clean install is healthy", clean.Healthy);
            Check("a clean install plans nothing", clean.Actions.Count == 1 && clean.Actions[0].Kind == ActionKind.Nothing);

            DiagPlan nullFacts = RemedyPlanner.Plan(null);
            Check("null facts produce a blocker instead of a crash", HasFinding(nullFacts, "no-facts", FindingLevel.Block));
        }

        static void RendererTests()
        {
            Console.WriteLine("-- ReportRenderer");
            DiagFacts held = Healthy();
            held.Holders.Add(new HolderFact { Pid = 11, InsideInstallDir = true });
            DiagPlan plan = RemedyPlanner.Plan(held);

            string human = ReportRenderer.Human(held, plan);
            Check("text names the holder finding", human.Contains("holders"));
            Check("text marks the consent requirement", human.Contains("--yes"));
            Check("a warning renders as 'to repair', not as 'blocked'", human.Contains("to repair") && !human.Contains("blocked"));
            Check("a clean install renders as clean", ReportRenderer.Human(Healthy(), RemedyPlanner.Plan(Healthy())).Contains("verdict: clean"));

            string json = ReportRenderer.Json(held, plan);
            Check("json is a single object", json.StartsWith("{") && json.EndsWith("}"));
            Check("json carries healthy", json.Contains("\"healthy\":"));
            Check("json carries stable finding codes", json.Contains("\"code\":\"holders\""));

            var quotes = new DiagFacts { InstallDir = "a\"b\\c" };
            Check("json escapes quotes and backslashes", ReportRenderer.Json(quotes, RemedyPlanner.Plan(quotes)).Contains("\\\"") );
        }

        static void RemedySessionTests()
        {
            Console.WriteLine("-- RemedySession");

            DiagFacts before = Healthy();
            before.Holders.Add(new HolderFact { Pid = 1, InsideInstallDir = true });
            before.Updater = UpdaterState.Suspicious;
            DiagPlan plan = RemedyPlanner.Plan(before);

            var recorder = new FakeRecorder();
            RemedyReport report = RemedySession.Run(before, plan, recorder, delegate { return Healthy(); }, TextWriter.Null);

            Check("steps run in planner order: holders before staging",
                recorder.Calls.Count >= 2 && recorder.Calls[0].StartsWith("stop-holders") && recorder.Calls[1] == "clear-updater-pending");
            Check("a clean re-check reports done", report.Outcome == RemedyOutcome.Done);
            Check("executed steps are counted", report.StepsExecuted == recorder.Calls.Count);

            var failing = new FakeRecorder();
            failing.Failing.Add("stop-holders");
            RemedyReport failedReport = RemedySession.Run(before, plan, failing, delegate { return Healthy(); }, TextWriter.Null);
            Check("first failure stops the run", failedReport.Outcome == RemedyOutcome.Failed);
            Check("failure is attributed to the step", failedReport.FailedStep == "stop-holders");
            Check("no step runs after a failure", failing.Calls.Count == 1);

            var optimistic = new FakeRecorder();
            RemedyReport unverified = RemedySession.Run(before, plan, optimistic, delegate { return before; }, TextWriter.Null);
            Check("outcome follows the re-check, not optimism", unverified.Outcome == RemedyOutcome.Failed);

            var idle = new FakeRecorder();
            RemedyReport nothing = RemedySession.Run(Healthy(), RemedyPlanner.Plan(Healthy()), idle, delegate { return Healthy(); }, TextWriter.Null);
            Check("nothing-to-do executes no steps", idle.Calls.Count == 0 && nothing.Outcome == RemedyOutcome.Done);
        }

        static void SurfaceTests()
        {
            Console.WriteLine("-- command surface");
            string[] commands = CommandSurface.Commands;
            Check("the command surface stays within the cap", commands.Length <= CommandSurface.MaxCommands);
            Check("command names are unique", new HashSet<string>(commands).Count == commands.Length);
            Check("diag is present", Array.IndexOf(commands, "diag") >= 0);
            Check("port is present", Array.IndexOf(commands, "port") >= 0);
        }

        static DiagFacts Healthy()
        {
            return new DiagFacts
            {
                InstallDir = "C:\\Apps\\DeepSeek Harness",
                UpdaterDir = "C:\\Updater",
                InstallDirExists = true,
                MainExeExists = true,
                UninstallerExists = true,
                ProductVersion = "0.2.2-rc.5",
                Updater = UpdaterState.Clean,
            };
        }

        static bool HasFinding(DiagPlan plan, string code, FindingLevel level)
        {
            foreach (Finding finding in plan.Findings)
                if (finding.Code == code && finding.Level == level) return true;
            return false;
        }

        static bool HasAction(DiagPlan plan, ActionKind kind)
        {
            foreach (RemedyAction action in plan.Actions)
                if (action.Kind == kind) return true;
            return false;
        }

        static bool IsDangerous(DiagPlan plan, ActionKind kind)
        {
            foreach (RemedyAction action in plan.Actions)
                if (action.Kind == kind) return action.Dangerous;
            return false;
        }

        static List<int> PidsOf(DiagPlan plan, ActionKind kind)
        {
            foreach (RemedyAction action in plan.Actions)
                if (action.Kind == kind) return action.Pids;
            return new List<int>();
        }
    }
}