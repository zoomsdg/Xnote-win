# XNote for Windows（纪事 · Windows 版）

Android 版「纪事 / XNote」的 Windows 桌面重建版，使用 **.NET 8 + WPF（C#）**。
核心目标：**与 Android 版导出的纪事互相导入**——两端共用同一套加密 ZIP 备份格式。

> 本仓库是独立于 Android 工程的新仓库。Android 端逻辑参考自原 Kotlin 工程
> （`ExportImportUtils.kt` / `NoteRepository.kt` 等），本项目按其产品逻辑重建。

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
- ✅ 编辑器：标题、分类、置顶、文本块与图片块（增删/上下移）
- ✅ 分类管理（默认分类不可删、删除分类纪事转“日常”）

尚未实现（后续迭代）：

- ⏳ 音频录制/播放（导入的音频块会被妥善保存与再导出，但暂不在编辑器内播放）
- ⏳ 富文本图文混排的所见即所得编辑（当前为块列表编辑）
- ⏳ 本地媒体静态加密（Windows 端暂以明文存于 `%AppData%`；不影响跨平台互通）
