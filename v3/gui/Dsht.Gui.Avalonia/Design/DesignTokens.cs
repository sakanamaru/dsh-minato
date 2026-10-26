using System;
using System.Collections.Generic;
using System.Globalization;

namespace Dsht.Gui.Avalonia.Design
{
    /// <summary>★ 设计 token 的**唯一出处**（视觉规范化 · 2026-10-26 · 提案 §12 ✓✓）。
    ///
    /// <para>【本文件是什么】界面上所有**颜色值 / 间距 / 字号 / 层级 / 动效时长**都写在这里，
    /// 业务文件只许引用（<c>DesignTokens.…</c> / <c>Palette.…</c>）✗ 不许再出现字面量 ✗
    /// （有源码扫描断言盯着：`v3/gui` 下任何 `#RRGGBB` 只要不在本文件里就红 ✓ 可证伪 ✓）。</para>
    ///
    /// <para>【为什么纯 C#、不碰 Avalonia】本文件被 GUI 工程与 GUI 逻辑测试**同时**编译
    /// （`Dsht.Gui.LogicTests` 显式 `Compile Include`）⇒ 断言能在**无图形环境**里读它 ✓
    /// 因此这里只有 `string`/`double`/`int` 与自带的纯色math ✗ 不含任何 Avalonia 类型 ✗
    /// （画刷与控件在 <c>Design/StatusLight.cs</c> ✓）。</para>
    ///
    /// <para>【许可与署名】思路参考：**Radix Colors**（MIT · 语义色阶 + 明暗两套 ✓）、
    /// **Catppuccin**（MIT · 柔和暗色 ✓）、**Material Design 3 / Fluent 2**（间距 · 层级 · 状态色规则 ✓）、
    /// **Uptime Kuma / Gatus**（红黄绿灰的语义与文案 ✓）。
    /// ✗ 未复制其代码 / 图标 / 图片 / 字体 / 音效 ✗ 也**未直接采用其色值**：
    /// 本表色值全部是**本项目既有取值**（2026-10-10 样式重构时自定，四风格 A/B/C/D ✓）
    /// ⇒ 不构成衍生作品，无需附加许可与署名 ✓（若将来直接采编其色值 ⇒ 必须在此补许可与署名 ✗）。</para>
    ///
    /// <para>【与四风格的关系】四个风格（A 浅色卡片 / B 深色卡片 / C 深色紧凑 / D 浅色仪表盘）
    /// 各自一整套取值，**逐值与改动前完全一致** ✗ 本轮不动它们的观感 ✗（改前/改后截图对比 ✓）。
    /// 五个语义色另有**明暗两套基准**（<see cref="SemanticColor(Semantic, Mode)"/> ✓）——
    /// 风格可以偏离基准，但**必须在 <see cref="StyleSet.Overrides"/> 里登记** ✗ 否则断言红 ✓。</para></summary>
    public static class DesignTokens
    {
        // =====================================================================================
        //  ① 语义色 5 个（正常 / 注意 / 异常 / 未知 / 强调）
        // =====================================================================================

        /// <summary>语义色 5 个 ✗ 就这 5 个 ✗ 不许自造第 6 个（断言盯着 ✓）。
        /// 括号里是业界惯用名（Success / Warning / Error / Unknown / Accent ✓）——
        /// 我们**不用** Success/Error 作名字，因为"成功/失败"在本项目里另有诚实口径
        /// （未知 ≠ 失败 ✗ 见 <see cref="Unknown"/> ✓）。</summary>
        public enum Semantic
        {
            /// <summary>正常（Success）—— 绿。</summary>
            Normal = 0,
            /// <summary>注意（Warning）—— 黄/琥珀。</summary>
            Attention = 1,
            /// <summary>异常（Error）—— 红。</summary>
            Abnormal = 2,
            /// <summary>未知（Unknown）—— 中性灰 ✗ **不是失败** ✗ 也不许借"正常"的颜色替未知背书 ✗。</summary>
            Unknown = 3,
            /// <summary>强调（Accent）—— 各风格的主色（链接 / 激活 / 主按钮 ✓）。</summary>
            Accent = 4
        }

