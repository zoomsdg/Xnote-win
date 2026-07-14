using System.Linq;
using System.Windows;
using XNote.Core.Models;

namespace XNote.App;

/// <summary>列表行视图模型。</summary>
public sealed class NoteRowVM
{
    public FullNote Full { get; }

    public NoteRowVM(FullNote full, string categoryName)
    {
        Full = full;
        CategoryName = categoryName;
    }

    public string Id => Full.Note.Id;
    public string Title => string.IsNullOrWhiteSpace(Full.Note.Title) ? "无标题" : Full.Note.Title;
    public string CategoryName { get; }

    public string Preview
    {
        get
        {
            var text = Full.Blocks
                .Where(b => b.Type == BlockType.Text && !string.IsNullOrWhiteSpace(b.Text))
                .OrderBy(b => b.Order)
                .Select(b => b.Text!.Trim())
                .FirstOrDefault();
            return text ?? "";
        }
    }

    public string MediaText
    {
        get
        {
            var img = Full.Blocks.Count(b => b.Type == BlockType.Image);
            var aud = Full.Blocks.Count(b => b.Type == BlockType.Audio);
            var att = Full.Blocks.Count(b => b.Type == BlockType.File);
            var parts = new System.Collections.Generic.List<string>();
            if (img > 0) parts.Add($"🖼{img}");
            if (aud > 0) parts.Add($"🎵{aud}");
            if (att > 0) parts.Add($"📎{att}");
            return string.Join(" ", parts);
        }
    }

    public bool IsPinned => Full.Note.IsPinned;
    public Visibility PinVisibility => IsPinned ? Visibility.Visible : Visibility.Collapsed;

    public string DateText =>
        DateTimeOffset.FromUnixTimeMilliseconds(Full.Note.UpdatedAt).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
}
