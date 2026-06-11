using System.IO;
using System.Windows.Media;

namespace XNote.App.Audio;

/// <summary>
/// 单实例音频播放（基于 WPF MediaPlayer）。媒体在本机是加密存储，播放前解密成临时明文文件，
/// 停止/切换/结束时删除临时文件。同一时刻只播放一个；再次点击同一文件即停止。
/// </summary>
public sealed class AudioPlaybackService
{
    private readonly MediaPlayer _player = new();
    private string? _currentPath;   // 原始（加密）路径，作为 UI 标识
    private string? _currentTemp;   // 解密后的临时文件

    /// <summary>正在播放的文件路径变化时触发（参数为受影响路径，便于刷新 UI）。</summary>
    public event Action<string?>? PlayingChanged;

    public AudioPlaybackService()
    {
        _player.MediaEnded += (_, _) => StopInternal();
    }

    public string? CurrentPath => _currentPath;

    /// <summary>播放/停止切换。返回切换后是否正在播放该文件。</summary>
    public bool Toggle(string path)
    {
        if (string.Equals(_currentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            StopInternal();
            return false;
        }

        StopInternal();
        var temp = MediaAccess.DecryptToTemp(path);
        _player.Open(new Uri(temp));
        _player.Play();
        _currentPath = path;
        _currentTemp = temp;
        PlayingChanged?.Invoke(path);
        return true;
    }

    public void Stop() => StopInternal();

    private void StopInternal()
    {
        var previous = _currentPath;
        if (previous == null) return;
        _player.Stop();
        _player.Close();
        _currentPath = null;
        TryDeleteTemp();
        PlayingChanged?.Invoke(previous);
    }

    private void TryDeleteTemp()
    {
        if (_currentTemp == null) return;
        try { if (File.Exists(_currentTemp)) File.Delete(_currentTemp); } catch { /* ignore */ }
        _currentTemp = null;
    }
}
