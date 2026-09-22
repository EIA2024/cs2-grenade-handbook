using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Handbook.Core;
using InputBinding = Handbook.Core.InputBinding;
using InputDevice = Handbook.Core.InputDevice;
using BindingSchema = Handbook.Core.InputBindings;

namespace Handbook.Desktop;

internal static class BindingNames
{
    public static string Display(InputBinding binding)
    {
        var mods = new List<string>();
        if (binding.Modifiers.HasFlag(InputModifiers.Control)) mods.Add("Ctrl");
        if (binding.Modifiers.HasFlag(InputModifiers.Alt)) mods.Add("Alt");
        if (binding.Modifiers.HasFlag(InputModifiers.Shift)) mods.Add("Shift");
        var key = binding.Device == InputDevice.Mouse ? binding.Code switch { 1 => "鼠标左键", 2 => "鼠标右键", 4 => "鼠标中键", 5 => "鼠标侧键 1", 6 => "鼠标侧键 2", _ => "未知按钮" } : KeyInterop.KeyFromVirtualKey(binding.Code).ToString();
        mods.Add(key); return string.Join("+", mods);
    }
    public static InputBinding ParseLegacy(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new FormatException("旧快捷键为空。");
        var normalized = Handbook.Core.InputBindings.ParseLegacy(string.Join('+', parts[..^1].Append("Q")));
        if (new KeyConverter().ConvertFromString(parts[^1]) is not Key key || key == Key.None) throw new FormatException("无效快捷键。");
        return normalized with { Code = KeyInterop.VirtualKeyFromKey(key) };
    }
    public static InputModifiers Modifiers() =>
        (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? InputModifiers.Control : 0) |
        (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? InputModifiers.Alt : 0) |
        (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? InputModifiers.Shift : 0);
    public static int MouseCode(MouseButton button) => button switch { MouseButton.Left => 1, MouseButton.Right => 2, MouseButton.Middle => 4, MouseButton.XButton1 => 5, MouseButton.XButton2 => 6, _ => 0 };
}

internal sealed class InputBindingDialog : Window
{
    private readonly Dictionary<InputCommand, InputBinding> draft;
    private readonly Dictionary<InputCommand, Button> buttons = [];
    private readonly TextBlock status = Theme.Text("点击右侧绑定，再按键盘按键或鼠标按钮。", 13, Theme.Muted);
    private InputCommand? capturing;
    private Button? cancelCapture;
    private readonly Action<IReadOnlyDictionary<InputCommand, InputBinding>> commit;
    public InputBindingDialog(Window owner, IReadOnlyDictionary<InputCommand, InputBinding> bindings, Action<IReadOnlyDictionary<InputCommand, InputBinding>> commit)
    {
        this.commit = commit; draft = new(bindings); Owner = owner; Title = "后台浏览 · 自定义绑定"; Width = 590; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)Application.Current.Resources[typeof(Window)];
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(Theme.Text("不切换焦点的道具导航", 21, Theme.Accent));
        panel.Children.Add(Theme.Text("支持键盘、鼠标五键，以及 Ctrl / Alt / Shift 组合。\n这些按键仍可能同时触发前台应用中的动作。", 13, Theme.Muted));
        foreach (var command in Enum.GetValues<InputCommand>())
        {
            var row = new Grid { Margin = new Thickness(0, 5, 0, 5) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(265) });
            row.Children.Add(Theme.Text(BindingSchema.Labels[command]));
            var button = Theme.Button(BindingNames.Display(draft[command]), () => BeginCapture(command)); buttons[command] = button; Grid.SetColumn(button, 1); row.Children.Add(button); panel.Children.Add(row);
        }
        panel.Children.Add(status);
        var actions = new WrapPanel();
        actions.Children.Add(Theme.Button("保存全部", Save));
        cancelCapture = Theme.Button("取消录入", () => { capturing = null; RefreshLabels(); status.Text = "已取消录入，原有草稿保留。"; }); actions.Children.Add(cancelCapture);
        actions.Children.Add(Theme.Button("恢复默认", () => { capturing = null; foreach (var pair in BindingSchema.Defaults()) draft[pair.Key] = pair.Value; RefreshLabels(); }));
        actions.Children.Add(Theme.Button("关闭不保存", Close)); panel.Children.Add(actions); Content = panel;
        PreviewKeyDown += CaptureKey; PreviewMouseDown += CaptureMouse;
    }
    private void BeginCapture(InputCommand command)
    { capturing = command; RefreshLabels(); buttons[command].Content = "等待按键或鼠标按钮…"; status.Text = "请按下新绑定。Alt+F4 可关闭并放弃；窗口关闭按钮也不会保存。"; }
    private void CaptureKey(object sender, KeyEventArgs e)
    {
        if (capturing == null || e.IsRepeat) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        e.Handled = true; int code = KeyInterop.VirtualKeyFromKey(key);
        if (BindingSchema.IsModifier(code)) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) { status.Text = "不支持 Win 键组合。"; return; }
        Capture(new(InputDevice.Keyboard, code, BindingNames.Modifiers()));
    }
    private void CaptureMouse(object sender, MouseButtonEventArgs e)
    {
        if (capturing == null) return;
        if (cancelCapture?.IsMouseOver == true) return;
        e.Handled = true;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) { status.Text = "不支持 Win 键组合。"; return; }
        Capture(new(InputDevice.Mouse, BindingNames.MouseCode(e.ChangedButton), BindingNames.Modifiers()));
    }
    private void Capture(InputBinding binding)
    {
        if (capturing is not { } command) return;
        // Validate one binding independently. Duplicate actions can be swapped in draft,
        // but the complete draft must be unique before Save can commit anything.
        if (binding.Device == InputDevice.Keyboard && (binding.Code < 8 || binding.Code > 254)) { status.Text = "不支持该按键。"; return; }
        draft[command] = binding; capturing = null; RefreshLabels();
        try { BindingSchema.Validate(draft); status.Text = "已录入，点击“保存全部”后生效。"; }
        catch (FormatException e) { status.Text = e.Message + " 可继续修改其他动作，或取消。"; }
    }
    private void RefreshLabels() { foreach (var (command, button) in buttons) button.Content = BindingNames.Display(draft[command]); }
    private void Save()
    {
        if (capturing != null) { status.Text = "请先完成或取消当前录入。"; return; }
        try { BindingSchema.Validate(draft); commit(draft); DialogResult = true; }
        catch (Exception e) { status.Text = "未保存，原绑定不变：" + e.Message; }
    }
}
