namespace Dsht.Toolbox
{
    /// <summary>
    /// Exit codes, as documented in docs/SCOPE.md. Scripts depend on these, so they are
    /// listed in one place and asserted in tests.
    /// </summary>
    internal static class ExitCodes
    {
        /// <summary>The command did what it was asked to do.</summary>
        public const int Ok = 0;

        /// <summary>The command ran and reported a problem, or a repair step failed.</summary>
        public const int Failed = 1;

        /// <summary>The arguments do not describe a valid operation.</summary>
        public const int Usage = 2;

        /// <summary>The command exists but is not implemented on this platform or yet at all.</summary>
        public const int Unsupported = 3;
    }
}
