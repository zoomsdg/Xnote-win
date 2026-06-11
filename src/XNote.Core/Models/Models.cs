namespace XNote.Core.Models;

/// <summary>
/// 内容块类型。序列化到导出 JSON 时用小写字符串 "text"/"image"/"audio"，
/// 与 Android 端 Gson 的 @SerializedName 保持一致。
/// </summary>
public enum BlockType
{
    Text,
    Image,
    Audio
}

public static class BlockTypeJson
{
    public static string ToJson(BlockType t) => t switch
    {
        BlockType.Text => "text",
        BlockType.Image => "image",
        BlockType.Audio => "audio",
        _ => "text"
    };

    public static BlockType FromJson(string? s) => s switch
    {
        "image" => BlockType.Image,
        "audio" => BlockType.Audio,
        _ => BlockType.Text
    };
}

/// <summary>记事（与 Android Note 实体对应）。</summary>
public sealed class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string CategoryId { get; set; } = "daily";
    public bool IsPinned { get; set; }
    public long CreatedAt { get; set; } = Time.NowMillis();
    public long UpdatedAt { get; set; } = Time.NowMillis();
    public int Version { get; set; } = 1;
}

/// <summary>内容块（与 Android NoteBlock 实体对应）。Url 为本地媒体文件绝对路径。</summary>
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
    public long CreatedAt { get; set; } = Time.NowMillis();
    public long UpdatedAt { get; set; } = Time.NowMillis();
}

/// <summary>完整记事（含所有块）。</summary>
public sealed class FullNote
{
    public Note Note { get; set; } = new();
    public List<NoteBlock> Blocks { get; set; } = new();
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
