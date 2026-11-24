using System.Collections.Generic;
using System.IO;

namespace Dsht.Toolbox.Core.Remedy
{
    /// <summary>
    /// The side-effecting half of a repair. Kept behind an interface for two reasons: the
    /// orchestration can be tested without touching the machine, and every destructive step
    /// is visible in one place during review.
    /// </summary>
    public interface IRemedyExecutor
    {
        /// <summary>Closes each process, gracefully first. Returns false when any survivor remains.</summary>
        bool StopHolders(IReadOnlyList<int> pids, TextWriter log);

        /// <summary>Removes staged updater artefacts. Returns false when the path cannot be cleared.</summary>
        bool ClearUpdaterPending(string updaterDir, TextWriter log);

        /// <summary>
        /// Runs the staged installer with a neutral working directory. Returns false when the
        /// installer is missing, cannot start, or exits non-zero.
        /// </summary>
        bool RunInstallerFromTemp(string updaterDir, string exeName, TextWriter log);
    }
}
