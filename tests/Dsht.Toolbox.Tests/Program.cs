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
    /// removed, and the name states what breaks when it does.
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
            Check("rc.10 outranks rc.9 because segments compare numerically", VersionFacts.Compare("0.2.2-rc.10", "0.2.2-rc.9") == 1);
            Check("a release outranks its pre-release", VersionFacts.Compare("0.2.2", "0.2.2-rc.5") == 1);
            Check("identical versions compare equal", VersionFacts.Compare("0.2.1-rc.3", "0.2.1-rc.3") == 0);
            Check("unparsable input yields null instead of a guess", VersionFacts.Compare("x", "0.2.2") == null);
            Check("assembly normalisation: 0.2.2.0 equals 0.2.2", VersionFacts.Compare("0.2.2.0", "0.2.2") == 0);
            Check("a pre-release survives four-part normalisation", VersionFacts.Parse("0.2.2.0-rc.5") == "0.2.2-rc.5");
            Check("SameCore ignores the pre-release tag", VersionFacts.SameCore("0.2.2-rc.5", "0.2.2.0"));
            Check("SameCore rejects different cores", !VersionFacts.SameCore("0.2.1", "0.2.2"));
            Check("SameCore rejects unparsable input", !VersionFacts.SameCore("x", "0.2.2"));
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
            held.Holders.Add(new HolderFact { Pid = 11, Name = "DeepSeek Harness", InsideInstallDir = true });
            held.Holders.Add(new HolderFact { Pid = 12, Name = "node", InsideInstallDir = true, IsBackend = true });
            DiagPlan heldPlan = RemedyPlanner.Plan(held);
            Check("holders are reported as a warning", HasFinding(heldPlan, "holders", FindingLevel.Warn));
            Check("closing holders is marked dangerous", IsDangerous(heldPlan, ActionKind.StopHolders));
            Check("every install-dir holder is targeted", PidsOf(heldPlan, ActionKind.StopHolders).Count == 2);
            Check("a warning never counts as healthy", !heldPlan.Healthy);

            var outside = Healthy();
            outside.Holders.Add(new HolderFact { Pid = 21, Name = "DeepSeek Harness", InsideInstallDir = false });
            Check("a same-named process outside the install dir is not targeted", PidsOf(RemedyPlanner.Plan(outside), ActionKind.StopHolders).Count == 0);

            var opaque = Healthy();
            opaque.UnreadableProcesses = 3;
            Check("unreadable process paths are reported", HasFinding(RemedyPlanner.Plan(opaque), "unreadable-procs", FindingLevel.Info));
            Check("unreadable process paths do not by themselves block", RemedyPlanner.Plan(opaque).Healthy);

            var noExe = Healthy();
            noExe.MainExeExists = false;
            Check("a missing main executable blocks", HasFinding(RemedyPlanner.Plan(noExe), "main-exe-missing", FindingLevel.Block));

            var noUninstaller = Healthy();
            noUninstaller.UninstallerExists = false;
            DiagPlan noUninstallerPlan = RemedyPlanner.Plan(noUninstaller);
            Check("a missing uninstaller only warns", HasFinding(noUninstallerPlan, "uninstaller-missing", FindingLevel.Warn));
            Check("a missing uninstaller is not a blocker", !HasFinding(noUninstallerPlan, "uninstaller-missing", FindingLevel.Block));

            var sameVersion = Healthy();
            sameVersion.Updater = UpdaterState.PendingKnown;
            sameVersion.PendingVersion = "0.2.2-rc.5";
            sameVersion.ProductVersion = "0.2.2-rc.5";
            Check("a duplicate staging is reported as equal", HasFinding(RemedyPlanner.Plan(sameVersion), "version-equal", FindingLevel.Info));

            var newer = Healthy();
            newer.Updater = UpdaterState.PendingKnown;
            newer.PendingVersion = "0.2.3";
            newer.ProductVersion = "0.2.2";
            newer.PendingExeName = "deepseek-harness-0.2.3-win-x64.exe";
            newer.InstallerInRoot = true;
            DiagPlan newerPlan = RemedyPlanner.Plan(newer);
            Check("a staged update is reported", HasFinding(newerPlan, "updater-pending", FindingLevel.Warn));
            Check("a stale staging area is cleared", HasAction(newerPlan, ActionKind.ClearUpdaterPending));
            Check("a stray cache-root installer adds the re-run step", HasAction(newerPlan, ActionKind.RunInstallerFromTemp));
            Check("re-running the installer requires consent", IsDangerous(newerPlan, ActionKind.RunInstallerFromTemp));

            var ambiguous = Healthy();
            ambiguous.Updater = UpdaterState.PendingKnown;
            ambiguous.PendingVersion = "0.2.2-rc.5";
            ambiguous.ProductVersion = "0.2.2.0";
            DiagPlan ambiguousPlan = RemedyPlanner.Plan(ambiguous);
            Check("an unprovable ordering is reported as same-core", HasFinding(ambiguousPlan, "version-same-core", FindingLevel.Info));
            Check("no ordering is invented for an ambiguous version", !HasFinding(ambiguousPlan, "version-older", FindingLevel.Info));

            var pendingUnknown = Healthy();
            pendingUnknown.Updater = UpdaterState.PendingUnknown;
            Check("a staged installer without a version still plans a clear", HasAction(RemedyPlanner.Plan(pendingUnknown), ActionKind.ClearUpdaterPending));

            var suspicious = Healthy();
            suspicious.Updater = UpdaterState.Suspicious;
            Check("leftover updater artefacts are cleared", HasAction(RemedyPlanner.Plan(suspicious), ActionKind.ClearUpdaterPending));

            DiagPlan clean = RemedyPlanner.Plan(Healthy());
            Check("a clean install is healthy", clean.Healthy);
            Check("a clean install plans nothing", clean.Actions.Count == 1 && clean.Actions[0].Kind == ActionKind.Nothing);

            Check("null facts produce a blocker instead of a crash", HasFinding(RemedyPlanner.Plan(null), "no-facts", FindingLevel.Block));
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
            Check("a warning renders as to-repair, not as blocked", human.Contains("to repair") && !human.Contains("blocked"));

            string blocked = ReportRenderer.Human(new DiagFacts { InstallDirExists = false }, RemedyPlanner.Plan(new DiagFacts { InstallDirExists = false }));
            Check("a blocker renders as blocked", blocked.Contains("verdict: blocked"));

            string clean = ReportRenderer.Human(Healthy(), RemedyPlanner.Plan(Healthy()));
            Check("a clean install renders as clean", clean.Contains("verdict: clean"));

            string unknownVersion = ReportRenderer.Human(new DiagFacts { InstallDirExists = true, MainExeExists = true },
                RemedyPlanner.Plan(new DiagFacts { InstallDirExists = true, MainExeExists = true }));
            Check("an unknown version is printed as unknown, never as a number", unknownVersion.Contains("(unknown)"));

            string json = ReportRenderer.Json(held, plan);
            Check("json is a single object", json.StartsWith("{") && json.EndsWith("}"));
            Check("json carries healthy", json.Contains("healthy"));
            Check("json carries stable finding codes", json.Contains("holders"));

            string emptyJson = ReportRenderer.Json(new DiagFacts(), RemedyPlanner.Plan(new DiagFacts()));
            Check("json renders a missing version as null", emptyJson.Contains("productVersion") && emptyJson.Contains("null"));

            var quoted = new DiagFacts { InstallDir = "a" + (char)34 + "b" + (char)92 + "c" };
            string escaped = ReportRenderer.Json(quoted, RemedyPlanner.Plan(quoted));
            Check("json escapes embedded quotes and backslashes", escaped.Contains((char)92 + "" + (char)34) && escaped.Contains((char)92 + "" + (char)92));
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
            Check("the first failure stops the run", failedReport.Outcome == RemedyOutcome.Failed);
            Check("the failure is attributed to the step", failedReport.FailedStep == "stop-holders");
            Check("no step runs after a failure", failing.Calls.Count == 1);

            var optimistic = new FakeRecorder();
            RemedyReport unverified = RemedySession.Run(before, plan, optimistic, delegate { return before; }, TextWriter.Null);
            Check("the outcome follows the re-check, not the steps", unverified.Outcome == RemedyOutcome.Failed);

            var idle = new FakeRecorder();
            RemedyReport nothing = RemedySession.Run(Healthy(), RemedyPlanner.Plan(Healthy()), idle, delegate { return Healthy(); }, TextWriter.Null);
            Check("nothing-to-do executes no steps", idle.Calls.Count == 0 && nothing.Outcome == RemedyOutcome.Done);

            var noRecheck = new FakeRecorder();
            RemedyReport withoutRecheck = RemedySession.Run(before, plan, noRecheck, null, TextWriter.Null);
            Check("a run without a re-check still reports done", withoutRecheck.Outcome == RemedyOutcome.Done);
            Check("a run without a re-check leaves facts empty", withoutRecheck.After == null);

            var reinstallObstacle = new FakeRecorder();
            var blockedFacts = new DiagFacts { InstallDirExists = false };
            RemedyReport blockedRun = RemedySession.Run(blockedFacts, RemedyPlanner.Plan(blockedFacts), reinstallObstacle, delegate { return Healthy(); }, TextWriter.Null);
            Check("an unrepairable plan reports failure", blockedRun.Outcome == RemedyOutcome.Failed);
            Check("an unrepairable plan attributes the step", blockedRun.FailedStep == "reinstall");

            var log = new StringWriter();
            RemedySession.Run(before, plan, new FakeRecorder(), delegate { return Healthy(); }, log);
            Check("the log names each executed step", log.ToString().Contains("stop-holders"));
            Check("the log reports the re-check", log.ToString().Contains("Re-check:"));
        }

        static void SurfaceTests()
        {
            Console.WriteLine("-- command surface");
            string[] commands = CommandSurface.Commands;
            Check("the command surface stays within the cap", commands.Length <= CommandSurface.MaxCommands);
            Check("the cap is the documented eight", CommandSurface.MaxCommands == 8);
            Check("command names are unique", new HashSet<string>(commands).Count == commands.Length);
            Check("IsKnown accepts a real command", CommandSurface.IsKnown("diag"));
            Check("IsKnown rejects an invented command", !CommandSurface.IsKnown("deploy"));
            Check("diag is present", Array.IndexOf(commands, "diag") >= 0);
            Check("port is present", Array.IndexOf(commands, "port") >= 0);
        }

        static DiagFacts Healthy()
        {
            return new DiagFacts
            {
                InstallDir = "C:" + (char)92 + "Apps" + (char)92 + "DeepSeek Harness",
                UpdaterDir = "C:" + (char)92 + "Updater",
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
