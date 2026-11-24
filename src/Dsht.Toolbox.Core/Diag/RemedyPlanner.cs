using System.Collections.Generic;

namespace Dsht.Toolbox.Core.Diag
{
    public enum FindingLevel { Ok, Info, Warn, Block }

    /// <summary>One observation about the install. <see cref="Code"/> is stable and asserted by tests.</summary>
    public sealed class Finding
    {
        public FindingLevel Level;
        public string Code = string.Empty;
        public string Title = string.Empty;
        public string Detail = string.Empty;
    }

    public enum ActionKind
    {
        /// <summary>Close every process holding the install directory.</summary>
        StopHolders = 0,

        /// <summary>Delete staged updater artefacts so the next update starts clean.</summary>
        ClearUpdaterPending = 1,

        /// <summary>Re-run the staged installer with %TEMP% as its working directory.</summary>
        RunInstallerFromTemp = 2,

        /// <summary>Nothing can be repaired in place; a reinstall is required.</summary>
        Reinstall = 3,

        /// <summary>No action needed.</summary>
        Nothing = 4,
    }

    /// <summary>A step the planner recommends. Ordered; <see cref="Dangerous"/> needs explicit consent.</summary>
    public sealed class RemedyAction
    {
        public ActionKind Kind;
        public string Why = string.Empty;
        public bool Dangerous;
        public List<int> Pids = new List<int>();
        public string ExeName = string.Empty;
    }

    public sealed class DiagPlan
    {
        public List<Finding> Findings = new List<Finding>();
        public List<RemedyAction> Actions = new List<RemedyAction>();

        /// <summary>True when nothing needs doing: no <see cref="FindingLevel.Block"/> and no
        /// <see cref="FindingLevel.Warn"/>. Warnings must not be reported as healthy.</summary>
        public bool Healthy;
    }

    /// <summary>
    /// Turns facts into findings and an ordered remedy plan. Pure: same input, same output,
    /// no IO and no clock, so every branch below is directly testable.
    /// </summary>
    public static class RemedyPlanner
    {
        public static DiagPlan Plan(DiagFacts facts)
        {
            var plan = new DiagPlan();
            if (facts == null)
            {
                Add(plan, FindingLevel.Block, "no-facts", "No facts were collected", "Re-run the command; if it persists, run it elevated.");
                return plan;
            }

            if (!facts.InstallDirExists)
            {
                Add(plan, FindingLevel.Block, "install-missing", "Install directory does not exist", facts.InstallDir);
                plan.Actions.Add(new RemedyAction { Kind = ActionKind.Reinstall, Why = "nothing to repair in place" });
                return plan;
            }

            Add(plan, facts.MainExeExists ? FindingLevel.Ok : FindingLevel.Block,
                facts.MainExeExists ? "main-exe" : "main-exe-missing",
                facts.MainExeExists ? "Main executable present" : "Main executable missing",
                facts.MainExeExists ? string.Empty : "DeepSeek Harness.exe is not in the install directory");

            if (!facts.UninstallerExists)
                Add(plan, FindingLevel.Warn, "uninstaller-missing", "Uninstaller missing", "Usually a sign of a partially applied overwrite install.");

            PlanHolders(plan, facts);
            PlanUpdater(plan, facts);
            PlanVersionSkew(plan, facts);

            plan.Healthy = HasNoProblems(plan);
            if (plan.Healthy && plan.Actions.Count == 0)
                plan.Actions.Add(new RemedyAction { Kind = ActionKind.Nothing, Why = "nothing to repair" });

            return plan;
        }

        static void PlanHolders(DiagPlan plan, DiagFacts facts)
        {
            var pids = new List<int>();
            int backend = 0;
            foreach (HolderFact holder in facts.Holders)
            {
                if (!holder.InsideInstallDir) continue;
                pids.Add(holder.Pid);
                if (holder.IsBackend) backend++;
            }

            if (pids.Count > 0)
            {
                string detail = "This is the direct cause of the Windows error 32/5 seen when updating.";
                if (backend > 0) detail += " " + backend + " of them is the bundled backend runtime, which holds node.exe open.";
                Add(plan, FindingLevel.Warn, "holders", pids.Count + " process(es) hold the install directory", detail);
                plan.Actions.Add(new RemedyAction
                {
                    Kind = ActionKind.StopHolders,
                    Why = "release file handles on the install directory",
                    Dangerous = true,
                    Pids = pids,
                });
            }
            else
            {
                Add(plan, FindingLevel.Ok, "no-holders", "No process holds the install directory", string.Empty);
            }

            if (facts.UnreadableProcesses > 0)
                Add(plan, FindingLevel.Info, "unreadable-procs",
                    facts.UnreadableProcesses + " process path(s) could not be read",
                    "Access denied. Absence of evidence, not evidence of absence; an elevated run sees more.");
        }

