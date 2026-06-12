using ICSharpCode.SharpZipLib.Zip;
using XNote.Core.ImportExport;
using XNote.Core.Models;
using XNote.Core.Storage;

// 用法:
//   (无参)                 自做一次导出→导入往返测试，校验格式与字段
//   <zip> <password>       检查一个真实 ZIP（如 Android 导出文件）能否被读取
//   inspect <zip>          只列出 ZIP 条目与加密信息（不解密内容）

if (args.Length >= 1 && args[0] == "inspect")
{
    Inspect(args[1]);
    return 0;
}

if (args.Length >= 2)
{
    return ImportReal(args[0], args[1]);
}

return RoundTrip();

static int RoundTrip()
{
    Console.WriteLine("== XNote 导入/导出往返自测 ==");
    var work = Path.Combine(Path.GetTempPath(), "xnote_verify_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(work);
    var mediaDir = Path.Combine(work, "media_src");
    Directory.CreateDirectory(mediaDir);

    // 造一张假图片（内容无所谓，互通只看字节往返一致）
    var imgPath = Path.Combine(mediaDir, "pic.jpg");
    var imgBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 0xFF, 0xD9 };
    File.WriteAllBytes(imgPath, imgBytes);

    var note = new FullNote
    {
        Note = new Note
        {
            Id = "note-1", Title = "测试纪事", CategoryId = "work", IsPinned = true,
            CreatedAt = 1700000000000, UpdatedAt = 1700000100000, Version = 1
        },
        Blocks =
        {
            new NoteBlock { NoteId = "note-1", Type = BlockType.Text, Order = 0, Text = "你好，世界 🌏" },
            new NoteBlock { NoteId = "note-1", Type = BlockType.Image, Order = 1, Url = imgPath,
                            Alt = "图", Width = 100, Height = 80 },
        }
    };

    var io = new ExportImportService();
    var zipPath = Path.Combine(work, "export.zip");
    const string pwd = "Secret123!";
    io.Export(new[] { note }, new Dictionary<string, string> { ["work"] = "工作" }, pwd, zipPath);
    Console.WriteLine($"已导出: {zipPath} ({new FileInfo(zipPath).Length} bytes)");

    Inspect(zipPath);

    // 错误密码必须失败
    try
    {
        io.Import(zipPath, "wrong", Path.Combine(work, "x"));
        return Fail("错误密码竟然成功了");
    }
    catch (InvalidPasswordException) { Console.WriteLine("[OK] 错误密码被正确拒绝"); }

    // 正确密码往返
    var res = io.Import(zipPath, pwd, Path.Combine(work, "extract"));
    if (res.Notes.Count != 1) return Fail("导入条数不对");
    var imp = res.Notes[0];

    Check("title", imp.Title, "测试纪事");
    Check("categoryId", imp.CategoryId, "work");
    Check("categoryName", imp.CategoryName, "工作");
    Check("isPinned", imp.IsPinned, true);
    Check("createdAt", imp.CreatedAt, 1700000000000);
    Check("updatedAt", imp.UpdatedAt, 1700000100000);
    Check("blockCount", imp.Blocks.Count, 2);
    Check("textBlock", imp.Blocks[0].Text, "你好，世界 🌏");
    Check("imageAlt", imp.Blocks[1].Alt, "图");
    Check("imageW", imp.Blocks[1].Width, 100);

    var roundImg = File.ReadAllBytes(imp.Blocks[1].MediaFilePath!);
    if (!roundImg.SequenceEqual(imgBytes)) return Fail("图片字节往返不一致");
    Console.WriteLine("[OK] 图片字节往返一致");

    // 顺带验证 LocalStore 端到端（独立数据目录）
    var storePaths = new AppPaths(Path.Combine(work, "store"));
    var store = new LocalStore(storePaths);
    var n = store.Import(zipPath, pwd);
    if (n != 1) return Fail("LocalStore 导入条数不对");
    var stored = store.ListNotes().Single();
    Check("store.title", stored.Note.Title, "测试纪事");
    Check("store.category", store.CategoryName(stored.Note.CategoryId), "工作");
    Console.WriteLine("[OK] LocalStore 端到端导入成功");

    // 静态加密：notes.json / categories.json 应被加密落盘
    if (!XNote.Core.Storage.MediaCryptor.IsEncrypted(storePaths.NotesFile)) return Fail("notes.json 未加密");
    if (!XNote.Core.Storage.MediaCryptor.IsEncrypted(storePaths.CategoriesFile)) return Fail("categories.json 未加密");
    Console.WriteLine("[OK] notes.json / categories.json 已加密落盘 (XNW1)");

    // 重新打开同目录：能解密读回（证明 decrypt-on-load）
    var reopened = new LocalStore(storePaths).ListNotes().Single();
    Check("reopen.title", reopened.Note.Title, "测试纪事");
    Console.WriteLine("[OK] 重开数据目录解密读回成功");

    // 静态加密：落盘媒体应被 DPAPI 加密，但解密读回与原图一致
    var storedImg = stored.Blocks.First(b => b.Type == BlockType.Image);
    if (!XNote.Core.Storage.MediaCryptor.IsEncrypted(storedImg.Url!)) return Fail("落盘媒体未加密");
    Console.WriteLine("[OK] 落盘媒体已加密 (XNW1)");
    if (!store.Media.ReadPlain(storedImg.Url!).SequenceEqual(imgBytes)) return Fail("解密读回与原图不一致");
    Console.WriteLine("[OK] 解密读回与原图一致");

    // 从“加密落盘”再导出 → 新库导入，图片字节仍一致（证明导出会解密成明文写入 ZIP）
    var zip2 = Path.Combine(work, "reexport.zip");
    store.ExportAll(pwd, zip2);
    var store2 = new LocalStore(new AppPaths(Path.Combine(work, "store2")));
    store2.Import(zip2, pwd);
    var img2 = store2.ListNotes().Single().Blocks.First(b => b.Type == BlockType.Image);
    if (!store2.Media.ReadPlain(img2.Url!).SequenceEqual(imgBytes)) return Fail("再导出往返图片不一致");
    Console.WriteLine("[OK] 加密落盘 → 再导出 → 再导入 图片字节一致");

    try { Directory.Delete(work, true); } catch { }
    Console.WriteLine("\n== 全部通过 ✅ ==");
    return 0;
}

