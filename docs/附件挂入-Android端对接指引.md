# 附件挂入 — Android 端对接指引（可直接作为提示词）

> Windows 端（XNote-Win）已实现「纪事挂入任意格式附件」并已进 main。
> 本文是给 Android 端 agent 的任务说明：把同一功能实现出来，并保证两端导出 ZIP 互通。
> 契约以 Windows 端已落地的实现为准，**Android 端要对齐它，不要改契约**。

---

## 一、要做的功能

在一条纪事里可以挂入本机任意格式的文件（csv / txt / pdf / xlsx / zip…）。

**本项目不解析、不打开附件内容**，只负责四件事：

1. 用户从本机选一个文件，挂入当前纪事，成为一个内容块；
2. 纪事里显示「有这么个文档」（图标 + 原始文件名 + 大小），可与图片/音频块一样上移/下移/删除；
3. 「打开」→ 交给系统里已安装的程序去打开；「另存为」→ 保存回本机存储；
4. 附件随导出 ZIP 一起走，且**与图片/音频同等加密**。

目的：相关文档与纪事自成一体、方便携带，同时不把本项目做成文档编辑器——**不要引入任何 CSV/PDF/Office 解析库**。

## 二、跨平台契约（必须逐字对齐，否则两端互导会丢附件）

导出 ZIP 结构不变：`notes_data.json` + `media/<文件>`，整包 zip4j `EncryptionMethod.AES` / `KEY_STRENGTH_256` 密码保护。

附件**没有新增任何 JSON 字段**，只新增了一个 block `type` 取值 `"file"`，复用已有字段：

```jsonc
{
  "type": "file",                    // 新取值；除此之外无新字段
  "order": 2,
  "text": "[附件] 报表.csv",          // 兜底：不认识 file 的老客户端当文本块显示，不至于变空块
  "mediaFileName": "file_9f3a.csv",  // ZIP 内 media/ 下的文件名，前缀固定 file_
  "alt": "报表.csv"                   // 原始文件名（含扩展名），显示/打开/另存为都用它
}
```

Android 端要做的对齐点：

- `BlockType` 枚举增加 `FILE`，Gson 注解 `@SerializedName("file")`；
- **导出**：附件块写入 `media/file_<uuid><原扩展名>`，`mediaFileName` 填该文件名，`alt` 填原始文件名，
  并且**必须同时填 `text = "[附件] " + 原始文件名"`**（这是给老客户端的兜底，不能省）；
- **导入**：`type == "file"` 且 `mediaFileName` 能在 `media/` 里找到 → 还原成附件块；
  找不到文件 → 退化成文本块 `"[附件丢失] " + alt`，不要留空壳块；
- **健壮性**：解析到未知 `type` 一律回落成文本块，绝不抛异常（Gson 对未知枚举值会给 null，注意判空）。

## 三、本地存储要求

- 附件与图片/音频**走同一条加密路径**（Android 端现有的 Keystore/MediaCryptor 媒体加密），不得明文落盘。
- 块实体复用现有字段：`url` = 加密后的本地文件路径，`alt` = 原始文件名；
  另加一个**纯本地字段** `size`（字节数，仅供列表显示）——**只进本地数据库/本地 JSON，绝不能进导出 DTO**。
- 单个附件上限 **50MB**：本地挂入超限直接拒绝并提示；
  **但从 ZIP 导入既有附件时不卡这个上限**——宁可收下也不要丢数据。

## 四、UI 要求（Android 惯用做法）

- 挂入：`Intent.ACTION_OPEN_DOCUMENT`（`type = "*/*"`）选文件 → 读字节 → 加密入库 → 追加附件块。
  用 `DocumentFile` / `ContentResolver.query` 取原始文件名与大小。
- 打开：先把附件**解密到 app 私有 cache 的一个独立子目录、保留原文件名**（避免同名互相覆盖），
  再用 `FileProvider.getUriForFile` + `Intent.ACTION_VIEW`（带 `FLAG_GRANT_READ_URI_PERMISSION`），
  由系统里能打开该类型的程序处理；没有可处理的程序时给出友好提示而不是崩溃。
  这些解密出来的明文缓存要在**下次启动时统一清理**。
- 另存为：`Intent.ACTION_CREATE_DOCUMENT` 让用户选保存位置，把解密后的字节写过去。
- 列表页：有附件的纪事显示 `📎N` 角标（与现有 `🖼N` / `🎵N` 并列）。

## 五、验收标准

1. Android 挂入附件 → 导出 ZIP → **Windows 端导入**：附件可见、文件名一致、字节一致、能打开。
2. Windows 端导出的含附件 ZIP → **Android 导入**：同上。
3. 附件在本机落盘是**加密**的（直接读文件拿不到明文）。
4. 同一 ZIP 重复导入不产生副本（沿用现有去重逻辑；去重键只看标题 + 文本块，附件块不参与）。
5. 老版本 Android 客户端导入含附件的 ZIP 时不崩溃（附件块被当作文本块显示 `[附件] 报表.csv`）。

## 六、参考

Windows 端实现（可对照）：`ExportImportService.cs`（导出/导入的 file 分支）、
`MediaStore.cs`（`DecryptToTempNamed`）、`LocalStore.cs`（`ImportAttachmentFile` / `MaxAttachmentBytes`）、
`docs/附件挂入-功能规格.md`。
