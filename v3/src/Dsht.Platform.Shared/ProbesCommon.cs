using System;
using System.Net;
using System.Net.Sockets;
using Dsht.Domain.Abstractions;

namespace Dsht.Platform.Shared
{
    /// <summary>端口探测（Windows/Linux 共用实现 · B-05 平台层去重）：TcpClient 连接回环地址。逐条对齐 v2.x 的 IsPortOpen。</summary>
    public abstract class PortProbeCommon : IPortProbe
    {
        public bool IsOpen(int port, int timeoutMs)
        {
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    System.Threading.Tasks.Task t = c.ConnectAsync(IPAddress.Loopback, port);
                    return t.Wait(timeoutMs) && c.Connected;
                }
            }
            catch { return false; }
        }
    }

    /// <summary>HTTP 探测（Windows/Linux 共用实现 · B-05 平台层去重）：GET，2xx/3xx 视为就绪。对齐 v2.x 的 HttpReady / HttpResponds。</summary>
    public abstract class HttpProbeCommon : IHttpProbe
    {
        public bool IsReady(string url, int timeoutMs)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.AllowAutoRedirect = true;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    return code >= 200 && code < 400;
                }
            }
            catch { return false; }
        }

        /// <summary>接口方法：任意状态码也算有应答（对齐 v2.x 的 HttpResponds）。</summary>
        public bool Responds(string url, int timeoutMs)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.AllowAutoRedirect = true;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    return resp != null;
                }
            }
            catch (WebException wex) { return wex.Response != null; }
            catch { return false; }
        }
    }
}
