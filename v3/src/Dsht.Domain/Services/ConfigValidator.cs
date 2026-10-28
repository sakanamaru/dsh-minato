using System;
using Dsht.Domain.Model;

namespace Dsht.Domain.Services
{
    /// <summary>config-set 校验（纯函数，与 v2.x 的 NIValidateConfigSet 逐条对齐）：
    /// 返回 null=通过；否则原因键 no-key / unknown-key / bad-value。ws 用注入的规范化委托判定。</summary>
    public static class ConfigValidator
    {
        /// <summary>回读校验 ✓：把 val 应用到 cfg 后**配置是否没有变化** —— 是则说明配置里已经是请求的值 ✓。
        /// 用途：config-set 的"写入真的生效了吗"判定（写盘可能静默失败 ✗，实测曾报 OK 而文件没变 ✗✗）。
        /// 判据是"存储状态 == 请求状态" ✓，不是"我调用过写盘" ✗；用序列化比较（纯函数 ✓ 无需逐字段 ✓）。
        /// 空值/未知键由 Validate 先行拦截 ✓，这里只管"是否已生效" ✓。</summary>
        public static bool SameValue(ToolkitConfig cfg, string key, string value, Func<string, string> canonicalizePath)
        {
            if (cfg == null) return false;
            try
            {
                ToolkitConfig after = ApplyTo(cfg, key, value, canonicalizePath);
                return ConfigCodec.Serialize(after) == ConfigCodec.Serialize(cfg);
            }
            catch { return false; }
        }
        public static string Validate(string key, string value, Func<string, string> canonicalizePath)
        {
            if (string.IsNullOrWhiteSpace(key)) return "no-key";
            string k = key.Trim().ToLowerInvariant();
            string v = (value == null ? "" : value).Trim();
            if (k == "lang") return (v == "zh" || v == "en" || v == "auto" || v == "") ? null : "bad-value";
            if (k == "host") return (v == "127.0.0.1" || v == "localhost") ? null : "bad-value";
            if (k == "ws")
            {
                if (v.Length == 0) return null;
                if (canonicalizePath == null) return null;
                try { canonicalizePath(v); return null; } catch { return "bad-value"; }
            }
            if (k == "keep_backups")
            {
                int n;
                return (int.TryParse(v, out n) && n >= 3) ? null : "bad-value";
            }
            if (k == "check_update" || k == "check_dsh_update") return (v == "on" || v == "off") ? null : "bad-value";
            if (k == "update_channel") return (v == "stable" || v == "rc") ? null : "bad-value";
            if (k == "close_action") return (v.Length == 0 || v == "ask" || v == "tray" || v == "exit") ? null : "bad-value";
            if (k == "auto_start") return (v == "on" || v == "off") ? null : "bad-value";
            // ✗ 上次只加到了 ApplyTo ✓ 漏了这份**白名单** → config-set 报 unknown-key ✗（实测抓到 ✓）
            if (k == "auto_start_target") return (v == "auto" || v == "desktop" || v == "web") ? null : "bad-value";
            if (k == "browser_mode") return (v == "auto" || v == "snap" || v == "direct" || v == "xdg") ? null : "bad-value";
            if (k == "ui_parallel" || k == "scan_children") return (v == "on" || v == "off") ? null : "bad-value";
            // ★ GUI 启动页（2026-10-02 ✓）：值是 NavItems 的索引 → 只认 0..9 ✓（越界一律拒绝 ✗ 不猜 ✗）
            if (k == "gui_start_page")
            {
                int n;
                return (int.TryParse(v, out n) && n >= 0 && n <= 9) ? null : "bad-value";
            }
            // ★ 概览自动刷新（2026-10-02 ✓）：off / 空 = 暂停 ✓；或秒数 0.5–3600 ✓（越界拒绝 ✗ 不猜 ✗）
            if (k == "gui_auto_refresh")
            {
                if (v.Length == 0 || v == "off") return null;
                double sec;
                return (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sec) && sec >= 0.5 && sec <= 3600) ? null : "bad-value";
            }
            // ★ GUI 布局/风格偏好（2026-10-06）：范围与 Shells/Palette 一致 ✓ 越界拒绝 ✗ 不猜 ✗
            if (k == "gui_shell") { int n; return (int.TryParse(v, out n) && n >= 0 && n <= 4) ? null : "bad-value"; }
            if (k == "gui_style") { int n; return (int.TryParse(v, out n) && n >= 0 && n <= 3) ? null : "bad-value"; }
            // ★ DeepSeek 余额检测的 key（2026-10-02 ✓）：自由文本，空 = 未绑定 ✓；不猜格式 ✗（明文本地存储 ✓ 见 CONFIGNOTE ✓）
            if (k == "balance_key")
            {
                // 审查修复（安全 Low）：自由文本键不能含换行 ✗ —— 否则可往 launcher.config 注入任意配置行 ✓
                return (v.IndexOf('\r') >= 0 || v.IndexOf('\n') >= 0) ? "bad-value" : null;
            }
            // ★ 看板第一批（2026-10-08 ✓）：口径默认三选一 ✓（第一批只实现 global ✓ 但存 parents/parents_sub 是合法配置 ✓）
            if (k == "sessions_default_level") return (v == "global" || v == "parents" || v == "parents_sub") ? null : "bad-value";
            // ★ 单价键（费用显示是第三批 ✓ 现在只落存储 ✓）：≥0 的十进制 ✓ 0 = 未填 → 不显示费用 ✓ 绝不硬编码价格 ✗✓
            if (k == "sessions_price_in_per_mtok" || k == "sessions_price_cache_read_per_mtok" || k == "sessions_price_cache_write_per_mtok" || k == "sessions_price_out_per_mtok")
            {
                decimal pd;
                return (decimal.TryParse(v, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out pd) && pd >= 0m) ? null : "bad-value";
            }
            // ★ 每日预算上限（成本面板 v3.2 · 2026-10-16 ✓）：与单价键**同一条规则**（≥0 的十进制 ✓ 0 = 未设 ✓）
            if (k == "sessions_budget_usd_per_day")
            {
                decimal bd;
                return (decimal.TryParse(v, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out bd) && bd >= 0m) ? null : "bad-value";
            }
            // ——— 悬边状态灯悬浮窗（2026-10-26 · 提案 §11 ✓）：六个键 ✓ 白名单与 ApplyTo 双保险 ✓ ———
            if (k == "overlay_enabled" || k == "overlay_show_title") return (v == "on" || v == "off") ? null : "bad-value";
            if (k == "overlay_edge") return (v == "left" || v == "right" || v == "top" || v == "bottom") ? null : "bad-value";
            // ★ 悬边灯样式（2026-10-28 ✓）：只认三个**语义值** line / dot / capsule ✓
            //   （✗ 不收 a/b/c 位置代号 ✗ —— 配置里看到 a/b/c 就是非法值 ⇒ 拒绝写入 ✓）
            if (k == "overlay_light_style") return ConfigCodec.IsOverlayLightStyle(v) ? null : "bad-value";
            if (k == "overlay_offset")
            {
                decimal off;
                return (decimal.TryParse(v, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out off) && off >= 0m && off <= 1m) ? null : "bad-value";
            }
            if (k == "overlay_scale") { int n; return (int.TryParse(v, out n) && n >= 0 && n <= 2) ? null : "bad-value"; }
            // ★ 显示器标识是自由文本（`DisplayName` 可能是 `\\.\DISPLAY1` 这种 ✓ 空 = 主屏 ✓）；
            //   与 balance_key 同一条安全规则：**不许含换行** ✗（否则可往 launcher.config 注入任意配置行 ✓）
            if (k == "overlay_screen") return (v.IndexOf('\r') >= 0 || v.IndexOf('\n') >= 0) ? "bad-value" : null;
            return "unknown-key";
        }

