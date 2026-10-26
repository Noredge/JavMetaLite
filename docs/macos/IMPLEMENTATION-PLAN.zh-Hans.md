# JavMetaLite for macOS — 原生 Mac 实施规格与 Codex 开工任务书

> **交付目标：做出真正好用、漂亮的原生 Mac 元数据审核工具，同时保留 Windows 版。**
>
> 技术路线已经确定：**SwiftUI + 必要的 AppKit；随 App 打包的 C# Worker；共享跨平台 Core。**
>
> 本文是实施规格，不是已经完成的移植报告。标为“现状”的内容来自仓库核查；标为“新增”“目标”“建议路径”的内容需要实现。不要把文中的协议、脚本或目录误认为仓库已经具备。

- 编写日期：2026-10-21，America/Los_Angeles。
- 仓库：<https://github.com/Noredge/JavMetaLite>
- 核查基线：`main` / `874b2dd524106189806e4d0c86589a0c8ee142c1`。
- 基线版本：`1.2.3`；该提交说明为 `Release v1.2.3: fix dotted-prefix movie discovery`。[R01]
- 本次交付物：实施文档。尚未修改仓库，尚未在 macOS/Windows 编译或运行测试。

---

> 阅读建议：Codex 先读第 0、2、5—8、15 节；产品与界面看第 3—4 节；验收看第 14—16 节。第 17 节附有可直接粘贴的启动指令。

## 0. 给 Codex 的执行指令

你正在一个已有稳定 Windows 产品的仓库中新增原生 macOS 客户端。请先读取根目录及相关子目录实际存在的 `AGENTS.md`、README、测试与维护说明，再核对当前提交和本文基线的差异。

**直接实施，不要只返回架构建议、设计稿或待办列表。** 在工作区安全、工具可用的前提下，第一次开工连续完成 M0—M3：平台依赖审计、Core 解耦、真实 Worker 通信，以及原生 UI 的单片 NFO 保存闭环。随后按 M4—M6 推进；不要以“页面已经显示”为完成标准。

执行约定：

1. 先检查 `git status`，保留用户未提交修改。没有相关工作分支时再创建 `feature/macos-native`；不得重置用户工作区、强制切分支或直接发布 Release。
2. 保留 `JavMetaLite.App` 的 WPF 界面、已有功能和 Windows 发布入口。允许为共享 Core 注入适配器而作小范围修改，不顺带重构整个 Windows UI。
3. 本文已经确定原生路线，不再改成 Avalonia、Electron、Tauri、MAUI/Mac Catalyst 或 WebView 主界面。
4. 优先复用经过测试的业务逻辑。Swift 不再实现一套 scraper、NFO writer、番号解析和文件整理规则。
5. 每个里程碑同时交付可运行代码、测试和必要说明；正常路径中不得用硬编码数据、延迟模拟或空实现代替业务功能。Fixture 仅用于测试与 SwiftUI Preview。
6. 美术、布局和普通工程细节按本文默认值落地，不为每个小选择暂停询问。确有阻塞时说明具体原因、已完成部分和可验证证据，不捏造测试通过。
7. 单次工作结束更新 `docs/macos/STATUS.md`：完成项、实测命令及退出码、未验证项、真实阻塞、下一项任务。区分“实现了”“编译通过”“实机验证过”。
8. 不读取或操作开发者真实片库进行自动测试。使用临时目录、虚构番号、合成图片和测试 NFO。

### 已拍板的产品决策

| 项目 | 决策 |
|---|---|
| 体验优先级 | 原生 Mac 交互与数据安全优先；不追求与 WPF 像素一致 |
| 主界面 | 任务队列 + 中央编辑工作区 + 可收起的保存检查器 |
| 前端 | SwiftUI；文件选择、Finder 集成、复杂焦点/窗口行为按需使用 AppKit |
| 后端 | `net10.0` 控制台 Worker；通过父子进程标准输入输出通信 |
| 复用 | providers、元数据审核、NFO round-trip、保存计划与事务保护 |
| 首个可用版本 | 单片：导入 → 本地资料 → 在线查询 → 编辑 → 预览 → 保存 NFO |
| 完整首版 | 在上述基础上补图片输出、批量审核、文件整理和原生网页导入 |
| 系统基线 | 产品默认最低 macOS 15；优先 Apple Silicon。Intel 在独立验证后发布 |
| 分发 | 先本地 `.app`；正式版 Developer ID 签名/公证，非 Mac App Store 路线 |
| 沙盒 | 首版不启用 App Sandbox，但仍需正确处理系统文件访问权限 |
| Windows | 继续 WPF，不要求 Windows 通过 Worker 调用 Core |

macOS 15 是本项目的兼容范围选择，不是“SwiftUI 或 .NET 只能从 15 开始”的技术结论。微软当前文档列出了 .NET 10 的 macOS 支持范围；开发环境和正式发布前仍应核实支持矩阵。[A01]

---

## 1. 产品定位与范围

### 1.1 一句话定位

**把一批本地影片及其 NFO、封面整理好：看得清、改得顺、保存前知道会发生什么。**

这是本地优先的 metadata 审核与整理工具。核心流程保持：

```text
导入文件/文件夹
    ↓
识别影片与分段，读取已有 NFO 和图片
    ↓
查询资料，逐字段比较并编辑
    ↓
选择图片及输出方式
    ↓
预览具体磁盘变更
    ↓
确认保存，显示结果或恢复信息
```

单片和批量共用同一种 `MovieJob` 工作流。批量只是管理多个审核任务，不绕过检查、编辑和保存计划。

### 1.2 首版不要扩张成这些东西

不新增完整媒体数据库、自动扫描全盘、播放器、Jellyfin 服务器管理、AI 自动分类/翻译、账号体系、云同步、插件市场或常驻后台服务。不要为 Mac 移植顺便改变 Jellyfin 输出规则。

不强制启动时展示海报墙；不把工作区做成网站 Dashboard；不新增每天都要维护的外部服务。

### 1.3 功能优先级

| 范围 | 必须交付的能力 |
|---|---|
| P0 / 第一个可用版本 | 原生窗口、文件导入、任务切换、本地 NFO 读取、已有图片显示、LibreDMM 查询、字段编辑、保存预览、原位置安全写 NFO、错误和取消处理 |
| P1 / 日常可用版 | 文件夹递归扫描、CD 分组与重复项处理、R18.dev/多来源审核、图片选择与输出、批量预检及顺序保存、整理目标目录、跨卷安全策略 |
| P2 / 完整首版 | WKWebView 手动导入、样张与本地图片管理、四种界面语言、偏好设置、完善测试、正式打包签名公证 |
| 后续独立迭代 | 崩溃后的完整任务草稿恢复、复杂自定义裁剪、Finder 扩展、自动更新、经过验证的同卷移动性能优化 |

**P0 允许暂不输出新图片和暂不移动视频，但必须隐藏/禁用这些未实现入口并解释原因；不能显示成功却没有写出文件。** 这是一条交付顺序，不是永久删减已有功能。

---

## 2. 实际仓库核查：不要从空白项目思路出发

以下内容基于上述固定提交。开工时复核差异，尤其是 `main` 后续可能新增的修复。

