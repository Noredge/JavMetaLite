# macOS 布局与图片预览验收记录（本地候选build19）

2026-10-23；Apple M5 / 16 GiB、macOS27.0.1，仅CLT。本轮从已交付 build9 开始，使用可丢弃合成影片、NFO和合成图片，未扫描真实媒体库。主验收者提供原生GUI观察；工具日志、文件回读、固定HTTP与自动测试分别标明，不能相互替代。本轮交付的本地候选为 **build19**。18的完整门禁、主包和独立源码重建已通过；19原生目标复验、9套门禁、正式ad-hoc包及GUI前端身份绑定已完成；独立无.git源码重建及主包/交付/解包身份对照亦已通过。40项按各自证据范围为33通过、6待测、1不适用，不代表全部在19重跑。

工作证据位于会话工作目录 `work/native-remaining-qa`，测试日志位于仓库 `artifacts/test-results`；本轮本地候选输出根为 `outputs/Mac-regression-layout-preview-2026-10-22`。以下相对路径均按上述位置解释。私有附件全文和真实长文件名不随本文复制。

## 候选沿革与反例

| 候选 | 实际操作与结论 | 不可扩大的范围 |
| --- | --- | --- |
| 9 | 旧查看器在受控HTTP503终态失败后仍显示loading，实际复现。 | 旧包失败保留，不因新测试通过而删除。 |
| 10 | 修复错误/详情/重试；四语言16布局、样张术语各入口、深浅冲突普通/选中文字/禁用确认留图；完成下节慢图测试。 | 正常窗口约1067×762点、最小内容920×620点。截图不证明物理IME/完整VoiceOver。 |
| 11 | 英文队列状态改为完整换行；四语言16组合再次回看，英/日最小窗底部批量操作可滚动到达。 | 其后观察到同类选择通知警告；早前布局成功不证明没有图库冷启动问题。 |
| 12 | 应用户标题栏/侧栏线交接反馈，使用公开 `titlebarSeparatorStyle = .none`。自动门禁/打包/独立重建通过，但16布局后的图库切换真实高CPU/布局循环。 | 不交付。保留sample、Activity Monitor截图、402个RSS样本；不能由自动门禁宣称原生稳定。 |
| 13 | 加入同值导航去重及真实通知计数回归，Local2/检查器展开/普通窗口首次进入图库仍因AppKit约束循环保护而终止。 | 去重不能被写成整个崩溃已修复。2012保护文件回读完全不变。 |
| 14 | A/B仅将分隔线改回 `.automatic`，同路径仍崩溃。 | 说明 `.none` 不是故障必要条件；不是系统缺陷归因证明。 |
| 15 | 改有限固定列宽，首次图库切入仍崩溃；先隐藏检查器加载图库后，再反复切页/开合/批量导航可用。 | 热路径成功不能替代冷启动复演。 |
| 16 | 完全不调用分隔线setter，冷启动同样失败。 | 再次保留反例，不能仅撤销视觉API就宣布修复。 |
| 17 | 图库由LazyVGrid改为有限列宽、LazyVStack按行懒加载；Local2→双选Space勾选2→单选→展开检查器→首次图片页通过。 | 约0.74秒是一次工具观察，无预定性能阈值。 |
| 18 | 保留按行图库并恢复 `.none`，同一冷启动复演通过，约0.84秒。完成主界面四语言16组合、样张选择保持、QA18慢图，以及9套门禁/打包/独立重建。 | 首次批量预览超时未定因；62,494条视图更新期间发布警告仍真实存在，不能称整体无故障。 |
| 19 | 修复队列导航在视图更新期间发布状态；实际冷启动图库/展开检查器、1002项搜索/导航与1004项队列的浅深冲突预览通过。 | 指定PID/区间Publishing警告0条，负宽/高各135条仍存在；9套完整门禁、正式包与独立源码重建已通过；不能从门禁推导所有原生场景无问题。 |

已保存的崩溃/挂起证据包括 `build12-ui-timeout.sample.txt`、`build13-gallery-crash.ips`、`build14-gallery-crash.ips`、`build15-gallery-crash.ips`、`build16-gallery-crash.ips`。13/14报告为主线程SIGABRT与AppKit约束更新栈，不能由这些堆栈推导已发生错任务文件写入。正常保存仍需要明确的plan与revision校验。

## QA10 慢图片：原生动作与服务器证据分开

QA10是独立bundle ID/签名的测试App，使用同一阶段编译前端、非交付RegressionHost及真实loopback HTTP服务器。以下均为合成图片，不是live网站或远程成人图；请求号来自 `image-requests.jsonl`，汇总为 `native-remaining-gui-image-evidence.json`。

