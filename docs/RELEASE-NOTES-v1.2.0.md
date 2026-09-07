# JavMetaLite v1.2.0 — Release notes / 版本说明

Published 2026-09-05. 本页记录 1.2.0 的功能与使用边界。

## 简体中文

### 单片轻量，批量统一审核

- 支持文件、文件夹与混合拖入，递归发现影片，并将 CD1/CD2 分组为一部影片。
- 单片保留简洁工作区；批量支持选择、筛选、移除、统一变更预览和顺序保存，不必逐部打开预览或标记准备完成。
- 每部影片保留独立的资料、来源与审核状态。移除／清空队列不会删除磁盘影片。

### 来源与图片选择更明确

- LibreDMM 作为默认推荐来源；R18.dev 用于英文资料或按需比较图片。自定义多来源仍支持逐字段规则，以及可选的高分辨率整套图片选择。
- 单片／批量均遵守当前来源选择；JAVLibrary 仅保留手动网页查询。R18 限流、部分失败和重试范围更清楚。
- 图片查看器默认“全部图片”并定位到点击项；本地与当前来源在线图片分行，来源和样张选择联动。
- 查看器内支持确认后立即删除本地海报、横版封套及样张。未应用的来源／勾选调整可放弃；已确认的本地删除不会因关闭查看器撤销。

### 流畅性与保存可靠性

- 减少大队列刷新、统计和导入开销，使用有界预览缓存；修复快速方向键导航误切语言。
- 当前影片最多同时下载 3 张在线样张；影片仍逐部提交，首次失败或取消即停止。
- 尺寸明显偏小的完整封套显示黄色“待审核”，在详情区查看原因；仅提醒，不自动换源或禁止保存。
- 无当前影片的在线样张候选时，勾选“替换本地 Extra Fanart”不会清空本地图片。执行替换时，选中图片下载失败会阻止该次替换。
- 普通保存保留全部所选本地样张；50 张上限只用于在线选择。本地读取失败会阻止提交；批量预检阻止不同影片的样张输出互相覆盖。
- 修复查看器应用来源后快速切片可能串图，以及取消来源重试时卡在搜索中的状态。
- 文件提交／恢复对短暂 Windows 共享冲突有限重试；持续失败保留备份与恢复现场。
- 批量保存中刻意延后加载的预览显示“预览未加载”，与真实缺图／加载失败区分。

### 使用与升级提示

- Windows 10/11 x64 便携程序，无需另装 .NET Runtime；内置浏览器需要 WebView2 Runtime。
- 简体中文、繁體中文、English、日本語；统一单片／批量搜索栏、折叠区域、像素滚动与深色确认框。
- 旧 Auto 来源设置迁移为 LibreDMM；旧 JAVLibrary 自动来源迁移为手动模式，自定义规则保留。
- 队列仅在内存中；关闭前先完成需要的保存。重要媒体请另行备份；不要直接清理失败后留下的恢复目录。
- 低分辨率提示不是模糊检测。网站速度、限流与可用性无法保证；本版本不包含 R18 离线数据库。

## English

### A lightweight single-movie editor and a unified batch workflow

- Add files, folders or mixed drops; recursively discover movies and group CD1/CD2 parts.
- Keep single-movie editing simple while batching selection, filtering, removal, change previews and ordered saves. Opening every movie or marking each ready is not required.
- Keep metadata, source candidates and review state separate for each movie. Removing queue entries does not delete movie files.

### Clearer source and artwork choices

- LibreDMM is the recommended default. Use R18.dev for English metadata or optional image comparison. Custom multi-source rules retain per-field choices and optional higher-resolution, same-provider artwork sets.
- Single and batch searches honor the selected source. JAVLibrary is manual web lookup only; R18 throttling, partial results and retry scope are clearer.
- The viewer opens at the clicked image within All images. Local images and the current source's online candidates have separate rows; source and sample choices work together.
- Confirmed local poster, fanart and sample deletions happen immediately in the viewer. Closing discards unapplied source/sample changes, but does not undo confirmed deletions.

### Responsiveness and safer saves

- Reduce large-queue refresh, statistics and import overhead with bounded preview caching. Fix held queue arrows accidentally changing the display language.
- Download up to three online samples within the current movie; movie transactions remain sequential and stop at the first failure or cancellation.
- Flag clearly undersized full covers with a yellow review badge and details in the main panel, without blocking saves or changing sources automatically.
- Preserve local Extra Fanart when no online sample candidates exist for the current movie. A failed selected-image download prevents full replacement.
- Preserve all selected local samples in ordinary saves; the 50-image limit applies only to online selections. Abort on local read failures, and reject overlapping batch outputs before confirmation.
- Prevent stale viewer-source previews from appearing on another movie and restore a usable state after canceling source retry during image comparison.
- Briefly retry Windows sharing conflicts during file commit/recovery; retain backups and recovery files when failures persist.
- Label intentionally deferred batch-save previews “Preview not loaded”, separately from missing or failed artwork.

### Usage and upgrade notes

- Portable Windows 10/11 x64 application; no separate .NET Runtime installation. The embedded browser requires WebView2 Runtime.
- Simplified Chinese, Traditional Chinese, English and Japanese; consistent search toolbars, collapsible sections, pixel scrolling and dark confirmation dialogs.
- Legacy Auto settings migrate to LibreDMM; legacy JAVLibrary automatic mode becomes manual. Custom rules remain available.
- The queue is memory-only. Back up important media, and do not blindly remove recovery folders left after failures.
- Resolution hints are not blur detection. Website availability, rate limits and throughput remain external constraints. No R18 offline database is bundled.

## More

[Screenshots](SCREENSHOTS-v1.2.0.md) · [Changelog](../CHANGELOG.md) · [Testing](../TESTING.md)

Screenshots use synthetic data and identify the build shown. Release packages include SHA-256 checksums.
