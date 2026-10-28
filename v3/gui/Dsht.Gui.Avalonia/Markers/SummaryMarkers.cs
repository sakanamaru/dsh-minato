using System;
using System.Collections.Generic;

namespace Dsht.Gui.Avalonia.Markers
{
    /// <summary>体检摘要（`doctor` 的 DOCTOR_OK/WARN/ERROR 与分级条目行）。</summary>
    public sealed class DoctorSummary
    {
        public bool Ok;
        public int Pass;
        public int Warn;
        public int Error;
        public List<string> WarnLines = new List<string>();
        public List<string> ErrorLines = new List<string>();
        public string Headline
        {
            get
            {
                if (!Ok) return "未运行体检";
                if (Error > 0) return "发现 " + Error + " 个错误";
                if (Warn > 0) return "发现 " + Warn + " 个提醒";
                return "一切正常";
            }
        }
    }

    /// <summary>备份摘要（`backup-list` 的 BACKUP_LIST_OK 与路径行）。</summary>
    public sealed class BackupSummary
    {
        public bool Ok;
        public int Count;
        // N15 FIX: a `Latest` field used to live here, parsed from the first BACKUP_ITEM line and
    }

    public static class SummaryMarkers
    {
        public static DoctorSummary ParseDoctor(string output)
        {
            DoctorSummary d = new DoctorSummary();
            if (string.IsNullOrEmpty(output)) return d;
            string[] lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] == null ? "" : lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("DOCTOR_OK", StringComparison.Ordinal)) { d.Ok = true; continue; }
                if (line.StartsWith("DOCTOR_WARN", StringComparison.Ordinal)) { d.Ok = true; continue; }   // 标记行只表示"体检跑过"，计数以条目行为准
                if (line.StartsWith("DOCTOR_ERROR", StringComparison.Ordinal)) { d.Ok = true; continue; }
                if (line.StartsWith("[错误]", StringComparison.Ordinal) || line.StartsWith("[ERROR", StringComparison.OrdinalIgnoreCase)) { d.Error++; if (d.ErrorLines.Count < 8) d.ErrorLines.Add(line); continue; }
                if (line.StartsWith("[提醒]", StringComparison.Ordinal) || line.StartsWith("[WARN", StringComparison.OrdinalIgnoreCase)) { d.Warn++; if (d.WarnLines.Count < 8) d.WarnLines.Add(line); continue; }
                if (line.StartsWith("[OK]", StringComparison.Ordinal) || line.StartsWith("[通过]", StringComparison.Ordinal)) { d.Pass++; continue; }
            }
            return d;
        }

        public static BackupSummary ParseBackups(string output)
        {
            BackupSummary b = new BackupSummary();
            if (string.IsNullOrEmpty(output)) return b;
            string[] lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] == null ? "" : lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("BACKUP_LIST_OK", StringComparison.Ordinal))
                {
                    b.Ok = true;
                    string rest = line.Substring("BACKUP_LIST_OK".Length).Trim();
                    int n;
                    if (int.TryParse(rest, out n)) b.Count = n;
                    continue;
                }
            }
            return b;
        }
    }
}
namespace Dsht.Gui.Avalonia.Markers
{
    /// <summary>一条备份（`backup-list --detail` 的 BACKUP_ITEM 行）。</summary>
    public sealed class BackupItem
    {
        public string Name = "";
        public string Kind = "Manual";
        /// <summary>F14 FIX: the CLI lists entries it cannot restore from and marks them. This
        /// lets the page show them as invalid instead of as ordinary backups.</summary>
        public bool Invalid = false;
        public long Bytes;
        public string Time = "";
        public string KindText { get { return Invalid ? "无效（不能用于恢复）" : (Kind == "Manual" ? "手动" : (Kind == "Auto" ? "自动" : Kind)); } }
        public string SizeText { get { return Invalid ? "—" : SessionRow.Human(Bytes); } }
    }