| 请求 | 服务器观测 | 主验收者原生观察 |
| --- | --- | --- |
| 22→23 | sample01首次注入503、0bytes；重试完整540bytes，147×200。 | 终态错误/详情/Retry Image可见；显式重试成功。 |
| 24→25 | sample02等待响应头时客户端断开、0bytes；再次请求待gate释放后完整420bytes，257×126。 | pending关闭，再打开并重试成功。 |
| 26 | sample03发送首1byte后等待正文；客户端断开。 | Escape关闭pending查看器。 |
| 27 | 同sample03再请求，首1byte后取消；随后才释放旧gate，没有该请求的完整bodySent。 | 切到Local，400×600本地图在旧gate释放后保持。 |
| 28–32 | SourceAll后额外请求：28小封面2040bytes、29完整封面4588bytes、30sample01 540bytes、31sample02 420bytes；**32**才是sample03重试完整4588bytes，1200×800。 | 回到All后sample03成功；不把28误记为该样张成功。 |
| 33 | sample04响应头前取消、0bytes；随后释放gate。 | Escape后切换另一任务，其本地147×200在释放后保持。 |

合计12请求：7完整响应、1注入HTTP错误、4客户端取消。完整bodySent只证明服务器写完响应，显示结果来自独立GUI观察；取消先于gate释放，不伪称取消后完整迟到图片仍被交付。GUI没有读取reviewRevision，相关状态隔离由独立Swift Store/Worker测试补证。

原始Local/Conflict共12文件（4合成视频、2 NFO、6 PNG）SHA256和bytes与 `fixture-baseline.json` 相同、无新增/缺失。**这份早期基线没有mtime，不能说QA10前后mtime已验证。**

## 真实保留时效与文件保护

`measure-real-expiry.py` 使用已交付App9 Worker、两个独立可丢弃data-root、无SDK环境，无网络与时钟注入，实际耗时1808.446秒，exit0：

- 发现令牌实龄908.239秒：`DISCOVERY_STALE`，发现会话仍0 jobs，小回执标记expired。
- 未执行plan实龄908.058秒：`PLAN_STALE`，队列、Worker手动review/revision及媒体SHA/mtime不变；尚未30分钟的旧保存回执仍存在并幂等。
- 旧保存operation实龄1808.023秒：`OPERATION_NOT_FOUND`；其旧已执行plan为`PLAN_STALE`。最近回执实龄899.919秒仍可读，重复execute复用同operation，不新增事务。
- 仅两个预期NFO控制保存，未知XML、actor扩展与resume保留，恰好两个completed journals；3合成视频不变。两Worker退出0，会话cache均删除。

证据为 `native-remaining-real-expiry.json` 和 `native-remaining-real-expiry.log`。比较App9与11的198个Worker payload文件：197个完全相同，native apphost仅嵌入签名区32字节不同，非签名字节一致；Worker/Core DLL同SHA。该关联不是“App11实际运行30分钟”。build18主包的Worker/Core DLL与App9同SHA（`native-remaining-source-rebuild-build18-delivery-comparison.json`），不把独立重建产物的不同二进制SHA冒充逐字节相同。最终19的198个Worker payload文件与9比较：197个整文件相同，native apphost嵌入签名区不同，排除该签名区后的SHA完全相同；Worker/Core DLL一致，见`final-build19-protection/worker-payload-comparison.json`。**该实现关联不能称19实际运行了这30分钟。** 真实TTL、五分钟循环和RSS观测也不是长期无泄漏证明。

build12之后的2012文件保护基线含mtime。build13崩溃后 `fixture-build13-after-crash.json` 对照 `fixture-build13-before.json`：2012/2012 SHA256、bytes、mtimeNs全同，新增/缺失/变化均0。该较晚基线不能向前补证QA10的mtime，也不证明崩溃时草稿或内存状态恢复。随后18独立CLI预览及QA18结束时再次回读，2012文件仍全同；19/QA19最终退出后再次独立回读2012文件，SHA/bytes/mtime全同、新缺0，见`native-remaining-qa19-image-evidence.json`与`final-build19-protection/fixture-protection.json`。

## build18主界面、术语及选择保护

生产截图在 `outputs/Mac-regression-layout-preview-2026-10-22/screenshots/build18/`：

