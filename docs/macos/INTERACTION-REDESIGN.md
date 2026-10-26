# macOS交互重设计 — build24验收与build23历史

2026-10-23，America/Los_Angeles。用户六点反馈已落实为统一三栏工作区、默认保存设置、直接批量动作、清空入口和明确网页来源。当前App0.4.0/build24运行源码冻结，保留build23全套历史证据；本记录区分最终生产包、同编译前端的受控原生QA、live网站及历史失败。

## build24自动验证与原生candidate

283项构建输入digest为`c2e5c3ae21e40429ce338c133ac8417b77fc2bf753c06a752e5cc5bd38420eb3`。QA24历史全部22条发布期状态修改警告，经app backtrace/atos确认为来源Picker触发`BrowserSession.chooseSource`同步发布（`interaction-qa24-publishing-symbolication.json`），并非sheet问题。24仅将三来源Picker换成显式按钮，保留同步chooseSource、generation/revision与身份保护，提供active tint及AX选中态。Browser2+Store4+Startup3定向9/9通过，1.246秒（`interaction-browser-source-buttons-swift-final.log`），总Swift仍46。

正式production candidate24的原生记录为`results/native24-operation-record.json`：02:09:07.759–02:11:18.463 UTC，App96501/Worker96504。首次连接1.5674秒即ready、默认右栏；原生OpenPanel/发现确认1片，本地NFO加载；8次来源按钮切换的AX选中态及地址正确，Esc关闭后动作恢复；标题改为“Shipping build24 — 本机正式包 NFO 保存”并Undo/Redo；⌘S预览仅NFO Update、视频及3图片Keep，确认保存1片；查看结果再Close立即恢复动作；清空Esc保持1片，再确认1片/0未保存归0并正常⌘Q。`shipping24-local-native-readback.json`独立确认5文件仅预期NFO改变，标题正确，未知XML/注释/root属性/原演员role及扩展保留，其余4文件SHA/bytes/mtime不变，无新增/删除；默认Library settings前后均不存在，未保存偏好，两个进程均已退出。`shipping24-native-pid-bounded-log-summary.json`记录精确PID96501及上述时段：12335实际事件，发布期状态修改0、负几何186、约束冲突0。发布期状态修改未在此回归重现，负几何仍未解决；不声称日志全干净。

来源限制仍在：LibreDMM合成番号未找到，R18无contentID进入home且不支持导入，JAVLibrary为live CF挑战，导入始终禁用；无CAPTCHA、外部打开点击或live导入完成。仍有文案问题：HTTPS主页面下子frame导航被阻止，也会显示仅允许HTTP/HTTPS的泛化提示；未修，不据此声称JAVLibrary已打通。

`interaction-source-rebuild24-summary.json`记录无.git中文/空格路径WIP文档源码独立重建224.676秒通过：283构建输入digest一致，解包49生产协议+queue4+retention5通过，未包含RegressionHost。去签名前端仍不逐字节相同（两侧47处绝对根路径），198份Worker payload中7份字节不同；只证明可独立重建，不冒充最终文档源码ZIP或binary-same。

| 证据 | 实际结果及范围 |
| --- | --- |
| `gate24-summary.json` / `interaction-complete-gate24.log` | 10/10套exit0，200.758秒：399 keys/1626检查、发布工具36、manifest15、Core50项/559断言、Swift46（17+29）、协议63（49生产+14受控provider）、queue4、retention5；起止283构建输入不变 |
| `package24-summary.json` / `interaction-package24.log` | 175.315秒通过；正式ad-hoc签名包与解包生产协议49、queue4、retention5，以及签名/依赖/manifest/no-SDK Worker验证通过 |
| 正式ZIP | 41,464,845 bytes，SHA256 `f43ff52deb15141032c814359e205e3b630944c8f0bdda8b613c800f94315da3`；无Developer ID、公证或Gatekeeper验收 |

**candidate24不是最终shipping24二进制。** 去签名前端SHA分别为`b90b33d3d85123584282ada7ad47053914007ef58cfedbc3d1be04757f06450c`和`ec03cb669757a2dc507c1bc4dcccf8475b362f4cb5fe702f8303d01a39533676`，仅同283构建输入；Worker DLL同为`09da0ce0394bb386018d293b5d4fb3e7764c82476f285099f4f9d4748aca6c6f`。candidate原生结果不代替最终shipping原生检查，最终包已另行实操如下。下文23执行数字、原包哈希及警告均为历史。

最终交付目录`outputs/Mac-interaction-redesign-2026-10-23/`内以build24 App、ZIP及`BUILD-MANIFEST.json`为准。最终源码`JavMetaLite-source-build24.zip`的完整归档manifest、逐文件hash/mode与上述283构建输入绑定见`results/source24-export-validation.json`；独立重建使用WIP文档源码，其结果仅证明同运行源码可重建。

