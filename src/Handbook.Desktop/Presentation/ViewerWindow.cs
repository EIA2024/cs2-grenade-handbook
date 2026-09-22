using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Handbook.Core;

namespace Handbook.Desktop;

internal sealed class ViewerWindow : Window, IPresentationHost
{
    private readonly AppController controller;
    private readonly Grid main = new();
    private readonly StackPanel controls = new();
    private readonly ComboBox collection = new(), map = new(), side = new();
    private readonly ComboBox location = new() { MinHeight = 26, Margin = new Thickness(0, 0, 0, 5), ToolTip = "位置 / 自定义分类" };
    private readonly TextBox search = new() { ToolTip = "搜索道具名或目录路径" };
    private readonly ListBox list = new() { MaxHeight = 130, DisplayMemberPath = "Label" };
    private readonly TextBlock heading = Theme.Text("道具手册", 14, Theme.Accent);
    private readonly Grid images = new();
    private readonly Image standing = new() { Stretch = Stretch.Uniform }, aiming = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock notes = Theme.Text("请先选择道具。", 13);
    private readonly TextBlock standingError = Theme.Text("站位图"), aimingError = Theme.Text("瞄准图");
    private readonly Thumb resize = new() { Width = 20, Height = 12, Cursor = Cursors.SizeNWSE, HorizontalAlignment = HorizontalAlignment.Right, Background = Theme.Muted };
    private readonly GridSplitter splitter = new() { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Theme.Muted, ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
    private readonly ScrollViewer notesScroll;
    private bool selecting, refreshing;
    private readonly QuickNavigation navigation = new();
    private readonly Border browsePanel = new() { Background = Theme.Background, BorderBrush = Theme.Accent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10), Visibility = Visibility.Collapsed };
    private readonly TextBlock browsePath = Theme.Text("", 12, Theme.Muted);
    private readonly StackPanel browseRows = new();
    private readonly TextBlock browseHints = Theme.Text("", 11, Theme.Muted);
    private readonly TextBlock browseCounter = Theme.Text("", 12, Theme.Accent);
    private readonly TextBlock inputError = Theme.Text("", 12, Brushes.Orange);
    public PresentationMode Mode { get; private set; } = PresentationMode.Viewing;
    internal QuickNavigation Navigation => navigation;
    private Entry? current;
    internal bool Selecting => selecting;
    internal string? CurrentPath => current?.Path;
    public ViewerWindow(AppController controller)
    {
        this.controller = controller; var settings = controller.Settings;
        Style = (Style)Application.Current.Resources[typeof(Window)];
        Title = "道具手册 · 查看"; Width = Math.Clamp(settings.Width, 380, 1400); Height = Math.Clamp(settings.Height, 280, 1200); MinWidth = 380; MinHeight = 280;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = !settings.Topmost; ShowActivated = false; Topmost = settings.Topmost; Opacity = Math.Clamp(settings.Opacity, .35, 1);
        Left = settings.Left; Top = settings.Top;
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 50 });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Clamp(settings.NotesHeight, 45, 250)), MinHeight = 35 });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        heading.Margin = new Thickness(0, 0, 0, 6); heading.Cursor = Cursors.SizeAll;
        heading.MouseLeftButtonDown += (_, e) => { if (selecting && e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        main.Children.Add(heading);
        BuildControls(); Grid.SetRow(controls, 1); main.Children.Add(controls);
        images.Children.Add(ImageCard("站位", standing, standingError)); images.Children.Add(ImageCard("瞄准", aiming, aimingError)); Grid.SetRow(images, 2); main.Children.Add(images); SetImageLayout();
        Grid.SetRow(splitter, 3); main.Children.Add(splitter);
        notesScroll = new ScrollViewer { Content = notes, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(2, 8, 2, 0) }; Grid.SetRow(notesScroll, 4); main.Children.Add(notesScroll);
        Grid.SetRow(resize, 5); main.Children.Add(resize);
        resize.DragDelta += (_, e) => { Width = Math.Max(MinWidth, ActualWidth + e.HorizontalChange); Height = Math.Max(MinHeight, ActualHeight + e.VerticalChange); settings.Height = Math.Max(280, Height - controls.ActualHeight); controller.PersistSoon(); };
        splitter.DragCompleted += (_, _) => { settings.NotesHeight = main.RowDefinitions[4].ActualHeight; controller.PersistSoon(); };
        var browser = new DockPanel(); DockPanel.SetDock(browsePath, Dock.Top); browser.Children.Add(browsePath);
        DockPanel.SetDock(browseHints, Dock.Bottom); browser.Children.Add(browseHints);
        DockPanel.SetDock(browseCounter, Dock.Bottom); browser.Children.Add(browseCounter);
        browser.Children.Add(new ScrollViewer { Content = browseRows, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, IsHitTestVisible = false }); browsePanel.Child = browser;
        var layers = new Grid(); layers.Children.Add(main); layers.Children.Add(browsePanel);
        inputError.Visibility = Visibility.Collapsed; inputError.Background = Theme.Background; inputError.VerticalAlignment = VerticalAlignment.Bottom; layers.Children.Add(inputError);
        Content = new Border { Child = layers, Background = Theme.Background, BorderBrush = Theme.Accent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(12) };
        SourceInitialized += (_, _) => NativeWindow.SetPassThrough(this, !selecting);
        Loaded += (_, _) => ClampPosition();
        SizeChanged += (_, _) => SaveGeometry(); LocationChanged += (_, _) => SaveGeometry();
        Closing += (_, e) => { if (!controller.Exiting) { e.Cancel = true; Hide(); } };
        RefreshLibrary();
        SetMode(PresentationMode.Viewing);
    }
    private Border ImageCard(string role, Image image, TextBlock error)
    {
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition());
        var label = Theme.Text(role, 11, Theme.Muted); label.Margin = new Thickness(0, 0, 0, 4); grid.Children.Add(label);
        Grid.SetRow(image, 1); grid.Children.Add(image); Grid.SetRow(error, 1); error.VerticalAlignment = VerticalAlignment.Center; error.HorizontalAlignment = HorizontalAlignment.Center; grid.Children.Add(error);
        var card = new Border { Background = Theme.Panel, Padding = new Thickness(6), Margin = new Thickness(2), Child = grid };
        card.MouseLeftButtonDown += (_, e) => { if (selecting && e.ClickCount == 2) Enlarge(role == "站位" ? current?.Standing : current?.Aiming); }; return card;
    }
    private void Enlarge(string? path)
    {
        if (path == null) return;
        try
        {
            var preview = new Window { Title = "图片放大 · " + Path.GetFileName(path), Width = 1000, Height = 700, Owner = this, Topmost = Topmost, Content = new Image { Source = Images.Load(path, true), Stretch = Stretch.Uniform }, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            preview.ShowDialog();
        }
        catch (Exception e) { MessageBox.Show(e.Message, "图片无法显示"); }
    }
    private void BuildControls()
    {
        var filters = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        foreach (var box in new[] { collection, map, side })
        {
            filters.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetColumn(box, filters.ColumnDefinitions.Count - 1); box.Margin = new Thickness(0, 0, 4, 0); box.MinHeight = 26; filters.Children.Add(box);
        }
        collection.ToolTip = "合集"; map.ToolTip = "地图"; side.ToolTip = "阵营";
        collection.SelectionChanged += (_, _) => { if (!refreshing) RefreshMaps(); };
        map.SelectionChanged += (_, _) => { if (!refreshing) RefreshSides(); };
        side.SelectionChanged += (_, _) => { if (!refreshing) RefreshLocations(); };
        controls.Children.Add(filters);
        controls.Children.Add(location);
        location.SelectionChanged += (_, _) => { if (!refreshing) RefreshEntries(); };
        var find = new DockPanel { Margin = new Thickness(0, 0, 0, 5) }; var searchLabel = Theme.Text("搜索  ", 12, Theme.Muted); searchLabel.VerticalAlignment = VerticalAlignment.Center; find.Children.Add(searchLabel); find.Children.Add(search); controls.Children.Add(find);
        search.TextChanged += (_, _) => RefreshEntries();
        list.SelectionChanged += (_, _) => { if (!refreshing && list.SelectedItem is Entry entry) Present(entry); };
        list.MouseDoubleClick += (_, _) => { if (current != null) controller.OpenViewer(); }; controls.Children.Add(list);
        var actions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        actions.Children.Add(Theme.Button("完成调整", () => controller.OpenViewer()));
        actions.Children.Add(Theme.Button("横 / 竖", () => { controller.Settings.Vertical = !controller.Settings.Vertical; SetImageLayout(); controller.PersistSoon(); }));
        actions.Children.Add(Theme.Button("置顶 / 普通", () => SetTopmost(!Topmost)));
        actions.Children.Add(Theme.Button("编辑器", controller.OpenEditor));
        actions.Children.Add(Theme.Button("最近", () =>
        {
            var recent = controller.Settings.Recent.Select(p => controller.Snapshot.Entries.FirstOrDefault(e => Path.GetRelativePath(controller.Repository.LibraryRoot, e.Path) == p && e.Ready)).OfType<Entry>();
            var choice = Dialogs.Choose(this, "最近查看", recent, e => $"{e.Collection} / {e.Map} / {e.Side} / {e.RelativePath}"); if (choice != null) Present(choice);
        })); controls.Children.Add(actions);
        var opacityPanel = new DockPanel(); opacityPanel.Children.Add(Theme.Text("透明度  ", 11, Theme.Muted));
        var opacity = new Slider { Minimum = .35, Maximum = 1, Value = Opacity, Width = 140, HorizontalAlignment = HorizontalAlignment.Left };
        opacity.ValueChanged += (_, _) => { Opacity = opacity.Value; controller.Settings.Opacity = opacity.Value; controller.PersistSoon(); }; opacityPanel.Children.Add(opacity); controls.Children.Add(opacityPanel);
    }
    private void SetImageLayout()
    {
        images.RowDefinitions.Clear(); images.ColumnDefinitions.Clear();
        if (controller.Settings.Vertical) { images.RowDefinitions.Add(new RowDefinition()); images.RowDefinitions.Add(new RowDefinition()); }
        else { images.ColumnDefinitions.Add(new ColumnDefinition()); images.ColumnDefinitions.Add(new ColumnDefinition()); }
        for (int i = 0; i < 2; i++) { Grid.SetRow(images.Children[i], controller.Settings.Vertical ? i : 0); Grid.SetColumn(images.Children[i], controller.Settings.Vertical ? 0 : i); }
    }
    public void RefreshLibrary()
    {
        navigation.Update(controller.Snapshot);
        var old = collection.SelectedItem as string; refreshing = true;
        collection.ItemsSource = controller.Snapshot.Entries.Where(e => e.Ready).Select(e => e.Collection).Distinct().Order().ToList();
        collection.SelectedItem = old; if (collection.SelectedIndex < 0) collection.SelectedIndex = 0; refreshing = false; RefreshMaps();
        if (current != null)
        {
            var updated = controller.Snapshot.Entries.FirstOrDefault(e => e.Path == current.Path && e.Ready);
            Present(updated);
        }
    }
    private void RefreshMaps()
    {
        var old = map.SelectedItem as string; refreshing = true; map.ItemsSource = controller.Snapshot.Entries.Where(e => e.Ready && e.Collection == collection.SelectedItem as string).Select(e => e.Map).Distinct().Order().ToList(); map.SelectedItem = old; if (map.SelectedIndex < 0) map.SelectedIndex = 0; refreshing = false; RefreshSides();
    }
    private void RefreshSides()
    {
        var old = side.SelectedItem as string; refreshing = true; side.ItemsSource = controller.Snapshot.Entries.Where(e => e.Ready && e.Collection == collection.SelectedItem as string && e.Map == map.SelectedItem as string).Select(e => e.Side).Distinct().Order().ToList(); side.SelectedItem = old; if (side.SelectedIndex < 0) side.SelectedIndex = 0; refreshing = false; RefreshLocations();
    }
    private void RefreshLocations()
    {
        var old = location.SelectedItem as string;
        var folders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in controller.Snapshot.Entries.Where(e => e.Ready && e.Collection == collection.SelectedItem as string && e.Map == map.SelectedItem as string && e.Side == side.SelectedItem as string))
        {
            var parts = entry.RelativePath.Split(" / ");
            for (int i = 1; i < parts.Length; i++) folders.Add(string.Join(" / ", parts.Take(i)));
        }
        refreshing = true; location.ItemsSource = new[] { "全部位置" }.Concat(folders).ToList(); location.SelectedItem = old; if (location.SelectedIndex < 0) location.SelectedIndex = 0; refreshing = false; RefreshEntries();
    }
    private void RefreshEntries()
    {
        if (refreshing) return; refreshing = true;
        var folder = location.SelectedItem as string;
        list.ItemsSource = controller.Snapshot.Entries.Where(e => e.Ready && e.Collection == collection.SelectedItem as string && e.Map == map.SelectedItem as string && e.Side == side.SelectedItem as string && (folder is null or "全部位置" || e.RelativePath.StartsWith(folder + " / ", StringComparison.OrdinalIgnoreCase)) && e.RelativePath.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.RelativePath).ToList();
        list.SelectedItem = current; refreshing = false;
    }
    public void Present(Entry? entry)
    {
        current = entry; heading.Text = entry == null ? "道具手册 · 请先在编辑器添加双图道具" : $"{entry.Map} · {entry.Side} · {entry.Name}";
        SetImage(standing, standingError, entry?.Standing); SetImage(aiming, aimingError, entry?.Aiming); notes.Text = entry?.Notes is { Length: > 0 } text ? text : "暂无说明";
        if (entry != null)
        {
            controller.Remember(entry);
            collection.SelectedItem = entry.Collection; map.SelectedItem = entry.Map; side.SelectedItem = entry.Side;
        }
    }
    private static void SetImage(Image image, TextBlock error, string? path)
    {
        image.Source = null;
        try { image.Source = Images.Load(path); error.Text = path == null ? "未选择图片" : ""; }
        catch (Exception e) { error.Text = "图片无法显示：" + e.Message; }
        error.Visibility = image.Source == null ? Visibility.Visible : Visibility.Collapsed;
    }
    public void SetMode(PresentationMode mode)
    {
        var old = Mode; Mode = mode; selecting = mode == PresentationMode.Adjusting;
        controls.Visibility = selecting ? Visibility.Visible : Visibility.Collapsed; resize.Visibility = selecting ? Visibility.Visible : Visibility.Collapsed;
        splitter.Visibility = selecting ? Visibility.Visible : Visibility.Collapsed; notesScroll.VerticalScrollBarVisibility = selecting ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
        browsePanel.Visibility = mode == PresentationMode.Browsing ? Visibility.Visible : Visibility.Collapsed;
        if (mode != PresentationMode.Browsing) navigation.Cancel();
        if (selecting && Height < 620) Height = 620;
        if (old == PresentationMode.Adjusting && !selecting) Height = Math.Max(280, controller.Settings.Height);
        ShowActivated = selecting; NativeWindow.SetPassThrough(this, !selecting);
        // Intentionally no Activate/Focus here, even when opening the menu.
    }
    public void HandleCommand(InputCommand command)
    {
        if (Mode == PresentationMode.Adjusting) return;
        if (command == InputCommand.ToggleVisibility) { ToggleVisibility(); return; }
        if (command == InputCommand.ToggleMenu)
        {
            if (Mode == PresentationMode.Browsing) { SaveBrowsePosition(); navigation.Cancel(); SetMode(PresentationMode.Viewing); }
            else
            {
                string? savedFolder = null, savedItem = null;
                if (controller.Settings.BrowseItem.Length > 0)
                {
                    try
                    {
                        savedFolder = controller.Settings.BrowseFolder.Length == 0 ? "" : controller.Repository.EnsurePath(Path.Combine(controller.Repository.LibraryRoot, controller.Settings.BrowseFolder));
                        savedItem = controller.Settings.BrowseItem.StartsWith('$') ? controller.Settings.BrowseItem : controller.Repository.EnsurePath(Path.Combine(controller.Repository.LibraryRoot, controller.Settings.BrowseItem));
                    }
                    catch (InvalidDataException) { savedFolder = savedItem = null; }
                }
                navigation.Open(current?.Path, savedFolder, savedItem); SetMode(PresentationMode.Browsing); RenderBrowse();
                if (!IsVisible) { ShowActivated = false; Show(); }
            }
            return;
        }
        if (Mode != PresentationMode.Browsing) return;
        if (command == InputCommand.Previous) navigation.Move(-1);
        else if (command == InputCommand.Next) navigation.Move(1);
        else if (command == InputCommand.Confirm && navigation.Confirm() is { } chosen)
        { SaveBrowsePosition(); Present(chosen); SetMode(PresentationMode.Viewing); return; }
        SaveBrowsePosition(); RenderBrowse();
    }
    private void SaveBrowsePosition()
    {
        controller.Settings.BrowseFolder = navigation.Folder.Length == 0 ? "" : Path.GetRelativePath(controller.Repository.LibraryRoot, navigation.Folder);
        var id = navigation.Selected?.Id ?? "";
        controller.Settings.BrowseItem = id.Length == 0 || id.StartsWith('$') ? id : Path.GetRelativePath(controller.Repository.LibraryRoot, id);
        controller.PersistSoon();
    }
    public void RefreshBindingHints()
    {
        browseHints.Text = $"{controller.BindingLabel(InputCommand.Previous)} 上一项 · {controller.BindingLabel(InputCommand.Next)} 下一项\n{controller.BindingLabel(InputCommand.Confirm)} 确认 · {controller.BindingLabel(InputCommand.ToggleMenu)} 取消";
    }
    private void RenderBrowse()
    {
        browsePath.Text = navigation.Breadcrumb; browsePath.TextTrimming = TextTrimming.CharacterEllipsis; browsePath.TextWrapping = TextWrapping.NoWrap;
        browsePath.ToolTip = navigation.Breadcrumb; browseRows.Children.Clear();
        int count = navigation.Items.Count;
        int visible = Math.Clamp((int)((Height - 145) / 28), 1, 7);
        int start = Math.Clamp(navigation.Index - visible / 2, 0, Math.Max(0, count - visible));
        for (int i = start; i < Math.Min(count, start + visible); i++)
        {
            var item = navigation.Items[i]; bool highlighted = i == navigation.Index;
            var text = new TextBlock { Text = (highlighted ? "›  " : "   ") + item.Label, Foreground = highlighted ? Theme.Background : Theme.Ink, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            browseRows.Children.Add(new Border { Height = 28, Padding = new Thickness(6, 0, 6, 0), Background = highlighted ? Theme.Accent : Theme.Panel, CornerRadius = new CornerRadius(3), Child = text });
        }
        if (count == 0) browseRows.Children.Add(Theme.Text("没有可用道具。请在编辑器补齐双图；菜单键可关闭。", 13, Theme.Muted));
        browseCounter.Text = count == 0 ? "0 / 0" : $"{navigation.Index + 1} / {count}  ·  高亮只是候选，确认才换图";
        RefreshBindingHints();
    }
    public void ShowInputError(string message)
    {
        SetMode(PresentationMode.Viewing); inputError.Text = message + "\n可从系统托盘返回编辑器或鼠标调整。"; inputError.Visibility = Visibility.Visible;
        ShowActivated = false; if (!IsVisible) Show();
    }
    public void ClearInputError() => inputError.Visibility = Visibility.Collapsed;
    public void SetTopmost(bool enabled) { Topmost = enabled; ShowInTaskbar = !enabled; controller.Settings.Topmost = enabled; controller.PersistSoon(); }
    public void ToggleVisibility()
    {
        if (Mode == PresentationMode.Browsing) SaveBrowsePosition();
        SetMode(PresentationMode.Viewing);
        if (IsVisible) Hide(); else { ShowActivated = false; ClampPosition(); Show(); }
    }
    public void ResetPosition()
    {
        var area = SystemParameters.WorkArea; Left = Math.Max(area.Left, area.Right - Width - 24); Top = area.Top + 24; if (!IsVisible) { ShowActivated = false; Show(); } SaveGeometry();
    }
    private void ClampPosition()
    {
        var area = SystemParameters.WorkArea;
        if (!double.IsFinite(Left) || !double.IsFinite(Top) || Left < SystemParameters.VirtualScreenLeft || Top < SystemParameters.VirtualScreenTop || Left + Width > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth || Top + Height > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        { Left = Math.Max(area.Left, area.Right - Width - 24); Top = area.Top + 24; }
    }
    private void SaveGeometry()
    {
        if (!IsLoaded) return;
        var s = controller.Settings; s.Left = Left; s.Top = Top; s.Width = Width; if (!selecting) s.Height = Height; controller.PersistSoon();
    }
}
