using System;
using Dsht.Toolbox.Commands;

namespace Dsht.Toolbox
{
    /// <summary>唯一真源：命令注册表。每命令一行 Reg(...)；超过 8 条即违反 docs/SCOPE.md。</summary>
    internal static class Program
    {
        static readonly string[] Names = { "hud", "diag", "scan", "repair", "backup", "port", "profile", "log" };

        static int Main(string[] args)
        {
            if (args == null || args.Length == 0) return Usage();
            string cmd = args[0].Trim().ToLowerInvariant();
            string[] rest = new string[args.Length - 1];
            Array.Copy(args, 1, rest, 0, rest.Length);

            if (cmd == "--help" || cmd == "-h" || cmd == "help") return Usage();
            if (cmd == "--version" || cmd == "version") { Console.WriteLine("dsht 0.1.0-dev (v4)"); return ExitCodes.Ok; }

            switch (cmd)
            {
                case "diag": return DiagCommand.Run(rest);
                case "port": return PortCommand.Run(rest);
                case "hud":
                case "scan":
                case "repair":
                case "backup":
                case "profile":
                case "log":
                    Console.Error.WriteLine("not implemented yet: " + cmd + "  (v4 M1 只做 diag/port)");
                    return ExitCodes.Unsupported;
                default:
                    Console.Error.WriteLine("unknown command: " + cmd);
                    return Usage();
            }
        }

        static int Usage()
        {
            Console.WriteLine("dsht — dsh toolbox + HUD (v4)");
            Console.WriteLine();
            Console.WriteLine("  dsht diag [--json] [--fix --yes]   诊断桌面端安装/更新故障（--fix 才写）");
            Console.WriteLine("  dsht port                           3080 端口与占用进程");
            Console.WriteLine("  dsht hud|scan|repair|backup|profile|log   （未实现，exit 3）");
            Console.WriteLine();
            Console.WriteLine("退出码: 0 成功 / 1 执行失败 / 2 用法错误 / 3 不支持");
            return ExitCodes.Usage;
        }

        public static bool IsKnown(string name)
        {
            foreach (var n in Names) if (n == name) return true;
            return false;
        }
    }
}
