# preview.49：搜索统计低分配优化（内部）

日期：2026-09-04。基线：preview.48，本地提交 `56c4712`。本轮只优化测量确认的查询热点，不改变 UI、导入批次、网站并发、保存保护、语言资源或依赖；没有引入状态缓存和增量计数器。

## 为什么改、改了什么

队列状态原本通过 `IsNullOrWhiteSpace(MovieIdParser.Normalize(id))` 判断是否填写番号。`Normalize` 对无法识别的非空输入也会返回去除首尾空白的大写原文，因此这里的解析并没有提供额外的格式有效性校验，却会在反复统计时创建正则匹配、分组和字符串对象。

针对实际调用做独立测量：1,000 个合成影片 × 100 轮，问题状态和搜索资格各分配约 78.4 MB；未搜索影片的重试资格检查约 84.8 MB。它们解释了本轮值得优先处理的重复工作，而不是依靠“代码看起来复杂”推断瓶颈。

应用代码只改三个位置：

- `MainWindow.HasQueueIssue`：直接判断原始 ID 是否为空白。
- `MovieJob.CanBatchSearch`：先检查选择/搜索状态，再直接判断 ID 是否为空白。
- `MainWindow.CanRetrySources`：直接检查 ID，按索引检查已记录的失败来源，避免每行创建捕获窗口实例的谓词。

实际搜索、保存、图片替换身份比较中的番号规范化保留；DMM/R18/JAVLibrary 的查询边界、失败重试范围、用户审核选择和取消行为不变。未尝试通过隐藏状态更新、延后正确性检查或提升网站并发换取数字。

## 端到端模拟搜索

同机、相同测试入口、100/500/1,000 部各三次。实际点击 WPF 批量搜索按钮；HTTP 返回固定合成 JSON，计划延迟 2 ms，Windows 定时器实际延迟可能更长。每轮均断言 DMM 请求数等于影片数、R18 请求数为 0，完整视图 Reset 均为 1。

环境：Windows，.NET 10.0.11，16 个逻辑处理器。构建与性能窗口顺序运行，没有同时运行两份基准；不清空 OS 缓存，不操作真实影片库或真实网站。强制 GC 在计时与分配采样之后。

以下为三次中位数，MB/GB 使用十进制：

| 影片数 | 累计托管分配：前 → 后 | 总耗时：前 → 后 |
| --- | --- | --- |
| 100 | 25.51 → 4.35 MB | 374.27 → 350.10 ms |
| 500 | 525.53 → 14.89 MB | 1715.07 → 1549.08 ms |
| 1,000 | 2060.64 → 27.37 MB | 3462.17 → 3232.18 ms |

千片累计分配减少约 **98.7%**。这是运行期间创建对象的累计量，不是常驻内存，更不是“释放了 2 GB”；千片末尾托管保留内存仍约 7.94 MB，进程工作集也没有明显改善。主要收益是消除重复解析带来的分配和执行开销。

时间收益有限且存在噪声：千片前 3243.52–3647.69 ms，后 3054.22–3354.39 ms，区间重叠。100 部最大 UI 心跳间隔的中位数甚至从 26.85 变为 28.20 ms；不能宣称每项操作更快或全程无卡顿。首轮探索的百片耗时也曾小幅倒退，而分配下降稳定复现。这里不包含真实网络、图片下载或 R18 限流，不能宣传为真实网站搜索倍数提升。

## 独立热点调用对照

每行是对含 1,000 部影片的队列连续刷新统计 100 次的中位数，**不是一次真实搜索的阶段耗时**。只统计当前 UI 线程分配，不在测量内部推动 Dispatcher。三种状态依次为全可搜索、全成功待审核、25% DMM 失败。

| 队列状态 | 刷新累计分配：前 → 后 | 100 次刷新耗时：前 → 后 |
| --- | --- | --- |
| 可搜索 | 163.51 → 0.24 MB | 322.23 → 14.63 ms |
| 已搜索成功 | 240.32 → 0.24 MB | 123.45 → 19.21 ms |
| 混合失败 | 149.68 → 7.65 MB | 88.15 → 27.22 ms |