## 最终shipping24原生与交付绑定

操作记录见`results/shipping24-final-native-operation-record.json`。此轮直接运行不可变`Shipping production24.app`，没有RegressionHost或QA环境，使用默认Library。CUA调用02:33:11.900–02:36:19.805 UTC；`shipping24-final-native-process-provenance.json`独立确认实际App644/Worker649及启动秒02:33:12。首次1.3518秒ready、右栏默认展开。原生OpenPanel导入全新tmp的TEST-930并发现确认，本地NFO加载；6次来源按钮循环的AX选中态与地址正确，JAVLibrary仍CF挑战且无live导入，未操作CAPTCHA/外部打开；Esc关闭后动作恢复。

标题改为“Final shipping24 — 正式签名包安全保存”并完成Undo/Redo。首次覆盖关闭时预览确认禁用，取消保留草稿；明确开启覆盖、关闭ID前缀、仅NFO开启（其余3项关闭、视频改名关闭），重新⌘S显示1份NFO Update及4份Keep，确认保存1片。再打开已完成结果、Close后动作立即恢复；清空先Esc保留1片，再确认1片/0未保存归0，正常⌘Q。

`shipping24-final-local-native-readback.json`确认5文件仅TEST-930.nfo改变，精确标题正确；未知XML/注释/root属性/原演员roles与扩展语义保留，其余4文件SHA/bytes/mtime不变，无新增/删除；默认settings前后不存在，App644/Worker649均退出。`shipping24-final-native-pid-bounded-log-summary.json`记录PID644在02:33:12–02:36:19.805 UTC的10660实际事件（另1工具结束行已排除）：发布期状态修改0、负几何198、约束冲突0。此范围未重现发布期状态修改，负几何仍未解决，不扩展为所有布局/长期运行或未捕获输出均干净。

`delivery24-exact-shipping-inventory.json`确认交付`JavMetaLite.app`全部219项的内容、hash、mode及签名属性与此不可变最终shipping包完全一致，deep/strict签名验证通过。最终生产ZIP与包清单另见上述package24记录；不以candidate24或独立重建的不同二进制替代这一对应关系。

## 基线与实现边界

build19精确备份`before-source-build19.zip` SHA256为`d5e469bb21b0c5dcf0802c8fd90c700387665530c9b6d05c5a0bf2bde179bdeb`；原STATUS逐字节保留于 [STATUS-interaction-baseline-build19.md](STATUS-interaction-baseline-build19.md)。历史33/6/1矩阵只限已记录至19的证据，12挂起、13–16崩溃、18超时/发布警告、19负几何警告及20/23启动失败不删除。保留未提交修改，未reset/commit/push。

主查询/预览/保存栏共用**多选导航→勾选→当前**范围与数量，命令提交输入并处理延迟导航后捕获ID。右栏默认展开，窄窗不自动隐藏；编辑器分区、图库有限列宽懒行。清空先显示队列/未保存数量，取消为安全默认；Worker整组验证和完整ack后才移除会话状态。网页入口仅允许一个有番号的导航对象；导入、来源重试、清空/browser均有模态保护。build23发布AppKit sheet开始/结束状态并转发delegate，修复结果关闭后按钮直到另导航才恢复的真实P2。启动失败新增显式安全重试。

上述入口继续使用MovieJob revision、当前页面/任务/generation、NFO round-trip、全队列冲突预检及事务/journal；清空不保存/删除媒体，网页导入仅进入审核。

## build23历史自动验证与产物

| 证据 | 版本、执行结果与范围 |
| --- | --- |
| `interaction-complete-gate23.log` / `gate23-summary.json` | 10/10套exit0，170.596秒：399 keys/1626检查、发布工具36、manifest15、Core50项/559断言、Swift46（17+29）、协议63（49生产+14受控provider）、queue4、retention5 |
| `interaction-package23.log` / `package23-summary.json` | 250.828秒；正式ad-hoc包及解包49生产协议+queue4+retention5通过；17 native文件、签名/entitlement/依赖/manifest/no-SDK Worker验证通过 |
| `interaction-native-sheet-startup-swift-final.log` | 定向7/7，0.845秒：真实AppKit begin/end sheet动作恢复和delegate转发，加原范围/清空/启动恢复；后续全量46已重跑 |
| `interaction-source-rebuild23-summary.json` / `.log` | 无.git中文/空格路径WIP文档源码独立重建271.882秒；283构建输入完全相同，解包49+4+5通过；最终文档源码ZIP由主代理另行导出核对 |

