using System.IO;
using Dsht.Domain.Abstractions;
using Dsht.Domain.Model;
using Dsht.Domain.Targets;
using Dsht.Platform.Linux;
using Dsht.Platform.Windows;

namespace Dsht.Cli
{
    /// <summary>平台装配：按运行平台选择实现（单 exe、运行时判定）。
    /// 判定用 Path.DirectorySeparatorChar 而非 RuntimeInformation —— 前者在 .NET Framework 与 .NET 8 上行为一致，
    /// 且不需要额外 API（这也是本地用 csc 就能编译验证整套代码的原因）。</summary>
    internal static class PlatformComposition
    {
        internal const int WebPort = 3080;
        /// <summary>探测/显示用的 web 地址。**host 接线（2026-10-06）**：配置键 host 现在真的生效 ——
        /// 默认 127.0.0.1（行为不变 ✓）；设成 localhost 时探测/显示用 http://localhost:3080 ✓。</summary>
        internal static string WebUrl = "http://127.0.0.1:3080";

        /// <summary>启动前（Compose 之前）调用：把配置里的 host 应用到 WebUrl ✓ 白名单只有 127.0.0.1/localhost ✓。</summary>
        internal static void ApplyWebHost(string host)
        {
            string h = (host ?? "").Trim();
            if (h == "localhost" || h == "127.0.0.1") WebUrl = "http://" + h + ":3080";
        }

        public static bool IsWindows()
        {
            return Path.DirectorySeparatorChar == '\\';
        }

        /// <summary>默认数据根候选（由当前平台实现给出）。供 restore --apply 的隔离判定使用——
        /// 领域层只接收字符串数组，不依赖任何平台 API。</summary>
        public static string[] DefaultDataRoots()
        {
            return IsWindows() ? WindowsPaths.DefaultDataRoots() : LinuxPaths.DefaultDataRoots();
        }


        /// <summary>组合服务目标：web 用真实观测；headless/acp/desktop 是**预留形态**（当前无可观测事实）。
        /// 这样"未识别形态"在 CLI 里是一等公民——不假装 Ready，也不假装"没在跑"。</summary>
        /// <summary>为指定端口构造一个 Web 目标（stop --port 用）。
        /// 放在这里是因为 WebTarget 就在本文件域内 —— 跨文件猜命名空间已经失败过两次，不再猜。</summary>
        internal static IServiceTarget WebFor(int port, IPortProbe portProbe, IHttpProbe http, IProcessQuery proc)
        {
            return new WebTarget(portProbe, http, proc, new WebTargetOptions(port, "http://127.0.0.1:" + port, 800, 800));
        }
        private static IServiceTarget Composite(IPortProbe port, IHttpProbe http, IProcessQuery proc)
        {
            return new CompositeServiceTarget(new IServiceTarget[]
            {
                new WebTarget(port, http, proc, new WebTargetOptions(WebPort, WebUrl, 800, 800)),
                new ReservedTarget(AppKind.Headless, "无监听端口可观测；需要进程枚举能力（待接入）"),
                new ReservedTarget(AppKind.Acp, "ACP 走 stdio/管道，当前无可观测入口（待 dsh 侧形态明确）"),
                new ReservedTarget(AppKind.Desktop, "桌面端形态待观测（进程名/IPC 未知）")
            });
        }

        public static ServiceRegistry Compose()
        {
            ServiceRegistry reg = new ServiceRegistry();
            if (IsWindows())
            {
                WindowsPaths paths = new WindowsPaths();
                WindowsHttpProbe http = new WindowsHttpProbe();
                WindowsPortProbe port = new WindowsPortProbe();
                WindowsProcessQuery proc = new WindowsProcessQuery(http, WebUrl, 800);
                reg.Add<IPaths>(paths);
                reg.Add<IPortProbe>(port);
                reg.Add<IHttpProbe>(http);
                reg.Add<IProcessQuery>(proc);
                reg.Add<IToolchainQuery>(new WindowsToolchainQuery());
                reg.Add<IFileSystemQuery>(new WindowsFileSystemQuery());
                reg.Add<IIntegritySource>(new WindowsIntegritySource());
                reg.Add<IProfileSource>(new WindowsProfileSource(paths));
                reg.Add<IProfileManifestSource>(new WindowsProfileManifestSource(paths));
                reg.Add<ISessionStatsSource>(new WindowsSessionStatsSource(paths));
                reg.Add<IServiceControl>(new WindowsServiceControl());
                reg.Add<IBackupSource>(new WindowsBackupSource(paths));
                reg.Add<IConfigSource>(new WindowsConfigSource(paths));
                reg.Add<ILogSource>(new WindowsLogSource(paths));
                reg.Add<IServiceTarget>(Composite(port, http, proc));
            }
            else
            {
                LinuxPaths paths = new LinuxPaths();
                LinuxHttpProbe http = new LinuxHttpProbe();
                LinuxPortProbe port = new LinuxPortProbe();
                LinuxProcessQuery proc = new LinuxProcessQuery(http, WebUrl, 800);
                reg.Add<IPaths>(paths);
                reg.Add<IPortProbe>(port);
                reg.Add<IHttpProbe>(http);
                reg.Add<IProcessQuery>(proc);
                reg.Add<IToolchainQuery>(new LinuxToolchainQuery());
                reg.Add<IFileSystemQuery>(new LinuxFileSystemQuery());
                reg.Add<IIntegritySource>(new LinuxIntegritySource());
                reg.Add<IProfileSource>(new LinuxProfileSource(paths));
                reg.Add<IProfileManifestSource>(new LinuxProfileManifestSource(paths));
                reg.Add<ISessionStatsSource>(new LinuxSessionStatsSource(paths));
                reg.Add<IServiceControl>(new LinuxServiceControl());
                reg.Add<IBackupSource>(new LinuxBackupSource(paths));
                reg.Add<IConfigSource>(new LinuxConfigSource(paths));
                reg.Add<ILogSource>(new LinuxLogSource(paths));
                reg.Add<IServiceTarget>(Composite(port, http, proc));
            }
            return reg;
        }
    }
}