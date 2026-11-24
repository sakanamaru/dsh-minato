using System;
using Dsht.Toolbox.Commands;
using Dsht.Toolbox.Core;
using Dsht.Toolbox.Windows;

namespace Dsht.Toolbox
{
    /// <summary>Thin dispatcher; the command list itself lives in <see cref="CommandSurface"/>.</summary>
    internal static class Program
    {
        internal const string Version = "0.1.0-dev";

        internal static int Main(string[] args)
        {
            // The overlay must be DPI aware before any window or font is created.
            Dpi.Initialize();

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
                    return HudCommand.Run(rest);

                default:
                    if (!CommandSurface.IsKnown(command))
                    {
                        Console.Error.WriteLine("unknown command: " + command);
                        Console.Error.WriteLine();
                        return PrintUsage();
                    }

                    Console.Error.WriteLine(command + ": reserved, not implemented (" + ExitCodes.Unsupported + ")");
                    return ExitCodes.Unsupported;
            }
        }

        static int PrintUsage()
        {
            Console.WriteLine("dsht " + Version + " - dsh toolbox (diagnosis only)");
            Console.WriteLine();
            Console.WriteLine("  dsht diag [--json] [--dir <path>] [--updater <path>]");
            Console.WriteLine("            Read-only diagnosis of a DeepSeek Harness desktop install.");
            Console.WriteLine("  dsht port [--port <n>]");
            Console.WriteLine("            Is the web port listening, and which dsh processes are running.");
            Console.WriteLine("  dsht hud --preview <file.png> [--snapshot <path>] [--scale <100-300>]");
            Console.WriteLine("            Render the status overlay to an image (the window comes later).");
            Console.WriteLine();
            Console.WriteLine("  dsht scan|repair|backup|profile|log   reserved (exit " + ExitCodes.Unsupported + ")");
            Console.WriteLine();
            Console.WriteLine("Exit codes: 0 ok / 1 problems found / 2 usage / 3 unsupported");
            Console.WriteLine("This build never modifies the machine. Repair is not implemented.");
            return ExitCodes.Usage;
        }
    }
}