构建输入digest为`070710aae58cb1812467145656e8166aec2b75e80954d289f06b12fdf0e274dc`，gate/package起止不变。生产ZIP为41,462,022 bytes，SHA256 `7fc899457595ba1bfe025bde5e97bb6a84e7652f5383371f6ef66b4015211a27`。仅ad-hoc签名，未公证、无Developer ID/Gatekeeper验收。

独立重建**不是二进制相同**：前端嵌入47处不同绝对根路径，去签名后SHA不同；198份Worker payload中7份字节不同，新增/删除为0。此结果证明同一构建输入可独立构建并通过执行测试，不证明可复现到同一二进制；WIP文档源码归档也不冒充最终交付源码ZIP。

build23当时使用常规`artifacts/macos`产物路径，该路径现已由24更新；23历史应按报告/哈希或保留的不可变副本识别。统一交付位置为任务根`outputs/Mac-interaction-redesign-2026-10-23/`；此处为build23历史，当前交付App/ZIP以24段落为准。交付证据在`results/`；原日志位于repo的`artifacts/test-results/`，JSON/readback/provenance原件位于任务根`work/interaction-redesign-qa/`。

## build23编译前端的原生QA24（历史）

`shipping23-native24-exact-provenance.json`证明QA24与production23的去签名前端SHA同为`4eca18c5f5e7a33dfda1e2e16808fbfdc51bcf6df767b78159e347aec844fc23`。QA24独立bundle ID，使用原始RegressionHost及真实Workspace/provider解析/文件事务，受控HTTP/图片服务器；配置/dataRoot在独立`/private/tmp`。没有用诊断替身前端/host冒充最终版本，受控provider结果也不当作live站点可用性。

| 原生操作 | 实际结果及磁盘证据 |
| --- | --- |
| A已勾选→导航多选B/C→⌘R | 只两片结果；关闭结果后无另行导航/点击即恢复主动作；立即⌘S预览/确认保存2片。`fixture-readback23-after-native-batch24.json`仅B/C两NFO改变，A排除 |
| 清勾选→当前A | 标题Undo/Redo、⌘S单片保存完成；`fixture-single24-BC-excluded-confirmed.json`确认B/C SHA/bytes/mtime保持 |
| R18“全部已识别” | ⇧⌘R完成3片查询，不自动保存；与前一次保存后的27文件对比无新增磁盘变化 |
| 四语/最小布局 | 简中、英文、日文、繁中最小窗口截图1842×1346px，主动作和右栏固定底部可达。日语“勾选可见”和英语原位置Picker有原生省略号，完整AX可读且可操作；不标为全部无截断 |
| 窄窗图库 | 两列可用；本地样张勾选降为0，资料/图库切换后仍0、图片来源不变 |
| 清空 | ⌥⌘Backspace显示3未保存，Esc保持3片及样张0；再次明确确认队列为0，文件不变 |
| 深色冲突 | 两个U/UC合成影片打开视频改名后形成共享mp4/NFO目标冲突，预览阻断/确认禁用；取消后动作恢复，再清空2片归0并正常退出，原冲突目录只保留2个输入视频 |
| 模态命令 | 网页sheet内⌘O没有打开第二个sheet；关闭sheet后动作可继续 |

`fixture-readback23-after-native-single24.json`相对27文件基线共3份预期NFO改变，无新增/删除；每份未知XML/注释/root属性及演员扩展保留，非NFO字节/mtime不变。`fixture-final24-no-disk-change-confirmed.json`确认后续查询/图库/网页/清空/冲突及退出后全部27文件的SHA/bytes/mtime与单片保存后完全一致。用户原手测92文件也由`user-fixture40-final-readonly.json`确认全部不变，无新增/删除。

`qa24-native-pid-bounded-log-summary.json`统计QA24精确PID84661、2026-10-24 01:41:05–01:52:10 UTC：8873行（含1条工具结束，实际事件8872），发布期状态修改22、负几何366、约束冲突0。**这是build23历史结果。** 24把来源Picker换为按钮后，8次切源的candidate24回归未再记录发布期状态修改；负几何仍在。相同Mach-O UUID使统一日志中的进程路径解析为旧QA23；先前严格path过滤仅1行的0计数为漏报对照，不能用作最终结论。原始有界日志保留；它不覆盖未捕获stdout/stderr、其他进程、旧版或长期内存。

## build23启动/网页对照（历史）

QA23在任务Documents目录放配置/dataRoot时，首次initialize及两次显式重试仍超时；sample含`SystemNative_Open/open$NOCANCEL`，managed调用点及首因未定。相同前端/原host的QA24使用独立身份及`/private/tmp`，首次应用连接0.901秒即ready。`qa23-qa24-startup-contrast.json`保留变化因素，**不能归因于TCC、actor或单一路径因素**。

