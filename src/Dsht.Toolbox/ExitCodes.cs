namespace Dsht.Toolbox
{
    /// <summary>退出码契约（与 docs/SCOPE.md 第 7 条一致）。</summary>
    internal static class ExitCodes
    {
        public const int Ok = 0;
        public const int Failed = 1;
        public const int Usage = 2;
        public const int Unsupported = 3;
    }
}
