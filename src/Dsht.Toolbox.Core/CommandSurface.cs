using System;

namespace Dsht.Toolbox.Core
{
    /// <summary>
    /// The public command contract. Lives in Core rather than in the executable so tests can
    /// assert the cap without referencing a user-facing binary.
    /// </summary>
    public static class CommandSurface
    {
        /// <summary>Hard cap. A toolbox that keeps growing stops being a toolbox (docs/SCOPE.md).
        /// Adding a ninth command requires deleting one first.</summary>
        public const int MaxCommands = 8;

        public static readonly string[] Commands = new string[]
        {
            "hud", "diag", "scan", "repair", "backup", "port", "profile", "log",
        };

        public static bool IsKnown(string name)
        {
            foreach (string command in Commands)
                if (string.Equals(command, name, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
