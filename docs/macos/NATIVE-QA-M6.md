# M6 原生验收记录

记录日期：2026-10-22，America/Los_Angeles。版本：App 0.4.0 / build 4，Worker 0.4.0-dev。主机为 Apple M5、16 GiB、macOS 27.0.1，使用 Swift 6.4 Command Line Tools；完整 Xcode 未安装。

本记录分别标明本机 GUI 操作、合成文件回读、自动化测试及未验证事项。GUI 操作由本任务的主代理通过本机界面完成；本文件据操作记录、截图和测试日志整理。使用的影片、图片、NFO 均为虚构测试资料。本地 ad-hoc 签名、Finder 启动和命令合同测试不等于 Developer ID、公证或下载后的 Gatekeeper 验收。

## 候选边界

| 候选 | 已有证据 | 本文范围 |
| --- | --- | --- |
| Candidate 1 | 从 `work/M6 解包 验收/JavMetaLite.app` 经 Finder 启动；原生编辑/预览/保存、晚到 NFO 拒绝、小窗口及日语结果刷新；截图 01–10 | 下列实际 GUI 观察均属于此候选 |
| Candidate 2（当前最终候选） | 四语言 SOURCE_CHANGED 提示修正；重新打包及解包后 Worker 40/40；实际本地启动、同一最小窗口恢复、已保存 NFO 回读、晚到 NFO 拒绝、日语/简体中文提示、正常退出；截图 11–14 | 最终候选复核范围见下一节；不把 candidate 1 的完整保存操作写成 candidate 2 重做 |

最终 candidate 2 App ZIP SHA-256：`ac736a52265e9be67a0dbeeb1ebd4ba50a54f84b2740e799d9b99c96f62986fe`。实际包 manifest 与本次记录一致。

准确的最终 ZIP、源码导出和交付文件清单由 STATUS.md 及交付 manifest 记录。

## Candidate 2 最终本机复核

- 从 `work/M6 最终 解包/JavMetaLite.app` 本地启动，恢复 candidate 1 的同一最小窗口；截图 11。此观察只覆盖本机同一显示器的恢复。
- 导入四部测试影片，读回 DEMO-001 先前已保存的标题；最终界面回读见截图 14。这次复核没有重复 candidate 1 的整段编辑/覆写保存操作。
- DEMO-005 原先无 NFO，导入后外部新建 `movie.nfo`。Cmd-S 被 SOURCE_CHANGED 拒绝，新增文件保持其基线内容；截图 12、13 分别记录日语与简体中文修正后的重新导入提示。独立哈希基线为 `work/m6-ui-fixtures/late-final-nfo-sha256.json`。
- 完成后语言恢复简体中文、外观恢复浅色。正常 Cmd-Q 后没有残留 App/Worker 进程。

**当前恢复旧 NFO 基线的原生操作：先确认并妥善保存或另行记录未存编辑，退出 App，再重新启动并导入。** 同一会话再次导入同一路径会执行去重，不会刷新已有任务的 NFO 基线。只重复导入或只生成新预览，会继续得到 SOURCE_CHANGED；不要用反复确认覆写绕过这个保护。尚未保存的内存编辑不能假定会跨重启保留。

## Candidate 1 实际 GUI 与磁盘结果

