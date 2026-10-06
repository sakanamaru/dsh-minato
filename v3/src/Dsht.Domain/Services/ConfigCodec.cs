using System;
using System.Text;
using Dsht.Domain.Model;

namespace Dsht.Domain.Services
{
    /// <summary>launcher.config 的解析与序列化（纯函数）。逐条对齐 v2.x 的 LoadConfig / SaveConfig：
    ///   · 每行 key=value；未知键忽略
    ///   · lang ∈ {zh,en,auto}；host ∈ {localhost,127.0.0.1}；keep_backups 数字且夹到 ≥3
    ///   · check_update / check_dsh_update / auto_start：任意非空值 → 关掉除非值为 "off"
    ///   · update_channel："rc" 否则 stable；close_action ∈ {ask,tray,exit} 否则空
    ///   · ws：交给注入的 canonicalize 委托（平台侧用真实路径规范化，失败则视为无效）
    /// 序列化固定键顺序 + CRLF，与 v2.x 一致（便于比对）。</summary>
    public static class ConfigCodec
    {
        public static ToolkitConfig Parse(string text, Func<string, string> canonicalizePath)
        {
            ToolkitConfig c = new ToolkitConfig();
            if (string.IsNullOrEmpty(text)) return c;
            string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string t = (lines[i] ?? "").Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith("lang=", StringComparison.Ordinal))
                {
                    string v = t.Substring(5).Trim().ToLowerInvariant();
                    c.Lang = (v == "zh" || v == "en") ? v : "auto";
                }
                else if (t.StartsWith("host=", StringComparison.Ordinal))
                {
                    string v = t.Substring(5).Trim().ToLowerInvariant();
                    if (v == "localhost" || v == "127.0.0.1") c.Host = v;
                }
                else if (t.StartsWith("ws=", StringComparison.Ordinal))
                {
                    string v = t.Substring(3).Trim().Trim('"');
                    if (v.Length > 0) c.Workspace = Canon(canonicalizePath, v);
                }
                else if (t.StartsWith("keep_backups=", StringComparison.Ordinal))
                {
                    int n;
                    if (int.TryParse(t.Substring(13).Trim(), out n)) c.KeepBackups = n < 3 ? 3 : n;
                }
                else if (t.StartsWith("check_update=", StringComparison.Ordinal))
                {
                    string v = t.Substring(13).Trim().ToLowerInvariant();
                    if (v.Length > 0) c.CheckUpdate = v != "off";
                }
                else if (t.StartsWith("check_dsh_update=", StringComparison.Ordinal))
                {
                    string v = t.Substring(17).Trim().ToLowerInvariant();
                    if (v.Length > 0) c.CheckDshUpdate = v != "off";
                }
                else if (t.StartsWith("dsh_versions=", StringComparison.Ordinal))
                {
                    string v = t.Substring(13).Trim();
                    if (v.Length > 0) c.DshVersions = v;
                }
                else if (t.StartsWith("update_channel=", StringComparison.Ordinal))
                {
                    string v = t.Substring(15).Trim().ToLowerInvariant();
                    c.UpdateChannel = v == "rc" ? "rc" : "stable";
                }
                else if (t.StartsWith("close_action=", StringComparison.Ordinal))
                {
                    string v = t.Substring(13).Trim().ToLowerInvariant();
                    c.CloseAction = (v == "tray" || v == "exit") ? v : (v == "ask" ? "ask" : "");
                }
                else if (t.StartsWith("auto_start=", StringComparison.Ordinal))
                {
                    string v = t.Substring(11).Trim().ToLowerInvariant();
                    if (v.Length > 0) c.AutoStart = v != "off";
                }
            // ✗ 上次写成 auto_start= 分支的 else → 只有 auto_start= 为空时才读 ✗ 永远读不到 ✓（CLI 的回读校验抓到 ✓✓）
            else if (t.StartsWith("auto_start_target=", StringComparison.Ordinal)) c.AutoStartTarget = t.Substring("auto_start_target=".Length).Trim();
            else if (t.StartsWith("browser_mode=", StringComparison.Ordinal)) c.BrowserMode = t.Substring("browser_mode=".Length).Trim();
            else if (t.StartsWith("ui_parallel=", StringComparison.Ordinal)) c.UiParallel = t.Substring("ui_parallel=".Length).Trim() != "off";
            else if (t.StartsWith("scan_children=", StringComparison.Ordinal)) c.ScanChildren = t.Substring("scan_children=".Length).Trim() != "off";
            else if (t.StartsWith("gui_start_page=", StringComparison.Ordinal))
            {
                // 与白名单同一条规则 ✓：0..9 之外不认（保持默认 ✓ 不猜 ✗）——旧配置文件没有这行 → 默认 1 ✓
                int n;
                if (int.TryParse(t.Substring("gui_start_page=".Length).Trim(), out n) && n >= 0 && n <= 9) c.GuiStartPage = n;
            }
            else if (t.StartsWith("gui_auto_refresh=", StringComparison.Ordinal))
            {
                // 与白名单同一条规则 ✓：off / 空 = 暂停 ✓；0.5–3600 秒之外不认（保持默认 off ✓）
                string v2 = t.Substring("gui_auto_refresh=".Length).Trim();
                double sec2;
                if (v2.Length == 0 || v2 == "off") c.GuiAutoRefresh = "off";
                else if (double.TryParse(v2, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sec2) && sec2 >= 0.5 && sec2 <= 3600) c.GuiAutoRefresh = v2;
            }
            else if (t.StartsWith("gui_shell=", StringComparison.Ordinal))
            {
                // 与白名单同一条规则 ✓：0..4 之外不认（保持默认 ✓ 不猜 ✗）
                int n;
                if (int.TryParse(t.Substring("gui_shell=".Length).Trim(), out n) && n >= 0 && n <= 4) c.GuiShell = n;
            }
            else if (t.StartsWith("gui_style=", StringComparison.Ordinal))
            {
                int n;
                if (int.TryParse(t.Substring("gui_style=".Length).Trim(), out n) && n >= 0 && n <= 3) c.GuiStyle = n;
            }
            else if (t.StartsWith("balance_key=", StringComparison.Ordinal)) c.BalanceKey = t.Substring("balance_key=".Length).Trim();
            }
            return c;
        }

        public static string Serialize(ToolkitConfig c)
        {
            if (c == null) c = new ToolkitConfig();
            StringBuilder sb = new StringBuilder();
            sb.Append("lang=").Append(c.Lang).Append("\r\n");
            sb.Append("host=").Append(c.Host).Append("\r\n");
            sb.Append("ws=").Append(c.Workspace == null ? "" : c.Workspace).Append("\r\n");
            sb.Append("keep_backups=").Append(c.KeepBackups).Append("\r\n");
            sb.Append("check_update=").Append(c.CheckUpdate ? "on" : "off").Append("\r\n");
            sb.Append("check_dsh_update=").Append(c.CheckDshUpdate ? "on" : "off").Append("\r\n");
            sb.Append("dsh_versions=").Append(c.DshVersions).Append("\r\n");
            sb.Append("update_channel=").Append(c.UpdateChannel).Append("\r\n");
            sb.Append("close_action=").Append(c.CloseAction).Append("\r\n");
            sb.Append("auto_start=").Append(c.AutoStart ? "on" : "off").Append("\r\n");
            sb.Append("auto_start_target=").Append(c.AutoStartTarget).Append("\r\n");
            sb.Append("browser_mode=").Append(c.BrowserMode).Append("\r\n");
            sb.Append("ui_parallel=").Append(c.UiParallel ? "on" : "off").Append("\r\n");
            sb.Append("scan_children=").Append(c.ScanChildren ? "on" : "off").Append("\r\n");
            sb.Append("gui_start_page=").Append(c.GuiStartPage).Append("\r\n");
            sb.Append("gui_auto_refresh=").Append(c.GuiAutoRefresh).Append("\r\n");
            sb.Append("gui_shell=").Append(c.GuiShell).Append("\r\n");
            sb.Append("gui_style=").Append(c.GuiStyle).Append("\r\n");
            sb.Append("balance_key=").Append(c.BalanceKey == null ? "" : c.BalanceKey).Append("\r\n");
            return sb.ToString();
        }

        private static string Canon(Func<string, string> f, string v)
        {
            if (f == null) return v;
            try { return f(v); } catch { return null; }
        }
    }
}