        /// <summary>明暗两套取值（跟随四风格：A/D 属 Light ✓ B/C 属 Dark ✓）。</summary>
        public enum Mode { Light = 0, Dark = 1 }

        /// <summary>五个语义色各有几个（断言用 ✓ 改坏即红）。</summary>
        public const int SemanticCount = 5;

        /// <summary>语义色枚举顺序（断言遍历用 ✓）。</summary>
        public static readonly Semantic[] Semantics =
        {
            Semantic.Normal, Semantic.Attention, Semantic.Abnormal, Semantic.Unknown, Semantic.Accent
        };

        /// <summary>★ 5 语义色 × 明暗两套的**基准表**（唯一一张 5×2 网格 ✓）。
        /// 索引：<c>SemanticGrid[(int)semantic, (int)mode]</c>；取值即 `#RRGGBB`。
        /// 基准取自本项目既有取值：Light = A 风格 ✓ Dark = B 风格 ✓（其余风格见 <see cref="Styles"/> 的覆盖登记）。</summary>
        private static readonly string[,] SemanticGrid =
        {
            //                          Light       Dark
            /* Normal    正常 */    { "#16A34A", "#3FB97F" },
            /* Attention 注意 */    { "#B45309", "#F0B35E" },
            /* Abnormal  异常 */    { "#DC2626", "#EF6A6A" },
            /* Unknown   未知 */    { "#8A919C", "#9AA3B2" },
            /* Accent    强调 */    { "#4D6BFE", "#7C8DFF" }
        };

        /// <summary>明暗基准取值（语义色 5 个 × 明暗 ✓ 缺一即红 ✓）。</summary>
        public static string SemanticColor(Semantic s, Mode m) { return SemanticGrid[(int)s, (int)m]; }

        // =====================================================================================
        //  ② 四风格（A/B/C/D）一整套取值 —— 与改动前逐值一致 ✗ 本轮不改观感 ✗
        // =====================================================================================

        /// <summary>一个风格的完整取值。字段名与 <c>Palette</c> 的对外字段一一对应 ✓
        /// （Palette 现在只做"把 token 变成画刷"✓ 不再自己藏色值 ✓）。</summary>
        public sealed class StyleSet
        {
            public string Key;          // A / B / C / D（配置 gui_style 的索引即数组下标 ✓）
            public string Name;         // 用户可见的风格名（与 Palette.StyleName 逐字一致 ✓）
            public Mode Mode;

            // 品牌 / 强调
            public string Accent, AccentHover, AccentSoft, OnAccent;
            // 表面层级（低 → 高）
            public string PageBg, SidebarBg, CardBg, CardHover, InsetBg;
            // 描边
            public string Border, BorderStrong;
            // 文字层级（三档：主 / 次 / 弱 ✓）
            public string Text, TextDim, TextFaint;
            // 语义色（每风格一份；与基准的关系由 Overrides 登记 ✓）
            public string Normal, Attention, Abnormal, Unknown;
            public string NormalSoft, AttentionSoft, AbnormalSoft;
            public string Muted;        // 中性底（条形轨道/空态底之类 ✓ 语义上属"未知"家族 ✓）
            // 其余
            public string BarTrack, FormAcp, CardShadow;

            /// <summary>本风格**偏离明暗基准**的语义色（✗ 没登记就不许偏 ✗ 断言盯着 ✓）。
            /// A/B 与基准逐值相同 ⇒ 空；C（青色强调的紧凑暗色）与 D（更冷的仪表盘浅色）有登记 ✓。</summary>
            public Semantic[] Overrides;

            /// <summary>该语义色是否已登记为"偏离基准"。</summary>
            public bool IsOverride(Semantic s)
            {
                for (int i = 0; i < Overrides.Length; i++) if (Overrides[i] == s) return true;
                return false;
            }

