# macOS 发布检查

macOS **1.0.0 / build50 / macos-v1.0.0** 按以下清单发布。执行命令见 [RELEASING](RELEASING.md) 和 [TESTING](TESTING.md)，已记录范围与限制见 [STATUS](STATUS.md)。

## 版本与来源

- App 版本、build、标签、四语 README 与 Release 文案一致；Windows v1.2.3 版本、标签、资产与 Latest 不变。
- 冻结提交及发布标签的 Mac、Windows **full** 门禁均通过。PR head、CI 实际 merge checkout 和打包提交分别记录。
- 包来自干净源码；源码输入、工具链、依赖锁、许可证与 notices 完整。ZIP、外部清单、包内清单和 SHA-256 对应同一产物。

## 包与业务保护

- `ditto` 解包后检查架构、最低系统、完整签名、权限、依赖、四语资源及无 SDK Worker 启动；不包含 RegressionHost、测试注入或私有媒体。
- 过期 revision/plan、外部改动、重复执行、未选输入和全批次冲突受到保护；NFO 未编辑 XML 与原始值保留。
- 取消/失败保留源文件、草稿与恢复现场；回滚不删除外部替换文件，失联卷不得报告清理完成。
- 事务核验与归档要求文件、记录及会话状态保持一致；仅归档日志，不改媒体或重放旧计划。
- 批量范围只来自勾选；失败、取消、未开始和部分成功分别显示。图片 Trash 独立确认，无永久删除回退；视频整理使用完整 SHA-256 校验。

## 分发与人工记录

- 默认 `github-adhoc` 允许正式 GitHub Release，不要求 Developer ID 或公证；不得称为 Apple 已验证、扫描或自动放行。
- `--policy github-adhoc --require-distribution` 通过；本地完整性、Developer ID、公证和 Gatekeeper 字段分别保留。显式 `signed-only` / `notarized` 不降低门槛。
- `manualAcceptanceVerified: false` 是自动脚本的范围声明；原生导入、审核、取消、保存回读和恢复流程必须另有证据。
- Release、四语 README 和包内 `READ-ME.md` 提供 SHA 校验及“系统设置 → 隐私与安全性 → 仍要打开”的单 App 操作说明，不绕过损坏或恶意软件警报。
- 对最终包记录实际安装/启动环境、系统拦截与手动放行情况；离线测试注明 Worker、WebKit 或整机的真实隔离范围。
- 对干净 Mac、最低系统、Intel、物理卷/SMB、断电、长期压力、IME/VoiceOver/手势分别报告，未完成的范围不宣称通过。

## 发布核对

- 稳定版清除 Pre-release，**不设为 Latest**；发布前核对三个附件的服务端大小/摘要。
- 发布后确认 tag 指向、公开下载、ZIP SHA、版本/签名说明，以及 Windows Latest 仍为 v1.2.3。
- 已发布附件保持不可变；后续二进制变更使用新版本和标签。