1. 经 Finder 打开中文/空格路径内解包后的 App，观察空工作区，再导入测试影片。这里证明本机解包后的 App 可启动，不是浏览器下载、另一台无 SDK Mac 或在线公证验收。
2. 编辑 DEMO-001，执行 Cmd-Z 与 Shift-Cmd-Z，观察 Undo/Redo 恢复字段值。未使用实体中文/日文输入法验收组合输入。
3. 覆写关闭时打开保存预览，现存 NFO 替换被拦截。回读最初 13 个测试文件的 SHA-256，全部与导入前一致。
4. 显式开启覆写，再生成新预览并确认保存 DEMO-001。只改变该影片的 `movie.nfo`；另外 12 个原文件逐字节一致，包括所有视频、图片及其他影片资料。回读保留未知 XML、actor 扩展、resume 和原始 `N/A` 值。覆写许可只对应这次已加载的 round-trip 基线。
5. DEMO-004 导入时没有 NFO；随后由测试准备步骤新建 `movie.nfo`，再从 App 尝试预览。Worker 返回 SOURCE_CHANGED，新增 NFO 未被覆盖。Candidate 1 的提示没有充分说明必须重新导入，因此 candidate 2 将四语言提示改为“先重新导入，再生成新预览”。
6. 使用系统 Window → Move & Resize → Left / Top Left，把窗口调整至约 921×673（外框），内容约 920×620。实际滚动元数据区域和图库，观察顶部/底部及图片。此结果补充 M5 未成功改变窗口尺寸的记录；不外推为多显示器或所有尺寸验收。
7. 切换到日语和 Dark 外观后，已完成的导入摘要从中文转为日语，仍保留“新导入 0 / 已存在 3”的计数；窄窗口内观察状态和图库。

原始哈希基线为 `work/m6-ui-fixtures/before-sha256.json`（13 项）；晚到 NFO 的独立基线为 `work/m6-ui-fixtures/late-nfo-sha256.json`。上述路径在任务工作区，交付副本及最终核对记录由 STATUS.md/evidence 清单定位。截图仅证明对应可见界面；字节不变与未知 XML 保留来自另行文件回读。

## 截图对应

截图位于交付目录 `outputs/M6/screenshots/`。01–10 为 candidate 1，11–14 为最终 candidate 2；Undo/Redo 和完整哈希回读是操作/检查记录，不由单张截图单独证明。

| 文件 | 可见内容 |
| --- | --- |
| `01-finder-launched-empty.jpg` | Finder 启动后的空工作区 |
| `02-overwrite-blocked.jpg` | 未授权覆写时的预览拦截 |
| `03-save-preview.jpg` | 明确同意覆写后重新生成的保存预览 |
| `04-saved-zh-Hans.jpg` | 简体中文保存完成界面 |
| `05-late-nfo-protected.jpg` | 导入后新出现 NFO 的 SOURCE_CHANGED 拒绝 |
| `06-narrow-window.jpg` | 系统窗口操作后的窄窗口 |
| `07-minimum-window-top.jpg` | 约最小内容尺寸下的上部字段 |
| `08-minimum-window-bottom.jpg` | 滚动到下部字段 |
| `09-japanese-minimum-status.jpg` | 日语/Dark、小窗口与保留计数的完成摘要 |
| `10-japanese-minimum-gallery.jpg` | 日语/Dark、小窗口图库 |
| `11-final-restored-minimum.jpg` | 最终 candidate 2 本地启动并恢复同一最小窗口 |
| `12-final-late-nfo-protected-ja.jpg` | 最终 candidate 2 日语 SOURCE_CHANGED 与重新导入提示 |
| `13-final-late-nfo-protected-zh-Hans.jpg` | 最终 candidate 2 简体中文 SOURCE_CHANGED 与重新导入提示 |
| `14-final-loaded-nfo.jpg` | 最终 candidate 2 读回 DEMO-001 已保存 NFO |

## 自动化门禁与首次失败记录

| 检查 | 已读日志中的结果 | 证据文件 |
| --- | --- | --- |
| Portable Core Release | 41/41，252 assertions | `artifacts/test-results/macos-arm64/portable.log` |
| Swift Release | 20/20：13 WorkerKit + 7 NativeBridgeKit | `artifacts/test-results/macos-arm64/swift.log` |
| Worker 真实进程 | 40/40 | `artifacts/test-results/macos-arm64/protocol.log` |
| 四语言 | 309 keys × 4；1,266 runtime checks；candidate 2 提示修改后也重跑通过 | `artifacts/test-results/macos-arm64/localization.log`、`artifacts/test-results/m6-source-changed-localization.log` |
| 发布命令合同 | 30/30 | `artifacts/test-results/macos-arm64/release-tools.log` |
| 发布 manifest | 15/15 | `artifacts/test-results/macos-arm64/release-manifest.log` |
| Windows 交叉编译 | App、SmokeTests、RegressionTests、UiSmokeTests 全部 exit 0 | `artifacts/test-results/windows-crossbuild-results.tsv`、`windows-crossbuild.log` |
| Candidate 1 解包后协议 | 40/40，9.997 s | `artifacts/test-results/macos-arm64/package-m6-candidate1.log` |
| Candidate 2 解包后协议 | 40/40，10.827 s；ad-hoc package 完成 | `artifacts/test-results/macos-arm64/package-m6.log` |

