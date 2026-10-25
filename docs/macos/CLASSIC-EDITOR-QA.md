# 经典编辑器：build28 交付与原生验收

2026-10-23，America/Los_Angeles。最终交付为 0.4.0 / build28 / arm64 / ad-hoc 签名；按用户要求不公证。工作树从 HEAD `874b2dd` 上已有的未提交迁移实现继续，未 reset、commit 或 push，未操作正在运行的用户 build24 会话。

## 实施

统一原生工具栏提供添加、按明确范围查询及其菜单、预览保存和右栏开关。主页面同时显示审核选中来源的海报与完整封套，图片页仍保留双图。在线横图的海报缩略预览按 Core 的原始方向右半裁切；本地海报及完整封套保留原图。图片读取继续使用 Worker 登记来源、revision/generation 校验、取消和有界缓存，不直接读任意网络地址。

资料字段直接展开；原生文本会话、Undo、marked-text 保护、候选对比与只读保留字段继续使用原实现。队列“全选”直接进入带双图缩略预览的批量概览，队列及概览立即显示草稿标题。操作范围仍为导航多选、独立勾选、当前影片的既有规则，数量显示在工具栏和右栏。清空仍需确认且仅移除当前会话。

右栏提供输出预设；视频重命名、NFO 标题前加番号及附属文件覆盖合并为一个“更多选项”。三个选项没有常驻解释，使用原生 `.help`；实际 AX Help 内容已核对，未将辅助功能树读取冒充完整 VoiceOver 或悬停延时测量。四语言补齐 13 个键。查询按钮显式提供随语言和数量更新的辅助功能名称。

Core、Worker、Windows 及事务相关构建输入对照开工快照保持 hash/mode 不变。MovieJob revision、NFO round-trip、全队列/批量冲突预检、Worker 计划 token 与指纹、事务/journal 均保留。Mac 前端不直接写 NFO。

## 自动测试与正式包

- `classic28-final-gate.log`：10 套 exit 0。412 keys、1678 本地化运行检查，发布工具 36，manifest 15，Core 50 项/559 断言，Swift 48（18 + 30），协议 63（49 生产 + 14 固定 provider 宿主），队列 4，保留策略 5。
- 两项新增回归验证审核双图来源隔离/缺图回退，以及合成 401×200 图的右半裁切、像素、完整图缓存隔离和源文件保护。初次测试宏编译错误及颜色夹具失败的日志保留；修正的是测试写法/像素夹具，不隐藏产品失败。
- `classic28-package.log`：17 native 文件的签名、entitlements、依赖与四语资源验证通过，无 SDK 环境下 Worker 初始化/快照/退出通过；正式 ZIP 解包后生产协议 49、队列 4、保留策略 5 通过。
- 283 项输入 digest：`3b243ff62fffa56e947d56d42565c78f893138edf54a9efd05526717de97a56f`。交付 App 全部 payload 的 hash/mode/签名属性与正式打包 App 一致。
- ZIP SHA256：`fcdbd627cf1aaadddd6a1a41f4532b1090174c544f242bf7567b90c8a60f2316`。

## 原生操作与文件回读

原生验收采用独立 QA bundle 身份，保留用户原 App。仅使用任务内合成影片/NFO/图片与独立 `/private/tmp` 数据根。QA helper 增加一个仅传递 `--data-root` 的原生 argv shim；没有替换 provider、Core 或生产 Worker。最终 QA28 与交付28去签名前端代码 SHA 相同：`1374550fa44e7c10abda016062c4dcc36047046ab891c18cae89dc0d57ec628f`，Worker/Core DLL 也相同；证据为 `native28-final-shipping-binding.json`。QA bundle 不交付，签名身份与根配置的区别仍保留。

