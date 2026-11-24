using System;
using System.Collections.Generic;

namespace Dsht.Toolbox.Core.Diag
{
    /// <summary>一条占用事实（纯数据，不读进程）。</summary>
    public sealed class HolderFact
    {
        public int Pid;
        public string Name = "";
        public string Path = "";
        /// <summary>true = 路径在安装目录内（真正的占用嫌疑）；false = 仅同名进程。</summary>
        public bool InsideInstallDir;
        /// <summary>true = 是 dsh 的后端 node（resources\runtime 下）。</summary>
        public bool IsBackend;
    }

    /// <summary>updater 缓存目录的状态（纯数据）。</summary>
    public enum UpdaterState
    {
        /// <summary>目录不存在：从未检查过更新。</summary>
        None = 0,
        /// <summary>只有空壳/无待装包：干净。</summary>
        Clean = 1,
        /// <summary>有 pending 安装包但版本未知。</summary>
        PendingUnknown = 2,
        /// <summary>有 pending 安装包且知道版本（可用于和已装版本比较）。</summary>
        PendingKnown = 3,
        /// <summary>有可疑残留（installer.exe 在根、pending 半截、blockmap 残）。</summary>
        Suspicious = 4,
    }

    /// <summary>诊断输入的全部事实（由 IO 层填充，Core 只做推理）。</summary>
    public sealed class DiagFacts
    {
        public string InstallDir = "";
        public string UpdaterDir = "";
        public bool InstallDirExists;
        public bool MainExeExists;
        public bool UninstallerExists;
        /// <summary>安装目录里 version 文件的内容（Electron 版本），读不到为 null。</summary>
        public string ElectronVersion;
        /// <summary>主 exe 的 ProductVersion（dsh 版本），读不到为 null。</summary>
        public string ProductVersion;
        public UpdaterState Updater;
        public string PendingExeName;
        public string PendingVersion;
        public long PendingBytes;
        public bool InstallerInRoot;
        public List<HolderFact> Holders = new List<HolderFact>();
        /// <summary>无法读取路径的进程数（权限不足），不等于“没占用”。</summary>
        public int UnreadableProcesses;
    }
}