| 文件前缀 | 实际范围 | 限制 |
| --- | --- | --- |
| `41-{en,ja,zh-Hans,zh-Hant}-{normal,minimum}-{single,batch}.jpg` | 四语言主界面正常/最小、单片/批量16组合；工具栏、编辑器、检查器与分栏实际查看。 | 部分长字段只显示编辑器当前视口；最小内容920×620点，不证明所有次级窗口无截断。 |
| `42-en`、`42-ja` | 最小窗口向下滚动后底部批量操作按钮可达。 | 可达与无需滚动始终可见是不同结论。 |
| `41-QA-*-fresh-default-{single,batch}.jpg` | QA18四语言8图，实际2360×1520物理像素。重拍Hans图是真单片，输出/保护开关与两预览按钮无相互遮挡。 | Hans替换图是查询后的未保存状态，不是同一Local-only长标题；不同QA bundle/测试宿主，不冒充生产来源。 |
| `43-*source-note*`、`43-*gallery-sample-output*`、`43-*sample-viewer*` | 四语言来源说明、图库写出/替换样张及查看器可见术语核对：sample(s)、スチル画像、样张、樣張一致。 | 按文件实际正常/最小尺寸分别记录，不声称12图都为最小窗或所有隐藏文字已见。 |
| `44*` | 主验收者取消一张样张，最小窗→Metadata→正常尺寸→Artwork，来源All保持不变；实际仍未选。 | 静态before图的复选框在视口外，不能单凭两图推导完整动作；after空选框可见，动作链来自原生操作记录。 |

`build18-single-visual-review.json`、`build18-batch-visual-review.json`、`build18-qa-fresh-default-visual-review.json`及`build18-terminology-visual-review.json`分别记录静态复核。原`41-en-normal-single`曾误拍批量页，原QA18 Hans single也误拍批量页，旧原件移入work并保留SHA，替换图均独立再看；历史问题没有删除。

## build18批量预览超时：保留未定因

生产App PID28311、Worker28314初次批量预览显示Working，随后报`WORKER_UNAVAILABLE`。未进行`save.execute`；也没有实际发出取消，因为操作前已经出现注意警告。采样时原生主线程在正常AppKit事件循环、Worker在stdin/runtime等待，没有采样到正在复制/哈希或同步管道写阻塞。没有原始stdio请求/回执转录，**根因仍为undetermined**。

主验收者随后Check Status在0.635秒恢复，同PID新只读预览1.106秒、深色1.849秒得到冲突阻断。独立最终18包Worker的新四任务会话，Local930样张未选且另有Local931，只预览Conflict2：0.055435792秒、两次operations.get即completed/canExecute=false。两个Local任务完整DTO及revision不变，2012文件SHA/bytes/mtime均不变，Worker退出0。新CLI使用兼容importInputs而非GUI发现历史；这能排除该输入的新会话稳定后端挂起，不能定位原会话超时原因。

证据：`build18-batch-preview-timeout-investigation.json`、`build18-batch-hang-*.sample.txt`、`cli-conflict-preview-build18.json`，截图`45*`。同一时段的Publishing警告不是已证明的超时因果关系；19导航修复也不能被称为该超时已确证解决。

## QA18慢图片：与QA10单独记录

QA18使用已核对的18同编译前端、独立bundle ID/签名及非交付RegressionHost。`native-remaining-qa18-image-evidence.json`限定服务器控制点2026-10-23 00:04:21Z至请求69于00:15:28Z结束（本地仍10月22日）；只包括请求34–69，排除QA10及19。

| 请求 | 服务器事实 | 原生观察与截图 |
| --- | --- | --- |
| 38、40、43→44 | 三个独立503尝试，各0bytes；解除失败规则后44完整540bytes、147×200。 | 错误/Details/Retry可见，显式重试成功；`48-QA18-*`。不将三个失败写成单次请求。 |
| 46、51→56 | 两次sample03各发送1byte后clientClosed；二者均早于00:14:28Z释放thirdBody。56完整4588bytes、1200×800。 | pending Escape关闭再打开仍pending，切Local1/3显示400×600，释放旧gate后仍Local；显式回sample03成功；`49-QA18-*`。 |
| 57、63→69 | 两次sample04响应头前clientClosed、0bytes；均早于00:15:15Z释放fourthHeaders。69完整23096bytes、960×540。 | pending关闭再打开，Escape切TEST931；释放后仍TEST931，其独立本图400×600可查看；回930显式重试成功；`50-QA18-*`。 |

合计36请求：29完整响应、3注入503、4客户端取消，requestEnd累计88478bytes。其余封面/样张实际重取也列入报告，不隐藏为“只下载三图”。**旧请求在gate释放前已断开，不能声称取消后仍收到完整迟到body。** 客户端当前来源/任务保持来自实际GUI，服务器日志本身不包含这些状态；GUI没有直接读reviewRevision。受控loopback与测试宿主结果不是live网站证明。

