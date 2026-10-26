# build40 — 完整回归复跑与小范围修复

2026-10-24，America/Los_Angeles。基线为已交付的0.4.0/build38；本轮产物为 **build40回归修复候选，arm64，ad-hoc签名，不公证**。自动门禁通过不等于全部原生验收通过：QA41首次保存预览超时仍未解决，不能标为已验收。

## 代码改动

1. 队列搜索使用当前有效番号与标题，含未提交草稿，并支持原视频文件名；忽略搜索词两端空白。新增四语言悬停提示。搜索/筛选不改变查看、草稿、revision或勾选范围。
2. 图片查看器切换来源时取消旧的viewer-owned请求；主页面拥有的请求和已完成缓存继续保留。避免切到本地图后旧远程图片仍下载；不重新引入全局busy或清空缓存。
3. 两项新增可执行回归均先在修复前失败，再在修复后通过。原生也分别验证了搜索及来源切换的取消。

9个构建输入变化：WorkspaceStore、WorkspaceView、Info.plist、字符串目录及4种strings、NativeBridgeKitTests。Core、生产Worker、WPF、MovieJob、NFO writer、BatchSavePathConflicts、文件事务与journal源码hash/mode均与本轮开工相同。保留历史WIP，未reset、commit或push。

## 自动测试与打包

| 范围 | 实际结果 | 证据 |
| --- | --- | --- |
| build38基线完整门禁 | 10套exit0，Core50/579断言，Swift56，协议69＝50生产+19固定HTTP，queue4，retention5，发布工具36/manifest15 | review38-gate.log |
| build40最后完整门禁 | 10套exit0；Swift58＝18 WorkerKit+40 NativeBridge/interaction；Core50/579，协议69，queue4，retention5，发布工具36/manifest15 | review40-final-gate.log、final-gate-suites/results.tsv |
| 四语言 | 441 keys；1794真实Bundle/区域选择/回退检查通过 | localization40.log、最后门禁 |
| APFS大小写敏感卷 | Core51/598断言；16MiB合成媒体跨设备ID完整SHA-256复制及复制取消通过 | case-volume-portable.log、case-volume-result.json |
| 最终生产包 | 17 native文件、签名/entitlements/资源/依赖/manifest、无SDK Worker启动通过；ZIP解包后生产50、queue4、retention5通过 | review40-package.log |
| 交付绑定 | 交付App payload/mode/签名属性与生产包一致；QA42前端去签名代码、全部资源和生产Worker/Core DLL与最终包一致 | verification-summary.json、regression42-bundle-provenance.json |

APFS测试挂载旧测试镜像的独立shadow，完成后detach；原镜像SHA未变。只证明本机磁盘镜像的独立设备ID，不冒充外接硬盘。仍未进行Windows实机、Intel、macOS15或干净Mac测试。

## 原生实测：版本和结果分开记录

QA40应用名对应build39；QA41和QA42对应最终build40。它们采用独立身份和私有/tmp状态，替换入口为非发布固定HTTP宿主；运行真实Workspace、解析器、NFO及事务代码。图片来自仅回环的中性合成素材服务，**不是live抓取证据**。测试host/config不随交付包发布。

| 场景 | 观察与回读 | 截图/证据 |
| --- | --- | --- |
| build39搜索 | “北岸”找到正在编辑的草稿，前后空白不影响；TEST-930-UC找到原文件名；勾选及当前编辑保留 | 01-draft-title-search.jpg、02-overwrite-blocked.jpg |
| build39默认预览/覆盖关闭 | Confirm Save禁用；取消后标题与勾选保留，8文件SHA/bytes/mtime不变 | native-blocked-readback.json |
| build39明确允许覆盖后保存 | 预览1 Update+3 Preserve；仅TEST-930/movie.nfo的title字节改变，其余XML原始字节及7文件SHA/bytes/mtime不变，无增删 | 03-save-success.jpg、native-save-readback.json |
| build39图片错误重试 | 第一次受控HTTP503显示错误，Retry成功147×200；其余审核状态保留 | 04-image-retry.jpg、image-requests.jsonl |
| build39关闭慢图片 | 响应头前关闭，服务端记录clientClosed且0 bytes；放行后重新打开成功257×126 | 05-image-cancel-reopen.jpg、image-after-viewer-close.json |
| build39切源遗漏 | 样张03响应体仅发1字节时切本地，旧请求仍active，成功复现；单元回归也先失败 | image-after-source-switch-before-fix.json、viewer-before-fix.log |
| build40切源修复 | 同样响应体等待中切本地，立即显示300×450本地图；服务端在gate仍关闭时记录clientClosed/sentBytes=1、active为空 | 11-final40-source-cancel-local.jpg、image-final40-source-cancel.json |
| build40千片原生 | 确认1000片、定位PERF-60999、筛选下全选1000→取消0→单片1，当前查看保持；清空成功，2000文件SHA/bytes/mtime均未变；没有发起千片查询/保存 | 12–13、native40-queue1000-*.txt/json |
| build39四语言受限窗口 | 系统Window→Move & Resize→Top Left，实测1842×1346px（约921×673pt；最小内容策略920×620）；四语主页面双图、来源、保存设置与批量按钮可见，长说明换行/主体滚动正常 | 06–09四语截图、native-*-constrained.txt |

