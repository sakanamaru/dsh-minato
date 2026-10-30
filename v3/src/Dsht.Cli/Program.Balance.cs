using System;
using Dsht.Domain.Model;

namespace Dsht.Cli
{
    /// <summary>balance —— DeepSeek 平台余额检测（2026-10-02 用户要求："做完再加个DSH余额检测（自己填写key）" ✓✓）。</summary>
    public static partial class Program
    {
        /// <summary>balance（V3 独有，**只读** ✓）。
        /// 标记行：
        ///   `BALANCE_STATE bound|unbound`      ← unbound 时**后面什么都没有** ✓（GUI 据此隐藏余额卡 ✓ 用户要求 ✓）
        ///   `BALANCE_CURRENCY CNY`             ← 平台给的币种 ✓
        ///   `BALANCE_TOPUP n`                   ← **充值余额** ✓（topped_up_balance ✓）
        ///   `BALANCE_GRANTED n`                ← **活动赠送余额** ✓（granted_balance ✓）
        ///   `BALANCE_TOTAL n`                  ← 总计 ✓
        ///   `BALANCE_NOTE <说明>`              ← is_available=false 时如实转述 ✓
        ///   `BALANCE_UNAVAILABLE <原因>`       ← 取不到时（key 无效 / 网络 ✗ **绝不冒充数字** ✓✓）
        /// 纪律：
        ///   · key 来自配置（`balance_key` ✓ 用户自己填 ✓ 留空 = 未绑定 ✓）—— 本命令**不接收命令行传 key** ✗
        ///     （命令行会进 shell 历史/进程列表 ✓ 配置文件只在你机器上 ✓ 比参数安全 ✓）
        ///   · 联网命令 ✓ —— 已加进 about 的"会联网的只有…"清单 ✓（诚实边界 ✓✓）
        ///   · 解析是领域层纯函数（BalanceJson ✓），HTTP 是 CLI 层 IO ✓ 领域纯净度门槛不破 ✓。</summary>
        private static int Balance(CommandContext ctx)
        {
            ServiceRegistry reg = ctx.Reg;
            string[] args = ctx.Args;
            ToolkitConfig cfg = ctx.Cfg;
            string key = cfg == null ? "" : (cfg.BalanceKey == null ? "" : cfg.BalanceKey).Trim();
            if (key.Length == 0)
            {
                Console.WriteLine("BALANCE_STATE unbound");
                return 0;
            }
            Console.WriteLine("BALANCE_STATE bound");
            try
            {
                string body = BalanceHttpGet("https://api.deepseek.com/user/balance", key, 10000);
                Dsht.Domain.Services.BalanceInfo bi = Dsht.Domain.Services.BalanceJson.Parse(body);
                if (!bi.Parsed)
                {
                    Console.WriteLine("BALANCE_UNAVAILABLE " + T("响应不认（key 可能无效，或平台改了返回格式），因此不给出数字",
                        "unrecognized response (invalid key, or the format changed) - no numbers reported"));
                    return 0;
                }
                if (bi.Currency.Length > 0) Console.WriteLine("BALANCE_CURRENCY " + bi.Currency);
                Console.WriteLine("BALANCE_TOPUP " + (bi.Topup.Length == 0 ? "unknown" : bi.Topup));
                Console.WriteLine("BALANCE_GRANTED " + (bi.Granted.Length == 0 ? "unknown" : bi.Granted));
                Console.WriteLine("BALANCE_TOTAL " + (bi.Total.Length == 0 ? "unknown" : bi.Total));
                if (!bi.IsAvailable)
                    Console.WriteLine("BALANCE_NOTE " + T("平台标记该账户当前不可用（is_available=false，可能欠费或 key 已停用）",
                        "the platform marks this account as currently unavailable (possibly out of balance, or the key is disabled)"));
            }
            catch (Exception ex)
            {
                Console.WriteLine("BALANCE_UNAVAILABLE " + T("请求失败（网络不通或超时）", "request failed (network unreachable or timeout)") + ": " + ex.Message);
            }
            return 0;
        }

        /// <summary>带 Bearer 的 GET ✓（与 FetchLatestRelease **同一套** HttpWebRequest 姿势 ✓ 零第三方依赖 ✓）。
        /// 任何异常**直接抛**给调用方 ✓（由 Balance 翻译成 BALANCE_UNAVAILABLE ✓ 绝不吞成空数据 ✗✗）。
        /// 注：3072 = Tls12 的数值形式 ✓（老运行时没有这个枚举名 ✓ 数值两边都合法 ✓）。</summary>
        private static string BalanceHttpGet(string url, string key, int timeoutMs)
        {
            try { System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072; } catch { }
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            req.UserAgent = "dsh-minato";
            req.Headers.Add("Authorization", "Bearer " + key);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
            using (System.IO.Stream s = resp.GetResponseStream())
            using (System.IO.StreamReader r = new System.IO.StreamReader(s, new System.Text.UTF8Encoding(false)))
            {
                return r.ReadToEnd();
            }
        }
    }
}
