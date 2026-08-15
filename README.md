# JAV Metadata Lite

一个只处理单个影片的轻量 Windows metadata 工具。它不会扫描整个媒体库，也不会移动或重命名影片。

## v0.3.0-r4 已实现

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
- 在影片旁边生成同名 `.nfo`
- 优先使用 DMM/FANZA 高清横版原图，而不是低清 `ps.jpg`
- 自动截取横版原图右半部分，生成 `<影片名>-poster.jpg`
- 使用作品完整横版封套生成 `<影片名>-fanart.jpg`
- 可选把所有有效 Sample Images 保存到 `extrafanart/`
- NFO 同时写入本地 poster 与 fanart 文件名
- 左侧同时预览 poster 与真正的横版 fanart
- 默认拒绝覆盖已有 NFO 或封面
- 未勾选“允许覆盖”时，如检测到重复输出，会弹窗询问是否仅本次覆盖
- 不修改影片本身
- 修复网站验证时内置浏览器没有打开的问题
- 修复网页导入后远程封面触发 `This Freezable cannot be frozen.` 的问题
- 修复来源选择框下拉列表过白、文字难以辨认的问题

## 开发构建

需要 .NET 10 SDK：

```powershell
dotnet build .\JavMetaLite.App\JavMetaLite.App.csproj
dotnet run --project .\JavMetaLite.App\JavMetaLite.App.csproj
```

运行不依赖测试框架的 smoke tests：

```powershell
dotnet run --project .\JavMetaLite.SmokeTests\JavMetaLite.SmokeTests.csproj
dotnet run --project .\JavMetaLite.UiSmokeTests\JavMetaLite.UiSmokeTests.csproj
```

发布 Windows x64 单文件版本：

```powershell
dotnet publish .\JavMetaLite.App\JavMetaLite.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 输出示例

```text
IPX-123.mp4
IPX-123.nfo
IPX-123-poster.jpg
IPX-123-fanart.jpg
extrafanart/          # 勾选“保存全部剧照”时生成
  fanart1.jpg
  fanart2.jpg
```

## 当前边界

- v0.3 只生成伴随文件，不移动、重命名或写入 MP4/MKV 容器。
- `actors/` 暂不生成；演员图片以 NFO 内的远程 `thumb` 提供，`extrafanart/` 是可选输出。
- 网站结构变化可能需要更新 `JavLibraryClient`。
- 内置浏览器依赖 Microsoft Edge WebView2 Runtime；Windows 10/11 通常已经安装。
- 请遵守资料来源网站的使用条款，仅以合理频率查询。
