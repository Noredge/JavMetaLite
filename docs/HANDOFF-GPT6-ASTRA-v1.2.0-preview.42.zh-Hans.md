# JavMetaLite v1.2.0-preview.42 — GPT-6 Astra 接手说明

> 历史接手记录。当前已推进到 1.2.0 定稿，后续步骤以 [当前交付清单](RELEASE-v1.2.0.zh-Hans.md) 为准。以下“下一步”、版本及验证数字仅记录当时状态，不作为当前发布许可。

更新日期：2026-09-04

接手后的最新状态：Astra 已完成只读接管与内部可靠性收尾，见 [preview.43 收尾记录](CLOSEOUT-v1.2.0-preview.43.zh-Hans.md)。用户要求暂不对外发布；正式发布前必须先做 [代码与性能优化](PERFORMANCE-NEXT.zh-Hans.md)。以下版本号、提交及测试数字是接手前的 preview.42 历史基线。

## 接手前基线

- 工作目录：`work/JavMetaLite-review`
- 当前分支：`preview42-artwork-comparison`
- preview.42 功能实现提交：`33de64e Scope viewer candidates by image source`
- 上游基线：`origin/main` / `388fffb`（v1.1.1 合并提交；本地历史显示为 grafted）
- 应用版本：`1.2.0-preview.42`
- 用户已完成实际界面验收，并确认当前行为没有问题。
- 发布准备材料正在交给用户审核；在用户批准前，不要提交、推送、建立标签或发布。

`release/`、`.dotnet-sdk/`、`.dotnet-home/` 和 `.dotnet-sdk.zip` 都被 Git 忽略。它们是本机验证资产，不应误加入版本库；用户明确要求保留本地 SDK 和下载文件，避免后续重复下载。

## 接手时先做

1. 运行 `git status --short --branch` 和 `git log -5 --oneline`，不要假设工作树仍与本说明完全一致。
2. 审核 `CHANGELOG.md`、发布候选稿和两张 preview.42 截图；这是当时的未推送审核步骤。原实拍现仅本地留存，公开材料使用明确标注版本的合成替代图。
3. 若代码发生任何变化，重新运行完整自动化门槛并重新打包；只有文档审阅时不要无意义地重复生成 60 MB 便携包。
4. 不要清理 `.dotnet-sdk`、`.dotnet-home` 或 `.dotnet-sdk.zip`。
5. 不要在没有用户明确授权时推送、创建 PR、打标签或发布 GitHub Release。

## 不应破坏的产品行为

- 产品坚持本地、审核优先：选择影片和读取本地 NFO 不写文件；搜索不会自动保存；影片文件不会被覆盖。
- 单片和批量保存都必须走现有验证、预览、冲突检测、暂存、校验和回滚路径。不要另建绕过 `SavePlan` 的快捷写入流程。
- 未搜索、搜索失败或没有在线样张时，即使勾选“替换本地 Extra Fanart”，保存也必须原样保留本地图片。
- 图片来源是一整套选择：poster、fanart 和在线 Extra Fanart 必须保持来源一致；人工 Extra Fanart 勾选仍按来源分别记忆。
- 图片查看器默认在“全部图片”中定位入口图片。顶部“来源”决定在线候选范围，本地行始终保留；关闭或 Esc 放弃未应用的来源／在线样张修改。
- 删除已确认的本地 poster、fanart 或 Extra Fanart 在查看器中立即执行，但必须先显示应用内深色强制确认，并默认聚焦取消。
- 键盘交互保持精简：`Ctrl+S`、番号框 `Enter`、聚焦队列的 `↑/↓`。不要恢复全局 `Alt+↑/↓`，也不要把主窗口 `Esc` 绑定到取消任务。
- 来源选择、字段候选、本地 NFO 与手动修改必须继续可审核、可切回，并显示来源。

## 主要代码入口

