using System;
using System.Collections.Generic;
using System.Text;

namespace Dsht.Gui.Avalonia.Markers
{
    /// <summary>★★ 2026-10-11（插件卡可读性）：**从更新页插件卡搬出来的那段机制说明**（纯文本 ✓ 纯函数 ✓ 无 IO 之外的东西 ✓）。
    ///
    /// 为什么在这里：维护者实测「更新页的插件卡读起来费劲 ✗」——
    ///   ① 每行是一句长话 ✗ ② 卡头长 ✗ ③ 一条 NOTE 是一整段机制说明 ✗ ④ 长路径直接印在卡里抢注意力 ✗。
    /// → 卡片改**固定五列的小表**（`UpdateCenterMarkers.TableHeaders` ✓ 每格短 ✓）；
    ///   而**基准是什么 / 路径在哪 / 双比对是什么 / 为什么没有独立版本渠道 / desktop 为什么装不进 /
    ///   更新前的风险与备份 / 作者维护不担保** —— 整段搬进 GUI 的**「说明」页** ✓（架构说明本来就在那一页 ✓）。
    /// ★ **内容一个字都没丢** ✗（搬走可以 ✓ 消失不行 ✗）：下面两段就是被搬走的原文口径 ✓；
    ///   而且它是**纯函数** → LogicTests 与 `verify_plugin_update_baseline.ps1` 都能直接断言"它真的还在" ✓
    ///   （✗ 不靠"某处的 UI 代码看起来像写了它" ✗）。
    /// ✗ 本类不引用 Avalonia ✗（渲染在 `Shells/Shells.cs` 的说明页里做 ✓）。</summary>
    public static class UpdateBaselineExplain
    {
        /// <summary>「插件更新的基准与判定」这一节的正文 ✓（逐条对应卡片里**不再写**的那些口径 ✓）。
        /// 段与段之间用 `\n` ✓（渲染侧直接交给会换行的 TextBlock ✓）。</summary>
        public static string Text()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("更新页里的插件行只回答一件事：你这台机器上每个 profile 的 dsh-minato-bridge，和你这份工具箱自带的那一份比，是「已是最新」「有更新」还是「内容不一致」。");
            sb.Append('\n').Append("基准 = 你这份工具箱自带的插件，就在本程序旁边的 plugin\\dsh-minato-bridge\\（完整路径见下面这一节）。");
            sb.Append('\n').Append("这个基准是离线的、可判的：不查网上的最新版，也因此不需要任何新的版本渠道。");
            sb.Append('\n').Append("latest 取的是工具箱自带那一份的版本号。插件没有独立的版本渠道，它的版本号（package.json 的 version）与工具箱的 tag（如 v3.1.0）不在一个序列上，所以本工具不会拿工具箱 tag 当插件的 latest。");
            sb.Append('\n').Append("判定 = 双比对：先比版本号（VersionComparer），再比内容哈希（snapshot.js 与 index.js 各算一份 SHA-256，两个都相同才算内容一致）。版本号相同但哈希不同 = 半旧或被改过，单独算一态，只看版本号抓不出来。");
            sb.Append('\n').Append("有些 profile 不需要你动手：desktop 由 dsh 桌面端独占管理，命令行装不进也卸不掉，请在桌面端的插件机制里更新（它接受本地目录路径）。");
            sb.Append('\n').Append("插件更新一律先描述风险再确认；更新前会自动备份数据根并保留回滚点。插件由各自作者维护，本工具不替它更新、也不替它担保。");
            return sb.ToString();
        }

        /// <summary>「插件基准的完整路径（本机实读）」这一节的正文 ✓。
        /// ① 基准目录 = **本程序旁边的 `plugin\dsh-minato-bridge\`** ✓
        ///    —— 与 CLI 的判定同一口径（CLI：基准 = exe 目录下的 `plugin\&lt;包名&gt;\` ✓ 见 `Program.Ops.cs` 的 `LoadBundledBridge` ✓）；
        ///    开发构建（源码编译、单独复制 exe）旁边常常没有它 → **如实说"没有"** ✓ ✗ 不编一个路径 ✗。
        /// ② 顺带把"这台机器上真的去看过哪些位置"原样列出 ✓（`VersionMarkers.BridgeCandidates` ✓ 与"形态与插件"页同一函数 ✓）
        ///    —— 卡片里那三行说的就是这些位置的实读结果 ✓；✗ 路径不再印在更新页的卡片里 ✗。</summary>
        public static string PathsText()
        {
            StringBuilder sb = new StringBuilder();
            string dir = null;
            try
            {
                string exe = System.Reflection.Assembly.GetEntryAssembly() == null ? "" : System.Reflection.Assembly.GetEntryAssembly().Location;
                if (!string.IsNullOrEmpty(exe))
                    dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(exe), "plugin", "dsh-minato-bridge");
            }
            catch { }
            if (string.IsNullOrEmpty(dir)) sb.Append("工具箱自带的那一份基准：读不到本程序自己的位置（下面的候选位置仍然可用）。");
            else if (System.IO.Directory.Exists(dir)) sb.Append("工具箱自带的那一份基准：").Append(dir);
            else sb.Append("工具箱自带的那一份基准：").Append(dir).Append("（这份构建旁边没有它 —— 源码编译、单独复制 exe 属正常；官方发布包会带）");
            List<string> cands = null;
            try { cands = VersionMarkers.BridgeCandidates(null); } catch { }
            if (cands == null || cands.Count == 0)
            {
                sb.Append('\n').Append("本机候选位置：一个都没列出来（读不到数据根）。");
                return sb.ToString();
            }
            sb.Append('\n').Append("本机去看过的候选位置（按序）：");
            for (int i = 0; i < cands.Count; i++) sb.Append('\n').Append(cands[i]);
            return sb.ToString();
        }

        /// <summary>说明页上那一节的**小标题** ✓（同时也是"这段文字到底搬去哪了"的答案 ✓）。</summary>
        public static string Title() { return "插件更新的基准与判定"; }

        /// <summary>路径那一节的**小标题** ✓。</summary>
        public static string PathsTitle() { return "插件基准的完整路径（本机实读）"; }
    }
}
