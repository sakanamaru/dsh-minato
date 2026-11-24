using System.Collections.Generic;
using System.IO;
using Dsht.Toolbox.Core.Remedy;

namespace Dsht.Toolbox.Tests
{
    /// <summary>
    /// Records what a repair would have done. This is what keeps the orchestration testable:
    /// no test below touches a process, a directory, or the network.
    /// </summary>
    internal sealed class FakeRecorder : IRemedyExecutor
    {
        public readonly List<string> Calls = new List<string>();

        /// <summary>Step names that should report failure, e.g. "stop-holders".</summary>
        public readonly HashSet<string> Failing = new HashSet<string>();

        public bool StopHolders(IReadOnlyList<int> pids, TextWriter log)
        {
            Calls.Add("stop-holders:" + pids.Count);
            return !Failing.Contains("stop-holders");
        }

        public bool ClearUpdaterPending(string updaterDir, TextWriter log)
        {
            Calls.Add("clear-updater-pending");
            return !Failing.Contains("clear-updater-pending");
        }

        public bool RunInstallerFromTemp(string updaterDir, string exeName, TextWriter log)
        {
            Calls.Add("run-installer-from-temp");
            return !Failing.Contains("run-installer-from-temp");
        }
    }
}
