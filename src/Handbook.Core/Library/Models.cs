namespace Handbook.Core;

public enum NodeKind { Collection, Map, Side, Folder, Entry }
public sealed record LibraryNode(string Path, string Name, NodeKind Kind, IReadOnlyList<LibraryNode> Children);
public sealed record Entry(string Path, string Collection, string Map, string Side, string RelativePath,
    string? Standing, string? Aiming, string Notes, IReadOnlyList<string> Errors)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public bool Ready => Standing != null && Aiming != null && Errors.Count == 0;
    public string Label => $"{RelativePath}{(Ready ? "" : "  [草稿]")}";
}
public sealed record LibrarySnapshot(IReadOnlyList<LibraryNode> Roots, IReadOnlyList<Entry> Entries, IReadOnlyList<string> Errors);
public sealed record RecycledItem(string Id, string OriginalRelativePath, DateTime DeletedAt);
public sealed record ImportResult(string CollectionPath, bool AlreadyImported, IReadOnlyList<string> Warnings);

