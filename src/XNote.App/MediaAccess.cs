using System.IO;
using XNote.Core.Storage;

namespace XNote.App;

/// <summary>
/// App 层访问加密媒体的薄封装。启动时由 MainWindow 注入 <see cref="LocalStore.Media"/>。
/// 未注入时（理论上不会）回落到直接读文件，保证不崩。
/// </summary>
public static class MediaAccess
{
    public static MediaStore? Store;

    public static byte[] ReadPlain(string path)
        => Store != null ? Store.ReadPlain(path) : File.ReadAllBytes(path);

    public static string DecryptToTemp(string path)
        => Store != null ? Store.DecryptToTemp(path) : path;
}
