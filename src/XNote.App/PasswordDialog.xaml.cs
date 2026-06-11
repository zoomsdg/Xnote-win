using System.Windows;

namespace XNote.App;

public partial class PasswordDialog : Window
{
    public string Password { get; private set; } = "";

    private readonly bool _confirm;

    private PasswordDialog(string prompt, bool confirm)
    {
        InitializeComponent();
        PromptText.Text = prompt;
        _confirm = confirm;
        Pwd2.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => Pwd.Focus();
    }

    /// <summary>导入用：只要一次密码。返回 null 表示取消。</summary>
    public static string? AskOnce(Window owner, string prompt = "请输入导入密码")
        => Show(owner, prompt, confirm: false);

    /// <summary>导出用：输入并确认密码。返回 null 表示取消。</summary>
    public static string? AskNew(Window owner, string prompt = "设置导出密码（用于加密 ZIP）")
        => Show(owner, prompt, confirm: true);

    private static string? Show(Window owner, string prompt, bool confirm)
    {
        var d = new PasswordDialog(prompt, confirm) { Owner = owner };
        return d.ShowDialog() == true ? d.Password : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var p = Pwd.Password;
        if (string.IsNullOrEmpty(p)) { Err.Text = "密码不能为空"; return; }
        if (_confirm && p != Pwd2.Password) { Err.Text = "两次输入的密码不一致"; return; }
        Password = p;
        DialogResult = true;
    }
}
