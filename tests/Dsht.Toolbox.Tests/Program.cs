using System;
using System.Collections.Generic;
using System.Text.Json;
using Dsht.Toolbox.Core;
using Dsht.Toolbox.Core.Diag;

namespace Dsht.Toolbox.Tests
{
    /// <summary>
    /// Dependency-free assertion runner. Every check is named for the behaviour it protects, and
    /// JSON checks parse the document instead of searching it for substrings.
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
            SurfaceTests();

            Console.WriteLine();
            Console.WriteLine("== " + passed + " passed, " + failed + " failed ==");
            return failed == 0 ? 0 : 1;
        }

        static void VersionTests()
        {
            Console.WriteLine("-- VersionFacts");
            Check("parses a release installer filename without inventing a tag",
                VersionFacts.Parse("deepseek-harness-0.2.2-win-x64.exe") == "0.2.2");
            Check("parses a pre-release installer filename",
                VersionFacts.Parse("deepseek-harness-0.2.2-rc.5-win-x64.exe") == "0.2.2-rc.5");
            Check("parses a bare version", VersionFacts.Parse("0.2.2") == "0.2.2");
            Check("rejects text without a version", VersionFacts.Parse("no-version-here") == null);
            Check("rejects a bare integer", VersionFacts.Parse("v5") == null);
            Check("rc.5 outranks rc.4", VersionFacts.Compare("0.2.2-rc.5", "0.2.2-rc.4") == 1);
            Check("rc.10 outranks rc.9 numerically", VersionFacts.Compare("0.2.2-rc.10", "0.2.2-rc.9") == 1);
            Check("a release outranks its pre-release", VersionFacts.Compare("0.2.2", "0.2.2-rc.5") == 1);
            Check("a numeric pre-release segment outranks an alphanumeric one", VersionFacts.Compare("1.2.3-1", "1.2.3-0a") == 1);
            Check("identical versions compare equal", VersionFacts.Compare("0.2.1-rc.3", "0.2.1-rc.3") == 0);
            Check("unparsable input yields null, not a guess", VersionFacts.Compare("x", "0.2.2") == null);
            Check("assembly normalisation: 0.2.2.0 equals 0.2.2", VersionFacts.Compare("0.2.2.0", "0.2.2") == 0);
            Check("SameCore ignores the pre-release tag", VersionFacts.SameCore("0.2.2-rc.5", "0.2.2.0"));
            Check("SameCore rejects different cores", !VersionFacts.SameCore("0.2.1", "0.2.2"));
            Check("SameCore rejects unparsable input", !VersionFacts.SameCore("x", "0.2.2"));
            Check("a parsed version parses to itself", VersionFacts.Parse(VersionFacts.Parse("0.2.2-rc.5")) == "0.2.2-rc.5");
        }

        static void PlannerTests()
        {
            Console.WriteLine("-- RemedyPlanner (diagnosis only)");

            Check("an unreadable install directory blocks and asks for elevation",
                HasFinding(RemedyPlanner.Plan(new DiagFacts { InstallDirPresence = Presence.Unreadable }), "install-unreadable", FindingLevel.Block));
            Check("an absent install directory blocks",
                HasFinding(RemedyPlanner.Plan(new DiagFacts { InstallDirPresence = Presence.Absent }), "install-missing", FindingLevel.Block));

            DiagPlan healthy = RemedyPlanner.Plan(Healthy());
            Check("a healthy install is healthy", healthy.Healthy);
            Check("a healthy install needs no attention", !healthy.NeedsAttention);
            Check("a healthy install still reports the main executable", HasFinding(healthy, "main-exe", FindingLevel.Ok));

            DiagFacts running = Healthy();
            running.Holders.Add(new HolderFact { Pid = 11, InsideInstallDir = true });
            DiagPlan runningPlan = RemedyPlanner.Plan(running);
            Check("a running app is informational, not a problem", HasFinding(runningPlan, "app-running", FindingLevel.Info));
            Check("a running app alone keeps the install healthy", runningPlan.Healthy);

            DiagFacts noExe = Healthy();
            noExe.MainExePresence = Presence.Absent;
            Check("a missing main executable blocks", HasFinding(RemedyPlanner.Plan(noExe), "main-exe-missing", FindingLevel.Block));

            DiagFacts noUninstaller = Healthy();
            noUninstaller.UninstallerPresence = Presence.Absent;
            DiagPlan noUninstallerPlan = RemedyPlanner.Plan(noUninstaller);
            Check("a missing uninstaller warns", HasFinding(noUninstallerPlan, "uninstaller-missing", FindingLevel.Warn));
            Check("a missing uninstaller is not a blocker", !HasFinding(noUninstallerPlan, "uninstaller-missing", FindingLevel.Block));
            Check("a warning names a manual remedy", AdviceOf(noUninstallerPlan, "uninstaller-missing").Length > 0);

            DiagFacts gaps = Healthy();
            gaps.PayloadGaps.Add("resources");
            Check("payload gaps are reported", HasFinding(RemedyPlanner.Plan(gaps), "payload-gaps", FindingLevel.Warn));

            DiagFacts unknownProcesses = Healthy();
            unknownProcesses.ProcessEnumerationFailed = true;
            Check("an unenumerable process table is reported as unknown",
                HasFinding(RemedyPlanner.Plan(unknownProcesses), "holders-unknown", FindingLevel.Info));

            DiagFacts staged = Healthy();
            staged.Updater = UpdaterState.Pending;
            staged.PendingExeName = "deepseek-harness-0.2.3-win-x64.exe";
            staged.PendingVersion = "0.2.3";
            staged.PendingSha512 = "abc";
            staged.ProductVersion = "0.2.2";
            DiagPlan stagedPlan = RemedyPlanner.Plan(staged);
            Check("a newer staged installer is reported, not acted on", HasFinding(stagedPlan, "updater-newer", FindingLevel.Info));
            Check("a newer staged installer keeps the install healthy", stagedPlan.Healthy);

            DiagFacts oldStaged = Healthy();
            oldStaged.Updater = UpdaterState.Pending;
            oldStaged.PendingVersion = "0.2.1";
            oldStaged.ProductVersion = "0.2.2";
            Check("an older staged installer is not treated as a fault",
                HasFinding(RemedyPlanner.Plan(oldStaged), "updater-staged", FindingLevel.Ok));

            DiagFacts ambiguous = Healthy();
            ambiguous.Updater = UpdaterState.Ambiguous;
            Check("several staged installers warn", HasFinding(RemedyPlanner.Plan(ambiguous), "updater-ambiguous", FindingLevel.Warn));

            DiagFacts unverified = Healthy();
            unverified.Updater = UpdaterState.PendingUnverified;
            unverified.PendingVersion = "0.2.3";
            unverified.ProductVersion = "0.2.2";
            Check("an unverified staged package is still only a report",
                HasFinding(RemedyPlanner.Plan(unverified), "updater-unreadable", FindingLevel.Info) ||
                HasFinding(RemedyPlanner.Plan(unverified), "updater-newer", FindingLevel.Info));

            Check("null facts produce a blocker instead of a crash",
                HasFinding(RemedyPlanner.Plan(null), "no-facts", FindingLevel.Block));
        }

        static void RendererTests()
        {
            Console.WriteLine("-- ReportRenderer");

            DiagFacts facts = Healthy();
            facts.Holders.Add(new HolderFact { Pid = 11, InsideInstallDir = true });
            DiagPlan plan = RemedyPlanner.Plan(facts);

            using (JsonDocument document = JsonDocument.Parse(ReportRenderer.Json(facts, plan)))
            {
                JsonElement root = document.RootElement;
                Check("json is parseable", root.ValueKind == JsonValueKind.Object);
                Check("json reports health as a boolean", root.GetProperty("healthy").ValueKind == JsonValueKind.True);
                Check("json reports needsAttention as a boolean", root.GetProperty("needsAttention").ValueKind == JsonValueKind.False);
                Check("json carries the install dir", root.GetProperty("installDir").GetString() == facts.InstallDir);
                Check("json findings include the app-running code", HasJsonCode(root, "app-running"));
                Check("json findings carry advice", FindJsonAdvice(root, "app-running").Length > 0);
            }

            using (JsonDocument document = JsonDocument.Parse(ReportRenderer.Json(new DiagFacts(), RemedyPlanner.Plan(new DiagFacts()))))
            {
                JsonElement root = document.RootElement;
                Check("a null version serialises as json null", root.GetProperty("productVersion").ValueKind == JsonValueKind.Null);
                Check("an unreadable install reports the presence enum", root.GetProperty("installDirPresence").GetString() == "Absent");
            }

            var tricky = new DiagFacts { InstallDir = "q\"b" + (char)92 + "c" + (char)10 + "d" };
            using (JsonDocument document = JsonDocument.Parse(ReportRenderer.Json(tricky, RemedyPlanner.Plan(tricky))))
            {
                Check("quotes, backslashes and newlines round-trip",
                    document.RootElement.GetProperty("installDir").GetString() == tricky.InstallDir);
            }

            string human = ReportRenderer.Human(facts, plan);
            Check("text names the app-running finding", human.Contains("app-running"));
            Check("text prints an advice line", human.Contains("-> "));
            Check("text says the app is not a problem", human.Contains("Normal."));
            Check("text never suggests a repair flag", !human.Contains("--fix"));

            string blocked = ReportRenderer.Human(new DiagFacts { InstallDirPresence = Presence.Absent },
                RemedyPlanner.Plan(new DiagFacts { InstallDirPresence = Presence.Absent }));
            Check("a blocker renders as blocked", blocked.Contains("verdict: blocked"));

            string clean = ReportRenderer.Human(Healthy(), RemedyPlanner.Plan(Healthy()));
            Check("a clean install renders as clean", clean.Contains("verdict: no problems detected"));
        }

        static void SurfaceTests()
        {
            Console.WriteLine("-- command surface");
            string[] commands = CommandSurface.Commands;
            Check("the surface stays within the cap", commands.Length <= CommandSurface.MaxCommands);
            Check("the cap is the documented eight", CommandSurface.MaxCommands == 8);
            Check("command names are unique", new HashSet<string>(commands).Count == commands.Length);
            Check("diag is known", CommandSurface.IsKnown("diag"));
            Check("port is known", CommandSurface.IsKnown("port"));
            Check("an invented command is rejected", !CommandSurface.IsKnown("deploy"));
            Check("repair is reserved, not implemented", CommandSurface.IsKnown("repair"));
        }

        static DiagFacts Healthy()
        {
            return new DiagFacts
            {
                InstallDir = "C:" + (char)92 + "Apps" + (char)92 + "DeepSeek Harness",
                UpdaterDir = "C:" + (char)92 + "Updater",
                InstallDirPresence = Presence.Present,
                MainExePresence = Presence.Present,
                UninstallerPresence = Presence.Present,
                AppVersion = "0.2.2-rc.5",
                ProductVersion = "0.2.2.0",
                Updater = UpdaterState.Clean,
            };
        }

        static bool HasFinding(DiagPlan plan, string code, FindingLevel level)
        {
            foreach (Finding finding in plan.Findings)
                if (finding.Code == code && finding.Level == level) return true;
            return false;
        }

        static string AdviceOf(DiagPlan plan, string code)
        {
            foreach (Finding finding in plan.Findings)
                if (finding.Code == code) return finding.Advice;
            return string.Empty;
        }

        static bool HasJsonCode(JsonElement root, string code)
        {
            foreach (JsonElement finding in root.GetProperty("findings").EnumerateArray())
                if (finding.GetProperty("code").GetString() == code) return true;
            return false;
        }

        static string FindJsonAdvice(JsonElement root, string code)
        {
            foreach (JsonElement finding in root.GetProperty("findings").EnumerateArray())
                if (finding.GetProperty("code").GetString() == code) return finding.GetProperty("advice").GetString();
            return string.Empty;
        }
    }
}
