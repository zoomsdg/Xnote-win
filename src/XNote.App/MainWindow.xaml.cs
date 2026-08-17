using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    // 右侧详情抽屉（只读预览）
    private readonly ObservableCollection<EditBlockVM> _detailBlocks = new();
    private readonly Audio.AudioPlaybackService _player = new();
    private string? _detailNoteId;
    private bool _suppressListSelection;

    public MainWindow()
    {
        InitializeComponent();
        MediaAccess.Store = _store.Media; // 注入加密媒体访问点
        DetailBlocks.ItemsSource = _detailBlocks;
        _player.PlayingChanged += OnPlayingChanged;
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
        var keepId = Selected?.Id ?? _detailNoteId;
        var rows = _store.ListNotes(_currentNotebookId, CurrentCategoryFilter, SearchBox.Text)
                         .Select(f => new NoteRowVM(f, _store.CategoryName(f.Note.CategoryId)))
                         .ToList();

        // 换 ItemsSource 会先把选中项清空，抖动出一次 SelectionChanged —— 先屏蔽，再自己恢复选中
        _suppressListSelection = true;
        NoteList.ItemsSource = rows;
        var keep = keepId == null ? null : rows.FirstOrDefault(r => r.Id == keepId);
        NoteList.SelectedItem = keep;
        _suppressListSelection = false;

        // 抽屉开着时同步最新内容；对应纪事已不在当前视图（删除/换 tab/被过滤）则收起
        if (_detailNoteId != null)
        {
            if (keep != null) ShowDetail(keep, force: true);
            else CloseDetail();
        }

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

    /// <summary>键盘上下键换选时也跟随预览。</summary>
    private void NoteList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressListSelection) return;
        if (Selected is { } row) ShowDetail(row);
    }

    /// <summary>
    /// 单击行 = 打开详情抽屉。走点击而非只靠 SelectionChanged：抽屉被 ✕ 关掉后，
    /// 再点同一行不会触发选中变化，仍需要能重新打开。
    /// </summary>
    private void NoteList_ClickUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RowFromClick(e.OriginalSource) is { } row) ShowDetail(row);
    }

    /// <summary>从点击命中的可视元素向上找到所属 ListBoxItem，取其行 VM。</summary>
    private static NoteRowVM? RowFromClick(object source)
    {
        var d = source as System.Windows.DependencyObject;
        while (d != null && d is not System.Windows.Controls.ListBoxItem)
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        return (d as System.Windows.Controls.ListBoxItem)?.DataContext as NoteRowVM;
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

    // ---------- 详情抽屉（只读） ----------

    /// <param name="force">true = 即使是同一条也重建内容（列表刷新后同步最新数据）。</param>
    private void ShowDetail(NoteRowVM row, bool force = false)
    {
        if (!force && _detailNoteId == row.Id && DetailDrawer.Visibility == Visibility.Visible) return;

        _player.Stop(); // 切换纪事时停掉上一条的音频
        _detailNoteId = row.Id;

        DetailTitle.Text = row.Title;
        DetailPin.Visibility = row.PinVisibility;
        DetailCategory.Text = row.CategoryName;
        DetailTime.Text = row.DateText;

        _detailBlocks.Clear();
        foreach (var b in row.Full.Blocks.OrderBy(b => b.Order))
            _detailBlocks.Add(EditBlockVM.From(b));
        DetailEmpty.Visibility = _detailBlocks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        DetailDrawer.Visibility = Visibility.Visible;
        DetailScroll.ScrollToTop();
    }

    private void CloseDetail()
    {
        _player.Stop();
        _detailNoteId = null;
        _detailBlocks.Clear();
        DetailDrawer.Visibility = Visibility.Collapsed;
    }

    private void CloseDetail_Click(object sender, RoutedEventArgs e) => CloseDetail();

    private EditBlockVM? DetailBlockOf(object sender) => (sender as FrameworkElement)?.DataContext as EditBlockVM;

    private void DetailViewImage_Click(object sender, RoutedEventArgs e)
    {
        if (DetailBlockOf(sender) is not ImageEditBlockVM vm) return;
        if (string.IsNullOrEmpty(vm.Path) || !System.IO.File.Exists(vm.Path))
        {
            MessageBox.Show(this, "图片文件缺失。", "查看", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new ImageViewerWindow(vm.Path) { Owner = this }.ShowDialog();
    }

    private void DetailPlayAudio_Click(object sender, RoutedEventArgs e)
    {
        if (DetailBlockOf(sender) is not AudioEditBlockVM vm) return;
        if (string.IsNullOrEmpty(vm.Path) || !System.IO.File.Exists(vm.Path))
        {
            MessageBox.Show(this, "音频文件缺失。", "播放", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _player.Toggle(vm.Path);
    }

    private void OnPlayingChanged(string? path)
    {
        foreach (var vm in _detailBlocks.OfType<AudioEditBlockVM>())
            if (vm.Path == path)
                vm.IsPlaying = _player.CurrentPath == path;
    }

    /// <summary>解密到临时目录（保留原名）后交给系统默认程序打开——本项目自身不解析附件内容。</summary>
    private void DetailOpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (DetailBlockOf(sender) is not FileEditBlockVM vm || !EnsureAttachmentExists(vm)) return;
        try
        {
            var tmp = _store.Media.DecryptToTempNamed(vm.Path, vm.FileName);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tmp) { UseShellExecute = true });
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this, "无法打开该附件（本机可能没有关联的程序）：\n" + ex.Message,
                "打开附件", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DetailSaveFileAs_Click(object sender, RoutedEventArgs e)
    {
        if (DetailBlockOf(sender) is not FileEditBlockVM vm || !EnsureAttachmentExists(vm)) return;

        var ext = System.IO.Path.GetExtension(vm.FileName);
        var dlg = new SaveFileDialog
        {
            Title = "保存附件到磁盘",
            FileName = vm.FileName,
            Filter = string.IsNullOrEmpty(ext) ? "所有文件|*.*" : $"({ext})|*{ext}|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            System.IO.File.WriteAllBytes(dlg.FileName, _store.Media.ReadPlain(vm.Path));
            MessageBox.Show(this, "已保存到：\n" + dlg.FileName, "保存成功",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool EnsureAttachmentExists(FileEditBlockVM vm)
    {
        if (!string.IsNullOrEmpty(vm.Path) && System.IO.File.Exists(vm.Path)) return true;
        MessageBox.Show(this, "附件文件缺失。", "附件", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _player.Stop();
        base.OnClosed(e);
    }
}
