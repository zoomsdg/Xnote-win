namespace XNote.Core.Storage;

/// <summary>应用数据目录。默认 %AppData%\XNote，可通过构造函数覆盖（便于测试）。</summary>
public sealed class AppPaths
{
    public string Root { get; }
    public string DataDir => Path.Combine(Root, "data");
    public string MediaDir => Path.Combine(Root, "media");
    public string TempDir => Path.Combine(Root, "temp");
    public string NotesFile => Path.Combine(DataDir, "notes.json");
    public string CategoriesFile => Path.Combine(DataDir, "categories.json");

    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XNote");
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(MediaDir);
        Directory.CreateDirectory(TempDir);
    }

    public static AppPaths Default { get; } = new();
}
