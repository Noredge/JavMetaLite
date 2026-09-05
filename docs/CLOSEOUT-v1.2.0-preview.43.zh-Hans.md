# JavMetaLite preview.43 内部收尾记录

日期：2026-09-04

状态：实现、自动化与便携包校验完成，等待用户审核。**仅供内部使用，暂不对外发布。** 正式外部发布前，必须完成单独的代码与性能优化，并再次取得用户授权。

## 基线与范围

- 接手基线：`preview42-artwork-comparison` 分支，`33de64e Scope viewer candidates by image source`。
- 当前内部版本：`1.2.0-preview.43`；仍在原分支，改动未提交，没有推送、标签或 Release。
- 保留 WPF + Core、MovieJob 隔离、逐字段审核与文件事务设计。此次不重构架构、不升级依赖、不提高搜索并发、不开展大队列优化。
- 原有 preview.42 发布草稿和两张截图保留并标注为历史材料，不冒充 preview.43 截图；四语言 README 的稳定版下载链接不改变。

## 已收尾的问题

1. **替换图片失败时保留原文件。** 全面替换 Extra Fanart 时，选中的新图只要有下载失败，就中止该影片的保存事务，包括“部分成功”的情况。回归检查覆盖单片和多 CD 的影片、已有图片及旁车字节；旧的未搜索/没有在线样张保护继续生效。用户明确取消全部勾选仍与下载失败分开处理，保持既有预览和确认规则。
2. **关闭窗口等待正在执行的操作。** 运行期间关闭主窗口会请求取消，并等待任务恢复/清理完成；反复关闭不能绕过等待。不响应取消、但已进入收尾的操作也必须完成后才能关闭。异常以及批量保存返回的失败结果保留窗口。没有新增“取消任务”确认弹窗，主窗口 Esc 仍不取消任务。
3. **失败来源重试不重置审核选择。** 保留已选字段值与来源、手动留空、演员图片、封套来源和样张勾选；保留重试结果未携带的手动网页候选。新结果仍补充候选并填入此前未选择的空字段。编辑数据与 provider 原始结果不再共用同一个可变对象。
4. **程序与包版本一致。** 项目 Version 驱动程序集、界面、日志与默认包名。打包前拒绝不匹配的版本，发布产物生成后检查 EXE ProductVersion/FileVersion。新增每版本校验文件，汇总校验文件保留此前条目。

## 验证记录

先新增回归用例，确认旧代码在下载失败替换、重试丢失选择、关闭早于恢复这三处失败，再实施修复。

最终 Release 配置结果：

- Package validation：PASS；版本默认值、显式一致/不一致、缺失/无效项目元数据、错误 EXE 版本及只读验证入口。
- Core smoke tests：36/36 PASS。
- 文件事务回归：30/30 PASS。
- WPF UI smoke：PASS；含反复关闭、取消恢复等待、不响应取消时的完成等待、四语言程序集版本显示和此前全部界面回归。
- 总门槛：`AUTOMATED TEST GATE PASSED`。
- 便携包：实际解压，递归找到唯一 EXE；4 个文件逐个哈希与打包目录一致，README/EXE 版本和 ZIP SHA-256 均匹配。
- preview.42 ZIP 的原始 SHA-256 仍为 `6CD272145B7A3F2A00564188D9025336DC3D074A89F943EA20BBED969322DF64`，未被覆盖。

本次命令（复用已缓存 SDK/依赖，不还原或安装）：

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet-home'
& .\scripts\Test-Automated.ps1 -DotNet .\.dotnet-sdk\dotnet.exe -Configuration Release -NoRestore -BuildOutputDirectory bin/preview43-validation/
& .\scripts\New-ReleasePackage.ps1 -DotNet .\.dotnet-sdk\dotnet.exe -NoRestore
```

测试环境说明：中途版本资源键重名被 WPF 测试拦截，已修复；崩溃残留的旧 UI 测试进程占用默认输出，未强制结束无法核实路径的进程。最终使用上述独立的逐项目输出目录完成 Release 门槛。UI 测试入口现已捕获异常并返回失败退出码，避免再次进入 Windows 崩溃报告。旧占用解除前请沿用该输出参数。

## 内部审核资产

| 项目 | 值 |
| --- | --- |
| ZIP | `release/JavMetaLite-v1.2.0-preview.43-win-x64-portable.zip` |
| SHA-256 | `0C06D0FEBFFD660FF2E83D1CEF63D6F13B26132FA185565157D5013746283BF8` |
| 单版本校验 | `release/JavMetaLite-v1.2.0-preview.43-win-x64-portable.zip.sha256` |
| 汇总校验 | `release/SHA256SUMS.txt` |
| ProductVersion | `1.2.0-preview.43` |
| FileVersion | `1.2.0.0` |
| 包内文件 | `JavMetaLite.exe`、`README.txt`、`LICENSE.txt`、`THIRD_PARTY_NOTICES.txt` |

SDK、依赖缓存、旧版便携包均保留；这些本地资产继续由 Git 忽略。

## 尚未宣称完成的验收

- 未运行真实网站搜索/批量压力测试，没有读取或改写用户的实际影片库。
- 未验证真实 NAS 断线、断电/强制结束进程、杀毒软件干预；现有跨卷测试使用隔离的模拟文件操作，不能代替这些场景。
- WPF 测试已验证应用窗口与交互；此次便携 EXE 做了静态身份、解压和完整性校验，未另行启动它去加载用户的真实偏好。独立便携包的人工试用仍交给用户审核。
- 100/500/1,000 部影片的导入、滚动、切换及搜索性能尚未测量，因此不承诺规模与速度。

## 下一步

审核本内部版本，然后按 [大队列性能计划](PERFORMANCE-NEXT.zh-Hans.md) 建立基线，优先优化证实的瓶颈。完成优化、回归与实际副本验收后，再决定未来功能和外部发布；不要把本次收尾视为发布授权。
