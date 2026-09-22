using Handbook.Storage;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Handbook.Core;
using Forms = System.Windows.Forms;

namespace Handbook.Desktop;

internal sealed class AppController : IDisposable
{
    public LibraryRepository Repository { get; }
    public PackageService Packages { get; }
    public UserSettings Settings { get; }
    public LibrarySnapshot Snapshot { get; private set; }
    public ViewerWindow Viewer { get; }
    private EditorWindow? editor;
    private readonly IInputCommandService input;
    private readonly Forms.NotifyIcon tray;
    private readonly Application app;
    private readonly DispatcherTimer settingTimer;
    public bool Exiting { get; private set; }
    public bool IsUsing { get; private set; }
    internal bool InputActive => input.Active;
    public AppController(LibraryRepository repository, Application app)
    {
        this.app = app; Repository = repository; Packages = new(repository); Settings = repository.LoadSettings();
        var warning = InputBindings.Migrate(Settings, BindingNames.ParseLegacy);
        if (repository.SettingsWarnings.Count > 0)
            warning = string.Join("\n", repository.SettingsWarnings.Append(warning).Where(w => !string.IsNullOrEmpty(w)));
        Snapshot = repository.Scan();
        settingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        settingTimer.Tick += (_, _) => { settingTimer.Stop(); Safe(() => Repository.SaveSettings(Settings)); };
        Viewer = new ViewerWindow(this);
        input = new RawInputCommandService(Viewer, Settings.InputBindings!);
        input.Command += command => { if (IsUsing) Viewer.HandleCommand(command); };
        input.Failed += message => { IsUsing = false; Viewer.ShowInputError(message); };
        tray = new Forms.NotifyIcon { Text = "道具手册 · 后台快捷浏览", Icon = System.Drawing.SystemIcons.Information, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("进入后台浏览", null, (_, _) => Safe(() => OpenViewer(true)));
        menu.Items.Add("隐藏 / 显示", null, (_, _) => Safe(() => { if (!IsUsing) OpenViewer(); else Viewer.ToggleVisibility(); }));
        menu.Items.Add("鼠标调整面板", null, (_, _) => Safe(OpenAdjustment));
        menu.Items.Add("打开资料编辑器", null, (_, _) => Safe(OpenEditor));
        menu.Items.Add("恢复面板位置", null, (_, _) => Safe(Viewer.ResetPosition));
        menu.Items.Add("退出", null, (_, _) => Safe(Exit));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Safe(OpenEditor);
        if (warning != null) app.Dispatcher.BeginInvoke(() => ReportError(warning));
    }
    private void Safe(Action action) { try { action(); } catch (Exception e) { ReportError(e.Message); } }
    public void ReportError(string message)
    {
        if (IsUsing || (Viewer.IsVisible && Viewer.Mode != PresentationMode.Adjusting)) Viewer.ShowInputError(message);
        else MessageBox.Show(message, "操作未完成");
    }
    public void PersistSoon() { settingTimer.Stop(); settingTimer.Start(); }
    public string BindingLabel(InputCommand command) => BindingNames.Display(Settings.InputBindings![command]);
    public void Remember(Entry entry, bool resetBrowse = false)
    {
        var relative = Path.GetRelativePath(Repository.LibraryRoot, entry.Path); Settings.SelectedPath = relative;
        if (resetBrowse) { Settings.BrowseFolder = ""; Settings.BrowseItem = ""; }
        Settings.Recent.RemoveAll(p => p.Equals(relative, StringComparison.OrdinalIgnoreCase)); Settings.Recent.Insert(0, relative);
        if (Settings.Recent.Count > 12) Settings.Recent.RemoveRange(12, Settings.Recent.Count - 12); PersistSoon();
    }
    public void SelectBrowseScope(string path)
    {
        Settings.BrowseFolder = Path.GetRelativePath(Repository.LibraryRoot, Repository.EnsurePath(path));
        Settings.BrowseItem = "$scope"; PersistSoon();
    }
    public bool FlushEditor() => editor?.FlushNotes() ?? true;
    public void Refresh(string? selected = null)
    {
        Snapshot = Repository.Scan(); Images.Clear(); editor?.Reload(selected); Viewer.RefreshLibrary();
    }
    private string? StopInput()
    {
        try { input.Stop(); return null; }
        catch (Exception e) { return e.Message; }
        finally { IsUsing = false; }
    }
    public void OpenEditor()
    {
        var inputError = StopInput(); Viewer.SetMode(PresentationMode.Viewing); Viewer.Hide();
        if (editor == null) { editor = new EditorWindow(this); editor.Closed += (_, _) => editor = null; }
        editor.Show(); editor.WindowState = WindowState.Normal; editor.Activate();
        if (inputError != null) ReportError(inputError);
    }
    public void OpenViewer(bool browse = false)
    {
        if (!FlushEditor()) return;
        Refresh();
        var entry = Snapshot.Entries.FirstOrDefault(e => Path.GetRelativePath(Repository.LibraryRoot, e.Path) == Settings.SelectedPath && e.Ready) ?? Snapshot.Entries.FirstOrDefault(e => e.Ready);
        Viewer.Present(entry); Viewer.SetMode(PresentationMode.Viewing);
        editor?.Hide(); IsUsing = true;
        try { input.Start(); Viewer.ClearInputError(); }
        catch (Exception e) { IsUsing = false; Viewer.ShowInputError(e.Message); return; }
        Viewer.Show();
        if (browse || entry == null) Viewer.HandleCommand(InputCommand.ToggleMenu);
    }
    public void OpenAdjustment()
    {
        if (!FlushEditor()) return;
        var inputError = StopInput(); Refresh(); editor?.Hide();
        if (Viewer.CurrentPath == null)
            Viewer.Present(Snapshot.Entries.FirstOrDefault(e => Path.GetRelativePath(Repository.LibraryRoot, e.Path) == Settings.SelectedPath && e.Ready) ?? Snapshot.Entries.FirstOrDefault(e => e.Ready));
        Viewer.SetMode(PresentationMode.Adjusting); Viewer.Show(); Viewer.Activate();
        if (inputError != null) ReportError(inputError);
    }
    public void ConfigureHotkeys(Window owner)
    {
        if (IsUsing && StopInput() is { } inputError) ReportError(inputError);
        new InputBindingDialog(owner, Settings.InputBindings!, CommitBindings).ShowDialog();
    }
    internal void CommitBindings(IReadOnlyDictionary<InputCommand, InputBinding> draft)
    {
        InputBindings.Validate(draft);
        // Persist a copy before changing either live settings or the input matcher.
        var candidate = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(Settings))!;
        candidate.SettingsVersion = 2; candidate.InputBindings = new(draft);
        Repository.SaveSettings(candidate);
        input.SetBindings(candidate.InputBindings);
        Settings.InputBindings = candidate.InputBindings; Settings.SettingsVersion = 2;
        Viewer.RefreshBindingHints();
    }
    public void Exit()
    {
        if (!FlushEditor()) return;
        settingTimer.Stop();
        var errors = new List<string>();
        if (StopInput() is { } inputError) errors.Add(inputError);
        try { Repository.SaveSettings(Settings); }
        catch (Exception e) { errors.Add("本次窗口及浏览设置未能保存：" + e.Message); }
        // A settings permission failure must never trap the user in a running receiver.
        // Unsaved document text is handled by FlushEditor above, before shutdown begins.
        Exiting = true;
        try
        {
            if (errors.Count > 0) MessageBox.Show(string.Join("\n", errors) + "\n软件仍将退出。", "退出提示");
        }
        finally { app.Shutdown(); }
    }
    public void Dispose()
    {
        settingTimer.Stop(); tray.Visible = false; tray.Dispose();
        // Native unregister errors have already been surfaced during the transition.
        // Disposal still releases the window callback and unmanaged buffer.
        try { input.Dispose(); } catch (System.ComponentModel.Win32Exception) { }
    }
}

