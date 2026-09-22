using Handbook.Storage;
using System.IO.Compression;
using System.Text;
using Handbook.Core;

namespace Handbook.Desktop;

internal static class StorageSecurityTests
{
    public static void Run(string root, Action<string, Action> test, Action<bool, string> assert)
    {
        void Check(bool condition) => assert(condition, "Storage security assertion failed");
        void Reject(Action action)
        {
            try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException) { return; }
            throw new Exception("Unsafe operation was accepted");
        }
        var testRoot = Path.Combine(root, "storage-security");
        Directory.CreateDirectory(testRoot);
        test("Repository probe preserves existing names; local paths reject streams and devices", () =>
        {
            var data = Path.Combine(testRoot, "probe"); Directory.CreateDirectory(Path.Combine(data, "用户数据"));
            var existing = Path.Combine(data, "用户数据", ".write-test"); File.WriteAllText(existing, "preserve");
            _ = new LibraryRepository(data); Check(File.ReadAllText(existing) == "preserve");
            Reject(() => LibraryRepository.ValidateLocalPath(@"\\server\share\data"));
            Reject(() => LibraryRepository.ValidateLocalPath(@"\\?\C:\data"));
            Reject(() => LibraryRepository.ValidateLocalPath(@"C:\data\file:secret"));
            Reject(() => LibraryRepository.ValidateName("COM¹"));
        });
        test("Metadata reads are bounded and corrupt recycle records are isolated", () =>
        {
            var repo = new LibraryRepository(Path.Combine(testRoot, "metadata"));
            var collection = repo.CreateCollection("Valid"); repo.Recycle(collection);
            var valid = repo.GetRecycled().Single();
            var badId = Guid.NewGuid().ToString("N"); var bad = Path.Combine(repo.UserRoot, "回收区", badId);
            Directory.CreateDirectory(Path.Combine(bad, "内容")); File.WriteAllText(Path.Combine(bad, "记录.json"), "broken");
            Check(repo.GetRecycled().Count == 1); Check(Directory.Exists(repo.Restore(valid.Id)));
            var oversized = Path.Combine(repo.UserRoot, "oversized.json"); File.WriteAllText(oversized, new string('x', LibraryRepository.MaxNotesBytes + 1));
            Reject(() => LibraryRepository.ReadMetadata(oversized));
            var settings = Path.Combine(repo.UserRoot, "设置.json"); File.WriteAllText(settings, "{broken");
            _ = repo.LoadSettings(); Check(repo.SettingsWarnings.Count > 0);
            Check(Directory.GetFiles(repo.UserRoot, "*.invalid.json").Any(file => File.ReadAllText(file) == "{broken"));
            var wrongEncoding = Path.Combine(repo.UserRoot, "utf16.json"); File.WriteAllText(wrongEncoding, "{}", Encoding.Unicode);
            Reject(() => LibraryRepository.ReadMetadata(wrongEncoding));
        });
        test("ZIP rejects executable attachments and invalid UTF8 notes without committing", () =>
        {
            var repo = new LibraryRepository(Path.Combine(testRoot, "zip")); var service = new PackageService(repo);
            void BadZip(string name, string leaf, byte[] content)
            {
                var zipPath = Path.Combine(testRoot, name + ".zip");
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                using (var stream = zip.CreateEntry("Collection/Dust2/CT/Entry/" + leaf).Open()) stream.Write(content);
                Reject(() => service.Import(zipPath)); Check(!Directory.EnumerateDirectories(repo.LibraryRoot).Any());
            }
            BadZip("attachment", "run.exe", Encoding.UTF8.GetBytes("not executable but disallowed"));
            BadZip("encoding", "说明.txt", [0xff, 0xfe, 0xff]);
        });
        test("Moving deep subtrees rejects destinations that scan cannot represent", () =>
        {
            var repo = new LibraryRepository(Path.Combine(testRoot, "depth")); var collection = repo.CreateCollection("Depth"); var map = repo.CreateMap(collection, "Dust2");
            var parent = Path.Combine(map, "CT");
            for (var i = 0; i < 17; i++) parent = repo.CreateFolder(parent, "Level" + i, false);
            var source = repo.CreateFolder(Path.Combine(map, "T"), "Source", false); repo.CreateFolder(source, "Child", false);
            Reject(() => repo.Transfer(source, parent, true)); Check(!Directory.Exists(Path.Combine(parent, "Source")));
        });
        test("Image extension replacement rolls back when an original is locked", () =>
        {
            var repo = new LibraryRepository(Path.Combine(testRoot, "image-transaction")); var collection = repo.CreateCollection("Images"); var map = repo.CreateMap(collection, "Dust2");
            var entry = repo.CreateFolder(Path.Combine(map, "CT"), "Entry", true);
            var oldPath = Path.Combine(entry, "站位.jpg"); byte[] original = [255, 216, 255, 0, 0, 0, 0, 0]; File.WriteAllBytes(oldPath, original);
            var source = Path.Combine(testRoot, "replacement.png"); byte[] replacement = [137, 80, 78, 71, 13, 10, 26, 10]; File.WriteAllBytes(source, replacement);
            using (var locked = new FileStream(oldPath, FileMode.Open, FileAccess.Read, FileShare.None)) Reject(() => repo.SetImage(entry, "站位", source));
            Check(File.ReadAllBytes(oldPath).SequenceEqual(original)); Check(!File.Exists(Path.Combine(entry, "站位.png")));
            repo.SetImage(entry, "站位", source); Check(!File.Exists(oldPath)); Check(File.ReadAllBytes(Path.Combine(entry, "站位.png")).SequenceEqual(replacement));
            File.WriteAllText(Path.Combine(entry, "说明.txt"), "wrong encoding", Encoding.Unicode);
            Check(repo.ReadEntry(entry).Notes == "" && !repo.ReadEntry(entry).Ready);
        });
        test("Scan stops with a visible error at node and cumulative note budgets", () =>
        {
            var repo = new LibraryRepository(Path.Combine(testRoot, "node-budget"));
            for (var i = 0; i <= LibraryRepository.MaxScanNodes; i++) Directory.CreateDirectory(Path.Combine(repo.LibraryRoot, "Collection" + i));
            var snapshot = repo.Scan(); Check(snapshot.Roots.Count == LibraryRepository.MaxScanNodes); Check(snapshot.Errors.Any(e => e.Contains("10000")));
            repo = new LibraryRepository(Path.Combine(testRoot, "notes-budget")); var collection = repo.CreateCollection("Notes"); var map = repo.CreateMap(collection, "Dust2");
            var text = new string('a', LibraryRepository.MaxNotesBytes);
            for (var i = 0; i < 65; i++)
            {
                var entry = repo.CreateFolder(Path.Combine(map, "CT"), "Entry" + i, false); repo.SaveNotes(entry, text);
            }
            snapshot = repo.Scan(); Check(snapshot.Entries.Count == 64); Check(snapshot.Errors.Any(e => e.Contains("64 MB")));
        });
    }
}

