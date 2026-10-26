# macOS 发布验收清单

此清单区分本地候选包、正式分发和需要单独实机验收的项目。精确候选版本、产物 SHA-256、执行命令及结果统一以 STATUS.md 和随包 evidence 为准。勾选“自动化通过”不表示相关硬件、账号或真人交互已经验收。

当前用户选择为**只签名，不公证**，当前交付目录为 `outputs/M6-artwork-batch-live`；先前 `outputs/M6` 的已实测记录继续保留。此轮不执行公证上传、staple 或请求公证凭据。线上来源的新增实际测试由本轮 STATUS/evidence 单独记录，不提前把下方未验收项勾为完成。

## 本地候选包门禁

- [x] 保留既有未提交源码与 Windows 发布入口；开工前完整源码快照可回查。
- [x] 原生 App + stdio Worker + portable Core；用户无需安装 .NET。
- [x] 已有依赖固定版本；按 RID 锁定 NuGet restore，运行时完整许可证随包。
- [x] 源码输入、工具链、依赖、参数写入 build provenance；实际 payload/ZIP 另有哈希清单。
- [x] 源码 ZIP 含无 `.git` 场景可验证的导出 manifest；有意编辑必须明确记录。
- [x] ad-hoc 与 Developer ID 模式明确区分；正式签名凭据缺失、Team 不符时失败，绝不自动降级。
- [x] 签名顺序为嵌套 payload → Worker/helper → 外层 App；签名不用 `--deep`，验证使用它。
- [x] Developer ID 策略仅 Worker 开启 allow-jit，无调试权限；真实签名/JIT 仍待有效身份验收。
- [x] M6 本地门禁、无 .git 中文路径源码实际重建、中文路径 ZIP 解包与 Finder 启动已执行；准确候选与输入摘要见 STATUS。

## 业务与文件保护

- [x] stale revision/plan、重复执行、外部修改、批量读写冲突、共享样张保护有执行回归。
- [x] NFO round-trip、未知 XML/原始值保留和原视频不变有合成文件回读。
- [x] 图片系统废纸篓单独确认；取消/失败不转为永久删除，租约与审核 revision 不混用。
- [x] macOS 视频整理采用完整 SHA-256 保守复制，不以 Path.GetPathRoot 推断跨卷。
- [x] 晚到/外部修改 NFO 和登记路径重定向：Worker 41 项通过；原生晚到 NFO 拒绝且哈希未变。
- [ ] 外接卷/SMB/断盘/权限撤销/磁盘满/电源故障：未以普通本地目录代替验收。

## 签名政策与 Developer ID：分别验收

- [x] 默认 `--policy signed-only`；`signatureReady`、公证、Gatekeeper 实际结果分别报告，36 项发布命令/政策合同测试通过。
- [x] 0 个有效身份时可完成 ad-hoc 本地签名和完整性验证；没有冒充 Developer ID 或系统信任通过。
- [ ] 有效 Developer ID Application 身份和匹配 Team；本机当前 0 valid identities。
- [ ] 所有 native code 同 Team、secure timestamp/hardened runtime 正确；真实 Developer ID Worker JIT 正常。

`signed-only` 的 `distributionReady` 仍要求真实 Developer ID 包验证通过；未要求公证不等于该条件已满足。不带 `--require-distribution` 时，退出码 0 只表示本地包验证通过；带该参数才要求所选分发政策通过。

## 可选公证：不属于当前范围，尚未完成

保留下列旧政策供将来另行授权时使用；未勾选项目不是当前只签名政策的公证待办，也不代表已通过。选择 `--policy notarized` 时才把真实 Developer ID、stapled ticket 与 Gatekeeper 同时作为分发就绪条件。

- [ ] 持证人在安全 Keychain 中配置公证 profile；凭据不入库、不进日志。
- [ ] 对准确的分发 ZIP 显式执行 notarytool submit，记录 ID/SHA；`In Progress` 不能当成通过。
- [ ] Apple 状态 Accepted，逐条审核保存的 notary log；不能用测试 fixture 代替 Apple 服务结果。
- [ ] 对 staged App staple + validate，重新验签与 Gatekeeper assessment 全通过。
- [ ] staple 后重新生成 ZIP、payload manifest、SHA256；原输入 App 保持不变。