            /// <summary>本风格实际生效的语义色取值。</summary>
            public string SemanticOf(Semantic s)
            {
                switch (s)
                {
                    case Semantic.Normal: return Normal;
                    case Semantic.Attention: return Attention;
                    case Semantic.Abnormal: return Abnormal;
                    case Semantic.Unknown: return Unknown;
                    default: return Accent;
                }
            }

            /// <summary>语义色的软底（徽章 / 光环用 ✓ 未知与强调没有软底 ⇒ 走 InsetBg/AccentSoft ✓）。</summary>
            public string SoftOf(Semantic s)
            {
                switch (s)
                {
                    case Semantic.Normal: return NormalSoft;
                    case Semantic.Attention: return AttentionSoft;
                    case Semantic.Abnormal: return AbnormalSoft;
                    case Semantic.Accent: return AccentSoft;
                    default: return InsetBg;
                }
            }
        }

        /// <summary>四个风格 ✗ 就这四个 ✗（下标 = `gui_style` 取值 0..3 ✓）。
        /// 取值与 2026-10-10 样式重构后的工作副本**逐字一致** ✓（本轮只做搬家 ✗ 不调色 ✗）。</summary>
        public static readonly StyleSet[] Styles =
        {
            // —— 0 · A · 浅色卡片（默认）：暖灰底 + 品牌蓝 ——
            new StyleSet
            {
                Key = "A", Name = "A · 浅色卡片", Mode = Mode.Light,
                Accent = "#4D6BFE", AccentHover = "#3B57E8", AccentSoft = "#E9EEFF", OnAccent = "#FFFFFF",
                PageBg = "#F5F6F8", SidebarBg = "#FCFCFD", CardBg = "#FFFFFF", CardHover = "#F6F7FA", InsetBg = "#EFF1F5",
                Border = "#E5E7EB", BorderStrong = "#D6D9E0",
                Text = "#151A23", TextDim = "#5B6270", TextFaint = "#6B7280",
                Normal = "#16A34A", Attention = "#B45309", Abnormal = "#DC2626", Unknown = "#8A919C",
                NormalSoft = "#E2F4E8", AttentionSoft = "#FBEFD8", AbnormalSoft = "#FAE3E3", Muted = "#C9CDD4",
                BarTrack = "#ECEEF2", FormAcp = "#7C5CFC", CardShadow = "0 1 3 0 #10182810",
                Overrides = new Semantic[0]
            },
            // —— 1 · B · 深色卡片：石墨底 + 亮靛蓝 accent ——
            new StyleSet
            {
                Key = "B", Name = "B · 深色卡片", Mode = Mode.Dark,
                Accent = "#7C8DFF", AccentHover = "#97A4FF", AccentSoft = "#2A3558", OnAccent = "#FFFFFF",
                PageBg = "#0E1013", SidebarBg = "#12151A", CardBg = "#161A20", CardHover = "#1C222B", InsetBg = "#0B0D10",
                Border = "#262C37", BorderStrong = "#343D4B",
                Text = "#E8EAF0", TextDim = "#A6ADBB", TextFaint = "#7E8798",
                Normal = "#3FB97F", Attention = "#F0B35E", Abnormal = "#EF6A6A", Unknown = "#9AA3B2",
                NormalSoft = "#17352A", AttentionSoft = "#3A2E18", AbnormalSoft = "#3D1F1F", Muted = "#46505E",
                BarTrack = "#232A35", FormAcp = "#9D8CFF", CardShadow = "",
                Overrides = new Semantic[0]
            },
            // —— 2 · C · 深色紧凑：密度优先的控制台，青色 accent ——
            new StyleSet
            {
                Key = "C", Name = "C · 深色紧凑", Mode = Mode.Dark,
                Accent = "#2CC9B5", AccentHover = "#4BD4C2", AccentSoft = "#143B37", OnAccent = "#052622",
                PageBg = "#0A0C0E", SidebarBg = "#0C0F12", CardBg = "#0F1318", CardHover = "#151B22", InsetBg = "#08090B",
                Border = "#1F262E", BorderStrong = "#2C3540",
                Text = "#D9DFE8", TextDim = "#93A0AE", TextFaint = "#5F6B78",
                Normal = "#4ADE80", Attention = "#F2C14E", Abnormal = "#F2707C", Unknown = "#5F6B78",
                NormalSoft = "#133327", AttentionSoft = "#38300F", AbnormalSoft = "#3B1A1E", Muted = "#3E4854",
                BarTrack = "#1C232B", FormAcp = "#A78BFA", CardShadow = "",
                Overrides = new[] { Semantic.Normal, Semantic.Attention, Semantic.Abnormal, Semantic.Unknown, Semantic.Accent }
            },
            // —— 3 · D · 浅色仪表盘：更冷的蓝灰底，把可视化放大 ——
            new StyleSet
            {
                Key = "D", Name = "D · 浅色仪表盘", Mode = Mode.Light,
                Accent = "#2F6BFF", AccentHover = "#2059DB", AccentSoft = "#E4ECFF", OnAccent = "#FFFFFF",
                PageBg = "#EEF2F9", SidebarBg = "#F7F9FC", CardBg = "#FFFFFF", CardHover = "#F4F7FC", InsetBg = "#E8EEF7",
                Border = "#E2E7F0", BorderStrong = "#D0D8E4",
                Text = "#131C2B", TextDim = "#55617A", TextFaint = "#6B7280",
                Normal = "#0EA36B", Attention = "#B45309", Abnormal = "#E5484D", Unknown = "#6B7280",
                NormalSoft = "#DFF2E9", AttentionSoft = "#FBEFD5", AbnormalSoft = "#FBE5E5", Muted = "#CBD3DF",
                BarTrack = "#E6EBF3", FormAcp = "#8B5CF6", CardShadow = "0 1 3 0 #1B2A4A10",
                // D 的"注意"与浅色基准同值 ⇒ 不算覆盖 ✓（断言按**取值**判定，不看名单本身 ✓）
                Overrides = new[] { Semantic.Normal, Semantic.Abnormal, Semantic.Unknown, Semantic.Accent }
            }
        };

