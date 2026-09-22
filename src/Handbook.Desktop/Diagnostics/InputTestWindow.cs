using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Handbook.Core;
using InputDevice = Handbook.Core.InputDevice;
using BindingSchema = Handbook.Core.InputBindings;

namespace Handbook.Desktop;

// Optional foreground test process. It does not register Raw Input or inspect any other window.
// Only on-screen counters for the user's configured actions are kept, never input text or logs.
internal sealed class InputTestWindow : Window
{
    private readonly InputCommandMatcher matcher;
    private readonly Dictionary<InputCommand, int> counts = [];
    private readonly Dictionary<InputCommand, TextBlock> labels = [];
    private readonly TextBlock focus = Theme.Text("", 16, Theme.Accent);
    private readonly TextBlock pointer = Theme.Text("鼠标事件：0 · 移动鼠标并点击本窗口", 15, Theme.Accent);
    private int moves, clicks;
    public InputTestWindow(UserSettings settings)
    {
        BindingSchema.Migrate(settings, BindingNames.ParseLegacy); matcher = new(settings.InputBindings!);
        Title = "道具手册 · 双响应测试窗口（非游戏）"; Width = 1200; Height = 760; WindowState = WindowState.Maximized;
        Style = (Style)Application.Current.Resources[typeof(Window)];
        var dock = new DockPanel { Margin = new Thickness(24) }; var header = new StackPanel();
        header.Children.Add(Theme.Text("真实按键双响应测试", 25, Theme.Accent));
        header.Children.Add(Theme.Text("先在主程序进入查看模式，再点击此测试窗口使其处于前台。\n用真实按键打开手册菜单并浏览：本窗口计数与后台手册应同时响应，活动状态应保持。\n把鼠标移到手册面板覆盖的区域并点击，检查下方标记和点击计数是否继续变化。", 14));
        header.Children.Add(focus);
        var actions = new WrapPanel();
        foreach (var command in Enum.GetValues<InputCommand>())
        {
            counts[command] = 0; var label = Theme.Text($"{BindingSchema.Labels[command]} · {BindingNames.Display(settings.InputBindings![command])}：0", 13); labels[command] = label;
            var card = Theme.Card(label); card.Margin = new Thickness(0, 0, 10, 8); actions.Children.Add(card);
        }
        header.Children.Add(actions); header.Children.Add(pointer); header.Children.Add(Theme.Text("这里只记录当前窗口收到的已绑定动作次数，不记录文字、不接收后台输入、不保存结果。更改绑定后请重新打开测试窗口。", 12, Theme.Muted));
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        var canvas = new Canvas { Background = Theme.Panel, ClipToBounds = true };
        var marker = new Ellipse { Width = 24, Height = 24, Stroke = Theme.Accent, StrokeThickness = 3, IsHitTestVisible = false }; canvas.Children.Add(marker);
        canvas.MouseMove += (_, e) => { var p = e.GetPosition(canvas); Canvas.SetLeft(marker, p.X - 12); Canvas.SetTop(marker, p.Y - 12); moves++; UpdatePointer(); };
        dock.Children.Add(canvas); Content = dock;
        PreviewMouseDown += (_, e) => { clicks++; UpdatePointer(); Feed(InputDevice.Mouse, BindingNames.MouseCode(e.ChangedButton), true); };
        PreviewMouseUp += (_, e) => Feed(InputDevice.Mouse, BindingNames.MouseCode(e.ChangedButton), false);
        PreviewKeyDown += (_, e) => { if (!e.IsRepeat) Feed(InputDevice.Keyboard, KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key), true); };
        PreviewKeyUp += (_, e) => Feed(InputDevice.Keyboard, KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key), false);
        Activated += (_, _) => { matcher.Reset(); focus.Text = "前台状态：本测试窗口处于活动状态"; };
        Deactivated += (_, _) => { matcher.Reset(); focus.Text = "前台状态：本测试窗口已失去活动状态"; };
        void Feed(InputDevice device, int code, bool down)
        {
            if (matcher.Feed(device, code, down) is not { } command) return;
            counts[command]++; labels[command].Text = $"{BindingSchema.Labels[command]} · {BindingNames.Display(settings.InputBindings![command])}：{counts[command]}";
        }
        void UpdatePointer() => pointer.Text = $"本窗口鼠标移动事件：{moves} · 点击：{clicks}";
    }
}
