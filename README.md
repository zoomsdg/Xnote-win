# XNote for Windows（纪事 · Windows 版）

Android 版「纪事 / XNote」的 Windows 桌面重建版，使用 **.NET 8 + WPF（C#）**。
核心目标：**与 Android 版导出的纪事互相导入**——两端共用同一套加密 ZIP 备份格式。

> 本仓库是独立于 Android 工程的新仓库。Android 端逻辑参考自原 Kotlin 工程
> （`ExportImportUtils.kt` / `NoteRepository.kt` 等），本项目按其产品逻辑重建。

> ✅ 已实测：Android 与 Windows 两端可**互相导入**对方导出的纪事（双向验证通过）。

---

## 快速上手：启动与导入

### 一、启动 Windows 版

任选一种：

**方式 A：双击已编译好的程序（最简单）**

```
d:\tools\xnote-win\src\XNote.App\bin\Debug\net8.0-windows\XNote.App.exe
```

直接双击启动。

**方式 B：用命令启动**

```powershell
cd d:\tools\xnote-win
& "C:\Program Files\dotnet\dotnet.exe" run --project src\XNote.App
```

> 想要一个可随意拷贝、不依赖项目目录、连 .NET 都不用装的独立 exe，发布一份即可：
> ```powershell
> & "C:\Program Files\dotnet\dotnet.exe" publish src\XNote.App -c Release -r win-x64 `
>     --self-contained -p:PublishSingleFile=true -o d:\tools\xnote-win\publish
> ```
> 之后运行 `d:\tools\xnote-win\publish\XNote.App.exe`。

### 二、导入安卓导出的纪事

1. **在安卓上导出**：安卓「纪事」里导出，会在手机 `Download` 目录生成 `XNote_Export_*.zip`，记住设置的**导出密码**。
2. **把 zip 拷到电脑**（数据线 / 微信传文件 / 网盘均可）。
3. 启动 Windows 版 → 点顶部 **「导入」** 按钮。
4. 选中那个 `XNote_Export_*.zip`。
5. 输入安卓导出时设置的**密码** → 确定。
6. 提示「成功导入 N 条纪事」后，列表即可看到（图片一并导入）。

密码错误会提示「密码错误，无法解密」，换对密码重试即可。

> 反向同理：Windows 里「导出全部 / 导出选中」生成的加密 ZIP，拷到安卓后用安卓「导入」即可读入。

### 三、（可选）导入前先用命令行验证 zip

不打开界面，先确认安卓的文件能被正确解析：

```powershell
cd d:\tools\xnote-win
& "C:\Program Files\dotnet\dotnet.exe" run --project tools\XNote.Verify -- "C:\放zip的路径\XNote_Export_xxx.zip" 你的密码
```

会列出 zip 内条目与解析出的纪事条数/标题。这一步能读出来，界面里的「导入」就一定能成功。

---

## 跨平台备份格式（互通契约）

导出文件是一个 **WinZip AES-256 加密 ZIP**（密码保护），内部结构与 Android 端 zip4j 产物完全一致：

```
XNote_Export_*.zip   (AES-256, EncryptionMethod.AES / KEY_STRENGTH_256)
├── notes_data.json          # UTF-8 JSON，ExportNote 数组
└── media/
    ├── image_<uuid>.jpg      # 明文图片
    └── audio_<uuid>.m4a      # 明文音频
