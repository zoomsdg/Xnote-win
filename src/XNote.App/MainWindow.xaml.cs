using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using XNote.Core.Models;
using XNote.Core.Storage;

namespace XNote.App;

public partial class MainWindow : Window
{
    private readonly LocalStore _store = new();

    public MainWindow()
    {
        InitializeComponent();
        ReloadCategories();
        Refresh();
    }

    private void ReloadCategories()
    {
        // 过滤下拉：全部 + 各分类
        var items = new List<Category> { new Category { Id = "", Name = "全部分类" } };
        items.AddRange(_store.Categories);
        var prev = CategoryFilter.SelectedValue as string;
        CategoryFilter.ItemsSource = items;
        CategoryFilter.SelectedValue = items.Any(c => c.Id == prev) ? prev : "";
    }

    private string? CurrentCategoryFilter =>
        CategoryFilter.SelectedValue as string is { Length: > 0 } id ? id : null;

    private void Refresh()
    {
        var rows = _store.ListNotes(CurrentCategoryFilter, SearchBox.Text)
                         .Select(f => new NoteRowVM(f, _store.CategoryName(f.Note.CategoryId)))
                         .ToList();
        NoteList.ItemsSource = rows;
        StatusText.Text = $"共 {rows.Count} 条纪事";
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => Refresh();

    private NoteRowVM? Selected => NoteList.SelectedItem as NoteRowVM;

    // ---------- 工具栏 ----------

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var note = _store.CreateNote();
        ReloadCategories();
        Refresh();
        OpenEditor(note.Note.Id);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "XNote 加密备份 (*.zip)|*.zip|所有文件|*.*", Title = "选择要导入的 ZIP" };
        if (dlg.ShowDialog(this) != true) return;

        var pwd = PasswordDialog.AskOnce(this);
        if (pwd == null) return;

        try
        {
            var n = _store.Import(dlg.FileName, pwd);
            ReloadCategories();
            Refresh();
            MessageBox.Show(this, $"成功导入 {n} 条纪事。", "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (XNote.Core.ImportExport.InvalidPasswordException)
        {
            MessageBox.Show(this, "密码错误，无法解密该文件。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportAll_Click(object sender, RoutedEventArgs e)
    {
        var all = _store.ListNotes();
        ExportNotes(all, "XNote_Export");
    }

    private void ExportSelected_Click(object sender, RoutedEventArgs e)
    {
        if (Selected == null) return;
        ExportNotes(new[] { Selected.Full }, "XNote_Note");
    }

    private void ExportNotes(IReadOnlyList<FullNote> notes, string baseName)
    {
        if (notes.Count == 0) { MessageBox.Show(this, "没有可导出的纪事。", "导出"); return; }

        var dlg = new SaveFileDialog
        {
            Filter = "XNote 加密备份 (*.zip)|*.zip",
            FileName = $"{baseName}_{System.DateTime.Now:yyyyMMdd_HHmmss}.zip"
        };
        if (dlg.ShowDialog(this) != true) return;

        var pwd = PasswordDialog.AskNew(this);
        if (pwd == null) return;

        try
        {
            _store.Export(notes, pwd, dlg.FileName);
            MessageBox.Show(this, $"已导出 {notes.Count} 条纪事到：\n{dlg.FileName}", "导出完成",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ManageCategories_Click(object sender, RoutedEventArgs e)
    {
        var win = new CategoryWindow(_store) { Owner = this };
        win.ShowDialog();
        ReloadCategories();
        Refresh();
    }

    // ---------- 列表操作 ----------

    private void NoteList_DoubleClick(object sender, RoutedEventArgs e)
    {
        if (Selected != null) OpenEditor(Selected.Id);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected != null) OpenEditor(Selected.Id);
    }

    private void OpenEditor(string noteId)
    {
        var note = _store.GetNote(noteId);
        if (note == null) return;
        var win = new NoteEditWindow(_store, note) { Owner = this };
        win.ShowDialog();
        ReloadCategories();
        Refresh();
    }

    private void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        if (Selected == null) return;
        _store.SetPinned(Selected.Id, !Selected.IsPinned);
        Refresh();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected == null) return;
        if (MessageBox.Show(this, $"确定删除「{Selected.Title}」？", "删除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _store.DeleteNote(Selected.Id);
        Refresh();
    }
}
