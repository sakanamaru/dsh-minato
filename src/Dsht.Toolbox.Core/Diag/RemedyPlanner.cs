using System.Collections.Generic;

namespace Dsht.Toolbox.Core.Diag
{
    public enum FindingLevel { Ok, Info, Warn, Block }

    /// <summary>
    /// One observation. Advice is text for a human, never an action this tool performs: an
    /// earlier version planned repairs, and review showed the repair could not be proven safe,
    /// so the tool now stops at diagnosis.
    /// </summary>
    public sealed class Finding
    {
        public FindingLevel Level;
        public string Code = string.Empty;
        public string Title = string.Empty;
        public string Detail = string.Empty;
        public string Advice = string.Empty;
    }

    public sealed class DiagPlan
    {
        public List<Finding> Findings = new List<Finding>();

        /// <summary>True when nothing needs attention: no Block, no Warn.</summary>
        public bool Healthy;

        /// <summary>True when some observation needs a human decision.</summary>
        public bool NeedsAttention;
    }

    /// <summary>
    /// Facts to findings. Pure and deterministic; no plan, no side effects.
    ///
    /// Three judgement calls keep the report honest:
    /// - a staged update package is not evidence that an update failed (installers keep a
    ///   downloaded package on purpose), so its presence alone is informational;
    /// - a running application is normal and is reported as such;
    /// - anything that could not be read is reported as unknown, never as absent.
    /// </summary>
    public static class RemedyPlanner
    {
        public const string AdviceRunElevated = "Re-run from an elevated prompt.";
        public const string AdviceReinstall = "Reinstall DeepSeek Harness.";
        public const string AdviceCheckBackend = "Start the app and check for a backend startup error.";

        public static DiagPlan Plan(DiagFacts facts)
        {
            var plan = new DiagPlan();
            if (facts == null)
            {
                Add(plan, FindingLevel.Block, "no-facts", "No facts were collected", string.Empty, AdviceRunElevated);
                return Finish(plan);
            }

            if (facts.InstallDirPresence == Presence.Unreadable)
            {
                Add(plan, FindingLevel.Block, "install-unreadable", "Install directory could not be read",
                    "Access was denied, which is not evidence that it is missing.", AdviceRunElevated);
                return Finish(plan);
            }

            if (facts.InstallDirPresence == Presence.Absent)
            {
                Add(plan, FindingLevel.Block, "install-missing", "Install directory does not exist",
                    facts.InstallDir, AdviceReinstall);
                return Finish(plan);
            }

            PlanPayload(plan, facts);
            PlanHolders(plan, facts);
            PlanUpdater(plan, facts);
            PlanVersions(plan, facts);
            return Finish(plan);
        }

        static void PlanPayload(DiagPlan plan, DiagFacts facts)
        {
            if (facts.MainExePresence == Presence.Unreadable)
                Add(plan, FindingLevel.Block, "main-exe-unreadable", "Main executable could not be read", string.Empty, AdviceRunElevated);
            else if (facts.MainExePresence == Presence.Absent)
                Add(plan, FindingLevel.Block, "main-exe-missing", "Main executable missing",
                    "DeepSeek Harness.exe is not in the install directory.", AdviceReinstall);
            else
                Add(plan, FindingLevel.Ok, "main-exe", "Main executable present", string.Empty, string.Empty);

            if (facts.UninstallerPresence == Presence.Absent)
                Add(plan, FindingLevel.Warn, "uninstaller-missing", "Uninstaller missing",
                    "Usually a sign of a partially applied overwrite install.", AdviceReinstall);

            if (facts.PayloadGaps.Count > 0)
            {
                Add(plan, FindingLevel.Warn, "payload-gaps",
                    facts.PayloadGaps.Count + " payload component(s) missing or unreadable",
                    string.Join(", ", facts.PayloadGaps.ToArray()) + ". A partially applied update looks exactly like this.",
                    AdviceCheckBackend + " " + AdviceReinstall);
            }
        }

        static void PlanHolders(DiagPlan plan, DiagFacts facts)
        {
            if (facts.ProcessEnumerationFailed)
            {
                Add(plan, FindingLevel.Info, "holders-unknown", "Process table could not be enumerated",
                    "Processes running from the install directory are unknown.", AdviceRunElevated);
                return;
            }

            int running = 0;
            foreach (HolderFact holder in facts.Holders)
                if (holder.InsideInstallDir) running++;

            if (running > 0)
            {
                Add(plan, FindingLevel.Info, "app-running", "The app is running (" + running + " process(es))",
                    "Normal. If an update fails with a file-in-use error, quit the app first, including its tray icon.",
                    "Quit DeepSeek Harness from the tray icon, then retry the update.");
            }
            else
            {
                Add(plan, FindingLevel.Info, "app-stopped", "The app is not running", string.Empty, string.Empty);
            }
        }

