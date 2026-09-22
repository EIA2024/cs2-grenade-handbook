namespace Handbook.Core;

public sealed record BrowseItem(string Id, string Label, string? Folder, Entry? Entry, bool Back = false);

// Focus-independent navigation. Only Confirm on a leaf returns a new displayed entry.
public sealed class QuickNavigation
{
    private readonly Dictionary<string, LibraryNode> nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> parents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Entry> ready = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> remembered = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<LibraryNode> roots = [];
    public bool IsOpen { get; private set; }
    public string Folder { get; private set; } = "";
    public IReadOnlyList<BrowseItem> Items { get; private set; } = [];
    public int Index { get; private set; }
    public BrowseItem? Selected => Items.Count == 0 ? null : Items[Index];
    public string Breadcrumb
    {
        get
        {
            if (Folder.Length == 0) return "所有合集";
            var names = new List<string>(); var p = Folder;
            while (nodes.TryGetValue(p, out var node)) { names.Add(node.Name); p = parents[p]; }
            names.Reverse(); return string.Join(" / ", names);
        }
    }
    public void Update(LibrarySnapshot snapshot)
    {
        nodes.Clear(); parents.Clear(); ready.Clear();
        foreach (var entry in snapshot.Entries.Where(e => e.Ready)) ready[entry.Path] = entry;
        LibraryNode? Prune(LibraryNode node, string parent)
        {
            var children = node.Children.Select(c => Prune(c, node.Path)).OfType<LibraryNode>().ToList();
            if (!ready.ContainsKey(node.Path) && children.Count == 0) return null;
            var clean = node with { Children = children }; nodes[node.Path] = clean; parents[node.Path] = parent; return clean;
        }
        roots = snapshot.Roots.Select(n => Prune(n, "")).OfType<LibraryNode>().ToList();
        if (Folder.Length > 0 && !nodes.ContainsKey(Folder)) Folder = "";
        Rebuild();
    }
    public void Open(string? preferredEntry = null, string? savedFolder = null, string? savedItem = null)
    {
        if (savedFolder != null && (savedFolder.Length == 0 || nodes.ContainsKey(savedFolder)))
        { Folder = savedFolder; if (savedItem != null) remembered[Folder] = savedItem; }
        else if (preferredEntry != null && parents.TryGetValue(preferredEntry, out var parent))
        { Folder = parent; remembered[Folder] = preferredEntry; }
        IsOpen = true; Rebuild();
    }
    public void Cancel() { Remember(); IsOpen = false; }
    public void Move(int delta)
    {
        if (!IsOpen || Items.Count == 0) return;
        Index = ((Index + delta) % Items.Count + Items.Count) % Items.Count; Remember();
    }
    public Entry? Confirm()
    {
        if (!IsOpen || Selected == null) return null;
        var item = Selected; Remember();
        if (item.Entry != null) { IsOpen = false; return item.Entry; }
        if (item.Folder != null) { Folder = item.Folder; Rebuild(); }
        return null;
    }
    private void Remember() { if (Selected != null) remembered[Folder] = Selected.Id; }
    private void Rebuild()
    {
        var result = new List<BrowseItem>();
        if (Folder.Length > 0 && nodes.TryGetValue(Folder, out var node))
        {
            result.Add(new("$back", "← 返回上一级", parents[Folder], null, true));
            if (ready.TryGetValue(Folder, out var own)) result.Add(new("$self", "查看本目录道具 · " + own.Name, null, own));
            AddChildren(node.Children);
        }
        else AddChildren(roots);
        void AddChildren(IEnumerable<LibraryNode> children)
        {
            foreach (var child in children)
            {
                if (child.Children.Count == 0 && ready.TryGetValue(child.Path, out var leaf)) result.Add(new(child.Path, child.Name, null, leaf));
                else result.Add(new(child.Path, child.Name + "  ›", child.Path, null));
            }
        }
        Items = result;
        Index = remembered.TryGetValue(Folder, out var last) ? result.FindIndex(i => i.Id == last) : -1;
        if (Index < 0) Index = result.Count > 1 && result[0].Back ? 1 : 0;
    }
}
