# JavMetaLite 1.2.1

## 简体中文

本次为旧库兼容与整理体验的小补丁。

- 支持读取独立影片目录里的 `movie.nfo`、`poster.jpg`、`fanart.jpg` 等本地资料。多部不同影片混放时，不猜测目录级 NFO 和封套的归属。
- 普通单片原地保存保留已识别的目录级命名；保存到新目录仍使用现有输出规则。原有 CD 分段输出行为不变。
- 正确区分 NFO 中显式的 `cid` 与默认番号；原地替换 PNG 图片保持 PNG 编码。
- 迁移成功后清理本次涉及的空 `extrafanart` 目录；有内容、取消或失败时保留，不删除影片父目录。
- 四语言界面的保存选项、图片查看器及计数统一使用“剧照”及对应译名，磁盘目录名称不变。

无需迁移现有库或重新搜索。两套 NFO 共存时优先影片同名文件，不自动合并或删除另一份。WebP 原地重新编码暂不支持，可以保留原图或保存到新目录。升级前请完成队列保存并关闭旧程序；重要媒体仍建议备份。

## English

A small compatibility and organization update.

- Read folder-level `movie.nfo`, `poster.jpg` and `fanart.jpg` in a dedicated movie folder. Do not assign shared NFO/cover files when different movies occupy the same folder.
- Retain recognized folder-level names for single-movie in-place saves. New directories and multipart output retain their existing naming rules.
- Read explicit NFO `cid` values separately from the default movie number, and preserve PNG encoding when replacing PNG artwork.
- Remove only the empty source `extrafanart` directory involved in a successful move. Keep nonempty directories and movie parent folders; do not clean up after cancellation or failure.
- Use consistent still-image terminology across all four UI languages without renaming folders on disk.

No library migration or new search is required. Filename-based NFOs take priority when both layouts exist; the other NFO is not automatically merged or deleted. WebP re-encoding in place is not supported: retain the original or save to a new folder. Finish saving the queue and close the old app before upgrading. Back up important media.
