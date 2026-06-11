using System.Security.Cryptography;
using System.Text;

namespace XNote.Core.Storage;

/// <summary>
/// 媒体文件静态加密。落盘格式： [magic 4B "XNW1"][DPAPI 密文]。
/// 使用 Windows DPAPI（CurrentUser 作用域）——密钥绑定当前 Windows 用户账户，
/// 即便文件被拷走，换个账户/机器也无法解密。对应 Android 端 Keystore 的 MediaCryptor。
///
/// 兼容性：不带 magic 头的文件视为明文直接返回，便于历史明文媒体平滑过渡。
/// 注意：这是“本机静态存储”加密，与跨平台导出 ZIP 无关——导出时一律解密成明文写入 ZIP。
/// </summary>
public static class MediaCryptor
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("XNW1"); // 0x58 4E 57 31

    public static void EncryptToFile(byte[] plain, string dest)
    {
        var cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        using var fs = File.Create(dest);
        fs.Write(Magic);
        fs.Write(cipher);
    }

    public static bool IsEncrypted(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < Magic.Length) return false;
            var head = new byte[Magic.Length];
            using var fs = File.OpenRead(path);
            return fs.Read(head, 0, head.Length) == head.Length && head.SequenceEqual(Magic);
        }
        catch { return false; }
    }

    /// <summary>解密读取；非加密（历史明文）文件原样返回。</summary>
    public static byte[] ReadPlain(string path)
    {
        var all = File.ReadAllBytes(path);
        if (all.Length < Magic.Length || !all.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            return all; // 明文兼容
        var cipher = all[Magic.Length..];
        return ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
    }
}

/// <summary>
/// media 目录的统一访问点：写入一律加密，读取一律解密。
/// 需要文件路径的系统组件（MediaPlayer 等）用 <see cref="DecryptToTemp"/> 拿明文临时文件。
/// </summary>
public sealed class MediaStore
{
    private readonly AppPaths _paths;
    public MediaStore(AppPaths paths) => _paths = paths;

    public string SaveBytes(byte[] plain, string ext, bool isImage)
    {
        var prefix = isImage ? "image" : "audio";
        var dest = Path.Combine(_paths.MediaDir, $"{prefix}_{Guid.NewGuid()}{ext}");
        MediaCryptor.EncryptToFile(plain, dest);
        return dest;
    }

    public string SaveFromFile(string sourcePath, bool isImage)
        => SaveBytes(File.ReadAllBytes(sourcePath), Path.GetExtension(sourcePath), isImage);

    public byte[] ReadPlain(string path) => MediaCryptor.ReadPlain(path);

    /// <summary>解密成明文临时文件，返回路径。调用方用完应删除（或靠启动清理）。</summary>
    public string DecryptToTemp(string path)
    {
        var ext = Path.GetExtension(path);
        var tmp = Path.Combine(_paths.TempDir, "media_" + Guid.NewGuid().ToString("N") + ext);
        File.WriteAllBytes(tmp, ReadPlain(path));
        return tmp;
    }

    /// <summary>启动时清理上次遗留的解密临时文件。</summary>
    public void CleanupTemp()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_paths.TempDir, "media_*"))
                try { File.Delete(f); } catch { /* ignore */ }
        }
        catch { /* ignore */ }
    }
}
