using System.Collections.Generic;

namespace Dsht.Toolbox.Core.Diag
{
    /// <summary>
    /// Three states, because unreadable is not the same as absent. The default is Absent on
    /// purpose: an unset fact must not read as "something is there".
    /// </summary>
    public enum Presence { Absent = 0, Present = 1, Unreadable = 2 }

    public sealed class HolderFact
    {
        public int Pid;
        public string Name = string.Empty;
        public string Path = string.Empty;
        public long StartTimeUtcTicks;

        /// <summary>Path lies inside the install directory, compared on directory boundaries.</summary>
        public bool InsideInstallDir;

        /// <summary>Looks like the bundled backend runtime rather than the shell.</summary>
        public bool IsBackend;
    }

    public enum UpdaterState
    {
        None = 0,
        Clean = 1,

        /// <summary>A staged package exists and metadata identifies it.</summary>
        Pending = 2,

        /// <summary>A staged package exists but metadata is missing or unparsable.</summary>
        PendingUnverified = 3,

        /// <summary>Several staged executables, or a staging area that is not a directory.</summary>
        Ambiguous = 4,

        Unreadable = 5,
    }

    /// <summary>
    /// Facts collected from the machine. Dumb data only; anything unreadable is reported as such.
    /// </summary>
    public sealed class DiagFacts
    {
        public string InstallDir = string.Empty;
        public string UpdaterDir = string.Empty;

        public Presence InstallDirPresence;
        public Presence MainExePresence;
        public Presence UninstallerPresence;

        /// <summary>Version reported by the desktop application itself, when readable.</summary>
        public string AppVersion;

        /// <summary>Version from the executable's version resource; four-part and tag-less.</summary>
        public string ProductVersion;

        /// <summary>Electron's own version, from the install directory.</summary>
        public string ElectronVersion;

        /// <summary>Payload components that are missing or unreadable, by relative path.</summary>
        public List<string> PayloadGaps = new List<string>();

        public UpdaterState Updater;
        public string PendingExeName;
        public string PendingVersion;
        public string PendingSha512;
        public long PendingBytes;
        public bool InstallerInRoot;

        public List<HolderFact> Holders = new List<HolderFact>();

        /// <summary>True when the process table could not be enumerated at all.</summary>
        public bool ProcessEnumerationFailed;
    }
}