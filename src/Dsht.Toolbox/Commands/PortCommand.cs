using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Dsht.Toolbox.Windows;

namespace Dsht.Toolbox.Commands
{
    /// <summary>port：3080 是否被占、被谁占。只读。</summary>
    internal static class PortCommand
    {
        public static int Run(string[] args)
        {
            int port = 3080;
            string p = DiagCommand.Arg(args, "--port");
            if (!string.IsNullOrEmpty(p)) int.TryParse(p, out port);

            bool listening = false;
            try
            {
                var props = IPGlobalProperties.GetIPGlobalProperties();
                foreach (var ep in props.GetActiveTcpListeners())
                    if (ep.Port == port) { listening = true; break; }
            }
            catch (Exception ex) { Console.Error.WriteLine("读端口失败: " + ex.Message); return ExitCodes.Failed; }

            Console.WriteLine("port " + port + ": " + (listening ? "LISTENING" : "not listening"));
            if (!listening) return ExitCodes.Ok;

            Console.WriteLine();
            Console.WriteLine("持有该端口的进程：");
            int holders = 0;
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    string path = null;
                    try { path = proc.MainModule != null ? proc.MainModule.FileName : null; } catch { }
                    if (path == null) continue;
                    bool isDsh = path.IndexOf("DeepSeek Harness", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isDsh) continue;
                    Console.WriteLine("  pid " + proc.Id + "  " + proc.ProcessName + "  " + path);
                    holders++;
                }
                catch { }
                finally { try { proc.Dispose(); } catch { } }
            }
            if (holders == 0)
            {
                Console.WriteLine("  （没有找到 dsh 进程；端口可能是别的程序占用）");
                return ExitCodes.Failed;
            }
            return ExitCodes.Ok;
        }
    }
}
