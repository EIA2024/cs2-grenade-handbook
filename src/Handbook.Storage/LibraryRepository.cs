using System.Text;
using System.Text.Json;

using Handbook.Core;

namespace Handbook.Storage;

public sealed class LibraryRepository : ILibraryRepository
{
    public string DataRoot { get; }
    public string LibraryRoot { get; }
    public string PackagesRoot { get; }
    public string UserRoot { get; }
    public static readonly string[] Maps = ["Ancient", "Anubis", "Dust2", "Inferno", "Mirage", "Nuke", "Overpass", "Train", "Vertigo"];
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];
    public const long MaxImageBytes = 32 * 1024 * 1024;
    public const int MaxNotesBytes = 1024 * 1024;
    public const int MaxScanNodes = 10000;
    public const long MaxScanNotesBytes = 64L * 1024 * 1024;
    private sealed class ScanBudget { public int Nodes; public long NotesBytes; public bool Stopped; }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Action<string>? imageDecoder;
    public List<string> SettingsWarnings { get; } = [];
    public LibraryRepository(string root, Action<string>? imageDecoder = null)
    {
        this.imageDecoder = imageDecoder;
        DataRoot = System.IO.Path.GetFullPath(root);
        ValidateLocalPath(DataRoot);
        RejectLinks(DataRoot);
        LibraryRoot = System.IO.Path.Combine(DataRoot, "资料库");
        PackagesRoot = System.IO.Path.Combine(DataRoot, "图片包");
        UserRoot = System.IO.Path.Combine(DataRoot, "用户数据");
        foreach (var dir in new[] { LibraryRoot, PackagesRoot, UserRoot })
        {
            RejectLinks(dir);
            Directory.CreateDirectory(dir);
        }
        var probe = System.IO.Path.Combine(UserRoot, ".write-test-" + Guid.NewGuid().ToString("N"));
        using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write)) stream.WriteByte(1);
        File.Delete(probe);
    }
    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name != name.Trim() || name.EndsWith('.') ||
            name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || name.Contains(':') || name is "." or "..")
            throw new InvalidDataException("名称无效：不能包含路径分隔符、末尾空格或句点，且不能超过 100 字符。");
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³" }.Contains(stem))
            throw new InvalidDataException("该名称是 Windows 保留名称。");
    }
    public static void RejectLinks(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        ValidateLocalPath(fullPath);
        FileSystemInfo? current = Directory.Exists(fullPath) ? new DirectoryInfo(fullPath) : new FileInfo(fullPath);
        while (current != null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("资料路径不允许使用符号链接或目录联接。");
            current = current is DirectoryInfo d ? d.Parent : ((FileInfo)current).Directory;
        }
    }
    public static void ValidateLocalPath(string path)
    {
        if (!System.IO.Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0)
            throw new InvalidDataException("资料只支持本地普通绝对路径，不允许网络、设备路径或备用数据流。");
    }
    public string EnsurePath(string path, bool allowRoot = false)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (!(allowRoot && full.Equals(LibraryRoot, StringComparison.OrdinalIgnoreCase)) &&
            !full.StartsWith(LibraryRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("操作必须位于本软件资料库内。");
        RejectLinks(full); return full;
    }
    public static void AtomicWrite(string path, string text)
    {
        RejectLinks(path);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, text, new UTF8Encoding(false)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public UserSettings LoadSettings()
    {
        SettingsWarnings.Clear(); string? original = null;
        void Backup()
        {
            if (original == null) return;
            var backup = System.IO.Path.Combine(UserRoot, "设置-" + Guid.NewGuid().ToString("N") + ".invalid.json");
            try { AtomicWrite(backup, original); SettingsWarnings.Add("原设置已备份为 " + System.IO.Path.GetFileName(backup)); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { SettingsWarnings.Add("设置备份失败：" + e.Message); }
        }
        try
        {
            original = ReadMetadata(System.IO.Path.Combine(UserRoot, "设置.json"));
            var settings = JsonSerializer.Deserialize<UserSettings>(original) ?? throw new JsonException("设置对象不能为空。");
            var warnings = SettingsValidation.Normalize(settings);
            if (warnings.Count > 0) { SettingsWarnings.AddRange(warnings); Backup(); }
            return settings;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return new(); }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or DecoderFallbackException)
        { SettingsWarnings.Add("设置读取失败，已使用默认值：" + e.Message); Backup(); return new(); }
    }
    public static string ReadMetadata(string path)
    {
        RejectLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxNotesBytes) throw new InvalidDataException("元数据超过 1 MB。");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false);
        var text = reader.ReadToEnd();
        return text.StartsWith('\uFEFF') ? text[1..] : text;
    }
    public void SaveSettings(UserSettings settings) => AtomicWrite(System.IO.Path.Combine(UserRoot, "设置.json"), JsonSerializer.Serialize(settings, JsonOptions));
    public LibrarySnapshot Scan()
    {
        RejectLinks(LibraryRoot);
        var entries = new List<Entry>(); var errors = new List<string>(); var roots = new List<LibraryNode>(); var budget = new ScanBudget();
        foreach (var dir in ScanDirectories(LibraryRoot, budget))
        {
            if (budget.Stopped) break;
            try { var node = Walk(dir, 0, entries, errors, budget); if (node != null) roots.Add(node); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { errors.Add($"{System.IO.Path.GetFileName(dir)}：{e.Message}"); }
        }
        return new(roots, entries, errors);
    }
    private static IEnumerable<string> ScanDirectories(string path, ScanBudget budget) => Directory.EnumerateDirectories(path).Take(Math.Max(1, MaxScanNodes - budget.Nodes + 1)).Order(StringComparer.OrdinalIgnoreCase);
    private LibraryNode? Walk(string path, int depth, List<Entry> entries, List<string> errors, ScanBudget budget)
    {
        if (++budget.Nodes > MaxScanNodes) { budget.Stopped = true; errors.Add("扫描已停止：目录节点超过 10000 个。"); return null; }
        EnsurePath(path);
        if (depth > 20) throw new InvalidDataException("目录层级超过 20 层。");
        var name = System.IO.Path.GetFileName(path);
        if (depth == 2 && name != "CT" && name != "T") throw new InvalidDataException($"阵营目录必须为 CT 或 T：{name}");
        var isEntry = depth >= 3 && Directory.EnumerateFiles(path).Any(f =>
            System.IO.Path.GetFileName(f).Equals("说明.txt", StringComparison.OrdinalIgnoreCase) ||
            new[] { "站位", "瞄准" }.Contains(System.IO.Path.GetFileNameWithoutExtension(f)));
        var kind = depth switch { 0 => NodeKind.Collection, 1 => NodeKind.Map, 2 => NodeKind.Side, _ => isEntry ? NodeKind.Entry : NodeKind.Folder };
        if (isEntry)
        {
            var notesPath = System.IO.Path.Combine(path, "说明.txt");
            if (File.Exists(notesPath))
            {
                RejectLinks(notesPath);
                var bytes = Math.Min(new FileInfo(notesPath).Length, MaxNotesBytes);
                if (budget.NotesBytes + bytes > MaxScanNotesBytes)
                {
                    budget.Stopped = true; errors.Add("扫描已停止：累计说明超过 64 MB。");
                    return new(path, name, kind, []);
                }
                budget.NotesBytes += bytes;
            }
            entries.Add(ReadEntry(path));
        }
        var children = new List<LibraryNode>();
        foreach (var child in ScanDirectories(path, budget))
        {
            if (budget.Stopped) break;
            try { var node = Walk(child, depth + 1, entries, errors, budget); if (node != null) children.Add(node); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { errors.Add($"{System.IO.Path.GetRelativePath(LibraryRoot, child)}：{e.Message}"); }
        }
        return new(path, name, kind, children);
    }
    public Entry ReadEntry(string path)
    {
        EnsurePath(path); var parts = System.IO.Path.GetRelativePath(LibraryRoot, path).Split(System.IO.Path.DirectorySeparatorChar);
        if (parts.Length < 4) throw new InvalidDataException("道具必须位于地图和 CT/T 目录下。");
        var errors = new List<string>();
        string? GetImage(string role)
        {
            var matches = Directory.GetFiles(path).Where(f => System.IO.Path.GetFileNameWithoutExtension(f) == role && ImageExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).ToArray();
            if (matches.Length == 0) { errors.Add($"缺少{role}图"); return null; }
            if (matches.Length > 1) { errors.Add($"{role}图重复，请只保留一张"); return null; }
            try { CheckImage(matches[0]); return matches[0]; }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { errors.Add(e.Message); return null; }
        }
        var standing = GetImage("站位"); var aiming = GetImage("瞄准"); var notes = "";
        var textPath = System.IO.Path.Combine(path, "说明.txt");
        try
        {
            if (File.Exists(textPath))
            {
                notes = ReadMetadata(textPath);
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or DecoderFallbackException) { errors.Add($"说明读取失败：{e.Message}"); }
        return new(path, parts[0], parts[1], parts[2], string.Join(" / ", parts.Skip(3)), standing, aiming, notes, errors);
    }
    public static void ValidateImage(string path)
    {
        RejectLinks(path);
        if (!ImageExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant())) throw new InvalidDataException("仅支持 PNG、JPG、JPEG。");
        var size = new FileInfo(path).Length;
        if (size < 8 || size > MaxImageBytes) throw new InvalidDataException("图片为空、损坏或超过 32 MB。");
        using var stream = File.OpenRead(path); Span<byte> header = stackalloc byte[8]; stream.ReadExactly(header);
        bool png = header.SequenceEqual(new byte[] {137,80,78,71,13,10,26,10});
        bool jpeg = header[0] == 255 && header[1] == 216 && header[2] == 255;
        if (!png && !jpeg) throw new InvalidDataException("图片内容不是有效 PNG/JPEG。");
        if (System.IO.Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) != png)
            throw new InvalidDataException("图片扩展名与内容不符。");
    }
    public void CheckImage(string path)
    {
        ValidateImage(path);
        if (imageDecoder != null)
        {
            try { imageDecoder(path); }
            catch (Exception e) when (e is not OutOfMemoryException) { throw new InvalidDataException("图片解码失败：" + e.Message, e); }
        }
    }
    public string CreateCollection(string name) => CreateDirectory(LibraryRoot, name);
    public string CreateMap(string collection, string name)
    {
        if (Parts(collection).Length != 1) throw new InvalidDataException("请先选择合集。");
        if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or ' ')) throw new InvalidDataException("地图名请使用英文、数字、空格、短横线或下划线。");
        var path = CreateDirectory(collection, name); Directory.CreateDirectory(System.IO.Path.Combine(path, "CT")); Directory.CreateDirectory(System.IO.Path.Combine(path, "T")); return path;
    }
    public string CreateFolder(string parent, string name, bool entry)
    {
        var parts = Parts(parent);
        if (parts.Length < 3 || parts[2] is not ("CT" or "T")) throw new InvalidDataException("请在 CT/T 或其下方目录中新建。");
        if (parts.Length >= 21) throw new InvalidDataException("目录最多 20 层。");
        var path = CreateDirectory(parent, name); if (entry) SaveNotes(path, ""); return path;
    }
    private string CreateDirectory(string parent, string name)
    {
        EnsurePath(parent, true); ValidateName(name); var dest = EnsurePath(System.IO.Path.Combine(parent, name));
        if (Directory.Exists(dest) || File.Exists(dest)) throw new IOException("已存在同名文件或目录。");
        Directory.CreateDirectory(dest); return dest;
    }
    private string[] Parts(string path) => System.IO.Path.GetRelativePath(LibraryRoot, EnsurePath(path)).Split(System.IO.Path.DirectorySeparatorChar);
    public void SaveNotes(string entry, string text)
    {
        if (Parts(entry).Length < 4) throw new InvalidDataException("仅能编辑道具说明。");
        if (Encoding.UTF8.GetByteCount(text) > MaxNotesBytes) throw new InvalidDataException("说明不能超过 1 MB。");
        AtomicWrite(System.IO.Path.Combine(entry, "说明.txt"), text);
    }
    public void SetImage(string entry, string role, string source)
    {
        if (Parts(entry).Length < 4 || role is not ("站位" or "瞄准")) throw new InvalidDataException("无效的图片目标。");
        CheckImage(source);
        var dest = EnsurePath(System.IO.Path.Combine(entry, role + System.IO.Path.GetExtension(source).ToLowerInvariant()));
        if (System.IO.Path.GetFullPath(source).Equals(dest, StringComparison.OrdinalIgnoreCase)) return;
        var stage = System.IO.Path.Combine(UserRoot, "图片事务-" + Guid.NewGuid().ToString("N"));
        RejectLinks(stage); Directory.CreateDirectory(stage);
        var temp = System.IO.Path.Combine(stage, "新图片" + System.IO.Path.GetExtension(dest));
        var backups = new List<(string Original, string Backup)>();
        var committed = false; var preserveBackups = false;
        try
        {
            File.Copy(source, temp); CheckImage(temp);
            var originals = Directory.EnumerateFiles(entry).Where(f => System.IO.Path.GetFileNameWithoutExtension(f) == role && ImageExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).ToArray();
            foreach (var original in originals)
            {
                EnsurePath(original);
                var backup = System.IO.Path.Combine(stage, System.IO.Path.GetFileName(original));
                File.Move(original, backup); backups.Add((original, backup));
            }
            File.Move(temp, dest); committed = true;
        }
        catch (Exception failure)
        {
            var restoreErrors = new List<Exception>();
            if (committed) { try { File.Delete(dest); } catch (Exception e) { restoreErrors.Add(e); } }
            foreach (var (original, backup) in backups.AsEnumerable().Reverse())
            {
                try { EnsurePath(original); File.Move(backup, original); }
                catch (Exception e) { restoreErrors.Add(e); }
            }
            if (restoreErrors.Count > 0)
            {
                preserveBackups = true;
                throw new IOException("图片替换失败，部分原图无法自动恢复，备份保留于：" + stage, new AggregateException(new[] { failure }.Concat(restoreErrors)));
            }
            throw;
        }
        finally
        {
            // Never delete the only surviving originals when rollback was incomplete.
            if (!preserveBackups)
            {
                try { Directory.Delete(stage, true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Safe leftover backups can be recovered manually. */ }
            }
        }
    }

    public string Rename(string path, string name)
    {
        var parts = Parts(path); if (parts.Length == 3) throw new InvalidDataException("CT/T 名称固定，不能重命名。");
        ValidateName(name);
        if (parts.Length == 2 && !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or ' ')) throw new InvalidDataException("地图名请使用英文。");
        var dest = EnsurePath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, name));
        if (path.Equals(dest, StringComparison.OrdinalIgnoreCase)) return path;
        if (Directory.Exists(dest) || File.Exists(dest)) throw new IOException("目标名称已存在。");
        Directory.Move(path, dest); return dest;
    }
    public string Transfer(string path, string parent, bool copy)
    {
        if (Parts(path).Length < 4 || Parts(parent).Length < 3) throw new InvalidDataException("移动/复制仅支持 CT/T 下的分类和道具。");
        var dest = EnsurePath(System.IO.Path.Combine(parent, System.IO.Path.GetFileName(path)));
        if (dest.Equals(path, StringComparison.OrdinalIgnoreCase) || dest.StartsWith(path + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("不能放入自身或子目录。");
        if (Directory.Exists(dest) || File.Exists(dest)) throw new IOException("目标已有同名目录。");
        foreach (var f in EnumerateSafe(path)) RejectLinks(f);
        var deepest = MaximumDirectoryDepth(path);
        if (Parts(parent).Length + 1 + deepest > 21) throw new InvalidDataException("移动或复制后的目录层级超过 20 层。");
        if (copy)
        {
            try
            {
                CopyTree(path, dest);
            }
            catch
            {
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                throw;
            }
        }
        else Directory.Move(path, dest);
        return dest;
    }
    public static IEnumerable<string> EnumerateSafe(string dir, int depth = 0)
    {
        if (depth > 20) throw new InvalidDataException("目录层级超过 20 层。");
        RejectLinks(dir);
        foreach (var file in Directory.EnumerateFiles(dir)) { RejectLinks(file); yield return file; }
        foreach (var child in Directory.EnumerateDirectories(dir)) { RejectLinks(child); foreach (var file in EnumerateSafe(child, depth + 1)) yield return file; }
    }
    private static int MaximumDirectoryDepth(string dir, int depth = 0)
    {
        if (depth > 20) throw new InvalidDataException("目录层级超过 20 层。");
        RejectLinks(dir);
        return Directory.EnumerateDirectories(dir).Select(child => MaximumDirectoryDepth(child, depth + 1)).DefaultIfEmpty(depth).Max();
    }
    private static void CopyTree(string source, string dest, int depth = 0)
    {
        if (depth > 20) throw new InvalidDataException("目录层级超过 20 层。");
        RejectLinks(source); Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source)) { RejectLinks(file); File.Copy(file, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(file))); }
        foreach (var dir in Directory.GetDirectories(source)) CopyTree(dir, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(dir)), depth + 1);
    }
    public void Recycle(string path)
    {
        var parts = Parts(path); if (parts.Length == 3) throw new InvalidDataException("请删除阵营下的内容，保留 CT/T。");
        foreach (var f in EnumerateSafe(path)) RejectLinks(f);
        var id = Guid.NewGuid().ToString("N"); var folder = System.IO.Path.Combine(UserRoot, "回收区", id); RejectLinks(folder); Directory.CreateDirectory(folder);
        AtomicWrite(System.IO.Path.Combine(folder, "记录.json"), JsonSerializer.Serialize(new RecycledItem(id, System.IO.Path.GetRelativePath(LibraryRoot, path), DateTime.Now)));
        Directory.Move(path, System.IO.Path.Combine(folder, "内容"));
    }
    public IReadOnlyList<RecycledItem> GetRecycled()
    {
        var root = System.IO.Path.Combine(UserRoot, "回收区"); if (!Directory.Exists(root)) return [];
        RejectLinks(root);
        var result = new List<RecycledItem>();
        foreach (var dir in Directory.GetDirectories(root))
        {
            try
            {
                RejectLinks(dir); if (!Directory.Exists(System.IO.Path.Combine(dir, "内容"))) continue;
                var id = System.IO.Path.GetFileName(dir);
                if (!Guid.TryParseExact(id, "N", out _)) continue;
                var item = JsonSerializer.Deserialize<RecycledItem>(ReadMetadata(System.IO.Path.Combine(dir, "记录.json")));
                if (item != null && item.Id == id)
                {
                    if (string.IsNullOrWhiteSpace(item.OriginalRelativePath) || System.IO.Path.IsPathRooted(item.OriginalRelativePath) || item.OriginalRelativePath.Contains(':')) continue;
                    foreach (var part in item.OriginalRelativePath.Split(['/', '\\'])) ValidateName(part);
                    EnsurePath(System.IO.Path.Combine(LibraryRoot, item.OriginalRelativePath)); result.Add(item);
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or ArgumentException) { /* A damaged record must not prevent restoring unrelated items. */ }
        }
        return result.OrderByDescending(i => i.DeletedAt).ToList();
    }
    public string Restore(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("无效回收记录。");
        var record = GetRecycled().Single(i => i.Id == id); var dest = EnsurePath(System.IO.Path.Combine(LibraryRoot, record.OriginalRelativePath));
        if (Directory.Exists(dest) || File.Exists(dest)) throw new IOException("原位置已有同名内容，请先重命名该内容。");
        var source = System.IO.Path.Combine(UserRoot, "回收区", id, "内容"); foreach (var f in EnumerateSafe(source)) RejectLinks(f);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!); Directory.Move(source, dest); return dest;
    }
}