```

`notes_data.json` 每个元素：

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 原始记事 id（导入时会重新分配本地 id） |
| `title` | string | 标题 |
| `categoryId` | string? | 分类 id（老备份可能缺失 → 回落 `daily`） |
| `categoryName` | string? | 分类显示名（跨设备按名字回落匹配/自建） |
| `isPinned` | bool? | 是否置顶（老备份缺失 → `false`） |
| `createdAt` / `updatedAt` | long | Unix 毫秒时间戳（= Android `System.currentTimeMillis()`） |
| `version` | int | 记事版本 |
| `blocks` | array | 内容块，见下 |

内容块 `blocks[]`：

| 字段 | 类型 | 适用 | 说明 |
|------|------|------|------|
| `type` | string | 全部 | `"text"` / `"image"` / `"audio"` |
| `order` | int | 全部 | 块顺序 |
| `text` | string? | text | 文本内容 |
| `mediaFileName` | string? | image/audio | 对应 `media/` 下的文件名 |
| `alt` | string? | image | 替代文本 |
| `width` / `height` | int? | image | 像素尺寸 |
| `duration` | long? | audio | 时长（毫秒） |

**互通要点**

- JSON 字段名、大小写、可空性与 Android Gson 输出逐字段对齐；写出时省略 `null` 字段（同 Gson 默认）。
- 时间一律 Unix 毫秒，避免时区/格式歧义。
- 媒体在 ZIP 内是**明文**——这是两端唯一需要一致的存储形态，因此互通不受各端“本地静态加密”差异影响。
- 导入分类解析顺序（与 Android 一致）：按 id 命中 → 按名字命中 → 按名字自建 → 回落 `daily`。
- 加密用标准 WinZip AES（PBKDF2-HMAC-SHA1 + AES-CTR + HMAC 校验），Android `zip4j` 与 Windows `SharpZipLib` 同一规范，互读互写。

---

## 工程结构

```
XNote.sln
├── src/XNote.Core      # 纯逻辑库：数据模型、导入导出、AES-ZIP、本地存储
│   ├── Models/         # Note / NoteBlock / Category / BlockType
│   ├── ImportExport/   # ExportModels（JSON 契约）+ ExportImportService（AES-ZIP）
│   └── Storage/        # AppPaths + LocalStore（JSON 文件存储 + 分类解析）
├── src/XNote.App       # WPF 界面：列表 / 搜索 / 分类筛选 / 编辑器 / 导入导出对话框
└── tools/XNote.Verify  # 控制台：往返自测 + 读取真实 Android 导出文件
```

本地数据位于 `%AppData%\XNote\`（`data\*.json` + `media\`）。

---

## 构建与运行

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download)。

```powershell
dotnet build XNote.sln -c Release
dotnet run --project src/XNote.App        # 启动桌面应用
```

### 验证跨平台互通

```powershell
# 1) 自做一次导出→导入往返，校验字段与加密格式
dotnet run --project tools/XNote.Verify

# 2) 用真实的 Android 导出文件验证（最终互通确认）
dotnet run --project tools/XNote.Verify -- "C:\path\XNote_Export_xxx.zip" 你的密码
```

> 互通的最终确认应做双向：把 Windows 导出的 ZIP 拿到 Android 导入，
> 把 Android 导出的 ZIP 用上面命令（或本应用「导入」）在 Windows 读取。

---

## 当前阶段（内核 + 基础 UI）

已实现：

- ✅ 与 Android 互通的加密 ZIP 导入/导出（含字段、媒体字节往返自测）
- ✅ 纪事列表、搜索（标题/正文）、分类筛选、置顶
- ✅ 编辑器：标题、分类、置顶、文本 / 图片 / 音频块（增删/上下移）
- ✅ 音频录制（麦克风 → WAV）与播放（编辑器内 ▶ 播放）
- ✅ 本地媒体静态加密：媒体以 Windows DPAPI（CurrentUser）加密落盘（`XNW1` 头），
  显示/播放/导出时按需解密；导出 ZIP 内仍是明文，不影响跨平台互通
- ✅ 分类管理（默认分类不可删、删除分类纪事转“日常”）

尚未实现（后续迭代）：

- ⏳ 富文本图文混排的所见即所得编辑（当前为块列表编辑）

> 音频说明：Windows 端录音保存为 `.wav`（无损通用，Android `MediaPlayer` 可直接播放）；
> 从 Android 导入的 `.m4a` 等音频同样能在本应用播放（走系统编解码器）。
