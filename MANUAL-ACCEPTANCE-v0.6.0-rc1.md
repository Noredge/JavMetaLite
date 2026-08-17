# JavMetaLite v0.6.0-rc1 最终集成验收

这份清单验证 v0.6 的完整真实闭环：Jellyfin 已识别的本地 metadata 经 JavMetaLite 重新载入、混合在线候选、保存、重新扫描，再次被两边正确读取。

dev4 的 B01–B07 已验证取消零写入、无变化不重写、未知 XML、直接保存、sidecar 迁移、外部修改冲突和影片哈希；本清单不机械重复全部用例。

## 验收原则与准备

- 只使用影片、NFO 和图片的完整测试副本，不选择唯一原件。
- 建议建立一个独立 Jellyfin 测试库，避免影响正式媒体库。
- 如果 Jellyfin 配置了自动写回 NFO，请在测试期间避免它与 JavMetaLite 同时修改该 NFO，以免混淆外部修改保护结果。
- 选择一个 LibreDMM 或 R18.dev 可以正常找到的番号。

准备目录：

```text
JavMetaLite-v0.6-RC1-Test/
  番号/
    番号.mp4
    番号.nfo
    番号-poster.jpg
    番号-fanart.jpg
```

要求该目录已经被 Jellyfin 正确识别为一部影片。建议在 NFO 的 `<movie>` 内增加一个测试用未知元素：

```xml
<my_custom_field owner="rc1">必须保留</my_custom_field>
```

记录影片、NFO、poster 和 fanart 的初始 SHA-256，并保存一份原始 NFO 副本：

```powershell
Get-FileHash "完整文件路径" -Algorithm SHA256
```

## R01 — RC1 启动与冻结状态

1. 启动 `publish-v0.6.0-rc1\JavMetaLite.exe`。
2. 确认底部显示 `v0.6.0-rc1 · 发布候选版`。
3. 确认默认仍为“多来源搜索（推荐）”，NFO、高清海报和横版 fanart 默认开启。
4. 确认直接保存、整理文件夹和影片重命名默认关闭。

通过标准：RC1 正常启动，版本正确，安全默认值没有改变。

## R02 — 载入 Jellyfin 已识别的本地资料

1. 在 Jellyfin 中记录测试影片当前的标题、简介、演员、poster 和背景图。
2. 在 JavMetaLite 中选择同一目录里的影片。
3. 确认文字字段显示本地 NFO 值和 `本地 NFO` 来源。
4. 确认 poster/fanart 分别显示现有图片，统一来源为 `本地图片`。
5. 确认“保存”可用，没有只读或图片下载失败提示。

通过标准：Jellyfin 已使用的本地 NFO 与两张图片可以完整进入编辑器，不发生自动写入。

## R03 — 混合在线候选并先取消一次

1. 点击“搜索资料”，取得至少一个可用在线来源。
2. 只把两三个文字字段切换到在线候选，例如标题、简介或演员；其余字段保留本地值。
3. 把“封套 / Fanart”切换到一个在线来源或手动完整封套。
4. 点击“保存”，确认预览中 NFO 为“更新”，poster/fanart 为“替换图片”。
5. 取消预览，重新检查四个文件的 SHA-256。

通过标准：本地与在线值可以混合；取消后 NFO、两张图片和影片哈希全部不变。

## R04 — 确认保存并检查磁盘结果

1. 再次保存并确认同一份变更预览。
2. 确认目录中仍只有一套同名影片、NFO、poster 和 fanart，没有 `.tmp` 或备份残留。
3. 打开 NFO，确认最终选择的字段已写入，`my_custom_field` 及其属性仍存在。
4. 确认 NFO 中的 poster/fanart 文件名与磁盘一致，两张新图片均能正常打开。
5. 重新计算影片 SHA-256。

通过标准：文字与图片按最终选择更新，未知 XML 保留，影片 SHA-256 与初始值一致。

## R05 — Jellyfin 重新扫描与显示

1. 在 Jellyfin 中扫描测试库或刷新该影片的 metadata。
2. 打开影片详情页，检查标题、原始标题、简介、演员、类型、poster 和背景 fanart。
3. 确认没有产生重复影片条目。

通过标准：Jellyfin 显示 R04 保存后的最终字段与图片，且仍只识别一部影片。

## R06 — 再次载入形成闭环

1. 回到 JavMetaLite，重新选择 R04 保存后的影片。
2. 确认刚保存的字段现在作为 `本地 NFO` 正确载入，新 poster/fanart 作为 `本地图片` 正确显示。
3. 不修改任何内容，点击“保存”。
4. 确认预览中的 NFO、poster 和 fanart 全部为“保持不变”，然后确认执行。
5. 再次检查 NFO、poster、fanart 和影片 SHA-256。

通过标准：第二次载入结果与 Jellyfin 一致；无变化保存不重写任何 sidecar；所有哈希保持 R04 完成后的值。

## 已由 dev4 与自动化持续覆盖

- 无效/危险 NFO 的写入阻止。
- 外部修改冲突、目标影片冲突和晚到 metadata 冲突。
- 文件锁导致的逐字节恢复和影片移动失败回滚。
- 整理到番号文件夹与影片重命名时的已知 sidecar 迁移。
- LibreDMM/R18.dev 单方失败、双方失败和番号不匹配。
- JAVLibrary 内置浏览器辅助导入。

## 结果反馈

```text
R01 PASS / FAIL：
R02 PASS / FAIL：
R03 PASS / FAIL：
R04 PASS / FAIL：
R05 PASS / FAIL：
R06 PASS / FAIL：
影片 SHA-256 是否一致：YES / NO
未知 XML 是否保留：YES / NO
补充观察：
```

如果失败，请附对应步骤截图、状态栏文字，以及只包含该测试番号的相邻日志行；不要发送包含其他影片路径的整份日志。