| 已核查位置 | 现状与迁移意义 |
|---|---|
| `README.md` | 版本 1.2.3；定位为单片/批量本地审核工具；默认建议 LibreDMM；JAVLibrary 仅手动网页查询。[R02] |
| `global.json` | SDK 为 `10.0.400`，`rollForward: disable`，不接受预发布；不要擅自升级整个仓库 SDK。[R03] |
| `JavMetaLite.Core/JavMetaLite.Core.csproj` | `net10.0-windows`、`UseWPF=true`，引用 AngleSharp；Core 目前还不是跨平台库。[R04] |
| `Core/Services/PosterImageProcessor.cs` | 使用 WPF 的 `BitmapDecoder`、`CroppedBitmap` 和编码器；横图取右半部分，JPEG 编码质量 95；原 JPEG fanart 可直接保留字节。[R05] |
| `Core/Services/OutputService.cs` | 保存过程直接调用上述静态图片处理器；在线样张批次大小 3、上限 50。这是需要注入适配器的真实调用点。[R06] |
| `Core/Services/OrganizationPathPlanner.cs` | `RequiresVerifiedCopy` 比较 `Path.GetPathRoot`；不能原样当成 macOS 跨卷判定。另有 Windows 文件名约束文案。[R07] |
| `Core/Services/FileOrganizationService.cs` | 已存在 `BuildPlan`、confirmed multipart 计划、源文件预期检查、视频目标冲突保护；路径逻辑中有 `OrdinalIgnoreCase`。应改适配点，不重写事务引擎。[R08] |
| `Core/Services/MovieJob.cs` | 已有 `ReviewRevision`、本地 NFO 保存阻塞、搜索/保存状态、手动字段保留和 `BeginSave(expectedReviewRevision)`。不要再发明一套冲突的业务状态。[R09] |
| `App/BatchSavePathConflicts.cs` | 批量读写冲突和共享 `extrafanart` 命名空间检查仍在 App 层；仅复用 Core 会漏掉它。[R10] |
| `Core/Services/BatchSaveCoordinator.cs` | 逐片执行；失败、取消会停止后续；**保存成功但存在 cleanup issues 也会停止后续**。已成功项目不会随整个批次回滚。[R11] |
| `App/MainWindow.xaml.cs` | UI 内仍持有 providers、保存服务、偏好、队列和部分编排逻辑；不能假设“业务已经全部抽到 Core”。[R12] |
| `Core/Models/MovieMetadata.cs` | 现有字段、演员结构、图片链接均有定义；日期/评分/时长等目前采用字符串，应避免 DTO 转换丢失原始值。[R13] |
| `Core/Services/AppPreferencesStore.cs` | 已支持显式配置目录，包含 schema 升级与“未来版本不可覆盖”保护；优先复用。[R14] |
| `Core/Services/AppLog.cs` | 已有 `ConfigureDirectory` 和日志清理；Mac 可配置目录，不必重写日志系统。[R15] |
| `TESTING.md` / `scripts/Test-Automated.ps1` | 现有门禁使用 `dotnet run` 执行三个测试程序，包含 Core smoke、事务回归、WPF UI；并非简单运行 `dotnet test` 就会覆盖。[R16][R17] |
| `JavMetaLite.SmokeTests.csproj` | smoke 工程本身也是 Windows/WPF 目标，需划分可移植测试与平台测试。[R18] |

### 两个不能漏掉的技术事实

**跨卷判断：** Unix 的绝对路径根通常是 `/`，不同挂载卷的文件可能得到相同路径根。[A02] 因此当前 `RequiresVerifiedCopy` 在 Mac 上不能代表真实卷关系。必须新增卷语义适配或采用安全的保守复制策略。

**批量安全：** `BatchSavePathConflicts` 不仅判断两个输出文件是否同名，还保护“另一部影片会读取或保留的文件”，并防止共享 `extrafanart` 混图。移植时必须完整迁移这套语义。[R10]

本次核查覆盖以上关键文件及部分大型文件的相关片段，**不是全仓逐行审计**。M0 仍需做完整平台依赖与调用点扫描。

---

## 3. Mac 设计方向

### 3.1 气质

关键词：**原生、安静、清楚、图片好看、编辑高效。**

用系统窗口、系统字体、系统控件、系统焦点与菜单行为建立质感。系统更新带来的材质变化自然跟随，不人工模仿某一版 macOS 的玻璃外观。[A03]

- 内容区保持清晰、稳定的背景；材质主要交给系统侧边栏和窗口 chrome。
- 以中性色为主，用系统强调色表达主要操作，颜色只辅助状态表达。
- 使用 SF Symbols，不用 emoji 充当正式工具栏图标。
- 不铺满独立卡片、粗描边、重阴影、渐变和胶囊按钮。
- 不做会干扰编辑的封面背景取色、持续粒子、光效和大幅过场。
- 封面保持可辨识，但不能占掉大部分工作区；软件的主任务仍是审核和编辑。

### 3.2 主窗口结构

采用两栏 `NavigationSplitView`，在 detail 工作区增加可折叠 `.inspector`。这样得到“队列 / 工作区 / 检查器”的三部分，不需要把 Inspector 伪装成额外导航层。[A04][A05]

```text
┌────────────────────── 原生标题栏 / 工具栏 ──────────────────────┐
│  侧栏  添加…     查询资料 ▾     图片预览       预览并保存    检查器 │
├──────────────┬──────────────────────────────┬──────────────────┤
│ 任务队列      │ 当前影片                      │ 保存检查器         │
│ 搜索/筛选     │                              │                  │
│              │ [封面]   番号 / 标题 / 状态    │ 输出项目           │
│ □ DEMO-001   │          文件位置 / 分段数     │ 保存位置           │
│ □ DEMO-002   │                              │ 命名设置           │
│ □ DEMO-003   │    资料              图片      │ 预览摘要           │
│              │ ──────────────────────────── │ 警告 / 冲突         │
│              │ 标题         [...........]   │                  │
│              │ 原标题       [...........]   │                  │
│              │ 演员 / 类型 / 制作信息        │                  │
│              │ 简介                         │                  │
├──────────────┴──────────────────────────────┴──────────────────┤
│ 3 部影片 · 1 部待检查                 查询进度 / 保存阶段          │
└───────────────────────────────────────────────────────────────┘
```

图中尺寸和布局为实施起点，不是固定像素复制稿。

| 区域 | 初始建议 |
|---|---|
| 窗口 | 理想初始约 1180 × 760 pt，必须限制在当前屏幕 visible frame 内 |
| 最小尺寸 | 约 920 × 620 pt；窄窗口自动收起 Inspector，而不是压扁字段 |
| 队列 | 默认约 230 pt，可在约 200—320 pt 范围拖动 |
| Inspector | 默认约 285 pt，可收起；窗口不足约 1120 pt 时优先收起 |
| 编辑工作区 | 自适应；正常宽度使用对齐的两列轻量字段布局，窄时改一列 |
| 封面 | 概览中约 150—180 pt 宽，保留比例；完整查看进入独立预览 |
| 间距 | 以 8/12/16/24 pt 为节奏，优先服从系统控件 intrinsic size |

初次只导入一部影片时队列可以保持简洁，不出现“批量管理大后台”。同一套窗口支持从一部自然增长到多部，不复制单片/批量两份编辑器。

### 3.3 原生行为

保留红黄绿窗口按钮、窗口缩放、全屏、菜单栏、右键菜单、系统文字编辑、拖放、滚动条和键盘焦点。第一版只维护一个主工作区窗口，允许独立图片查看窗口和设置窗口；不做多工作区并发写入。

关闭主窗口与退出应用分别遵循 Mac 行为：主窗口可关闭，应用可通过 Dock 重新打开；未保存编辑和正在进行的保存必须有明确处理。不要因重建 SwiftUI View 而重新初始化 Worker 或清空任务。

### 3.4 原生不等于只有 SwiftUI

优先使用 SwiftUI。遇到混合文件/文件夹选择、窗口关闭拦截、复杂图片缩放、精确焦点或列表性能问题，可局部封装 AppKit。不要为了“纯 SwiftUI”牺牲正常的桌面交互，也不要把所有视图改回巨型 AppKit 控制器。

---

## 4. 各界面与交互规格

### 4.1 空状态与导入

空状态显示应用名称、一句用途说明、“添加影片或文件夹…”按钮，以及拖放提示。不展示伪造的最近项目和统计数字。

使用 `NSOpenPanel`，支持文件、文件夹、多选，以及一次选择混合输入。[A06] Finder 拖入与菜单导入走同一个后端入口；支持后续追加，不替换当前队列。

扫描在后台运行，显示已发现数量并可取消。跳过不可访问项时返回逐项原因，不把部分成功说成全部成功。默认不递归跟随目录 symlink，不扫描隐藏系统目录；显式选中的文件仍按权限和类型检查。

使用现有影片识别规则，保留 dotted-prefix 等已修复案例。相同文件重复加入应定位已有任务或提示重复；不同文件但番号相同不静默合并。CD 自动分组与“用户明确确认的多段影片”分别保留原规则。[R01][R08]

### 4.2 任务队列

每行展示缩略图、番号、可截断标题和清晰状态。正常显示不需要把完整文件路径塞进每一行；路径通过 tooltip、上下文菜单或详情查看。

必须区分两个概念：

- **当前查看/选中的行**：决定中央面板展示什么。
- **参与批量的勾选项**：对应现有 `IsSelectedForBatch`，决定“查询已勾选”“保存已勾选”的范围。

P0 可先单选导航。P1 增加原生 Command/Shift 多选；多选时中央显示批量摘要，不随机选一部让用户误以为正在修改全部。列表空格键可以切换批量勾选，前提是焦点在列表而不在文本或图片视图。

