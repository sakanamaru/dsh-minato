using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Dsht.Gui.Avalonia.Design;

namespace Dsht.Gui.Avalonia.Design
{
    /// <summary>★ 状态灯的**绘制辅助**（提案 §12⑥ ✓）：**柔光晕 + 去饱和底色** ✓。
    ///
    /// <para>【为什么不直接画一个纯色圆】纯饱和 RGB 的实心点是最常见的"廉价感"来源 ✗
    /// ⇒ 这里三层叠出来：① 外圈**柔光晕**（语义色低 alpha + 高斯模糊 ✓ 很轻 ✓）
    /// ② 灯体**去饱和底色**（语义色 ↔ 该风格中性色混合 ✓ 不再是纯饱和色 ✓）
    /// ③ 细灯环（半透明语义色 ✓ 深色底上也有轮廓 ✓）。</para>
    ///
    /// <para>【色值一律来自 <see cref="DesignTokens"/>】本文件**没有**任何 `#RRGGBB`
    /// ✗（源码扫描断言盯着 ✓）—— 它只负责"把 token 变成画刷/控件" ✓。
    /// ★ 尺寸 / 光晕模糊与扩散 / 灯环粗细 / 透明 也一律读 <see cref="DesignTokens"/> ✗ 本文件不留自造数值 ✗。</para>
    ///
    /// <para>★ **本文件是全项目唯一的"状态灯画法"** ✗ 不许有第二处 ✗：悬边灯与展开列表每行的灯
    /// 都调 <see cref="Create"/> ✓（断言盯着：界面文件里不许再 `new Ellipse` 画灯 ✗）。</para>
    ///
    /// <para>【明暗都要过对比度】底色 vs 卡片面的对比度由断言守住（WCAG 1.4.11 的 3:1 ✓
    /// 四风格逐个算 ✓ 缺一个即红 ✓）；✗ 不靠"看起来还行" ✗。</para>
    ///
    /// <para>【动效】只做淡入 + 轻微位移 ✓ 时长取 <see cref="DesignTokens.Duration"/>（≤200ms ✓
    /// 且跟随"减少动效"⇒ 为 0 时**瞬变** ✗ 不播动画 ✗）。</para></summary>
    public static class StatusLight
    {
        /// <summary>灯体直径（默认档 ✓ 与间距网格 4 的倍数同源：8 / 12 ✓）。**值来自 token 表** ✓。</summary>
        public const double SizeSm = DesignTokens.LightSizeSm;
        public const double SizeMd = DesignTokens.LightSizeMd;

        /// <summary>柔光晕的模糊半径（越大越柔 ✓ 我们只用到 8 ✓ 不做发光循环 ✗）。**值来自 token 表** ✓。</summary>
        public const double GlowBlur = DesignTokens.LightGlowBlur;

        /// <summary>★ token 色值 → Avalonia 颜色（**唯一转换点** ✓）。
        /// <para>★★ 这里有个真机冒烟抓到的坑（2026-10-26 ✗✓）：token 表的 8 位色值是 **`#RRGGBBAA`**
        /// （本项目的口径 ✓ 见 <see cref="DesignTokens.Parse"/>），而 Avalonia 的 `Color.Parse` 把 8 位
        /// 当成 **`#AARRGGBB`**（alpha 在前 ✓）⇒ 直接把 token 喂给它，**色相会被当成 alpha**：
        /// 实测灯环/光晕变成**紫色**（8 位里那四个字节被整体错位：色相 R/G/B 落到了 alpha/R/G 上 ✗）。
        /// 这个缺陷在"画法还没接进界面"时是隐性的（画法文件自己单测不到渲染 ✗）——接进悬边灯后截图才看出来 ✓。
        /// ⇒ 一律先过 <see cref="DesignTokens.Parse"/>（按本项目的口径解析 ✓）再按 **(A,R,G,B)** 组装 ✓
        /// （✗ 本文件不许再出现 `Color.Parse(` ✗ 断言盯着 ✓）。</para></summary>
        private static Color ColorOf(string hex)
        {
            DesignTokens.Rgb c = DesignTokens.Parse(hex);
            return Color.FromArgb((byte)c.A, (byte)c.R, (byte)c.G, (byte)c.B);
        }

        /// <summary>「什么都不画」的透明画刷（✗ 不写 `Brushes.Transparent` ✗ 值仍来自 token 表 ✓）。</summary>
        public static IBrush TransparentBrush()
        {
            return new SolidColorBrush(ColorOf(DesignTokens.TransparentHex));
        }

