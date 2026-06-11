using System.Linq;
using System.Windows;
using XNote.Core.Models;
using XNote.Core.Storage;

namespace XNote.App;

public partial class CategoryWindow : Window
{
    private readonly LocalStore _store;

    public CategoryWindow(LocalStore store)
    {
        InitializeComponent();
        _store = store;
        Reload();
    }

    private void Reload() => List.ItemsSource = _store.Categories.ToList();

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask(this, "新建分类", "分类名称：");
        if (string.IsNullOrWhiteSpace(name)) return;
        _store.CreateCategory(name);
        Reload();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not Category cat) return;
        if (LocalStore.ProtectedCategoryIds.Contains(cat.Id))
        {
            MessageBox.Show(this, "默认分类不可删除。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"删除分类「{cat.Name}」？其下纪事会移到“日常”。", "删除分类",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _store.DeleteCategory(cat.Id);
        Reload();
    }
}
