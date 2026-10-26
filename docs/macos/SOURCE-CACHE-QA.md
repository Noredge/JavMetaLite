# build43：番号隔离与前端通知缓存

2026-10-24，America/Los_Angeles。App 0.4.0/build43/arm64，ad-hoc 签名，不公证。当前轮次仅修复前次审查的两项问题；不覆盖历史未提交修改，未 reset、commit 或 push。

## 修改与边界

- Worker 编辑番号经 `MovieJob.ChangeReviewedId`。真正改变规范化番号时，丢弃旧在线结果、字段候选、封套/样张来源、内容编号和自动来源地址，重置查询状态；不把旧在线值转换成手工回退。规范化后相同的大小写/连字符变体保留同片缓存。
- 保留本地 NFO 原始 XML、读取基线/指纹、视频路径、本地图片及当前选中的手工字段（包含明确的空值）。随后查询仍遵循既有来源优先规则，保留的手工候选可重新选用。不保留此前未选中的旧手工候选跨影片流转。
- 缓存来源列表、单字段选择、封套、远程样张和查询合并再次核对在线来源所属番号。改号会增加审核 revision，使旧保存计划失效；已有 `expectedReviewRevision`、全队列路径冲突预检、磁盘指纹、事务与恢复 journal 继续有效。
- `WorkspaceStore` 移除无限增长的完整通知/序号字典，改用 `OperationProgressCache`。只保存 operation ID、sequence、phase、currentIndex/totalJobs；不保存 result/error/路径等大载荷。默认最多 128 条、64 KiB 序列化精简进度；完成通知、等待结束、队列清空成功和 Worker 断开时释放。容量超限淘汰最旧记录，保护当前操作。
- 真实结果继续通过 `operations.get`/`workspace.getSnapshot` 对账；超时不重放保存。64 KiB 是缓存数据预算，不是进程 RSS 上限。通知帧解码和界面正常工作集仍占内存。
- WPF App 源码本轮没有改变。共享 Core 新方法只由原生 Worker 调用，未替换 WPF 的现有 ID 绑定。NFO writer、文件保存/事务与 `BatchSavePathConflicts` 源码本轮未改。

## 自动测试

完整 `scripts/macos/test.sh --configuration Release` 十套门禁均 exit 0：

| 项目 | 结果 |
| --- | --- |
| Core portable | 51 cases / 583 assertions |
| Swift | 63 tests = WorkerKit 20 + NativeBridgeKit/Workspace 43 |
| Worker 协议 | 73 = 生产 50 + 固定 HTTP 集成 23 |
| 队列 | 4 |
| Worker retention | 5 |
| 发布工具 / manifest | 36 / 15 |
| 四语言 | 443 keys / 1802 runtime checks |

新增协议 72/73 在冻结 build42 的实际 Worker/Core 程序集上分别复现旧标题残留、旧来源仍可选；build43 通过。覆盖改号、同号变体、手工空值/候选、本地 NFO 基线、旧计划失效、旧来源拒绝、新查询与实际安全保存。新增 Swift 测试覆盖通知载荷裁减、乱序/重复过滤、数量与字节预算、当前进度、完成/断线/队列清空释放。

额外实际编译 `WorkspaceStore` 探针：200 条各含 256 KiB 结果的完成通知后，缓存 **0 条 / 0 bytes**（旧42探针为 200 条 / 52,456,271 序列化 bytes）；2000 条进度含同样的大额无关 details 后，仅保留 **128 条 / 13,440 bytes**；断线后 **0 / 0**。这是合成通知代码探针，不是长时间原生 RSS 压测。

首次门禁遇到新增 Swift Testing 宏捕获可变缓存的编译错误；改为先取结果再断言后，完整重跑通过。独立探针首次缺少资源 bundle，补齐同构建资源后重跑通过。失败日志保留，不以失败尝试计作通过。

## 原生实测

主机 Apple M5 / 16 GiB / macOS 27.0.1 (26A434)。全部通过真实 AppKit/SwiftUI 窗口操作，只选择新建隔离合成夹具。两套 QA 均从冻结候选43复制，前端去签名后代码 hash、资源、生产 Worker/Core/Imaging 程序集与候选核对；QA 身份及启动 shim 不交付。

**QA48：生产 Worker，无固定 HTTP。** 导入 DEMO-648 本地 NFO 与合成双图，编辑标题并勾选。默认禁止覆盖时，预览确认禁用，5 个夹具文件 SHA/bytes/mtime 全部不变；取消保留草稿与勾选。显式允许覆盖后再次预览并确认，实际仅改 `movie.nfo`：标题变为 `QA43 safety title`，既有 NFO 规则补充 javnumber uniqueid，XML 声明编码拼写改为 utf-8；其他 XML（包括注释、未知属性/节点、演员、未规范化日期/时长/评分）逐字保留。视频和双图 SHA/bytes/mtime 不变，一份 completed 事务。随后原生清空队列，磁盘文件保留。

**QA49：受控网站响应，真实生产解析/审核/事务。** 外部非发布 RegressionHost 加载与候选43相同的 Worker/Core/Imaging DLL；仅替换资料 HTTP 响应，图片来自本机 loopback 合成 PNG。查询 IPX-081 的 R18 A 资料，编辑手工原始标题、明确清空简介、改号 IPX-082，查询 LibreDMM B。缓存来源菜单只剩 LibreDMM，旧 R18 不可选；旧图片、片商、简介、旧详情地址均移除。手工原始标题保留为候选并可恢复。预览后真实保存新 NFO，磁盘回读为 IPX-082 / `New movie B - Libre fixture` / `QA43 explicit manual original` / 空简介，无 A 资料或旧图片地址；影片文件 SHA/bytes/mtime 不变，一份 completed 事务。

原生 ID 文本输入先停留在前端草稿；查询/选源/保存操作提交草稿时执行隔离。因此 `qa49-id-edited` 是提交前草稿截图，仍可见旧的已提交摘要；最终查询、候选与磁盘验收以之后截图/回读为准。后续查询采用当前来源优先值，手工候选可恢复，未宣称所有手工值在新查询后仍自动选中。

QA48/49 已通过应用菜单正常退出，进程消失；loopback 图片服务验收后停止。共 11 张本轮截图，索引说明提交前草稿与最终结果的区别。

## 产物、重跑与未验证

会话 `outputs/Mac-source-cache-2026-10-24/`：`JavMetaLite.app`、ZIP、manifest/hash、results 与 screenshots。退出旧版后打开该 App（Apple Silicon、macOS15+）。不在原生验收后重建替换候选；最终 ZIP 解包后复跑生产50/queue4/retention5、签名/资源/17 native依赖与无SDK验证。

```sh
DOTNET="$PWD/../dotnet/dotnet" \
JAVMETALITE_PRIVATE_PARSING_CASES="$PWD/../mac-regression-core-private/parsing-cases.json" \
bash scripts/macos/test.sh --configuration Release
```

原始证据：会话 `work/source-cache43-qa/`；`qa48-saved-readback.json`、`qa49-saved-readback.json`、`frontend-retention43-result.json`、`source43-delta.json` 与最终 results/verification-summary.json。旧 build42 与历史产物保持。

未重验：本轮完整四语/最小窗口原生矩阵（build42 历史证据保留，不冒充43重验）、X03/R08全部次级状态、live站点/Cloudflare、物理IME/完整VoiceOver/手势、长期RSS与大影片压力、Windows/Intel/macOS15/干净Mac、物理外接卷/SMB/满盘/断电、Developer ID/Gatekeeper。此次只有 ad-hoc 签名；不公证。
