# build33 缓存来源切换与图片选择验收

日期：2026-10-24 America/Los_Angeles。基线为本会话build31；本轮32候选在原生保存回读时暴露来源URL/ContentId残留问题，33修正并复验。正式产物0.4.0/build33，arm64、macOS15+、ad-hoc签名，不公证。

## 实施

- Mac查询菜单移除“LibreDMM + R18.dev”，保留LibreDMM、R18.dev、自定义来源；配置入口统一为“设置自定义来源”。旧协议all模式保留兼容，不再出现在Mac菜单。
- 主页面双图右上角来源改为整部审核来源。只列出已载入的本地NFO、LibreDMM、R18.dev，不联网获取新资料。一次命令应用该来源的全部可用字段、可用封套和样张；缺失的可编辑字段保留当前值。手动候选可重新选择。来源名称、网址和内容编号也随该来源恢复，避免切回本地却新增外来provider ID/website。
- 字段来源混用或有草稿时显示“混合 / 当前资料”；若选择来源仅缺少部分字段，保留这些字段不会掩盖已选来源。JavLibrary候选仍能逐字段比较，未新增本轮未要求的整部JavLibrary选项。
- 新jobs.selectSource在Worker锁内先验证可用来源、空闲状态、review revision及快照容量，再应用修改并使原保存计划失效。没有另行查询或直接写文件的路径。
- artwork.contentKey由已注册位置/后备位置与本地文件大小、mtime、链接状态生成，排除selection和review revision。未变化图片的URL、尺寸、已解码缩略图和进行中的请求继续复用。变更/移除内容仍取消并清理；旧Worker无此key时保守按revision失效。异步响应仍检查当前job/artwork/contentKey与请求owner，文件保存仍使用原revision/指纹/全队列冲突检查和事务。
- 更多选项改为全宽Button，文字、箭头、行内空白均属于点击区域，AX状态显示展开/收起；原保存选项、Help和默认预览行为保留。

## 执行结果

| 范围 | 实际结果 |
| --- | --- |
| 完整门禁 | source33-gate.log，10/10套exit0；四语431 keys/1754检查；Core50/579断言；Swift54（18+36）；协议67（50生产+17固定HTTP）；queue4、retention5；发布工具36、manifest15 |
| 新协议测试 | source66验证本地/DMM/R18全字段切换、缺失字段保留、手动候选保留、来源信息恢复、无新增provider请求、拒绝无效来源和过期revision、旧计划失效及不写磁盘；source67验证图片选中/元数据编辑保持contentKey，而本地图片替换改变key |
| 新Swift测试 | 已加载路径/尺寸/低清标记和request key跨勾选保持，图片实际替换时失效；真实Worker本地三图勾选→refresh仍保留预览，整部本地切换恢复字段并保留手动候选，NFO/视频保持原样 |
| 原生33菜单/切源 | 发现确认1片；自定义查询一次取得两份受控JSON；菜单无组合来源、配置新名称正确；主页面LibreDMM→R18→本地→LibreDMM，标题、原始标题、日期、时长及双图按来源变化，本地来源网址清除。图11–14与native33-local/r18文本 |
| 原生33选图 | DMM图库两张样张交替勾选8次，四张图片按钮每次均保留；计数12→12，新增图片请求0。累计12包含前一候选的请求，测量窗口由before/after JSON界定；图15–16、native33-toggle-log.json、images33-selection-result.json |
| 原生33保存 | 覆盖关闭时预览禁用确认；切回本地再改标题，明确允许覆盖后预览1 NFO Update+5 Keep，确认成功；NFO除title外原始字节完全一致，没有新增website/外来ContentId，其他5文件SHA/bytes/mtime不变，无增删。图17–20、native33-save-readback.json |
| 更多选项 | 正常/最小窗口AX按钮展开/收起通过；另外实际按截图像素点文字[1380,834]展开、行内右侧空白[1750,834]收起，均避开箭头。图21–23、native33-more-pointer.json |
| 签名/解包 | source33-package.log，17 native文件签名/entitlements/依赖/资源、无SDK Worker通过；解包生产协议50、queue4、retention5通过；仅ad-hoc，不公证 |
| 绑定 | QA33与交付33去签名前端代码、全部资源、生产Worker/Core DLL一致；交付App完整payload/hash/mode/签名属性与正式包一致。QA33额外固定HTTP host与私有data root不交付，未冒充live provider |

原生使用一个不可播放的合成影片占位文件、中性PNG、NFO和回环图片服务；未扫描真实片库。固定HTTP host运行生产Workspace、真实provider解析器及文件事务，不证明真实网站可用性。普通生产App没有fixture入口。build32图01–10和当时包含额外uniqueid/website的NFO差异保留为失败证据；最终33图11–23及回读另列，不混用版本结论。最初32创建provenance不作为最终33绑定依据。

## 交付与未验证

会话outputs/Mac-source-switch-2026-10-24/含JavMetaLite.app、ZIP、manifest、results和screenshots。退出旧版后打开新App；自包含Worker无需SDK。输入284项中19项变化，无增删；Core/WPF及MovieJob/NFO writer/BatchSavePathConflicts/事务源码hash与开工前相同。无reset/commit/push，原31包保留。

源输入digest：`effbc8e4fe876820404ffda31dc0e8b634c1f7b5330c40b08e9d1bbec17dc331`。ZIP SHA256：`345f13d089750efc5f7f73c4c703eb46962d862db7455d1deec01672fff0c442`。

本轮没有重新验证live网站、物理滚轮/捏合、IME、完整VoiceOver、Windows/Xcode/Intel/macOS15、外接卷/SMB/断电/满盘、Developer ID/Gatekeeper或未保存退出提示聚焦。33在已保存或空队列时正常退出，不扩展为这些项目通过。历史Cloudflare和未保存退出提示边界继续按STATUS保留。
