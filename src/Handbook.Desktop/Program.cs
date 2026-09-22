using Handbook.Storage;
using System.Windows;
using Handbook.Core;

namespace Handbook.Desktop;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return SelfTests.Run();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Theme.Install(app);
        bool inputTest = args.Contains("--input-test");
        // Acquire the application lock before any repository initialization/writes.
        bool first = true;
        using var mutex = inputTest ? null : new Mutex(true, "Local\\IndependentHandbook-Desktop-v1", out first);
        if (!first)
        { MessageBox.Show("道具手册已运行，请从系统托盘打开。", "道具手册"); return 0; }
        LibraryRepository? repo = null;
        var locationFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "独立道具手册", "资料位置.txt");
        var dataRoot = DataRootLocator.Read(locationFile, out var locationWarning) ?? AppContext.BaseDirectory;
        if (locationWarning != null) MessageBox.Show(locationWarning, "资料位置");
        bool rememberLocation = false;
        while (repo == null)
        {
            try { repo = new LibraryRepository(DataRootLocator.ValidateRoot(dataRoot), Images.Validate); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                MessageBox.Show("默认资料目录不可写，请选择一个可写目录。\n" + e.Message, "选择资料目录");
                var picker = new Microsoft.Win32.OpenFolderDialog { Title = "选择资料根目录" };
                if (picker.ShowDialog() != true) return 1;
                dataRoot = picker.FolderName; rememberLocation = true;
            }
        }
        if (rememberLocation && DataRootLocator.Save(locationFile, dataRoot) is { } saveWarning)
            MessageBox.Show(saveWarning, "资料位置");
        if (inputTest)
        {
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.Run(new InputTestWindow(repo.LoadSettings())); return 0;
        }
        using var controller = new AppController(repo, app);
        app.DispatcherUnhandledException += (_, e) => { controller.ReportError(e.Exception.Message); e.Handled = true; };
        controller.OpenEditor();
        app.Run(); return 0;
    }
}

