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
    private string _currentNotebookId = LocalStore.DefaultNotebookId;
    private bool _suppressTabEvent;

    public MainWindow()
    {
        InitializeComponent();
        MediaAccess.Store = _store.Media; // 注入加密媒体访问点
        ReloadTabs();
        ReloadCategories();
        Refresh();
    }

    // ---------- 标签页 (tab) ----------

    private void ReloadTabs()
    {
        _suppressTabEvent = true;
        Tabs.ItemsSource = _store.Notebooks;
        // 保持当前选中；不存在则回落到第一个
        var match = _store.Notebooks.FirstOrDefault(t => t.Id == _currentNotebookId)
                    ?? _store.Notebooks.FirstOrDefault();
        _currentNotebookId = match?.Id ?? LocalStore.DefaultNotebookId;
        Tabs.SelectedItem = match;
        _suppressTabEvent = false;
    }

    private void Tabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressTabEvent) return;
        if (Tabs.SelectedItem is Notebook nb)
        {
            _currentNotebookId = nb.Id;
            Refresh();
        }
    }

    private void NewTab_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask(this, "新建标签页", "标签页名称：");
        if (string.IsNullOrWhiteSpace(name)) return;
        var nb = _store.CreateNotebook(name);
        _currentNotebookId = nb.Id;
        ReloadTabs();
        Refresh();
    }

    private void Tab_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (NotebookFromClick(e.OriginalSource) is { } nb) RenameTab(nb);
    }

    private void Tab_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (NotebookFromClick(e.OriginalSource) is not { } nb) return;
        e.Handled = true;

        var menu = new System.Windows.Controls.ContextMenu();
        var rename = new System.Windows.Controls.MenuItem { Header = "重命名" };
        rename.Click += (_, _) => RenameTab(nb);
        var delete = new System.Windows.Controls.MenuItem { Header = "删除标签页" };
        delete.Click += (_, _) => DeleteTab(nb);
        menu.Items.Add(rename);
        menu.Items.Add(delete);
        menu.IsOpen = true;
    }

    /// <summary>从点击命中的可视元素向上找到所属 TabItem，取其 Notebook。</summary>
    private static Notebook? NotebookFromClick(object source)
    {
        var d = source as System.Windows.DependencyObject;
        while (d != null && d is not System.Windows.Controls.TabItem)
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        return (d as System.Windows.Controls.TabItem)?.DataContext as Notebook;
    }

    private void RenameTab(Notebook nb)
    {
        var name = InputDialog.Ask(this, "重命名标签页", "标签页名称：", nb.Name);
        if (string.IsNullOrWhiteSpace(name) || name == nb.Name) return;
        _store.RenameNotebook(nb.Id, name);
        ReloadTabs();
    }

    private void DeleteTab(Notebook nb)
    {
        if (nb.Id == LocalStore.DefaultNotebookId)
        {
            MessageBox.Show(this, "默认标签页不可删除。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"删除标签页「{nb.Name}」？其下纪事会移到「本地纪事」。", "删除标签页",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _store.DeleteNotebook(nb.Id);
        if (_currentNotebookId == nb.Id) _currentNotebookId = LocalStore.DefaultNotebookId;
        ReloadTabs();
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
        var rows = _store.ListNotes(_currentNotebookId, CurrentCategoryFilter, SearchBox.Text)
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
        var note = _store.CreateNote(notebookId: _currentNotebookId);
        ReloadCategories();
        Refresh();
        OpenEditor(note.Note.Id);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "XNote 加密备份 (*.zip)|*.zip|所有文件|*.*", Title = "选择要导入的 ZIP" };
        if (dlg.ShowDialog(this) != true) return;

        // 选择导入目标标签页：新建 or 现有
        var target = ImportTargetWindow.Ask(this, _store.Notebooks);
        if (target == null) return;

        var pwd = PasswordDialog.AskOnce(this);
        if (pwd == null) return;

        // 密码确认后再建新标签页，避免取消时残留空标签页
        var notebookId = target.IsNew ? _store.CreateNotebook(target.NewName!).Id : target.ExistingId!;

        try
        {
            var sum = _store.Import(dlg.FileName, pwd, notebookId);
            _currentNotebookId = notebookId;
            ReloadTabs();
            ReloadCategories();
            Refresh();
            MessageBox.Show(this,
                $"导入完成：新增 {sum.Added} 条，更新 {sum.Updated} 条，跳过 {sum.Skipped} 条。",
                "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (XNote.Core.ImportExport.InvalidPasswordException)
        {
            CleanupEmptyNewTab(target, notebookId);
            MessageBox.Show(this, "密码错误，无法解密该文件。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (System.Exception ex)
        {
            CleanupEmptyNewTab(target, notebookId);
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>导入失败时，删除刚为本次导入新建、但仍为空的标签页。</summary>
    private void CleanupEmptyNewTab(ImportTargetWindow.Result target, string notebookId)
    {
        if (!target.IsNew) return;
        if (_store.ListNotes(notebookId).Count > 0) return; // 已落入纪事则保留
        _store.DeleteNotebook(notebookId);
        if (_currentNotebookId == notebookId) _currentNotebookId = LocalStore.DefaultNotebookId;
        ReloadTabs();
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
