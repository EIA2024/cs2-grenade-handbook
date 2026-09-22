using System.Windows;
using System.Windows.Controls;

namespace Handbook.Desktop;

internal static class Dialogs
{
    public static string? Prompt(Window owner, string title, string label, string initial = "")
    {
        var dialog = new Window { Title = title, Width = 480, SizeToContent = SizeToContent.Height, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var stack = new StackPanel { Margin = new Thickness(20) }; stack.Children.Add(Theme.Text(label));
        var input = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 14) }; stack.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var accept = Theme.Button("确定", () => dialog.DialogResult = true); accept.IsDefault = true;
        var cancel = Theme.Button("取消", () => dialog.DialogResult = false); cancel.IsCancel = true;
        buttons.Children.Add(accept); buttons.Children.Add(cancel); stack.Children.Add(buttons); dialog.Content = stack;
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }
    public static T? Choose<T>(Window owner, string title, IEnumerable<T> items, Func<T, string> label) where T : class
    {
        var dialog = new Window { Title = title, Width = 620, Height = 440, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(16) }; var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Bottom);
        var list = new ListBox(); foreach (var item in items) list.Items.Add(new ListBoxItem { Content = label(item), Tag = item });
        buttons.Children.Add(Theme.Button("选择", () => { if (list.SelectedItem != null) dialog.DialogResult = true; })); buttons.Children.Add(Theme.Button("取消", () => dialog.DialogResult = false));
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem != null) dialog.DialogResult = true; };
        panel.Children.Add(buttons); panel.Children.Add(list); dialog.Content = panel;
        return dialog.ShowDialog() == true ? (T)((ListBoxItem)list.SelectedItem).Tag : null;
    }
    public static string? PickImage(Window owner)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择图片", Filter = "图片|*.png;*.jpg;*.jpeg", CheckFileExists = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
}