公证脚本默认只预检；`--submit` 才会上传。`--resume` 可在 Accepted 后执行收尾/staple，当前轮也不运行它。未知提交结果不自动再次上传。保留的可选命令见 RELEASING.md。

## 系统信任与分发观察：独立记录

- [ ] 浏览器实际下载后解压/安装，从 Finder 启动；优先另一台无 SDK 的干净 Mac。
- [ ] 首次下载/离线启动通过。不得关闭 Gatekeeper 或删除 quarantine 来宣称验收成功。
- [ ] 确认发布版本、支持范围、已知问题后再获得线上 Release 发布授权；当前没有执行上传/发布。

只签名策略不会将 `notarizationVerified` 或 `gatekeeperAccepted` 改为 true；当前 ad-hoc 包只读检查两项均未通过。

## 独立平台与交互验收

- [ ] 最低 macOS 15 的真实运行。
- [ ] Intel 的独立构建、native assets、Worker 和 UI 实机验收；Apple Silicon 结果不等于 Intel 支持。
- [ ] Windows 原有 Smoke/Regression/UI/图片与打包实机门禁；交叉编译只证明可构建。
- [ ] 物理中文/日文输入法、组合输入过程中快切任务/后台事件/关闭窗口。
- [ ] VoiceOver 朗读名称、顺序、焦点、错误和表单操作；AX tree 不等于完整朗读验收。
- [x] 最低 920×620 内容窗口可用、日语/深色、同一屏退出/启动恢复：已实际操作与截图。
- [ ] 多显示器断接、其他屏幕几何/缩放：仅 API 几何测试，尚未实机验证。
- [x] 三个真实番号 × LibreDMM/R18 六次实际查询、三个 NFO 保存及原生 LibreDMM 单片闭环；详见 LIVE-PROVIDER-QA.md。
- [x] 三片 LibreDMM 真实图片输出：3 封套 + 3 海报 + 37 全部所选样张；原生三片批量保存、最终签名 Worker 独立真实批量保存及 revision/冲突/文件回读通过，详见 LIVE-ARTWORK-BATCH-QA.md。
- [ ] 验证码/会话、真实网页导入、R18 图片输出及来源持续可用性；三片成功不代替这些。
- [ ] Jellyfin 真实扫描结果、1,000 行原生 UI 流畅度和持续内存使用。
- [ ] 完整 Xcode/XCUI 与远程 GitHub CI 实际执行；本地 SwiftPM/CLT 不代替这些结果。

## 交付审阅

- [x] App ZIP / 完整源码 / 许可证 / manifest / SHA / 运行说明 / 测试记录齐全，交付于 outputs/M6。
- [x] 截图只用虚构资料，01–10 为候选 1，11–14 为最终候选 2。
- [x] STATUS 明确区分“已实现”“自动化通过”“本机 GUI 观察”“未验证”。
- [x] 明确交付本地 Apple Silicon ad-hoc 候选；distributionReady 为 false，Gatekeeper 实际 rejected。

以上已勾选交付项对应原 `outputs/M6`。当前 `outputs/M6-artwork-batch-live`（此前为 `outputs/M6-signed-only-live`）的最终文件、哈希、线上测试结果和证据由 STATUS 与新目录内记录确认；不复用历史勾选来证明新产物已验收。


## Windows 历史案例回归（2026-10-22）

新增真实历史案例对应的解析、文件身份、旁车归属、来源隔离、重试、取消/清理故障和原生队列检查，结果见 [40项矩阵](WINDOWS-CASE-REGRESSION.md) 与 [STATUS](STATUS.md)。独立不可发布 RegressionHost 必须不在最终包内；完整 test.sh 强制固定HTTP集成，ZIP解包门禁仅运行生产 Worker。发布时须保留 .NET 签名 xattrs（使用 ditto）；复制后重新验证，不用文件字节相同替代签名检查。签名政策仍为 signed-only；ad-hoc不是 Developer ID，公证不执行。
