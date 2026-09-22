using Handbook.Storage;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Handbook.Core;

namespace Handbook.Desktop;

internal static class SelfTests
{
    public static int Run()
    {
        var root = Path.Combine(Environment.CurrentDirectory, "测试结果", DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(root);
        var log = new List<string>(); int failures = 0;
        void Test(string name, Action test)
        {
            try { test(); log.Add("PASS " + name); }
            catch (Exception e) { failures++; log.Add("FAIL " + name + ": " + e); }
        }
        void Assert(bool condition, string detail = "Assertion failed") { if (!condition) throw new InvalidOperationException(detail); }
        void Reject(Action action) { bool threw = false; try { action(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or ArgumentException or InvalidOperationException) { threw = true; } Assert(threw, "Expected rejection"); }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Theme.Install(app);
        var repo = new LibraryRepository(Path.Combine(root, "资料测试"), Images.Validate); var service = new PackageService(repo);
        var standing = Path.Combine(root, "站位.png"); var aiming = Path.Combine(root, "瞄准.jpg");
        MakeImage(standing, "站位示意 / PLACEHOLDER", "站在示例标记附近 · 非真实投掷教程", false);
        MakeImage(aiming, "瞄准示意 / PLACEHOLDER", "瞄准示例标记 · 非真实投掷教程", true);
        string entry = "", collection = "";
        Test("GUI-compatible repository creates hierarchy and both sides", () =>
        {
            collection = repo.CreateCollection("示例道具合集"); var map = repo.CreateMap(collection, "Dust2"); Assert(Directory.Exists(Path.Combine(map, "CT")) && Directory.Exists(Path.Combine(map, "T")));
            var folder = repo.CreateFolder(Path.Combine(map, "CT"), "A大", false); entry = repo.CreateFolder(folder, "防守烟（占位示例）", true);
            Assert(!repo.ReadEntry(entry).Ready); repo.SetImage(entry, "站位", standing); repo.SetImage(entry, "瞄准", aiming); repo.SaveNotes(entry, "【占位示例，不是实际投掷教程】\n站位：在这里填写你的站位细节。\n瞄准：在这里填写瞄准位置。\n投掷：在这里填写左键、右键或跳投操作。\n请自行提供真实、已验证的图片与说明。"); Assert(repo.ReadEntry(entry).Ready);
        });
        Test("Unicode notes, mixed PNG/JPEG and nested folders survive ZIP roundtrip", () =>
        {
            var zip = Path.Combine(root, "示例道具合集.zip"); service.Export(collection, zip); var before = File.ReadAllBytes(zip);
            var other = new LibraryRepository(Path.Combine(root, "重导入")); var packages = new PackageService(other); var result = packages.Import(zip); var snapshot = other.Scan(); Assert(snapshot.Entries.Count == 1 && snapshot.Entries[0].Ready); Assert(snapshot.Entries[0].Notes == repo.ReadEntry(entry).Notes); Assert(File.ReadAllBytes(snapshot.Entries[0].Standing!).SequenceEqual(File.ReadAllBytes(standing))); Assert(File.ReadAllBytes(snapshot.Entries[0].Aiming!).SequenceEqual(File.ReadAllBytes(aiming))); Assert(packages.Import(zip).AlreadyImported); Assert(before.SequenceEqual(File.ReadAllBytes(zip))); Assert(Directory.Exists(Path.Combine(result.CollectionPath, "Dust2", "T")));
        });
        Test("Duplicate names, traversal and reserved filenames are rejected", () =>
        {
            Reject(() => repo.CreateCollection("示例道具合集")); Reject(() => repo.CreateCollection("../outside")); Reject(() => repo.CreateCollection("CON")); Reject(() => repo.EnsurePath(Path.GetTempPath())); Reject(() => repo.Rename(Path.Combine(collection, "Dust2", "CT"), "Other"));
        });
        Test("Move/copy/rename and recycle/restore preserve contents", () =>
        {
            var target = repo.CreateFolder(Path.Combine(collection, "Dust2", "T"), "测试分类", false); var copy = repo.Transfer(entry, target, true); Assert(repo.ReadEntry(copy).Notes == repo.ReadEntry(entry).Notes);
            copy = repo.Rename(copy, "复制条目"); var moved = repo.Transfer(copy, Path.Combine(collection, "Dust2", "T"), false); Assert(!Directory.Exists(copy)); repo.Recycle(moved); Assert(!Directory.Exists(moved)); var item = repo.GetRecycled().First(); Assert(repo.Restore(item.Id) == moved); Assert(repo.ReadEntry(moved).Ready); repo.Recycle(moved);
            Reject(() => repo.Transfer(entry, entry, true));
        });
        Test("Duplicate image roles become drafts and invalid images preserve originals", () =>
        {
            var extra = Path.Combine(entry, "站位.jpg"); File.Copy(aiming, extra); Assert(!repo.ReadEntry(entry).Ready); File.Delete(extra);
            var bad = Path.Combine(root, "bad.png"); File.WriteAllText(bad, "not an image"); var before = File.ReadAllBytes(Path.Combine(entry, "站位.png")); Reject(() => repo.SetImage(entry, "站位", bad)); Assert(before.SequenceEqual(File.ReadAllBytes(Path.Combine(entry, "站位.png"))));
        });
        Test("Failed save preserves existing notes", () =>
        {
            var before = repo.ReadEntry(entry).Notes; Reject(() => repo.SaveNotes(entry, new string('字', LibraryRepository.MaxNotesBytes))); Assert(repo.ReadEntry(entry).Notes == before);
            using (var locked = new FileStream(Path.Combine(entry, "说明.txt"), FileMode.Open, FileAccess.Read, FileShare.None)) Reject(() => repo.SaveNotes(entry, "不能覆盖锁定文件"));
            Assert(repo.ReadEntry(entry).Notes == before);
        });
        void MakeZip(string path, params (string Name, byte[] Bytes)[] files)
        {
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create); foreach (var (name, bytes) in files) { using var output = archive.CreateEntry(name).Open(); output.Write(bytes); }
        }
        Test("Malicious ZIP paths and malformed structures leave library unchanged", () =>
        {
            int count = Directory.GetDirectories(repo.LibraryRoot).Length;
            var zip = Path.Combine(root, "越界.zip"); MakeZip(zip, ("合集/../../escape.txt", Encoding.UTF8.GetBytes("bad"))); Reject(() => service.Import(zip));
            var zip2 = Path.Combine(root, "错误阵营.zip"); MakeZip(zip2, ("合集/Dust2/XYZ/点位/说明.txt", [])); Reject(() => service.Import(zip2));
            var zip3 = Path.Combine(root, "重复路径.zip"); MakeZip(zip3, ("合集/Dust2/CT/点位/说明.txt", []), ("合集/Dust2/CT/点位/说明.txt", [])); Reject(() => service.Import(zip3));
            Assert(Directory.GetDirectories(repo.LibraryRoot).Length == count); Assert(!File.Exists(Path.Combine(repo.UserRoot, "escape.txt")));
        });
        Test("Same-name different package is imported as a new collection", () =>
        {
            var zip = Path.Combine(root, "变化.zip"); MakeZip(zip, ("示例道具合集/Mirage/T/新道具/说明.txt", Encoding.UTF8.GetBytes("草稿"))); var result = service.Import(zip); Assert(Path.GetFileName(result.CollectionPath) == "示例道具合集 (2)"); Assert(!result.AlreadyImported);
        });
        Test("Encrypted archives and checksum corruption are rejected before commit", () =>
        {
            var zip = Path.Combine(root, "加密标记.zip"); MakeZip(zip, ("加密包/Dust2/CT/测试/说明.txt", Encoding.UTF8.GetBytes("hello")));
            var bytes = File.ReadAllBytes(zip); bytes[6] |= 1;
            for (int i = 0; i < bytes.Length - 10; i++) if (BitConverter.ToUInt32(bytes, i) == 0x02014b50) bytes[i + 8] |= 1;
            File.WriteAllBytes(zip, bytes); Reject(() => service.Import(zip)); Assert(!Directory.Exists(Path.Combine(repo.LibraryRoot, "加密包")));
            var crcZip = Path.Combine(root, "校验错误.zip"); MakeZip(crcZip, ("校验包/Dust2/CT/测试/说明.txt", Encoding.UTF8.GetBytes("hello")));
            bytes = File.ReadAllBytes(crcZip); for (int i = 0; i < bytes.Length - 20; i++) if (BitConverter.ToUInt32(bytes, i) == 0x02014b50) bytes[i + 16] ^= 0xFF;
            File.WriteAllBytes(crcZip, bytes); Reject(() => service.Import(crcZip)); Assert(!Directory.Exists(Path.Combine(repo.LibraryRoot, "校验包")));
        });
        Test("Oversized expansion and archive links are rejected", () =>
        {
            var zip = Path.Combine(root, "超限.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) { using var stream = archive.CreateEntry("大包/Dust2/CT/测试/附件.bin").Open(); var zeros = new byte[1024 * 1024]; for (int i = 0; i < 65; i++) stream.Write(zeros); }
            Reject(() => service.Import(zip)); Assert(!Directory.Exists(Path.Combine(repo.LibraryRoot, "大包")));
            var links = Path.Combine(root, "链接.zip"); using (var archive = ZipFile.Open(links, ZipArchiveMode.Create)) { var link = archive.CreateEntry("链接包/跳转"); link.ExternalAttributes = unchecked((int)0xA1FF0000); using var output = link.Open(); output.Write(Encoding.UTF8.GetBytes("../../outside")); }
            Reject(() => service.Import(links));
        });
        Test("Image decoding rejects corrupt PNG body", () =>
        {
            var broken = Path.Combine(root, "broken.png"); File.WriteAllBytes(broken, [137,80,78,71,13,10,26,10]);
            bool rejected = false; try { Images.Load(broken); } catch { rejected = true; } Assert(rejected);
            var zip = Path.Combine(root, "损坏图片包.zip"); MakeZip(zip, ("损坏合集/Dust2/CT/测试/站位.png", File.ReadAllBytes(broken))); Reject(() => service.Import(zip)); Assert(!Directory.Exists(Path.Combine(repo.LibraryRoot, "损坏合集")));
            var badEntry = repo.CreateFolder(Path.Combine(collection, "Dust2", "T"), "损坏草稿", true); File.Copy(broken, Path.Combine(badEntry, "站位.png")); Assert(!repo.ReadEntry(badEntry).Ready); Assert(repo.Scan().Entries.Any(e => e.Path == badEntry)); repo.Recycle(badEntry);
        });
        QuickBrowseTests.Run(Test, Assert);
        InputSecurityTests.Run(Test, Assert);
        StorageSecurityTests.Run(root, Test, Assert);
        StartupSecurityTests.Run(root, Test, Assert);
        Test("Settings survive restart and legacy keyboard bindings migrate", () =>
        {
            var settings = new UserSettings { Width = 640, Vertical = true, SelectedPath = "示例", SelectionHotkey = "Shift+F8", VisibilityHotkey = "Ctrl+OemComma" }; repo.SaveSettings(settings);
            var loaded = repo.LoadSettings(); Assert(loaded.Width == 640 && loaded.Vertical); Assert(InputBindings.Migrate(loaded, BindingNames.ParseLegacy) == null); Assert(loaded.InputBindings![InputCommand.ToggleMenu].Code == 119 && loaded.InputBindings[InputCommand.ToggleMenu].Modifiers == InputModifiers.Shift); Assert(loaded.SelectedPath == "示例" && loaded.Width == 640); repo.SaveSettings(loaded); Assert(repo.LoadSettings().SettingsVersion == 2);
        });
        Test("Own WPF windows render and native styles switch without a game", () =>
        {
            repo.SaveSettings(new UserSettings { SelectedPath = Path.GetRelativePath(repo.LibraryRoot, entry) });
            using var controller = new AppController(repo, app); var editor = new EditorWindow(controller); editor.Show(); editor.Reload(entry); Pump();
            editor.NotesBox.Text += "\n自动保存测试。"; Assert(editor.FlushNotes()); Assert(repo.ReadEntry(entry).Notes.Contains("自动保存测试"));
            Render(editor, Path.Combine(root, "01-资料编辑器.png")); editor.Hide();
            var viewer = controller.Viewer; viewer.Present(repo.ReadEntry(entry)); viewer.SetMode(PresentationMode.Adjusting); viewer.Show(); Pump();
            Assert((NativeWindow.ReadOwnStyle(viewer) & NativeWindow.Transparent) == 0); Render(viewer, Path.Combine(root, "02-鼠标调整.png"));
            viewer.SetMode(PresentationMode.Viewing); Pump(); var style = NativeWindow.ReadOwnStyle(viewer); Assert((style & NativeWindow.Transparent) != 0 && (style & NativeWindow.NoActivate) != 0 && (style & NativeWindow.Layered) != 0); Render(viewer, Path.Combine(root, "03-查看模式.png"));
            viewer.ToggleVisibility(); Assert(!viewer.IsVisible); viewer.ToggleVisibility(); Assert(viewer.IsVisible && !viewer.Selecting); viewer.SetTopmost(false); Assert(!viewer.Topmost);
            viewer.SetMode(PresentationMode.Adjusting); Assert((NativeWindow.ReadOwnStyle(viewer) & NativeWindow.NoActivate) == 0); viewer.Hide(); editor.Hide();
            viewer.SetMode(PresentationMode.Viewing);
            foreach (var scale in new[] { 1d, 1.25, 1.5 })
            {
                controller.Settings.Vertical = false;
                // Exercise available DIP sizes corresponding to 1080p/1440p and common scale factors.
                editor.Width = Math.Min(1180, 1920 / scale - 40); editor.Height = Math.Min(800, 1080 / scale - 60); editor.Show(); Pump();
                Assert(editor.ActualWidth >= editor.MinWidth && editor.ActualHeight >= editor.MinHeight); Render(editor, Path.Combine(root, $"04-编辑器-{scale:0.00}.png")); editor.Hide();
            }
            editor.Hide();
            controller.OpenViewer(); Assert(controller.InputActive); var size = (viewer.Width, viewer.Height);
            using var foreground = new WindowLifetime(new Window { Title = "本项目焦点验证", Width = 420, Height = 180, Content = Theme.Text("只验证本项目窗口的活动状态。") });
            foreground.Window.Show(); foreground.Window.Activate(); Pump(); Assert(foreground.Window.IsActive);
            viewer.HandleCommand(InputCommand.ToggleMenu); Pump(); Assert(viewer.Mode == PresentationMode.Browsing && (viewer.Width, viewer.Height) == size);
            Assert(foreground.Window.IsActive);
            Assert(!viewer.ShowActivated && (NativeWindow.ReadOwnStyle(viewer) & NativeWindow.NoActivate) != 0 && (NativeWindow.ReadOwnStyle(viewer) & NativeWindow.Transparent) != 0);
            Render(viewer, Path.Combine(root, "05-后台快捷浏览.png"));
            var beforePath = viewer.CurrentPath; viewer.HandleCommand(InputCommand.Previous); viewer.HandleCommand(InputCommand.ToggleMenu); Assert(viewer.CurrentPath == beforePath && viewer.Mode == PresentationMode.Viewing);
            viewer.HandleCommand(InputCommand.ToggleMenu); viewer.HandleCommand(InputCommand.ToggleVisibility); Assert(!viewer.IsVisible && viewer.Mode == PresentationMode.Viewing && controller.InputActive);
            viewer.HandleCommand(InputCommand.ToggleVisibility); Assert(viewer.IsVisible && viewer.Mode == PresentationMode.Viewing);
            Pump(); Assert(foreground.Window.IsActive);
            controller.OpenEditor(); Assert(!controller.InputActive);
            var original = new Dictionary<InputCommand, InputBinding>(controller.Settings.InputBindings!); var candidate = new Dictionary<InputCommand, InputBinding>(original) { [InputCommand.Next] = new(InputDevice.Keyboard, 78) };
            repo.SaveSettings(controller.Settings); var settingPath = Path.Combine(repo.UserRoot, "设置.json"); var originalBytes = File.ReadAllBytes(settingPath);
            using (var locked = new FileStream(settingPath, FileMode.Open, FileAccess.Read, FileShare.None)) Reject(() => controller.CommitBindings(candidate));
            Assert(controller.Settings.InputBindings!.SequenceEqual(original)); Assert(File.ReadAllBytes(settingPath).SequenceEqual(originalBytes));
            controller.CommitBindings(candidate); Assert(repo.LoadSettings().InputBindings![InputCommand.Next].Code == 78);
            using (var bindingsView = new WindowLifetime(new InputBindingDialog(editor, controller.Settings.InputBindings!, controller.CommitBindings)))
            { bindingsView.Window.Show(); Pump(); Render(bindingsView.Window, Path.Combine(root, "06-快捷键设置.png")); }
        });
        log.Add($"RESULT: {log.Count - failures} passed, {failures} failed"); File.WriteAllLines(Path.Combine(root, "验证结果.txt"), log); Console.WriteLine(root); Console.WriteLine(string.Join("\n", log)); app.Shutdown(); return failures == 0 ? 0 : 1;
    }
    private sealed class WindowLifetime(Window window) : IDisposable { public Window Window { get; } = window; public void Dispose() => Window.Close(); }
    private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
    private static void Render(Window window, string path)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, content.ActualWidth, content.ActualHeight);
            dc.DrawRectangle(Theme.Background, null, bounds);
            var offset = VisualTreeHelper.GetOffset(content);
            dc.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(offset.X, offset.Y, content.ActualWidth, content.ActualHeight) }, null, bounds);
        }
        var image = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var output = File.Create(path); encoder.Save(output);
    }
    private static void MakeImage(string path, string title, string subtitle, bool jpeg)
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 43, 59)), null, new Rect(0, 0, 960, 540));
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(55, 80, 100)), 1);
            for (int x = 0; x < 960; x += 60) dc.DrawLine(pen, new Point(x, 0), new Point(x, 540));
            for (int y = 0; y < 540; y += 60) dc.DrawLine(pen, new Point(0, y), new Point(960, y));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(45, 68, 86)), null, new Rect(280, 140, 400, 220), 12, 12);
            dc.DrawEllipse(null, new Pen(Theme.Accent, 4), new Point(480, 250), 30, 30);
            dc.DrawLine(new Pen(Theme.Accent, 3), new Point(430,250), new Point(530,250)); dc.DrawLine(new Pen(Theme.Accent, 3), new Point(480,200), new Point(480,300));
            var face = new Typeface("Microsoft YaHei UI");
            dc.DrawText(new FormattedText(title, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 30, Theme.Ink, 1), new Point(42, 38));
            dc.DrawText(new FormattedText(subtitle, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 24, Theme.Accent, 1), new Point(42, 440));
            dc.DrawText(new FormattedText("仅用于软件展示与验证", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 18, Theme.Muted, 1), new Point(42, 485));
        }
        var bitmap = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder() : new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}