四语布局截图是build39。最终40仅继续修改取消逻辑和测试，布局代码/本地化资源与39相同（除BUILD-MANIFEST）；以hash绑定记录，未冒充40逐语再次启动。此次系统为深色；**没有新验收深色冲突弹窗或全部四语模态窗口**。QA40、QA42已清空会话并正常退出，合成图片服务已停止。

## 仍未解决：QA41首次预览超时

在最终40独立QA41中，选择单个TEST-930目录、编辑标题并保存，首次预览未返回，随后显示engine unavailable/operation status unconfirmed。采样见.NET线程停在 `SystemNative_OpenDir → __opendir2 → open$NOCANCEL`，不能仅据此归因为TCC、特定目录权限或应用死锁。

未执行save.execute，未重试保存；8个夹具文件与启动前完全相同，草稿“Final40 草稿搜索 北岸”与勾选仍可见。已请求取消，但没有取得确认；正常退出被未知状态保护阻止。QA41按保护策略保留并最小化供诊断，没有强杀Worker、伪造完成状态或跳过目录冲突检查。该问题不能用QA40保存通过、QA42图片通过或生产协议测试通过抵消。

证据：10-final40-preview-unconfirmed.jpg、14-final40-close-blocked.jpg、native40-preview-unconfirmed.txt、native40-close-blocked.txt、native40-preview-timeout-no-write.json、qa41-first-preview-worker.sample.txt。**原生首次预览与取消/状态恢复为下一优先项；build40不宣称完整发布验收通过。**

## 性能复测及后续优化

使用不可变的生产build38自包含Worker；其Worker/Core DLL与40一致。每个规模使用新进程和新会话缓存，未清空OS文件缓存，无网络，不测真实大影片吞吐。只做单次观测，未定义性能通过阈值。

| 本机合成规模 | 发现ms | 确认并载入NFO ms | 三片NFO预览ms | 三片NFO执行ms |
| --- | ---: | ---: | ---: | ---: |
| 8 | 22.204 | 68.392 | 34.688 | 116.182 |
| 71 | 39.582 | 153.792 | 89.259 | 120.537 |
| 362 | 152.813 | 621.912 | 413.284 | 120.969 |
| 425 | 174.101 | 778.297 | 470.372 | 133.384 |
| 1000 | 210.872 | 1102.409 | 379.554 | 126.479 |

当前界面使用发现/确认路径；1000片确认约1.1秒，Worker snapshot约28ms。各规模均实际保存3份目标NFO，其余媒体及未编辑XML保留。重复本地图片预览中位数约0.423ms。证据：discovery-save-baseline.json、discovery-1000-baseline.json。

旧兼容 `workspace.importInputs` 导入1000个极小合成文件耗时36125ms；该接口需要后续性能分析，不能称为当前UI导入耗时。没有为了加速而删掉容量、全队列冲突、revision、指纹或事务保护。证据：worker-1000-baseline.json。

## 运行与产物

会话 `outputs/Mac-regression-review-2026-10-24/` 包含build40候选App、ZIP、manifest、results与screenshots。旧build38产物保持不变。Apple Silicon、macOS15+，Worker自包含；需要试用候选时先退出同名旧版。

尚未验证：QA41首次预览根因和恢复、旧兼容导入优化、完整四语原生弹窗矩阵/深色冲突窗口、live来源/Cloudflare长期稳定、物理IME/触控手势/完整VoiceOver、Windows/Intel/macOS15/干净Mac、物理外接卷/SMB/满盘/断电、Developer ID和Gatekeeper。按用户决定不进行公证；本机只做ad-hoc签名。
