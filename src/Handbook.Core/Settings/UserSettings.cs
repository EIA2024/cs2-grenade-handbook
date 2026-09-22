namespace Handbook.Core;

public sealed class UserSettings
{
    public int SettingsVersion { get; set; } = 1;
    public Dictionary<InputCommand, InputBinding>? InputBindings { get; set; }
    public string BrowseFolder { get; set; } = "";
    public string BrowseItem { get; set; } = "";
    public double Left { get; set; } = -1;
    public double Top { get; set; } = -1;
    public double Width { get; set; } = 520;
    public double Height { get; set; } = 380;
    public double Opacity { get; set; } = 1;
    public double NotesHeight { get; set; } = 100;
    public bool Vertical { get; set; }
    public bool Topmost { get; set; } = true;
    public string SelectedPath { get; set; } = "";
    public List<string> Recent { get; set; } = [];
    public string SelectionHotkey { get; set; } = "Ctrl+Alt+Q";
    public string VisibilityHotkey { get; set; } = "Ctrl+Alt+H";
}

