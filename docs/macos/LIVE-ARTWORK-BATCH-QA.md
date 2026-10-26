# 真实封套、海报、样张与批量保存验收

2026-10-22，Apple M5 / macOS 27.0.1。本轮三个真实番号 SSIS-001、STARS-804、IPX-177；视频仅为本机生成的 2 秒 H.264 合成片，无真实影片下载。只签名、不公证，无线上 Release。

## 结果与输出策略

| 番号 | 完整封套 | 竖版海报 | 所选/实际 JPEG 样张 |
| --- | --- | --- | --- |
| SSIS-001 | 2184×1468 | 1092×1468 | 10/10 |
| STARS-804 | 800×535 | 400×535 | 15/15 |
| IPX-177 | 800×538 | 400×538 | 12/12 |

已存在合成 poster.png/fanart.png 时保持 PNG 目标格式，完整封套重新编码写入 fanart.png；poster.png 从横版封套的右半部分裁切（x=W-floor(W/2)，宽 floor(W/2)）。来源小海报本身也实际抓取并记录 SHA/尺寸，但输出沿用封套派生策略。37 张样张保留来源 JPEG 原始 bytes，按所选顺序写 extrafanart/fanart1.jpg 等，并正确更新 NFO 引用。严格完整替换模式：所有所选图片准备完成后才替换已取消选择的本地样张。

比较 helper 使用独立 ImageIO/CoreGraphics，先验证编码大小/40 MP，解码至 sRGB RGBA8 数值比较；不生成真实图片拼图。封套用完整源图，海报用右半源图；允许不同 JPEG 解码器误差 MAE≤5 / p95≤20，报告实际值。6 项通过，MAE 约 0.107–0.469 / p95 1–2。样张全部 37 SHA/order 一致，没有 inconclusive 或去重缺项。本次未按图片观感人工验收，也不以 sips 属性检查代替全图解码。

## 首次失败、修复与最终候选

1. run-01：旧签名 Worker 逐个实际下载图片参考，42/43 成功；STARS-804 首选完整封套 HTTP 404，故报告 partial 并保守跳过该脚本自己的批量保存，28 个夹具输入文件不变。原生 GUI 独立成功并不改变首轮参考失败事实。
2. 真实原因：OutputService 保存时已有完整封套候选回退；Worker artwork.getPreview 只有首 URL。修复仅在该 job 的同一个已注册候选内尝试最多 3 个位置，保留 20 秒、20 MiB、40 MP、2 槽位、取消及 review revision，只读预览不改变媒体。响应附加实际 resolvedLocation/usedFallback/attempts；不接受 caller URL，不绕过非法内容/资源上限。
3. run-02：framework-dependent 修复后 Worker；严格校验后复用首跑 42 参考，只新下载失败的 STARS 封套。主 URL 404 后，https://pics.dmm.co.jp/mono/movie/adult/1stars804/1stars804pl.jpg 返回 200，800×535，SHA 07d99c52350c08baf9d29786f2685767415d9d43116a7f7631202db84a383efd。真实重新下载保存用的 3 封套+37 样张，三片 Completed；此轮未执行后来增加的 stale revision map 断言。
4. run-03：最终签名包 self-contained Worker，清空环境（无 DOTNET_ROOT，PATH 仅系统目录且无 dotnet 可执行文件）与新工作目录；43 参考逐一校验当前 registered primary URL、artwork ID、role、原参考路径/bytes/尺寸并复用，**没有声称 43 个参考重新 HTTP 下载**。实际保存仍由生产 Worker 重新联网下载 3 封套源图和 37 样张源图，三片 Completed。新增 stale expectedReviewRevisions 拒绝与其余全部保护均实测通过，正常 shutdown/exit 0。

最终 App 0.4.0/build4、Worker0.4.0-dev，ZIP SHA 21301b453e10d6d78ab06dda61dc1152db7d2d2f8f265f915117d063fd498cdd。最终 Worker apphost SHA a57ecf230fe1b39ca97878bfdf74a58ec88bfae32ae9e6799e3006e56c9b92aa、managed DLL SHA 9d0e4d57f4f747c8fe80a2b199095934cbe121a86b47d5c188ce383c3d1651c0，精确绑定 final report。

## 原生 GUI 实际操作与截图

使用旧签名候选 outputs/M6-signed-only-live/JavMetaLite.app（ZIP SHA 3cf74e1a1ccac7fbe69494403f5b0eaf4b8b48a97b0b57d41a51d899fd5b037c），在 work/live-artwork-qa/native 导入三片及未勾选的 DEMO-900，只有合成影片/本地合成海报/NFO。多选三个真实番号后“勾选选中项”；批量查询仅 LibreDMM。Inspector 开启 NFO、海报、横版封套、写出已选样张和替换本地样张集，原文件夹、不重命名视频。

先保持覆写关闭，批量预览被正确阻断；取消后全部 16 文件 SHA 与导入前一致，无新文件。明确开启附属文件覆写后生成新批量预览，逐项列出 37 张样张、6 个海报/封套和 3 个 NFO 的变更，确认保存，三个任务 Completed。原生界面 footer “已保存 3 部影片”。正常退出，无 App/Worker 残留。

