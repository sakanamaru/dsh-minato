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
            // ★ 启动只读「减少动效」这一个环境变量 ✓
            //   灯样式**不再走环境变量**（2026-10-28：唯一来源 = 配置键 `overlay_light_style` ✓
            //   设置页三选一改它 ⇒ 没有第二套状态 ✓）
            Dsht.Gui.Avalonia.Design.DesignTokens.ReadReducedMotionFromEnvironment();
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
