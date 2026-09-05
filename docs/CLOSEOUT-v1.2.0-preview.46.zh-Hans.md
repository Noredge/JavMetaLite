# preview.46：LibreDMM 默认推荐（内部审核）

日期：2026-09-04。未提交、未推送、未打标签、未对外发布。保留已有修改、SDK/依赖缓存及 preview.42–45 便携包。

## 产品决定与实现

- LibreDMM 为默认推荐来源；移除原“多来源搜索（推荐）”，保留 R18.dev、自定义多来源和手动模式。自定义模式仍查询两站，遵循已有字段/整套图片选择规则。
- 新用户、安全默认配置、缺省来源参数及旧 Auto 值均使用 LibreDMM。旧 JAVLibrary 值仍转为手动模式；明确保存的 R18/Custom/manual 选择及自定义规则不变。
- 搜索、批量搜索和失败来源重试继续使用同一来源规则；默认批量不请求 R18，不自动补充其他来源。
- 批量搜索栏下方显示非弹窗说明。LibreDMM 使用普通提示；R18/Custom 使用柔和黄色说明限流或网络状况可能导致搜索较慢。单片和手动模式不显示批量提醒。
- R18 提供英文资料及部分影片可能更高分辨率的图片，不承诺每部都更高清。提示及推荐名称覆盖四语言。
- 为容纳完整英文推荐名称，搜索栏的统一紧凑布局阈值调整至 600；所有来源使用同一阈值，切换来源不改变控件排列。

## 验证

```powershell
.\scripts\Test-Automated.ps1 -DotNet .\.dotnet-sdk\dotnet.exe -Configuration Release -NoRestore -BuildOutputDirectory bin/preview46-validation/
```

- Core **46/46**、文件事务回归 **30/30**、WPF UI、包版本校验全部通过；`git diff --check` 通过。
- Core 默认参数测试：71 片只调用 LibreDMM 71 次，R18 为零；来源矩阵使用独立预期值检查四个模式、旧 Auto/JAVLibrary 和无效值。
- 偏好测试涵盖旧 Auto 迁移、Custom 规则保留、显式来源持久化及缺省设置。
- 真实 WPF 按钮搭配模拟 HTTP：71 片 LibreDMM 搜索期间 R18/JAVLibrary 零请求；源选择快照、Custom 双来源、R18 单来源、手动保护、定向重试、当前影片查询均通过。
- 四语言 930×650 检查：推荐名称、提示可见性/颜色、无重叠及越界；既有底部失败状态、保存安全、图片查看器和键盘操作回归通过。
- 测试调整了旧用例依赖 Auto 默认值的部分：需要两来源的用例现在明确选择 Custom。多语言用例恢复进入前的语言，避免污染其他窗口用例。
- 不做真实网站批量压力测试，不操作真实影片库；模拟计数不是网络速度承诺。

## 内部便携包

- `release/JavMetaLite-v1.2.0-preview.46-win-x64-portable.zip`
- SHA-256：`D959A995F7A754C841948DDF85F2C75FFE7F462EF0C0205C762F2203F1FEDE88`
- ZIP 60,434,258 字节；EXE 65,925,218 字节。
- 独立解压目录：`release/.verify-preview46-4ca94e91ccd2423fb727c33a1eccc17c`。
- 4 个文件与打包来源逐文件哈希一致；ProductVersion 为 `1.2.0-preview.46`、FileVersion 为 `1.2.0.0`，README 版本匹配。
- 独立校验文件和总清单一致；preview.42/43/44/45 原包重新计算哈希，均与保留的清单条目一致。
- 未用打包 EXE 载入用户实际队列；真实窗口行为由 WPF 自动化验证。

## 后续边界

不引入 R18 全量本地库，也不添加翻译模型、API 或依赖。用户询问的 Jellyfin 标题与影片标签翻译插件仅讨论可行性：尚未创建、开发或安装，不扩展现有 Local Rating 项目。JavMetaLite 的类型主要写入 NFO `genre`，厂牌另写为 `Label:` 标签，后续翻译范围需明确区分。

正式外部发布继续等待大队列加载/交互/搜索性能测量及必要优化；本内部版本完成不代表发布授权。