筛选建议：全部、需填写番号、待查询、待审核、未保存修改、保存失败/冲突、已完成。状态计算复用 Core；不要把一枚“完成”徽章混用成“查询完成”和“磁盘已保存”。

从队列移除、清空队列绝不删除磁盘上的视频或 sidecar。有未保存编辑时显示数量并确认。保存期间锁定相关任务，不能移除正在提交的项目。

### 4.3 中央资料编辑器

字段来自 `MovieMetadata`，不要设计一套少字段 DTO 导致 NFO 信息被丢弃。[R13]

| 分组 | 字段 |
|---|---|
| 概览 | 番号、标题、封面、当前任务状态、文件/分段位置 |
| 基本资料 | 标题、原始标题、发布日期、时长、评分 |
| 人员与分类 | 演员、类型/标签、导演 |
| 制作信息 | Maker、Label、Series、ContentId；中文显示名遵循现有语义 |
| 简介 | 可正常换行、选中和撤销的多行编辑器 |
| 来源 | 当前候选来源、字段差异入口、源页面链接 |

日期、评分、时长应允许保留已有字符串；添加校验提示不能默默把无法解析的旧值清空。空字符串与“未修改该字段”必须区分。

每个具有多来源候选的字段右侧提供轻量来源入口。点击弹出候选比较，明确“本地”“手动”“LibreDMM”“R18.dev”等来源；采用一个值后保留其来源和审核状态。

搜索失败不清掉已有本地资料；重试失败来源不覆盖用户手动修改。查询时可暂时锁定该任务会冲突的字段，但必须允许浏览其他任务。

Swift 文本编辑器保留原生撤销、选区、光标和中文/日文输入法组合过程。避免每次后端事件到来都重建整个 Form 或回填正在输入的 TextField。

### 4.4 图片页与查看器

图片页分为“海报”“横版封套/fanart”“样张”。封面候选要明确来源、尺寸、当前选择；低清提醒复用既有判断，不能自创一个与 Windows 不一致的固定阈值。

主界面展示缩略图；打开查看器才加载必要的大图。允许适合窗口、100%、放大缩小和查看尺寸，不拉伸图片。左右切换图片时保持稳定窗口和清晰选中反馈。

P1 保留既有横向封套生成海报的语义：横图取右半部分，而不是居中裁切。奇数宽度也要覆盖测试；两端 JPEG 不要求逐字节相同，但裁切区域、尺寸和质量不能明显退化。[R05]

“图片源选择”“样张勾选”“写出图片”是不同操作。查看图片和切换来源不能提前修改磁盘。

P2 本地图片删除采用单独的显式操作：优先通过 Mac 的废纸篓机制；无可用废纸篓时默认保留文件并提示，不静默退化为永久删除。该行为是 Mac 版明确设计差异，Windows 现有即时删除行为不顺带修改。[R02]

删除必须显示实际文件路径并确认，完成后重新读取本地图片状态、更新 revision、作废旧保存预览。不要宣称它与本次 NFO 保存属于同一个可回滚事务。

### 4.5 保存检查器

Inspector 放“输出与整理”，不复制中央的全部 metadata 字段。

建议包含：

- 输出：NFO、海报、fanart、样张；未实现项目不能启用。
- 位置：原位置、源位置番号文件夹、自定义根目录下番号文件夹。
- 命名：是否重命名视频、标题是否包含番号等已有选项。
- 保护：覆盖 sidecar 的明确选择、跨卷完整 SHA-256 校验说明。
- 当前计划摘要：新增、替换、移动、保留各多少项，是否有阻塞。

对原位置保存，保留已识别的 `movie.nfo`、文件夹级 poster/fanart 及已有扩展名策略，不随意改成统一文件名。[R08]

首版默认始终显示保存预览，不提供跳过预览的快捷开关。已有 Windows 偏好即使包含跳过选项，也不能在 Mac 首次导入时悄悄开启。

### 4.6 保存预览 Sheet

工具栏“预览并保存”和 `⌘S` 都进入同一条预览流程。

预览按影片分组显示完整路径差异，至少包含：操作、来源、目标、覆盖/冲突、原因。路径可复制，过长时中间省略但能展开查看。

```text
将修改 1 部影片

新增       /…/DEMO-001.nfo
替换       /…/DEMO-001-poster.jpg
保留       /…/DEMO-001.mp4

[取消]                              [确认保存]
```

存在阻塞时禁用“确认保存”，给出可执行的解决方式，例如重新选择目标目录、修改输出设置或重新生成预览。

预览绑定 `planId` 和任务 `ReviewRevision`。用户改了 metadata、图片选择、输出设置，或者源文件被外部修改后，旧计划不得继续执行。

### 4.7 结果、错误和取消

成功后显示写出的具体位置、Finder 定位和“保存完成”的任务状态。结果分为：

| 结果 | UI 行为 |
|---|---|
| 成功 | 标记已保存，更新文件路径和本地状态 |
| 已保存但清理未完成 | 明确写“文件已保存，部分临时文件需处理”；展示位置并停止后续批次 |
| 失败且已恢复 | 保留编辑草稿，显示失败阶段，允许重新预览 |
| 恢复未完成 | 强提醒并提供恢复目录；禁止自动清理、自动重试保存 |
| 取消 | 说明已完成哪些、当前项目的最终状态、哪些尚未开始 |

取消请求不等于进程已经停止。提交或回滚时应显示“正在安全停止/恢复”，不直接杀 Worker。保存完成后才来的取消请求不能把成功结果改写为“已取消”。

### 4.8 菜单与快捷键

| 快捷键 | 行为 |
|---|---|
| `⌘O` | 添加影片或文件夹 |
| `⌘S` | 预览并保存当前影片；不绕过预览 |
| `⌘R` | 查询当前影片资料；菜单名与工具栏一致 |
| `⌘F` | 聚焦队列搜索 |
| `⌘,` | 设置 |
| `⌘Z` / `⇧⌘Z` | 编辑撤销/重做，遵循原生焦点上下文 |
| `⌘.` | 请求取消当前操作 |
| `Esc` | 关闭非破坏性弹层/图片查看；不得隐式确认保存 |
| `Delete` | 仅列表聚焦时从队列移除；文本输入时正常删除文字 |
| `Space` | 依据焦点切换列表勾选或图片预览，不抢占文本输入 |

不要把 `⇧⌘S` 随意用于批量提交。批量操作先通过有明确数量的菜单/工具栏入口提供，避免与“另存为”心智冲突。

### 4.9 退出与未保存编辑

第一版队列保持会话内数据，不自动恢复所有片库路径；这与当前 README 描述一致。[R02] UI 偏好、最近手动选择的目录与恢复日志可持久化，且提供清理入口。

切换任务不丢失草稿；退出前检查所有任务，而不是只检查当前任务。有未保存编辑时提供“返回编辑”“放弃编辑并退出”“查看待保存项目”。不能为了退出自动保存未审核影片。

有保存事务进行中时阻止立即退出，允许请求取消并等事务进入可结束状态。系统强制结束后的恢复属于异常恢复流程，不能承诺与正常退出等价。

---

## 5. 架构与责任边界

```text
JavMetaLite.App（现有 WPF）                  macOS 原生 App
          │                            SwiftUI + AppKit + WKWebView
          │                                     │
          │                             JSON-RPC 2.0 / stdio
          │                                     │
          │                            JavMetaLite.Worker
          │                                     │
          └───────────────┬─────────────────────┘
                          │
                 JavMetaLite.Core（net10.0）
          providers / review / NFO / save plan / transactions
                          │
                  可注入的有限平台能力
             图片处理、路径/卷语义、存储目录
```

### 5.1 Swift 层负责

窗口、视图状态、菜单、焦点、原生文件对话框、Finder/废纸篓、可访问性、输入法、图片显示、WKWebView、呈现 DTO 与错误信息。

Swift 可以暂存尚未提交给 Worker 的文本输入，但不拥有另一套 metadata 合并算法、编号标准化规则或保存计划生成算法。

### 5.2 Worker 负责

任务注册与生命周期、调用共享业务、串行化任务变更、协议、进度、取消、预览令牌、图片缓存、保存结果和恢复记录。只处理当前 App 会话，不安装 LaunchAgent，不监听 TCP，不开 localhost HTTP 服务。

### 5.3 Core 负责

与 GUI 无关的规则和安全行为。不得引用 AppKit、Swift、WPF、WebView2、窗口调度器和 UI 控件。