        /// <summary>风格数（断言：= 4 ✓）。</summary>
        public const int StyleCount = 4;

        /// <summary>风格下标归一（越界 ⇒ 0 ✓ 与旧 `Palette.Apply` 的 else 分支一致 ✓）。</summary>
        public static int Clamp(int styleKind) { return (styleKind >= 0 && styleKind < StyleCount) ? styleKind : 0; }

        /// <summary>取某风格的一整套取值（下标已归一 ✓ 不会越界 ✓）。</summary>
        public static StyleSet Style(int styleKind) { return Styles[Clamp(styleKind)]; }

        // =====================================================================================
        //  ③ 间距档：只用 4 的倍数（4 / 8 / 12 / 16 / 24）
        // =====================================================================================

        public const double S4 = 4;    // 极紧：图标 ↔ 短标签
        public const double S8 = 8;    // 紧凑：行内元素
        public const double S12 = 12;  // 卡片内分组
        public const double S16 = 16;  // 卡片之间 / 页面分区
        public const double S24 = 24;  // 页面级留白

        /// <summary>间距档（4 的倍数 ✓ 断言逐个取模 ✓）。</summary>
        public static readonly double[] SpacingSteps = { S4, S8, S12, S16, S24 };

        /// <summary>是否合法间距档（4 的倍数且 &gt; 0 ✓）。</summary>
        public static bool IsSpacingStep(double v) { return v > 0 && Math.Abs(v % 4) < 0.0001; }

        // =====================================================================================
        //  ④ 字号：**3 档**（小 / 中 / 大）✗ 不许 11.5 / 12.5 这种随手值 ✗
        // =====================================================================================

