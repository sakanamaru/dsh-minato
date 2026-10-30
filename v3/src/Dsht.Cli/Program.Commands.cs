using System;
using System.Collections.Generic;
using Dsht.Domain.Model;

namespace Dsht.Cli
{
    /// <summary>W3 / CLI-01：命令注册表 + 显式命令上下文（取代 Main 里的 39 条命令比较与 static ToolkitConfig 配置字段）。
    /// · 命令面的唯一真源 = <see cref="CommandTable"/>：每命令一行 `Reg("name", Handler)`（v3/tests/verify_fixes.ps1 从这里解析）。
    /// · handler 一律接收 <see cref="CommandContext"/>：ServiceRegistry / args / ToolkitConfig 都显式携带，不再有 static 配置。
    /// · 配置放在共享 holder 里：同一菜单会话内 config-set 改过的配置对后续菜单命令立即可见（与旧 static _cfg 行为一致）。
    /// · **保留原 null 语义**：Cfg 可能为 null，所有 `cfg == null` 分支语义不变。</summary>
    public static partial class Program
    {
        private sealed class CommandContext
        {
            public readonly ServiceRegistry Reg;
            public readonly string[] Args;
            private readonly CommandConfigHolder CfgHolder;
            public CommandContext(ServiceRegistry reg, string[] args, CommandConfigHolder cfgHolder)
            {
                Reg = reg;
                Args = args;
                CfgHolder = cfgHolder;
            }
            public ToolkitConfig Cfg
            {
                get { return CfgHolder.Value; }
                set { CfgHolder.Value = value; }
            }
            public CommandContext WithArgs(string[] args) { return new CommandContext(Reg, args, CfgHolder); }
        }

        /// <summary>配置持有者：菜单会话内的多个 CommandContext 共享同一个 holder → config-set 的更新对后续命令可见。</summary>
        private sealed class CommandConfigHolder { public ToolkitConfig Value; }

        /// <summary>一条命令注册（命令名 → 处理器）。每命令一行 Reg("name", Handler) —— 便于门槛脚本扫描。</summary>
        private sealed class CommandDef
        {
            public readonly string Name;
            public readonly Func<CommandContext, int> Handler;
            public CommandDef(string name, Func<CommandContext, int> handler)
            {
                Name = name;
                Handler = handler;
            }
        }

        private static CommandDef Reg(string name, Func<CommandContext, int> handler) { return new CommandDef(name, handler); }

        /// <summary>命令空串 = 无参数时的数字菜单；其余 38 条 = 对外命令面（与 Usage() 一一对应）。</summary>
        private static readonly CommandDef[] CommandTable = new CommandDef[]
        {
            Reg("", Menu),
            Reg("status", Status),
            Reg("overview", Overview),
            Reg("describe", Describe),
            Reg("bridge-install", BridgeInstall),
            Reg("profilecheck", ProfileCheck),
            Reg("profilepatch", ProfilePatch),
            Reg("profiles", Profiles),
            Reg("verify-install", VerifyInstall),
            Reg("wipe", WipeCmd),
            Reg("import", ImportCmd),
            Reg("update-info", UpdateInfo),
            Reg("update-center", UpdateCenter),
            Reg("balance", Balance),
            Reg("log", LogCmd),
            Reg("about", AboutCmd),
            Reg("shortcut", ShortcutCmd),
            Reg("ui", UiCmd),
            Reg("install", InstallCmd),
            Reg("update", UpdateCmd),
            Reg("uninstall", UninstallCmd),
            Reg("start", StartCmd),
            Reg("stop", StopCmd),
            Reg("sessions", Sessions),
            Reg("cost", Cost),
            Reg("backup-list", BackupList),
            Reg("backup-dir", BackupDirCmd),
            Reg("doctor", Doctor),
            Reg("version", VersionCmd),
            Reg("config-get", ConfigGet),
            Reg("config-set", ConfigSet),
            Reg("bootdiag", BootDiag),
            Reg("restore", Restore),
            Reg("selftest", SelfTest),
            Reg("check", Check),
            Reg("backup", Backup),
            Reg("backup-export", BackupExport),
            Reg("backup-delete", BackupDelete),
            Reg("autostart", AutoStartCmd),
        };

        private static readonly Dictionary<string, Func<CommandContext, int>> CommandMap = BuildCommandMap();

        private static Dictionary<string, Func<CommandContext, int>> BuildCommandMap()
        {
            Dictionary<string, Func<CommandContext, int>> map = new Dictionary<string, Func<CommandContext, int>>(StringComparer.Ordinal);
            for (int i = 0; i < CommandTable.Length; i++) map[CommandTable[i].Name] = CommandTable[i].Handler;
            return map;
        }

        /// <summary>T() 的语言判据：由配置派生的一次性标记（T 有 491 个调用点，不逐个传 ctx）。
        /// 与旧 static _cfg 行为一致：Main 载入配置时设置，config-set 重新载入配置时同步更新。</summary>
        private static bool _isEn;
        private static void ApplyLanguageFlag(ToolkitConfig cfg) { _isEn = cfg != null && cfg.Lang == "en"; }

        /// <summary>status 的注册入口：--detail 从参数取（内部调用方用 StatusImpl 显式给 true/false）。</summary>
        private static int Status(CommandContext ctx) { return StatusImpl(ctx, Has(ctx.Args, "--detail")); }

        /// <summary>install / update 共用实现，唯一的差别是 update 标记。</summary>
        private static int InstallCmd(CommandContext ctx) { return InstallLike(ctx, false); }
        private static int UpdateCmd(CommandContext ctx) { return InstallLike(ctx, true); }

        /// <summary>version：只读版本标记。</summary>
        private static int VersionCmd(CommandContext ctx)
        {
            Console.WriteLine("DSHT_VERSION " + ToolkitVersion);
            return 0;
        }
    }
}