        /// <summary>去饱和底色（`#RRGGBB` 字符串 ✓ 需要字符串时用它 ✓ 断言也读它 ✓）。</summary>
        public static string BaseHex(DesignTokens.Semantic s, int styleKind)
        {
            return DesignTokens.StatusBase(s, styleKind);
        }

        /// <summary>去饱和底色画刷。</summary>
        public static IBrush BaseBrush(DesignTokens.Semantic s, int styleKind)
        {
            return new SolidColorBrush(ColorOf(BaseHex(s, styleKind)));
        }

        /// <summary>灯环画刷（语义色 + 中低 alpha ✓）。</summary>
        public static IBrush RingBrush(DesignTokens.Semantic s, int styleKind)
        {
            return new SolidColorBrush(ColorOf(DesignTokens.StatusRing(s, styleKind)));
        }

        /// <summary>柔光晕色（语义色 + 低 alpha ✓ 8 位 hex ✓）。</summary>
        public static Color GlowColor(DesignTokens.Semantic s, int styleKind)
        {
            return ColorOf(DesignTokens.StatusHalo(s, styleKind));
        }

        /// <summary>柔光晕投影（模糊 + 极小扩散 ✓ 不是实心块 ✓）。</summary>
        public static BoxShadows Glow(DesignTokens.Semantic s, int styleKind)
        {
            return new BoxShadows(new BoxShadow
            {
                Blur = DesignTokens.LightGlowBlur,
                Spread = DesignTokens.LightGlowSpread,
                OffsetX = 0,
                OffsetY = 0,
                Color = GlowColor(s, styleKind)
            });
        }

        /// <summary>★ 造一盏可复用的状态灯（直径默认 <see cref="SizeSm"/> ✓ 与间距网格同源 ✓）。
        /// 悬边状态灯 / 会话行 / 列表都能直接用 ✗ 不必各自画一遍 ✗。</summary>
        public static Control Create(DesignTokens.Semantic s, int styleKind, double size = SizeSm)
        {
            if (size <= 0) size = SizeSm;

            // ① 柔光晕：同尺寸的圆角 Border + 模糊投影（投影落在灯体外圈 ⇒ 光晕 ✓）
            Border halo = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = TransparentBrush(),
                BoxShadow = Glow(s, styleKind),
                IsHitTestVisible = false
            };

            // ② 灯体：去饱和底色 + 半透明灯环
            Ellipse dot = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = BaseBrush(s, styleKind),
                Stroke = RingBrush(s, styleKind),
                StrokeThickness = DesignTokens.LightRingStroke,
                IsHitTestVisible = false
            };

