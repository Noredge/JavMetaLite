# preview.51：队列连续方向键焦点修复（内部）

日期：2026-09-04。基线：preview.50，本地提交 `02def79`。

用户已完成实际长按复测并反馈“测试没问题”。本补丁验收通过，作为保存性能优化前的本地基线；不代表公开发布授权。

## 问题和复现

用户长按批量队列的下方向键时，界面语言意外切换。录屏与日志中的简体 → 繁体 → English → 日本語顺序与语言下拉框一致；不是影片资料改变了界面翻译。

原实现只在队列的 PreviewKeyDown 中处理上下键。选择影片会进入 RunBusyAsync，暂时禁用队列；WPF 此时可能将键盘焦点移至仍可用的语言框。加载完成后才通过 Dispatcher 恢复队列焦点，因此连续按键在间隙内进入了下拉框。

回归测试通过可控的模拟图片请求暂停预览，并将焦点移到语言框，重现录屏中的临时焦点落点。按键从**当前焦点控件**走完整的 WPF PreviewKeyDown / KeyDown 路由，而非始终对队列调用单个处理函数。未修改应用代码的 preview.50 实际失败：

```text
Held Down leaked during preview: language=1, source=0, scenario=down.
```

记录：`artifacts/keyboard51/baseline-failure.log`。修复前测试运行目录 `JavMetaLite.UiSmokeTests/bin/keyboard51-baseline/` 保留；它只加入最初的回归入口，应用仍为 preview.50。不要在该目录重建，以免覆盖复现材料。

## 修改范围

- 在窗口的预览按键事件中先识别队列发起的上下键，再切换选中项。加载时即使焦点暂移，也会消费属于队列的重复按键，不改变语言或来源。
- 保留原有单次预览与忙碌锁，不并发切片、不排队累积加载请求。预览结束后仍可响应后续重复按键。
- 松开对应按键结束本次长按；若队列焦点尚在等待恢复，新的方向键仍归该待恢复的队列处理。
- 用焦点意图版本号保护异步恢复：用户点击其他位置、输入其他键（包括 Tab/修饰键）或窗口停用后，使旧的恢复失效。用户主动使用下拉框、文本框时不会被队列拦截。
- 没有改动四语言资源、来源路由、预览缓存策略、下载并发、保存逻辑、依赖或文件安全保护。

## 自动化结果

完整门槛：Core **46/46**、文件事务 **30/30**、WPF UI **PASS**、包版本规则 **PASS**。日志：`artifacts/keyboard51/final-gate.log`。

新增键盘检查已经加入普通 WPF 门槛，另保留快速入口 `--keyboard`。八组场景包括向下、向上、加载中松键、松键再按、主动鼠标切换、Tab 切换、窗口停用，以及取消预览。检查内容包括：

- 慢图片请求期间重复上下键，语言和来源不变；只产生原有两次预览请求，不累积重复任务。
- 缓存往返、首尾边界，以及不等待 Dispatcher 空闲的快速缓存往返；完成的图片继续复用。
- 显式焦点操作不被延迟恢复抢回；主动上下切换四语言和来源仍有效。
- 文本框中的方向键不改变队列影片。
- 取消未完成预览后可以继续导航，再次访问重新载入；不破坏 preview.50 的取消恢复保护。

这是实际 WPF 控件上的合成事件回归，不是系统级键盘长按回放；临时焦点落点由测试主动设置，窗口停用事件由测试触发。没有访问真实网站、操作用户的真实影片文件，或重新保存用户的偏好配置。当时建议用户用原来那种长按方式试用确认；后续人工复测已通过，见文首记录。

## 复测

```powershell
.\.dotnet-sdk\dotnet.exe run --project JavMetaLite.UiSmokeTests --configuration Release --no-restore -p:OutputPath=bin/keyboard51-test/ -- --keyboard artifacts/keyboard51/recheck
.\scripts\Test-Automated.ps1 -DotNet .\.dotnet-sdk\dotnet.exe -Configuration Release -NoRestore -BuildOutputDirectory bin/preview51-gate/
```

使用本地已有 SDK / 缓存，不重新安装依赖。内部便携包名为 `JavMetaLite-v1.2.0-preview.51-win-x64-portable.zip`。本轮不公开发布；保存速度优化仍是独立待办。

## 便携包校验

- ZIP：60,442,461 字节；SHA-256 `A6E37938220E9A2247EB570E2F5520BC9FC627F92B304440DA6A4DBAB5041F8E`，与版本专属校验文件一致。
- 独立解压到 `artifacts/keyboard51/portable-check/`，共 4 个文件；产品版本 `1.2.0-preview.51`、文件版本 `1.2.0.0`。
- 解压 EXE SHA-256 `F17E0E6DFA48E151A9C17753630588CCE50124CA04F9242AFE361122B60DA7C4`，与打包目录 EXE 一致。
- 打包日志：`artifacts/keyboard51/package.log`。便携 EXE 仅做解压、版本和哈希校验，未在真实影片库启动；不将其描述为用户实机验收。保留旧便携包、SDK、缓存及复现材料。
