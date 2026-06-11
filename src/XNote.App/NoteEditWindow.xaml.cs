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

    public NoteEditWindow(LocalStore store, FullNote note)
    {
        InitializeComponent();
        _store = store;
        _note = note;

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
        BlockType.Audio => new TextEditBlockVM { SourceId = b.Id, Text = "[音频块：当前版本暂不支持播放]" },
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
            }
        }
        _note.Blocks = newBlocks;

        _store.SaveNote(_note);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
