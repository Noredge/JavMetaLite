# JavMetaLite 1.2.3

## 简体中文

本次小补丁修复“发现影片”中的番号识别问题。

- 带网址式前缀的文件名（如 `mirror.example@ABF-391-U.mp4`）不再因重复去除扩展名而丢失番号，“选择已识别”可正确选中这些影片。
- 保留 U/UC 后缀和 CD 分段识别；无番号的广告等文件仍需手动确认，不会套用父文件夹番号。
- 补充解析和界面选择回归测试。搜索来源、保存行为与现有文件命名规则不变。

关闭旧程序后解压新版便携包运行即可，无需迁移媒体库。

## English

This patch fixes movie ID recognition in the discovery window.

- Filenames with dotted prefixes, such as `mirror.example@ABF-391-U.mp4`, no longer lose their IDs through repeated extension removal. Select recognized now includes these movies.
- U/UC suffixes and CD parts remain supported. Files without IDs, including unrelated advertisements, still require manual confirmation; IDs are not inferred from parent folders.
- Adds parser and discovery-selection regression tests. Search sources, save behavior and output naming are unchanged.

Close the old app, extract the new portable package and run it. No library migration is needed.
