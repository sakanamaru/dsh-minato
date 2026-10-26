using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Dsht.Gui.Avalonia.ViewModels;   // ✓ Palette 在这里 ✓（不在 Shells ✗ 我上一轮写错了 ✓）

namespace Dsht.Gui.Avalonia
{
    /// <summary>一个**最小的输入对话框** ✓✓（Avalonia 没有内置的输入框 ✗ 而"删除备份必须输入当前时间"必须有它 ✓）。
    ///
    /// **用户要求（2026-09-30）**：「删除弹窗输入当前时间才执行」✓✓
    ///   理由：删除备份**不可逆** ✓ 光有"再点一次确认"太容易手滑 ✓
    ///   → 必须**看着时间手打一遍** ✓ 打错了就删不掉 ✓✓
    ///
    /// 用法：
    ///   string typed = await PromptDialog.Ask(this, "删除备份", "请输入当前时间以确认：", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    ///   if (typed == null) return;   // 用户取消 ✓
    /// </summary>
    public sealed class PromptDialog : Window
    {
        private string _result = null;
        private readonly TextBox _box;

        private PromptDialog(string title, string caption, string hint, string initial)
        {
            Title = title;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Palette.PageBg;

            StackPanel s = new StackPanel { Margin = new Thickness(20), Spacing = ShapeTokens.S12 };
            s.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = ShapeTokens.FsEm,
                Foreground = Palette.Text,
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrEmpty(hint))
            {
                s.Children.Add(new TextBlock
                {
                    Text = hint,
                    FontSize = ShapeTokens.FsFoot,
                    Foreground = Palette.TextFaint,
                    TextWrapping = TextWrapping.Wrap
                });
            }
            _box = new TextBox
            {
                Text = initial ?? "",
                FontSize = ShapeTokens.FsEm,   // 输入框字号（有意比正文大 ✓ 聚焦录入；字阶例外，记录于样式重构文档）
                FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                Padding = new Thickness(10, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            // 打开就**全选** ✓ → 想照抄就按 Ctrl+C ✓ 想手打就直接敲 ✓✓
            _box.AttachedToVisualTree += delegate { _box.SelectAll(); _box.Focus(); };
            KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { _result = null; Close(); }   // F19 FIX: works from a button too
        };
        _box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { _result = _box.Text; Close(); e.Handled = true; }   // N14 FIX: the window handler would otherwise fire too
                else if (e.Key == Key.Escape) { _result = null; Close(); e.Handled = true; }
            };
            s.Children.Add(_box);

            StackPanel acts = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Button cancel = new Button { Content = "取消", Padding = new Thickness(14, 6) };
            cancel.Click += delegate { _result = null; Close(); };
            Button ok = new Button
            {
                Content = "确认",
                Padding = new Thickness(14, 6),
                Background = Palette.Accent,
                Foreground = Palette.OnAccent,   // 样式重构：原写死 White ✗ 风格 C 是浅青底 → 必须跟 token
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(ShapeTokens.RCtrl)
            };
            ok.Click += delegate { _result = _box.Text; Close(); };
            acts.Children.Add(cancel); acts.Children.Add(ok);
            s.Children.Add(acts);

            Content = s;
        }

        /// <summary>弹一个输入框 ✓ 返回用户输入（取消返回 null ✓ 不猜 ✓）。</summary>
        public static async System.Threading.Tasks.Task<string> Ask(Window owner, string title, string caption, string hint, string initial)
        {
            try
            {
                PromptDialog d = new PromptDialog(title, caption, hint, initial);
                if (owner != null) await d.ShowDialog(owner); else d.Show();
                return d._result;
            }
            catch { return null; }
        }
    }
}
