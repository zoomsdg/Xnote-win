using XNote.Core.Models;

namespace XNote.Core.ImportExport;

/// <summary>解析后的导入记事。媒体已解压到临时目录，MediaFilePath 指向解压后的明文文件。</summary>
public sealed class ImportNote
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>解析出的分类 id（老版本 ZIP 回落到 "daily"）。</summary>
    public string CategoryId { get; set; } = "daily";

    /// <summary>解析出的分类名；无名时为空串。</summary>
    public string CategoryName { get; set; } = "";

    public bool IsPinned { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public int Version { get; set; }
    public List<ImportBlock> Blocks { get; set; } = new();
}

public sealed class ImportBlock
{
    public BlockType Type { get; set; } = BlockType.Text;
    public int Order { get; set; }
    public string? Text { get; set; }

    /// <summary>解压到临时目录后的媒体文件绝对路径；缺失/无效时为 null。</summary>
    public string? MediaFilePath { get; set; }

    public string? Alt { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public long? Duration { get; set; }
}
