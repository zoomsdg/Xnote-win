using System.Collections.Generic;
using System.Windows;
using XNote.Core.Models;

namespace XNote.App;

public partial class ImportTargetWindow : Window
{
    /// <summary>导入目标选择结果。IsNew=true 时用 NewName 新建标签页，否则用 ExistingId。</summary>
    public sealed class Result
    {
        public bool IsNew { get; init; }
        public string? ExistingId { get; init; }
        public string? NewName { get; init; }
    }

    public Result? Choice { get; private set; }

    private ImportTargetWindow(IReadOnlyList<Notebook> notebooks)
    {
        InitializeComponent();
        ExistingBox.ItemsSource = notebooks;
        if (notebooks.Count > 0) ExistingBox.SelectedIndex = 0;
        Loaded += (_, _) => { NewName.Focus(); };
    }

    /// <summary>返回 null 表示取消。</summary>
    public static Result? Ask(Window owner, IReadOnlyList<Notebook> notebooks)
    {
        var d = new ImportTargetWindow(notebooks) { Owner = owner };
        return d.ShowDialog() == true ? d.Choice : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (NewRadio.IsChecked == true)
        {
            var name = NewName.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "请输入新标签页的名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Choice = new Result { IsNew = true, NewName = name };
        }
        else
        {
            if (ExistingBox.SelectedValue is not string id)
            {
                MessageBox.Show(this, "请选择一个标签页。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Choice = new Result { IsNew = false, ExistingId = id };
        }
        DialogResult = true;
    }
}
