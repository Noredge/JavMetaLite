# preview.52：批量保存的图片等待优化（内部）

日期：2026-09-04。已验收基线 preview.51 的本地提交为 `fd7d628`；该版本修复长按方向键误切语言，用户已反馈复测通过。

## 范围与结论

本轮只优化当前影片的样品图下载，并加上保存分段计时。保留批量影片逐部执行和文件事务提交顺序；不并行保存多个影片，不增加设置项、跨影片预取、持久缓存或依赖，不改 UI、四语言资源和来源路由。

此前用户日志中一次保存 71 部耗时约 5 分 30 秒，全部成功。旧日志无法精确拆开图片下载、解码、转换和临时写入，因此本轮先用相同计时与合成材料做串行/并发对照，不把旧日志的整段耗时全部称为网络耗时。

选择 **每批最多 3 张在线样品图**。相比为全部 50 张图片立即建任务，小批次可以在每批完成后按用户顺序去重，及时释放重复图片的字节；代价是当前批次有慢图时，不提前开启下一批。纯本地选择继续串行读取。封套仍先读取一次，供海报裁剪和横版封套共用，不与样品图同时启动。

## 安全与行为保持

- 输入仍只取前 50 张，结果按原始选择顺序汇总；内容哈希去重仍保留第一次出现的图片。完成顺序不会改变 fanart1、fanart2 等编号。
- 每批所有任务结束后才向上返回；取消和不可恢复异常会立即取消同批其他请求，并等待其结束。事务清理不会与尚在运行的图片任务交错。
- 普通保存中的可恢复图片失败仍可跳过；全面替换时，任一所选图片失败仍会拒绝全部替换，且发生在输出写入/移除原图之前。无搜索时的本地 Extra Fanart 保护仍由原有保存计划保证。
- 没有改动图片 URL 候选回退、元数据搜索并发、R18 API 限流、覆盖确认、影片冲突、SHA-256 校验或回滚实现。模拟图片 HTTP 429 不额外自动重试，也不绕过站点限制。

## 测量方法

在本机 Windows / .NET 10.0.11 上，通过实际 `BatchSaveCoordinator → FileOrganizationService → OutputService` 执行合成保存：

- 每轮 12 部，每部 1 张封套和 12 张不同样品图；输出 NFO、poster、fanart 和 Extra Fanart。
- JPEG 为 1500–1620 × 1000 的确定性纯色图片；每个 HTTP 请求使用 60/80/100 ms 的确定延迟，确保存在不同返回顺序。封套为 60 ms。
- 每版先单部预热，再执行三轮；每轮使用新目录和新客户端，没有预览缓存复用。只有 7 字节合成影片，无跨盘移动，不访问真实网站、不修改真实影片库。
- 基线在 preview.51 上只加入相同计时与 benchmark，样品图仍串行。保留的运行目录为 `JavMetaLite.RegressionTests/bin/save52-baseline/`；优化版为 `bin/save52-three/`。
- 基线 Core DLL SHA-256：`72143B0123156EE97445A515F25B7A1D42B8C7E12AF07AB3B3B52F0EFEA8C9A6`；优化版：`DDD0AF9DCEED6D5A0F5B17704C80ECB0EB5F9AC955134926838F99E807DCBD7D`。不要覆盖这些运行目录。

三轮原始数据：[串行](performance/preview52/sequential.json)、[最多 3 张](performance/preview52/three-downloads.json)。零延迟补充：[串行](performance/preview52/sequential-zero-delay.json)、[最多 3 张](performance/preview52/three-zero-delay.json)。本地详细日志和哈希清单位于 `artifacts/save52/baseline-r1/`、`three/`、`baseline-zero/`、`three-zero/`。

| 指标 | 串行 | 最多 3 张 |
| --- | ---: | ---: |
| 12 部总耗时，三轮 | 13.560 / 13.560 / 13.585 s | 6.615 / 6.529 / 6.624 s |
| 总耗时中位数 | 13.560 s | 6.615 s |
| 样品图读取阶段，每轮平均 | 11.939 s | 4.930 s |
| 图片裁剪/编码阶段，每轮平均 | 0.525 s | 0.545 s |
| 图片临时写入阶段，每轮平均 | 0.178 s | 0.188 s |
| 最终提交阶段，每轮平均 | 0.104 s | 0.107 s |
| 每轮图片请求 / 同时执行影片峰值 | 156 / 1 | 156 / 1 |
| 图片请求峰值 / 结束后未完成请求 | 1 / 0 | 3 / 0 |

中位总耗时减少 **51.2%**；主要收益发生在样品图读取阶段。三轮加预热的 592 个文件（含合成影片与全部输出）哈希清单完全一致。并发未增加请求数。

