# JavMetaLite 1.2.0 定稿与交付记录

日期：2026-09-05。用户已完成 preview.56 测试并批准定稿，随后明确授权更新 GitHub main 和 Release 便携包。

## 定稿范围

以通过最终审查修复与用户回验的 preview.56 为功能基线。仅调整版本身份、正式版底栏和交付材料，不追加功能或修改保存／搜索行为。正式版四语言底栏显示 `v1.2.0`；后续预览版仍显示对应语言的预览标记，已补回归。

## 正式版本验证

以下均针对 1.2.0 重新执行，不复用 preview.56 的包或哈希。

| 检查 | 结果 |
| --- | --- |
| 完整自动化 | Core 46/46、文件事务 43/43、WPF 完整冒烟、发布脚本与完整许可说明验证通过 |
| 版本显示回归 | 正式／预览版、构建元数据、四语言切换与程序集版本一致性通过 |
| 便携包独立解压 | 4 个预期文件；产品／文件版本、EXE、ZIP 校验和与打包目录一致 |
| 许可材料 | 86,885 字节；包内与源文件一致，第三方组件和完整许可内容门禁通过 |
| 四语言阅读材料 | README 各 47 行；相对链接、图片存在性与定稿措辞核对通过 |
| 正式截图 | 8 张四语言单片／多片、2 张查看器重新渲染并逐张目视核对；均使用合成数据 |

验证命令：`scripts/Test-Automated.ps1 -DotNet ./.dotnet-sdk/dotnet.exe -Configuration Release -NoRestore -BuildOutputDirectory bin/release12-final/`。

本地证据：完整门禁（`artifacts/release12-final/final-gate.log`）、包校验（`artifacts/release12-final/portable-validation.json`）、文档检查（`artifacts/release12-final/document-validation.json`）、[截图来源与清单](SCREENSHOTS-v1.2.0.md)。`artifacts/` 与 `release/` 被 Git 忽略，仅在当前工作区可用；本记录中的结果与哈希供远程阅读，不要求下载这些验证目录。

## 本地定稿包身份

- 文件：JavMetaLite-v1.2.0-win-x64-portable.zip（`release/JavMetaLite-v1.2.0-win-x64-portable.zip`）。
- 大小：60,472,877 字节。
- 产品版本：`1.2.0`；文件版本：`1.2.0.0`。
- ZIP SHA-256：`41A20F6E55AA0BB3F85E099719DC5975D14F447D9DFCE52FE8730569274A2E45`。
- EXE SHA-256：`F775D2E5F599955A9387C70F9A1589C80DF185EE7A45A44C6C386BBED5600A11`。
- 包内：`JavMetaLite.exe`、`README.txt`、`LICENSE.txt`、`THIRD_PARTY_NOTICES.txt`；单包及汇总 SHA256SUMS 已核对。

本地正式包已经自动启动冒烟：主窗口出现并响应，未载入影片，用户设置哈希未改变。检查结束仅停止该次空闲测试进程，避免正常关闭时写入偏好。该项不是人工操作验收；不同 DPI、真实跨盘／UNC、网站与 WebView2 等未逐项确认的环境不补记为通过。

上述哈希仅标识本地定稿包；GitHub CI 在干净 Windows runner 重新构建，最终 Release 采用通过 CI 校验的那一份 ZIP，以其配套 SHA256SUMS 为准，不套用本地构建哈希。

## GitHub 初次只读连接检查（历史快照）

- `origin`：`https://github.com/Noredge/JavMetaLite.git`；当前认证账号 `Noredge`，仓库权限 `ADMIN`。
- 默认分支 `main`；远端 HEAD／main：`388fffb9dfaa70870d7cdcd254ca19f6b8ca1484`。
- 当前本地分支 `preview42-artwork-comparison` 未绑定上游；本地及远端均未发现 `v1.2.0` 标签。
- 当前 Git 的 HTTPS 辅助程序目录与默认 exec-path 不一致；单次命令指定已有辅助程序目录后，`ls-remote` 成功。未修改配置，也未通过写入操作试探权限。
- 未执行 fetch、pull、commit、tag、push 或创建 Release。以上远端状态为检查时快照，不保证后续不变。

## GitHub 交付流程

- 用户已授权更新 main 与 v1.2.0 Release。先读取最新 main，采用普通非强制推送；CI 成功后才发布同一提交的便携包与校验文件。
- 原开发历史含两张真实本地封套截图；原分支和文件保留在本地，main 使用基于远端 main 的干净交付提交，不将私人截图带入新提交或祖先历史。旧合成迭代图仅保留必要的两项状态示例。
- CI 固定 SDK 10.0.400，并执行完整门禁、真实打包与独立 ZIP 校验；成功产物保留 7 天供下载验证。新增校验脚本的 6 种损坏包负例均被拒绝。
- 发布时核对 CI 提交、下载包 SHA-256、四文件、EXE 身份与许可；创建新 v1.2.0 标签／Release，不覆盖旧版本。
- 本文记录已完成的本地检查和获准流程，不提前声明远端执行结果。最终状态见 [CI](https://github.com/Noredge/JavMetaLite/actions/workflows/ci.yml) 和 [Releases](https://github.com/Noredge/JavMetaLite/releases)。保留旧包、SDK 及失败恢复目录，不启动新功能开发。