问题状态与搜索资格的孤立检查，三种状态下均未测到托管分配；无失败时的重试检查也未测到分配。混合失败仍有来源范围计算和 UI 格式化开销，暂不追加缓存状态或复杂统计框架。端到端数据与调用成本测试相互佐证，但各行不能相加，也不是完整采样剖析报告。

## 验证

新增回归以 preview.48 的原表达式为参照，覆盖空值、Unicode 空白、标准番号、非标准自由输入、取消/失败/审核/保存冲突、全部来源模式、大小写来源、JAVLibrary/未知来源排除，以及搜索中修改 ID。10,000 轮正常状态查询另有限定分配预算的回归；不以毫秒阈值判定机器性能。

完整自动化已通过：Core **46/46**、文件事务 **30/30**、WPF **PASS**、包版本规则 **PASS**。已有请求计数、部分失败/重试、NFO 未知 XML、替换失败保留原文件、复制取消/回滚、四语言和真实 WPF 布局检查继续保留。日志：`artifacts/performance49/final-gate.log`。

便携包 `release/JavMetaLite-v1.2.0-preview.49-win-x64-portable.zip`（60,441,669 字节）已独立解压校验：4 个文件，EXE 产品版本 `1.2.0-preview.49` / 文件版本 `1.2.0.0`，与打包目录内 EXE 哈希一致。没有在真实影片目录启动便携包；实际窗口回归来自同一源码构建的 WPF 测试。

- ZIP SHA-256：`7ED0A6F15624F4952E79F86B51F69B83824E014BC974F7B2FBD17594E8A9DEB4`
- EXE SHA-256：`07189109D12B13042F1244EDDA4AA8FDD86244E90BA85AABB003B3F23C5E254C`
- 打包日志：`artifacts/performance49/package.log`。仅本地保存，未推送或公开发布。

## 复测与证据

```powershell
.\scripts\Measure-Performance.ps1 -DotNet .\.dotnet-sdk\dotnet.exe -SearchOnly -Output artifacts/performance49/search-current.json
.\scripts\Measure-Performance.ps1 -DotNet .\.dotnet-sdk\dotnet.exe -SearchHotspots -Output artifacts/performance49/hotspots-current.json
.\scripts\Test-Automated.ps1 -DotNet .\.dotnet-sdk\dotnet.exe -NoRestore -BuildOutputDirectory bin/preview49-gate/
```

`SearchHotspots` 固定 1,000 部、100 轮调用、三次测量，不使用脚本的规模/重复次数参数。运行时日志和测试材料置于 artifacts，保留缓存、旧 SDK 和基线运行目录；不重复下载依赖。

- [模拟搜索之前](performance/preview49/search-before.json)、[之后](performance/preview49/search-after.json)。
- [独立调用成本之前](performance/preview49/hotspots-before.json)、[之后](performance/preview49/hotspots-after.json)。
- 优化前应用来自 `JavMetaLite.UiSmokeTests/bin/preview49-baseline/`，业务源码仍为 `56c4712`，仅先加入测量入口。基线 App SHA-256：`57823548C27E724BE36147578C18F9F27351543E8F453B1588B4EEE514E2C356`；Core：`1FA8326525E048FD6A296565BCC42C7EE60321C06BA427BE3ACF0539BD1B65F8`。
- 原基线可直接通过缓存 SDK 执行其中的测试 DLL：`--performance <output> 100,500,1000 3 search` 或 `--search-hotspots <output>`，不重建或覆盖该目录。最终版本运行目录为 `bin/performance-measure/`；首轮探索数据保留于 `artifacts/performance49/*-initial.json`。

下一步优先连续使用稳定性验证；仅在仍出现可复现的导入停顿时考虑时间预算调度。本轮不继续扩大优化范围。内部使用，不对外发布。
