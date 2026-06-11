using NAudio.Wave;

namespace XNote.App.Audio;

/// <summary>
/// 麦克风录音 → WAV 文件（PCM 44.1kHz/16bit/单声道）。
/// WAV 是无损通用格式，Android 端 MediaPlayer 同样可播放，满足跨平台互导。
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _path;
    private long _bytes;
    private int _avgBytesPerSec;

    public bool IsRecording { get; private set; }

    /// <summary>开始录音，写入 <paramref name="path"/>（.wav）。</summary>
    public void Start(string path)
    {
        if (IsRecording) return;
        _path = path;
        _bytes = 0;
        var format = new WaveFormat(44100, 16, 1);
        _avgBytesPerSec = format.AverageBytesPerSecond;

        _waveIn = new WaveInEvent { WaveFormat = format };
        _writer = new WaveFileWriter(path, format);
        _waveIn.DataAvailable += OnData;
        _waveIn.StartRecording();
        IsRecording = true;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        _bytes += e.BytesRecorded;
    }

    /// <summary>停止录音，返回 (文件路径, 时长毫秒)。</summary>
    public (string Path, long DurationMs) Stop()
    {
        if (!IsRecording || _path == null) return ("", 0);
        _waveIn!.StopRecording();
        _waveIn.DataAvailable -= OnData;
        _waveIn.Dispose();
        _waveIn = null;
        _writer?.Dispose();
        _writer = null;
        IsRecording = false;

        var durMs = _avgBytesPerSec > 0 ? _bytes * 1000L / _avgBytesPerSec : 0;
        return (_path, durMs);
    }

    public void Dispose()
    {
        try { if (IsRecording) Stop(); } catch { /* ignore */ }
        _waveIn?.Dispose();
        _writer?.Dispose();
    }
}
