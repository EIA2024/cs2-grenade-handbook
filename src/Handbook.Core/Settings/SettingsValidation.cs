namespace Handbook.Core;

// Settings are editable local data, not trusted object invariants. Keep recovery here
// so every presentation host receives usable values without duplicating WPF checks.
public static class SettingsValidation
{
    public static IReadOnlyList<string> Normalize(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var warnings = new List<string>();
        if (settings.BrowseFolder == null || settings.BrowseItem == null || settings.SelectedPath == null ||
            settings.SelectionHotkey == null || settings.VisibilityHotkey == null || settings.Recent == null ||
            settings.Recent?.Any(string.IsNullOrWhiteSpace) == true)
            warnings.Add("设置中的空字段已恢复默认值。");
        settings.BrowseFolder ??= ""; settings.BrowseItem ??= ""; settings.SelectedPath ??= "";
        settings.SelectionHotkey ??= "Ctrl+Alt+Q"; settings.VisibilityHotkey ??= "Ctrl+Alt+H";
        settings.Recent = (settings.Recent ?? []).Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        double Valid(double value, double fallback, double minimum, double maximum)
        {
            if (double.IsFinite(value) && value >= minimum && value <= maximum) return value;
            warnings.Add("无效的窗口显示设置已恢复默认值。"); return fallback;
        }
        settings.Width = Valid(settings.Width, 520, 380, 1400);
        settings.Height = Valid(settings.Height, 380, 280, 1200);
        settings.Opacity = Valid(settings.Opacity, 1, .35, 1);
        settings.NotesHeight = Valid(settings.NotesHeight, 100, 35, 1200);
        if (!double.IsFinite(settings.Left) || !double.IsFinite(settings.Top))
        { settings.Left = settings.Top = -1; warnings.Add("无效的窗口位置已重置。"); }
        return warnings.Distinct().ToList();
    }
}
