# macOS 发布检查

每个候选包按本清单核对；实际已完成范围见 [STATUS](STATUS.md)，执行方式见 [TESTING](TESTING.md) 和 [RELEASING](RELEASING.md)。

## 构建与完整性

- 精确源码提交的 Mac、Windows full CI 均通过；PR head 与 CI 实际 merge checkout 分开记录，记录架构、最低系统和工具链。
- 自包含 Worker，无开发 SDK/PATH 依赖；包内无 RegressionHost、测试注入入口或私有媒体。
- NuGet 锁、许可证和 notices 完整；来源、payload、ZIP 哈希及清单相符。
- 原始 ZIP 用 ditto 解包后，重新检查全部签名、权限、依赖、四语资源及 Worker 运行。

## 业务保护

- revision/plan 过期、外部变动、重复执行、未选输入和全批次冲突均受保护。
- NFO 未编辑 XML/原始值保留；取消/失败后的源文件、草稿、恢复记录可核对。
- 图片废纸篓独立确认，无永久删除回退；视频整理完整 SHA-256 校验，不以 Unix 路径根判断同卷。
- 批量查询/保存范围只来自勾选；失败、取消、未开始和部分成功分别显示。

## 发布与验证范围

- 默认使用 `github-adhoc`：允许在 GitHub 发布 ad-hoc 正式版或预览版，不要求 Developer ID 或公证。这是项目分发政策，不表示 Apple 已验证身份、扫描恶意软件或自动放行。
- `--policy github-adhoc --require-distribution` 必须通过包完整性与本地签名检查；`signatureReady` 仍只表示 Developer ID，公证及 Gatekeeper 结果分别保留。显式 `signed-only` 和 `notarized` 模式不降低门槛。
- `distributionReady` 仅表示所选自动政策通过；脚本的 `manualAcceptanceVerified` 固定为 false。安装与原生业务验收必须有独立记录，不能由该返回值代替。
- GitHub Release、四语 README 和包内 `READ-ME.md` 提供首次启动说明：核对下载后先尝试打开，再在“系统设置 → 隐私与安全性 → 仍要打开”对该 App 放行。此流程不适用于损坏或恶意软件警报，不关闭全局 Gatekeeper。
- Developer ID 模式单独核验身份、Team、时间戳、hardened runtime 和 Worker JIT。
- 对最终 ZIP 记录浏览器下载、系统原始拦截、用户单 App 放行及离线启动结果；写明实际机器与系统，不把本机验收当作干净机或最低系统验收。
- 发布前核对三个附件的服务端哈希，发布后核对 tag/下载；保留 Windows Latest。
- 对完整 Xcode、Intel/系统矩阵、浏览器下载启动、物理卷/SMB/故障、长期压力、全部窗口与 IME/VoiceOver/手势分别记录；未实际完成则保留未验证。
