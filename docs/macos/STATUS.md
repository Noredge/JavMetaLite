# macOS 状态与验证范围

macOS **1.1.0**，Apple Silicon，最低部署目标 macOS 15。下载、精确源码提交、最终 ZIP 的 SHA-256、清单与门禁链接以 [macOS 1.1.0 Release](https://github.com/Noredge/JavMetaLite/releases/tag/macos-v1.1.0) 为准。Windows 保持 **v1.2.4**，独立保留 Latest。

GitHub 包采用 **ad-hoc 签名**，不提供 Developer ID 或公证。项目的 `github-adhoc` 政策允许正式发布，但不代表 Apple 身份验证、恶意软件扫描或 Gatekeeper 自动放行。首次打开方式见 [安装说明](../../macos/README.md#first-launch)。

## 1.1.0 发布验证

- 发布标签的 Apple Silicon 完整测试、应用打包和解压启动检查通过。同一提交的 Windows 回归测试与便携包校验通过。[Mac CI](https://github.com/Noredge/JavMetaLite/actions/runs/44570373664) · [Windows CI](https://github.com/Noredge/JavMetaLite/actions/runs/44570405698)
- 在 Apple Silicon Mac（macOS 27.2.1）上打开正式 ZIP 中的应用并载入本地影片。用随包 Worker 关联已有 NFO 的副本，保存后回读，确认手动修改的标题和清空的评分均符合预期，原有导演、演员、简介、类型和来源网址均保留。
- 公开下载的三个附件均已校验。版本、提交和 ZIP 的 SHA-256 可在[发布页](https://github.com/Noredge/JavMetaLite/releases/tag/macos-v1.1.0)核对。

1.0 版的磁盘映像、真实来源和压力测试数据保存在[当时的验证记录](https://github.com/Noredge/JavMetaLite/blob/macos-v1.0.1/docs/macos/STATUS.md)。

## 使用与恢复边界

- 保存默认先预览；过期 revision/plan、外部文件变化与输入冲突必须拒绝。取消/失败保留草稿和可恢复现场，不自动重放保存。
- 事务恢复核验文件后，只有符合已记录状态且未变化的记录才可由用户确认归档。归档只移动日志；相关影片须重新导入取得新基线。参考 JSON 不含可执行计划，也不自动恢复草稿。
- 旧预览版日志缺少可信前后态证据，保持只读。按 [人工核查流程](TESTING.md#legacy-transaction-records)保留原日志、核对文件与备份后处理，不能通过删除警告宣称已恢复。
- 队列和未提交草稿不会在普通退出后自动恢复。1.1.0 中需要删除本地图片时，请在 Finder 操作。

## 尚未覆盖

- Intel、最低 macOS 15 实机、干净 Mac、完整 Xcode 宿主/UI 构建及完整 OS/硬件矩阵。
- 物理外接卷、SMB、物理拔盘、断电和保存中途真实崩溃；磁盘映像测试不能外推为这些结果。
- 全部次级窗口、物理中日输入法、完整 VoiceOver、手势、多显示器及慢/大图片的全部组合。
- 小时/天级原生压力、真实大型媒体库、完整来源图片与验证码/登录流程、Jellyfin 扫描。
- Developer ID、公证和自动 Gatekeeper 接受未建立；若选择更严格分发政策，必须另行验证。首次下载/手动放行仅按最终 Release 的实测记录说明。

开发过程与旧版本数据保留在 Git 历史。维护命令见 [TESTING](TESTING.md)，分发规则见 [RELEASING](RELEASING.md)。
