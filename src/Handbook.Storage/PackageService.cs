using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using Handbook.Core;

namespace Handbook.Storage;

public sealed class PackageService(LibraryRepository repository) : IPackageService
{
    private const long MaxExpandedBytes = 1024L * 1024 * 1024;
    private const int MaxFiles = 10000;
    private readonly string ledger = Path.Combine(repository.UserRoot, "导入记录.json");
    private static readonly uint[] CrcTable = BuildCrcTable();
    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++) { uint crc = i; for (int j = 0; j < 8; j++) crc = (crc & 1) != 0 ? 0xedb88320U ^ (crc >> 1) : crc >> 1; table[i] = crc; }
        return table;
    }
    public ImportResult Import(string zipPath)
    {
        LibraryRepository.RejectLinks(zipPath);
        using var input = File.OpenRead(zipPath);
        if (input.Length > MaxExpandedBytes) throw new InvalidDataException("ZIP 超过 1 GB。");
        var hash = Convert.ToHexString(SHA256.HashData(input)); input.Position = 0;
        var known = File.Exists(ledger) ? JsonSerializer.Deserialize<Dictionary<string, string>>(LibraryRepository.ReadMetadata(ledger)) ?? [] : [];
        if (known.TryGetValue(hash, out var previous))
        {
            LibraryRepository.ValidateName(previous);
            var existing = repository.EnsurePath(Path.Combine(repository.LibraryRoot, previous));
            if (Directory.Exists(existing)) return new(existing, true, []);
        }
        var staging = Path.Combine(repository.UserRoot, "导入-" + Guid.NewGuid().ToString("N"));
        LibraryRepository.RejectLinks(staging);
        Directory.CreateDirectory(staging);
        try
        {
            using var archive = new ZipArchive(input, ZipArchiveMode.Read);
            if (archive.Entries.Count > MaxFiles) throw new InvalidDataException("ZIP 最多允许 10000 个文件和目录。");
            long total = 0; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in archive.Entries)
            {
                if (item.IsEncrypted) throw new InvalidDataException("不支持加密 ZIP，请先解密后重新打包。");
                var relative = item.FullName.Replace('\\', '/');
                if (relative.StartsWith('/') || relative.Contains(':')) throw new InvalidDataException("ZIP 包含绝对路径。");
                var parts = relative.TrimEnd('/').Split('/');
                foreach (var part in parts) LibraryRepository.ValidateName(part);
                if (parts.Length > (relative.EndsWith('/') ? 21 : 22)) throw new InvalidDataException("ZIP 目录过深。");
                roots.Add(parts[0]);
                if (roots.Count > 1) throw new InvalidDataException("ZIP 第一层必须只有一个合集目录。");
                if (parts.Length == 1 && !relative.EndsWith('/')) throw new InvalidDataException("文件必须放入合集目录。");
                if (((item.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (item.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("ZIP 不允许符号链接。");
                var destination = Path.GetFullPath(Path.Combine(staging, relative));
                if (!destination.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ZIP 路径越界。");
                if (!seen.Add(destination.TrimEnd(Path.DirectorySeparatorChar))) throw new InvalidDataException("ZIP 存在重复路径。");
                total = checked(total + item.Length);
                if (total > MaxExpandedBytes || item.Length > 64L * 1024 * 1024) throw new InvalidDataException("解压限制：总计 1 GB、单文件 64 MB。");
                if (relative.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var source = item.Open(); using var output = new FileStream(destination, FileMode.CreateNew);
                var buffer = new byte[81920]; long written = 0; int read; uint crc = uint.MaxValue;
                while ((read = source.Read(buffer)) > 0)
                {
                    written += read; if (written > item.Length || written > 64L * 1024 * 1024) throw new InvalidDataException("解压大小与 ZIP 声明不符。");
                    for (int i = 0; i < read; i++) crc = CrcTable[(crc ^ buffer[i]) & 255] ^ (crc >> 8);
                    output.Write(buffer, 0, read);
                }
                if (written != item.Length) throw new InvalidDataException("ZIP 文件不完整。");
                if (~crc != item.Crc32) throw new InvalidDataException("ZIP 文件 CRC 校验失败。");
            }
            if (roots.Count != 1) throw new InvalidDataException("ZIP 为空。");
            var rootName = roots.Single(); var stagedCollection = Path.Combine(staging, rootName);
            var warnings = ValidateStructure(stagedCollection);
            var dest = repository.EnsurePath(Path.Combine(repository.LibraryRoot, rootName)); int suffix = 2;
            while (Directory.Exists(dest) || File.Exists(dest)) dest = repository.EnsurePath(Path.Combine(repository.LibraryRoot, $"{rootName} ({suffix++})"));
            Directory.Move(stagedCollection, dest);
            known[hash] = Path.GetFileName(dest);
            try { LibraryRepository.AtomicWrite(ledger, JsonSerializer.Serialize(known)); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { warnings.Add("合集已导入，但去重记录保存失败：" + e.Message); }
            return new(dest, false, warnings);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    private List<string> ValidateStructure(string collection)
    {
        var warnings = new List<string>();
        foreach (var map in Directory.GetDirectories(collection))
        {
            if (!Path.GetFileName(map).All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '_' or '-')) throw new InvalidDataException("地图目录请使用英文名称。");
            foreach (var side in Directory.GetDirectories(map))
                if (Path.GetFileName(side) is not ("CT" or "T")) throw new InvalidDataException("地图下的目录必须为 CT 或 T。");
        }
        foreach (var file in LibraryRepository.EnumerateSafe(collection))
        {
            var parts = Path.GetRelativePath(collection, file).Split(Path.DirectorySeparatorChar);
            var role = Path.GetFileNameWithoutExtension(file);
            if (parts.Length < 4) throw new InvalidDataException("资料文件必须位于 地图/CT或T/道具 目录内。");
            if (Path.GetFileName(file).Equals("说明.txt", StringComparison.OrdinalIgnoreCase))
            {
                if (new FileInfo(file).Length > LibraryRepository.MaxNotesBytes) throw new InvalidDataException("说明超过 1 MB。");
                var bytes = File.ReadAllBytes(file);
                try { _ = new System.Text.UTF8Encoding(false, true).GetString(bytes); }
                catch (System.Text.DecoderFallbackException e) { throw new InvalidDataException("说明必须使用 UTF-8 编码。", e); }
                continue;
            }
            if (role is not ("站位" or "瞄准") || !LibraryRepository.ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                throw new InvalidDataException("ZIP 只允许站位图、瞄准图和说明.txt，不允许其他附件。");
            if (role is "站位" or "瞄准")
            {
                if (parts.Length < 4) throw new InvalidDataException("道具图片必须位于 地图/CT或T/道具 目录内。");
                repository.CheckImage(file);
                var matches = Directory.GetFiles(Path.GetDirectoryName(file)!).Count(f => Path.GetFileNameWithoutExtension(f) == role && LibraryRepository.ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                if (matches > 1) throw new InvalidDataException($"{parts[^2]} 的{role}图重复。");
            }
        }
        return warnings;
    }
    public void Export(string collectionPath, string zipPath)
    {
        repository.EnsurePath(collectionPath);
        if (Path.GetDirectoryName(collectionPath) != repository.LibraryRoot) throw new InvalidDataException("请选择一个合集导出。");
        _ = ValidateStructure(collectionPath);
        var target = Path.GetFullPath(zipPath); LibraryRepository.RejectLinks(target);
        if (target.StartsWith(collectionPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("不能导出到合集自身目录中。");
        if (File.Exists(target)) throw new IOException("请使用新文件名，原始 ZIP 不会被覆盖。");
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create)) AddDirectory(zip, collectionPath, Path.GetFileName(collectionPath));
            File.Move(temp, target);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void AddDirectory(ZipArchive zip, string dir, string relative, int depth = 0)
    {
        if (depth > 20) throw new InvalidDataException("目录层级超过 20 层。");
        LibraryRepository.RejectLinks(dir); zip.CreateEntry(relative + "/");
        foreach (var file in Directory.GetFiles(dir)) { LibraryRepository.RejectLinks(file); zip.CreateEntryFromFile(file, relative + "/" + Path.GetFileName(file)); }
        foreach (var child in Directory.GetDirectories(dir)) AddDirectory(zip, child, relative + "/" + Path.GetFileName(child), depth + 1);
    }
}