`INotifyPropertyChanged` 本身不是 WPF 专属，不必为了“纯净”机械删除；只要 Core 不依赖 UI Dispatcher，由各客户端正确调度即可。

### 5.4 建议新增结构

```text
JavMetaLite.Core/                        # 改为 net10.0
JavMetaLite.App/                         # 原 WPF，不替换
JavMetaLite.Imaging.Windows/             # WPF 图像实现，Windows 专用
JavMetaLite.Imaging.Skia/                # Worker 使用的跨平台图像实现
JavMetaLite.Worker/                      # console host、DTO、协议、任务注册
JavMetaLite.PortableTests/               # 可移植控制台回归门禁；沿用仓库测试风格

macos/
  JavMetaLiteMac.xcodeproj/              # 提交可直接打开的工程与 shared scheme
  JavMetaLiteMac/
    App/
    Features/Queue/
    Features/Editor/
    Features/Artwork/
    Features/SaveReview/
    Features/Settings/
    Infrastructure/Worker/
    Infrastructure/FileAccess/
    Resources/                          # assets、Localizable.xcstrings
  JavMetaLiteMacTests/
  JavMetaLiteMacUITests/

contracts/worker-v1/                     # 协议说明、schema、跨语言 golden fixtures
scripts/macos/                          # build/test/package/verify
fixtures/macos/                          # 合成数据；不得含真实片库信息

docs/macos/
  AUDIT.md
  ARCHITECTURE.md
  PROTOCOL.md
  TESTING.md
  STATUS.md
```

目录可按实际仓库约定小幅调整，但不能增加不必要的 service/repository/factory 层。一个 Swift 工程、一个 Worker、共享 Core 足够支撑首版。

---

## 6. Core 去 Windows 化的具体任务

### 6.1 平台依赖扫描

扫描所有生产代码和测试中的以下内容，并在 `AUDIT.md` 记录“保留/移动/替换/需实测”：

```text
System.Windows / PresentationCore / PresentationFramework
BitmapSource / BitmapDecoder / CroppedBitmap / Int32Rect
Dispatcher / Application.Current / WebView2
Microsoft.Win32 / Registry / user32 / shell32 / DllImport
Windows 路径、盘符、UNC、反斜杠假设
OrdinalIgnoreCase 在路径集合、去重、冲突检查中的使用
Path.GetPathRoot 与跨卷判断
Process.Start / explorer.exe / cmd.exe / powershell.exe
LocalApplicationData / 日志 / 临时目录
FileShare / 文件占用重试 / File.Replace / 取消与回滚
```

不要全局替换所有 `OrdinalIgnoreCase`：番号、来源名、扩展名和真实文件路径具有不同语义。

### 6.2 图片处理接口

新增窄接口，例如：

```csharp
public interface IArtworkImageProcessor
{
    (int Width, int Height) GetDimensions(byte[] bytes);
    byte[] CreatePoster(byte[] sourceBytes, bool png);
    byte[] CreateFanartJpeg(byte[] sourceBytes);
    byte[] CreateFanartPng(byte[] sourceBytes);
}
```

这是**拟新增接口**，不是现有 API。

将当前 WPF 算法移入 Windows 实现，优先保留其行为；`OutputService` 通过构造参数获得处理器，更新所有生产与测试调用点。不要留下一个在 macOS 静默返回空图片的参数默认值。

Mac Worker 的默认实现使用 SkiaSharp 及对应 macOS native assets；开工时锁定同一兼容版本并记录许可证，不使用浮动包版本。[A07] P0 若只支持 NFO 输出，仍应通过 capability 正确禁止未接入的图片写出，不能把 `NotImplementedException` 藏在已启用按钮背后。

测试至少覆盖 JPEG/PNG、竖图、横图、奇数宽度、透明度处理、损坏/空输入、EXIF 方向、大小限制、原 JPEG fanart 保留语义。Windows 保持既有结果；任何有意修正的图像行为需单独测试与说明。

### 6.3 提取遗漏的共享编排

把 `BatchSavePathConflicts` 的算法从 App 层提取到 Core 或公共工作流服务，用纯数据输入，移除对 WPF 预览模型的依赖。Windows 调用同一份实现；Mac 不重抄一个简化版本。[R10]

逐项审查 `MainWindow.xaml.cs` 内的导入、搜索、应用来源、重试、预览准备和提交编排。只有需要两个客户端共享的业务才提取；窗口尺寸、控件刷新、焦点等留在 WPF。[R12]

复用已有 `MovieJob`、`MetadataReviewSession`、`ArtworkCoverReviewSession`、`MovieJobLoader`、搜索和批量协调器；以当前实际签名为准，不凭名称假设其完整能力。

### 6.4 Core 与测试目标

最终 Core 只目标 `net10.0`，不通过多目标保留 WPF 泄漏。Windows 图像实现和 WPF UI 测试继续使用 Windows TFM。

旧 smoke/transaction 测试需按实际依赖划分：可移植案例应在 Mac 上运行；平台特有案例保留在 Windows 门禁，不为获得“全绿”直接跳过。

如果暂时无法原地迁移旧测试，先建立 `JavMetaLite.PortableTests`，共享 fixture 和测试逻辑，再逐步减少重复。不能把只运行一个 hello-world 测试称为 Core 跨平台验证。

---

## 7. 文件系统与事务：移植的硬要求

### 7.1 统一路径语义

用统一的路径/卷语义服务供扫描、去重、计划、跨任务冲突及执行使用。接口形态可类似：

```text
CompareExistingFiles(a, b) → Same / Different / Unknown
GetVolumeRelation(source, destinationParent) → Same / Different / Unknown
GetCaseSensitivity(existingParent) → Sensitive / Insensitive / Unknown
ValidateTarget(plan) → conflicts + source expectations
```

`Unknown` 是正式结果，不允许默认当作安全或同卷。

已存在文件比较尽可能使用文件系统身份；不存在目标应按最近存在的父目录所在卷判断。macOS 的卷标识和是否区分大小写可通过原生文件系统能力查询；Foundation 提供相应资源属性。[A08] Worker 的执行安全不能仅依赖前端发来的可过期提示。

如使用原生互操作，封装为小型平台适配并针对 arm64/x64 验证 ABI；不要凭记忆复制未经测试的 `stat` 结构体。不要为这个适配再引入常驻系统服务。

### 7.2 允许的首版保守策略

在可靠的 Mac 同卷识别完成前：**所有实际视频位移都可走既有“复制、完整 SHA-256 校验、提交成功后移除来源”的安全路径。** 同路径仅编辑 NFO 不复制视频。

该降级牺牲性能，不降低校验；UI 应如实显示复制操作。无法可靠区分大小写别名或文件身份时阻止有歧义的破坏性操作，并给出原因。

这让项目能够先交付，而不需要在 P0 就完成所有文件系统优化。不能用 `GetPathRoot` 相同作为快速移动依据。[R07][A02]

### 7.3 macOS 必测场景

- APFS 默认大小写行为与区分大小写卷；不能只按操作系统决定 comparer。
- 中文、日文、空格、emoji，以及 NFC/NFD 表示差异；不得为了比较而重写真实文件名。
- 同一文件通过 symlink 或其他路径别名重复加入；扫描不得进入 symlink 循环。
- 大小写不同但目标可能指向同一文件；无法明确处理时禁用该重命名。
- 外接卷或 SMB 在预览后断开、只读、权限被撤销、空间不足、挂载名变化。
- 自定义根目录不存在时不自动创建一个替代挂载目录。
- 两部片共享 sidecar 或 `extrafanart`；未保存/被跳过任务的源文件也必须受到保护。

新生成的名称采用兼容 Windows/Jellyfin 交换的保守规则；已有文件名不擅自清洗。不要把 `/Volumes/NAS` 不见了理解成“创建一个同名本地目录继续保存”。

### 7.4 真实保存入口

所有正式输出经 `FileOrganizationService` 的计划和执行流程。不得从 Swift 直接写 NFO，也不得为方便让 Worker 直接调用 `NfoWriter` 覆盖用户源文件而绕开事务保护。[R08]

保存前后至少保证：

1. 预览与执行对应同一份审核内容及设置。
2. 重新验证源 NFO/文件预期和目标占用；外部修改会使计划失效。
3. **永不覆盖已有目标视频。** sidecar 的覆盖也必须是明确确认的计划项。
4. 临时输出、备份、校验、提交和失败恢复沿用现有保护行为。
5. 跨卷源文件只有在目标完整校验及事务提交成功后才能移除。
6. cleanup warning 与提交失败区分；恢复失败时保留现场。

