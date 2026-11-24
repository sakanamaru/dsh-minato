using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace Dsht.Toolbox.Commands
{
    /// <summary>
    /// <c>dsht port</c>: is the dsh web port taken, and by what. Read-only.
    /// Exists because "port 3080 already in use" is the most common start failure and the
    /// answer is rarely in dsh's own logs.
    /// </summary>
    internal static class PortCommand
    {
        const int DefaultPort = 3080;

        public static int Run(string[] args)
        {
            int port = DefaultPort;
            string explicitPort = Flags.Value(args, "--port");
            if (!string.IsNullOrEmpty(explicitPort) && !int.TryParse(explicitPort, out port))
            {
                Console.Error.WriteLine("--port needs a number");
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
            if (!listening) return ExitCodes.Ok;

            Console.WriteLine();
            Console.WriteLine("dsh processes (holders are matched by executable path):");
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
                catch
                {
                    // A process that exits mid-enumeration is not an error.
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (found == 0)
            {
                Console.WriteLine("  (no dsh process found; something else holds the port)");
                return ExitCodes.Failed;
            }

            return ExitCodes.Ok;
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