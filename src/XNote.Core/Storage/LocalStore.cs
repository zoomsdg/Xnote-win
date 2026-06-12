using System.Text;
using System.Text.Json;
using XNote.Core.ImportExport;
using XNote.Core.Models;

namespace XNote.Core.Storage;

/// <summary>
/// 本地存储（JSON 文件 + media 目录）。负责记事/分类的增删改查、默认分类种子，
/// 以及与 <see cref="ExportImportService"/> 协作完成导入/导出。
///
/// 静态加密：纪事正文（notes.json）、分类（categories.json）与媒体均用 Windows DPAPI
/// （CurrentUser）加密落盘（文件头 <c>XNW1</c>），读取时自动解密；历史明文文件可直接读入，
/// 下次保存即自动迁移为加密。这只影响“本机静态存储”，不影响跨平台互通——导出 ZIP 内仍是约定明文格式。
/// </summary>
public sealed class LocalStore
{
    private readonly AppPaths _paths;
    private readonly ExportImportService _io = new();
    private readonly MediaStore _media;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly List<FullNote> _notes = new();
    private readonly List<Category> _categories = new();

    /// <summary>不可删除的默认分类固定 id（与 Android 端一致）。</summary>
    public static readonly string[] ProtectedCategoryIds = { "daily", "work", "thoughts" };

    public LocalStore(AppPaths? paths = null)
    {
        _paths = paths ?? AppPaths.Default;
        _media = new MediaStore(_paths);
        _media.CleanupTemp();
        CleanupTemp();
        Load();
    }