这不是“所有文件系统上天然 ACID”的承诺。断电、强制杀进程、设备消失需独立测试并如实记录保障范围。

### 7.5 批量规则

批量先准备所有相关计划并执行跨任务冲突检查，再按稳定顺序逐片提交。

每个影片是独立提交单元。前面已完成的影片保留结果；当前失败/取消后后续项为 `NotStarted`，不要伪造“整个批次全部回滚”。保存成功但清理问题未解决，也停止下一片。[R11]

跨任务保存全局串行；查询和缩略图加载可在受控并发下执行，但不能与同一任务的写入产生竞争。

### 7.6 Worker 异常与恢复记录

在启动每个真实保存事务前，持久化最小操作记录：事务 ID、plan ID、相关路径、预期操作、开始时间和状态；通过事务阶段观察点补充 staging/backup/recovery 位置。

记录放 Application Support，不放可随意清理的 Caches。必要时增加窄的事务观察接口，不能为日志改写整个事务算法。

Worker 异常退出后，下次启动标记未确认事务为“需检查”。允许用户查看日志与恢复目录，但不得自动重放写入或自动删除现场。完整自动断点续作不属于首版。

`planId` 的防重复提交仅在有记录的状态范围内成立；不能声称跨进程崩溃后天然 exactly-once。

---

## 8. Worker 协议：明确到可以实现

### 8.1 传输约定

使用 **JSON-RPC 2.0 消息结构 + 每行一个 UTF-8 JSON 对象**，运行在 Swift `Process` 的 stdin/stdout 上。这是项目定义的 framing，不是 HTTP，也不是 LSP 的 Content-Length framing。[A09]

- UTF-8，无 BOM，单条消息占一行；字符串里的换行由 JSON 转义。
- stdout 只允许协议消息；日志走 stderr 和日志文件。
- 同时异步读取 stdout/stderr，所有 stdout 写入经单一 writer 队列串行化。
- 请求 `id`、任务 `jobId`、操作 `operationId`、计划 `planId` 都是字符串，不混用。
- 普通消息上限默认 8 MiB；不通过 JSON 发送原图 base64。
- DTO 带协议版本；未知可选字段容忍，未知方法/不兼容版本返回明确错误。
- 握手不兼容时停止写操作，显示“客户端与内置引擎版本不匹配”。

### 8.2 初始化

```json
{"jsonrpc":"2.0","id":"req-1","method":"initialize","params":{"protocolVersion":1,"clientVersion":"0.1.0-dev","locale":"zh-Hans"}}
```

```json
{"jsonrpc":"2.0","id":"req-1","result":{"protocolVersion":1,"workerVersion":"0.1.0-dev","sessionId":"session-demo","capabilities":{"readLocal":true,"searchLibreDmm":true,"writeNfo":true,"writeArtwork":false,"organizeVideo":false,"manualBrowserImport":false}}}
```

这些版本号是开发阶段示例，不要求改动 Windows `1.2.3` 版本或发布新版本。

### 8.3 必备方法

以下均为**拟新增协议方法**，实现时落入 `contracts/worker-v1` 并让两端测试使用同一批 fixture。

| 方法 | 作用 | 关键约束 |
|---|---|---|
| `initialize` | 版本与能力握手 | 所有写操作之前必做 |
| `workspace.importInputs` | 导入本地文件/目录 | 返回 operationId，后台发现并发事件 |
| `workspace.getSnapshot` | 读取整个会话摘要 | 用于 UI 同步与漏事件恢复 |
| `jobs.get` | 获取完整任务审核状态 | 包括 revision、候选、错误及本地保护状态 |
| `jobs.applyEdits` | 应用用户手动编辑 | 携带 expectedReviewRevision，只改显式提交字段 |
| `jobs.selectCandidate` | 采用来源字段候选 | 走已有审核逻辑，不直接改 DTO |
| `jobs.search` | 单片或勾选批次查询 | 返回 operationId；遵循 provider 限流与取消 |
| `jobs.retryFailedSources` | 重试失败来源 | 不丢手动值、已有成功来源和图片选择 |
| `jobs.remove` | 移除任务 | 不删除磁盘文件；活动保存任务不可移除 |
| `artwork.getPreview` | 获得受控缩略图/预览缓存 | 返回 cacheId 和缓存文件位置，不返回任意本地文件 |
| `artwork.select` | 更新封面、样张选择 | 变更 revision，作废相关计划 |
| `save.preview` | 生成单片/批次保存计划 | 返回 planId、修订、冲突与磁盘变更 |
| `save.execute` | 执行已确认计划 | 仅接受服务端已有 planId，不接受前端自造写入列表 |
| `operations.cancel` | 请求取消 operationId | 返回收到请求，不假装已终止 |
| `operations.get` | 查询操作当前/最终状态 | 用于超时、界面重连、重复请求处理 |
| `preferences.get/update` | 读写业务偏好 | schema 保护；UI 外观偏好留 Swift |
| `shutdown` | 正常退出 | 先等待事务安全结束；关闭所有管道 |

P2 追加 `browser.importPage`、本地图片移除后的刷新/预检入口，并更新 capability。不要在 P0 对尚未实现方法返回伪成功。

### 8.4 编辑并发与 revision

Swift 层为每个任务维护本地文本缓冲。失焦或短暂 debounce 后调用 `jobs.applyEdits`；搜索、保存预览、任务移除前必须 flush 并等待结果。

每个任务的变更在 Worker 中串行处理。`expectedReviewRevision` 不匹配时返回当前快照与 `REVISION_CONFLICT`，不盲目覆盖新值。复用 `MovieJob.ReviewRevision`，不要创建一枚业务含义相同但可能失步的第二 revision。[R09]

原生撤销产生新的用户编辑命令；不能把后端 revision 简单减一。文本输入中的 IME composition 不做自动回填覆盖。

### 8.5 长操作事件

长操作请求快速返回 `operationId`；后续通过通知发送阶段与完成结果，不长时间占住请求读取循环。

```json
{"jsonrpc":"2.0","id":"req-9","result":{"operationId":"op-save-demo"}}
```

```json
{"jsonrpc":"2.0","method":"operation.progress","params":{"operationId":"op-save-demo","jobId":"job-demo","sequence":4,"phase":"verifying","completedBytes":5242880,"totalBytes":10485760,"canCancel":true}}
```

```json
{"jsonrpc":"2.0","method":"operation.finished","params":{"operationId":"op-save-demo","sequence":8,"status":"completed","result":{"savedJobIds":["job-demo"],"notStartedJobIds":[],"cleanupWarnings":[]}}}
```

没有可测总量时发送阶段和不确定进度；不要制造百分比或假倒计时。每个 operation 事件序号单调递增，UI 丢弃过期事件，发现缺口可以查询 snapshot/status。

长操作 timeout 是“状态待确认”，不能自动重试 `save.execute`。首次提交将 plan 标记执行中；重复提交同一 plan 返回原 operation 状态，不启动第二次写入。会话重启后旧 plan 失效。

### 8.6 错误协议

保留 JSON-RPC 标准 parse/invalid request/method not found 错误。业务错误使用稳定的应用码与可本地化 reason；原始 exception 只进入诊断详情。

```json
{"jsonrpc":"2.0","id":"req-12","error":{"code":-32010,"message":"Save preview is no longer valid.","data":{"reason":"PLAN_STALE","jobId":"job-demo","retryable":true,"requiresNewPreview":true}}}
```

至少定义：`REVISION_CONFLICT`、`PLAN_STALE`、`PATH_CONFLICT`、`ACCESS_DENIED`、`VOLUME_UNAVAILABLE`、`SOURCE_CHANGED`、`PROVIDER_RATE_LIMITED`、`PARTIAL_SOURCE_FAILURE`、`UNSUPPORTED_CAPABILITY`、`RECOVERY_REQUIRED`、`WORKER_UNAVAILABLE`。

### 8.7 图片数据与缓存

Worker 下载来源图，缓存原始数据和需要共享的产物；Swift 使用本地缓存做显示与缩略图降采样。源文件只能来自显式导入上下文或允许的缓存，远程 metadata 中的字符串不能自动变成本地文件读取权限。