        public const double FontSm = 11;   // 小：脚注 / 徽章 / 表格次级（原 10.5 / 11 / 11.5 归并到这里）
        public const double FontMd = 13;   // 中：正文 / 按钮 / 行标题 / 小节题（原 12 / 12.5 / 13 / 13.5 / 15 归并到这里）
        public const double FontLg = 22;   // 大：页题 / 大数字（原 20 / 22 / 24 归并到这里）

        /// <summary>字号档（**只有 3 个** ✓ 断言：数量 ≤ 3 且互不相同 ✓）。</summary>
        public static readonly double[] FontSteps = { FontSm, FontMd, FontLg };

        /// <summary>是否合法字号档（必须正好等于三档之一 ✗ 随手值不算 ✗）。</summary>
        public static bool IsFontStep(double v)
        {
            for (int i = 0; i < FontSteps.Length; i++) if (Math.Abs(FontSteps[i] - v) < 0.0001) return true;
            return false;
        }

        // =====================================================================================
        //  ⑤ 层级三档：主 / 次 / 弱 —— 用**色阶**表达 ✗ 不靠 opacity 随手调 ✗
        // =====================================================================================

        /// <summary>层级三档（主：数值大而亮 ✓ 次：标签暗一档 ✓ 弱：脚注更暗更小 ✓）。</summary>
        public enum Level { Primary = 0, Secondary = 1, Tertiary = 2 }

        /// <summary>层级档数（断言：= 3 ✓）。</summary>
        public const int LevelCount = 3;

        /// <summary>层级 → 该风格的文字色（三个色阶 ✓ 都来自风格表本身 ✓ 不是透明度的把戏 ✓）。</summary>
        public static string LevelColor(int styleKind, Level level)
        {
            StyleSet s = Style(styleKind);
            if (level == Level.Primary) return s.Text;
            if (level == Level.Secondary) return s.TextDim;
            return s.TextFaint;
        }

        /// <summary>层级 → 字号档（主=大 / 次=中 / 弱=小 ✓）。</summary>
        public static double LevelFont(Level level)
        {
            if (level == Level.Primary) return FontLg;
            if (level == Level.Secondary) return FontMd;
            return FontSm;
        }

        /// <summary>层级枚举顺序（断言遍历用 ✓）。</summary>
        public static readonly Level[] Levels = { Level.Primary, Level.Secondary, Level.Tertiary };

        // =====================================================================================
        //  ⑥ 动效：≤ 200ms 常量 + "减少动效"的唯一读取点
        // =====================================================================================

        public const int MotionFastMs = 120;   // 淡入（仅透明度）
        public const int MotionBaseMs = 180;   // 淡入 + 轻微位移（悬浮/展开）
        public const int MotionMaxMs = 200;    // ★ 上限：任何动效时长都不许超过它（断言盯着 ✓）

        /// <summary>动效时长常量（断言：全部 ≤ <see cref="MotionMaxMs"/> ✓）。</summary>
        public static readonly int[] MotionDurations = { MotionFastMs, MotionBaseMs, MotionMaxMs };

        /// <summary>环境变量名：设为 1/true/on ⇒ 减少动效（用户/系统层不必改代码就能关掉动效 ✓）。</summary>
        public const string ReduceMotionEnv = "DSHT_REDUCE_MOTION";

        private static bool _reduced;

        /// <summary>★ "减少动效"的**唯一读取点** ✗ 别在别处各读一份 ✗。
        /// 平台/设置层（跟随系统设置的地方）只许写这一个属性；绘制辅助只许读它 ✓。
        /// 默认 false（跟随系统这一层目前在 GUI 壳里，本轮不动它 ✓ 见施工记录的未验证清单 ✓）。</summary>
        public static bool ReducedMotion
        {
            get { return _reduced; }
            set { _reduced = value; }
        }

