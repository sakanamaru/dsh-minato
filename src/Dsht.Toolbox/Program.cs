using System;
using Dsht.Toolbox.Commands;
using Dsht.Toolbox.Core;

namespace Dsht.Toolbox
{
    /// <summary>Thin dispatcher. The command list itself is <see cref="CommandSurface"/> in Core.</summary>
    internal static class Program
    {

        internal static int Main(string[] args)
        {
            if (args == null || args.Length == 0) return PrintUsage();


            string command = args[0].Trim().ToLowerInvariant();
            string[] rest = new string[args.Length - 1];
            Array.Copy(args, 1, rest, 0, rest.Length);

            switch (command)
            {
                case "--help":
                case "-h":
                case "help":
                    return PrintUsage();

                case "--version":
                case "version":
                    Console.WriteLine("dsht " + Version + " (v4)");
                    return ExitCodes.Ok;

                case "diag":
                    return DiagCommand.Run(rest);

                case "port":
                    return PortCommand.Run(rest);

                case "hud":
                case "scan":
                case "repair":
                case "backup":
                case "profile":
                case "log":
                    Console.Error.WriteLine(command + ": not implemented yet (" + ExitCodes.Unsupported + ")");
                    return ExitCodes.Unsupported;

                default:
                    Console.Error.WriteLine("unknown command: " + command);
                    Console.Error.WriteLine();
                    return PrintUsage();
            }
        }

        internal const string Version = "0.1.0-dev";

        static int PrintUsage()
        {
            Console.WriteLine("dsht " + Version + " - dsh toolbox + HUD");
            Console.WriteLine();
            Console.WriteLine("  dsht diag [--json] [--dir <path>] [--updater <path>]");
            Console.WriteLine("            Diagnose a desktop install. Read-only.");
            Console.WriteLine("  dsht diag --fix [--yes]");
            Console.WriteLine("            Close holders, clear the updater staging area, re-run the staged installer.");
            Console.WriteLine("  dsht port [--port <n>]");
            Console.WriteLine("            Is the web port taken, and by which dsh processes.");
            Console.WriteLine();
            Console.WriteLine("  dsht hud|scan|repair|backup|profile|log   (planned, exit " + ExitCodes.Unsupported + ")");
            Console.WriteLine();
            Console.WriteLine("Exit codes: 0 ok / 1 failed / 2 usage / 3 unsupported");
            return ExitCodes.Usage;
        }
    }
}