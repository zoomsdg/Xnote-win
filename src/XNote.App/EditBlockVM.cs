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

public static class ImageLoader
{
    /// <summary>用 OnLoad 缓存加载，避免锁定源文件。</summary>
    public static ImageSource? Load(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new System.Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>读取图片像素尺寸。</summary>
    public static (int w, int h) Dimensions(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var frame = BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch { return (0, 0); }
    }
}
