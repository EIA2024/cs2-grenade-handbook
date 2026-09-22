using Handbook.Storage;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Handbook.Core;
using BindingSchema = Handbook.Core.InputBindings;

namespace Handbook.Desktop;

internal sealed class EditorWindow : Window
{
    private readonly AppController controller;
    private readonly TreeView tree = new();
    private readonly TextBlock title = Theme.Text("建立你的第一份道具手册", 23);
    private readonly TextBlock pathLabel = Theme.Text("从左侧合集开始，或导入已有 ZIP。", 12, Theme.Muted);
    private readonly TextBlock status = Theme.Text("就绪", 12, Theme.Accent);
    private readonly TextBlock validation = Theme.Text("", 12, Theme.Muted);
    private readonly TextBox notes = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 130 };
    private readonly Image standing = new() { Stretch = Stretch.Uniform }, aiming = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock standingHint = Theme.Text("选择或拖入站位图片", 13, Theme.Muted), aimingHint = Theme.Text("选择或拖入瞄准图片", 13, Theme.Muted);
    private readonly DispatcherTimer autosave = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private LibraryNode? selected;
    private bool loading, dirty, busy;
    internal TextBox NotesBox => notes;
    public EditorWindow(AppController controller)
    {
        this.controller = controller;
        Style = (Style)Application.Current.Resources[typeof(Window)];
        Title = "道具手册 · 资料编辑器"; Width = 1180; Height = 800; MinWidth = 850; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var shell = new DockPanel { Margin = new Thickness(22) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var brand = new StackPanel(); brand.Children.Add(Theme.Text("道具手册", 26, Theme.Accent)); brand.Children.Add(Theme.Text("整理一次，在需要的时候快速找到。", 12, Theme.Muted));
        var enter = Theme.Button("进入查看模式  ↗", () => controller.OpenViewer(true)); enter.Background = Theme.Accent; enter.Foreground = Theme.Background; enter.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(enter, Dock.Right); header.Children.Add(enter); header.Children.Add(brand); DockPanel.SetDock(header, Dock.Top); shell.Children.Add(header);
        var toolbar = new WrapPanel();
        toolbar.Children.Add(Theme.Button("新建合集", () => WithSaved(() => { var name = Dialogs.Prompt(this, "新建合集", "合集名称"); if (name != null) controller.Refresh(controller.Repository.CreateCollection(name)); })));
        toolbar.Children.Add(Theme.Button("导入 ZIP", async () => await ImportZip()));
        toolbar.Children.Add(Theme.Button("图片包收件箱", async () =>
        {
            var packages = Directory.GetFiles(controller.Repository.PackagesRoot, "*.zip").Order().ToList();
            if (packages.Count == 0) { MessageBox.Show("把 ZIP 放入：\n" + controller.Repository.PackagesRoot + "\n也可以使用“导入 ZIP”从其他位置选择。", "图片包收件箱"); return; }
            var chosen = Dialogs.Choose(this, "选择待导入 ZIP", packages, Path.GetFileName); if (chosen != null) await ImportZip(chosen);
        }));
        toolbar.Children.Add(Theme.Button("导出合集", async () => await ExportZip()));
        toolbar.Children.Add(Theme.Button("重新扫描", () => WithSaved(() => { controller.Refresh(selected?.Path); SetStatus("已重新扫描资料库。"); })));
        toolbar.Children.Add(Theme.Button("回收区", Restore));
        toolbar.Children.Add(Theme.Button("快捷键", () => controller.ConfigureHotkeys(this)));
        toolbar.Children.Add(Theme.Button("鼠标调整", controller.OpenAdjustment));
        toolbar.Children.Add(Theme.Button("使用说明", () => MessageBox.Show("进入查看模式后，菜单和双图始终鼠标穿透，不主动切换焦点。\n\n" + string.Join("\n", Enum.GetValues<InputCommand>().Select(c => controller.BindingLabel(c) + "：" + BindingSchema.Labels[c])) + "\n\n先打开菜单，再用上一项、下一项、确认浏览；返回项可以切换地图和阵营。\n要拖动或缩放面板，从编辑器或托盘进入“鼠标调整”。\n返回编辑器时停止后台输入接收。\n\n普通置顶不保证覆盖独占全屏。软件不感知或操作任何游戏。\n资料位置：" + controller.Repository.DataRoot, "使用说明")));
        DockPanel.SetDock(toolbar, Dock.Top); shell.Children.Add(toolbar);
        DockPanel.SetDock(status, Dock.Bottom); status.Margin = new Thickness(0, 12, 0, 0); shell.Children.Add(status);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290), MinWidth = 210 }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new DockPanel();
        var treeActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        treeActions.Children.Add(Theme.Button("＋ 地图", NewMap)); treeActions.Children.Add(Theme.Button("＋ 分类", () => NewChild(false))); treeActions.Children.Add(Theme.Button("＋ 道具", () => NewChild(true))); DockPanel.SetDock(treeActions, Dock.Top); sidebar.Children.Add(treeActions);
        var more = new WrapPanel(); more.Children.Add(Theme.Button("重命名", Rename)); more.Children.Add(Theme.Button("移动", () => Transfer(false))); more.Children.Add(Theme.Button("复制", () => Transfer(true))); more.Children.Add(Theme.Button("删除", Delete)); DockPanel.SetDock(more, Dock.Bottom); sidebar.Children.Add(more);
        sidebar.Children.Add(tree); body.Children.Add(sidebar);
        var divider = new GridSplitter { Width = 4, HorizontalAlignment = HorizontalAlignment.Center, Background = Theme.Panel }; Grid.SetColumn(divider, 1); body.Children.Add(divider);
        var detail = new Grid { Margin = new Thickness(8, 0, 0, 0) };
        detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); detail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 170 }); detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); detail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 130 }); detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        detail.Children.Add(title); Grid.SetRow(pathLabel, 1); detail.Children.Add(pathLabel);
        var imageGrid = new Grid(); imageGrid.ColumnDefinitions.Add(new ColumnDefinition()); imageGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var a = BuildImageCard("站位", standing, standingHint); var b = BuildImageCard("瞄准", aiming, aimingHint); Grid.SetColumn(b, 1); imageGrid.Children.Add(a); imageGrid.Children.Add(b); Grid.SetRow(imageGrid, 2); detail.Children.Add(imageGrid);
        var notesHeader = Theme.Text("说明  /  投掷方式、站位细节与注意事项", 13, Theme.Muted); notesHeader.Margin = new Thickness(0, 14, 0, 7); Grid.SetRow(notesHeader, 3); detail.Children.Add(notesHeader);
        Grid.SetRow(notes, 4); detail.Children.Add(notes);
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var openFolder = Theme.Button("打开所在文件夹", () =>
        {
            var target = selected?.Path ?? controller.Repository.LibraryRoot; controller.Repository.EnsurePath(target, true);
            // Explicit user action: open only OUR user-data directory in Explorer.
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }); DockPanel.SetDock(openFolder, Dock.Right); footer.Children.Add(openFolder); footer.Children.Add(validation); Grid.SetRow(footer, 5); detail.Children.Add(footer);
        Grid.SetColumn(detail, 2); body.Children.Add(detail); shell.Children.Add(body); Content = shell;
        tree.SelectedItemChanged += (_, _) =>
        {
            if (loading) return;
            if (!FlushNotes()) { ReloadSelectionOnly(); return; }
            if (tree.SelectedItem is TreeViewItem { Tag: LibraryNode node }) SelectNode(node);
        };
        notes.TextChanged += (_, _) => { if (loading || selected?.Kind != NodeKind.Entry) return; dirty = true; SetStatus("正在编辑…"); autosave.Stop(); autosave.Start(); };
        autosave.Tick += (_, _) => { autosave.Stop(); FlushNotes(); };
        Closing += (_, e) => { if (busy || !FlushNotes()) { e.Cancel = true; return; } autosave.Stop(); if (!controller.Exiting) { e.Cancel = true; Hide(); } };
        Reload();
    }
    private Border BuildImageCard(string role, Image image, TextBlock hint)
    {
        var dock = new DockPanel(); var header = new DockPanel();
        var add = Theme.Button("替换图片", () => SetImage(role, Dialogs.PickImage(this))); DockPanel.SetDock(add, Dock.Right); header.Children.Add(add); header.Children.Add(Theme.Text(role + "图", 16, Theme.Accent)); DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        var picture = new Grid(); picture.Children.Add(image); hint.HorizontalAlignment = HorizontalAlignment.Center; hint.VerticalAlignment = VerticalAlignment.Center; picture.Children.Add(hint); dock.Children.Add(picture);
        var card = Theme.Card(dock); card.Margin = new Thickness(0, 0, 8, 0); card.AllowDrop = true;
        card.DragOver += (_, e) => { e.Effects = selected?.Kind == NodeKind.Entry && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        card.Drop += (_, e) => { try { if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length == 1) SetImage(role, files[0]); } catch (Exception error) { MessageBox.Show(error.Message, "导入图片失败"); } };
        picture.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2 || image.Source == null) return;
            new Window { Title = role + "图预览", Width = 1000, Height = 700, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = new Image { Source = image.Source, Stretch = Stretch.Uniform } }.ShowDialog();
        }; return card;
    }
    private void SetImage(string role, string? source)
    {
        if (source == null) return; RequireSelection(NodeKind.Entry); if (!FlushNotes()) return;
        Images.Load(source, true); // Fully decode before copying, leaving the old image intact on corruption.
        controller.Repository.SetImage(selected!.Path, role, source); controller.Refresh(selected.Path); SetStatus(role + "图已保存。");
    }
    private void WithSaved(Action action) { if (!busy && FlushNotes()) action(); }
    private void RequireSelection(params NodeKind[] allowed)
    {
        if (selected == null || (allowed.Length > 0 && !allowed.Contains(selected.Kind))) throw new InvalidOperationException("请先在左侧选择对应的合集、分类或道具。");
    }
    private void NewMap() => WithSaved(() =>
    {
        RequireSelection(); var parts = Path.GetRelativePath(controller.Repository.LibraryRoot, selected!.Path).Split(Path.DirectorySeparatorChar); var parent = Path.Combine(controller.Repository.LibraryRoot, parts[0]);
        var name = Dialogs.Prompt(this, "新建地图", "英文地图名称（可自定义）\n" + string.Join(" / ", LibraryRepository.Maps), "Mirage");
        if (name != null) controller.Refresh(controller.Repository.CreateMap(parent, name));
    });
    private void NewChild(bool entry) => WithSaved(() =>
    {
        RequireSelection(NodeKind.Side, NodeKind.Folder, NodeKind.Entry);
        var name = Dialogs.Prompt(this, entry ? "新建道具" : "新建分类", "创建于：" + Path.GetRelativePath(controller.Repository.LibraryRoot, selected!.Path) + "\n名称由你自由定义");
        if (name != null) controller.Refresh(controller.Repository.CreateFolder(selected.Path, name, entry));
    });
    private void Rename() => WithSaved(() => { RequireSelection(); var name = Dialogs.Prompt(this, "重命名", "新名称", selected!.Name); if (name != null) controller.Refresh(controller.Repository.Rename(selected.Path, name)); });
    private static IEnumerable<LibraryNode> Flatten(IEnumerable<LibraryNode> roots)
    { foreach (var node in roots) { yield return node; foreach (var child in Flatten(node.Children)) yield return child; } }
    private void Transfer(bool copy) => WithSaved(() =>
    {
        RequireSelection(NodeKind.Folder, NodeKind.Entry);
        var target = Dialogs.Choose(this, copy ? "复制到" : "移动到", Flatten(controller.Snapshot.Roots).Where(n => n.Kind is NodeKind.Side or NodeKind.Folder or NodeKind.Entry), n => Path.GetRelativePath(controller.Repository.LibraryRoot, n.Path));
        if (target != null) controller.Refresh(controller.Repository.Transfer(selected!.Path, target.Path, copy));
    });
    private void Delete() => WithSaved(() =>
    {
        RequireSelection(); var parent = Path.GetDirectoryName(selected!.Path); controller.Repository.Recycle(selected.Path); selected = null; controller.Refresh(parent); SetStatus("已移入回收区，可以恢复。");
    });
    private void Restore() => WithSaved(() =>
    {
        var choice = Dialogs.Choose(this, "回收区 · 选择要恢复的内容", controller.Repository.GetRecycled(), i => $"{i.DeletedAt:g}  {i.OriginalRelativePath}");
        if (choice != null) controller.Refresh(controller.Repository.Restore(choice.Id));
    });
    private async Task ImportZip(string? path = null)
    {
        if (busy || !FlushNotes()) return;
        if (path == null)
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Title = "导入道具合集", Filter = "ZIP 合集|*.zip", InitialDirectory = controller.Repository.PackagesRoot };
            if (picker.ShowDialog(this) != true) return; path = picker.FileName;
        }
        busy = true; IsEnabled = false; SetStatus("正在校验并导入，请稍候…");
        try
        {
            // PackageService calls the injected image decoder before committing the package.
            var result = await Task.Run(() => controller.Packages.Import(path));
            controller.Refresh(result.CollectionPath); SetStatus(result.AlreadyImported ? "此 ZIP 已导入，已定位已有合集。" : "导入完成。" + string.Join("；", result.Warnings));
        }
        catch (Exception e) { SetStatus("导入失败，已有资料保持不变。"); MessageBox.Show(e.Message, "导入失败"); }
        finally { busy = false; IsEnabled = true; }
    }
    private async Task ExportZip()
    {
        if (busy || !FlushNotes()) return; RequireSelection();
        var rootName = Path.GetRelativePath(controller.Repository.LibraryRoot, selected!.Path).Split(Path.DirectorySeparatorChar)[0];
        var picker = new Microsoft.Win32.SaveFileDialog { Title = "导出为新 ZIP", Filter = "ZIP 合集|*.zip", FileName = rootName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip", InitialDirectory = controller.Repository.PackagesRoot };
        if (picker.ShowDialog(this) != true) return;
        busy = true; IsEnabled = false; SetStatus("正在导出…");
        try { await Task.Run(() => controller.Packages.Export(Path.Combine(controller.Repository.LibraryRoot, rootName), picker.FileName)); SetStatus("已导出：" + picker.FileName); }
        catch (Exception e) { MessageBox.Show(e.Message, "导出失败"); SetStatus("导出失败。"); }
        finally { busy = false; IsEnabled = true; }
    }
    public bool FlushNotes()
    {
        if (busy) { SetStatus("请等待导入或导出完成。"); return false; }
        if (!dirty || selected == null) return true;
        autosave.Stop();
        try { controller.Repository.SaveNotes(selected.Path, notes.Text); dirty = false; SetStatus("已保存 · " + DateTime.Now.ToString("HH:mm:ss")); return true; }
        catch (Exception e) { SetStatus("保存失败，编辑内容已保留：" + e.Message); return false; }
    }
    public void Reload(string? preferred = null)
    {
        if (!FlushNotes()) return;
        preferred ??= selected?.Path; loading = true; tree.Items.Clear();
        TreeViewItem? found = null;
        TreeViewItem Make(LibraryNode node)
        {
            var text = node.Kind switch { NodeKind.Collection => "▣  ", NodeKind.Map => "◇  ", NodeKind.Side => "▸  ", NodeKind.Entry => "▧  ", _ => "▹  " };
            var isDraft = node.Kind == NodeKind.Entry && controller.Snapshot.Entries.Any(e => e.Path == node.Path && !e.Ready);
            var item = new TreeViewItem { Header = text + node.Name + (isDraft ? "  · 草稿" : ""), Tag = node, IsExpanded = node.Kind is NodeKind.Collection or NodeKind.Map or NodeKind.Side || preferred?.StartsWith(node.Path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) == true };
            if (node.Path == preferred) found = item;
            foreach (var child in node.Children) item.Items.Add(Make(child)); return item;
        }
        foreach (var node in controller.Snapshot.Roots) tree.Items.Add(Make(node));
        if (found != null) { found.IsSelected = true; found.BringIntoView(); SelectNode((LibraryNode)found.Tag); }
        else { selected = null; SelectNode(null); }
        loading = false;
        if (controller.Snapshot.Errors.Count > 0) SetStatus("扫描提示：" + string.Join("；", controller.Snapshot.Errors.Take(3)));
    }
    private void ReloadSelectionOnly()
    {
        loading = true;
        void Walk(ItemsControl parent) { foreach (TreeViewItem item in parent.Items) { if (((LibraryNode)item.Tag).Path == selected?.Path) item.IsSelected = true; Walk(item); } }
        Walk(tree); loading = false;
    }
    private void SelectNode(LibraryNode? node)
    {
        var wasLoading = loading; loading = true; selected = node;
        if (!wasLoading && node != null && node.Kind != NodeKind.Entry) controller.SelectBrowseScope(node.Path);
        title.Text = node?.Name ?? "建立你的第一份道具手册";
        pathLabel.Text = node == null ? "新建合集，或导入已有 ZIP。" : Path.GetRelativePath(controller.Repository.LibraryRoot, node.Path);
        notes.IsEnabled = node?.Kind == NodeKind.Entry; notes.Text = ""; standing.Source = null; aiming.Source = null;
        standingHint.Text = "选择或拖入站位图片"; aimingHint.Text = "选择或拖入瞄准图片"; standingHint.Visibility = aimingHint.Visibility = Visibility.Visible;
        validation.Text = node == null ? "支持 PNG / JPG · 本地保存 · 无游戏连接" : "可在左侧继续创建分类和道具。";
        if (node?.Kind == NodeKind.Entry)
        {
            var entry = controller.Repository.ReadEntry(node.Path); notes.Text = entry.Notes;
            void Preview(Image image, TextBlock hint, string? path)
            {
                try { image.Source = Images.Load(path); if (image.Source != null) hint.Visibility = Visibility.Collapsed; }
                catch (Exception e) { hint.Text = "图片无法显示：" + e.Message; }
            }
            Preview(standing, standingHint, entry.Standing); Preview(aiming, aimingHint, entry.Aiming);
            validation.Text = entry.Ready ? "双图齐全 · 可在查看模式中选择" : "草稿 · " + string.Join("；", entry.Errors);
            if (entry.Ready) controller.Remember(entry, !wasLoading);
        }
        dirty = false; loading = wasLoading;
    }
    private void SetStatus(string text) => status.Text = text;
}

