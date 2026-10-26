# macOS 发布检查

每个候选包按本清单核对；实际已完成范围见 [STATUS](STATUS.md)，执行方式见 [TESTING](TESTING.md) 和 [RELEASING](RELEASING.md)。

## 构建与完整性

- 精确源码提交的 Mac、Windows CI 均通过；记录架构、最低系统和工具链。
- 自包含 Worker，无开发 SDK/PATH 依赖；包内无 RegressionHost、测试注入入口或私有媒体。
- NuGet 锁、许可证和 notices 完整；来源、payload、ZIP 哈希及清单相符。
- 原始 ZIP 用 ditto 解包后，重新检查全部签名、权限、依赖、四语资源及 Worker 运行。

## 业务保护

- revision/plan 过期、外部变动、重复执行、未选输入和全批次冲突均受保护。
- NFO 未编辑 XML/原始值保留；取消/失败后的源文件、草稿、恢复记录可核对。
- 图片废纸篓独立确认，无永久删除回退；视频整理完整 SHA-256 校验，不以 Unix 路径根判断同卷。
- 批量查询/保存范围只来自勾选；失败、取消、未开始和部分成功分别显示。

## 发布与验证范围

- 只签名、不公证；标明 ad-hoc 或 Developer ID，不混用完整性与系统信任结论。
- ad-hoc 包标为预览，说明首次启动可能受 Gatekeeper 阻止；不将其报告为 Developer ID 分发就绪。
- Developer ID 模式单独核验身份、Team、时间戳、hardened runtime 和 Worker JIT。
- 发布前核对三个附件的服务端哈希，发布后核对 tag/下载；保留 Windows Latest。
- 对完整 Xcode、Intel/系统矩阵、浏览器下载启动、物理卷/SMB/故障、长期压力、全部窗口与 IME/VoiceOver/手势分别记录；未实际完成则保留未验证。
