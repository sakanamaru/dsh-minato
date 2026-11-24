using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace Dsht.Toolbox.Commands
{
    /// <summary>
    /// <c>dsht port</c>: whether the web port is listening, and which processes run from a
    /// DeepSeek Harness install.
    ///
    /// This deliberately does not claim to name the owner of the port: resolving a listener to a
    /// process needs the TCP owner table, and presenting a list of dsh processes as "the owner"
    /// would be a guess. The two facts are reported separately and the report says which is which.
    /// </summary>
    internal static class PortCommand
    {
        const int DefaultPort = 3080;
        const int MinPort = 1;
        const int MaxPort = 65535;

        public static int Run(string[] args)
        {
            if (!TryParse(args, out int port, out string error))
            {
                Console.Error.WriteLine(error);
                return ExitCodes.Usage;
            }

            bool listening;
            try
            {
                listening = IsListening(port);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("could not read the port table: " + ex.Message);
                return ExitCodes.Failed;
            }

            Console.WriteLine("port " + port + ": " + (listening ? "LISTENING" : "not listening"));

            int dsh = ReportHarnessProcesses();
            Console.WriteLine();
            Console.WriteLine("owner of port " + port + " : not resolved (this build does not read the TCP owner table)");
            Console.WriteLine("processes running from a dsh install : " + dsh);

            if (!listening && dsh == 0) return ExitCodes.Ok;
            if (listening && dsh == 0)
            {
                Console.WriteLine("verdict: the port is held by something outside a dsh install");
                return ExitCodes.Failed;
            }

            return ExitCodes.Ok;
        }

        static int ReportHarnessProcesses()
        {
            int found = 0;
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    string path = TryGetPath(process);
                    if (path == null) continue;
                    if (path.IndexOf("DeepSeek Harness", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Console.WriteLine("  pid " + process.Id + "  " + process.ProcessName + "  " + path);
                    found++;
                }
                catch (InvalidOperationException)
                {
                    // The process exited while being inspected; it cannot be listed, which is not
                    // an error in a read-only inventory.
                }
                finally
                {
                    process.Dispose();
                }
            }
            return found;
        }

        static bool TryParse(string[] args, out int port, out string error)
        {
            port = DefaultPort;
            error = string.Empty;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != "--port")
                {
                    error = "unknown option: " + args[i];
                    return false;
                }

                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    error = "--port needs a value";
                    return false;
                }

                if (!int.TryParse(args[i + 1], out port) || port < MinPort || port > MaxPort)
                {
                    error = "--port must be a number between " + MinPort + " and " + MaxPort;
                    return false;
                }

                i++;
            }

            return true;
        }

        static bool IsListening(int port)
        {
            IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
            foreach (IPEndPoint endpoint in properties.GetActiveTcpListeners())
                if (endpoint.Port == port) return true;
            return false;
        }

        static string TryGetPath(Process process)
        {
            try { return process.MainModule != null ? process.MainModule.FileName : null; }
            catch { return null; }
        }
    }
}