        static void PlanUpdater(DiagPlan plan, DiagFacts facts)
        {
            switch (facts.Updater)
            {
                case UpdaterState.None:
                    Add(plan, FindingLevel.Info, "updater-none", "No updater cache",
                        "No update has been staged on this machine.", string.Empty);
                    return;

                case UpdaterState.Clean:
                    Add(plan, FindingLevel.Ok, "updater-clean", "Updater cache is clean", string.Empty, string.Empty);
                    return;

                case UpdaterState.Unreadable:
                    Add(plan, FindingLevel.Info, "updater-unreadable", "Updater cache could not be read",
                        "Its contents are unknown.", AdviceRunElevated);
                    return;

                case UpdaterState.Ambiguous:
                    Add(plan, FindingLevel.Warn, "updater-ambiguous", "The staging area holds several installers",
                        "A previous download did not finish cleanly, so the next update may retry the wrong package.",
                        "Delete %LOCALAPPDATA%\\@deepseek-aidsh-desktop-updater\\pending, then run the update again from the app.");
                    return;
            }

            string detail = "Staged: " + Value(facts.PendingExeName) + " (" + Value(facts.PendingVersion) + ").";
            if (string.IsNullOrEmpty(facts.PendingSha512))
                detail += " No sha512 is recorded for it, so its integrity cannot be established from here.";
            else
                detail += " Recorded sha512 present.";

            // Presence is normal; only a version we can compare and find newer is worth flagging,
            // and even then the advice is human, because applying it is not this tool's job.
            int? order = VersionFacts.Compare(facts.PendingVersion, InstalledVersion(facts));
            if (order.HasValue && order.Value > 0)
            {
                Add(plan, FindingLevel.Info, "updater-newer", "A newer installer is staged and not applied",
                    detail + " Installed: " + Value(InstalledVersion(facts)) + ".",
                    "Open the app and let it update, or run the staged installer with the app fully closed.");
            }
            else if (!order.HasValue)
            {
                Add(plan, FindingLevel.Info, "updater-uncompared", "A staged installer is present but its version cannot be compared",
                    detail + " Installed: " + Value(InstalledVersion(facts)) + ".", string.Empty);
            }
            else
            {
                Add(plan, FindingLevel.Ok, "updater-staged", "A staged installer is present and not newer than the installed build",
                    detail, string.Empty);
            }

            if (facts.InstallerInRoot)
            {
                Add(plan, FindingLevel.Info, "updater-root-installer", "The updater cache root also holds installer.exe",
                    "Installers keep a cached copy there; it is not by itself a fault.", string.Empty);
            }
        }

        static void PlanVersions(DiagPlan plan, DiagFacts facts)
        {
            if (string.IsNullOrEmpty(facts.ProductVersion) && string.IsNullOrEmpty(facts.AppVersion))
            {
                Add(plan, FindingLevel.Info, "version-unknown", "The installed version could not be read",
                    "Neither the app metadata nor the executable version resource was readable.", AdviceRunElevated);
            }
        }

        static DiagPlan Finish(DiagPlan plan)
        {
            plan.Healthy = HasNoProblems(plan);
            plan.NeedsAttention = !plan.Healthy;
            return plan;
        }

        static string InstalledVersion(DiagFacts facts)
        {
            return !string.IsNullOrEmpty(facts.AppVersion) ? facts.AppVersion : facts.ProductVersion;
        }

        static bool HasNoProblems(DiagPlan plan)
        {
            foreach (Finding finding in plan.Findings)
                if (finding.Level == FindingLevel.Block || finding.Level == FindingLevel.Warn) return false;
            return true;
        }

        static string Value(string text)
        {
            return string.IsNullOrEmpty(text) ? "(unknown)" : text;
        }

        static void Add(DiagPlan plan, FindingLevel level, string code, string title, string detail, string advice)
        {
            plan.Findings.Add(new Finding
            {
                Level = level,
                Code = code,
                Title = title,
                Detail = detail ?? string.Empty,
                Advice = advice ?? string.Empty,
            });
        }
    }
}
