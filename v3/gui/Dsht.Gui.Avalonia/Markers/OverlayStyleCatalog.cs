using System;
using Dsht.Gui.Avalonia.Design;

namespace Dsht.Gui.Avalonia.Markers
{
    /// <summary>★ 悬边灯样式「三选一」的**用户可见文案与取值映射**唯一出处（2026-10-28）。
    ///
    /// <para>【它管什么】配置键 <c>overlay_light_style</c> 的三个语义取值
    /// （<c>line</c> = A 极简细条 · <c>dot</c> = B 圆点 + 轻环（默认）· <c>capsule</c> = C 双端胶囊）
    /// 在设置页显示成什么名字、哪一句人话、哪个是默认、当前选的是哪个 ✓。</para>
    ///
    /// <para>【它不管什么】✗ 不画灯 ✗ 不取色 ✗ 不读配置 ✗ 不碰 IO ✗：
    /// 形态几何走 <see cref="DesignTokens.EdgeLampOf"/> ✓ 画法走 <c>StatusLight.Create</c> ✓
    /// 配置值来自 CLI 的 <c>CONFIG overlay_light_style</c> ✓（设置页与悬浮窗读**同一个键** ✓
    /// ✗ 没有第二套状态 ✗）。</para>
    ///
    /// <para>本文件是**纯文案 + 纯函数**（不依赖 Avalonia ⇒ GUI 逻辑测试直接钉死 ✓
    /// 改坏即红：键盘名/默认标记/回落提示都在这几张常量里 ✓）。</para></summary>
    public static class OverlayStyleCatalog
    {
        /// <summary>配置键名（与 CLI 白名单逐字一致 ✓）。</summary>
        public const string Key = "overlay_light_style";

        /// <summary>三个语义取值（顺序 = A/B/C，设置页按它排 ✓ 与 <see cref="DesignTokens.LampStyleKeys"/> 同一份）。</summary>
        public static readonly string[] Keys = DesignTokens.LampStyleKeys;

        /// <summary>默认取值（B 圆点 + 轻环 ✓ 只有一个地方写"默认是哪个"）。</summary>
        public const string DefaultKey = DesignTokens.DefaultLampStyleKey;

        // —— 用户可见文案（零 ✓/✗ 记号、零施工语气 ✓）——

        /// <summary>设置页这一组的标题。</summary>
        public const string SectionTitle = "悬边灯样式";

        /// <summary>一句话说清"换样式不会牵动什么"。</summary>
        public const string SectionNote = "三种画法共用同一套颜色含义、优先级与点击穿透；只换灯的形态，选完立刻生效。";

        /// <summary>默认款的角标（每一行都显示，和"当前使用"分开 —— 用户换到别的样式后仍看得出哪个是默认）。</summary>
        public const string DefaultBadge = "默认";

        /// <summary>当前选中的那一款的角标。</summary>
        public const string CurrentBadge = "当前使用";

        /// <summary>标题右侧的"当前：…"前缀。</summary>
        public const string CurrentPrefix = "当前：";

        /// <summary>旧版 CLI 还没有这个键时的如实说明（✗ 不假装可选 ✗）。</summary>
        public const string StaleCliHint = "当前这个 CLI 还没有悬边灯样式键：升级后这里就能选。";

        /// <summary>选项名（带 A/B/C 前缀 ✓ 与灯样式名一致）。</summary>
        public static string NameOf(string key)
        {
            if (key == "line") return "A · 极简细条";
            if (key == "capsule") return "C · 双端胶囊";
            return "B · 圆点 + 轻环";
        }

        /// <summary>不带字母的短名（用于"当前：…"一行 ✓）。</summary>
        public static string ShortNameOf(string key)
        {
            if (key == "line") return "极简细条";
            if (key == "capsule") return "双端胶囊";
            return "圆点 + 轻环";
        }

