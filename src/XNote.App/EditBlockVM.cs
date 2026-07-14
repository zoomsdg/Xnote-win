using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XNote.Core.Models;

namespace XNote.App;

public abstract class EditBlockVM : INotifyPropertyChanged
{
    /// <summary>保留原块 id（新块为 null）。</summary>
    public string? SourceId { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TextEditBlockVM : EditBlockVM
{
    private string _text = "";
    public string Text
    {
        get => _text;
        set { _text = value; Raise(nameof(Text)); }
    }
}

public sealed class ImageEditBlockVM : EditBlockVM
{
    /// <summary>本地媒体文件绝对路径（已在 media 目录内）。</summary>
    public string Path { get; init; } = "";
    public string? Alt { get; set; }
    public int? Width { get; init; }
    public int? Height { get; init; }

    private ImageSource? _image;
    public ImageSource? Image => _image ??= ImageLoader.Load(Path);
}

public sealed class AudioEditBlockVM : EditBlockVM
{
    /// <summary>本地媒体文件绝对路径（已在 media 目录内）。</summary>
    public string Path { get; init; } = "";
    public long? Duration { get; init; }

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set { _isPlaying = value; Raise(nameof(IsPlaying)); Raise(nameof(PlayLabel)); }
    }

    public string PlayLabel => (_isPlaying ? "⏹ 停止 " : "▶ 播放 ") + DisplayDuration;

    public string DisplayDuration
    {
        get
        {
            if (Duration is not > 0) return "";
            var t = System.TimeSpan.FromMilliseconds(Duration.Value);
            return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
        }
    }
}

/// <summary>
/// 附件块：本项目不解析文件内容，只显示「有这么个文档」，交给系统默认程序打开 / 另存为。
/// </summary>
public sealed class FileEditBlockVM : EditBlockVM
{
    /// <summary>本地媒体文件绝对路径（已加密，在 media 目录内）。</summary>
    public string Path { get; init; } = "";

    /// <summary>原始文件名（含扩展名），用于显示、打开与另存为。</summary>
    public string FileName { get; init; } = "";

    /// <summary>原始字节数；未知时为 null。</summary>
    public long? Size { get; init; }

    public string Icon => System.IO.Path.GetExtension(FileName).ToLowerInvariant() switch
    {
        ".csv" or ".xls" or ".xlsx" => "📊",
        ".txt" or ".md" or ".log" => "📄",
        ".pdf" => "📕",
        ".doc" or ".docx" => "📘",
        ".zip" or ".rar" or ".7z" => "🗜",
        _ => "📎"
    };

    public string DisplaySize => Size switch
    {
        null or <= 0 => "",
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024.0:0.#} KB",
        _ => $"{Size / 1024.0 / 1024.0:0.#} MB"
    };
}

public static class ImageLoader
{
    /// <summary>
    /// 解密后从内存流加载（OnLoad 缓存，不锁定文件）。
    /// 用 BitmapFrame.Create 而非 BitmapImage：后者配合 StreamSource + IgnoreImageCache
    /// 会抛 ArgumentNullException('key')，导致图片不显示。
    /// </summary>
    public static ImageSource? Load(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            using var ms = new MemoryStream(MediaAccess.ReadPlain(path));
            var frame = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (frame.CanFreeze) frame.Freeze();
            return frame;
        }
        catch { return null; }
    }

    /// <summary>读取图片像素尺寸（从解密后的字节）。</summary>
    public static (int w, int h) Dimensions(string path)
    {
        try
        {
            using var ms = new MemoryStream(MediaAccess.ReadPlain(path));
            var frame = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch { return (0, 0); }
    }
}