    /// <summary>一个配置项（`config-get` 的 CONFIG 行）。</summary>
    public sealed class ConfigItem
    {
        public string Key = "";
        public string Value = "";
        /// <summary>人话说明（治"这键是干什么的"）。</summary>
        public string Desc
        {
            get
            {
                switch (Key)
                {
                    case "lang": return "界面语言（auto / zh / en）";
                    case "host": return "dsh 监听地址（默认 127.0.0.1）";
                    case "ws": return "默认工作区目录（留空=自动探测）";
                    case "keep_backups": return "自动备份保留份数（最少 3）";
                    case "check_update": return "启动时检查工具箱更新";
                    case "check_dsh_update": return "启动时检查 dsh 更新";
                    case "update_channel": return "更新通道（stable / rc）";   // N16 FIX: the CLI accepts stable and rc, not beta
                    case "close_action": return "关闭窗口时的行为";
                    case "auto_start": return "启动时自动启动 dsh";
                    case "auto_start_target": return "开机自启启动什么（auto / desktop / web）";
                    case "dsh_versions": return "已安装的 dsh 版本（只读）";
                    // ★ 2026-10-02：排障开关与界面偏好的人话说明（设置页分类后按组显示 ✓）
                    case "browser_mode": return "浏览器打开方式（Linux 打不开时换）";
                    case "ui_parallel": return "切页卡顿时的排障开关：并行取数据";
                    case "scan_children": return "会话页提速：是否扫子代理归类";
                    case "gui_start_page": return "GUI 启动先开哪页（0=概览 … 9=日志）";
                    case "gui_auto_refresh": return "概览页自动刷新间隔（off / 秒数）";
                    case "balance_key": return "DeepSeek 平台 API key（余额检测；留空=未绑定 → 概览页不显示余额卡）";
                    // ★ 成本估算（2026-10-16 v3.2）：四个现成单价键 + 每日预算键（✗ 不新造键 ✓ 校验规则在后端 ✓）
                    case "sessions_price_in_per_mtok": return "输入单价（美元 / 百万 token；0=未填 → 不显示金额）";
                    case "sessions_price_out_per_mtok": return "输出单价（美元 / 百万 token；0=未填 → 不显示金额）";
                    case "sessions_price_cache_read_per_mtok": return "缓存读单价（美元 / 百万 token；0=未填）";
                    case "sessions_price_cache_write_per_mtok": return "缓存写单价（美元 / 百万 token；0=未填）";
                    case "sessions_budget_usd_per_day": return "每日预算上限（美元 / 天；0 或留空=不设上限，只在看板提示）";
                    // ★ 悬边状态灯悬浮窗（2026-10-26 · 提案 §11 ✓）：六个纯 GUI 偏好键 ✓
                    case "overlay_enabled": return "置顶进度窗：屏幕边缘的状态灯（默认关）";
                    case "overlay_light_style": return "悬边灯样式（line=极简细条 / dot=圆点 + 轻环（默认）/ capsule=双端胶囊）";
                    case "overlay_edge": return "状态灯贴哪一边（left / right / top / bottom）";
                    case "overlay_offset": return "状态灯在那条边上的位置（0~1；0.5=居中）";
                    case "overlay_scale": return "状态灯尺寸档（0=紧凑 / 1=标准 / 2=宽松）";
                    case "overlay_screen": return "记住的显示器（留空=主屏；拔掉自动回主屏）";
                    case "overlay_show_title": return "列表里是否显示会话标题（默认关；标题由模型从对话内容派生）";
                    default: return "";
                }
            }
        }
        public bool ReadOnly { get { return Key == "dsh_versions"; } }
        /// <summary>成本估算相关的键（单价四件 + 每日预算）——设置页据此给输入框加水印/说明 ✓ 界面只做引导 ✗ 不改校验规则 ✓。</summary>
        public static bool IsCostKey(string key)
        {
            return key == "sessions_price_in_per_mtok" || key == "sessions_price_out_per_mtok"
                || key == "sessions_price_cache_read_per_mtok" || key == "sessions_price_cache_write_per_mtok"
                || key == "sessions_budget_usd_per_day";
        }
        /// <summary>on/off 型配置 → 渲染成两枚按钮 ✓（2026-10-02 补上 ui_parallel / scan_children ✓ 原来它们被当自由文本 ✗；
        /// 2026-10-26 补上 overlay_enabled / overlay_show_title ✓ 同一处，✗ 不再各写一份 ✗）。</summary>
        public bool IsSwitch { get { return Key == "check_update" || Key == "check_dsh_update" || Key == "auto_start" || Key == "ui_parallel" || Key == "scan_children" || Key == "overlay_enabled" || Key == "overlay_show_title"; } }
    }

    /// <summary>DeepSeek 余额（`balance` 命令的 BALANCE_* 行）✓ 2026-10-02 用户要求 ✓。
    /// 诚实边界：`unbound` → GUI **整卡隐藏** ✓（用户要求"未绑定隐藏" ✓）；
    /// 取不到 → Unavailable 带原因 ✗ 绝不冒充数字 ✓✓。</summary>
    public sealed class BalanceSummary
    {
        public bool Ok;                     // 命令跑过（有 BALANCE_STATE 行 ✓）
        public bool Bound;                  // bound = 绑了 key ✓
        public string Currency = "";
        public string Topup = "";           // 充值余额 ✓（BALANCE_TOPUP ✓）
        public string Granted = "";         // 活动赠送余额 ✓（BALANCE_GRANTED ✓）
        public string Total = "";           // 总计 ✓
        public string Note = "";            // BALANCE_NOTE（账户不可用等 ✓ 如实转述 ✓）
        public string Unavailable = "";    // BALANCE_UNAVAILABLE 原因 ✓（网络/key ✗ ≠ 0 ✓✓）
        public bool HasNumbers { get { return Topup.Length > 0 || Granted.Length > 0 || Total.Length > 0; } }
    }

