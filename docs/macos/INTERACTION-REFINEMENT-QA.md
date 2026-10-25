# build31 交互改进与原生验收

日期：2026-10-24 America/Los_Angeles。完整门禁、签名打包、原生流程及磁盘回读已完成；最终交付另有极小四语提示文案修正，逐项记录在下方。

## 实施范围

- 主编辑页隐藏Series/ContentId两项无效编辑展示，Core round-trip写入语义保留。
- 右上角与⌘S统一为保存。默认开启预览；更多选项增加“跳过保存预览”。跳过只省略确认sheet，仍调用异步save.preview并验证当前选项，再用服务端planId保存；不可执行或过期计划仍展示阻断详情。覆盖NFO/图片的许可独立保留。
- 队列工具栏及⌘⌥L可隐藏/恢复；⌘F会恢复队列并聚焦搜索。
- 新查询采用配置来源的最新内容，旧手动值保留候选；失败重试保留审核选择。十二字段可分别选择LibreDMM/R18.dev，配置取消不改变偏好，非法协议值在队列/network变更前拒绝。
- 导入成功并收到Worker确认/刷新后关闭浏览器，失败保留窗口。使用应用内持久WebKit站点状态和默认User-Agent；仅子frame额外允许about:blank/srcdoc，主页面/抓取仍限制HTTP(S)，禁用下载、脚本弹窗和本机scheme。
- JavLibrary兼容旧?v=与网站现行语言目录下jav….html详情页，继续检查实际页面番号/最大2MiB/revision，不允许搜索页或挑战页导入。
- 查看器左侧关闭、图片边缘左右导航、实际尺寸按钮与实时百分比；NSScrollView原生画布支持鼠标滚轮缩放、精确触控板滚动平移、⌘滚动缩放与捏合。
- ICNS直接封装原有Windows品牌ICO中的PNG，不引入新品牌；SwiftPM/Xcode/CLT资源和包校验注册，运行时设置Dock图标。

## 网站与Cloudflare界限

