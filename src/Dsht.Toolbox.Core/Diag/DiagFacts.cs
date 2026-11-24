using System.Collections.Generic;

namespace Dsht.Toolbox.Core.Diag
{
    /// <summary>One process that may be holding the install directory open.</summary>
    public sealed class HolderFact
    {
        public int Pid;
        public string Name = string.Empty;
        public string Path = string.Empty;

        /// <summary>Path lives under the install directory: a real suspect for a locked file.</summary>
        public bool InsideInstallDir;

        /// <summary>Bundled backend runtime (resources/runtime): its node.exe is the usual lock.</summary>
        public bool IsBackend;
    }

    /// <summary>Shape of the desktop updater cache directory.</summary>
    public enum UpdaterState
    {
        /// <summary>The cache does not exist: no update was ever staged.</summary>
        None = 0,

        /// <summary>The cache exists with nothing staged.</summary>
        Clean = 1,

        /// <summary>An installer is staged but its version is unknown.</summary>
        PendingUnknown = 2,

        /// <summary>An installer is staged and its version is known.</summary>
        PendingKnown = 3,

        /// <summary>Partial or leftover artefacts that make the next update fail repeatedly.</summary>
        Suspicious = 4,
    }

    /// <summary>
    /// Everything the planner needs, filled by an IO layer. Deliberately dumb data:
    /// no behaviour here, so the planner stays a pure function.
    /// </summary>
    public sealed class DiagFacts
    {
        public string InstallDir = string.Empty;
        public string UpdaterDir = string.Empty;

        public bool InstallDirExists;
        public bool MainExeExists;
        public bool UninstallerExists;

        /// <summary>Contents of the Electron <c>version</c> file, when present.</summary>
        public string ElectronVersion;

        /// <summary>ProductVersion of the main executable; may be normalised to four parts.</summary>
        public string ProductVersion;

        public UpdaterState Updater;
        public string PendingExeName;
        public string PendingVersion;
        public long PendingBytes;
        public bool InstallerInRoot;

        public List<HolderFact> Holders = new List<HolderFact>();

        /// <summary>
        /// Processes whose path could not be read (access denied). Not evidence of absence:
        /// reported so an unelevated run does not look more certain than it is.
        /// </summary>
        public int UnreadableProcesses;
    }
}