真正shipping production23（无QA配置/host、默认Library）首次应用连接1.3906秒即ready；进程来源见`shipping23-native-process-provenance.json`。它是独立生产启动证据，不抹去特殊配置路径超时。正式生产单片也已实际完成：原生OpenPanel导入独立tmp TEST-930并发现确认、本地NFO加载、标题改为“Shipping build23 — 本机正式包 NFO 保存”、开启覆盖/关闭ID前缀（未保存为偏好）、⌘S仅预览NFO Update和其余4项Keep、确认保存1片。随后清空会话确认显示0未保存，队列归0并正常退出。`shipping23-local-native-readback.json`确认5文件中仅TEST-930.nfo改变，标题正确，未知XML/注释/root属性/原演员role及扩展保留；其余4文件SHA/bytes/mtime不变，无新增/删除。默认Library settings前后均不存在，未保存偏好；App90451及Worker90455均已退出。

三来源已实际切换并自动导航：合成TEST-930在LibreDMM未找到，R18合成详情404明确显示失败，JAVLibrary live Cloudflare挑战令导入禁用。此前只读访问JAVLibrary搜索页/www根页/裸域根页均403，`Cf-Mitigated: challenge`、标题`Just a moment...`，见`work/browser-interaction-qa/javlibrary-readonly-network.json`。未操作CAPTCHA/绕过风控或TLS。

网页仅允许正常HTTP(S)，保留host/详情/番号、revision/generation及2MiB捕获验证。正常用户新窗口链接回当前页，脚本弹窗/危险scheme拒绝。外部浏览器只打开URL，不读内容/共享cookie/自动回传；**本轮未点击外部打开，三站live手动导入均未完成**。原固定HTML/loopback自动测试通过不能弥补此覆盖。

`shipping23-native-pid-bounded-log-summary.json`另记正式production23精确PID90451、01:51:42–01:57:25 UTC的1656行（含工具结束1条，实际事件1655）：发布期状态修改0、负几何54、约束冲突0。该单片流程也不能宣称日志全干净；与QA24分别保留范围，不合并或把QA路径解析结果当进程真实身份。

## 截图与历史

截图根：`outputs/Mac-interaction-redesign-2026-10-23/screenshots/`。

| 文件 | 范围 |
| --- | --- |
| `26-final-shipping24-browser-sources.png`、`27-final-shipping24-overwrite-blocked.png`、`28-final-shipping24-nfo-preview.png`、`29-final-shipping24-workspace-saved.png` | 真正最终shipping24：来源按钮、覆盖关闭时阻断、NFO安全计划与确认保存；对应最终5文件回读、PID644日志及交付全件绑定 |
| `23-build24-three-browser-buttons.png`、`24-build24-production-nfo-preview.png`、`25-build24-production-save-completed.png` | production candidate24的8次切源、单片NFO审核及保存完成；对应5文件回读和精确PID日志，非最终shipping二进制 |
| `11-final-results-dismiss-restores-actions.png`、`12-final-batch-save-completed.png` | 最终前端23/原生QA24：结果关闭立即恢复及批量保存 |
| `13-final-zh-normal-workspace.png`、`14-final-zh-minimum.png`、`15-final-en-minimum.png`、`16-final-ja-minimum.png`、`17-final-zh-hant-minimum.png` | 正常主窗口与四语最小布局 |
| `18-final-javlibrary-challenge-disabled-import.png`、`19-final-narrow-gallery-selection.png`、`20-final-dark-conflict-save-disabled.png` | live挑战禁用导入、图库保持与深色冲突阻断 |
| `21-shipping-production-nfo-preview.png`、`22-shipping-production-save-completed.png` | 原始正式production23/默认Library/正式Worker的单片NFO审核及保存完成 |
| `00`、`10`；`01`–`09` | 启动失败留档；QA22历史，其中06为关闭结果后动作禁用的已修复P2，不能当作最终截图 |

尚未解决：负几何警告与HTTPS子frame失败的泛化提示；build23的发布期状态修改已保留历史，24 candidate的8次及最终shipping的6次切源回归未重现。尚未验证：三站live手动导入/外部打开、物理IME/完整VoiceOver、长按、多显示器、真实外接卷/SMB/拔盘/满盘/断电、Intel/macOS15、Windows实机、长期无泄漏、干净Mac、Developer ID/Gatekeeper。最终源码ZIP校验见`results/source24-export-validation.json`；本轮loopback图片服务器已优雅退出且active请求归0，其他已完成项目不再列为待测。
