# build41 — 首次保存预览与取消恢复

2026-10-24，America/Los_Angeles。基线为build40，交付 **0.4.0/build41/arm64，ad-hoc签名，不公证**。使用一次性合成影片、NFO及中性图片，没有扫描真实媒体库。本轮的首次预览和取消恢复目标已完成原生复验；不扩大到所有硬件、站点或异常存储验收。

## 已查明的代码原因

新身份QA43使用未修改的生产build40 Worker，选择单个TEST-930目录，第一次保存预览再次超时。`dotnet-stack 10.0.750501`抓到两条同时存在的托管调用栈：

- `FileSystemPathSemantics.ValidateUnambiguousPath → FileOrganizationService.BuildPlan → BuildPreviewAsync`停在`Interop.Sys.OpenDir`；目标容器的检查会枚举其父目录，超出了用户选择的影片目录。
- 处理状态查询的`Workspace.Dispatch`停在`Monitor.Enter`。原预览把这段文件读取包在工作区`_gate`锁内，因此`operations.get/cancel`也无法及时处理。

这确定了应用中的阻塞位置及取消无法确认的原因；**没有把OS层长等待武断归因为Cloudflare、TCC缺陷、网络盘或死锁**。旧QA41的读取后来结束后，检查状态可以恢复；仅靠加长RPC超时不能解决锁被文件读取占用的问题。

证据：`qa43-blocked-managed-stack.txt`、`native40-repro43-timeout.txt`、截图02。尝试取heap dump被系统`task_for_pid`权限拒绝，未得到dump，未增加调试权限或削弱签名entitlements；不把失败的dump当作证据。

## 修复及保护边界

- macOS已有原影片目录、明确选择的自定义根目录，使用公开Darwin `getattrlist/ATTR_CMN_NAME`验证实际名称，拒绝大小写/Unicode拼写别名及直接symlink，避免枚举未选择的父目录。该方法只确认已有容器；**每个文件目标、新建/迁移影片目录仍保留完整歧义检查**。没有用路径根判断跨卷；非Windows视频移动仍需完整SHA-256验证。
- Worker在锁内只捕获审核快照和短状态变更，预览文件读取、规划、全队列输入保护及指纹计算移到锁外。快照保留owner身份用于冲突归属，不读取或改写其live审核字段。取消可结束等待，释放所选job的busy状态；迟到结果和失败被观察后丢弃，不能发布plan或覆盖新编辑。
- 最多保留一个尚未返回的只读检查，避免反复取消累积线程。若内核读取仍未返回，新的预览会明确返回JOB_BUSY，其他审核操作仍可用；不会把“请求取消”伪装为内核I/O已停止。读取返回后可正常重试。测试覆盖了这一等待边界。
- 配置/revision只在验证成功发布计划时绑定；源NFO新出现、祖先重定向、取消等情况不再提前改变revision。仍检查选中revision、队列版本及条目身份，再发放server-owned planId。
- 原生前端把已确认的CANCELED作为正常取消结果，清理旧预览并刷新状态，保留审核、勾选和编辑；不弹失败警告，不重发save.execute。真正未知状态、源变动及覆盖冲突仍保持阻断。

11个已有构建输入改变，新增MacDirectoryName.cs。WPF、MovieJob、NFO读写器、BatchSavePathConflicts、Saving.cs及journal执行源码hash/mode与开工前相同；FileOrganizationService仅调整容器验证调用，保留原事务路径。未reset、commit或push，历史WIP保持。

## 可执行测试

| 范围 | 最后结果 |
| --- | --- |
| 完整门禁 | `preview41-final-gate.log`：10套exit0 |
| Core | 51/583断言，含补充私有文件名案例和新增已有容器/别名/symlink检查 |
| Swift | 59项：18 WorkerKit + 41 NativeBridge/interaction；真实Workspace受控慢读取下取消、恢复编辑及重试覆盖保护通过 |
| 协议 | 71项：50生产 + 21非发布受控宿主；新增未返回读取时取消、保持revision/编辑、限量等待、迟到失败不复活计划、正常重试保存 |
| 原安全回归 | 晚出现NFO、祖先重定向、过期revision/plan、全队列冲突、取消与事务/journal、NFO扩展XML继续通过 |
| APFS大小写敏感镜像 | 52/602断言；16MiB跨实际设备ID完整SHA复制及复制取消通过。使用独立shadow，detach成功，原镜像SHA未变；不冒充物理外接盘 |
| 其他门禁 | queue4、retention5、发布工具36、manifest15，四语441 keys/1794检查 |
| 最终包/ZIP解包 | 签名、17 native文件、entitlements/资源/依赖/manifest、无SDK启动，以及解包生产50/queue4/retention5通过 |