第一次 M6 package 并非全部通过。`package-m6-initial.log` 中 test 35 失败，断言为 `2 != 3: Canceled slot waiter must not start a request`。原因是测试同时提交三个 Task.Run 操作，再假定第三个一定在等候；调度不保证 FIFO，取消顺序可能先释放活跃槽位，使真正的等候者开始请求。

测试修正为先提交两个操作，等待本地 HTTP 服务确认两个槽位均占用，再提交已知的第三个等候者，先取消该等候者再取消活跃请求。生产并发/取消实现未因这一失败改变。修正后 test 35 独立连续五次 exit 0，日志为 `artifacts/test-results/m6-release-preparation/artwork-cancellation-repeat.log`；之后 candidate 1 和 candidate 2 解包后的完整 40 项协议测试均通过。首次失败日志保留，不把后续重跑写成首次成功。

30 项发布合同测试使用离线 Apple/Developer ID 夹具，验证无签名降级、路径范围、上传中断恢复、日志 ID/SHA 绑定和最终 ZIP 解包验证顺序。它们没有产生真实 Apple Accepted、公证票据或正式签名。本机报告 0 valid code-signing identities；readiness 的 `localPackageVerified=true`、`signMode=adhoc`、`distributionReady=false`，凭据未验证。正式分发仍未通过。

最终无 `.git` 源码导出在中文/空格路径 `work/M6 最终 源码 重建/JavMetaLite` 完成真实 locked restore、Swift Release 构建、ad-hoc 签名、包验证和无 SDK Worker 握手，exit 0。源码重建 inputDigest 为 `00f86a936eb8d8c78e608d2838cb31405fe3c971394b5220d5e85aa8aadb308f`，与最终包 manifest 相同。构建日志为 `artifacts/test-results/m6-release-preparation/source-export-final-build.log`。输入一致和真实重建不等于签名二进制/ZIP 跨机器逐字节一致。

## 性能范围

`artifacts/test-results/m6-worker-performance.json` 记录最终 Worker 对同一平面目录内 1,000 个微小合成视频的一次本机测量：导入 22,995.94 ms，包含全队列输入保护的单片预览 1,450.43 ms，snapshot 中位数 4.32 ms，确认前没有媒体写入。优化只减少一次 fresh sidecar lookup 内的重复路径处理，没有跨操作缓存目录状态，没有改变导入去重或降低事务检查。

这是 Worker 描述性测量，不能当成 1,000 行原生滚动/搜索/切换或持续内存验收。Core 同步计划构建仍可能延迟 Worker 的取消响应；不在原生 UI 线程运行。

## 仍未验证

- 实体中文/日文 IME、完整 VoiceOver 朗读与交互、全流程纯键盘验收，以及专业日语/繁体中文语言校对。
- Intel、最低 macOS 15、Windows 实机运行/UI/图片/打包；交叉编译只证明可构建。
- 外置卷真实跨卷、case-sensitive APFS、硬链接、SMB、拔盘/重挂载、TCC 撤销、磁盘满、真实 I/O 与断电恢复。
- 1,000 行原生 UI、持续内存、多显示器断接/恢复和其他窗口几何。
- 线上 provider、真实验证页面/会话、远程图片可用性和 Jellyfin 扫描。
- 完整 Xcode/XCUI、远程 GitHub CI 实际运行、Developer ID 签名/JIT、Apple 公证、浏览器下载后的 Gatekeeper/首次离线启动、另一台干净 Mac。

文件安全依靠预览、批次与单项提交前的多次路径/指纹复验及事务保护，不声称对其他未协调进程的任意最后瞬间修改提供原子文件系统快照，也不声称跨崩溃 exactly-once。
