using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Dsht.Gui.Avalonia.ViewModels;   // Palette 在这里

namespace Dsht.Gui.Avalonia
{
    /// <summary>最小的确认对话框 ✓（close_action=ask 用；Avalonia 没有内置确认框）。</summary>
    public sealed class ConfirmDialog : Window
    {
        private bool _yes;

        private ConfirmDialog(string title, string message)
        {
            Title = title;
            Width = 400;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Palette.PageBg;

            StackPanel s = new StackPanel { Margin = new Thickness(20), Spacing = 14 };
            s.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 13,
                Foreground = Palette.Text,
                TextWrapping = TextWrapping.Wrap
            });
            StackPanel acts = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Button cancel = new Button { Content = "取消", Padding = new Thickness(14, 6) };
            cancel.Click += delegate { _yes = false; Close(); };
            Button ok = new Button
            {
                Content = "确认退出",
                Padding = new Thickness(14, 6),
                Background = Palette.Accent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(7)
            };
            ok.Click += delegate { _yes = true; Close(); };
            acts.Children.Add(cancel); acts.Children.Add(ok);
            s.Children.Add(acts);
            Content = s;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { _yes = false; Close(); }
            };
        }

        public static async System.Threading.Tasks.Task<bool> Ask(Window owner, string title, string message)
        {
            try
            {
                ConfirmDialog d = new ConfirmDialog(title, message);
                if (owner != null) await d.ShowDialog(owner); else d.Show();
                return d._yes;
            }
            catch { return false; }
        }
    }
}