QA18结束后2012文件与build13基线的SHA256、bytes、mtimeNs全部相同、新/缺失0；6张关键状态图独立只读复核，其他动作链明确引用操作人记录。

## build19目标原生复验及仍存在的警告

实际App PID33379、Worker33383；冷启动图库且Inspector展开通过。先Local2，再发现/确认Queue1000共1002项；搜索60999后批量勾选仍2，清筛选后30Up/30Down到队尾，Space使勾选3→2。加入Conflict2共1004项，仅勾选冲突两片；NFO开、图片关、原位置重命名开、覆写关。浅/深色批量预览都列冲突并禁用确认，未execute；正常Quit，明确丢弃两项未保存编辑。

截图独立位于 `screenshots/build19/`：`51-cold-gallery-inspector-seam.jpg`、`52-*`发现/搜索/导航、`53-conflict-light-disabled.jpg`与`53-conflict-dark-disabled.jpg`、`53-main-dark-seam.jpg`。独立读图确认浅/深文字和禁用按钮可见；**`53-conflict-dark-selected-text.jpg`虽然名字含selected，未见可辨认的系统选区高亮，不作为“19已选中文字”证明。** 早期QA10的选中文字证据保留，不改写为19。

`native-build18-runtime-warnings-final-summary.json`记录PID28311的62,494条Publishing警告；符号指向selectNavigation/selectQueueNavigation及勾选导航。19修复后，`native-build19-runtime-warning-summary.json`在本地17:15:55–17:18:35、精确PID33379的保留日志中Publishing为0；仍有负width135条、负height135条，其根因未定位。日志范围以外或所有未来路径不能由“0”推导；不能把19写为无警告。

## 最终19包主界面与样张选择复验

`build19/54-{en,ja,zh-Hans,zh-Hant}-{normal,minimum}-{single,batch}.jpg`为冻结生产19前端的16组合，独立报告`build19-layout-visual-review.json`逐张核对语言、单/批量与尺寸：正常2134×1524、最小1842×1346物理像素，没有可见关键控件重叠。最小四语言单片、英/日批量的底部预览操作原先在视口之外，不能据此直接宣称可达。

随后主验收者从最终交付App启动（PID58326），通过真实发现导入Local2、勾选2、单选并展开Inspector后首次进入图库，冷启动通过（`59-*`）。在上述6个最小窗口组合，实际滚动Inspector到底部；`60-*`六图均独立看见两预览按钮、文字完整、没有控件遮挡，批量状态单片按钮正确禁用。可滚动到达不等于无需滚动始终可见。`build19-final-controls-visual-review.json`保留各图SHA和限制。

实际取消一张样张后，Metadata→原生Window Top Left最小窗口→Artwork→滚动到底部，Source All未变，样张仍0；正常Quit并明确Discard1。`61-*`after空复选框可见，before复选框在视口外；不能单凭这两张静态图证明整条操作链，动作及来源保持来自操作记录/AX。

另有独立QA19默认窗口四语言单片/批量8图，`54-QA-*-fresh-default-*.jpg`实际2360×1520像素。`qa19-final-frontend-binding.json`将生产16图所用候选、package主App和交付App绑定为同一前端签名字节SHA `8a843f0959ae6391d61a63a2666b3b532a50b10d1ef3e7a1c39d05cc8503f2d6`；QA19有不同bundle ID与签名，其非签名字节SHA与生产相同，为`9ef86d5ddd9ea7728b41ddf91cbc5775a166bea56f9e2f8859664380798190eb`。QA使用非交付RegressionHost，不能称其完整包与生产包相同。所有次级窗口与全部长文案仍未覆盖，X03/R08保持待测。

## QA19慢图片：最终前端受控复验

`qa19-initial-server-state.json`的`nextRequest=70`是服务器**已分配到70**，不是下一个新请求；70属于此前已查询TEST-930的封套请求，排除在本轮计数外。`native-remaining-qa19-image-evidence.json`严格统计71–97，共27请求：22完整响应、1次503、4客户端取消，requestEnd累计69794bytes。16张55–58截图及对应AX逐一只读核对；各图不直接显示内部reviewRevision。

