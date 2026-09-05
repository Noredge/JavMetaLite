# v1.2 截图目录 / Screenshot provenance

当前界面基线：**1.2.0**。2026-09-05 正式定稿构建重新渲染，素材用于本地审核，不代表已对外发布。

## 当前截图

- 单片与多片：简体中文、繁體中文、English、日本語各一张，共 8 张。
- 查看器：简体中文、English 各一张；本地 2 张，当前来源在线 6 张，样张 4/4，位置 3/8。
- 四语言主界面与查看器已逐张目视核对；默认宽度下图片标题不再与按钮重叠，移除按钮和目标位置文字完整可读。

![多片工作区，合成演示数据](images/javmetalite-v1.2.0-main-batch.zh-Hans.png)

![图片查看器，合成演示数据](images/javmetalite-v1.2.0-artwork-viewer.zh-Hans.png)

## 来源与边界

使用当前完整门禁构建的真实 WPF 控件，而非设计稿。主窗口默认 1120×790 逻辑尺寸，输出 1104×751；查看器输出内容区 992×689，不含 Windows 标题栏与外边距。96 DPI 的 RenderTargetBitmap／VisualBrush 渲染不等于在不同系统 DPI 下人工验收。

- 影片、标题、图片、日期及 `C:\Demo\` 路径均为合成；未读取真实媒体或使用用户提供的成人封套作为发布素材。
- 来源名称仅演示界面；图片通过内存 fixture 提供，没有请求真实网站，删除回调禁用。
- 截图状态由夹具初始化，不是完整操作录像。底栏准备就绪、已搜索影片的搜索按钮禁用均为演示状态。
- 本地生成入口：`artifacts/release12-final/Capture.csproj`、`Capture.cs`；复用 `JavMetaLite.UiSmokeTests/bin/release12-final/` 的程序集和现有 SDK。截图工具不进入发布包。
- 截图夹具对主窗口使用固定客户区取景，查看器单独按内容区取景；已修正初次渲染中的额外留白／裁边，不修改或美化产品界面。
- 所有旧图片保留。下面两项状态示例仍来自 preview.55 的合成回归，必须保留其历史版本标注，不能冒充正式版截图。

历史状态示例：[preview.55 低分辨率提示](images/javmetalite-v1.2.0-preview.55-cover-warning.zh-Hans.png)、[preview.55 延后预览](images/javmetalite-v1.2.0-preview.55-deferred.zh-Hans.png)。

## 当前文件清单

哈希只标识截图，不是程序包校验值。

| 文件 | 字节 | SHA-256 |
| --- | ---: | --- |
| [artwork-viewer.en.png](images/javmetalite-v1.2.0-artwork-viewer.en.png) | 91,351 | `D5F0E3F03D923B7463A8A2F2FC4E42171BA0F301F10BEA0EB0CC43794A0CEA05` |
| [artwork-viewer.zh-Hans.png](images/javmetalite-v1.2.0-artwork-viewer.zh-Hans.png) | 87,960 | `001079950C577A252BEF4B48B683450E85FD821952453EA3297B6BD1360AEAA4` |
| [main-batch.en.png](images/javmetalite-v1.2.0-main-batch.en.png) | 134,702 | `1AA5230C15E9BB33F45F0C9F58D5B71590B381797838CCA64BFC2318CB1456D2` |
| [main-batch.ja.png](images/javmetalite-v1.2.0-main-batch.ja.png) | 165,141 | `9CDC22F856F6D67F8AC6993CB674AC3EFD3495073421CC9DF8548433E12DCEB2` |
| [main-batch.zh-Hans.png](images/javmetalite-v1.2.0-main-batch.zh-Hans.png) | 128,612 | `284D1FD1410E4D995F8A0AF60500ED0BA9B424707BB1000530575922F94AAC0A` |
| [main-batch.zh-Hant.png](images/javmetalite-v1.2.0-main-batch.zh-Hant.png) | 130,145 | `DC31BB60297E5F1278020F516388EE36ACAD553DA45A6B6E506B86C2EFE7E595` |
| [main-single.en.png](images/javmetalite-v1.2.0-main-single.en.png) | 104,925 | `BD30DA4EDDE9E812D568EE38B28D208CE61670D2231D8C2A13DF03DCBDEB1EBE` |
| [main-single.ja.png](images/javmetalite-v1.2.0-main-single.ja.png) | 121,497 | `FA3724B27464D6D00E6949ABBC6ABEED49F1D20626077BD38A99BED5ADC1AF42` |
| [main-single.zh-Hans.png](images/javmetalite-v1.2.0-main-single.zh-Hans.png) | 99,585 | `E8B12B5858CB112644AC2C9690C90731D958067027EB1D03DE42FC22D1D0B24E` |
| [main-single.zh-Hant.png](images/javmetalite-v1.2.0-main-single.zh-Hant.png) | 99,867 | `DA9247E83CA6ACDFF6BBF673CC5F78D53D49ED128E34D520B034017287106F70` |

主界面底栏已核对为 v1.2.0，不带预览版标记；合成演示与历史图片的版本说明须保留。
