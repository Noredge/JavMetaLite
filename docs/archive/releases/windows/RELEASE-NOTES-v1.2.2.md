# JavMetaLite 1.2.2

## 简体中文

本次维护更新聚焦批量导入、冲突处理与保存异常提示。

- 扫描文件夹支持 Ctrl／Shift 多选，合并显示扫描结果；保留子目录选项与路径去重。
- 新增“选择已识别”，方便只加入识别出番号的影片。修复 MIDE-720 等番号被误当作分辨率过滤的问题；不再自动识别纯数字日期式番号。
- 队列新增“选择待处理”“选择有问题”按钮，勾选对应分类并取消其他勾选；筛选菜单仍只控制显示，菜单中附带分类数量。
- 扫描异常可点击查看完整路径与详情；明确区分权限不足和读取失败，不把系统目录拒绝访问描述为影片损坏。
- 多条影片将写入相同目标时，可选择保存一条、确认顺序后作为 CD 分段保存，或本次跳过。不会根据 a/b 自动合并，也不会覆盖已有影片。
- 冲突列表使用深色背景与清晰选中态。解决冲突后仍需确认保存预览。
- 保存取消或失败后确认临时目录清理结果；无法确认时显示路径与原始错误。已提交但清理未完成时保留成功状态并暂停后续批次，不误报为安全取消。

手动合为分段限同来源目录、同番号、保存设置一致的单文件影片。按列表顺序命名为 CD1、CD2，使用第一条影片的资料和图片；原地保存会改为番号子目录，其他分段独有的本地资料留在原处。

升级前完成队列保存并关闭旧程序。无需迁移现有库；重要媒体仍建议备份。遇到恢复不完整提示时保留现场，不要直接删除临时目录。本次不自动清理历史遗留目录。

## English

This maintenance update improves batch discovery, conflict handling and save diagnostics.

- Select multiple scan folders with Ctrl/Shift and review combined, deduplicated results.
- Select only recognized IDs during discovery. IDs such as MIDE-720 are no longer rejected as resolution markers; numeric date-style IDs are no longer auto-detected.
- Select pending/problem movies with dedicated buttons. These replace the batch selection without changing the display filter; filter entries now show category counts.
- Open full scan diagnostics, including long paths. Permission denial is distinguished from read failures and does not imply damaged media.
- Resolve duplicate batch destinations explicitly: save one movie, confirm ordered CD parts, or skip the group. No automatic a/b merging or existing-video overwrite. A final save preview remains mandatory after resolution.
- Use a readable dark conflict list. Report unconfirmed temporary cleanup and retain original recovery errors after cancellation. A committed save with cleanup warnings remains completed and stops the remaining batch.

Manual parts require single-file movies in the same source folder with matching ID and save settings. Top-to-bottom order becomes CD1, CD2; metadata and artwork come from the first item. In-place mode becomes an ID subfolder, while other parts' separate local metadata stays at the source.

Finish saving the queue and close the old app before upgrading. No library migration is required. Back up important media and retain recovery directories when recovery is incomplete; historical leftovers are not automatically deleted.