| 范围 | 文件 |
| --- | --- |
| 主窗口、单片／批量编排、图片查看器入口 | `JavMetaLite.App/MainWindow.xaml`、`MainWindow.xaml.cs` |
| 图片查看器 | `JavMetaLite.App/ArtworkViewerWindow.xaml`、`ArtworkViewerWindow.xaml.cs` |
| 保存前预览 | `SavePreviewWindow.*`、`BatchSavePreviewWindow.*` |
| 自定义多来源规则 | `SourceProfileWindow.*`、`MetadataSourcePreferenceApplier.cs`、`ArtworkResolutionSelector.cs` |
| 每部影片隔离状态 | `JavMetaLite.Core/Services/MovieJob.cs` |
| NFO 安全读写 | `NfoReader.cs`、`NfoWriter.cs`、`NfoRoundTripWriter.cs` |
| 文件计划、事务与回滚 | `OrganizationPathPlanner.cs`、`OutputService.cs`、`FileOrganizationService.cs` |
| 自动来源 | `LibreDmmClient.cs`、`R18DevClient.cs`、`MetadataSearchCoordinator.cs` |
| 手动网页导入 | `BrowserWindow.*`、`BrowserImportRouting.cs` |
| 四语言资源 | `JavMetaLite.App/Resources/Strings.*.xaml` |
| 自动化门槛 | `JavMetaLite.SmokeTests`、`JavMetaLite.RegressionTests`、`JavMetaLite.UiSmokeTests` |

## 验证与打包

完整门槛：

```powershell
& .\scripts\Test-Automated.ps1 `
  -DotNet .\.dotnet-sdk\dotnet.exe `
  -Configuration Release `
  -NoRestore
```

preview.42 最终结果：

- Core：35/35
- 文件事务：29/29
- WPF UI：PASS
- 总门槛：`AUTOMATED TEST GATE PASSED`

打包（现在默认读取项目版本；不得用旧版本号给新程序命名）：

```powershell
& .\scripts\New-ReleasePackage.ps1 `
  -DotNet .\.dotnet-sdk\dotnet.exe `
  -NoRestore
```

成品：

- `release/JavMetaLite-v1.2.0-preview.42-win-x64-portable.zip`
- SHA-256：`6CD272145B7A3F2A00564188D9025336DC3D074A89F943EA20BBED969322DF64`
- 包内文件数：4
- `FileVersion`：`1.2.0.0`
- `ProductVersion`：`1.2.0-preview.42`
- `release/SHA256SUMS.txt` 已与压缩包一致

验证压缩包时必须先解压，再递归定位 `JavMetaLite.exe`；不要假设 EXE 位于解压根目录。验证临时目录必须位于已确认的 `release/` 目录内，完成后再删除。

## 发布材料

- 逐预览版本历史：`CHANGELOG.md`
- preview.42 行为边界：`docs/ROADMAP-v1.2-preview.42.zh-Hans.md`
- 发布候选审核稿：`docs/RELEASE-CANDIDATE-v1.2.0-preview.42.zh-Hans.md`
- 原 preview.42 主界面与图片查看器实拍仅保留在本地，不纳入公开源码。
- 当前合成示意：[1.2.0 主界面](images/javmetalite-v1.2.0-main-single.en.png)、[1.2.0 图片查看器](images/javmetalite-v1.2.0-artwork-viewer.en.png)；不是 preview.42 的历史截图。

## 已知边界

- 当前自动来源依赖外部网站结构和可用性；解析变化要基于真实当前页面修复，并保留旧结构回退测试。
- 内置网页导入依赖 Microsoft Edge WebView2 Runtime。
- 便携 EXE 未签名，Windows 可能显示 SmartScreen。
- 队列不会跨应用重启恢复。
- 真实 NAS／UNC 的权限、速度和中断行为仍取决于用户环境；第一次整理应使用测试副本。

## 当前下一步（取代原发布流程）

1. 审核 preview.43 内部修复、验证记录和便携包；没有用户明确授权，不提交或发布。
2. 先建立大量影片导入、界面响应和搜索请求的性能基线，再根据测量决定优化点。
3. 完成优化与回归、真实副本验收后，重新由用户决定版本号和是否对外发布。不得自动执行推送、PR、标签或 GitHub Release。