        /// <summary>一句人话（名称 + 取舍；默认那一款自己写明"默认"，与角标不矛盾）。</summary>
        public static string BlurbOf(string key)
        {
            if (key == "line") return "极简细条 · 最不打扰";
            if (key == "capsule") return "双端胶囊 · 保留胶囊形态";
            return "圆点 + 轻环（默认）· 最好点中";
        }

        /// <summary>这一款是不是**默认款**（与用户当前选哪个无关 ✓）。</summary>
        public static bool IsDefault(string key)
        {
            return NormalizeKey(key) == DefaultKey;
        }

        /// <summary>这个值是不是三个语义取值之一（✗ 不认 a/b/c 位置代号 ✗）。</summary>
        public static bool IsValidKey(string raw)
        {
            return DesignTokens.IsLampStyleKey(raw);
        }

        /// <summary>归一化：合法值 → 小写语义键；其它/空 ⇒ 默认款（✗ 不猜 ✗）。
        /// ★ 与 CLI 的回落规则同一条：非法值回落默认 ✓ 区别只在"提示"由 CLI 的 CONFIGWARN 行给出 ✓。</summary>
        public static string NormalizeKey(string raw)
        {
            return IsValidKey(raw) ? raw.Trim().ToLowerInvariant() : DefaultKey;
        }

        /// <summary>语义键 → 画法变体（设置页预览与悬边灯**同一个映射** ✓ 选中的就是看到的 ✓）。</summary>
        public static DesignTokens.LampVariant VariantOf(string key)
        {
            return DesignTokens.ParseLampStyle(key);
        }

        /// <summary>"当前：…"一行的文案。</summary>
        public static string CurrentText(string key)
        {
            return CurrentPrefix + ShortNameOf(NormalizeKey(key));
        }

        /// <summary>★ 从 `config-get` 原文里取 CLI 给的**回落提示**（原样转述 ✓ 不在界面里另写一遍 ✗）。
        /// <para>只认 `CONFIGWARN overlay_light_style &lt;提示&gt;` 这一行（CLI 在配置原文的值非法时打 ✓）；
        /// 没有 ⇒ 空串 ✓（✗ 不编一句"可能有问题" ✗）。</para></summary>
        public static string WarningFromOutput(string cliOutput)
        {
            if (string.IsNullOrEmpty(cliOutput)) return "";
            string[] ls = cliOutput.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            const string prefix = "CONFIGWARN " + Key + " ";
            for (int i = 0; i < ls.Length; i++)
            {
                string t = (ls[i] == null ? "" : ls[i]).Trim();
                if (t.StartsWith(prefix, StringComparison.Ordinal)) return t.Substring(prefix.Length).Trim();
            }
            return "";
        }

        /// <summary>设置项在不在（旧 CLI 没有这个键 ⇒ 只给如实说明 ✓ 不装作能选 ✗）。</summary>
        public static bool Present(string cliOutput)
        {
            if (string.IsNullOrEmpty(cliOutput)) return false;
            string[] ls = cliOutput.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            const string prefix = "CONFIG " + Key + " ";
            for (int i = 0; i < ls.Length; i++)
            {
                string t = (ls[i] == null ? "" : ls[i]).Trim();
                if (t.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>从 `config-get` 原文里取当前值（取不到 ⇒ 默认 ✓ 并配合 <see cref="Present"/> 判定是否旧版 ✓）。</summary>
        public static string ValueFromOutput(string cliOutput)
        {
            if (string.IsNullOrEmpty(cliOutput)) return DefaultKey;
            string[] ls = cliOutput.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            const string prefix = "CONFIG " + Key + " ";
            for (int i = 0; i < ls.Length; i++)
            {
                string t = (ls[i] == null ? "" : ls[i]).Trim();
                if (t.StartsWith(prefix, StringComparison.Ordinal)) return NormalizeKey(t.Substring(prefix.Length).Trim());
            }
            return DefaultKey;
        }
    }
}
