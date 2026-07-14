using System.Text;
using System.Text.Json;
using ICSharpCode.SharpZipLib.Zip;
using XNote.Core.Models;

namespace XNote.Core.ImportExport;

/// <summary>
/// 跨平台导入/导出。ZIP 内容与 Android 端完全一致：
///   notes_data.json   —— ExportNote 数组（UTF-8 JSON）
///   media/&lt;file&gt;   —— 明文图片/音频
/// ZIP 使用 WinZip AES-256 加密（与 Android zip4j EncryptionMethod.AES /
/// KEY_STRENGTH_256 同一规范），密码保护。两端互通。
/// </summary>
public sealed class ExportImportService
{
    public const string DataEntryName = "notes_data.json";
    public const string MediaPrefix = "media/";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        // Gson 默认省略 null 字段——对齐它，避免写出 Android 端没有的空字段。
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public sealed class ExportInput
    {
        public required FullNote Note { get; init; }
        /// <summary>分类 id → 显示名，用于写入 categoryName（跨设备按名字回落匹配）。</summary>
        public required IReadOnlyDictionary<string, string> CategoryNameById { get; init; }
    }

    /// <summary>
    /// 把记事导出为加密 ZIP。<paramref name="mediaReader"/> 负责把 block.Url 指向的本地媒体
    /// 读成明文字节（默认直接读文件；上层可传入解密读取）。
    /// </summary>
    public void Export(
        IReadOnlyList<FullNote> notes,
        IReadOnlyDictionary<string, string> categoryNameById,
        string password,
        string outputZipPath,
        IProgress<string>? progress = null,
        Func<string, byte[]>? mediaReader = null)
    {
        var readMedia = mediaReader ?? File.ReadAllBytes;
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("导出密码不能为空", nameof(password));

        progress?.Report("处理记事数据...");

        var exportNotes = new List<ExportNote>(notes.Count);
        // 收集要写入 ZIP 的媒体：ZIP 内文件名 → 本地源文件路径
        var mediaToWrite = new Dictionary<string, string>();

        foreach (var full in notes)
        {
            var n = full.Note;
            var title = string.IsNullOrWhiteSpace(n.Title) ? "无标题" : n.Title;
            progress?.Report($"导出：{title}");

            var blocks = new List<ExportBlock>(full.Blocks.Count);
            foreach (var b in full.Blocks.OrderBy(x => x.Order))
            {
                switch (b.Type)
                {
                    case BlockType.Text:
                        blocks.Add(new ExportBlock { Type = "text", Order = b.Order, Text = b.Text });
                        break;

                    case BlockType.Image:
                    case BlockType.Audio:
                        var isImage = b.Type == BlockType.Image;
                        if (!string.IsNullOrEmpty(b.Url) && File.Exists(b.Url))
                        {
                            var ext = Path.GetExtension(b.Url);
                            var prefix = isImage ? "image" : "audio";
                            var fileName = $"{prefix}_{Guid.NewGuid()}{ext}";
                            mediaToWrite[fileName] = b.Url!;
                            blocks.Add(new ExportBlock
                            {
                                Type = isImage ? "image" : "audio",
                                Order = b.Order,
                                MediaFileName = fileName,
                                Alt = isImage ? b.Alt : null,
                                Width = isImage ? b.Width : null,
                                Height = isImage ? b.Height : null,
                                Duration = isImage ? null : b.Duration
                            });
                        }
                        else
                        {
                            blocks.Add(new ExportBlock
                            {
                                Type = "text",
                                Order = b.Order,
                                Text = isImage ? "[图片文件丢失]" : "[音频文件丢失]"
                            });
                        }
                        break;

                    case BlockType.File:
                        if (!string.IsNullOrEmpty(b.Url) && File.Exists(b.Url))
                        {
                            var name = string.IsNullOrWhiteSpace(b.Alt)
                                ? Path.GetFileName(b.Url)!
                                : b.Alt!;
                            var fileName = $"file_{Guid.NewGuid()}{Path.GetExtension(b.Url)}";
                            mediaToWrite[fileName] = b.Url!;
                            blocks.Add(new ExportBlock
                            {
                                Type = "file",
                                Order = b.Order,
                                // 兜底：不认识 "file" 的客户端把它当文本块，至少看得见附件名
                                Text = $"[附件] {name}",
                                MediaFileName = fileName,
                                Alt = name
                            });
                        }
                        else
                        {
                            blocks.Add(new ExportBlock
                            {
                                Type = "text",
                                Order = b.Order,
                                Text = $"[附件丢失] {b.Alt ?? ""}".TrimEnd()
                            });
                        }
                        break;
                }
            }

            exportNotes.Add(new ExportNote
            {
                Id = n.Id,
                Title = n.Title,
                CategoryId = n.CategoryId,
                CategoryName = categoryNameById.TryGetValue(n.CategoryId, out var cn) ? cn : "",
                IsPinned = n.IsPinned,
                CreatedAt = n.CreatedAt,
                UpdatedAt = n.UpdatedAt,
                Version = n.Version,
                Blocks = blocks
            });
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(exportNotes, WriteOptions);

        progress?.Report("创建加密ZIP文件...");

        var dir = Path.GetDirectoryName(outputZipPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var fsOut = File.Create(outputZipPath);
        using var zip = new ZipOutputStream(fsOut) { Password = password };
        zip.SetLevel(6);

        WriteEntry(zip, DataEntryName, json);
        foreach (var (entryName, sourcePath) in mediaToWrite)
            WriteEntry(zip, MediaPrefix + entryName, readMedia(sourcePath));

        zip.Finish();
        progress?.Report("导出完成！");
    }

    private static void WriteEntry(ZipOutputStream zip, string name, byte[] data)
    {
        var entry = new ZipEntry(name)
        {
            DateTime = DateTime.Now,
            AESKeySize = 256 // WinZip AES-256，与 zip4j KEY_STRENGTH_256 对应
        };
        zip.PutNextEntry(entry);
        zip.Write(data, 0, data.Length);
        zip.CloseEntry();
    }

    public sealed class ImportResult
    {
        public List<ImportNote> Notes { get; init; } = new();
        /// <summary>媒体被解压到的临时目录；调用方用完应删除。</summary>
        public string TempMediaDir { get; init; } = "";
    }

    /// <summary>
    /// 从加密 ZIP 解析记事。媒体解压到 <paramref name="mediaExtractDir"/>。
    /// 密码错误抛 <see cref="InvalidPasswordException"/>。
    /// </summary>
    public ImportResult Import(
        string zipPath,
        string password,
        string mediaExtractDir,
        IProgress<string>? progress = null)
    {
        progress?.Report("解密文件...");
        Directory.CreateDirectory(mediaExtractDir);

        byte[]? dataBytes = null;
        var extractedMedia = new Dictionary<string, string>(); // mediaFileName → 解压后绝对路径

        using (var zf = new ZipFile(zipPath) { Password = password })
        {
            foreach (ZipEntry entry in zf)
            {
                if (!entry.IsFile) continue;
                var name = entry.Name.Replace('\\', '/');

                if (string.Equals(name, DataEntryName, StringComparison.OrdinalIgnoreCase))
                {
                    dataBytes = ReadAll(zf, entry);
                }
                else if (name.StartsWith(MediaPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var fileName = Path.GetFileName(name);
                    if (string.IsNullOrEmpty(fileName)) continue;
                    // 防路径遍历：只取文件名，落到指定目录
                    var dest = Path.Combine(mediaExtractDir, fileName);
                    File.WriteAllBytes(dest, ReadAll(zf, entry));
                    extractedMedia[fileName] = dest;
                }
            }
        }

        if (dataBytes is null)
            throw new InvalidDataException("ZIP文件格式不正确，缺少数据文件 notes_data.json");

        progress?.Report("读取记事数据...");

        List<ExportNote>? exportNotes;
        try
        {
            exportNotes = JsonSerializer.Deserialize<List<ExportNote>>(
                Encoding.UTF8.GetString(dataBytes), ReadOptions);
        }
        catch (Exception e)
        {
            throw new InvalidDataException($"数据文件格式错误：{e.Message}", e);
        }

        exportNotes ??= new List<ExportNote>();
        if (exportNotes.Count > 10000)
            throw new InvalidDataException("导入的记事数量过多");

        var result = new ImportResult { TempMediaDir = mediaExtractDir };

        foreach (var en in exportNotes)
        {
            var blocks = new List<ImportBlock>(en.Blocks.Count);
            foreach (var eb in en.Blocks)
            {
                var type = BlockTypeJson.FromJson(eb.Type);
                string? mediaPath = null;
                if (type is BlockType.Image or BlockType.Audio or BlockType.File
                    && !string.IsNullOrEmpty(eb.MediaFileName)
                    && extractedMedia.TryGetValue(Path.GetFileName(eb.MediaFileName!), out var p))
                {
                    mediaPath = p;
                }

                blocks.Add(new ImportBlock
                {
                    Type = type,
                    Order = eb.Order,
                    Text = type == BlockType.Text ? Truncate(eb.Text, 10000) : null,
                    MediaFilePath = mediaPath,
                    Alt = eb.Alt,
                    Width = eb.Width,
                    Height = eb.Height,
                    Duration = eb.Duration
                });
            }

            result.Notes.Add(new ImportNote
            {
                Id = en.Id,
                Title = Truncate(en.Title, 200) ?? "",
                // 向后兼容：老版本无分类字段→回落 "daily"
                CategoryId = string.IsNullOrWhiteSpace(en.CategoryId) ? "daily" : en.CategoryId!,
                CategoryName = (en.CategoryName ?? "").Trim(),
                IsPinned = en.IsPinned ?? false,
                CreatedAt = en.CreatedAt,
                UpdatedAt = en.UpdatedAt,
                Version = en.Version,
                Blocks = blocks
            });
        }

        progress?.Report("导入完成！");
        return result;
    }

    private static byte[] ReadAll(ZipFile zf, ZipEntry entry)
    {
        try
        {
            using var s = zf.GetInputStream(entry);
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch (ZipException e) when (IsPasswordError(e))
        {
            throw new InvalidPasswordException();
        }
    }

    private static bool IsPasswordError(ZipException e) =>
        e.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        e.Message.Contains("Invalid", StringComparison.OrdinalIgnoreCase);

    private static string? Truncate(string? s, int max) =>
        s is null ? null : (s.Length <= max ? s : s.Substring(0, max));
}

/// <summary>密码错误（无法解密 ZIP）。</summary>
public sealed class InvalidPasswordException : Exception
{
    public InvalidPasswordException() : base("解密失败，请检查密码是否正确") { }
}