缓存文件带类型、大小、像素尺寸与明确生命周期；前端只接受缓存根目录内的 Worker 返回文件。网络 URL 与本地路径分开建模，不通过字符串猜测混用。对网络响应、图片压缩字节数和解码像素数设置明确上限，并测试超限拒绝；不要只靠文件扩展名判断图片格式。

### 8.8 进程生命周期

App 启动后异步启动一个 Worker，窗口可以先显示空状态。按 bundle 绝对路径启动可执行文件，传参数数组，不经 `/bin/sh -c` 拼接路径。

预发布 `.app` 必须自包含运行时，不依赖 Finder 启动时的 PATH、用户 Homebrew 或系统 `dotnet`。开发模式可以显式配置 SDK 启动，但不能把开发机路径写进正式包。

EOF/父进程退出时请求安全取消并完成必要收尾。常规操作失败不重启 Worker；协议损坏或进程崩溃可以提供重新启动入口，但所有保存计划必须失效，未确认事务必须检查。

---

## 9. Swift 侧代码组织与状态

建议核心对象：

| 对象 | 责任 |
|---|---|
| `WorkspaceStore` | `@MainActor`；列表摘要、当前选择、批量勾选、已加载任务视图状态 |
| `MovieEditorState` | 每任务输入缓冲、dirty 状态、来源选择、原生 UndoManager 协作 |
| `WorkerClient` | actor；启动进程、编解码、请求关联、通知分发、取消、关闭 |
| `SaveReviewModel` | 只呈现后端计划；管理 planId、预览失效、确认与结果 |
| `ArtworkImageLoader` | 后台降采样、取消、LRU 缓存；不阻塞 MainActor |
| `FileAccessCoordinator` | NSOpenPanel、Finder 定位、书签/访问失败、废纸篓操作 |

DTO 用 `Codable`，业务状态不要靠中文/英文显示字符串判断。UI 更新通过 MainActor，管道读取、JSON 解码、大图解码、目录扫描、文件哈希都不在主线程执行。

不要求全项目采用复杂架构框架或通用依赖注入容器。只给 Worker 和文件访问等可测试边界注入实现。

### 任务状态映射

沿用 Core 的搜索状态、保存状态和审核 revision，再在 Swift 推导显示：

```text
已导入 → 需番号 / 可查询
查询中 → 查询完成 / 部分来源失败 / 查询失败 / 已取消
编辑中 → 有未保存修改
预览中 → 可保存 / 有阻塞 / 已过期
保存中 → 已完成 / 已完成但清理警告 / 失败 / 已取消 / 需恢复
```

“查询完成”不意味着“已人工审核”，更不意味着“已保存”。字段未修改不代表用户必须点遍每个字段，保持原有审核工作流，不新增形式化审批负担。

---

## 10. 来源与内置浏览器

### 10.1 查询来源

保留 LibreDMM 默认来源、R18.dev 和自定义多来源规则。复用既有超时、重试、限流与降级；不要为 Mac 开无限并发。

JAVLibrary 仍为手动网页查询。不得把移植改造成绕过验证的自动抓取器，也不自动读取用户其他浏览器的 cookies 或登录资料。[R02]

### 10.2 原生网页导入（P2）

使用独立 `WKWebView` 窗口或 Sheet，不嵌套整个应用界面。[A10]

提供地址/当前域名、后退、前进、刷新、“导入当前页面”。用户主动触发导入时才提取当前页面内容，交由已有解析与导入路由处理。

仅在支持的页面/来源上启用导入。限制 URL scheme、HTML 大小和来源；网页内容视为不可信输入，不能调用文件保存、shell 或任意 Worker 方法。

网页里的番号与当前任务不匹配时明确阻止或让用户重新选择任务，不能把资料静默写到错误影片。复用现有 `BrowserImportRouting` 与 `MovieJob` 手动导入保护，实际签名由 M0 核查。

网站验证、加载失败和不支持页面要有真实状态。浏览器本身能打开不等于网页导入已经验收。

---

## 11. 设置、数据目录和隐私

### 11.1 存储位置

通过系统目录 API 取得实际路径，不拼接用户名。建议布局：

```text
~/Library/Application Support/JavMetaLite/
  settings.json             # Worker 管理的业务偏好
  recovery/                 # 未完成事务记录；不能作为普通缓存清除

~/Library/Caches/JavMetaLite/
  artwork/                  # 可重建图片缓存
  previews/                 # 可重建预览产物

~/Library/Logs/JavMetaLite/
  ...                       # 日志，默认适度轮换
```

Swift 的外观、侧栏宽度、Inspector 展开等 UI 偏好使用 UserDefaults。共享业务设置继续用 `AppPreferencesStore` 的 schema 与保护逻辑，不让两种存储同时争夺同一字段。[R14]

通过现有 `AppLog.ConfigureDirectory` 配置 Mac 日志目录。[R15] 业务偏好和事务记录不可写进 `.app` bundle。

### 11.2 文件权限

Developer ID 非沙盒分发不等于自动拥有所有目录权限。遇到系统拒绝访问，显示具体路径与操作，让用户重新选择或授权适当目录；不要默认要求 Full Disk Access。

普通书签可用于找回用户选择的位置；别承诺书签会永久授予权限。未来启用 App Sandbox 是独立工程任务，须重新设计 security-scoped access 与辅助进程访问，不在首版草率勾上 entitlement。

### 11.3 隐私默认值

默认无遥测、不上传片库路径、NFO、图片或诊断包。联网只因用户查询/图片请求/网页浏览发生，来源由设置明确展示。

常规日志优先记录 jobId、阶段与错误码，避免完整记录简介、HTML、token、cookies；诊断详情可能涉及路径时提示用户。导出诊断包必须用户主动触发，并默认脱敏路径和内容。

清理缓存不触碰恢复记录、用户 sidecar 或视频。设置中区分“清除图片缓存”“清除最近目录”“打开日志目录”。

---

## 12. 性能、可访问性与本地化

### 12.1 性能目标

这些是待测目标，不是已达成指标：

- 窗口先出现，Worker 初始化不阻塞首屏；实际冷启动时间记录机器与构建配置。
- 1000 条合成任务的队列仍可流畅滚动、搜索和切换；避免每次输入重建全队列。
- 后台扫描、哈希、下载时输入和窗口拖动无明显冻结。
- 缩略图按需加载；建议初始内存缓存预算约 128 MiB，并按实际测量调整。
- 任务切换取消过期图片请求，不能出现上一部的图片覆盖当前影片。
- 不循环轮询磁盘/网络；空闲时不维持无意义的动画或高频 timer。

在线来源延迟与本地 UI 响应分别测量；不能将 mock 搜索速度宣传成真实站点性能。

### 12.2 可访问性

所有无文本图标提供可访问性标签和 tooltip。键盘能完成导入、选择任务、编辑、预览、确认与取消；状态不只依赖颜色。尊重系统深浅模式、减少动态效果和提高对比度等设置。

图片查看器不能吞掉整个应用的键盘导航。自定义图像区域也要暴露角色和说明。

### 12.3 语言

从第一阶段采用 String Catalog，不在视图里拼接中文句子。P0 完成简体中文与英文；完整首版补齐繁体中文与日文，保持 Windows 的四语言覆盖。[R02]

保留日期/时长等原始资料值，显示本地化与写出 NFO 规则分离。错误协议传稳定 reason，Swift 映射本地化提示；原始异常仅作为可展开诊断。

---

## 13. 构建、打包与分发

### 13.1 开发构建

新增可从仓库根目录运行的脚本，命名默认如下：

```bash
./scripts/macos/build.sh --arch arm64 --configuration Debug
./scripts/macos/test.sh --arch arm64
./scripts/macos/package.sh --arch arm64 --configuration Release --sign-mode adhoc
./scripts/macos/verify-package.sh <实际生成的-app-路径>
```

以上脚本在当前基线不存在，需要实现。脚本应检查 Xcode、SDK 和目标架构，失败给出明确提示并返回非零。不要自动改全局开发环境或偷偷安装依赖。

提交可打开的 `.xcodeproj` 和 shared scheme；不是只有零散 `.swift` 文件。记录实际验证的 Xcode/Swift 版本，不以“用了最新版本”代替可复现信息。

### 13.2 Worker 发布

从 `global.json` 读取 SDK，先提供对应 RID 的 self-contained、非裁剪、非 AOT、非 single-file 发布，降低首次移植风险。[A11]

```bash
dotnet publish JavMetaLite.Worker/JavMetaLite.Worker.csproj \
  -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishAot=false \
  -o artifacts/worker/osx-arm64
```