    public static class BalanceMarkers
    {
        public static BalanceSummary Parse(string output)
        {
            BalanceSummary b = new BalanceSummary();
            if (string.IsNullOrEmpty(output)) return b;
            string[] lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] == null ? "" : lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("BALANCE_STATE bound", StringComparison.Ordinal)) { b.Ok = true; b.Bound = true; continue; }
                if (line.StartsWith("BALANCE_STATE unbound", StringComparison.Ordinal)) { b.Ok = true; b.Bound = false; continue; }
                if (line.StartsWith("BALANCE_CURRENCY ", StringComparison.Ordinal)) { b.Currency = line.Substring("BALANCE_CURRENCY ".Length).Trim(); continue; }
                if (line.StartsWith("BALANCE_TOPUP ", StringComparison.Ordinal)) { b.Topup = line.Substring("BALANCE_TOPUP ".Length).Trim(); continue; }
                if (line.StartsWith("BALANCE_GRANTED ", StringComparison.Ordinal)) { b.Granted = line.Substring("BALANCE_GRANTED ".Length).Trim(); continue; }
                if (line.StartsWith("BALANCE_TOTAL ", StringComparison.Ordinal)) { b.Total = line.Substring("BALANCE_TOTAL ".Length).Trim(); continue; }
                if (line.StartsWith("BALANCE_NOTE ", StringComparison.Ordinal)) { b.Note = line.Substring("BALANCE_NOTE ".Length).Trim(); continue; }
                if (line.StartsWith("BALANCE_UNAVAILABLE ", StringComparison.Ordinal)) { b.Unavailable = line.Substring("BALANCE_UNAVAILABLE ".Length).Trim(); continue; }
            }
            return b;
        }
    }

    public static class ConfigMarkers
    {
        public static List<ConfigItem> Parse(string output)
        {
            List<ConfigItem> list = new List<ConfigItem>();
            if (string.IsNullOrEmpty(output)) return list;
            string[] lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] == null ? "" : lines[i].Trim();
                if (!line.StartsWith("CONFIG ", StringComparison.Ordinal)) continue;
                string rest = line.Substring("CONFIG ".Length);
                int sp = rest.IndexOf(' ');
                ConfigItem c = new ConfigItem();
                if (sp < 0) { c.Key = rest; }
                else { c.Key = rest.Substring(0, sp); c.Value = rest.Substring(sp + 1).Trim(); }
                list.Add(c);
            }
            return list;
        }
    }

    public static class BackupItems
    {
        public static List<BackupItem> Parse(string output)
        {
            List<BackupItem> list = new List<BackupItem>();
            if (string.IsNullOrEmpty(output)) return list;
            string[] lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] == null ? "" : lines[i].Trim();
                if (line.StartsWith("BACKUP_ITEM_INVALID ", StringComparison.Ordinal))
                {
                    string badName = line.Substring("BACKUP_ITEM_INVALID ".Length).Trim();
                    if (badName.Length > 0)
                    {
                        BackupItem bad = new BackupItem();
                        bad.Name = badName;
                        bad.Kind = "Invalid";
                        bad.Invalid = true;
                        list.Add(bad);
                    }
                    continue;
                }
                if (!line.StartsWith("BACKUP_ITEM ", StringComparison.Ordinal)) continue;
                string[] p = line.Substring("BACKUP_ITEM ".Length).Split(new char[] { ' ' }, 4);
                if (p.Length < 1) continue;
                BackupItem b = new BackupItem();
                // ★★ 架构审计抓到（MAJOR）：CLI 那边**没有转义** ✗ 而这里按空格切 ✗
                //   → 备份目录名含空格（两个平台都合法 ✓）时会被切碎 ✗
                //     → 恢复/预览/删除会作用在**截断后的名字**上 ✗✗
                // ✓ 现在：CLI 侧已转义 ✓ 这里**解码** ✓✓（与 SESSION 的处理一致 ✓）
                b.Name = SessionsMarkers.Decode(p[0]);
                if (p.Length > 1) b.Kind = p[1];
                long n; if (p.Length > 2 && long.TryParse(p[2], out n)) b.Bytes = n;
                if (p.Length > 3) b.Time = p[3].Trim();
                list.Add(b);
            }
            return list;
        }
    }
}