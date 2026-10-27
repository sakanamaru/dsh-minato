using System;
using Avalonia;

namespace Dsht.Gui.Avalonia
{
    /// <summary>跨平台 GUI（Avalonia）入口。骨架阶段：一个窗口 + 左导航 + 右侧 CLI 输出。</summary>
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // ★ 评审用灯样式变体（DSHT_OVERLAY_LAMP=a|b|c）与「减少动效」各读一次 ✓
            //   都只影响画法/动效，不落盘、不新增配置键、不碰数据口径 ✓
            Dsht.Gui.Avalonia.Design.DesignTokens.ReadLampVariantFromEnvironment();
            Dsht.Gui.Avalonia.Design.DesignTokens.ReadReducedMotionFromEnvironment();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
        }
    }
}