        /// <summary>从环境变量读一次（供启动路径调用 ✓ 幂等 ✓ 返回读到的值 ✓）。</summary>
        public static bool ReadReducedMotionFromEnvironment()
        {
            string v = null;
            try { v = Environment.GetEnvironmentVariable(ReduceMotionEnv); } catch { }
            if (!string.IsNullOrEmpty(v))
            {
                string t = v.Trim().ToLowerInvariant();
                if (t == "1" || t == "true" || t == "on" || t == "yes") { _reduced = true; return true; }
                if (t == "0" || t == "false" || t == "off" || t == "no") { _reduced = false; return false; }
            }
            return _reduced;
        }

        /// <summary>实际用的时长：减少动效 ⇒ 0（瞬变 ✓）；否则夹到 ≤ <see cref="MotionMaxMs"/> ✓。</summary>
        public static int Duration(int ms)
        {
            if (_reduced) return 0;
            if (ms < 0) return 0;
            return ms > MotionMaxMs ? MotionMaxMs : ms;
        }

        // =====================================================================================
        //  ⑦ 状态灯：**柔光晕 + 去饱和底色**（提案 §12⑥ ✓）
        //     ✗ 不用纯饱和 RGB ✗（那是最常见的"廉价感"来源 ✓）· 明暗两套都要过对比度 ✓
        // =====================================================================================

        /// <summary>去饱和的比例：语义色与**中性色**（该风格的次要文字色 ✓ 本身就是中性灰调）混合。
        /// ✗ 不往"表面色"里混 ✗ —— 那样浅色下会越混越白、对比度掉到 3:1 以下（实测红 2.98 / 灰 2.28 ✗）。</summary>
        public const double DesaturateRatio = 0.40;

        /// <summary>柔光晕的不透明度（8 位 hex 的 alpha ✓ 很轻 ⇒ "柔" ✓ 不是实心块 ✓）。</summary>
        public const int HaloAlpha = 0x38;   // 22%

        /// <summary>灯环的不透明度（比光晕实一点，但仍是半透明 ✓）。</summary>
        public const int RingAlpha = 0x8C;   // 55%

        /// <summary>★ 状态灯的**底色**（去饱和）：语义色 ↔ 该风格中性色 按 <see cref="DesaturateRatio"/> 混合 ✓
        /// ⇒ 饱和度显著下降 ✗ 不再是纯饱和 RGB ✗，而明度基本守住 ⇒ 对比度可过 ✓。</summary>
        public static string StatusBase(Semantic s, int styleKind)
        {
            StyleSet st = Style(styleKind);
            return Mix(st.SemanticOf(s), st.TextDim, DesaturateRatio);
        }

        /// <summary>★ 状态灯的**柔光晕**色（语义色 + 低 alpha ✓ 绘制时叠在底色外圈 ✓）。</summary>
        public static string StatusHalo(Semantic s, int styleKind)
        {
            StyleSet st = Style(styleKind);
            return WithAlpha(st.SemanticOf(s), HaloAlpha);
        }

        /// <summary>灯环色（语义色 + 中低 alpha ✓ 让灯在深色底上也有轮廓 ✓）。</summary>
        public static string StatusRing(Semantic s, int styleKind)
        {
            StyleSet st = Style(styleKind);
            return WithAlpha(st.SemanticOf(s), RingAlpha);
        }

        /// <summary>状态灯要压在哪一层上（对比度断言的基准面 ✓：卡片面 ✓）。</summary>
        public static string StatusSurface(int styleKind) { return Style(styleKind).CardBg; }

        // =====================================================================================
        //  ⑦b 状态灯的**尺寸 / 光晕 / 描边 / 透明**（画法文件只许读它 ✗ 不许自造 ✗）
        //     值与本轮之前逐值一致 ✗ 本轮不改观感 ✗（只是把画法文件里的字面量搬进 token 表 ✓）
        // =====================================================================================

        /// <summary>状态灯直径（默认档 / 强调档 ✓ 与间距网格 4 的倍数同源：8 / 12 ✓）。</summary>
        public const double LightSizeSm = 8;
        public const double LightSizeMd = 12;

