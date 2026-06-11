using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using XNote.Core.Models;
using XNote.Core.Storage;

namespace XNote.App;

public partial class NoteEditWindow : Window
{
    private readonly LocalStore _store;
    private readonly FullNote _note;
    private readonly ObservableCollection<EditBlockVM> _blocks = new();
    private readonly Audio.AudioPlaybackService _player = new();
    private readonly Audio.AudioRecorder _recorder = new();

    public NoteEditWindow(LocalStore store, FullNote note)
    {
        InitializeComponent();
        _store = store;
        _note = note;

        _player.PlayingChanged += OnPlayingChanged;

        TitleBox.Text = note.Note.Title;
        PinCheck.IsChecked = note.Note.IsPinned;
        TimeText.Text = $"创建于 {Fmt(note.Note.CreatedAt)} · 修改于 {Fmt(note.Note.UpdatedAt)}";

        ReloadCategories();
        CategoryCombo.SelectedValue = note.Note.CategoryId;

        foreach (var b in note.Blocks.OrderBy(b => b.Order))
            _blocks.Add(ToVm(b));
        if (_blocks.Count == 0)
            _blocks.Add(new TextEditBlockVM());

        BlockList.ItemsSource = _blocks;
    }

    private static string Fmt(long ms) =>
        System.DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    private static EditBlockVM ToVm(NoteBlock b) => b.Type switch
    {
        BlockType.Image => new ImageEditBlockVM
        {
            SourceId = b.Id, Path = b.Url ?? "", Alt = b.Alt, Width = b.Width, Height = b.Height
        },
        BlockType.Audio => new AudioEditBlockVM { SourceId = b.Id, Path = b.Url ?? "", Duration = b.Duration },
        _ => new TextEditBlockVM { SourceId = b.Id, Text = b.Text ?? "" }
    };

    private void ReloadCategories()
    {
        var prev = CategoryCombo.SelectedValue as string;
        CategoryCombo.ItemsSource = _store.Categories.ToList();
        if (prev != null) CategoryCombo.SelectedValue = prev;
    }

    // ---------- 分类 ----------

    private void NewCategory_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask(this, "新建分类", "分类名称：");
        if (string.IsNullOrWhiteSpace(name)) return;
        var (id, _) = _store.CreateCategory(name);
        ReloadCategories();
        CategoryCombo.SelectedValue = id;
    }

    // ---------- 块操作 ----------

    private EditBlockVM? BlockOf(object sender) => (sender as FrameworkElement)?.DataContext as EditBlockVM;

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var vm = BlockOf(sender); if (vm == null) return;
        var i = _blocks.IndexOf(vm);
        if (i > 0) _blocks.Move(i, i - 1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        var vm = BlockOf(sender); if (vm == null) return;
        var i = _blocks.IndexOf(vm);
        if (i >= 0 && i < _blocks.Count - 1) _blocks.Move(i, i + 1);
    }

    private void DeleteBlock_Click(object sender, RoutedEventArgs e)
    {
        var vm = BlockOf(sender); if (vm == null) return;
        _blocks.Remove(vm);
    }

    private void ViewImage_Click(object sender, RoutedEventArgs e)
    {
        if (BlockOf(sender) is not ImageEditBlockVM vm) return;
        if (string.IsNullOrEmpty(vm.Path) || !System.IO.File.Exists(vm.Path))
        {
            MessageBox.Show(this, "图片文件缺失。", "查看", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new ImageViewerWindow(vm.Path) { Owner = this }.ShowDialog();
    }

    private void AddText_Click(object sender, RoutedEventArgs e) => _blocks.Add(new TextEditBlockVM());

    private void AddImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择图片",
            Filter = "图片 (*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        var stored = _store.ImportMediaFile(dlg.FileName, isImage: true);
        var (w, h) = ImageLoader.Dimensions(stored);
        _blocks.Add(new ImageEditBlockVM { Path = stored, Width = w > 0 ? w : null, Height = h > 0 ? h : null });
    }

    // ---------- 保存 ----------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _note.Note.Title = TitleBox.Text.Trim();
        _note.Note.IsPinned = PinCheck.IsChecked == true;
        if (CategoryCombo.SelectedValue is string cid && cid.Length > 0)
            _note.Note.CategoryId = cid;

        var newBlocks = new System.Collections.Generic.List<NoteBlock>();
        var order = 0;
        foreach (var vm in _blocks)
        {
            switch (vm)
            {
                case TextEditBlockVM t:
                    newBlocks.Add(new NoteBlock
                    {
                        Id = vm.SourceId ?? System.Guid.NewGuid().ToString(),
                        NoteId = _note.Note.Id, Type = BlockType.Text, Order = order++, Text = t.Text
                    });
                    break;
                case ImageEditBlockVM im:
                    newBlocks.Add(new NoteBlock
                    {
                        Id = vm.SourceId ?? System.Guid.NewGuid().ToString(),
                        NoteId = _note.Note.Id, Type = BlockType.Image, Order = order++,
                        Url = im.Path, Alt = im.Alt, Width = im.Width, Height = im.Height
                    });
                    break;
                case AudioEditBlockVM au:
                    newBlocks.Add(new NoteBlock
                    {
                        Id = vm.SourceId ?? System.Guid.NewGuid().ToString(),
                        NoteId = _note.Note.Id, Type = BlockType.Audio, Order = order++,
                        Url = au.Path, Duration = au.Duration
                    });
                    break;
            }
        }
        _note.Blocks = newBlocks;

        _store.SaveNote(_note);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ---------- 音频 ----------

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        if (!_recorder.IsRecording)
        {
            try
            {
                // 先录到 temp 明文，停止时再加密入库
                var path = _store.ReserveTempPath(".wav");
                _recorder.Start(path);
                RecordBtn.Content = "⏹ 停止录音";
                RecordStatus.Text = "● 录音中…";
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, "无法开始录音（请检查麦克风）：\n" + ex.Message,
                    "录音失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        else
        {
            var (path, dur) = _recorder.Stop();
            RecordBtn.Content = "● 录音";
            RecordStatus.Text = "";
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                // 加密入库，删除明文临时录音
                var encPath = _store.ImportMediaFile(path, isImage: false);
                try { System.IO.File.Delete(path); } catch { /* ignore */ }
                _blocks.Add(new AudioEditBlockVM { Path = encPath, Duration = dur });
            }
        }
    }

    private void PlayAudio_Click(object sender, RoutedEventArgs e)
    {
        if (BlockOf(sender) is not AudioEditBlockVM vm) return;
        if (string.IsNullOrEmpty(vm.Path) || !System.IO.File.Exists(vm.Path))
        {
            MessageBox.Show(this, "音频文件缺失。", "播放", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _player.Toggle(vm.Path);
    }

    private void OnPlayingChanged(string? path)
    {
        foreach (var vm in _blocks.OfType<AudioEditBlockVM>())
            if (vm.Path == path)
                vm.IsPlaying = _player.CurrentPath == path;
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _player.Stop();
        _recorder.Dispose();
        base.OnClosed(e);
    }
}
