using Handbook.Storage;
using System.Windows.Media.Imaging;
using Handbook.Core;

namespace Handbook.Desktop;

internal static class Images
{
    private static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> Order = new();
    private static readonly Dictionary<string, string?> Validation = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();
    public static void Validate(string path)
    {
        LibraryRepository.ValidateImage(path);
        var info = new FileInfo(path); var key = $"{path}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
        lock (Gate)
        {
            if (Validation.TryGetValue(key, out var previous)) { if (previous != null) throw new InvalidDataException(previous); return; }
            if (Validation.Count > 10000) Validation.Clear();
            try { Decode(path, 256); Validation[key] = null; }
            catch (Exception e) when (e is not OutOfMemoryException) { Validation[key] = e.Message; throw new InvalidDataException(e.Message, e); }
        }
    }
    public static BitmapSource? Load(string? path, bool large = false)
    {
        if (path == null) return null;
        LibraryRepository.ValidateImage(path);
        var info = new FileInfo(path); var key = $"{path}|{info.LastWriteTimeUtc.Ticks}|{info.Length}|{large}";
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var bitmap = Decode(path, large ? 2560 : 1200);
            Cache[key] = bitmap; Order.Enqueue(key);
            while (Order.Count > 8 || Cache.Values.Sum(b => (long)b.PixelWidth * b.PixelHeight * 4) > 48L * 1024 * 1024)
                Cache.Remove(Order.Dequeue());
            return bitmap;
        }
    }
    private static BitmapSource Decode(string path, int maximumDimension)
    {
        using var stream = File.OpenRead(path);
        // Header decode before full decode prevents huge decompression allocations.
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth < 1 || frame.PixelHeight < 1 || (long)frame.PixelWidth * frame.PixelHeight > 80_000_000)
            throw new InvalidDataException("图片像素过大（上限 8000 万像素）。");
        stream.Position = 0;
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (frame.PixelWidth >= frame.PixelHeight) bitmap.DecodePixelWidth = Math.Min(frame.PixelWidth, maximumDimension);
        else bitmap.DecodePixelHeight = Math.Min(frame.PixelHeight, maximumDimension);
        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        return bitmap;
    }
    public static void Clear() { lock (Gate) { Cache.Clear(); Order.Clear(); } }
}

