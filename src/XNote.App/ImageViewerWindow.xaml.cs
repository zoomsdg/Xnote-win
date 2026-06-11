using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace XNote.App;

public partial class ImageViewerWindow : Window
{
    private readonly string _path;
    private double _imgW, _imgH;
    private double _scale = 1.0;

    // 拖拽平移
    private bool _panning;
    private Point _panStart;
    private double _hOff, _vOff;

    public ImageViewerWindow(string mediaPath)
    {
        InitializeComponent();
        _path = mediaPath;

        if (ImageLoader.Load(mediaPath) is BitmapSource src)
        {
            Img.Source = src;
            _imgW = src.PixelWidth;
            _imgH = src.PixelHeight;
        }

        Loaded += (_, _) => Fit();
    }

    private void SetScale(double s)
    {
        _scale = System.Math.Clamp(s, 0.05, 16.0);
        Scale.ScaleX = Scale.ScaleY = _scale;
        Info.Text = _imgW > 0 ? $"{_imgW:0}×{_imgH:0} px · {_scale:P0}" : "";
    }

    private void Fit()
    {
        if (_imgW <= 0 || _imgH <= 0) { SetScale(1); return; }
        var vw = Scroll.ViewportWidth;
        var vh = Scroll.ViewportHeight;
        if (vw <= 0 || vh <= 0) { SetScale(1); return; }
        // 适应窗口：小图放大、大图缩小都允许，留 2% 余量
        SetScale(System.Math.Min(vw / _imgW, vh / _imgH) * 0.98);
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => Fit();
    private void Actual_Click(object sender, RoutedEventArgs e) => SetScale(1.0);
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetScale(_scale * 1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetScale(_scale * 0.8);

    private void Scroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetScale(e.Delta > 0 ? _scale * 1.15 : _scale / 1.15);
        e.Handled = true;
    }

    // ----- 拖拽平移 -----
    private void Scroll_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _panning = true;
        _panStart = e.GetPosition(Scroll);
        _hOff = Scroll.HorizontalOffset;
        _vOff = Scroll.VerticalOffset;
        Scroll.CaptureMouse();
    }

    private void Scroll_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var p = e.GetPosition(Scroll);
        Scroll.ScrollToHorizontalOffset(_hOff - (p.X - _panStart.X));
        Scroll.ScrollToVerticalOffset(_vOff - (p.Y - _panStart.Y));
    }

    private void Scroll_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _panning = false;
        Scroll.ReleaseMouseCapture();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var ext = Path.GetExtension(_path);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        var dlg = new SaveFileDialog
        {
            Title = "保存图片到磁盘",
            FileName = $"XNote_图片_{System.DateTime.Now:yyyyMMdd_HHmmss}{ext}",
            Filter = $"图片 (*{ext})|*{ext}|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, MediaAccess.ReadPlain(_path));
            MessageBox.Show(this, "已保存到：\n" + dlg.FileName, "保存成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
