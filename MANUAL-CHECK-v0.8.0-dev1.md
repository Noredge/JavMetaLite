# JavMetaLite v0.8.0-dev1 人工检查

dev1 只增加“启动时载入一个影片”。所有测试请使用影片副本；在本轮检查中不需要点击保存。

## E01 — 无参数正常启动

1. 双击 `JavMetaLite.exe`。
2. 确认主窗口正常出现。

预期：保持“尚未选择影片”，没有错误弹窗，所有安全默认值与 v0.7 一致。

## E02 — 命令行载入影片

在 PowerShell 中运行：

```powershell
& '完整路径\JavMetaLite.exe' '完整路径\SNOS-255 测试副本.mp4'
```

预期：窗口打开后直接显示该影片路径；自动识别番号，并按现有规则读取同名 NFO 与 poster/fanart。不会自动搜索网络资料。

## E03 — Windows“打开方式”

1. 右键一个测试影片，选择“打开方式”→“选择其他应用”。
2. 浏览到本版 `JavMetaLite.exe`；本轮不必勾选“始终使用”。

预期：JavMetaLite 直接载入该影片，结果与 E02、拖入和“选择影片”一致。

## E04 — 不存在路径

```powershell
& '完整路径\JavMetaLite.exe' 'C:\JavMetaLite-test\不存在的影片.mp4'
```

预期：提示影片不存在；关闭提示后主窗口仍可正常选择其他影片，没有新增影片或 metadata 输出。

## E05 — 文件夹与不支持格式

分别传入一个文件夹和一个真实存在的 `.txt` 文件。

预期：分别提示应选择影片文件、格式不受支持；主窗口保持可用。

## E06 — 多个影片参数

```powershell
& '完整路径\JavMetaLite.exe' '完整路径\A.mp4' '完整路径\B.mp4'
```

预期：提示一次只能打开一个影片，不会自行挑选其中一个。

## E07 — 既有入口回归

分别使用“选择影片”和拖入方式打开一个影片。

预期：两者仍正常工作，载入内容与 dev1 启动参数入口一致。

## E08 — 文件安全

比较 E02 测试影片在载入前后的 SHA-256。

预期：SHA-256 一致；没有生成、移动、重命名或覆盖任何影片和 metadata。

## 回报格式

```text
E01 PASS/FAIL
E02 PASS/FAIL
E03 PASS/FAIL
E04 PASS/FAIL
E05 PASS/FAIL
E06 PASS/FAIL
E07 PASS/FAIL
E08 PASS/FAIL
影片 SHA-256 是否一致：YES/NO
```