第一轮回归抓到源变动时revision被提前增加的三个失败断言；已把配置提交推迟到成功验证之后，随后全71项及最后完整门禁通过。初次新增测试把“真实保存后保留completed journal”误断言为空，已修正为取消前零journal、保存后completed记录；没有删除journal来满足测试。失败日志保留，不混入最后通过计数。

## 原生实测

两个build41测试身份都与最终交付的编译前端、资源、生产Worker/Core DLL绑定；身份/私有状态和测试shim/宿主不交付。

| 场景 | 实际观察及文件回读 | 证据 |
| --- | --- | --- |
| QA44：生产Worker首次冷启动 | 新身份，仅选叶子影片目录，没有先选父目录或预热预览；第一次保存正常展示预览，覆盖关闭时Confirm Save禁用。命令到AX观察1093ms，仅一次工具观测，不是内部延迟统计 | 03、native41-first-preview.txt/timing.json |
| QA44：取消预览/保存 | 取消后审核保留；明确允许覆盖并关闭标题加番号，预览1 Update+3 Preserve，实际保存成功。只修改TEST-930/movie.nfo的title字节；其余XML和7文件SHA/bytes/mtime不变，无增删 | 04–05、native41-overwrite-blocked-readback.json、native41-save-readback.json |
| QA45：慢读取取消 | 非发布RegressionHost注入单个不响应取消的读取gate，仍用生产Workspace/Core；gate存在时按取消，614ms工具观察已恢复编辑，原标题/勾选保留、无未知状态/失败弹窗 | 06–07、native41-controlled-cancel-timing.json |
| QA45：取消后继续编辑 | gate仍关闭时把标题改为“Final41 取消后继续编辑”；8文件完全不变、recovery journal为0。之后才放行读取 | 08、native41-edit-before-reader-release.txt、native41-cancel-no-write-readback.json |
| QA45：迟到读取后重试 | 新标题保留，首次重试仍被覆盖保护阻止；明确允许后重新预览及保存成功。只写新标题，其他NFO字节与7文件不变，无增删 | 09–10、native41-controlled-retry-saved.txt、native41-cancel-retry-save-readback.json |

QA44使用argv-only私有data-root shim，执行未替换的生产Worker，没有固定HTTP。QA45只控制读取时序，没有联网抓取，不能冒充真实断盘/SMB阻塞。QA44、QA45已清空测试队列并正常退出；本轮后续清理不能改写上述磁盘回读结论。旧build40反例单列，不冒充修复版的退出或保存结果。

## 运行及仍未验证

退出同名旧版后，打开会话 `outputs/Mac-preview-recovery-2026-10-24/JavMetaLite.app`。Apple Silicon、macOS15+，Worker自包含。相邻ZIP、manifest、results和screenshots含完整门禁、调用栈、原生观察与回读；截图01–02属于旧40，03–10属于最终41。原build38/40交付不覆盖。

仍未验证：所有真实外接盘/SMB/设备丢失/满盘/断电、Windows/Intel/macOS15/干净Mac、物理IME/触控手势/完整VoiceOver、live站点及Cloudflare长期稳定、Developer ID/Gatekeeper。未重做完整四语言原生弹窗/最小窗口矩阵，本轮四语为自动资源检查；上轮布局证据仍独立保留。内核层不可取消的I/O不会被本修复强杀或伪称完成。

按用户决定只签名、不公证；本机使用ad-hoc，未宣称Developer ID或Gatekeeper验收。

交付归档从已原生实测的不可变`candidate41.app`生成。常规打包脚本重链接后前端hash不同，虽源码digest/Worker/Core相同，也没有将其冒充同一前端。最终选用不可变包，重新完成签名/manifest/ZIP解包生产50、queue4、retention5验证；见`preview41-tested-package.log`。最终交付前端去签名代码及全部资源、生产Worker/Core与QA44/45精确绑定。
