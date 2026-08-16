# JavMetaLite v0.4.0-rc2 定向复测

RC1 已完成全部十项人工验收，其中九项通过。RC2 只修复 M03 的 START-237 来源回退、日文原标题、Gallery 和图片预览问题；已经通过的 M01、M02、M04–M10 无需完整重跑。

## 准备

- 使用 START-237 的普通影片副本，不要使用唯一原件。
- 记录测试副本当前的 SHA-256。
- 如果原 RC1 测试目录仍保留，请新建独立的 `M03-RC2` 目录，避免已有 metadata 影响结果。
- 双击 `publish-v0.4.0-rc2\JavMetaLite.exe`，确认底部显示 `v0.4.0-rc2 · START-237 修复复测版`。

## R1 — R18.dev 资料与预览

1. 选择 START-237 影片副本。
2. 来源选择 `R18.dev`，点击“搜索资料”。
3. 确认搜索期间不再出现“图片预览下载失败”。
4. 确认：
   - 标题为英文。
   - 原始标题为日文。
   - 演员、导演、片商和类型合理。
   - poster 与完整封套 fanart 均显示。
5. 勾选“保存全部剧照”。
6. 开启“整理到番号文件夹”和“影片重命名为番号”。
7. 保持“直接保存并覆盖（跳过预览）”关闭，保存并确认预览。
8. 确认输出结构：

```text
原影片目录/
  START-237/
    START-237.ext
    START-237.nfo
    START-237-poster.jpg
    START-237-fanart.jpg
    extrafanart/
      fanart1.jpg ... fanart20.jpg
```

9. 打开 NFO，确认 `title` 为英文、`originaltitle` 为日文。
10. 重新计算移动后影片的 SHA-256，必须与测试前完全一致。

预期结果：无预览失败弹窗；保存 20 张可打开的 Gallery；英文标题与日文原标题分工正确；影片哈希不变。

## R2 — 切换 LibreDMM

使用另一份未整理的 START-237 影片副本：

1. 先用 `R18.dev` 搜索成功。
2. 切换到 `LibreDMM` 并再次点击“搜索资料”。
3. 确认不出现“图片预览下载失败”。
4. 确认标题与原始标题为日文，poster、完整封套 fanart 均能显示。
5. 勾选“保存全部剧照”并原位保存。
6. 确认 `extrafanart` 中有 20 张可打开的图片。

说明：LibreDMM 当前的 START-237 JSON 没有 Description，因此该片简介为空可以接受，不判为失败。

## R3 — 日志证据

1. 点击“打开日志”。
2. 搜索 `START-237`。
3. 确认日志包含来源、实际 `contentId=1start237` 和 `screenshots=20`。
4. 如果仍有预览失败，复制与 START-237 相邻的 `图片预览下载候选失败` 行及异常类型。

## 结果反馈

```text
R1 PASS / FAIL：
R2 PASS / FAIL：
R3 PASS / FAIL：
影片 SHA-256 是否一致：YES / NO
```

如任一项失败，请保留测试目录并附上弹窗、输出目录和相关日志截图。