        /// <summary>柔光晕的**模糊半径**（越大越柔 ✓ 只用到 8 ✓ ✗ 不做发光循环 ✗）。</summary>
        public const double LightGlowBlur = 8;

        /// <summary>柔光晕的**扩散**（极小 ✓ ⇒ 它是"晕"不是"实心块" ✓）。</summary>
        public const double LightGlowSpread = 1;

        /// <summary>灯环的**粗细**（发丝 ✓）。</summary>
        public const double LightRingStroke = 1;

        /// <summary>「什么都不画」的**唯一取值**（窗口底 / 光晕底 ✓ 它不是语义色 ✓ 但也不许
        /// 在业务文件里写 `Brushes.Transparent` / `Color.FromArgb(0,…)` 这类命名颜色 ✗ 断言盯着 ✓）。</summary>
        public const string TransparentHex = "#00000000";

        // =====================================================================================
        //  ⑦c 悬边状态灯的**几何**（厚度 / 长度 / 透明余量 / 圆点 ✓）
        //     三档下标 = 配置 `overlay_scale`（0 紧凑 / 1 标准 / 2 大 ✓ 越界回落标准档 ✓）
        //     ★ 取值与本轮之前**逐值一致** ✗ 本轮不改观感 ✗ —— 只是从 `OverlayWindow` 搬进 token 表
        //       （断言：业务文件里不许再出现这些自造数值 ✓ 改回去即红 ✓）
        // =====================================================================================

        /// <summary>灯体**厚度**（贴边方向的宽度 ✓ 三档 8 / 11 / 14）。</summary>
        public static double LampThicknessOf(int scale) { return scale == 0 ? 8 : (scale == 2 ? 14 : 11); }

        /// <summary>灯体**长度**（沿边方向 ✓ 三档 56 / 74 / 92）。</summary>
        public static double LampLengthOf(int scale) { return scale == 0 ? 56 : (scale == 2 ? 92 : 74); }

        /// <summary>灯四周的**透明余量** ✓ —— 它就是"灯以外的区域"：必须点击穿透 ✓（三档 4 / 5 / 7）。</summary>
        public static double LampMarginOf(int scale) { return scale == 0 ? 4 : (scale == 2 ? 7 : 5); }

        /// <summary>灯体**圆点直径**（= 厚度 − 4 ✓ 下限 6 ⇒ 三档 6 / 7 / 10）。</summary>
        public static double LampDotOf(double thickness) { return Math.Max(6, thickness - 4); }

        /// <summary>展开列表**每行**的灯直径（9 ✓ 与悬边灯同一套画法 ✓）。</summary>
        public const double LampRowDot = 9;

        // =====================================================================================
        //  ⑧ 纯色math（不依赖任何图形库 ⇒ 断言可在无图形环境里算对比度 ✓）
        // =====================================================================================

