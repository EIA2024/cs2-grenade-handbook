using Handbook.Storage;
using System.Text;
using Handbook.Core;

namespace Handbook.Desktop;

// The location pointer is untrusted text, not permission to follow a network/device path.
internal static class DataRootLocator
{
    internal static string ValidateRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || !Path.IsPathFullyQualified(path) ||
            path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/') || path[2..].Contains(':'))
            throw new InvalidDataException("资料根目录必须是本机磁盘上的绝对路径，不支持网络或设备路径。");
        var full = Path.GetFullPath(path);
        LibraryRepository.RejectLinks(full);
        return full;
    }

    internal static string? Read(string pointer, out string? warning)
    {
        warning = null;
        try
        {
            LibraryRepository.RejectLinks(pointer);
            if (!File.Exists(pointer)) return null;
            using var file = new FileStream(pointer, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > 16 * 1024) throw new InvalidDataException("资料位置记录过大。");
            using var reader = new StreamReader(file, new UTF8Encoding(false, true), false);
            return ValidateRoot(reader.ReadToEnd().TrimStart('\uFEFF').Trim());
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { warning = "资料位置记录无法使用，将尝试程序目录：" + e.Message; return null; }
    }

    internal static string? Save(string pointer, string root)
    {
        try
        {
            root = ValidateRoot(root);
            LibraryRepository.RejectLinks(pointer);
            Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
            LibraryRepository.AtomicWrite(pointer, root);
            return null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { return "本次使用所选目录，但无法记住该位置，下次启动请重新选择：" + e.Message; }
    }
}



