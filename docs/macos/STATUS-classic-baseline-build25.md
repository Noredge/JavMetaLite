# JavMetaLite macOS — build25经典编辑器（待原生验收）

更新：2026-10-23 America/Los_Angeles（证据 UTC 2026-10-24）。当前候选 **App 0.4.0 / build25 / arm64 / ad-hoc 签名，不公证**。经典编辑器已实施，完整可执行门禁与正式打包/解包通过；**本轮原生窗口验收和截图尚未完成：Mac 锁屏，CUA 无法启动验收窗口，已请求用户解锁。目标仍未标记完成。** 不以编译、自动 AppKit 测试或早前图示代替本轮截图。

## build25 实施范围

- 原生统一标题栏放置添加、查询及其菜单、预览保存、右栏开关。保留 ⌘R / ⌘S / ⇧⌘R / ⌥⌘B 与现有提交编辑/模态保护；右侧保存设置默认打开。
- 主页面永久并排展示裁切海报与完整封套，切换资料/图片页仍保留双图；采用实际 Worker 预览和有界解码缓存。仅使用审核选中的图片来源，缺图不借用未选来源或样张。在线横图的海报缩略预览遵循 Core 的原始方向、右半裁切规则；本地既有海报与完整图保留原图。
- 资料字段默认全部展开，移除大块表单卡片装饰；保留原生文本桥、每字段/任务 Undo、IME 提交与候选对比。Series/ContentId 继续只读，未新增无效编辑能力。
- 队列提供直接“全选”进入批量概览；批量行同时展示海报/完整封套并可返回单片。保留导航多选 > 独立勾选 > 当前影片的既有动作范围，工具栏及保存右栏显示数量。清空入口与确认保护不变。
- 保存右栏提供仅元数据/元数据与图片/自定义输出方案。视频重命名、NFO 标题前加番号、覆盖 NFO 与图片合并到一个“更多选项”；解释通过原生 `.help` 悬停提示提供，不常驻在选项下方。补齐四语言共 13 个新键。
- 本轮仅修改 Mac 的 15 项构建输入；对照开工前 283 项输入快照，Core、Worker、Windows 与事务服务源码/hash/mode 均未改变。未 reset、commit、push，保留已有未提交实现和正在运行的用户 build24 会话。

## build25 当前验证

| 范围 | 实际结果 |
| --- | --- |
| 全量可执行门禁 | `classic25-final-gate.log`：10/10 套 exit 0；本地化 412 keys / 1678 检查，发布工具 36，manifest 15，Core 50 项 / 559 断言，Swift 48（18 WorkerKit + 30 NativeBridge），协议 63（生产 49 + 受控 provider 14），queue 4，retention 5 |
| 新增回归 | 审核双图来源隔离、缺图与远程封套海报回退；401×200 合成图右半裁切/像素/完整图缓存隔离及源文件不变。初次嵌套 `#require` 编译错误和测试夹具颜色写入失败均保留日志，修正后新增测试及全套通过；未掩盖产品失败 |
| 正式包与解包 | arm64 / ad-hoc 验签、17 native 文件签名/entitlements/依赖、四语资源、无 SDK Worker 启动验证通过；解包后生产协议 49、queue 4、retention 5 通过；未执行公证 |
| 源码与交付绑定 | 构建 283 项输入与当前输入一致，digest `18206ea8fe020a185a0abc0d5aace8f3b4469a166342d55ed42dd950f15d9f77`；交付 App 全部 payload hash/mode/签名属性与正式包一致；ZIP SHA256 `576f0cb5d61f93fe4c339684204c4b829aa0633b08726aa08096258e65967984` |
| 原生 GUI / 截图 | **待解锁后执行**。独立 QA25 身份已准备，前端去签名代码 SHA 与正式25相同，生产 Worker DLL 相同；仅增加 QA 专用 argv shim 指向独立 `/private/tmp` 数据根。后端初始化/私有数据根/退出已验证；不是原生 GUI 成功。未替换 provider，不交付 QA bundle |

完整产物在会话根 `outputs/Mac-classic-editor-2026-10-24/`：`JavMetaLite.app`、`JavMetaLite-macOS-arm64-Release-adhoc.zip`、`results/verification-summary.json`、`results/source-delta.json` 与完整门禁/打包日志。`screenshots/` 当前为空，不能引用旧图冒充25。开工前快照与一次性合成影片保存在 `work/classic-editor-qa/`。

待解锁后继续：单片双图/资料展开、更多选项与提示、最小窗口与四语、批量双图/范围/保存、Undo 与保存预览取消/确认、原始视频/图片/未知 XML 回读，并保存真实截图。revision、NFO round-trip、冲突预检与事务保护已有自动回归覆盖，仍需分别记录本轮原生交互证据。

未验证项目继续保留：物理 IME、完整 VoiceOver、真实外接卷/SMB/拔盘/满盘/断电、Intel/macOS15、Windows 实机、长期稳定、干净 Mac、Developer ID 与 Gatekeeper。本机 `security find-identity -v -p codesigning` 为 0 valid identities；ad-hoc 不等于 Developer ID。用户明确只签名不公证，未调用公证工具。build24负几何历史问题不因本次编译通过而宣称修复。

---
