using System.Windows;

namespace XNote.App;

public partial class InputDialog : Window
{
    public string Value { get; private set; } = "";

    private InputDialog(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        Input.Text = initial;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    /// <summary>返回 null 表示取消。</summary>
    public static string? Ask(Window owner, string title, string prompt, string initial = "")
    {
        var d = new InputDialog(title, prompt, initial) { Owner = owner };
        return d.ShowDialog() == true ? d.Value : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Value = Input.Text.Trim();
        DialogResult = true;
    }
}