发现旧实现拦截子frame的about:blank/srcdoc，且每次关窗丢弃站点状态。前者不符合[Cloudflare WebView配置说明](https://developers.cloudflare.com/turnstile/get-started/mobile-implementation/)，后者影响重复验证；不能据此认定所有无限循环的唯一根因。Cloudflare也将[嵌入浏览器列为有限支持](https://developers.cloudflare.com/cloudflare-challenges/reference/supported-browsers/)。本轮不伪装UA、不迁移外部浏览器cookie、不自动完成CAPTCHA。用户手动完成网站年龄/条款确认。

build29候选已实时打开JavLibrary IPX-081搜索结果并点击对应`https://www.javlibrary.com/cn/javli43n4q.html`，随后发现旧?v=专属检查拒绝导入。build31中用户完成网站确认后，新格式详情可导入，成功后自动关闭，主页面显示JAVLibrary来源和最新资料。再打开浏览器直接到同片详情（含#_片段），没有再次要求条款。App完整退出重开后站点会再次要求条款；不保证站点会话cookie跨进程永久保留。LibreDMM IPX-081页面导入成功后实际自动关闭，未把新资料保存到磁盘。

## 证据位置

会话`work/interaction-next-qa/`保留源基线、隔离合成夹具、QA身份、文件回读；`outputs/Mac-interaction-next-2026-10-24/`将包含交付App、ZIP、结果和真实截图。原29候选截图01–06与最终包截图按文件名区分。

## 仍待验证

物理滚轮/双指捏合、中文日文IME、完整VoiceOver、Xcode、Windows实机、Intel/macOS15、外接卷/SMB/满盘/断电、Developer ID/Gatekeeper及长期Cloudflare稳定性。仅ad-hoc签名，不公证。

## 执行结果与版本边界

| 范围 | 实际结果 |
| --- | --- |
| 完整可执行门禁 | `interaction31-final-gate.log`：10/10套exit0；四语426 keys/1734检查；发布工具36、manifest15；Core50/579断言；Swift52（18+34）；协议65（49生产+16固定HTTP）；queue4、retention5 |
| 最终文案修正 | 结果页说明与日文隐私范围改为“通过保存计划校验/默认开启预览”，分别重跑四语结构及实际Bundle解析1734检查，再重建签名包。逻辑源码未再改；不把旧截图冒充重新打包后的二进制截图 |
| build29候选 | 默认预览、skip仍阻止覆盖、明确允许后单片保存；001 NFO标题更新并新建002 NFO，其余10个文件保持SHA/bytes/mtime；001非title XML语义保留。来源配置、双图查看器及真实LibreDMM导入自动关闭通过 |
| build31首次原生 | 发现3片；默认预览阻止已有NFO覆盖；skip开启后两片批次整体阻止，11文件完全不变；允许覆盖后直接完成两片。只改001 NFO并新建002 NFO，其他10文件保持不变。四个新交互界面和最小窗口已截图 |
| build31签名包复验 | 两片现有NFO仅标题按预期更新，10文件保持SHA/bytes/mtime；未选第三片不变，001原非title XML语义保留。skip仍阻止未经允许覆盖；清空3/1未保存草稿后磁盘不变，App与Worker正常退出。截图17–22对应最后日文说明修正前的签名包；最终资源/二进制绑定和追加截图另存results |
| 实时网站 | build29 LibreDMM IPX-081导入成功自动关窗；build31 JavLibrary IPX-081搜索→对应`/cn/javli43n4q.html`→成功导入→自动关窗→重开同片详情通过。网站本人确认由用户完成；没有自动处理CAPTCHA。最后日文文案修正后的完整App重开仍出现网站条款，未代用户再次接受 |

`source-before.json`记录开工时283项输入。最终仅28项既有输入变化并新增原品牌ICNS，WPF输入保持hash/mode不变；Core只改BrowserPageImporter新详情地址检查。MovieJob审核、NFO round-trip写入器、BatchSavePathConflicts、文件事务/journal源码与开工前一致。Worker查询逻辑是本次明确改动；未覆盖历史WIP、未commit/push/reset。

QA29候选结束时，CUA无法正确聚焦/读取未保存退出提示，后续主窗口AX动作又进入了嵌套NSAlert模态循环。保留`qa29-quit-sample.txt`，通过活动监视器仅对已确认PID29635的QA29强制退出，Worker随EOF退出；这不是正常退出验收。最终QA31在清空临时草稿后两次正常退出。**未保存退出弹窗的聚焦与自动化读取仍需人工复查，未宣称已修复。** 用户原会话PID24526（原build28交付）与Worker24529保持运行，未操作它们。

截图01–06为29候选，07–16为31首轮，17–22为修正批量提示后的31复验。均为实际原生窗口，合成中性图片；网页验收使用AX文本记录，未将网站实际成人图片放入交付截图。工作包与交付包的身份/私有数据根shim差异写入provenance与verification-summary；不交付QA shim。

## 最后交付绑定

最终输入digest：`a4791f5035d651c28a6c29098237d97ce4af68b622c966f8ee331277dc3e766e`。ZIP SHA256：`97ed20c42844e06a0d1e0603bc92592b246ca3d1538fdb064eaaf76f63d5acb2`。`verification-summary.json`记录当前交付payload与生产包逐项相等、去签名前端代码SHA和Worker/Core DLL SHA。最后日文说明修正后已再次进行原生导入、编辑、单片安全保存、图片左右切换/100%、设置日文核对和正常退出；截图23–25、`delivery31-native-readback.json`对应这一次。001原NFO除title外原始字节完全相同，其他11文件SHA/bytes/mtime不变。

运行：先退出用户当前旧版，再打开`outputs/Mac-interaction-next-2026-10-24/JavMetaLite.app`；最低macOS15，Apple Silicon，Worker自包含。原28交付保持原样。只ad-hoc签名，不公证。
