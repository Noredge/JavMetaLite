# JAV Metadata Lite

一个只处理单个影片的轻量 Windows metadata 工具。它不会扫描整个媒体库；文件整理和直接保存都默认关闭，用户可以先预览所有变更，也可以明确选择跳过预览并直接覆盖 metadata。

## v0.5.0 开发中

当前 `feature/v0.5.0` 保留 dev4 已通过的文字字段候选：“多来源搜索（推荐）”会取得 LibreDMM 与 R18.dev 的完整文字资料，并允许逐字段选择。左侧封套区域右上方只提供一个紧凑的“封套 / Fanart”来源下拉，poster 与 fanart 始终共用所选来源；不提供独立剧照来源，也不跨网站合并剧照。若剧照不合适，可在主来源选择框改用 LibreDMM、R18.dev 或 JAVLibrary 后重新读取。

## v0.4.0 稳定版

- 拖入或选择一个影片文件
- 从常见文件名中识别番号
- 来源可选择“自动补全”、LibreDMM、R18.dev、JAVLibrary 或纯手动填写
- 自动补全以 LibreDMM 为日文主来源，只用 R18.dev 填补空字段
- LibreDMM 不可用或未找到时自动回退到 R18.dev
- 优先读取 LibreDMM 影片详情 JSON 的完整日文 Description，并只移除末尾商店配送说明
- 自动读取 LibreDMM 与 R18.dev 返回的样张列表
- R18.dev 使用其 Gallery 对应的 DMM 高清封套与 Sample Images 地址
- R18.dev 以英文标题、片商、导演、演员和类型为主，日文标题保存在 `originaltitle`
- 将 LibreDMM 演员图片网址写入 NFO 的演员资料（不额外生成 `actors/` 文件夹）
- JAVLibrary 要求验证时，可使用内置 WebView2 浏览器手动打开详情页并读取
- 所有字段都可以在保存前修改
- 默认先显示完整变更预览；勾选“直接保存并覆盖（跳过预览）”后从主窗口直接执行
- 可选创建标准番号文件夹，并可独立选择是否把影片重命名为番号
- 标准番号文件夹始终建立在影片当前目录内；若当前目录已经正好以番号命名，则直接使用该目录
- 影片目标已存在时阻止执行，永远不会覆盖另一个影片
- 先在临时目录生成完整 metadata，再提交输出并最后移动影片
- 提交失败时自动删除新输出、恢复被覆盖的 metadata，并尽量保持原影片位置不变
- 本地记录搜索来源、图片候选失败、保存步骤和恢复结果，界面提供“打开日志”入口
- 在影片旁边生成同名 `.nfo`
- 优先使用 DMM/FANZA 高清横版原图，而不是低清 `ps.jpg`
- 自动截取横版原图右半部分，生成 `<影片名>-poster.jpg`
- 使用作品完整横版封套生成 `<影片名>-fanart.jpg`
- 可选把所有有效 Sample Images 保存到 `extrafanart/`
- NFO 同时写入本地 poster 与 fanart 文件名
- 左侧同时预览 poster 与真正的横版 fanart
- 默认拒绝覆盖已有 NFO 或封面
- 未勾选“直接保存并覆盖（跳过预览）”时，如检测到重复输出，会在预览窗口要求明确确认
- 勾选“直接保存并覆盖（跳过预览）”后不再弹出变更预览，已有 NFO 或图片会直接覆盖
- 默认不修改影片本身；“整理到番号文件夹”和“影片重命名为番号”都默认关闭
- 修复网站验证时内置浏览器没有打开的问题
- 修复网页导入后远程封面触发 `This Freezable cannot be frozen.` 的问题
- 修复来源选择框下拉列表过白、文字难以辨认的问题

## 开发构建

需要 .NET 10 SDK：

```powershell
dotnet build .\JavMetaLite.App\JavMetaLite.App.csproj
dotnet run --project .\JavMetaLite.App\JavMetaLite.App.csproj
```

运行完整自动化测试门槛（核心 smoke、v0.4 文件回归、WPF UI smoke）：

```powershell
.\scripts\Test-Automated.ps1
```

完整自动化测试分层、回归范围和手动检查边界见 [`TESTING.md`](TESTING.md)。

v0.4.0 的真实来源、文件安全与 Jellyfin 完整人工验收见 [`MANUAL-ACCEPTANCE-v0.4.0-rc1.md`](MANUAL-ACCEPTANCE-v0.4.0-rc1.md)，START-237 修复的最终通过记录见 [`MANUAL-RETEST-v0.4.0-rc2.md`](MANUAL-RETEST-v0.4.0-rc2.md)。v0.5.0-dev3 的字段候选人工检查见 [`MANUAL-CHECK-v0.5.0-dev3.md`](MANUAL-CHECK-v0.5.0-dev3.md)，dev4 的多来源闭环检查见 [`MANUAL-CHECK-v0.5.0-dev4.md`](MANUAL-CHECK-v0.5.0-dev4.md)，dev5-r2 的统一封套功能检查见 [`MANUAL-CHECK-v0.5.0-dev5-r2.md`](MANUAL-CHECK-v0.5.0-dev5-r2.md)，dev5-r3 的顶部布局检查见 [`MANUAL-CHECK-v0.5.0-dev5-r3.md`](MANUAL-CHECK-v0.5.0-dev5-r3.md)。

发布 Windows x64 单文件版本：

```powershell
dotnet publish .\JavMetaLite.App\JavMetaLite.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 输出示例

```text
影片当前目录/
  IPX-123/            # 勾选“整理到番号文件夹”时在原地创建
    IPX-123.mp4
    IPX-123.nfo
    IPX-123-poster.jpg
    IPX-123-fanart.jpg
    extrafanart/      # 勾选“保存全部剧照”时生成
      fanart1.jpg
      fanart2.jpg
```

## 当前边界

- v0.4 仍然是单片编辑器，不提供媒体库扫描或批量刮削。
- 整理功能只移动当前选择的影片并生成本次选择的 metadata；不会自动搬运未知的字幕或旧伴随文件。
- 自定义目标路径暂未提供；当前整理目标固定为影片当前目录内的番号子文件夹。
- 软件不会写入 MP4/MKV 容器内部 metadata。
- `actors/` 暂不生成；演员图片以 NFO 内的远程 `thumb` 提供，`extrafanart/` 是可选输出。
- 网站结构变化可能需要更新 `JavLibraryClient`。
- 内置浏览器依赖 Microsoft Edge WebView2 Runtime；Windows 10/11 通常已经安装。
- 请遵守资料来源网站的使用条款，仅以合理频率查询。
- 运行日志默认位于 `%LOCALAPPDATA%\JavMetaLite\Logs`，保留最近 14 天。
