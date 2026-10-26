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
    /// ✗（源码扫描断言盯着 ✓）—— 它只负责"把 token 变成画刷/控件" ✓。</para>
    ///
    /// <para>【明暗都要过对比度】底色 vs 卡片面的对比度由断言守住（WCAG 1.4.11 的 3:1 ✓
    /// 四风格逐个算 ✓ 缺一个即红 ✓）；✗ 不靠"看起来还行" ✗。</para>
    ///
    /// <para>【动效】只做淡入 + 轻微位移 ✓ 时长取 <see cref="DesignTokens.Duration"/>（≤200ms ✓
    /// 且跟随"减少动效"⇒ 为 0 时**瞬变** ✗ 不播动画 ✗）。</para></summary>
    public static class StatusLight
    {
        /// <summary>灯体直径（默认档 ✓ 与间距网格 4 的倍数同源：4 / 8 / 12 ✓）。</summary>
        public const double SizeSm = 8;
        public const double SizeMd = 12;

        /// <summary>柔光晕的模糊半径（越大越柔 ✓ 我们只用到 8~10 ✓ 不做发光循环 ✗）。</summary>
        public const double GlowBlur = 8;

        /// <summary>去饱和底色（`#RRGGBB` 字符串 ✓ 需要字符串时用它 ✓ 断言也读它 ✓）。</summary>
        public static string BaseHex(DesignTokens.Semantic s, int styleKind)
        {
            return DesignTokens.StatusBase(s, styleKind);
        }

        /// <summary>去饱和底色画刷。</summary>
        public static IBrush BaseBrush(DesignTokens.Semantic s, int styleKind)
        {
            return new SolidColorBrush(Color.Parse(BaseHex(s, styleKind)));
        }

        /// <summary>灯环画刷（语义色 + 中低 alpha ✓）。</summary>
        public static IBrush RingBrush(DesignTokens.Semantic s, int styleKind)
        {
            return new SolidColorBrush(Color.Parse(DesignTokens.StatusRing(s, styleKind)));
        }

        /// <summary>柔光晕色（语义色 + 低 alpha ✓ 8 位 hex ✓）。</summary>
        public static Color GlowColor(DesignTokens.Semantic s, int styleKind)
        {
            return Color.Parse(DesignTokens.StatusHalo(s, styleKind));
        }

        /// <summary>柔光晕投影（模糊 + 极小扩散 ✓ 不是实心块 ✓）。</summary>
        public static BoxShadows Glow(DesignTokens.Semantic s, int styleKind)
        {
            return new BoxShadows(new BoxShadow
            {
                Blur = GlowBlur,
                Spread = 1,
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
                Background = Brushes.Transparent,
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
                StrokeThickness = 1,
                IsHitTestVisible = false
            };

            Grid g = new Grid { Width = size, Height = size };
            g.Children.Add(halo);
            g.Children.Add(dot);
            return g;
        }

        /// <summary>淡入 + 轻微位移（≤200ms ✓ 减少动效 ⇒ 直接到终态 ✗ 不播 ✗）。
        /// 返回实际用的毫秒数（0 = 没播动画 ✓ 便于断言/日志 ✓）。</summary>
        public static int FadeIn(Control c, double fromDy = 4)
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