    /// <summary>清理 temp 目录里遗留的录音临时文件与导入解压目录。</summary>
    private void CleanupTemp()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_paths.TempDir, "rec_*"))
                try { File.Delete(f); } catch { /* ignore */ }
            foreach (var d in Directory.EnumerateDirectories(_paths.TempDir, "import_*"))
                try { Directory.Delete(d, recursive: true); } catch { /* ignore */ }
        }
        catch { /* ignore */ }
    }

    /// <summary>媒体访问点（加密读写 / 解密到临时文件）。</summary>
    public MediaStore Media => _media;

    // ---------- 加载 / 持久化 ----------

    private void Load()
    {
        _notes.Clear();
        _categories.Clear();

        if (File.Exists(_paths.NotesFile))
        {
            var loaded = JsonSerializer.Deserialize<List<FullNote>>(ReadJson(_paths.NotesFile));
            if (loaded != null) _notes.AddRange(loaded);
        }

        if (File.Exists(_paths.CategoriesFile))
        {
            var loaded = JsonSerializer.Deserialize<List<Category>>(ReadJson(_paths.CategoriesFile));
            if (loaded != null) _categories.AddRange(loaded);
        }

        EnsureDefaultCategories();

        // 历史明文数据文件：启动时立即加密迁移
        if (File.Exists(_paths.NotesFile) && !MediaCryptor.IsEncrypted(_paths.NotesFile)) SaveNotes();
        if (File.Exists(_paths.CategoriesFile) && !MediaCryptor.IsEncrypted(_paths.CategoriesFile)) SaveCategories();
    }

    /// <summary>读取并解密 JSON 数据文件（历史明文文件原样返回）。</summary>
    private static string ReadJson(string path) =>
        Encoding.UTF8.GetString(MediaCryptor.ReadPlain(path));

    /// <summary>序列化并 DPAPI 加密写入 JSON 数据文件。</summary>
    private static void WriteJson<T>(string path, T value) =>
        MediaCryptor.EncryptToFile(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOpts)), path);

    private void SaveNotes() => WriteJson(_paths.NotesFile, _notes);

    private void SaveCategories() => WriteJson(_paths.CategoriesFile, _categories);

    private void EnsureDefaultCategories()
    {
        if (_categories.Count > 0) return; // 仅首次种子，用户删掉的不复活
        var now = Time.NowMillis();
        _categories.AddRange(new[]
        {
            new Category { Id = "daily", Name = "日常", IsDefault = true, CreatedAt = now },
            new Category { Id = "work", Name = "工作", IsDefault = true, CreatedAt = now },
            new Category { Id = "thoughts", Name = "感悟", IsDefault = true, CreatedAt = now },
            new Category { Id = "finance", Name = "金融", IsDefault = true, CreatedAt = now },
            new Category { Id = "health", Name = "健康", IsDefault = true, CreatedAt = now },
        });
        SaveCategories();
    }

    // ---------- 查询 ----------

    public IReadOnlyList<Category> Categories => _categories;

    public Category? GetCategory(string id) => _categories.FirstOrDefault(c => c.Id == id);

    public string CategoryName(string id) => GetCategory(id)?.Name ?? "";

    /// <summary>记事摘要列表：置顶优先，再按更新时间倒序。可按分类/关键字过滤。</summary>
    public List<FullNote> ListNotes(string? categoryId = null, string? query = null)
    {
        IEnumerable<FullNote> q = _notes;
        if (!string.IsNullOrEmpty(categoryId))
            q = q.Where(n => n.Note.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            q = q.Where(n => Matches(n, term));
        }
        return q.OrderByDescending(n => n.Note.IsPinned)
                .ThenByDescending(n => n.Note.UpdatedAt)
                .ToList();
    }

    private static bool Matches(FullNote n, string term)
    {
        if (n.Note.Title.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
        return n.Blocks.Any(b => b.Type == BlockType.Text
                                 && b.Text != null
                                 && b.Text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public FullNote? GetNote(string id) => _notes.FirstOrDefault(n => n.Note.Id == id);

    // ---------- 记事 CRUD ----------

    public FullNote CreateNote(string title = "无标题")
    {
        var now = Time.NowMillis();
        var note = new Note
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            CategoryId = "daily",
            CreatedAt = now,
            UpdatedAt = now
        };
        var full = new FullNote
        {
            Note = note,
            Blocks = { new NoteBlock { NoteId = note.Id, Type = BlockType.Text, Order = 0, Text = "" } }
        };
        _notes.Add(full);
        SaveNotes();
        return full;
    }

    /// <summary>整体保存一条记事（覆盖同 id）。会刷新 updatedAt。</summary>
    public void SaveNote(FullNote full, bool touch = true)
    {
        if (touch) full.Note.UpdatedAt = Time.NowMillis();
        var idx = _notes.FindIndex(n => n.Note.Id == full.Note.Id);
        if (idx >= 0) _notes[idx] = full;
        else _notes.Add(full);
        SaveNotes();
    }

    public void DeleteNote(string id)
    {
        var note = GetNote(id);
        if (note == null) return;
        foreach (var b in note.Blocks)
            TryDeleteMedia(b.Url);
        _notes.RemoveAll(n => n.Note.Id == id);
        SaveNotes();
    }

    public void SetPinned(string id, bool pinned)
    {
        var n = GetNote(id);
        if (n == null) return;
        n.Note.IsPinned = pinned;
        n.Note.UpdatedAt = Time.NowMillis();
        SaveNotes();
    }

    public void SetCategory(string id, string categoryId)
    {
        var n = GetNote(id);
        if (n == null) return;
        n.Note.CategoryId = categoryId;
        n.Note.UpdatedAt = Time.NowMillis();
        SaveNotes();
    }

    /// <summary>在 temp 目录内预留一个新文件路径（用于录音等先写明文、再加密入库的场景）。</summary>
    public string ReserveTempPath(string ext)
        => Path.Combine(_paths.TempDir, "rec_" + Guid.NewGuid().ToString("N") + ext);

    /// <summary>把外部明文媒体文件加密存入 media 目录，返回加密文件绝对路径。</summary>
    public string ImportMediaFile(string sourcePath, bool isImage)
        => _media.SaveFromFile(sourcePath, isImage);

    private void TryDeleteMedia(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            // 只删 media 目录内的文件，避免误删外部路径
            if (Path.GetDirectoryName(path) == _paths.MediaDir && File.Exists(path))
                File.Delete(path);
        }
        catch { /* 忽略清理失败 */ }
    }

    // ---------- 分类 ----------

    public (string Id, bool IsNew) CreateCategory(string name)
    {
        var trimmed = name.Trim();
        var existing = _categories.FirstOrDefault(c =>
            string.Equals(c.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return (existing.Id, false);

        var cat = new Category { Id = Guid.NewGuid().ToString(), Name = trimmed, IsDefault = false };
        _categories.Add(cat);
        SaveCategories();
        return (cat.Id, true);
    }

    public void DeleteCategory(string categoryId)
    {
        if (ProtectedCategoryIds.Contains(categoryId))
            throw new InvalidOperationException("默认分类不可删除");
        var cat = GetCategory(categoryId);
        if (cat == null) return;

        // 该分类下的记事转移到“日常”
        foreach (var n in _notes.Where(n => n.Note.CategoryId == categoryId))
        {
            n.Note.CategoryId = "daily";
            n.Note.UpdatedAt = Time.NowMillis();
        }
        _categories.RemoveAll(c => c.Id == categoryId);
        SaveCategories();
        SaveNotes();
    }

    /// <summary>导入分类解析：id 命中 → 名字命中 → 按名字自建 → 回落 daily（与 Android 一致）。</summary>
    private string ResolveImportCategoryId(string importCategoryId, string importCategoryName)
    {
        if (!string.IsNullOrWhiteSpace(importCategoryId) && GetCategory(importCategoryId) != null)
            return importCategoryId;

        var name = importCategoryName.Trim();
        if (name.Length > 0)
        {
            var byName = _categories.FirstOrDefault(c => c.Name.Trim() == name);
            if (byName != null) return byName.Id;

            var (id, _) = CreateCategory(name);
            return id;
        }
        return "daily";
    }

    // ---------- 导入 / 导出 ----------

    public void Export(IReadOnlyList<FullNote> notes, string password, string outputZipPath,
        IProgress<string>? progress = null)
    {
        var nameById = _categories.ToDictionary(c => c.Id, c => c.Name);
        // 导出读取媒体时解密成明文写入 ZIP（跨平台 ZIP 内一律明文）
        _io.Export(notes, nameById, password, outputZipPath, progress, _media.ReadPlain);
    }

    public void ExportAll(string password, string outputZipPath, IProgress<string>? progress = null)
        => Export(_notes.ToList(), password, outputZipPath, progress);

    /// <summary>导入 ZIP，落盘为本地记事。返回成功导入的条数。</summary>
    public int Import(string zipPath, string password, IProgress<string>? progress = null)
    {
        var extractDir = Path.Combine(_paths.TempDir, "import_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = _io.Import(zipPath, password, extractDir, progress);
            foreach (var imp in result.Notes)
            {
                progress?.Report($"导入：{(string.IsNullOrWhiteSpace(imp.Title) ? "无标题" : imp.Title)}");
                SaveImported(imp);
            }
            SaveNotes();
            return result.Notes.Count;
        }
        finally
        {
            try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true); }
            catch { /* 忽略临时目录清理失败 */ }
        }
    }

    private void SaveImported(ImportNote imp)
    {
        var categoryId = ResolveImportCategoryId(imp.CategoryId, imp.CategoryName);
        var note = new Note
        {
            Id = Guid.NewGuid().ToString(), // 新 id，避免与本地冲突
            Title = imp.Title,
            CategoryId = categoryId,
            IsPinned = imp.IsPinned,
            CreatedAt = imp.CreatedAt,
            UpdatedAt = imp.UpdatedAt,
            Version = 1
        };

        var blocks = new List<NoteBlock>(imp.Blocks.Count);
        foreach (var ib in imp.Blocks.OrderBy(b => b.Order))
        {
            string? url = null;
            if (ib.Type is BlockType.Image or BlockType.Audio
                && ib.MediaFilePath != null && File.Exists(ib.MediaFilePath))
            {
                url = ImportMediaFile(ib.MediaFilePath, ib.Type == BlockType.Image);
            }

            blocks.Add(new NoteBlock
            {
                Id = Guid.NewGuid().ToString(),
                NoteId = note.Id,
                Type = ib.Type,
                Order = ib.Order,
                Text = ib.Type == BlockType.Text ? ib.Text : null,
                Url = url,
                Alt = ib.Alt,
                Width = ib.Width,
                Height = ib.Height,
                Duration = ib.Duration,
                CreatedAt = imp.CreatedAt,
                UpdatedAt = imp.UpdatedAt
            });
        }

        _notes.Add(new FullNote { Note = note, Blocks = blocks });
    }
}
