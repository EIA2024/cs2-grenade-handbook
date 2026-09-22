using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Handbook.Desktop;

internal static class Theme
{
    public static Brush Background = new SolidColorBrush(Color.FromRgb(18, 24, 34));
    public static Brush Panel = new SolidColorBrush(Color.FromRgb(28, 37, 51));
    public static Brush Ink = new SolidColorBrush(Color.FromRgb(230, 238, 248));
    public static Brush Muted = new SolidColorBrush(Color.FromRgb(150, 169, 190));
    public static Brush Accent = new SolidColorBrush(Color.FromRgb(72, 207, 173));
    public static void Install(Application app)
    {
        var window = new Style(typeof(Window)); window.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Microsoft YaHei UI"))); window.Setters.Add(new Setter(Control.FontSizeProperty, 13d)); window.Setters.Add(new Setter(Control.BackgroundProperty, Background)); window.Setters.Add(new Setter(Control.ForegroundProperty, Ink)); app.Resources.Add(typeof(Window), window);
        var button = new Style(typeof(Button)); button.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 7, 12, 7))); button.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 7, 7))); button.Setters.Add(new Setter(Control.BackgroundProperty, Panel)); button.Setters.Add(new Setter(Control.ForegroundProperty, Ink)); button.Setters.Add(new Setter(Control.BorderBrushProperty, Muted)); app.Resources.Add(typeof(Button), button);
        foreach (var type in new[] { typeof(TextBox), typeof(ListBox), typeof(TreeView) })
        {
            var style = new Style(type); style.Setters.Add(new Setter(Control.BackgroundProperty, Panel)); style.Setters.Add(new Setter(Control.ForegroundProperty, Ink)); style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(57, 73, 93)))) ; style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7))); app.Resources.Add(type, style);
        }
        var treeItem = new Style(typeof(TreeViewItem)); treeItem.Setters.Add(new Setter(Control.ForegroundProperty, Ink)); treeItem.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(3))); app.Resources.Add(typeof(TreeViewItem), treeItem);
        var listItem = new Style(typeof(ListBoxItem)); listItem.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5))); app.Resources.Add(typeof(ListBoxItem), listItem);
    }
    public static Button Button(string text, Action action)
    {
        var button = new Button { Content = text }; button.Click += (_, _) => { try { action(); } catch (Exception e) { MessageBox.Show(e.Message, "操作未完成"); } }; return button;
    }
    public static TextBlock Text(string text, double size = 13, Brush? color = null) => new() { Text = text, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    public static Border Card(UIElement child) => new() { Background = Panel, CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = child };
}