static int ImportReal(string zip, string pwd)
{
    Console.WriteLine($"== 读取真实 ZIP: {zip} ==");
    Inspect(zip);
    var io = new ExportImportService();
    var res = io.Import(zip, pwd, Path.Combine(Path.GetTempPath(), "xnote_real_" + Guid.NewGuid().ToString("N")));
    Console.WriteLine($"成功解析 {res.Notes.Count} 条纪事:");
    foreach (var n in res.Notes)
        Console.WriteLine($"  - [{n.CategoryName}] {n.Title}  ({n.Blocks.Count} 块, pinned={n.IsPinned})");
    return 0;
}

static void Inspect(string zip)
{
    using var zf = new ZipFile(zip);
    Console.WriteLine($"  ZIP 条目 ({zf.Count}):");
    foreach (ZipEntry e in zf)
    {
        var aes = e.AESKeySize > 0 ? $"AES-{e.AESKeySize}" : (e.IsCrypted ? "ZipCrypto" : "无加密");
        Console.WriteLine($"    {e.Name,-40} {e.Size,8}B  加密={aes}");
    }
}

static void Check<T>(string field, T actual, T expected)
{
    if (!Equals(actual, expected))
    {
        Console.WriteLine($"[FAIL] {field}: 期望 '{expected}' 实得 '{actual}'");
        Environment.Exit(2);
    }
    Console.WriteLine($"[OK] {field} = {actual}");
}

static int Fail(string msg)
{
    Console.WriteLine($"[FAIL] {msg}");
    return 2;
}