发布 x64 时单独构建 `osx-x64`，完整验证 Swift 前端和所有 native assets 的架构匹配。首版优先提供分架构包，不为了 Universal 2 阻塞交付，也不只把前端 `lipo` 后就宣称 Worker 已通用。

### 13.3 App bundle

将 Worker 及其全部依赖包装为可签名的嵌套 helper bundle，放在 App 的标准辅助代码位置。参考布局：

```text
JavMetaLite.app/
  Contents/
    Info.plist
    MacOS/JavMetaLite
    Resources/...
    Helpers/
      JavMetaLiteWorker.bundle/
        Contents/
          Info.plist
          MacOS/
            JavMetaLite.Worker
            JavMetaLite.Worker.dll
            *.runtimeconfig.json
            *.deps.json
            运行时与其他发布依赖...
```

这是建议布局，最终须以实际 `codesign`、启动和缺失依赖测试为准；不要仅凭目录长得正确就验收。App 以解析后的 bundle 绝对路径启动 helper，并验证路径仍在自身 bundle 内。

打包脚本检查可执行位、各 native 库架构和依赖位置。允许从带空格/中文的 App 路径、Finder、`/Applications` 以及无 SDK 的环境启动。

### 13.4 签名与公证

Debug / 自用测试可以使用明确标记的 ad-hoc 构建，不能把它称为正式签名发行版。

正式版由用户提供的 Developer ID 身份与安全存储的凭据签名、公证；凭据不入库。先签嵌套 native 代码及 helper，再签外层 App；不能依赖一条 `codesign --deep` 代替正确的签名顺序。

当前微软文档对非 Native AOT 的 .NET apphost 要求 `com.apple.security.cs.allow-jit`；该 entitlement 应落在真正运行 .NET 的 Worker 可执行文件上，不无差别加到所有 Swift 代码。其他权限按实测最小化添加，Release 不保留调试 entitlement。[A11]

采用 Apple 支持的 `notarytool` 流程提交、公证并 staple；完成签名验证、Gatekeeper 评估和实际启动检查。[A12] 没有凭据时提供可验证的本地包及明确的“未公证”状态，不为了打包指导用户关闭系统安全保护。

首版输出 `.app` ZIP；正式分发可再提供标准 DMG、SHA-256 和第三方许可证说明。生成包不等于自动上传或发布 GitHub Release。

---

## 14. 自动化测试与实机验收

### 14.1 保留 Windows 门禁

现有入口继续有效：

```powershell
./scripts/Test-Automated.ps1
./scripts/New-ReleasePackage.ps1
./scripts/Test-PortablePackage.ps1
```

目前 `Test-Automated.ps1` 通过 `dotnet run` 执行 smoke、事务、WPF UI 三个工程。[R17] 新增 Mac CI 不能替换原 Windows workflow；改动共享 Core 后 Windows CI 必须检查。

### 14.2 新增可移植门禁

建议沿用仓库的 console runner 风格：

```bash
dotnet build JavMetaLite.Core/JavMetaLite.Core.csproj -c Release
dotnet run --project JavMetaLite.PortableTests/JavMetaLite.PortableTests.csproj -c Release
./scripts/macos/test.sh --arch arm64
```

测试脚本实际执行 Swift 单元测试、必要 UI 测试与 Worker 集成测试，并保存结果。测试框架可根据现有结构调整，但文档必须给出正确命令，不能仅加一个不运行测试的工程。

### 14.3 最低测试矩阵

| 范围 | 必测情况 | 预期 |
|---|---|---|
| 导入 | 单文件、混合输入、目录递归、重复加入 | 稳定任务身份，无重复/漏报的假成功 |
| 识别 | dotted-prefix、CD1/CD2、confirmed multipart | 保留现有修复；不同文件不误合并 |
| NFO | 新建、编辑、未知 XML 节点/属性、原文件名 | 复用 round-trip；不清空未编辑数据 |
| NFO 安全 | 格式损坏、外部修改、只读、磁盘不足 | 阻止不安全保存，保留原件 |
| metadata | 空值、非标准日期/评分、演员结构 | DTO 不丢失数据 |
| 查询 | 成功、无结果、429、超时、部分来源失败 | 正确状态；不清空已有资料 |
| 重试 | 手动编辑后重试某来源 | 人工选择保留 |
| 图片 | 右半裁切、奇数宽度、JPEG/PNG、损坏、EXIF | 语义一致，可取消，无错误覆盖 |
| 样张 | 下载失败、完整替换、并发上限 | 未完成下载不触发不完整全量替换 |
| 预览 | 编辑后旧计划、文件外部变化、重复提交 | 失效/拒绝，不执行第二次写入 |
| 批量 | 输出冲突、read/write 冲突、共享 extrafanart | 写入前发现；保护跳过任务的输入 |
| 批量停止 | 第 2 部失败、取消、cleanup warning | 第 1 部保持完成；后续未开始 |
| 文件系统 | 大小写、Unicode、别名、symlink 循环 | 不误覆盖、不无限递归 |
| 事务 | 同路径 NFO、跨卷复制、目标视频已存在 | 永不覆盖已有视频；保留恢复能力 |
| 协议 | 分片读、连续多消息、转义换行、超大消息 | 正确 framing；可控失败 |
| 协议并发 | 进度插入、stderr 持续输出、乱序响应 | 无管道死锁、无响应串线 |
| 异常 | Worker 退出、预览后重启、保存中 EOF | 不自动重放写入；显示需检查事务 |
| UI | 中文输入法、Undo、快速切片、窄窗口 | 不丢字、不串图、不压坏布局 |
| 打包 | 未装 .NET、无开发 PATH、中文 App 路径 | 内置 Worker 正常启动与工作 |

NFO 解析需禁用外部实体和远程 DTD，避免导入文件触发意外联网或读取本地文件。NFO 测试样本使用虚构数据，未知节点/属性是否保留以原有 `NfoRoundTripWriter` 行为为基准，并新增跨语言流程回归。不要为了漂亮 DTO 重建一份丢字段的 XML。

### 14.4 实机项目单独列出

真实外接盘、SMB、卷拔出、权限弹窗、签名后的 Worker JIT、线上来源、网页验证、Jellyfin 扫描结果需单独验证。[R16]

在没有相应硬件/账号/凭据时标“未验证”，保留测试步骤；不得用本地目录假装完整验证真实断盘、SMB 或公证行为。生产可用范围只宣称已验证的部分。

UI 验收至少提供合成数据下的空状态、单片编辑、多片队列、图片页、预览、冲突/失败、设置截图；包含浅色与深色，并检查 13 英寸笔记本的可用窗口尺寸。

---

## 15. 里程碑与交付门槛

### M0 — 复核与施工准备

**实施：** 核对基线、读取仓库规则、完整扫描平台依赖、确认测试入口；输出 `AUDIT.md` 与简短 `ARCHITECTURE.md`，列出真实需要改的文件和项目引用。

**完成标准：** 当前 Windows 基线状态被准确记录；已有失败与本次引入失败分开；没有把“读了 README”称为完成 Core 审计。

**不要停在这里。** 能继续实现就进入 M1。

### M1 — 可移植 Core

**实施：** 图片处理接口与 Windows 适配；Core 改为 `net10.0`；共享编排提取；日志/设置路径注入；添加可移植测试入口和文件系统保守策略边界。

**完成标准：** Mac 能构建 Core 并运行非 UI 测试；Windows 保持原图片与保存行为；Core 无 WPF 引用。NFO-only 路径不依赖 Windows 图像库。

### M2 — Worker 与 App 真正连通

**实施：** Worker host、协议、初始化、任务导入/查询/编辑/预览/保存入口；Swift WorkerClient；打出能内置启动 Worker 的开发 `.app`。

**完成标准：** 原生 App 与真实 Worker 完成一次握手和读写临时 NFO 的集成测试；stderr 大量输出不挂死；退出无孤儿进程；没有依赖临时 shell 或机器绝对路径。

### M3 — 第一个真实可用闭环（第一次开工目标）

**实施：** 原生主窗口、队列、编辑器、保存 Inspector、预览 Sheet、基本设置；接入本地 NFO、现有图片展示、LibreDMM 查询、单片原位置 NFO 保存。

**必须演示：**

```text
选择一份合成视频与 NFO
→ 读到原值及未知节点
→ 查询/采用资料或手动编辑
→ ⌘S 显示真实变更
→ 确认保存
→ 重新读取验证结果
→ 原视频未变，NFO 未丢非编辑字段
```