另做一轮 12 部零模拟延迟对照：串行 1.214 s、优化版 1.178 s，输出清单一致。该单轮结果只用于排查明显额外开销，不将约 36 ms 差异视为可靠收益。

每 10 ms 采样的进程工作集峰值，三轮最高约为 94.61 MB / 93.79 MB；托管内存采样峰值最高约为 1.60 MB / 1.62 MB。本样本未见明显内存增长，不能宣称零内存成本或覆盖所有分辨率。采样可能漏掉瞬间峰值，纯色 JPEG 压缩后较小，不代表真实图片带宽与大文件内存压力。累计分配和 CPU 数据包含 benchmark 监视器，不据此声称业务代码分配下降。

这是离线可控延迟实验，不等于真实站点吞吐、UI 主线程流畅度或跨盘保存测试。不能把用户原来的 5 分 30 秒直接按 51.2% 换算。

## 保存计时日志

每次 metadata 生成与每次文件事务各输出一条 `保存耗时` 摘要，不逐张写成功计时日志：

- `area=metadata`：`coverReadMs`、`samplesReadMs`、`prepareOutputsMs`、`imageProcessMs`、`imageWriteMs`、`nfoWriteMs`。
- `area=transaction` / `transaction-multipart`：`metadataPrepareMs`、`stageAndVerifyMs`、`commitMs`、`cleanupMs`，失败路径另有 `rollbackMs`。
- `result=completed/incomplete` 和 `totalMs` 用于区分完整与中断操作；具体错误仍看相邻原有日志。

这些都是阶段的墙钟耗时。读取阶段包括 HTTP/本地读取、图片有效性/尺寸检查和哈希，不是纯网络时间；转换阶段包括写出前的解码/裁剪/编码。`stageAndVerify` 包含原有旁车复制、源文件检查及必要的跨盘复制/校验；不是纯哈希时间。事务的 metadata 准备包含内层 metadata 计时，二者不能相加。生成保存计划之前的开销和队列 UI 收尾不在这些计时范围内。

## 回归与复测

完整门槛通过：Core **46/46**、文件事务与下载 **36/36**、WPF UI **PASS**、包版本规则 **PASS**。日志：`artifacts/save52/final-gate.log`。

新增六组检查已加入普通门槛：3 请求上限/乱序/去重/50 张限制；普通部分失败及 429；混合本地在线；单片/多 CD 替换失败；响应头返回后在响应体读取中取消；不可恢复错误取消同批请求并阻止下一部影片。取消与失败后检查请求数归零、原影片/NFO/图片哈希不变和事务临时目录清理。preview.51 键盘、四语言、来源范围及既有文件保护也全部通过。

```powershell
# 输出目录必须是新目录，防止覆盖旧测量材料；不联网恢复依赖。
.\scripts\Test-SavePerformance.ps1 -DotNet .\.dotnet-sdk\dotnet.exe -OutputDirectory artifacts/save-performance-new -Count 12 -Rounds 3 -DelayMilliseconds 60
.\scripts\Test-Automated.ps1 -DotNet .\.dotnet-sdk\dotnet.exe -NoRestore -BuildOutputDirectory bin/preview52-gate/
```

脚本入口另已执行 1 部 × 1 轮、零延迟检查（`artifacts/save52/script-check/`）。缓存 SDK、旧版本、基线运行目录与失败调试材料均保留，无依赖安装/升级。

## 便携包

- `release/JavMetaLite-v1.2.0-preview.52-win-x64-portable.zip`：60,444,319 字节。
- ZIP SHA-256：`DD2663D0DA435B10DB70EFCB46936BE932CAB4C655D5515C9E6964781E09301C`，与版本专属校验文件一致。
- 独立解压到 `artifacts/save52/portable-check/`，共 4 个文件；产品版本 `1.2.0-preview.52` / 文件版本 `1.2.0.0`。
- EXE SHA-256：`26A61F4B216D35D86372DE8CDEE1FCCB1CE083396D99237E3EE84CF076AFC904`，与打包目录一致。
- 打包日志与校验记录：`artifacts/save52/package.log`、`portable-validation.json`。

便携 EXE 本轮仅做解压、版本和哈希校验，未在真实影片库启动或保存；不能将合成测试称作用户真实场景验收。仅内部试用，未推送、建发布标签或公开发布。

## 下一步边界

交付内部 preview.52 后，由用户在原有保存场景复测。新日志可以进一步区分真实图片等待与磁盘开销；如仍明显缓慢，再依据测量决定是否处理图片重复解码或其他瓶颈。现在不追加跨影片预取、图片持久缓存、数据库、UI 调整或文件事务并行化。外部发布继续暂缓。
