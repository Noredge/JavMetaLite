# build42 — 四语言、最小窗口与深色冲突回归

2026-10-24，Apple M5 / 16 GiB、macOS27.0.1 (26A434)，仅CLT。交付 **0.4.0/build42/arm64，ad-hoc签名，不公证**。所有操作只用本轮20个合成夹具文件，没有读取真实媒体库或请求网站。

## 基线与实际修改

开始时读取规则、STATUS及历史回归文档，记录Git工作区与build41源码输入清单。仓库及祖先未发现AGENTS.md，保留全部历史WIP，未reset、commit或push。build41的完整10套门禁先通过，并在独立QA46完成安全保存，再检查真实最小窗口。

本轮发现并修复两处显示问题：

- AppKit来源弹出菜单在切换语言后可能保留旧的选中标签，直到打开菜单才刷新。只按语言重建`ReviewSourcePicker`控件，保留选择、草稿和勾选范围。
- 保存预览直接显示Worker英文冲突说明。对已知的跨影片输入/输出路径冲突及已有附属文件覆盖确认提供四语说明；路径字节、未知诊断原文保持。新增真实资源Bundle测试，包含长路径、组合Unicode、空格与`%`，不改变协议或保护判断。

相对本轮开始，8个既有Mac构建输入变化，新增1个Swift测试文件。Core、Worker、WPF全部受清单覆盖的源码hash/mode保持；revision、NFO reader/writer、全队列`BatchSavePathConflicts`、指纹及事务实现均未改动。

## 自动与包装验证

| 项目 | build42实际结果 |
| --- | --- |
| 完整门禁 | 10套exit0；Core51例/583断言、Swift60 (WorkerKit18 + 原生桥接/交互42)、协议71 (生产50 + 受控21)、队列4、retention5、发布工具36、manifest15。 |
| 四语资源 | 443 keys × 4，1802次运行时/回退检查；新冲突文本测试使用实际打包Bundle。 |
| 生产App/ZIP | 17个原生文件的签名、entitlements、依赖、资源、无SDK Worker启动检查通过；ZIP解包再跑生产50、队列4、retention5。 |
| 版本绑定 | 冻结`candidate42.app`后直接归档与交付，没有在原生验收后重建替换。QA47前端去签名字节、全部Resources及生产Worker/Core程序集与交付一致。 |

命令：

```sh
DOTNET="$PWD/../dotnet/dotnet" JAVMETALITE_PRIVATE_PARSING_CASES="$PWD/../mac-regression-core-private/parsing-cases.json" bash scripts/macos/test.sh --configuration Release
DOTNET="$PWD/../dotnet/dotnet" bash scripts/macos/build.sh --configuration Release --sign-mode adhoc
```

`work/native-layout41-qa/package-tested42.py`归档冻结候选并执行ZIP测试；`finalize42.py`核对来源、版本、原生绑定、截图尺寸及磁盘回读。包装日志末尾的一行曾残留复制助手的candidate41文字，日志已明确追加标签更正；实际脚本路径、manifest与全部测试均为candidate42，不将该旧标签当作另一版本的证据。

## 原生实测与文件回读

QA46使用已交付build41；QA47使用最终42。两者均为独立bundle身份、仅argv注入私有`/private/tmp`数据根的启动shim，实际执行生产Worker，没有固定HTTP宿主。不能称整个QA bundle与交付签名完全相同。QA46/47均通过正常Quit、明确放弃测试身份的内存草稿后退出；没有强制退出本轮App。

