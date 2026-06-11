using System.Text.Json.Serialization;

namespace XNote.Core.ImportExport;

/// <summary>
/// 导出 JSON 的根结构：一个 ExportNote 数组，写入 ZIP 内的 "notes_data.json"。
///
/// 字段名、类型、可空性必须与 Android 端 ExportImportUtils.ExportNote/ExportBlock
/// 经 Gson 序列化后的结果逐字段一致，否则跨平台导入会丢字段。Gson 默认省略 null 字段，
/// 因此这里用 JsonIgnoreCondition.WhenWritingNull（见 ExportJson）。
/// </summary>
public sealed class ExportNote
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";

    /// <summary>原始分类 id；老版本 ZIP 可能缺失（null）。</summary>
    [JsonPropertyName("categoryId")] public string? CategoryId { get; set; }

    /// <summary>原始分类显示名；老版本 ZIP 可能缺失（null）。</summary>
    [JsonPropertyName("categoryName")] public string? CategoryName { get; set; }

    /// <summary>是否置顶；老版本 ZIP 可能缺失（null → 导入回落 false）。</summary>
    [JsonPropertyName("isPinned")] public bool? IsPinned { get; set; }

    [JsonPropertyName("createdAt")] public long CreatedAt { get; set; }
    [JsonPropertyName("updatedAt")] public long UpdatedAt { get; set; }
    [JsonPropertyName("version")] public int Version { get; set; }
    [JsonPropertyName("blocks")] public List<ExportBlock> Blocks { get; set; } = new();
}

public sealed class ExportBlock
{
    /// <summary>"text" | "image" | "audio"</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("order")] public int Order { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }

    /// <summary>媒体文件在 ZIP 内 media/ 目录下的文件名（仅 image/audio）。</summary>
    [JsonPropertyName("mediaFileName")] public string? MediaFileName { get; set; }

    [JsonPropertyName("alt")] public string? Alt { get; set; }
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    [JsonPropertyName("duration")] public long? Duration { get; set; }
}
