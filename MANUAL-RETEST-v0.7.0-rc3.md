# JavMetaLite v0.7.0-rc3 集中复测

RC1 与 RC2 已全部通过。RC3 只改变 NFO 更新预览和保存按钮 tooltip 的条件文字，不改变实际写入、来源选择、图片或文件整理。

## P01 — 标准 NFO

1. 选择一个只含标准 Jellyfin/Kodi 字段的本地 NFO 影片。
2. 修改标题等文字字段并进入保存预览。
3. 如方便，将鼠标停在主窗口“保存”按钮上检查 tooltip。

预期：变更显示“更新 NFO”，后面没有“保留未知 XML”；tooltip 只说明更新受管理字段。

## P02 — 确实存在未知 XML

1. 在另一份测试 NFO 的 `<movie>` 内加入：`<my_custom_field owner="test">必须保留</my_custom_field>`。
2. 重新选择影片，修改一个受管理字段并进入保存预览。

预期：变更显示“更新 NFO（保留未知 XML）”；保存按钮 tooltip 也说明会保留检测到的未知 XML。

## P03 — 保存结果

1. 确认 P02 保存。
2. 打开 NFO，检查修改后的字段与 `my_custom_field`。
3. 检查影片 SHA-256。

预期：受管理字段已更新，自定义元素及 `owner` 属性完整保留，影片 SHA-256 不变。

## 回报格式

```text
P01 PASS/FAIL
P02 PASS/FAIL
P03 PASS/FAIL
影片 SHA-256 是否一致：YES/NO
```