            Grid g = new Grid { Width = size, Height = size };
            g.Children.Add(halo);
            g.Children.Add(dot);
            return g;
        }

        // =====================================================================================
        //  ⑦d 悬边灯的三种画法（2026-10-28）—— 语义与取色完全复用上面那一条路 ✓
        //     ★ 这里只决定"形状怎么摆" ✗ 不决定"什么颜色 / 什么含义" ✗：
        //       色值 / alpha / 尺寸 / 光晕 / 描边 / 动效全部由 DesignTokens.EdgeLampSpec 给 ✓
        //     ★ 三种画法都不做循环发光/跳动/旋转 ✗（§12⑥ ✓）
        // =====================================================================================

        /// <summary>★ 悬边灯的变体入口（与行灯共用同一个 <see cref="Create(DesignTokens.Semantic,int,double)"/> 语义 ✓）。
        /// <para>A 极简细条：一条窄胶囊 + 大而淡的光晕，收起时几乎只剩光。</para>
        /// <para>B 圆点 + 轻环：去饱和圆点 + 极淡外环（默认推荐款 ✓ 与列表圆点同一视觉语言）。</para>
        /// <para>C 双端点胶囊：细长胶囊 + 两端更亮的端帽渐变。</para>
        /// <para>✗ 三款都不改一色一信号；✗ 都不自造色值（一律从 token 取）。</para></summary>
        public static Control Create(DesignTokens.Semantic s, int styleKind, DesignTokens.EdgeLampSpec spec)
        {
            if (spec == null) return Create(s, styleKind, SizeSm);
            if (spec.Variant == DesignTokens.LampVariant.Minimal) return CreateMinimalBar(s, styleKind, spec);
            if (spec.Variant == DesignTokens.LampVariant.Capsule) return CreateCapsule(s, styleKind, spec);
            return CreateDotRing(s, styleKind, spec);
        }

        /// <summary>变体的柔光晕投影（模糊/扩散/alpha 全来自 spec ✓）。</summary>
        private static BoxShadows EdgeGlow(DesignTokens.Semantic s, int styleKind, DesignTokens.EdgeLampSpec spec)
        {
            return new BoxShadows(new BoxShadow
            {
                Blur = spec.HaloBlur,
                Spread = spec.HaloSpread,
                OffsetX = 0,
                OffsetY = 0,
                Color = ColorOf(DesignTokens.EdgeHalo(s, styleKind, spec.Variant))
            });
        }

        /// <summary>A · 极简细条：一条窄胶囊（灯体 + 发丝灯环 + 外扩柔光晕）。</summary>
        private static Control CreateMinimalBar(DesignTokens.Semantic s, int styleKind, DesignTokens.EdgeLampSpec spec)
        {
            Border bar = new Border
            {
                Width = spec.Width,
                Height = spec.Height,
                CornerRadius = new CornerRadius(spec.CornerRadius),
                Background = BaseBrush(s, styleKind),
                BorderBrush = new SolidColorBrush(ColorOf(DesignTokens.EdgeRing(s, styleKind, spec.Variant))),
                BorderThickness = new Thickness(spec.RingStroke),
                BoxShadow = EdgeGlow(s, styleKind, spec),
                IsHitTestVisible = false
            };
            return bar;
        }

        /// <summary>B · 圆点 + 轻环：去饱和圆点居中，外圈一圈极淡灯环，柔光晕最克制。</summary>
        private static Control CreateDotRing(DesignTokens.Semantic s, int styleKind, DesignTokens.EdgeLampSpec spec)
        {
            Grid g = new Grid { Width = spec.Width, Height = spec.Height };
            Border halo = new Border
            {
                Width = spec.RingSize,
                Height = spec.RingSize,
                CornerRadius = new CornerRadius(spec.RingSize / 2),
                Background = TransparentBrush(),
                BoxShadow = EdgeGlow(s, styleKind, spec),
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            IBrush ring = new SolidColorBrush(ColorOf(DesignTokens.EdgeRing(s, styleKind, spec.Variant)));
            Ellipse ringLine = new Ellipse
            {
                Width = spec.RingSize,
                Height = spec.RingSize,
                Fill = TransparentBrush(),
                Stroke = ring,
                StrokeThickness = spec.RingStroke,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            Ellipse dot = new Ellipse
            {
                Width = spec.DotSize,
                Height = spec.DotSize,
                Fill = BaseBrush(s, styleKind),
                Stroke = ring,
                StrokeThickness = spec.RingStroke,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            g.Children.Add(halo);
            g.Children.Add(ringLine);
            g.Children.Add(dot);
            return g;
        }

        /// <summary>C · 双端点胶囊：细长胶囊，两端灯环更亮、中间回到去饱和底色（线性渐变 ✓ 一次性 ✓）。</summary>
        private static Control CreateCapsule(DesignTokens.Semantic s, int styleKind, DesignTokens.EdgeLampSpec spec)
        {
            Color baseC = ColorOf(DesignTokens.EdgeBase(s, styleKind, spec.Variant));
            Color endC = ColorOf(DesignTokens.EdgeRing(s, styleKind, spec.Variant));
            LinearGradientBrush grad = new LinearGradientBrush
            {
                StartPoint = spec.Vertical ? new RelativePoint(0.5, 0, RelativeUnit.Relative) : new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = spec.Vertical ? new RelativePoint(0.5, 1, RelativeUnit.Relative) : new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(endC, 0),
                    new GradientStop(baseC, DesignTokens.EdgeGradientInner),
                    new GradientStop(baseC, 1 - DesignTokens.EdgeGradientInner),
                    new GradientStop(endC, 1)
                }
            };
            Border capsule = new Border
            {
                Width = spec.Width,
                Height = spec.Height,
                CornerRadius = new CornerRadius(spec.CornerRadius),
                Background = grad,
                BoxShadow = EdgeGlow(s, styleKind, spec),
                IsHitTestVisible = false
            };
            return capsule;
        }

        /// <summary>淡入 + 轻微位移（≤200ms ✓ 减少动效 ⇒ 直接到终态 ✗ 不播 ✗）。
        /// 返回实际用的毫秒数（0 = 没播动画 ✓ 便于断言/日志 ✓）。</summary>
        public static int FadeIn(Control c, double fromDy = DesignTokens.LightFadeDy)
        {
            int ms = DesignTokens.Duration(DesignTokens.MotionBaseMs);
            if (c == null) return 0;
            if (ms <= 0)
            {
                c.Opacity = 1;
                c.RenderTransform = null;
                return 0;
            }

            TranslateTransform tt = new TranslateTransform { X = 0, Y = fromDy };
            c.RenderTransform = tt;
            c.Opacity = 0;
            c.Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(ms) },
                new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(ms) }
            };
            c.Opacity = 1;
            tt.Y = 0;
            return ms;
        }
    }
}