        /// <summary>解析 `#RRGGBB` / `#RRGGBBAA`（也容忍 `#RGB` ✗ 我们不用它 ✗）。失败 ⇒ 抛（构造期就该炸 ✓）。</summary>
        public static Rgb Parse(string hex)
        {
            if (string.IsNullOrEmpty(hex)) throw new ArgumentException("空色值", "hex");
            string h = hex.Trim();
            if (h.Length > 0 && h[0] == '#') h = h.Substring(1);
            if (h.Length != 6 && h.Length != 8) throw new ArgumentException("色值必须是 #RRGGBB 或 #RRGGBBAA：" + hex, "hex");
            int r = int.Parse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int g = int.Parse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int b = int.Parse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int a = h.Length == 8 ? int.Parse(h.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : 255;
            return new Rgb(r, g, b, a);
        }

        /// <summary>是不是合法色值（`#RRGGBB` / `#RRGGBBAA` ✓ 断言用 ✓ 不抛 ✓）。</summary>
        public static bool IsHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return false;
            string h = hex.Trim();
            if (h.Length > 0 && h[0] == '#') h = h.Substring(1);
            if (h.Length != 6 && h.Length != 8) return false;
            for (int i = 0; i < h.Length; i++)
            {
                char c = h[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>`#RRGGBB` → 同色 + 指定 alpha（8 位 hex ✓ 大写 ✓）。</summary>
        public static string WithAlpha(string hex, int alpha)
        {
            Rgb c = Parse(hex);
            return "#" + c.R.ToString("X2", CultureInfo.InvariantCulture) + c.G.ToString("X2", CultureInfo.InvariantCulture)
                 + c.B.ToString("X2", CultureInfo.InvariantCulture) + Clamp255(alpha).ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>按比例把 <paramref name="a"/> 往 <paramref name="b"/> 混（0 ⇒ 全 a ✓ 1 ⇒ 全 b ✓）。</summary>
        public static string Mix(string a, string b, double ratio)
        {
            Rgb x = Parse(a), y = Parse(b);
            double t = ratio < 0 ? 0 : (ratio > 1 ? 1 : ratio);
            return ToHex(
                (int)Math.Round(x.R + (y.R - x.R) * t),
                (int)Math.Round(x.G + (y.G - x.G) * t),
                (int)Math.Round(x.B + (y.B - x.B) * t));
        }

        /// <summary>RGB → `#RRGGBB`（大写 ✓）。</summary>
        public static string ToHex(int r, int g, int b)
        {
            return "#" + Clamp255(r).ToString("X2", CultureInfo.InvariantCulture)
                 + Clamp255(g).ToString("X2", CultureInfo.InvariantCulture)
                 + Clamp255(b).ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>WCAG 2.x 相对亮度（0..1 ✓）。</summary>
        public static double RelativeLuminance(string hex)
        {
            Rgb c = Parse(hex);
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        /// <summary>WCAG 2.x 对比度（1.0 ~ 21.0 ✓ 顺序无关 ✓）。</summary>
        public static double Contrast(string a, string b)
        {
            double la = RelativeLuminance(a), lb = RelativeLuminance(b);
            double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        /// <summary>非文本界面组件的对比度下限（WCAG 1.4.11 的 3:1 ✓ 状态灯断言用它 ✓）。</summary>
        public const double MinUiContrast = 3.0;

        /// <summary>RGB 三通道 + alpha（纯值类型 ✓ 不依赖图形库 ✓）。</summary>
        public struct Rgb
        {
            public int R, G, B, A;
            public Rgb(int r, int g, int b, int a) { R = r; G = g; B = b; A = a; }
            public override string ToString() { return ToHex(R, G, B); }
        }

        private static int Clamp255(int v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }

        private static double Channel(int v)
        {
            double c = Clamp255(v) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        // =====================================================================================
        //  ⑨ 给断言用的自查（把"完整性"变成可读的一行行 ✓）
        // =====================================================================================

        /// <summary>语义色的中文名（断言/报告用 ✓ 也是用户可见文案的**唯一**措辞来源 ✓）。</summary>
        public static string SemanticName(Semantic s)
        {
            switch (s)
            {
                case Semantic.Normal: return "正常";
                case Semantic.Attention: return "注意";
                case Semantic.Abnormal: return "异常";
                case Semantic.Unknown: return "未知";
                default: return "强调";
            }
        }

        /// <summary>把 5×2 网格摊平成 `语义/明暗=色值` 清单（断言与报告都读它 ✓ 只有一份 ✓）。</summary>
        public static List<string> SemanticGridLines()
        {
            List<string> o = new List<string>();
            for (int i = 0; i < Semantics.Length; i++)
            {
                o.Add(SemanticName(Semantics[i]) + " · 明=" + SemanticColor(Semantics[i], Mode.Light)
                    + " 暗=" + SemanticColor(Semantics[i], Mode.Dark));
            }
            return o;
        }
    }
}
