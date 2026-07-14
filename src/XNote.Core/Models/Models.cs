namespace XNote.Core.Models;

/// <summary>
/// 内容块类型。序列化到导出 JSON 时用小写字符串 "text"/"image"/"audio"/"file"，
/// 与 Android 端 Gson 的 @SerializedName 保持一致。
///
/// "file"（附件）是 Windows 端新增的类型：本项目不解析其内容，只负责加密保存、
/// 随导出 ZIP 携带、交给系统默认程序打开。老客户端不认识该类型时会回落成文本块，
/// 因此导出时同时写入 text（"[附件] 原文件名"）作为兜底展示。
/// </summary>
public enum BlockType
{
    Text,
    Image,
    Audio,
    File
}

public static class BlockTypeJson
{
    public static string ToJson(BlockType t) => t switch
    {
        BlockType.Text => "text",
        BlockType.Image => "image",
        BlockType.Audio => "audio",
        BlockType.File => "file",
        _ => "text"
    };

    public static BlockType FromJson(string? s) => s switch
    {
        "image" => BlockType.Image,
        "audio" => BlockType.Audio,
        "file" => BlockType.File,
        _ => BlockType.Text
    };
}

/// <summary>记事（与 Android Note 实体对应）。</summary>
public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string CategoryId { get; set; } = "daily";

    /// <summary>所属标签页（tab）。纯本地概念，不进导出 ZIP。历史数据为空时迁移到 "local"。</summary>
    public string NotebookId { get; set; } = "local";

    /// <summary>导入来源纪事的原始 id，用于去重；本地新建纪事为 null。不进导出 ZIP。</summary>
    public string? SourceId { get; set; }

    public bool IsPinned { get; set; }
    public long CreatedAt { get; set; } = Time.NowMillis();
    public long UpdatedAt { get; set; } = Time.NowMillis();
    public int Version { get; set; } = 1;
}

/// <summary>
/// 内容块（与 Android NoteBlock 实体对应）。Url 为本地媒体文件绝对路径。
/// 附件块（<see cref="BlockType.File"/>）复用 Url（加密后的落盘路径）与 Alt（原始文件名）。
/// </summary>
public sealed class NoteBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string NoteId { get; set; } = "";
    public BlockType Type { get; set; } = BlockType.Text;
    public int Order { get; set; }
    public string? Text { get; set; }
    public string? Url { get; set; }
    public string? Alt { get; set; }
    public long? Duration { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    /// <summary>附件原始字节数，仅用于本地列表显示。纯本地字段，不进导出 ZIP（导入时按解压出的文件重算）。</summary>
    public long? Size { get; set; }
    public long CreatedAt { get; set; } = Time.NowMillis();
    public long UpdatedAt { get; set; } = Time.NowMillis();
}

/// <summary>完整记事（含所有块）。</summary>
public sealed class FullNote
{
    public Note Note { get; set; } = new();
    public List<NoteBlock> Blocks { get; set; } = new();
}

/// <summary>标签页（tab / 笔记本）。本地分区层，位于纪事之上。不参与跨平台导出。</summary>
public sealed class Notebook
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    /// <summary>标签条显示顺序。</summary>
    public int Order { get; set; }
    public long CreatedAt { get; set; } = Time.NowMillis();
}

/// <summary>分类（与 Android Category 实体对应）。</summary>
public sealed class Category
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public long CreatedAt { get; set; } = Time.NowMillis();
}

public static class Time
{
    /// <summary>等价于 Android System.currentTimeMillis()。</summary>
    public static long NowMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