        public static Dsht.Domain.Model.ToolkitConfig ApplyTo(Dsht.Domain.Model.ToolkitConfig c, string key, string value, Func<string, string> canonicalizePath)
        {
            if (c == null) c = new Dsht.Domain.Model.ToolkitConfig();
            string k = key.Trim().ToLowerInvariant();
            string v = (value == null ? "" : value).Trim();
            if (k == "lang") c.Lang = (v == "zh" || v == "en") ? v : "auto";
            else if (k == "host") c.Host = v;
            else if (k == "ws")
            {
                if (v.Length == 0) c.Workspace = null;
                else if (canonicalizePath != null) { try { c.Workspace = canonicalizePath(v); } catch { c.Workspace = null; } }
                else c.Workspace = v;
            }
            else if (k == "keep_backups") { int n; int.TryParse(v, out n); c.KeepBackups = n < 3 ? 3 : n; }
            else if (k == "check_update") c.CheckUpdate = v != "off";
            else if (k == "check_dsh_update") c.CheckDshUpdate = v != "off";
            else if (k == "update_channel") c.UpdateChannel = v == "rc" ? "rc" : "stable";
            else if (k == "close_action") c.CloseAction = (v == "tray" || v == "exit") ? v : (v == "ask" ? "ask" : "");
            else if (k == "auto_start") c.AutoStart = v != "off";
            else if (k == "auto_start_target") c.AutoStartTarget = v;
            else if (k == "browser_mode") c.BrowserMode = v;
            else if (k == "ui_parallel") c.UiParallel = v != "off";
            else if (k == "scan_children") c.ScanChildren = v != "off";
            else if (k == "gui_start_page") { int n; if (int.TryParse(v, out n) && n >= 0 && n <= 9) c.GuiStartPage = n; }   // 越界保持原值 ✓（Validate 已拦 ✓ 这里是双保险 ✓）
            else if (k == "gui_auto_refresh")
            {
                if (v.Length == 0 || v == "off") c.GuiAutoRefresh = "off";
                else
                {
                    double sec;
                    if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sec) && sec >= 0.5 && sec <= 3600)
                        c.GuiAutoRefresh = sec.ToString(System.Globalization.CultureInfo.InvariantCulture);   // 归一化保存（0.50 → 0.5 ✓）
                }
            }
            else if (k == "balance_key") c.BalanceKey = v;
            else if (k == "sessions_default_level") { if (v == "global" || v == "parents" || v == "parents_sub") c.SessionsDefaultLevel = v; }   // 白名单双保险 ✓
            else if (k == "sessions_price_in_per_mtok" || k == "sessions_price_cache_read_per_mtok" || k == "sessions_price_cache_write_per_mtok" || k == "sessions_price_out_per_mtok")
            {
                decimal pd;
                if (decimal.TryParse(v, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out pd) && pd >= 0m)
                {
                    if (k == "sessions_price_in_per_mtok") c.SessionsPriceInPerMTok = pd;
                    else if (k == "sessions_price_cache_read_per_mtok") c.SessionsPriceCacheReadPerMTok = pd;
                    else if (k == "sessions_price_cache_write_per_mtok") c.SessionsPriceCacheWritePerMTok = pd;
                    else c.SessionsPriceOutPerMTok = pd;
                }   // 越界/畸形保持原值 ✓（Validate 已拦 ✓ 双保险 ✓）
            }
            else if (k == "sessions_budget_usd_per_day")
            {
                decimal bd;
                if (decimal.TryParse(v, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out bd) && bd >= 0m)
                    c.SessionsBudgetUsdPerDay = bd;   // 0 = 未设 ✓（与四个单价同语义 ✓）
            }
            else if (k == "gui_shell") { int n; if (int.TryParse(v, out n) && n >= 0 && n <= 4) c.GuiShell = n; }   // 越界保持原值 ✓（Validate 已拦 ✓ 双保险 ✓）
            else if (k == "gui_style") { int n; if (int.TryParse(v, out n) && n >= 0 && n <= 3) c.GuiStyle = n; }
            // —— 悬边状态灯悬浮窗（2026-10-26 · 提案 §11 ✓）——
            else if (k == "overlay_enabled") c.OverlayEnabled = v != "off";
            // ★ 悬边灯样式：只存归一化后的三个语义值 ✓（越界/畸形保持原值 ✓ Validate 已拦 ✓ 双保险 ✓）
            else if (k == "overlay_light_style") { if (ConfigCodec.IsOverlayLightStyle(v)) c.OverlayLightStyle = v.Trim().ToLowerInvariant(); }
            else if (k == "overlay_edge") { if (v == "left" || v == "right" || v == "top" || v == "bottom") c.OverlayEdge = v; }
            else if (k == "overlay_offset")
            {
                decimal off;
                if (decimal.TryParse(v, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out off) && off >= 0m && off <= 1m)
                    c.OverlayOffset = off;   // 越界/畸形保持原值 ✓（Validate 已拦 ✓ 双保险 ✓）
            }
            else if (k == "overlay_scale") { int n; if (int.TryParse(v, out n) && n >= 0 && n <= 2) c.OverlayScale = n; }
            else if (k == "overlay_screen") c.OverlayScreen = v;
            else if (k == "overlay_show_title") c.OverlayShowTitle = v != "off";
            return c;
        }
    }
}