| 请求 | 服务器事实 | 实际GUI结果 |
| --- | --- | --- |
| 73→74 | 注入503、0bytes；解除失败规则后完整540bytes、147×200。 | 错误、Details和Retry可见；显式重试147×200成功（55系列）。 |
| 75→79 | 75响应头前clientClosed、0bytes；重新打开后的79保持等待，secondHeaders释放后完整420bytes、257×126。 | pending Escape关闭，再开仍pending，释放后257×126（56系列）。79没有被取消，不能误记为取消请求。 |
| 80、85→90 | 80/85各发送首1byte后clientClosed，均早于thirdBody释放；90完整4588bytes、1200×800。 | pending关闭/重开仍pending；切Local1/3、400×600，释放旧gate后仍本地图；显式回样张重试1200×800（57系列）。 |
| 91→97 | 91响应头前clientClosed、0bytes；后续97完整23096bytes、960×540。 | Escape后切TEST-931，释放旧gate后仍该任务；其本地400×600图可查看，查看器来源选择实际为All images；回TEST-930显式重试960×540（58系列）。 |

日志在91于2026-10-23 00:25:40Z结束与fourthHeaders于23:47:27Z释放之间有实际跨日操作暂停；91在暂停前已断开0bytes，不能称请求持续等待23小时、后台长期稳定或完整迟到body仍送达。97于23:47:56Z结束。四个取消请求均早于各自gate释放；GUI证明当前来源/任务仍显示正确内容，服务器仅证明取消/实际发送。正常Quit/Discard1来自主验收者操作记录。QA19为同编译前端+固定provider测试宿主+真实loopback合成图片，不是live网站或正式生产Worker来源验收；与QA10/QA18分别统计。

## 门禁、资源记录与交付边界

- **18已完成**：完整9套门禁、主包验证、独立无.git中文空格源码重建；重建213.524秒exit0，49生产+5retention通过，278 selected inputs一致。`native-remaining-source-rebuild-build18-delivery-comparison.json`核对主包/解包payload及同编译QA18前端；独立重建二进制不逐字节相同，仅证明源码与构建流程，不声称相同签名ZIP。
- **19已完成门禁与正式包**：9套exit0，Core50/559（含私有16输入）、Swift37、Worker63＝49生产＋14宿主（17.692秒）、retention5（98.272秒）、本地化358 keys/1462 checks、release36、manifest15。完整门禁156.172秒、正式打包145.252秒；包内49生产测试13.764秒、retention5为98.258秒。278 selected inputs的digest为`fe2972b9f04412cb99b24232ee0f35ee6997b06c54f80da58b437cb701c01dd7`。原生日志窗口及文件保护分别记录，不用门禁替代GUI；独立无.git中文空格目录离线重建157.414秒exit0，49生产测试12.912秒、retention5为89.810秒，两次PACKAGE PASS；278 selected inputs的路径/hash/mode与前后主仓库完全相同。
- build18资源记录为约600秒主界面601样本、约120秒大队列121样本。后者frontend RSS峰值276016 KiB、Worker92640 KiB；没有预设阈值或长期无泄漏结论。首段14样本带旧build13阶段标记，报告明确实际payload为18且该阶段不明，未倒改原始记录。
- 当前40项为33通过、6待测、1不适用；仍待T03、X03、X09、R05、R08、R10。T08/X08/R06仅按其已记录的冲突可读性、术语/清理及慢图状态组合范围通过，不扩张为所有UI/性能场景。

物理长按、中日IME、完整VoiceOver/纯键盘、多显示器、实体外接卷/SMB/拔盘/磁盘满/断电、真实EIO硬件故障、Intel/macOS15、Windows实机、Developer ID及干净Mac分发仍未验证。继续只签名、不公证；ad-hoc本地包不能标为Gatekeeper分发通过。

最终包ZIP SHA256：`4ad3bf43de2a7cf7568154f377da5fb8cf49719a299b2a85abba14f2f284ea56`；manifest SHA256：`d11e5d135c8d29cd045a646fe4289f373ccb752a1677dbaa90d89863ece26484`。`native-remaining-source-rebuild-build19-delivery-comparison.json`核对主包、交付App、ZIP解包payload及签名扩展属性精确一致，无RegressionHost；独立源码的依赖、工具链、构建选项、本地化和278 selected inputs一致，但不同绝对路径产生的前端/Worker/Core二进制SHA不同，不宣称bit-for-bit可复现。独立重建使用文档冻结前的临时源码快照；最终源码交付另由总清单绑定，不将临时源码ZIP冒充最终归档。

全部原生验收客户端正常退出；受控服务器0 active、所有gate释放后停止。40项矩阵当前失败0只指40行范围内没有尚未解决的失败项，**不表示无已知问题**：18首次预览超时根因仍未知，19精确短日志窗口负宽/高警告各135条的根因未定位，未测平台和6项待测仍保留。