没有线上网络时自动化可使用来源 fixture，但实际 provider 路径仍应已接入，且线上检查标未验证；不能发布固定假数据来源。

**完成标准：** 可运行 `.app`、源代码、测试结果和 UI 截图齐全。无“按钮 TODO 但界面完成”的交付。

### M4 — 日常功能与完整保存能力

**实施：** 递归导入、分段/重复处理、R18.dev/多来源、图片输出、批量保存与冲突预检、整理目录、跨卷保守复制和恢复记录。

**完成标准：** 文件安全矩阵通过；确认部分成功、cleanup warning、取消和恢复失败状态；主线程不执行大文件操作。

### M5 — 原生体验与完整首版功能

**实施：** WKWebView 手动导入、样张/本地图片管理、四语言、键盘/Undo/VoiceOver、深浅模式、窗口恢复、细节打磨和性能测量。

**完成标准：** 各主要工作流可用键盘完成；窄窗口可用；无输入法回填覆盖或跨任务串图；截图采用虚构数据。

### M6 — 发布候选

**实施：** 可复现构建、Mac CI、内置运行时包、许可证、包验证；凭据具备时签名/公证；Apple Silicon 实机验证；Intel 通过独立检查后再宣称支持。

**完成标准：** 下载/解压或安装后能从 Finder 启动；无 .NET 安装要求；文档准确说明兼容范围、已验证平台、已知限制。未经明确授权不发布线上 Release。

---

## 16. 给 Codex 的最终验收清单

### 产品与界面

- [ ] 主界面是真正 SwiftUI/AppKit，非网页或跨平台控件仿皮肤。
- [ ] 原生菜单、文件对话框、快捷键、焦点、Undo 与 IME 正常。
- [ ] 中央负责资料/图片，Inspector 负责输出/整理，信息不重复堆叠。
- [ ] 单片好用，多片自然扩展；没有无关 Dashboard 功能。
- [ ] 深浅模式、窄窗口和四语言的完成状态如实记录。

### 业务与安全

- [ ] Core 已真正移除 Windows UI 依赖。
- [ ] Windows 图片算法和测试未因移植被无意改变。
- [ ] 来源选择、手动编辑和本地 NFO 保护被保留。
- [ ] 所有磁盘写入经真实预览与事务入口。
- [ ] 旧 plan、重复提交、外部修改均有防护。
- [ ] 视频永不被覆盖；队列移除不等于磁盘删除。
- [ ] 批量读写冲突和共享 extrafanart 防混图规则已迁移。
- [ ] Mac 未用 `/` 路径根比较代替跨卷判断。
- [ ] 失败、取消、cleanup warning、恢复未完成有不同结果。

### 工程与交付

- [ ] Worker 协议有明确版本、DTO fixture、错误码与集成测试。
- [ ] stdout 无日志污染，stdout/stderr 不发生阻塞死锁。
- [ ] 未把 `.NET Runtime` 安装要求转嫁给最终用户。
- [ ] 原有 Windows CI/打包保留；新增 Mac 测试和构建入口。
- [ ] `.app` 可从 Finder 启动，支持带空格/中文的路径。
- [ ] 已执行测试与未执行测试分开记录；没有虚构成功。
- [ ] 所有新依赖版本锁定，许可证和发布说明同步更新。

---

## 17. 第一次交给 Codex 时可直接附上的指令

```text
请阅读并实施这份《JavMetaLite for macOS — 原生 Mac 实施规格与 Codex 开工任务书》。

路线已确定：SwiftUI + 必要的 AppKit；通过 stdio JSON-RPC 调用随 App 打包的
C# Worker；共享去 Windows 化的 Core；保留原 WPF Windows 客户端。

先核对当前仓库与文档基线及 AGENTS.md，不覆盖我的未提交修改。
直接开工，按 M0→M1→M2→M3 连续推进；不要只交付规划或静态界面。
这次优先交付真实的单片闭环：导入、本地 NFO、查询、编辑、预览、安全保存。

特别保留 MovieJob 的审核 revision、NFO round-trip 和文件事务保护。
注意 WPF 图片处理、仍在 App 层的 BatchSavePathConflicts，以及 Mac 不能用
Path.GetPathRoot 判断跨卷的问题。具体 UI、协议、测试和打包要求按文档执行。

普通细节按文档默认决策落地。每阶段运行可执行测试，更新
 docs/macos/STATUS.md。最后给出实际修改、运行方式、测试结果、截图/产物位置
以及尚未验证的项目。不要捏造 Mac 或 Windows 的实机验证结果。
```

---

## 18. 依据与进一步核对入口

### 仓库证据（固定提交，方便 Codex 对照）

下列链接对应本文核查基线，不代表开工时 `main` 永远停留在此提交。

- [R01 — 基线提交](https://github.com/Noredge/JavMetaLite/commit/874b2dd524106189806e4d0c86589a0c8ee142c1)
- [R02 — README](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/README.md)
- [R03 — global.json](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/global.json)
- [R04 — Core 工程](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/JavMetaLite.Core/JavMetaLite.Core.csproj)
- [R05 — PosterImageProcessor](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/JavMetaLite.Core/Services/PosterImageProcessor.cs)
- [R06 — OutputService](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/JavMetaLite.Core/Services/OutputService.cs)
- [R07 — OrganizationPathPlanner](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/JavMetaLite.Core/Services/OrganizationPathPlanner.cs)
- [R08 — FileOrganizationService](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/JavMetaLite.Core/Services/FileOrganizationService.cs)
- [R09 — MovieJob](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/JavMetaLite.Core/Services/MovieJob.cs)
- [R10 — BatchSavePathConflicts](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/JavMetaLite.App/BatchSavePathConflicts.cs)
- [R11 — BatchSaveCoordinator](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/JavMetaLite.Core/Services/BatchSaveCoordinator.cs)
- [R12 — MainWindow 编排](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/JavMetaLite.App/MainWindow.xaml.cs)
- [R13 — MovieMetadata](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/JavMetaLite.Core/Models/MovieMetadata.cs)
- [R14 — AppPreferencesStore](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/JavMetaLite.Core/Services/AppPreferencesStore.cs)
- [R15 — AppLog](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/JavMetaLite.Core/Services/AppLog.cs)
- [R16 — TESTING](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/TESTING.md)
- [R17 — Test-Automated.ps1](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/scripts/Test-Automated.ps1)
- [R18 — SmokeTests 工程](https://github.com/Noredge/JavMetaLite/blob/874b2dd524106189806e4d0c86589a0c8ee142c1/JavMetaLite.SmokeTests/JavMetaLite.SmokeTests.csproj)

### 平台官方资料

平台文档是 API 和分发要求的核对入口；本文的产品布局、分阶段范围、协议方法和交付门槛属于本项目设计决策。

- [A01 — .NET on macOS 支持与安装](https://learn.microsoft.com/en-us/dotnet/core/install/macos)
- [A02 — Path.GetPathRoot 与 Unix 路径根](https://learn.microsoft.com/en-us/dotnet/api/system.io.path.getpathroot?view=net-10.0)
- [A03 — Apple：Designing for macOS](https://developer.apple.com/design/human-interface-guidelines/designing-for-macos)
- [A04 — NavigationSplitView](https://developer.apple.com/documentation/swiftui/navigationsplitview)
- [A05 — SwiftUI Inspector](https://developer.apple.com/documentation/swiftui/view/inspector(ispresented:content:))
- [A06 — NSOpenPanel](https://developer.apple.com/documentation/appkit/nsopenpanel)
- [A07 — SkiaSharp macOS native assets 官方发布页](https://www.nuget.org/packages/SkiaSharp.NativeAssets.macOS)
- [A08 — Foundation URLResourceValues：卷标识与大小写能力](https://developer.apple.com/documentation/foundation/urlresourcevalues)
- [A09 — JSON-RPC 2.0 Specification](https://www.jsonrpc.org/specification)
- [A10 — WKWebView](https://developer.apple.com/documentation/webkit/wkwebview)
- [A11 — .NET：Publish apps for macOS，包括 JIT entitlement](https://learn.microsoft.com/en-us/dotnet/core/deploying/macos)
- [A12 — Apple：Notarizing macOS software before distribution](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)

> 最终标准：Windows 老用户不用担心被移植破坏；Mac 用户不用知道背后有 C#，打开后就能像使用一款认真打磨的 Mac App 一样完成工作。