磁盘回读：只改变三个任务的 9 个 NFO/poster/fanart，增加37张样张；四个视频及未勾选 DEMO-900 全部原文件不变；3 个 NFO 的未知 root/opaque/resume 保留。另以 run-02 的真实源参考只读检查这些原生输出，6 次数值比较和 37 次 SHA/顺序匹配全部通过。没有媒体写入或新增 HTTP。

截图为真实预览/结果 UI，背景保留合成 DEMO 海报，未拼装产品界面或展示来源图片：

- 01-batch-overwrite-blocked.jpg：覆写阻断预览。
- 02-batch-image-preview-top.jpg：已授权的新批量预览顶部。
- 03-batch-image-preview-tail.jpg：第三片及其15张样张的尾部列表。
- 04-native-batch-results.jpg：三片实际保存结果；可能包含来源标题文本。
- 05-native-batch-saved-summary.jpg：已保存三片的 footer、实际全样张/原文件夹设置与未选 DEMO-900。

这些 GUI 操作发生于修复前候选。最终包通过 run-03 的完整生产 Worker 实测；没有将旧 GUI 操作说成在新二进制重复。Swift UI、Core OutputService 与原生图片适配本轮未修改。

## 保护实测

新目录初始 28 文件；只读抓取与采纳前后所有媒体输入 SHA 不变。预检逐个拒绝：未允许覆写、输出覆盖未勾选已注册影片输入（PATH_CONFLICT）、旧 revision map（REVISION_CONFLICT）、编辑后的过期计划（PLAN_STALE）；拒绝后完整媒体 inventory 与审核值一致。然后新 preview→execute，保留顺序执行语义与文件事务；重复执行不重复写盘。

结果 62 文件：9 更新、3 旧本地样张退休、37 新增；16 受保护原文件完全不变，包括4视频、未选 PROTECT-999 的7文件、KEEP.txt。每片未知 XML/root/actor extension/resume/原始 N/A rating 保留，provider 采纳字段与 NFO 图片引用匹配；无远程样张上限截断；3 recovery journal Completed，无恢复警告、无媒体临时文件。

本轮没有组织或移动视频，不把视频哈希不变说成跨卷验收。已有完整 SHA 复制/事务回归仍执行，实体外接卷、SMB、断电/磁盘满未验证。

## 复跑

默认脚本不联网，必须显式 --run-live；工作目录/报告必须不存在，避免覆盖已验证结果。参数路径由调用者选择，--worker 指定实际候选包中的 helper；可在没有全局 .NET SDK 的环境运行自包含 Worker。独立比较 helper 的编译需本机 Apple CLT/Swift。

```sh
python3 scripts/macos/live-artwork-batch-check.py --run-live \
  --worker '/absolute/path/JavMetaLite.app/Contents/Helpers/JavMetaLiteWorker.bundle/Contents/MacOS/JavMetaLite.Worker' \
  --video-template '/absolute/path/synthetic-2s.mp4' \
  --poster-template '/absolute/path/synthetic-poster.png' \
  --workspace '/absolute/path/new-artwork-run' \
  --report '/absolute/path/new-artwork-report.json'
```

可选 --reference-report 指向旧报告，必须逐项满足脚本的 ID/URL/role/路径/SHA/尺寸校验才复用。它只减少参考图片重复抓取，保存流程仍真实下载并数值比较；不能离线或用假数据替代保存。网络失败保留 partial 报告与已验证事实。默认行为及 CLI 拒绝已实际运行。

合成影片生成/AVAssetReader 解码脚本在上一轮输出和本轮 templates 中；视频 320×180、60 帧、无音轨。全部真实图仅在 work/live-artwork-qa 的一次性夹具/参考缓存，不进入 App 或源码。交付 evidence 含URL/哈希/尺寸/流程状态，不含图片像素或来源标题/描述全文。

## 证据与限制

本轮 outputs/M6-artwork-batch-live/evidence：live-artwork-batch.json（首跑 partial）、live-artwork-batch-run-02.json、live-artwork-batch-final.json、native-artwork-batch-readback.json、native-blocked-readback.json、native-image-source-comparison.json；test-results 内有阶段门禁、41 Worker 回归、Swift20、独立helper8例与签名/打包日志。

无 .git 的中文/空格路径源码实际重建 exit0、55.47秒；264个 selected 输入与交付 App 摘要一致，verifiedExport=true。locked restore、Swift Release、ad-hoc 打包、两次包验证、中文路径解包后41项协议测试通过。构建产品与最终签名包未宣称逐位相同；最终文档更新不改变所选构建输入。

只签名、不公证。readiness localSignatureVerified=true、signMode=adhoc；0有效身份，signatureReady/distributionReady/Gatekeeper/notarization均false。没有公证提交、staple、关闭系统保护或线上发布。

未验证：Developer ID/干净Mac系统信任、Intel/macOS15/Windows实机、实体中日文IME/完整VoiceOver、多屏、外接卷/SMB/故障注入、Jellyfin扫描、真实网页验证码/会话与持续网络可用性。本次图片来源为LibreDMM三片，不代表R18图片流程或全目录覆盖率；低分辨率备用图如实保留，没有虚构高清结果。前阶段 metadata-only 证据另见 LIVE-PROVIDER-QA.md。
