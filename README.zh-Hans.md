# JavMetaLite

<img src="JavMetaLite.App/Resources/Brand/JavMetaLite-64.png" width="64" alt="JavMetaLite icon">

**简体中文** · [繁體中文](README.zh-Hant.md) · [English](README.md) · [日本語](README.ja.md)

一次只整理一部影片的轻量 Windows metadata 编辑器。选择或拖入影片，搜索资料，逐项检查和修改，然后在保存前预览所有文件变更。JavMetaLite 不扫描媒体库，也不会在用户确认前写入或移动影片。

## 主要功能

- 只处理当前选择的一部影片，不提供批量刮削或媒体库扫描。
- LibreDMM 提供日文资料，R18.dev 提供英文资料，JAVLibrary 可作为手动网页后备。
- 多来源搜索后可为每个字段选择资料来源，也可继续手动修改。
- 读取并安全更新本地 NFO、poster 和 fanart，保留未知 XML。
- 生成 Jellyfin 兼容的 NFO、poster、fanart，以及可选的 `extrafanart/`。
- 可保持影片原位、在原地建立番号文件夹，或整理到自定义目标根目录。
- 跨磁盘或 UNC 目标使用安全复制与 SHA-256 校验，失败时回滚。
- 保存前默认显示实际变更预览；目标影片冲突时始终阻止执行。
- 便携、自包含的 Windows x64 单文件程序，无需安装 .NET Runtime。

## 快速开始

1. 从 [GitHub Releases](https://github.com/Noredge/JavMetaLite/releases) 下载 `JavMetaLite-v1.0.0-win-x64-portable.zip`。
2. 对照同一 Release 内的 `SHA256SUMS.txt` 校验压缩包，然后解压。
3. 运行 `JavMetaLite.exe`，选择或拖入一个影片。
4. 检查番号并搜索资料，选择合适的文字与封套来源。
5. 修改所需字段，选择输出和目标位置。
6. 检查保存前变更预览，确认后执行。

首次运行未签名程序时，Windows 可能显示 SmartScreen 提示。请只从本仓库的正式 Release 下载，并核对 SHA-256。

## 输出示例

```text
目标根目录/
  IPX-123/
    IPX-123.mp4
    IPX-123.nfo
    IPX-123-poster.jpg
    IPX-123-fanart.jpg
    extrafanart/       # 可选
      fanart1.jpg
      fanart2.jpg
```

## 资料来源

| 来源 | 主要用途 | 说明 |
| --- | --- | --- |
| LibreDMM | 日文资料、完整封套、样张 | 推荐的日文来源 |
| R18.dev | 英文资料、完整封套、Gallery | 英文输出与辅助来源 |
| JAVLibrary | 手动网页导入 | 网站要求验证或自动来源失败时使用 |

来源网站可能变更或暂时不可用。多来源搜索对每个来源设置等待上限；失败时可以切换来源或手动填写，不需要反复重启程序。

## 安全设计

- 默认不移动影片，也不直接覆盖 metadata。
- 预览窗口显示即将新建、更新、移动或保持不变的文件。
- 影片目标已存在时不会覆盖另一个影片。
- 跨卷传输在删除来源前校验文件大小与 SHA-256。
- 提交失败时恢复已覆盖的 metadata，并尽量保持原影片位置。
- 搜索时会把识别出的番号发送给用户选择的资料来源。选择影片和读取本地 NFO 不会自动联网写入。
- 手动导入 JAVLibrary 时只读取当前影片页面；内置 WebView2 浏览器可能保留网站验证所需的 Cookie。

任何文件整理工具都不能替代备份。请先备份重要影片，并在第一次使用自定义目标位置时使用测试副本。

## 系统要求与边界

- Windows 10/11 x64。
- 首次运行跟随 Windows 的简中、繁中、英文或日文显示语言；其他系统语言回退到英文，之后记住用户选择。
- 内置浏览器需要 Microsoft Edge WebView2 Runtime；Windows 10/11 通常已经安装。
- 支持 MP4、MKV、AVI、WMV 影片选择；不会写入容器内部 metadata。
- 不扫描媒体库，不批量处理，不自动搬运未知字幕或伴随文件。
- `actors/` 暂不生成；演员图片通过 NFO 内的远程 `thumb` 提供。
- 真实网络共享的速度、权限和可用性取决于 Windows 与目标服务器。
- 请遵守资料来源网站的使用条款，并仅以合理频率查询。
- 资料来源可能包含成人内容。请仅在当地法律允许且符合用户年龄的情况下使用。

运行日志位于 `%LOCALAPPDATA%\JavMetaLite\Logs`，默认保留最近 14 天。用户偏好位于 `%LOCALAPPDATA%\JavMetaLite\settings.json`。

## 开发与测试

需要 .NET 10 SDK：

```powershell
dotnet build .\JavMetaLite.App\JavMetaLite.App.csproj
.\scripts\Test-Automated.ps1
```

生成干净的 Windows x64 便携包与 SHA-256：

```powershell
.\scripts\New-ReleasePackage.ps1
```

自动化测试层级见 [TESTING.md](TESTING.md)，版本历史见 [CHANGELOG.md](CHANGELOG.md)。

## 许可证

JavMetaLite 采用 [MIT License](LICENSE)，版权所有 © 2026 Noredge。第三方组件使用各自的许可证，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

JavMetaLite 与其读取的资料来源网站没有隶属或合作关系。本项目的 MIT 许可证不代表对来源网站数据的再授权。
