# macOS 当前状态

仅维护当前版本与验证边界；开发过程查看 Git 提交历史。

- 预览版：[macOS 0.4.0 Preview 1](https://github.com/Noredge/JavMetaLite/releases/tag/macos-v0.4.0-preview.1)，build45，Apple Silicon，macOS 15+。
- 发布包源码：`b0e9220`；ZIP、SHA-256 与构建/载荷清单随 Release 提供。
- ad-hoc 完整性签名，未 Developer ID 签名、未公证；不代表 Gatekeeper 放行。
- 原生 SwiftUI/AppKit 编辑器与自包含 C# Worker；原 Windows WPF 客户端保留。

## 已验证

发布包对应同一提交的 [Mac CI](https://github.com/Noredge/JavMetaLite/actions/runs/40254998779) 与 [Windows CI](https://github.com/Noredge/JavMetaLite/actions/runs/40254998618) 均通过：

- Mac：Portable 52/526 断言、Swift 69、协议 74、队列 4、缓存保留 5，以及四语、发布工具、签名和 ZIP 解包门禁。
- Windows：46 项 smoke、66 项事务回归、WPF 自动检查与便携包校验。
- 发布附件的哈希、载荷、17 个原生文件签名及无 SDK Worker 启动检查通过。
- revision、NFO round-trip、全队列冲突预检、指纹和文件事务保护保留。

[main 的 Mac CI](https://github.com/Noredge/JavMetaLite/actions/workflows/macos.yml?query=branch%3Amain) · [main 的 Windows CI](https://github.com/Noredge/JavMetaLite/actions/workflows/ci.yml?query=branch%3Amain)

## 尚未完整验证

- Intel、干净 Mac、完整 Xcode 构建/宿主测试及完整平台矩阵。
- 全部次级窗口的四语、最小布局、明暗模式和焦点组合；物理中日输入法、完整 VoiceOver、手势与多显示器。
- 小时/天级原生循环、真实大影片/大队列、多来源吞吐和持续内存占用。
- 物理外接卷、SMB、拔盘、满盘、权限撤销与断电。
- 真实站点长期可用性、验证码/登录会话、完整图片来源覆盖与 Jellyfin 扫描。
- Developer ID/JIT、浏览器首次下载后的 Gatekeeper 与离线启动验收。

自动化、受控夹具和有限原生观察不等于上述完整实机验收。运行命令见 [TESTING](TESTING.md)，发布要求见 [RELEASING](RELEASING.md)。
