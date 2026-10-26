# JavMetaLite — 四语言说明与main合并准备

更新：2026-10-25，America/Los_Angeles。[PR #11](https://github.com/Noredge/JavMetaLite/pull/11)以merge commit合并mac至main，并保留mac分支；当前仍在合并前门禁阶段。

- 英文、简中、繁中、日文README统一简述Mac功能、Apple Silicon/macOS15+要求、CI产物下载与构建命令；Windows截图/步骤明确标注，避免混淆。四份本地链接、版本和命令检查通过。
- PR Mac回归暴露浏览器地址解析用例的时序问题：原夹具以80ms延迟配合150ms预算，可能先超时，无法检验预期的revision冲突。改为测试专用HTTP响应门闩，确认请求挂起后编辑、再释放响应；此用例使用10秒预算，独立150ms真实超时回归不变。保留取消、影片不匹配、revision冲突及不落盘断言；定向两项及连续30轮通过。
- 仅改RegressionHost和协议测试，未修改生产Core/Worker/WPF/Swift、revision、NFO round-trip、冲突预检或文件事务逻辑。本机完整11套门禁exit0：Portable52/526断言、Swift68、协议74、queue4/retention5、发布工具36/manifest15、445keys/3590四语检查及Xcode源文件/编译布局检查。远端双平台仍须在当前提交通过，首轮失败记录保留。

只签名、不公证。未新增原生手动/物理设备验证；既有长期压力、真实卷/SMB/断电、完整IME/VoiceOver、Intel及Developer ID/Gatekeeper等未验证范围保持。

---

# JavMetaLite — Windows事务回归与取消夹具修复（历史记录）

更新：2026-10-25，America/Los_Angeles。修复基线`21ac04a`；[此前Windows CI](https://github.com/Noredge/JavMetaLite/actions/runs/40230254346)出现事务断言失败，随后在响应体取消用例挂起并触及20分钟上限，未执行Windows打包。本轮只改测试、CI诊断和文档，未修改生产Core/Worker/WPF/Swift或安全保护代码。

- 来源锁测试拆为两项：`FileShare.Read`允许重新校验但禁止移除，须完整回滚目标、保持来源hash并清理临时文件；`FileShare.None`使来源无法重新校验，须抛FileRecoveryException、保留来源及已验证副本并报告恢复路径，同时回滚NFO。保留保守恢复契约，没有为旧断言删除无法确认冗余的副本。
- 挂起源于HTTP夹具只实现HttpContent序列化；改用流读取后，框架的默认缓冲路径不传入取消令牌。将夹具提为Windows/Portable共用的StreamContent/GatedStream，在实际ReadAsync中接收取消，另有明确的夹具清理令牌。仍检查3路并发、取消后排空、致命错误取消同批请求及后续影片不启动、单片/多CD原视频/NFO/图片不变。
- 新Portable回归在旧夹具上实际失败：50/52，取消与致命失败均TimeoutException；流式夹具修复后52/52、526断言通过。Windows旧断言并未删除：保留可校验锁的完整回滚用例，新增独占锁保留副本用例。
- Windows事务runner输出每项START，单项超过2分钟则报错并终止测试进程，不继续运行文件测试或记为通过。取消/致命失败用例另有10秒上限及显式清理；没有延长原20分钟job限制。Windows CI无论成功失败均上传automated-gate.log。
- 本机最终11套门禁exit0：Portable52/526断言、Swift68、协议74、queue4/retention5、发布工具36/manifest15、445keys/3590四语检查及Xcode源文件/编译布局检查；Windows四项目交叉编译均exit0、无警告或错误。交叉编译不冒充Windows运行验证。

同提交双平台合并门禁（徽章跟随mac分支；最终须核对两套运行的head SHA相同）：

[![Windows CI](https://github.com/Noredge/JavMetaLite/actions/workflows/ci.yml/badge.svg?branch=mac)](https://github.com/Noredge/JavMetaLite/actions/workflows/ci.yml?query=branch%3Amac)
[![macOS CI](https://github.com/Noredge/JavMetaLite/actions/workflows/macos.yml/badge.svg?branch=mac)](https://github.com/Noredge/JavMetaLite/actions/workflows/macos.yml?query=branch%3Amac)

revision、NFO round-trip、冲突预检和事务/恢复逻辑保持。只签名、不公证；尚未合并main。远端Windows自动测试不等同用户电脑手动验收；既有长期使用、物理外接卷/SMB/断电、完整IME/VoiceOver及平台矩阵等未验证范围保持，未替换旧本机冻结App。

---

# JavMetaLite macOS — Mac CI兼容修复（历史记录）

更新：2026-10-25，America/Los_Angeles。首轮远端Mac CI在Swift测试编译失败；不是运行时保存超时，打包步骤当时未执行。[原始失败记录](https://github.com/Noredge/JavMetaLite/actions/runs/40225213296)。

- runner实际为macOS15/macOS SDK15.5、Xcode16.4、Swift6.1.2；BrowserInteractionTests的嵌套JSON表达式触发“unable to type-check this expression in reasonable time”。此前本机Swift6.4通过不能覆盖该编译器差异。
- `cd3a147`将三个来源字典、目标数组和2MiB计算拆成明确类型的局部值；[随后远端运行](https://github.com/Noredge/JavMetaLite/actions/runs/40228682086)已越过编译失败，执行全部68项Swift测试，暴露下列3项差异；其中5个失败断言保留原始记录，没有把本机成功当作远端成功。
- SwiftPM6.1将本地化目录转为小写（[上游实现](https://github.com/swiftlang/swift-package-manager/blob/swift-6.1.2-RELEASE/Sources/PackageModel/Resource.swift#L39-L40)），旧Bundle查找漏掉中文目录并回退英文。应用改为从资源根目录显式寻找规范及小写lproj拼写，保留英文/缺键回退；新增两种真实Bundle布局的四语检查，共445keys/3590运行检查。
- macOS15的`URL(string:)`将`about:blank#widget`表示为`about:blank%23widget`；尝试URLComponents仍被重新编码，[第二轮远端](https://github.com/Noredge/JavMetaLite/actions/runs/40229495288)已通过中文及超时测试、仅余此处2个失败断言。最终在生产导航策略中仅对嵌套blank/srcdoc识别这一种井号编码；不通用解码URL，主页面/弹窗/捕获仍拒绝about URL，双重编码、其他路径及主机、查询串继续阻断。
- 将远端的实际编码地址加入本机回归后，旧策略实际失败4个断言；修复后68项Swift及完整11套门禁通过，保留原浏览器切换、HTTP失败和取消/导入断言。没有将旧OS不兼容案例排除出测试。
- 超时测试原将Python冷启动也限制为250ms，远端初始化阶段偶发超时。现在给予正常请求5秒预算，fixture扣住保存响应直到客户端超时后显式释放；验证晚到响应不完成另一请求、保存计数仍为1、状态仍可查询。没有改生产超时、重试或Worker代码。
- 上述修复后本机完整11套门禁再次exit0：Core50/486断言、Swift68（20+48）、协议74、queue4/retention5、发布工具36/manifest15、445keys/3590四语检查；22个应用/8个测试源文件注册与非SwiftPM编译布局通过。本机编译器仍为Swift6.4/CLT，不冒充Swift6.1或完整Xcode运行。
- 远端沿用原macos-15/Swift6.1工作流及全部门禁，自包含App签名和解包回归结果以[mac分支Actions](https://github.com/Noredge/JavMetaLite/actions?query=branch%3Amac)中对应提交为准。revision、NFO round-trip、冲突预检和文件事务保护没有修改。只签名、不公证；本轮未重复原生UI验收或替换此前冻结的本机build45交付包，未验证平台/长期压力范围保持。

---

# JavMetaLite macOS — mac分支推送前审查（历史记录）

更新：2026-10-25，America/Los_Angeles。发布分支目标为原仓库的 **mac**，保留main；当前应用仍为0.4.0/build45，ad-hoc签名，不公证。详见 [PRE-PUSH-REVIEW.md](PRE-PUSH-REVIEW.md)。

- 本轮未发现新的可复现数据/取消恢复缺陷。修正Xcode工程遗漏：补齐1个生产源文件、5个测试/辅助源文件及非SwiftPM测试导入；旧工程完整性检查失败，修复后22个应用/8个测试源文件注册和CLT Xcode编译布局检查通过。完整Xcode构建/宿主测试仍未实测。
- macOS和Windows CI监听mac分支，修正文档中旧的预览/覆盖说明。运行时保存逻辑、revision、NFO round-trip、全队列冲突预检和文件事务保护保持；没有重打包45，也不混用新工程文件与旧包的来源哈希。
- 完整11套门禁exit0：Core50/486断言、Swift68、协议74、queue4/retention5、发布工具36/manifest15、445keys/1810四语检查，以及新增Xcode源文件/编译布局门禁。Windows四项目交叉编译通过，原WPF图片算法移动前后逐字一致；不冒充Windows运行验证。
- 已对提交候选另作干净clone，完整11套门禁再次全部通过、检出工作区干净；不依赖未提交文件或原目录构建产物。其后仅补记本报告和审查文档。
- 仅发布源码、协议、测试/合成夹具、必要资源/依赖锁、构建CI、许可证及文档。本地产物、日志、截图、SDK、缓存和8份重复阶段快照不上传；旧产物与未选本地文件保留。

仍未验证的完整Xcode、物理平台/外设/无障碍、长时间内存压力、真实网站及Developer ID/Gatekeeper范围保持，不因本轮源码审查上调。只签名、不公证。

---

# JavMetaLite macOS — build45记住设置与预览确认替换（历史记录）

更新：2026-10-25，America/Los_Angeles。当前交付 **App0.4.0/build45/arm64，ad-hoc签名，不公证**。按用户新要求，保存设置自动记住，预览“确认保存”即为替换已有NFO/图片的确认，无需先勾选更多选项中的允许覆盖。详见 [SAVE-PREFERENCES-QA.md](SAVE-PREFERENCES-QA.md)。

- 自动恢复输出项目、样张替换、重命名、NFO标题前缀、目标模式/根目录、跳过预览、查询来源、递归导入、左右面板和更多选项展开；语言/外观/自定义字段来源继续保留。Worker启动不再重置原生设置；不记住队列、勾选、未保存资料或计划。
- 移除额外覆盖复选框，预览新增替换说明；默认仍打开预览，明确开启跳过预览时直接执行合法计划，悬停说明包含NFO/图片替换。视频不被覆盖，重复目标、revision、文件变化和事务保护继续生效；Worker协议默认不授权替换的规则没有放宽。
- 最终串行10套门禁exit0：本轮Core portable50/486断言、Swift68（20+48）、协议74（生产50+固定HTTP24）、queue4、retention5、发布工具36/manifest15；四语445keys/1810检查。最终签名ZIP解包生产50/queue4/retention5、17 native依赖/资源/entitlements及无SDK检查通过。
- QA54相同编译前端/资源和生产Worker：三片已有NFO预览无需额外覆盖勾选，取消20文件不变，再预览确认3部Completed；仅641标题改变，其余XML及19文件不变。设置及面板状态经正常退出、磁盘偏好回读和冷启动恢复通过；新导入两片的影片/NFO/海报/封套冲突和跳过预览阻断通过。明确跳过预览后实际单片替换通过。最终仅同一NFO标题改变，4份completed事务、目标为空、QA前端/Worker正常退出。
- 14个构建输入变化全部位于macos/；Core、Worker、WPF、NFO writer、BatchSavePathConflicts、指纹/事务源码hash/mode保持。原生及交付绑定通过，验收后未重建替换。历史WIP及旧44产物保留，未reset/commit/push。

运行：退出旧版后打开会话 **outputs/Mac-save-preferences-2026-10-25/JavMetaLite.app**（Apple Silicon、macOS15+，无需安装.NET）。相邻ZIP、manifest、results与14份截图索引包含设置恢复、真实保存、冲突阻断及文件回读。旧版此前没有记录的输出/目录等选项无法凭空恢复，新版会从本次选择开始自动记住。

本轮原生覆盖英文常规窗口与简中设置/确认说明；完整四语/明暗/最小次级窗口矩阵、物理IME/完整VoiceOver/手势、长期内存/真实大影片/大队列/网络、live站点/Cloudflare、Windows/Intel/macOS15/干净Mac、物理外接卷/SMB/满盘/断电、Developer ID/Gatekeeper继续未重验。中间协议41一次本地HTTP回退观测失败，单独重跑、最终串行74项及签名ZIP生产50项通过，原始失败保留，根因未定；QA51历史外置宿主dyld失败亦不改写为已修复。不公证。

---

# JavMetaLite macOS — build44解锁后原生回归补齐（历史记录）

更新：2026-10-25，America/Los_Angeles。沿用 **App0.4.0/build44/arm64，ad-hoc签名，不公证**。此前锁屏阻挡的第三轮导入／移除及追加冲突窗口原生复验现已补齐。详见 [BATCH-SOAK-NATIVE-CLOSURE.md](BATCH-SOAK-NATIVE-CLOSURE.md)。

- QA52第三轮重新导入3片本地NFO，逐部切换/完整资料滚动/切页保持3个勾选，逐部移除至0；空队列查询、保存正确禁用。累计三轮完成；不将解锁后结果回填为锁屏时已通过。
- QA53使用相同前端/资源/生产Worker/Core/Imaging，独立身份及argv数据根shim。两片DEMO-611的影片、NFO、海报、封套重复目标在浅/深色均阻断；明确允许替换和跳过预览均不能绕过。深色长路径/实际蓝色选区可读、确认禁用；取消后勾选和操作恢复。只勾一片能生成合法预览，仅查看后取消，未执行保存。
- 英文深色受限主窗口实际1842×1346px，双图/队列/工具栏可用，设置区可滚动；冲突sheet1560×1120px，警告和取消可用。退出取消保留草稿，随后明确丢弃验收配置草稿正常退出；QA52/53前端与Worker均已独立确认退出。
- 直接运行交付包生产Worker定向6项全部通过（2.210秒）：NFO round-trip、revision、重复导入/移除、批量冲突、外部变动全批次阻断、取消保持已提交前项及未开始尾项。上轮10套全门禁为10月24日历史结果，本轮未重新全量运行。
- 原QA50/52的10文件及本轮冲突8文件共18文件SHA/bytes/mtime不变，未知XML逐字保持；目标目录为空，QA53无事务，QA52仍仅原3份completed事务。构建输入、冻结交付App/ZIP完全未变，签名/QA代码资源与程序集绑定复核通过。revision、NFO round-trip、冲突预检和文件事务保护没有修改，历史WIP保留，未reset/commit/push。

运行仍打开会话 **outputs/Mac-batch-soak-2026-10-24/JavMetaLite.app**（Apple Silicon、macOS15+，自包含Worker）。结果追加至`results/native-closure-2026-10-25/`，15张新截图见`screenshots/native-closure-2026-10-25/INDEX.md`；旧11图及原始时点summary保持。App/ZIP没有重新打包替换。

本轮无新增功能故障。可后续优化重复冲突提示和受限窗口英文“Uncheck All”换行。**仍未验证：** 没有新增持续RSS采样，隔夜锁屏空闲不计主动使用；小时/天级内存、真实大影片/大队列/网络压力、完整四语全次级窗口矩阵、物理IME/完整VoiceOver/手势、live站点/Cloudflare、Windows/Intel/macOS15/干净Mac、物理外接卷/SMB/满盘/断电、Developer ID/Gatekeeper。QA51外置测试宿主dyld启动根因仍未定，不计通过；X03/R08/R10保留部分待测。不公证。

输入digest：`521c9bd1b8e2aa7b6f15e859f40a2289f0f34ae91f38150c5e2149a5f42dda51`。ZIP SHA256：`1c9c84837c34333c825532555471a73b602b023152b1465405be9aaad59de18c`。

---

# JavMetaLite macOS — build44批量与内存审查（10月24日历史记录）

以下保留锁屏时点的实际结果；当时待解锁项已由上方10月25日记录补齐。

更新：2026-10-24，America/Los_Angeles。当前交付 **App0.4.0/build44/arm64，ad-hoc签名，不公证**。两项前端问题已修复，完整自动门禁及已执行原生流程通过；**锁屏中断的第三轮与追加冲突窗口原生复验未完成**。详见 [BATCH-SOAK-QA.md](BATCH-SOAK-QA.md)。

- 逐部移除/Worker合并移除后的标题标签、图片来源、草稿与编辑会话统一按权威快照释放；移除失败或状态未确认时不丢草稿，剩余影片Undo保持。100份退休审核状态探针由100标签/100编辑器/100草稿/100图片来源修为0；两组文本原共约12.5MiB，不当作RSS。
- 菜单“重试失败来源”按失败记录确定网站，避免新查询菜单改选后空重试；显式单来源按钮仍按指定范围。两个Swift定向回归先失败后通过，实际原生Libre503→新查询改为R18→菜单重试Libre恢复通过。
- 完整10套门禁exit0：Core51/583断言、Swift65、协议74（生产50+固定HTTP24）、queue4、retention5、发布工具36/manifest15；443keys/1802四语检查。新增三片查询取消/尾项重试/全批次旧revision阻断/实际NFO保存回归通过。最终签名ZIP解包生产50/queue4/retention5、17 native依赖/资源及无SDK验证通过。
- 生产Worker真实900.011秒：8个1MiB合成影片、88批次、704次NFO保存、17次重新导入；逐轮视频SHA与未知XML/原日期时长评分保持。RSS载入77.7MiB、采样峰值113.0MiB、结束70.0MiB；最终106回执/约1.36MiB、pending previews 0，未触及128回执上限，容量压力由retention5另测。
- QA50生产Worker：覆盖关闭时7文件保持，取消保留草稿；允许后仅改1份NFO（标题、既有uniqueid补充、编码声明大小写），其余XML和视频/双图保持。一份completed事务，正常退出。
- QA52包内固定HTTP宿主，同44前端/资源/生产程序集：三片查询取消结果为Completed/Canceled/NotStarted，只重新查询尾两片均完成；批量预览取消保留3个勾选，再预览实际保存3份NFO，磁盘回读正确，视频SHA/bytes/mtime保持、3份completed事务。随后两轮重新导入/完整字段滚动/切页/逐部移除通过；保存后的10文件再回读全部保持，无额外事务。
- QA52实际120份RSS采样、601.930秒：前端129.7→133.4MiB、采样峰值185.5MiB；测试Worker采样峰值114.2MiB、末次66.4MiB。**后段包含自动锁屏空闲，不是10分钟持续主动使用。** 第三轮及追加冲突窗口复验被锁屏阻挡，已请求手动解锁，隔离QA52窗口保留，最后确认队列为空。有限观察不证明长期零泄漏。
- QA51独立外置宿主启动超时，sample停dyld __open，未进入Worker/建立dataRoot；同宿主CLI初始化正常，OS根因未定。失败截图/sample保留，不计原生通过；测试进程结束、前端正常退出，改用包内QA52完成上述检查。包测试日志末尾有实际QA身份更正说明。
- 只4个构建输入改变。Core/Worker/WPF与revision、NFO round-trip、冲突预检、指纹/事务源码hash/mode保持；Worker/Core/Imaging DLL与43相同。历史WIP和43产物保留，未reset、commit或push。

运行：退出旧版后打开会话 **outputs/Mac-batch-soak-2026-10-24/JavMetaLite.app**（Apple Silicon、macOS15+）。相邻ZIP、manifest、results与screenshots/INDEX.md包含通过/失败记录、前后探针、RSS原始采样、磁盘回读与11张截图。交付为冻结候选44，原生验收后未重建替换。

**待验证：** 上述锁屏原生余项；数小时/数天、真实大影片/大队列/网络压力；完整四语/最小窗口原生矩阵与X03/R08全部次级状态、物理IME/完整VoiceOver/手势、live站点/Cloudflare、Windows/Intel/macOS15/干净Mac、物理外接卷/SMB/满盘/断电、Developer ID/Gatekeeper。R10继续部分待测，不据有限RSS采样上调为长期稳定。不公证。

输入digest：`521c9bd1b8e2aa7b6f15e859f40a2289f0f34ae91f38150c5e2149a5f42dda51`。ZIP SHA256：`1c9c84837c34333c825532555471a73b602b023152b1465405be9aaad59de18c`。


---

# JavMetaLite macOS — build43番号隔离与通知缓存修复

更新：2026-10-24，America/Los_Angeles。当前交付 **App0.4.0/build43/arm64，ad-hoc签名，不公证**。本轮修复前次审查的两项问题，完成全门禁与隔离合成夹具原生闭环。详见 [SOURCE-CACHE-QA.md](SOURCE-CACHE-QA.md)。

- 真正修改番号时清除旧在线字段、封套/样张候选、内容编号与自动来源地址；缓存合并/选源再次核对番号。不再把旧在线资料转成手工回退。同一规范化番号的大小写/连字符变体保留缓存。
- 保留当前手工字段（含明确空值）及本地NFO原始XML/读取基线、视频和本地图片；后续查询仍按既有来源优先规则，手工候选可恢复。改号增加review revision，旧预览失效；NFO round-trip、全队列冲突预检、文件指纹与事务保持。
- 前端通知只留精简进度，默认最多128条/64KiB序列化数据，不留完整result/error；完成/等待结束/队列清空成功/Worker断线释放。实际编译Store探针：200条×256KiB完成载荷后0条/0bytes；2000条进度后128条/13,440bytes；断线后0/0。这不是RSS上限或长期压力验证。
- 完整10套门禁exit0：Core51/583断言、Swift63、协议73（生产50+固定HTTP23）、queue4、retention5、发布工具36/manifest15；443keys/1802四语检查。新增改号回归在冻结42程序集先失败，43通过；缓存容量/释放及活跃进度测试通过。
- QA48生产Worker：覆盖关闭时阻断且两组共5个夹具文件SHA/bytes/mtime不变；取消保留草稿。允许覆盖后仅改1份NFO（标题、既有规则补充uniqueid和声明编码大小写），其他XML及视频/双图保持，一份completed事务；随后清空队列不删文件。
- QA49同43生产程序集、固定资料HTTP与loopback合成图片：R18旧A→改号B→Libre新B，旧来源/图片/片商/简介消失；手工原始标题候选可恢复。实际保存并回读B番号、B标题、手工原始标题及空简介，视频SHA/bytes/mtime不变，一份completed事务；不冒充live站点验证。
- 最终ZIP解包生产50/queue4/retention5、签名/资源/17 native依赖及无SDK检查通过。11张本轮原生截图与交付43代码/资源/Worker/Core绑定，验收后未重建替换；QA48/49菜单正常退出，图片服务停止。
- 本轮11个构建输入改变，WPF App、NFO writer、保存/文件事务及BatchSavePathConflicts源码hash/mode保持；历史未提交WIP保留，未reset、commit或push。

运行：退出旧版后打开会话 **outputs/Mac-source-cache-2026-10-24/JavMetaLite.app**（Apple Silicon、macOS15+）。相邻ZIP、manifest、results与`screenshots/INDEX.md`含截图、失败到通过日志、缓存探针和磁盘回读。`qa49-id-edited`为提交前草稿，旧摘要此时尚未刷新；以之后查询/来源菜单/保存及回读为准。

**未重验：** 完整四语/最小窗口原生矩阵沿用build42历史证据，不宣称43逐语重验；X03/R08全部次级状态、live站点/Cloudflare、物理IME/完整VoiceOver/手势、长期RSS/大影片压力、Windows/Intel/macOS15/干净Mac、物理外接卷/SMB/满盘/断电、Developer ID/Gatekeeper继续待验。不公证。

输入digest：`4d5ded3b125d72669256db6154a80e06eb83663dab278f2b6eaa0f7ff64e8e10`。ZIP SHA256：`a6c39e9f6f582167037ffef7a813a3395cea5e57f3eb3fa75d9736469fd02e3c`。

---

# JavMetaLite macOS — build42四语与原生布局回归

更新：2026-10-24，America/Los_Angeles。当前交付 **App0.4.0/build42/arm64，ad-hoc签名，不公证**。本轮在Apple M5/macOS27.0.1以隔离合成夹具完成四语主页面、真实最小窗口和深色冲突窗口原生检查；不是完整全平台验收。详见 [NATIVE-LAYOUT-QA.md](NATIVE-LAYOUT-QA.md)。

- 修复切换语言后来源菜单当前值滞留旧语言；仅重建该控件，草稿/勾选保持。补齐四语跨影片路径冲突与覆盖确认说明，路径和未知诊断原文保持。
- 最终42完整10套门禁exit0：Core51/583断言、Swift60、协议71（生产50+受控21）、queue4、retention5、发布工具36/manifest15；443 keys/1802四语检查。最终ZIP解包生产50/queue4/retention5与签名、17原生文件依赖/资源、无SDK检查通过。
- 最终42原生16组合＝四语×明暗×常规/最小；常规2560×1640px，实际最小1842×1346px，双图、工具栏和保存设置可用。四语最小深色图片页、双图批量概览及元数据底部可达均留图；来源标签无需打开菜单即切换。
- 四语生产Worker批次冲突：长路径完整换行、普通/选中文字可读、确认禁用；明确允许覆盖与跳过预览均不能绕过跨影片冲突。取消后编辑及四语切换保留草稿、勾选范围；退出仍保护未保存编辑。
- 最终42安全保存：覆盖关闭时20文件不变；取消保留编辑；明确允许后只改TEST-931 NFO标题，其余XML字节及19文件SHA/bytes/mtime不变。后续整个原生矩阵/冲突/退出未再写入，目标为空，仅一份已完成安全保存事务。
- Core/Worker/WPF源码hash/mode相对本轮基线未变；保留revision、NFO round-trip、全队列冲突预检、指纹与文件事务。未覆盖历史WIP，未reset、commit或push。QA46/47正常退出。

运行：退出旧版后打开会话 **outputs/Mac-native-layout-2026-10-24/JavMetaLite.app**（Apple Silicon、macOS15+）。相邻ZIP、manifest、results及`screenshots/INDEX.md`包含证据。共48图：01–06为build41问题复现；其余42图为最终42，代码/资源/生产Worker/Core与交付绑定，没有验收后重建替换。

**仍未完成：** X03/R08覆盖范围扩大但所有次级弹窗/状态/长文案组合尚未穷尽，40项分类不整体上调；日文最小侧栏清空按钮有原生省略显示，完整AX名称保留。物理IME/完整VoiceOver/手势、多显示器、长期压力、live站点、Windows/Intel/macOS15/干净Mac、物理外接卷/SMB/满盘/断电、Developer ID/Gatekeeper仍未重验；不公证。

输入digest：`cda3572b26910d5fa634b54104387c8c75516e546f2edb79ae174f0e4fd9915b`。ZIP SHA256：`6e54a9a73c82965480f4d139561a970b25cb908b60f35687ffe33f41d993b0c7`。

---

# JavMetaLite macOS — build41首次预览与取消恢复

更新：2026-10-24，America/Los_Angeles。**0.4.0/build41/arm64，ad-hoc签名，不公证。** 首次保存预览及取消后恢复编辑已完成修复和原生实测。详见 [PREVIEW-RECOVERY-QA.md](PREVIEW-RECOVERY-QA.md)。

- 生产build40新身份复现并取得托管栈：路径歧义检查的OpenDir持有工作区锁，状态查询阻塞在Monitor.Enter。已有影片/所选根目录改用Darwin实际名称元数据校验，避免枚举未选父目录；文件和新建/迁移目录的歧义保护继续保留，不归因为未经证实的OS/TCC缺陷。
- 预览读取/全队列保护/指纹计算使用审核快照并移出锁。取消释放busy，迟到结果/失败不能发布计划或替换新编辑；最多一个未返回只读检查，期间新预览明确JOB_BUSY，避免堆积。配置/revision在验证成功发布时绑定，源变动/取消不提前修改revision。
- 原生取消按正常结果处理，保留标题和勾选、不弹失败警告、不重发保存。revision、NFO round-trip、冲突预检和事务/journal保持；WPF、MovieJob、NFO writer、BatchSavePathConflicts和Saving.cs源码hash/mode未变，历史WIP保留，未reset/commit/push。
- 最后完整10套exit0：Core51/583断言、Swift59、协议71（50生产+21受控）、queue4、retention5、发布工具36/manifest15；441 keys/1794四语检查。APFS大小写敏感镜像52/602断言、完整SHA跨设备复制及取消通过，不冒充物理外接盘。
- 最终包签名、资源/17 native依赖、无SDK和ZIP解包生产50/queue4/retention5通过，与原生实测代码/资源/Worker/Core绑定。
- QA44生产Worker全新身份，仅选择叶子影片目录，第一次保存正常出现预览（一次命令/AX观察约1.1秒）；覆盖关闭阻断且8文件不变；允许后实际保存，只改1 NFO标题，其他XML及7文件完全保持。
- QA45受控非取消读取尚未放行时，取消约0.6秒工具观察已恢复编辑，8文件不变且无journal。继续编辑后放行迟到读取，新标题保留；重新预览仍受覆盖保护，明确允许后只保存新标题，其余XML及7文件不变。QA44/45已正常退出。

产物：会话 **outputs/Mac-preview-recovery-2026-10-24/**（App、ZIP、manifest、results、10张截图）；旧38/40交付保持。截图01–02为旧40，03–10为最终41。原生时序观测不是内部延迟统计；受控gate不等于真实断盘。尚未验证的Windows/Intel/macOS15、干净Mac、物理IME/完整VoiceOver/手势、所有外接卷/SMB/满盘/断电、live站点/Cloudflare、完整四语原生矩阵、Developer ID/Gatekeeper继续明确保留；不公证。

输入digest：`2ed3b50a6777a78627eff109169a97885ee60d6ef12733614d714a9282fdde8a`。最终归档hash及证据见results/verification-summary.json。

---

# JavMetaLite macOS — build40回归复跑与修复候选

更新：2026-10-24，America/Los_Angeles。当前产物 **0.4.0/build40/arm64，仅ad-hoc签名，不公证**。本轮重跑历史门禁并修复两项可复现交互问题；**独立原生QA41首次预览超时仍未解决，不宣称完整发布验收通过**。详见 [REGRESSION-REVIEW-QA.md](REGRESSION-REVIEW-QA.md)。

- 搜索支持未保存的当前标题/番号、原文件名及首尾空白；不改变查看、草稿与勾选。四语新增悬停提示。
- 查看器切换来源取消旧的viewer-owned下载，保留主页面请求及完成缓存。两项新增回归先失败、修复后通过；最终40原生切源时旧请求在1字节后关闭，服务端gate仍未放行。
- 最后完整10套门禁exit0：Swift58、Core50/579断言、协议69（50生产+19固定HTTP）、queue4、retention5、发布工具36/manifest15；441 keys/1794四语检查。APFS大小写敏感镜像Core51/598断言、完整SHA复制/取消通过，不冒充物理外接卷。
- 最终生产包签名/资源/17 native依赖/无SDK验证和ZIP解包生产50/queue4/retention5通过。交付与最终编译前端/资源/Worker/Core绑定。保留revision、NFO round-trip、全队列冲突预检与文件事务；9个Mac构建输入变化，Core/Worker/WPF及保护源码hash/mode未变，未覆盖历史WIP、reset、commit或push。
- build39原生：覆盖关闭时阻断且8文件不变；允许后仅1份NFO的title字节改变，其余XML及7文件保留。四语实际缩小至1842×1346px，双图和保存/批量控件可用；最终40布局/四语资源同hash，未冒充逐语重验。
- 最终40千片原生：导入、末尾搜索、筛选下全选1000→0→1、清空通过；2000文件SHA/bytes/mtime不变，没有执行千片网络查询/保存。
- 性能为本机极小合成文件的生产Worker观测：当前发现/确认1000片约1.1秒；旧兼容导入约36.1秒，列为后续优化；不代表网络或大影片吞吐。

**仍需优先处理：** QA41首次预览超时，采样停在SystemNative_OpenDir，根因未确定。未执行/重试保存，草稿、勾选和8个磁盘文件保持；请求取消未获确认，正常退出被未知状态保护阻止，QA41保留并最小化。截图10/14与sample完整保留。不能用自动测试通过或其他QA保存通过抵消此未验收项。

运行/证据在会话 **outputs/Mac-regression-review-2026-10-24/**：候选App、ZIP、manifest、results、14张截图；旧build38保留。01–09为build39，10–14为最终40，不混淆版本。QA40/QA42已正常退出，图片服务已停止。物理IME/完整VoiceOver/手势、完整四语弹窗和深色冲突、live网站、Windows/Intel/macOS15/干净Mac、外接卷/SMB/满盘/断电、Developer ID/Gatekeeper仍未重验；没有公证。

输入digest：`2ba1dfa17b2d739d35242758bde55bd68d0d9036bf368ec27835186459f98a73`。归档hash和全部证据见results/verification-summary.json。

---

# JavMetaLite macOS — build38复选框统一批量范围

更新：2026-10-24，America/Los_Angeles。当前 **App0.4.0/build38/arm64，仅ad-hoc签名，不公证**。本轮用户指定的查看/勾选/批量范围已实施并完成实际闭环。详见 [CHECKED-BATCH-QA.md](CHECKED-BATCH-QA.md)。

## 当前交互

- 点击影片行只切换查看、编辑；左侧原生复选框唯一决定查询与保存对象。零勾选时禁用，不回退到当前影片。
- 全选/取消全选作用于整个队列的复选框，包括筛选隐藏项；不改变当前查看及草稿。队列焦点下⌘A全选、⇧⌘A取消、Space切换当前影片勾选，文本框⌘A仍选择文字。
- 中间独立批量概览展示已勾选影片及每片双图；已删除“勾选选中项/取消勾选选中项”。点击行/番号或返回当前影片进入单片编辑，勾选保留。
- 工具栏、菜单与保存设置明确显示“查询已勾选3部”“保存已勾选3部”及一致范围。移除绕过复选框的直接全队列查询，仍可先勾选已识别影片；重试/分片确认也受勾选约束。
- revision、NFO round-trip、全队列冲突预检、计划指纹与文件事务保留。12个Mac构建输入变化；Core、Worker、WPF、MovieJob、NFO writer、BatchSavePathConflicts、事务/journal源码hash/mode未变，未覆盖历史WIP、reset、commit或push。

## 实际证据

| 范围 | 结果 |
| --- | --- |
| 自动测试 | 完整10套exit0；Core50/579断言、协议69（50生产+19受控）、queue4、retention5、发布工具36/manifest15；四语440 keys/1790检查。最后38全量Swift56再次通过。 |
| 最终生产包 | 签名/17 native文件entitlements/依赖/资源/无SDK验证，以及ZIP解包生产50、queue4、retention5通过；交付清单/签名与来源绑定通过。 |
| 最终38原生 | 四部合成夹具，勾选三部、查看第四部；每次勾选保持第四部草稿，批量概览只显示三对图片，旧控件不存在；全选/取消保持编辑，键盘4→0→1→0通过。 |
| 查询与保存 | 关闭覆盖正确阻断且16文件不变；明确允许后只保存前三部，三份NFO仅title字节改变；其余13文件SHA/bytes/mtime不变，含第四部全部文件。后续受控查询仅三部返回结果，第四部草稿不变；查询/清空未额外写盘。 |
| QA边界 | 原生QA39为最终38同编译前端/资源/生产Workspace与Core，独立身份、非交付固定HTTP宿主；不冒充live查询。QA39已正常退出。21–29是最终38图，候选36/37历史分开保留。 |

## 运行与限制

退出旧版后打开会话 **outputs/Mac-checked-batch-2026-10-24/JavMetaLite.app**（Apple Silicon、macOS15+，Worker自包含）。相邻ZIP、manifest、results包含门禁/打包日志、文件回读和原生截图，推荐21勾选/查看分离、22三片双图、25保存结果、27第四片保持。

本轮原生为简中正常窗口；拖角未实际缩小，仍2560×1640，未冒充最小窗口通过。完整四语原生矩阵、live网站、物理IME/完整VoiceOver、Windows/Intel/macOS15、外接卷/SMB/满盘/断电、干净Mac、Developer ID/Gatekeeper未重验；历史首次预览延迟仍未定因。QA37测试配置读取超时已保留sample，改用私有/tmp配置的QA39完成；不将该变化当作系统根因证明。未进行公证。

最终输入digest：`7eadc61a00b54ae2f141462ed2b8b40cad700e7848847b87cea6916b708a158d`。ZIP SHA256：`394b6721dda7eb48c5ae12cc0b98639a8887acac919f2fd4abeb018c4bb7a65b`。

---

# JavMetaLite macOS — build35选图与首次R18手动导入

更新：2026-10-24，America/Los_Angeles。当前 **App0.4.0/build35/arm64，仅ad-hoc签名，不公证**。两项反馈已修复并完成实际原生复验，详细版本和证据边界见 [ARTWORK-FLASH-R18-QA.md](ARTWORK-FLASH-R18-QA.md)。

## 实际改动

- 样张勾选、封套来源按钮改为独立串行审核队列，不切换全局忙碌、不把整页控件变灰、不插入全局进度条。已有图片缓存与进行中的内容请求保留，勾选即时显示。
- 后续查询/切源/保存等动作等待审核确认，使用最新revision；审核失败阻止紧接的保存。清空/移除不可与在途审核竞争，关闭/退出先等确认再检查未保存状态。
- 未查询过的R18在首次点来源时定位真实content ID后打开正确详情页；仅定位地址，不应用资料。定位可取消，旧来源/关闭/旧revision或错误番号的结果不能被采用。失败时保留站内搜索和重试，首页不再泛称“来源不支持”。
- 成功导入仍在Worker确认后自动关窗。revision、NFO round-trip、冲突预检、计划指纹及文件事务保持；Core和Windows源码未动。

## 实际测试与回读

| 范围 | 结果 |
| --- | --- |
| 自动门禁 | 10套exit0；434 keys/1766本地化检查、Core50/579断言（含补充私有案例）、协议69＝50生产+19固定HTTP、queue4、retention5、发布工具36/manifest15。最后35的Swift54单独全量复验通过。 |
| 最终生产包 | 签名、17 native文件entitlements/资源/依赖/无SDK启动，以及解包生产50/queue4/retention5通过。交付清单与签名校验通过。 |
| 最终35选图 | 本地8次、真实R18缓存4次，查询/保存保持可用；本地/R18封套按钮两方向切换通过。自动测试确认样张/封套选择不发布全局working=true；不把静态图当作逐帧测量。 |
| 最终35实时导入 | 没有先执行自动抓取，首次R18直接到STARS-804的 `id=1stars804/` 详情；实际导入更新资料并自动关窗。生产Worker，非固定HTTP来源。 |
| 最终35保存 | 覆盖关闭时阻止，取消保留草稿；明确开启后1 NFO Update+5 Keep保存成功，仅title字节改变，其他NFO原始字节及5文件SHA/bytes/mtime不变，无增删。后续网页/图片审核与清空不再写盘。 |
| 来源/退出 | QA35前端去签名代码、资源、Worker/Core DLL与交付35绑定；仅QA身份和私有dataRoot shim。QA34和35均正常退出。 |

16个构建输入变化，Core/WPF/MovieJob/NFO writer/BatchSavePathConflicts与事务/journal的源码hash/mode未改。未覆盖历史WIP、未reset/commit/push，原33交付保留。

## 运行与截图

退出旧版后，打开会话 **outputs/Mac-artwork-r18-2026-10-24/JavMetaLite.app**（Apple Silicon、macOS15+，Worker已自带）。同目录有ZIP、manifest、results和截图；11–20为最终35，推荐12选图、16保存、17首次R18详情、18导入关窗。34候选单列history-build34。

## 继续保留的边界

初次原生保存预览在首次观察时曾有等待，随后正确返回，第二次预览/保存正常；首因未定位，未宣称已修复，sample已保留。仅此真实STARS-804当次导入通过，长期站点稳定和Cloudflare不扩大结论。Windows/Intel/macOS15/干净Mac、物理IME/手势/完整VoiceOver、外接卷/SMB/满盘/断电、Developer ID/Gatekeeper继续未验证；没有公证。34的缺少标准uniqueid夹具产生既有规则补节点，35独立夹具完成除title外逐字节保留，两个结果分别记录。

最终输入digest：`3f69f4cd3eca9fa8391b9a397bb0cc1f59b16dc3f4e0acbcfd8f8cea24231de2`。ZIP SHA256：`4c0e187a0460b07d40867caec70aaa677b0dacb2002e3296745cf2a0f42221b0`。

---

# JavMetaLite macOS — build33缓存来源与选图交付

更新：2026-10-24，America/Los_Angeles。当前 **App0.4.0/build33/arm64/ad-hoc签名，不公证**。本轮四项交互改动已实施，完整门禁、签名解包及原生合成夹具验收完成。详情与版本边界见 [CACHED-SOURCE-SELECTION-QA.md](CACHED-SOURCE-SELECTION-QA.md)。

## 实际改动

- 查询菜单移除“LibreDMM + R18.dev”，保留自定义来源；入口改为“设置自定义来源”。旧all协议仅留兼容。
- 主页面双图右上角来源切换整部影片的本地/缓存资料，采用该来源全部可用字段及图片；缺失字段保留现值、手动候选保留。网址/内容编号也随来源恢复，切回本地不会把线上来源信息意外写入NFO。仅显示实际已载入来源，不发起额外查询。
- 选图和普通字段审核不再清空未变化的图片。已加载和进行中预览按内容身份复用；替换/删除图片、变更位置或过期owner仍被清理/拒绝。审核revision、NFO round-trip、冲突预检和文件事务保持有效。
- 更多选项改为文字/箭头/空白整行可点击按钮，并提供展开/收起AX状态；四语431项完整，保存默认预览与跳过预览选项保留。

## 实际证据

| 范围 | 结果 |
| --- | --- |
| 完整门禁 | source33-gate.log：10/10套exit0；431 keys/1754检查；Core50/579断言、Swift54、协议67（50生产+17受控provider）、queue4、retention5；发布工具36、manifest15 |
| 生产包/解包 | source33-package.log：签名/17 native entitlements/资源/依赖/无SDK Worker通过；解包生产协议50、queue4、retention5通过，仅ad-hoc，无公证 |
| 原生切源 | 本地、LibreDMM、R18三份资料可整部切换，字段和双图更新；查询菜单/配置名称正确。使用受控JSON与回环图片服务，未冒充真实网站查询 |
| 原生连续选图 | 两张样张交替勾选8次，图片请求数12→12、新增0次，四张预览每次均保留；切源/选图前6个夹具文件完全不变 |
| 原生保存回读 | 33覆盖关闭时正确阻止；明确允许后预览1 Update+5 Keep并完成保存。仅NFO标题变化，除此以外NFO原始字节全部保留，视频/4图片共5文件SHA/bytes/mtime不变，无增删 |
| 更多选项/布局 | 正常/最小窗口展开收起通过；直接点文字展开、点行内右侧空白收起均已验证。33图11–23为最终前端/资源；32图01–10保留为前期证据 |
| 身份绑定 | QA33与正式33前端去签名代码、全部资源、Worker/Core DLL同hash；交付App逐项payload/签名属性与正式包一致。测试host/私有状态不交付 |

32原生回读曾发现切回本地仍携带线上URL/ContentId，33已修正并增加断言；没有将32的NFO差异冒充完整round-trip成功。输入284项中19项变化，无增删；Core、WPF、MovieJob、NFO writer、BatchSavePathConflicts与事务/journal源码hash/mode与开工前相同，未覆盖历史WIP，未reset/commit/push。原31交付保留。

最终输入digest：`effbc8e4fe876820404ffda31dc0e8b634c1f7b5330c40b08e9d1bbec17dc331`。ZIP SHA256：`345f13d089750efc5f7f73c4c703eb46962d862db7455d1deec01672fff0c442`。

## 运行与产物

退出当前旧版，打开会话 **outputs/Mac-source-switch-2026-10-24/JavMetaLite.app**。Apple Silicon、macOS15+，Worker自包含无需SDK。相邻目录有ZIP、manifest、results/verification-summary.json、测试日志、回读与23张实际原生截图；推荐查看12来源菜单、16连续选图后、20保存主页面、21最小窗口、23文字点击。

## 未验证

本轮未重测真实网站/Cloudflare、物理手势/IME/完整VoiceOver、Windows实机/Xcode/Intel/macOS15、外接卷/SMB/满盘/断电、Developer ID/Gatekeeper或未保存退出弹窗聚焦。仍只签名、不公证；旧项目边界按下方历史记录保留，不扩大验收范围。

---

# JavMetaLite macOS — build31交互改进交付

更新：2026-10-24，America/Los_Angeles。当前 **App 0.4.0 / build31 / arm64 / ad-hoc签名，不公证**。本轮反馈已落地，完成可执行门禁、签名/解包验证和隔离合成夹具原生验收。没有覆盖历史WIP、reset、commit或push。详细记录见 [INTERACTION-REFINEMENT-QA.md](INTERACTION-REFINEMENT-QA.md)。

## 实际改动

- 隐藏两项NFO保留字段；原NFO内容仍round-trip保留。主页面继续同时展示裁切海报与完整封套。
- 右上角及⌘S统一为“保存”，默认开启预览；“更多选项”可明确开启跳过预览。省略界面确认后仍创建并校验Worker计划，revision、指纹、全队列/批次冲突预检与文件事务保持有效。未经允许的已有NFO/图片覆盖仍阻止整个批次。
- 队列工具栏与⌘⌥L可隐藏，⌘F恢复并聚焦。新查询显示本次来源的最新内容，旧手动值及未查询来源保留候选；重试保留已审核选择。新增12字段LibreDMM/R18.dev自定义来源配置，非法协议输入在任何队列/网络变更前拒绝。
- 网页导入完成并收到Worker确认后自动关闭。WebKit保留应用内站点状态，使用标准UA，允许验证子frame所需的about:blank/srcdoc；顶层/抓取仍限HTTP(S)。补齐JavLibrary现行jav….html详情地址，仍验证实际页面番号、2MiB和revision。
- 图片查看器左侧关闭、两侧切图、实际尺寸与实时百分比；原生画布实现滚轮/捏合缩放及触控板平移。物理手势验证单独列为待测。
- 使用原有Windows品牌图案生成ICNS，注册SwiftPM/Xcode/包资源与Dock图标。426项四语资源完整，旧“必须预览确认”说明已同步更正。

## 已执行证据

| 范围 | 结果 |
| --- | --- |
| 完整代码门禁 | `interaction31-final-gate.log`：10/10套exit0；Core50/579断言、Swift52（18+34）、协议65（49生产+16固定HTTP）、queue4、retention5；发布工具36、manifest15 |
| 最后四语说明修正 | 功能源码不再变化；`interaction31-final-ja-localization.log`：426 keys/1734真实Bundle检查通过，再生成最终签名包并原生核对日文说明 |
| 最终签名/解包 | `interaction31-delivery-package.log`：签名、17 native文件entitlements/依赖/资源/manifest/no-SDK Worker通过；解包生产协议49、queue4、retention5通过。只有ad-hoc，无Developer ID或公证声明 |
| 原生默认与跳过 | 默认保存先展示预览；skip打开但覆盖关闭仍阻断。首轮两片批次阻断时11文件完全不变，无部分新建；明确允许后直接完成两片 |
| 两片文件回读 | 001更新NFO、002新建NFO；10个其他文件SHA/bytes/mtime不变。后续正式签名复验仅修改两份预期标题、第三片保持不变，原001非title XML完整保留 |
| 最终交付字节复验 | 最后日文说明打包后再次启动：真实导入/编辑/直接保存成功；001 NFO除title外原始字节全部相同，其他11文件SHA/bytes/mtime不变，无增删；新图23–25。前端去签名代码、全部资源和Worker/Core DLL与交付包一致；私有数据根shim及QA身份明确隔离 |
| 实时网页 | build29真实LibreDMM IPX-081导入自动关窗；build31用户本人确认网站后，JavLibrary搜索→正确`/cn/javli43n4q.html`详情→导入→自动关窗→重开同片详情通过。此次未见Cloudflare循环，未自动处理CAPTCHA；不保证长期通过。完整退出App后网站可能再次要求条款 |
| 原生布局与图片 | 队列隐藏/⌘F恢复、正常/最小窗口、两侧箭头1/2↔2/2、实际尺寸100%、125%缩放、12字段配置、默认预览及批量范围已操作；最终主页面23、查看器24、日文25为最后交付字节截图 |

最终输入284项，对照开工前283项仅28项变化并新增ICNS；WPF源码hash/mode不变，Core仅BrowserPageImporter改动。MovieJob、NFO写入器、BatchSavePathConflicts、事务/journal相关源码与开工前相同。用户原build28会话PID24526/Worker24529保持运行，未操作它们。

最终输入digest：`a4791f5035d651c28a6c29098237d97ce4af68b622c966f8ee331277dc3e766e`。ZIP SHA256：`97ed20c42844e06a0d1e0603bc92592b246ca3d1538fdb064eaaf76f63d5acb2`。

## 运行与产物

退出当前旧版后，打开会话 **outputs/Mac-interaction-next-2026-10-24/JavMetaLite.app**（Apple Silicon，macOS15+，Worker已自带，无需SDK）。同目录包含约40MiB ZIP、manifest、results/verification-summary.json、全部测试日志与25张实际窗口截图。原build28交付保留；QA身份与shim不交付。

## 尚未验证与已知边界

物理滚轮/双指捏合、完整VoiceOver、物理中文/日文IME、Windows实机、Xcode、Intel/macOS15、外接卷/SMB/满盘/断电、干净Mac、Developer ID/Gatekeeper及长期站点稳定性未验证。原生API测试不等于这些实机验收。

QA29候选的未保存退出提示无法被CUA正确聚焦/读取，AX后续动作造成嵌套NSAlert模态循环，已保留进程sample并仅对QA29 PID29635通过活动监视器强制退出，Worker随EOF结束。最终QA31在无草稿或明确清空测试草稿后正常退出。**未保存退出提示焦点仍待人工复查，未宣称已修复。** 不将旧版本截图或站点会话结果冒充最后重新打包产物的同一项实测；来源及边界详见QA文档。

---

# JavMetaLite macOS — build28经典编辑器交付

更新：2026-10-23，America/Los_Angeles（最终证据 UTC 2026-10-24）。当前 **App 0.4.0 / build28 / arm64 / ad-hoc签名，不公证**。已实施确认的经典编辑器，并完成完整可执行测试、正式包/解包、独立身份的本机原生业务验收和真实截图。锁屏期间的待测记录保留在 [build25阶段记录](STATUS-classic-baseline-build25.md)，解锁后继续完成。详细改动、来源绑定、截图索引和边界见 [CLASSIC-EDITOR-QA.md](CLASSIC-EDITOR-QA.md)。

## 当前实现

- 原生统一工具栏，添加、查询/更多查询、预览保存靠右，保存右栏默认展开；直接全选进入批量概览，范围与数量明确，清空仍需确认。
- 主页面同时显示审核来源的海报与完整封套；切到图片页仍保留双图。批量行有成对缩略图，队列和概览立即显示草稿标题。
- 资料字段全部默认展开，保留原生文本桥、Undo/Redo、候选比对及只读NFO字段；没有新增无效编辑字段。
- 右栏提供输出方案，视频重命名/NFO标题前加番号/覆盖NFO与图片合并为“更多选项”，解释使用原生Help提示；四语资源完整。查询按钮的辅助功能名称随语言和数量更新。
- 对照开工前283项输入快照，仅15项Mac输入改变；Core、Worker、Windows及事务相关源码/hash/mode未改。MovieJob revision、NFO round-trip、全队列/批量冲突预检、计划token/指纹及文件事务/journal保留。未reset、commit或push，未操作用户原build24会话。

## 最终证据

| 范围 | 实际结果 |
| --- | --- |
| 完整门禁 | `classic28-final-gate.log`：10/10套exit0；412 keys/1678检查、发布工具36、manifest15、Core50项/559断言、Swift48（18+30）、协议63（49生产+14受控provider）、queue4、retention5 |
| 正式包/解包 | 签名/17 native文件entitlements/依赖/资源及无SDK Worker通过；解包生产协议49、queue4、retention5通过；仅ad-hoc，不公证 |
| 原生四语/布局 | QA27四语言最小单片09–12，实时草稿批量13、预览/结果14–15、图片页双图16；28仅新增动态AX标签，实际复验英语/简中名称及0→1→2→0数量，28正常/最小截图17–23，未把27截图冒充28 |
| 最终28原生闭环 | 发现确认3片、Unicode粘贴与Undo/Redo、双选2片；关闭覆盖时预览禁用确认，取消保留草稿；开启后重新预览1 NFO Update+7 Keep、保存2完成；关闭结果后动作可用，主图打开/关闭原生查看器；清空Esc保留3，再确认3/0未保存归零，正常退出 |
| 最终28文件回读 | 13文件仅001 NFO改变，精确标题“DEMO-001 · 经典编辑器 · build28 安全保存”；其他12文件SHA/bytes/mtime不变，无新增/删除、未选第三片完全不变。全部非title XML语义不变；清空不删文件 |
| 来源/交付绑定 | QA是独立身份和仅改数据根argv的shim，使用生产Worker，无provider替换；最终QA28去签名前端代码与交付28同SHA，Worker/Core DLL同SHA。交付App全部payload hash/mode/签名属性与正式包一致 |

构建283项输入digest：`3b243ff62fffa56e947d56d42565c78f893138edf54a9efd05526717de97a56f`。ZIP SHA256：`fcdbd627cf1aaadddd6a1a41f4532b1090174c544f242bf7567b90c8a60f2316`。

交付根为会话 `outputs/Mac-classic-editor-2026-10-24/`，含最终 `JavMetaLite.app`、ZIP、`results/verification-summary.json` 和 `screenshots/`。**主页面22、批量18、最小窗口21、保存19–20均为最终28原生截图**。原25包保留在 `history/build25/`。退出旧版后打开新版App即可手测；验收QA bundle不交付。

## 继续如实保留的边界

物理中文/日文IME、完整VoiceOver、真实外接卷/SMB/断电/满盘、Intel/macOS15、Windows实机、干净Mac、长期稳定、Developer ID与Gatekeeper未验证。本轮Unicode使用原生剪贴板，不冒充物理输入法；选项AX Help已核对，没有量化鼠标悬停延时。未执行本轮live provider/网页验收，JAVLibrary Cloudflare按用户要求保留手动验证；未进行公证。本机无有效Developer ID，ad-hoc不代表Developer ID分发。build24负几何日志历史未宣称根除。原40项矩阵继续按各次证据保留，不扩展为40项全部通过。

---

# build24 历史验收（原记录保留）

更新：2026-10-23，America/Los_Angeles。当前 **App0.4.0 / build24，arm64，仅ad-hoc签名，不公证**。统一三栏工作区、直接批量动作、默认右栏和清空入口已实施；build24完整门禁、签名打包及独立源码重建通过。candidate24和最终shipping24分别完成本机切源及单片保存；最终交付App的完整219项清单与已原生实操的shipping24一致。两次二进制不同，分别记录。build23完整记录保留为历史。

## build24当前证据

三来源Picker已换为显式按钮，提供当前来源着色与AX选中态；同步chooseSource/generation/revision保护不变。QA24历史22条发布期警告经backtrace/atos定位为来源Picker同步发布，并非sheet问题。Browser2+Store4+Startup3定向9/9通过，1.246秒；全量Swift仍46。283项构建输入digest为`c2e5c3ae21e40429ce338c133ac8417b77fc2bf753c06a752e5cc5bd38420eb3`。

| 范围 | 实际结果/边界 |
| --- | --- |
| 完整门禁 | `gate24-summary.json`：10/10套exit0，200.758秒；399 keys/1626检查、发布工具36、manifest15、Core50项/559断言、Swift46（17+29）、协议63（49生产+14受控provider）、queue4、retention5；构建输入起止不变 |
| 正式ad-hoc包/解包 | `package24-summary.json`：175.315秒通过；解包生产协议49、queue4、retention5和签名/依赖/manifest/no-SDK Worker验证通过；41,464,845 bytes，ZIP SHA256 `f43ff52deb15141032c814359e205e3b630944c8f0bdda8b613c800f94315da3` |
| candidate24原生操作 | 02:09:07.759–02:11:18.463 UTC，App96501/Worker96504；1.5674秒ready、默认右栏。发现确认1片→8次来源按钮切换（AX选中态/地址正确）→Esc恢复动作→标题Undo/Redo→NFO预览1 Update+4 Keep→保存1→结果Close立即恢复动作→清空Esc保持1、再确认1/0未保存归0→正常⌘Q |
| candidate24回读与日志 | 5文件仅预期NFO改变，未知XML/注释/root属性/演员扩展保留，其余4文件SHA/bytes/mtime不变，无新增/删除；默认settings前后均不存在，App/Worker已退出。精确PID有界日志为0发布期状态修改、186负几何、0约束冲突，负几何仍未解决 |
| 最终shipping24原生 | App644/Worker649，1.3518秒ready、默认右栏；发现确认1片→6次来源按钮切换→Undo/Redo→覆盖关闭时阻断预览→取消保留草稿→明确开启覆盖后NFO仅1 Update+4 Keep→保存1→结果Close立即恢复动作→清空Esc保持1、再确认1/0未保存归0→正常退出；截图26–29 |
| 最终shipping24回读/日志 | 5文件仅预期NFO改变，精确标题“Final shipping24 — 正式签名包安全保存”；未知XML/注释/root属性/原演员roles及扩展语义保留，其余4文件SHA/bytes/mtime不变，无新增/删除；settings前后不存在，App/Worker均退出。PID644在02:33:12–02:36:19.805 UTC记录10660实际事件：发布期修改0、负几何198、约束冲突0；不声称日志全干净 |
| 最终包绑定 | candidate去签名前端SHA `b90b33d3…`，shipping为`ec03cb66…`，并不相同；同283构建输入。交付App全部219项内容/hash/mode/签名属性与真正实操的不可变shipping24一致，deep/strict签名验证通过；以最终shipping自身原生记录验收 |
| 独立源码重建 | 224.676秒通过，同283项digest；无.git中文/空格路径重建、解包49生产协议+queue4+retention5通过。使用WIP文档源码，未证明二进制相同，不冒充最终源码ZIP |
| 当前网页限制 | LibreDMM合成番号未找到，R18无contentID进入home而不支持导入，JAVLibrary仍CF挑战，导入始终禁用；未操作CAPTCHA/外部打开/live导入。HTTPS主页面的子frame被阻止时仍会显示“只允许HTTP/HTTPS”泛化提示，文案问题未修 |

截图23–25与`results/native24-operation-record.json`记录candidate24；26–29及`shipping24-final-*`报告记录真正最终shipping24；11–22明确为build23历史。完整证据见 [INTERACTION-REDESIGN.md](INTERACTION-REDESIGN.md)。

基线为`feature/macos-native`、HEAD `874b2dd524106189806e4d0c86589a0c8ee142c1`；保留原有未提交实现，未reset/commit/push或发布Release，未发现适用AGENTS.md。build19原STATUS见 [STATUS-interaction-baseline-build19.md](STATUS-interaction-baseline-build19.md)，详细本轮证据见 [INTERACTION-REDESIGN.md](INTERACTION-REDESIGN.md)。原40项矩阵的 **33通过、6待测、1不适用** 仍仅限已记录的build19环境；T03、X03、X09、R05、R08、R10不因本轮交互验收自动改写。

## 实际改动

- 主窗口改为全宽两行动作条、`HSplitView`三栏和全宽底栏；保存设置启动默认展开。编辑器采用紧凑摘要与资料分区，图库保留有限列宽的按行懒加载。
- 查询/预览/保存栏统一范围：多选导航→已勾选→当前；⌘R查询，⌘S预览，⇧⌘R查询全部已识别。命令先提交编辑并处理延迟导航；导入不自动联网。
- 清空队列按钮与⌥⌘Delete先确认，显示数量/未保存项，取消为安全默认。整组Worker校验与完整ack后才清本地草稿，只移除会话条目。
- 单片网页入口与⌥⌘B有共同guard；三来源明确可见，初次/切源自动加载，正常HTTP(S)新窗口链接回当前页，HTTP失败有说明/重试/外部浏览器入口。外部浏览器不自动回传。
- 修复启动失败后缺少显式重试，以及关闭结果sheet后动作持续禁用；发布AppKit sheet生命周期并转发原delegate。导入/来源重试增加模态保护。

MovieJob revision、页面/任务身份、NFO round-trip、全队列冲突预检和事务/journal继续保留；WPF客户端保留。网页导入不直接写盘，清空不调用保存或废纸篓服务。

## build23历史测试和本机验收

| 范围 | 实际结果 |
| --- | --- |
| build23完整门禁 | 10/10套exit0，170.596秒；本地化399 keys/1626检查，发布工具36、manifest15、Core50项/559断言、Swift46（17+29）、协议63（49生产+14受控provider）、queue4、retention5 |
| 正式ad-hoc包/解包 | 250.828秒通过；解包后的生产协议49、queue4、retention5，以及17个native文件签名/依赖/manifest/no-SDK Worker验证通过；构建输入起止一致 |
| 最终前端原生QA24 | 与production23去签名前端SHA完全一致；多选B/C查询→关闭结果后立即⌘S→保存2片，A排除；清勾选后A单片Undo/Redo并保存1片，B/C排除；R18“全部已识别”3片完成且不自动保存 |
| 正式生产App原生单片 | 默认Library/正式Worker，无RegressionHost；OpenPanel发现确认1片→编辑标题→仅NFO预览→确认保存1片→清空会话/正常退出已完成；5文件仅NFO改变，未知XML/演员扩展保留，其余4文件SHA/bytes/mtime不变；默认settings未创建，App/Worker已退出 |
| 文件回读 | 27文件基线仅3份预期NFO改变；未知XML/注释/root属性/演员扩展保留，非NFO字节/mtime不变；后续查询/图库/清空/冲突/退出未新增磁盘变化 |
| 四语/图库/清空/冲突 | 四语最小窗口截图1842×1346px，主动作与右栏底部可达；图库两列、样张取消保持；清空Esc保留，再确认归0；U/UC两片共享视频/NFO目标冲突时确认禁用，深色可读，取消后动作恢复 |
| 独立源码重建 | 无.git中文/空格路径重建271.882秒，283构建输入与正式包完全匹配，解包49+4+5通过；使用WIP文档源码归档，证明可重建，未证明二进制逐字节相同 |

原生日语“勾选可见”及英语原位置Picker出现系统省略号，完整文字可由AX读取且控件可操作；不声称四语全部无截断。QA24的受控HTTP/图片服务器验证了真实解析、原生操作与文件事务，不能当作live抓取结果。精确PID/时段统一日志交叉核验：QA24仍记录22条发布期状态修改/366条负几何；正式production23单片流程为0/54，约束冲突均0。这属于build23历史；24的来源按钮回归不再记录发布期状态修改，负几何仍未解决。严格path过滤曾因相同Mach-O UUID的路径解析而漏报，已改以PID有界结果为准，不能宣称日志干净。

## 明确保留的限制

- QA23使用任务Documents中的配置/dataRoot时，首次及两次显式重试仍initialize超时，sample位于`SystemNative_Open/open$NOCANCEL`，首因未定，不能归因为TCC或actor。QA24改独立身份及`/private/tmp`配置/dataRoot后首次连接0.901秒即ready；**原始shipping production23使用默认Library、无QA配置/host，首次连接1.3906秒即ready**。两者分别记录，不抹去失败。
- 三来源切换/自动导航已实操：合成TEST-930在LibreDMM未找到，R18详情404有失败说明，JAVLibrary为live Cloudflare挑战且导入禁用。未操作CAPTCHA，未完成三站live手动导入，未点击外部浏览器按钮；外部打开实机仍未验。此前只读HTTP探测三条JAVLibrary网址均403。
- 物理IME/完整VoiceOver、长按、多显示器、真实外接卷/SMB/拔盘/满盘/断电、Intel/macOS15、Windows实机、长期无泄漏、干净Mac、Developer ID和Gatekeeper未验证。没有执行公证；ad-hoc不代表Developer ID分发。

## build23历史产物与资产

build23当时原包记录：41,462,022 bytes，SHA256 `7fc899457595ba1bfe025bde5e97bb6a84e7652f5383371f6ef66b4015211a27`，见`package23-summary.json`；常规artifacts产物路径已被build24更新，不能按当前路径读取到23。当前App可双击启动，手动导入影片后再查询。

统一交付目录为任务根`outputs/Mac-interaction-redesign-2026-10-23/`；当前App与ZIP以build24交付记录为准，日志/JSON在`results/`。最终源码为`JavMetaLite-source-build24.zip`，逐文件hash/mode、完整归档清单及同283构建输入的绑定校验以`results/source24-export-validation.json`为准；不把WIP重建归档当最终源码。截图11–20为最终编译前端23的原生QA24，13为正常主窗口、14–17为四语最小、18为挑战阻断、20为深色冲突；21/22为正式production23单片预览/保存完成；01–09保留QA22历史，06为已修复缺陷证据，00/10保留启动失败。

用户`outputs/合成影片_手动测试_2026-10-23`原有92文件的最终只读核对显示SHA/bytes/mtime全部不变，无新增/删除；未恢复、覆盖或清空其手测结果。
