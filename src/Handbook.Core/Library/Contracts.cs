namespace Handbook.Core;

public interface ILibraryRepository
{
    LibrarySnapshot Scan();
    string CreateCollection(string name);
    string CreateMap(string collection, string name);
    string CreateFolder(string parent, string name, bool entry);
    void SaveNotes(string entry, string text);
    void SetImage(string entry, string role, string source);
    string Rename(string path, string name);
    string Transfer(string path, string parent, bool copy);
    void Recycle(string path);
    IReadOnlyList<RecycledItem> GetRecycled();
    string Restore(string id);
}
public interface IPackageService
{
    ImportResult Import(string zipPath);
    void Export(string collectionPath, string zipPath);
}

