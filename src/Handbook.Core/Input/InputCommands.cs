namespace Handbook.Core;

public enum InputCommand { ToggleMenu, Previous, Next, Confirm, ToggleVisibility }
public enum InputDevice { Keyboard, Mouse }
[Flags]
public enum InputModifiers { None = 0, Control = 1, Alt = 2, Shift = 4 }
public sealed record InputBinding(InputDevice Device, int Code, InputModifiers Modifiers = InputModifiers.None);

public static class InputBindings
{
    public static readonly IReadOnlyDictionary<InputCommand, string> Labels = new Dictionary<InputCommand, string>
    {
        [InputCommand.ToggleMenu] = "打开／取消菜单", [InputCommand.Previous] = "上一项",
        [InputCommand.Next] = "下一项", [InputCommand.Confirm] = "确认／进入", [InputCommand.ToggleVisibility] = "隐藏／显示"
    };
    public static bool IsModifier(int code) => code is 16 or 17 or 18 or 160 or 161 or 162 or 163 or 164 or 165 or 91 or 92;
    public static void Validate(IReadOnlyDictionary<InputCommand, InputBinding> bindings)
    {
        if (bindings.Count != 5 || Enum.GetValues<InputCommand>().Any(c => !bindings.ContainsKey(c))) throw new FormatException("五个动作都需要绑定。");
        var used = new HashSet<InputBinding>();
        foreach (var binding in bindings.Values)
        {
            if (binding == null || !Enum.IsDefined(binding.Device) || ((int)binding.Modifiers & ~7) != 0) throw new FormatException("绑定格式无效。");
            if (binding.Device == InputDevice.Mouse && binding.Code is not (1 or 2 or 4 or 5 or 6)) throw new FormatException("仅支持左、右、中及两个侧键。");
            if (binding.Device == InputDevice.Keyboard && (binding.Code < 8 || binding.Code > 254 || IsModifier(binding.Code))) throw new FormatException("请使用普通按键，不能仅绑定修饰键。");
            if (!used.Add(binding)) throw new FormatException("两个动作不能使用相同绑定。");
        }
    }
    public static Dictionary<InputCommand, InputBinding> Defaults() => new()
    {
        [InputCommand.ToggleMenu] = new(InputDevice.Keyboard, 81, InputModifiers.Control | InputModifiers.Alt),
        [InputCommand.Previous] = new(InputDevice.Mouse, 5), [InputCommand.Next] = new(InputDevice.Mouse, 6),
        [InputCommand.Confirm] = new(InputDevice.Mouse, 4),
        [InputCommand.ToggleVisibility] = new(InputDevice.Keyboard, 72, InputModifiers.Control | InputModifiers.Alt)
    };
    // Called only for legacy text settings. New UI captures physical key/button choices.
    public static InputBinding ParseLegacy(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new FormatException("无效快捷键。");
        InputModifiers modifiers = InputModifiers.None;
        foreach (var part in parts[..^1]) modifiers |= part.ToUpperInvariant() switch
        {
            "CTRL" => InputModifiers.Control, "ALT" => InputModifiers.Alt, "SHIFT" => InputModifiers.Shift, _ => throw new FormatException("未知修饰键。")
        };
        var key = parts[^1].ToUpperInvariant(); int code;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) code = key[0];
        else if (key.StartsWith('F') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24) code = 111 + f;
        else code = key switch { "SPACE" => 32, "RETURN" or "ENTER" => 13, "ESC" or "ESCAPE" => 27, "TAB" => 9, "HOME" => 36, "END" => 35, "LEFT" => 37, "UP" => 38, "RIGHT" => 39, "DOWN" => 40, "INSERT" => 45, "DELETE" => 46, "PAGEUP" or "PRIOR" => 33, "PAGEDOWN" or "NEXT" => 34, _ => throw new FormatException("无法转换旧快捷键。") };
        return new(InputDevice.Keyboard, code, modifiers);
    }
    public static string? Migrate(UserSettings settings, Func<string, InputBinding>? legacyParser = null)
    {
        SettingsValidation.Normalize(settings);
        legacyParser ??= ParseLegacy;
        if (settings.SettingsVersion >= 2 && settings.InputBindings != null)
        {
            try { Validate(settings.InputBindings); return null; }
            catch (FormatException) { settings.InputBindings = Defaults(); return "输入绑定无效，已恢复默认值；原设置文件未被自动改写。"; }
        }
        var result = Defaults(); string? warning = null;
        try { result[InputCommand.ToggleMenu] = legacyParser(settings.SelectionHotkey); result[InputCommand.ToggleVisibility] = legacyParser(settings.VisibilityHotkey); Validate(result); }
        catch (Exception e) when (e is FormatException or ArgumentException or NotSupportedException) { result = Defaults(); warning = "旧快捷键无法转换，已使用默认绑定；其他资料和布局保持不变。"; }
        settings.InputBindings = result; settings.SettingsVersion = 2; return warning;
    }
}

// Pure edge matcher: no device APIs, polling, key log or game awareness.
public sealed class InputCommandMatcher
{
    private IReadOnlyDictionary<InputCommand, InputBinding> bindings;
    private readonly HashSet<(InputDevice Device, int Code)> down = [];
    public InputCommandMatcher(IReadOnlyDictionary<InputCommand, InputBinding> bindings) { InputBindings.Validate(bindings); this.bindings = new Dictionary<InputCommand, InputBinding>(bindings); }
    public void SetBindings(IReadOnlyDictionary<InputCommand, InputBinding> value) { InputBindings.Validate(value); bindings = new Dictionary<InputCommand, InputBinding>(value); Reset(); }
    public void Reset() => down.Clear();
    public InputCommand? Feed(InputDevice device, int code, bool pressed)
    {
        var key = (device, code);
        if (!pressed) { down.Remove(key); return null; }
        bool modifier = device == InputDevice.Keyboard && InputBindings.IsModifier(code);
        if (!modifier && !bindings.Values.Any(b => b.Device == device && b.Code == code)) return null;
        if (!down.Add(key) || modifier) return null;
        bool Has(params int[] codes) => codes.Any(c => down.Contains((InputDevice.Keyboard, c)));
        // Windows-key chords are deliberately outside the binding format.
        if (Has(91, 92)) return null;
        var mods = (Has(17, 162, 163) ? InputModifiers.Control : 0) |
                   (Has(18, 164, 165) ? InputModifiers.Alt : 0) |
                   (Has(16, 160, 161) ? InputModifiers.Shift : 0);
        foreach (var (command, binding) in bindings)
            if (binding.Device == device && binding.Code == code && binding.Modifiers == mods) return command;
        return null;
    }
}