| 实际构建/范围 | 观察及回读 |
| --- | --- |
| QA26 单片 | 发现确认导入3片，Unicode 粘贴/Undo/Redo；覆盖关闭时预览阻断，取消保留草稿，显式允许后保存1片。13文件仅001 NFO改变，其他12文件 SHA/bytes/mtime不变，原始标题、未知XML、root属性、演员扩展与resume保留。 |
| QA26 批量 | 两片导航多选，预览为001全部Keep、002一项NFO Update及3 Keep；完成2片。相对最初基线恰好2 NFO改变，其余11文件及未选第三片不变。 |
| QA27 布局/草稿 | 四语言最小窗口单片截图09–12；系统四分之一布局产生1842×1346像素窗口截图，最小内容策略为920×620。主操作、双图和展开的更多选项可见；图片页仍保留双图。三片概览在提交前显示“第一片草稿”，修复旧标题显示。 |
| QA27 保存 | 两片均编辑，覆盖关闭时阻断、取消后保留；输出预设真实勾选NFO/海报/完整封套，本地图片仍按计划Keep。完成2片，仅2 NFO变化，第三片和全部视频/图片不变。现有 writer 同时补入 canonical uniqueid；不能声称XML仅改标题，其他字段语义与原始标题/未知XML/actor/resume均保留。 |
| 最终 QA28 | 独立新副本发现确认3片；英语查询名称从0变1，切中文后从当前1变选中2，AX名称与Help一致。Unicode粘贴、Undo回原值、Redo恢复新标题；队列和批量概览立即显示新草稿。 |
| 最终 QA28 保存/清空 | 两片预览在覆盖关闭时禁用确认；取消、开启覆盖、重新预览后为1 NFO Update + 7 Keep，结果2完成。关闭结果后可立即操作，主页面海报可打开原生查看器并关闭。最终最小窗口/恢复正常尺寸留图；清空先Esc保留3，再明确确认3/0未保存归零，正常退出。 |
| 最终 QA28 回读 | 13文件仅001 NFO改变，标题精确为“DEMO-001 · 经典编辑器 · build28 安全保存”；其他12文件 SHA/bytes/mtime完全不变，无新增/删除，第三片全部不变。和执行前副本相比，全部非title XML语义不变。清空后文件仍全部存在。QA App/Worker均已退出。 |

工具的 `typeText` 注入中文时最初仅录入中点，随后改用原生剪贴板粘贴并核对完整中文字符串；这不是物理输入法测试或产品 IME 失败的证据。最早锁屏期间没有原生验收，解锁后才执行以上操作。

## 产物和截图

会话输出根：`outputs/Mac-classic-editor-2026-10-24/`。其中 `JavMetaLite.app` 和 `JavMetaLite-macOS-arm64-Release-adhoc.zip` 为最终28；原25 App/ZIP移入 `history/build25/`，不覆盖历史证据。日志、基线、回读、来源绑定与汇总在 `results/`，最终门禁逐套日志在 `results/full-gate28/`。

截图根为输出目录中的 `screenshots/`：

- 00：初次25工具栏缺陷；01–08：26单片、覆盖阻断、预览、批量与最小窗口历史。
- 09–12：27英语/日语/繁中/简中最小单片；13实时草稿批量；14–15批量保存；16图片页双图。
- **17、22：最终28单片主页面；18：最终28批量双图与范围；19–20：最终28预览/结果；21：最终28最小窗口；23：最终28确认清空后的空队列。**

截图是原生 QA 窗口，没有用图示或旧截图冒充最终28。四语言完整布局矩阵并未在28全部重跑；28只改查询辅助功能标签，实际复验英语/简中名称与数量、简中正常/最小及业务闭环；其四语资源逐项与27一致，仍不把27截图改名为28。

## 运行与边界

退出旧版后打开交付 `JavMetaLite.app`，添加影片并主动查询；导入不自动联网。源码运行：`DOTNET=/absolute/path/to/pinned/dotnet ./scripts/macos/build.sh --arch arm64 --configuration Release --sign-mode adhoc`；门禁：同一环境运行 `./scripts/macos/test.sh --arch arm64 --configuration Release`。

没有执行公证。本机没有有效 Developer ID 身份，ad-hoc 仅用于本地测试，不代表 Developer ID 分发或 Gatekeeper 接受。物理中文/日文 IME、完整 VoiceOver、真实外接卷/SMB/断电/满盘、Intel/macOS15、Windows实机、干净Mac和长期稳定继续未验证。未进行本轮live provider/网页验证；JAVLibrary Cloudflare按用户要求保留手动验证。build24负几何日志历史不因本轮界面通过而宣称已根除。原40项矩阵仍按各次证据保留，不将本次有限UI验收扩展为40项全通过。
