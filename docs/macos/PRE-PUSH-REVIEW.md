# mac分支推送前审查

2026-10-25，America/Los_Angeles。远端基线为Noredge/JavMetaLite的main提交`874b2dd524106189806e4d0c86589a0c8ee142c1`。本轮发布目标为独立`mac`分支，不合并或更改main。

## 审查结果与修正

- 核对保存设置记忆、预览确认/显式跳过预览、revision与取消恢复，以及Worker服务端计划、全队列冲突预检、NFO round-trip和事务边界。本轮没有发现新的可复现数据写入或取消恢复缺陷，没有修改这些运行时保护代码。
- 发现Xcode显式文件列表落后于SwiftPM：应用目标缺BrowserNavigationPolicy，测试目标缺浏览/启动/保存本地化/交互测试和TestPreferenceDomain。旧工程源文件完整性检查实际失败；补齐1个生产源文件、5个测试/辅助文件，并修正Xcode单模块编译下的测试导入。现在22个应用源文件、8个测试源文件注册一致，CLT按Xcode的非SWIFT_PACKAGE布局生成生产模块并typecheck测试通过。
- 新增`test_xcode_sources.py`并纳入门禁，防止后续新增Swift文件只进入SwiftPM、遗漏Xcode。该工具是源文件/编译布局检查，不冒充完整Xcode构建或Xcode宿主测试。
- macOS及Windows工作流均监听mac分支；保留main和手动触发，未增加自动公证/发布release。macOS runner仍使用官方标准Apple Silicon标签macos-15，并保留uname校验；runner类型依据[GitHub官方文档](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)核对，三个Mac action固定SHA与其官方v7/v6/v7标签一致。
- 修正README/macOS README中已过时的“必须显示预览/额外覆盖许可”说明，与build45当前行为一致。

## 实际验证

本机Apple M5/16GiB/macOS27.0.1(26A434)，CLT。完整11套门禁exit0：本地化、发布工具、manifest、portable、Worker编译、独立宿主编译、Swift、Xcode源文件/编译布局、协议、队列、保留预算。Core portable50/486断言；Swift68（20+48）；协议74（生产50+固定HTTP24）；queue4、retention5；发布工具36/manifest15；445keys/1810四语检查。

提交候选`bd3e08a341fa96f5730836f0cc455c89efb20c3b`另以`git clone --no-hardlinks --branch mac`干净检出，完整11套门禁再次全部exit0，检出目录无未提交变更。确认所选源码无需依赖未提交文件、排除的阶段快照或原目录构建产物；此后仅向当前审查报告和STATUS补记该结果，不改变已测代码。

Windows四项目App/SmokeTests/RegressionTests/UiSmokeTests在Mac交叉编译均exit0，无编译警告或错误；没有把它称为Windows运行验证。WPF的PosterImageProcessor移动前后字节完全相同，SHA256为`f60b445511a16472880127f773389409449c0002565612febcfcd48192f6c0fd`，必要的注入/项目引用随共享Core一起提交。

本轮不修改或重打包上一轮冻结build45产物；原生保存/冷启动证据仍按[SAVE-PREFERENCES-QA.md](SAVE-PREFERENCES-QA.md)的代码/资源与范围记录。此次修改为CI、Xcode工程/测试编译、检查工具和文档，不把build45既有包宣称为包含这些新工程文件哈希的重建包。

## 提交范围

显式选择应用/Worker/Core及Windows兼容适配、可执行测试、协议、两个合成PNG/NFO/HTML/JSON夹具、图标、本地化、锁定依赖、构建/验证脚本、许可证与必要文档。没有Git add整个工作目录，也不将本地未跟踪文件一概视作可发布。

不提交App/ZIP/DMG、SDK、bin/obj/.build、日志、缓存、浏览器状态、用户媒体、私有历史文件名案例、验收截图或签名身份。8份未被当前文档引用的重复阶段STATUS快照保留在本地；当前STATUS及独立QA报告继续保留可追溯范围和失败沿革。源码中的可疑凭据模式、文件类型/大小、可执行脚本权限及暂存差异经检查；仅新增两个合成PNG和必要ICNS二进制资源。

仍未声称完整Xcode构建、物理Windows/Mac兼容矩阵、完整IME/VoiceOver/手势、长期内存/真实大影片压力、live网站/Cloudflare、外接卷/SMB/满盘/断电或Developer ID/Gatekeeper通过。签名策略仍为只签名、不公证；远端CI结果应以对应commit的Actions记录为准。
