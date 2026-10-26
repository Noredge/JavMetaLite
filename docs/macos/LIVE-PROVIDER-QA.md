# 真实来源查询：合成影片验收

2026-10-22，Apple M5 / macOS 27.0.1。用户明确选择“只签名、不公证”，并授权少量真实番号、合成影片的实际抓取。本轮无 Apple 公证上传/staple、无真实影片下载、无验证码绕过，历史 M6 产物仍保留。

## 实际素材和网络路径

选择 **SSIS-001、STARS-804、IPX-177**，覆盖三个前缀。每个独立目录放一份相同的合成 H.264 MP4：320×180、30 fps、60 帧、2 秒、无音轨。用 AVAssetWriter 生成，并用 AVAssetReader 实际解码 60 帧，非仅改扩展名或填充字节。视频 SHA-256 为 `6a2e49c665cc35e4653a2fc5dc4837032b43e07882d4d02e36eb37d0e7074fc2`。另放本地合成海报和同 ID 的 NFO，含未知节点/root attribute、actor extension、resume、N/A 原始评分。

`live-provider-check.py` 通过已打包的生产 Worker，逐部调用真实 LibreDmmClient 和 R18DevClient。测试未注入 HTTP fixture、fake provider 或录制响应替换，也未设置 localhost 测试服务。每来源分别核对 operation 与 jobResults 状态、warnings、该来源的非空候选，不能只看到 operation completed 就判双来源成功。

LibreDMM 的实际来源入口为 `https://www.libredmm.com/movies/{ID}.json`；R18 使用实际 combined/dvd_id 与 content-id 回退链。STARS-804 的结果实际记录到 `combined=1stars804/json`，不是一直使用最初推测的编号。

## 首次真实执行结果

UTC 2026-10-22 19:37:42–19:37:46，生产 Worker **0.4.0-dev**。三部各两个来源，共 **6/6 成功，无 warnings**。

| 番号 | LibreDMM | R18.dev | 在线日期/时长候选 | 演员候选名数量（Libre/R18） | NFO 保存 |
| --- | --- | --- | --- | --- | --- |
| SSIS-001 | Completed | Completed | 两来源均非空 | 2 / 2 | Completed |
| STARS-804 | Completed | Completed，实际 content-id 回退 | 两来源均非空 | 1 / 1 | Completed |
| IPX-177 | Completed | Completed | 两来源均非空 | 1 / 1 | Completed |

逐部显式选择真实 provider 的 title/originalTitle/date/runtime/maker 候选，优先 LibreDMM；保留本地 actor 与 N/A，专门检验扩展及原始值，plot 写入验收备注。保存后回读 NFO 与已选择的真实值逐一相等；不把保留的本地 actor 说成在线演员已写入。报告只保留来源 URL、字段存在性、数量和哈希；完整来源标题/文本及原始 NFO 留在工作目录，不作为截图或交付内容。

每部以下 12 项断言通过：查询不改媒体文件、旧 revision 拒绝、未允许覆写时阻止、旧 plan 拒绝、新预览只写目标 NFO、重复 execute 返回相同 operation、未知 XML 保留、resume 保留、actor extension 保留、原始评分保留、视频 SHA 不变、海报 SHA 不变。共 **36 项断言**。三次保存均无 cleanup warning；Worker 正常 shutdown，exit 0。

脚本默认不运行网络，必须有 `--run-live`；已存在的 workspace/report 拒绝覆盖。上述两个拒绝条件也实际执行，保留日志。该脚本不加入离线 CI 默认门禁。

## 原生 App 实际操作

另用 `work/live-provider-qa/native` 中同一合成片，打开原 M6 本地 App（ZIP SHA `ac736a52265e9be67a0dbeeb1ebd4ba50a54f84b2740e799d9b99c96f62986fe`）：

1. ⌘O 导入 SSIS-001；选 LibreDMM，⌘R 实际查询。
2. 观察在线日期、150 分钟、2 名演员等真实资料进入审核。将标题/原始标题改为合成验收说明，明确通过来源候选选择真实日期 `2021-02-20`，重新选择本地合成封面。测试中一次错误的字段输入已通过候选选择纠正，未保存该错误值。
3. 检查器仅开启 NFO，明确允许覆写；⌘S 预览列出只更新 NFO，并保留 MP4、poster、fanart。确认后 UI 显示“已保存 1 部影片”。
4. 磁盘回读确认日期、时长、2 名演员已写入；未知 XML/root attribute、resume 保留。只 NFO 改变，视频/两张合成图哈希不变。正常 ⌘Q，没有 App/Worker 残留。

两张真实截图为 `screenshots/01-native-live-preview.jpg`、`02-native-live-saved.jpg`。截图显示合成封面与人工验收标题，在线来源元数据没有成为产品内固定假数据。`evidence/native-live-readback.json` 与 `native-saved-movie.nfo` 记录限定范围。未宣称完成 R18 原生 GUI 或远程图片输出验收；原生查询时图库曾载入远程缩略图尺寸，但本轮不将其提升为图像保存/完整样张验收。

首次 API/原生测试使用原 M6 包。当前签名策略及 opt-in 脚本未修改 Core/Worker/Swift 业务源码；新包打出后，以 Worker apphost/managed DLL 的精确 SHA 对比绑定执行证据，见 `component-identity.json`。不把不同 outer App 签名当成同一个 ZIP，也不把原生操作说成在新包重做。

## 复跑

先构建/解包 App，再从源码根目录运行，使用一个全新路径。模板必须由测试者选择的合成影片提供；本轮交付的 `fixtures/synthetic-2s.mp4` 可用。

```sh
python3 scripts/macos/live-provider-check.py --run-live \
  --worker '/path/to/JavMetaLite.app/Contents/Helpers/JavMetaLiteWorker.bundle/Contents/MacOS/JavMetaLite.Worker' \
  --video-template '/path/to/synthetic-2s.mp4' \
  --workspace '/path/to/new-live-run' \
  --report '/path/to/new-live-report.json'
```

若来源只部分成功，脚本保留逐来源失败，结果为 partial，而非 all pass。来源可用性为本次时点的观察，不保证将来持续可用；本次没有失败不等于线上错误/429/验证码全部通过。

## 范围限制

未测试真实网页验证/会话、验证码、远程图片输出/完整样张、Jellyfin 扫描或真实影片内容。三部、两来源不能代表整个目录覆盖率。provider 警告和批量错误仍有原始中/英文直接展示，部分错误分类较粗；本次成功路径没有证明四语言错误路径完整。物理 IME/完整 VoiceOver、Intel、最低 macOS 15、Windows 运行、外置卷和故障恢复仍沿用 STATUS 的未验证边界。
