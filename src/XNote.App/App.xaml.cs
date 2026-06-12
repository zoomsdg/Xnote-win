using System.Windows;
using System.Windows.Threading;

namespace XNote.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        // 兜底：任何未处理异常都弹窗提示，避免“双击没反应”式的静默崩溃。
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ShowError((e.ExceptionObject as System.Exception)?.Message ?? "未知错误");
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowError(Flatten(e.Exception));
        e.Handled = true;
        Shutdown(1);
    }

    private static void ShowError(string message) =>
        MessageBox.Show("程序发生错误，即将退出：\n\n" + message,
            "XNote 错误", MessageBoxButton.OK, MessageBoxImage.Error);

    private static string Flatten(System.Exception ex)
    {
        var msg = ex.Message;
        while (ex.InnerException != null)
        {
            ex = ex.InnerException;
            msg += "\n→ " + ex.Message;
        }
        return msg;
    }
}