        static void PlanUpdater(DiagPlan plan, DiagFacts facts)
        {
            switch (facts.Updater)
            {
                case UpdaterState.None:
                    Add(plan, FindingLevel.Info, "updater-none", "No updater cache", "No update was ever staged on this machine.");
                    return;

                case UpdaterState.Clean:
                    Add(plan, FindingLevel.Ok, "updater-clean", "Updater cache is clean", string.Empty);
                    return;

                case UpdaterState.Suspicious:
                    Add(plan, FindingLevel.Warn, "updater-suspicious", "Updater cache holds leftover artefacts",
                        "An incomplete staging area or a stray installer in the cache root makes the next update fail repeatedly.");
                    plan.Actions.Add(new RemedyAction { Kind = ActionKind.ClearUpdaterPending, Why = "remove the incomplete staging area" });
                    return;
            }

            // PendingKnown / PendingUnknown
            string detail = "Staged: " + (string.IsNullOrEmpty(facts.PendingExeName) ? "(unknown)" : facts.PendingExeName);
            int? order = VersionFacts.Compare(facts.PendingVersion, facts.ProductVersion);
            bool sameCore = VersionFacts.SameCore(facts.PendingVersion, facts.ProductVersion);

            if (order.HasValue && order.Value > 0)
                detail += "; newer than installed (installed " + Describe(facts.ProductVersion) + ", staged " + facts.PendingVersion + ")";
            else if (order.HasValue && order.Value == 0)
                detail += "; same version as installed (" + Describe(facts.ProductVersion) + ") - a duplicate";
            else if (sameCore)
                detail += "; same version core as installed (" + Describe(facts.ProductVersion) + ") - the installed side exposes only a numeric version, so no ordering is claimed";
            else if (!order.HasValue)
                detail += "; version not comparable (installed " + Describe(facts.ProductVersion) + ")";
            else
                detail += "; older than installed - leftover";

            Add(plan, FindingLevel.Warn, "updater-pending", "Updater has a staged installer", detail);
            plan.Actions.Add(new RemedyAction { Kind = ActionKind.ClearUpdaterPending, Why = "a stale staging area makes every following update fail" });

            if (facts.InstallerInRoot)
            {
                plan.Actions.Add(new RemedyAction
                {
                    Kind = ActionKind.RunInstallerFromTemp,
                    Why = "run the staged installer with %TEMP% as working directory; the installer renames its own working directory otherwise",
                    Dangerous = true,
                    ExeName = facts.PendingExeName ?? string.Empty,
                });
            }
        }

        static void PlanVersionSkew(DiagPlan plan, DiagFacts facts)
        {
            if (string.IsNullOrEmpty(facts.ProductVersion) || string.IsNullOrEmpty(facts.PendingVersion)) return;

            int? order = VersionFacts.Compare(facts.PendingVersion, facts.ProductVersion);
            if (order.HasValue && order.Value == 0)
            {
                Add(plan, FindingLevel.Info, "version-equal", "Staged version equals installed version",
                    "The staged update is a duplicate; clearing it is safe.");
                return;
            }

            if (VersionFacts.SameCore(facts.PendingVersion, facts.ProductVersion))
            {
                Add(plan, FindingLevel.Info, "version-same-core", "Staged and installed share a version core",
                    "The installed side only exposes " + facts.ProductVersion + ", so its pre-release tag is unknown; no ordering is claimed and clearing is safe.");
                return;
            }

            if (order.HasValue && order.Value < 0)
            {
                Add(plan, FindingLevel.Info, "version-older", "Staged version is older than installed",
                    "A stale leftover; clearing it prevents it from being retried.");
            }
        }

        static bool HasNoProblems(DiagPlan plan)
        {
            foreach (Finding finding in plan.Findings)
                if (finding.Level == FindingLevel.Block || finding.Level == FindingLevel.Warn) return false;
            return true;
        }

        static string Describe(string version)
        {
            return string.IsNullOrEmpty(version) ? "(unknown)" : version;
        }

        static void Add(DiagPlan plan, FindingLevel level, string code, string title, string detail)
        {
            plan.Findings.Add(new Finding { Level = level, Code = code, Title = title, Detail = detail ?? string.Empty });
        }
    }
}