| 场景 | 已完成范围与结果 |
| --- | --- |
| 先做安全闭环 | QA46覆盖关闭时20文件不变；明确允许后仅TEST-930 NFO标题改变。最终QA47再次导入、编辑TEST-931：默认预览正确阻断，取消保留标题；关闭标题前缀、明确允许覆盖后重新预览并确认成功。只有TEST-931的`movie.nfo`标题字节改变，其余XML字节及19文件SHA/bytes/mtime不变。 |
| 四语主页面 | English、简中、繁中、日文 × 浅/深 × 常规/最小，共16组合逐张查看。常规2560×1640px，最小1842×1346px，约921×673点（含工具栏；内容最小策略920×620）。实际通过系统Window → Move & Resize → Top Left缩小，未把正常窗口截图冒充最小。 |
| 双图与布局 | 各语海报/完整封套同时可见，长标题换行，保存选项可达，工具栏查询/保存均明确指向勾选2部。各语实际滚动到元数据底部，字段保持默认展开。 |
| 图片与批量 | 四语最小/深色图片页均有本地海报、fanart和两张样张，滚动后样张勾选可达；批量概览显示两部及各自双图。操作、缩放、主题切换均未改变勾选。蓝色图块是合成PNG本身。 |
| 深色真实冲突 | 仅勾选两个不同目录下的DEMO-611，使用共同根目录的番号文件夹，明确允许附属文件覆盖。生产全局预检仍发现视频/NFO/海报/封套目标冲突；四语说明正确、完整路径换行且可滚动，确认保存禁用。四语实际拖选产生可辨蓝色选区，白色文字可读。没有execute该冲突计划。 |
| 跳过预览 | 简中原生开启后再次保存相同冲突批次，仍显示阻断详情和禁用确认；之后关回预览默认路径。 |
| 取消与退出 | 取消冲突后编辑未勾选的TEST-930，四语切换均保留完整草稿、仅冲突两片勾选、查询/保存恢复可用。Quit提示3部未保存，返回编辑草稿保持，再明确放弃测试草稿正常退出。 |
| 最终磁盘 | 从QA47安全保存后的基线起，整个语言/尺寸/图片/批量/冲突/取消/退出阶段20文件SHA/bytes/mtime完全不变；SharedDestination为空。recovery仅有一份已完成的安全保存事务，未产生冲突保存事务。 |

## 证据位置

会话 `outputs/Mac-native-layout-2026-10-24/` 包含App、ZIP、manifest、results和48张原始JPEG，`screenshots/INDEX.md`逐张列出版本、范围、尺寸与链接。

- 01–06：旧build41/QA46安全保存与问题复现，保留历史，不计入最终42通过。
- 07–44及4张`*-build42-min-dark-metadata-bottom`：最终build42/QA47，共42张。
- 09–32：四语主页面、图片与批量；34–41：四语深色冲突普通/选中文字；42跳过预览仍阻断，43取消后编辑，44退出保护。
- `results/verification-summary.json`、`native42-screenshot-index.json`、两次安全回读、最终20文件回读、四语草稿状态、生产日志/完成事务、初始/最终源码清单和完整门禁日志分别保留。

源码输入digest：`cda3572b26910d5fa634b54104387c8c75516e546f2edb79ae174f0e4fd9915b`。
ZIP SHA256：`6e54a9a73c82965480f4d139561a970b25cb908b60f35687ffe33f41d993b0c7`。

## 范围限制与后续

主页面及本页明确列出的窗口/状态已检查，不代表所有次级弹窗、所有焦点/忙碌状态和任意长字段组合均已覆盖。X03/R08维持部分待测；40项总分类不因新增截图整体转为通过。日文最小侧栏的清空按钮采用原生省略号，完整AX名称保留，常规窗口显示完整，后续可优化短标签；各语队列行及批量长标题也采用预期省略，编辑器正文可查看完整内容。重复的逐片/全局冲突信息仍会显示，可另行做展示去重，不能丢失保护信息。

本轮未重验live网站/Cloudflare、物理IME/完整VoiceOver/触控手势、多显示器、Windows/Intel/macOS15/干净Mac、物理外接卷/SMB/满盘/断电、长期压力及Developer ID/Gatekeeper。CUA离散输入、AppKit程序测试及静态截图不冒充这些验证。只做ad-hoc签名；没有公证或staple。
