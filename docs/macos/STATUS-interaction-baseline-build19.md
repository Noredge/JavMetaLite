# JavMetaLite macOS — 布局与图片预览续作

更新：2026-10-23，America/Los_Angeles。**当前 App 0.4.0 / build19；本轮原生目标复验、9套门禁、正式ad-hoc打包及独立源码重建已完成，作为本地候选交付。** Worker 为 0.4.0-dev。基线 HEAD `874b2dd524106189806e4d0c86589a0c8ee142c1`、分支 `feature/macos-native`；原有未提交修改保留，未 reset、commit、push 或发布 Release。未发现适用 AGENTS.md。

40项矩阵当前为 **33通过、6待测、1不适用**；这是逐项、跨已记录构建的证据范围，不等于build19全项通过。新增通过为T08冲突可读性、X08样张术语与空目录边界、R06慢图片取消/重试；待测仍为T03、X03、X09、R05、R08、R10。详见 [本轮验收记录](REGRESSION-LAYOUT-PREVIEW-QA.md)、[40项矩阵](WINDOWS-CASE-REGRESSION.md)。私有附件全文及真实长文件名未复制进文档。

## 当前实现与实际验收

- 日语样张术语统一；图片终态失败显示错误、详情和显式重试。QA10、QA18及QA19分别验证受控HTTP503、响应头前/正文首字节后关闭再打开、重试、切来源和任务隔离；使用非交付测试宿主与合成loopback图片，不冒充生产网站或GUI审核revision读回。
- build18与最终冻结生产19前端分别完成四语言×正常/最小×单片/批量主界面16组合；QA18/QA19各有实际2360×1520像素默认窗口8图。生产19截图所用前端与正式交付包签名字节SHA完全相同；QA19仅非签名编译前端相同，独立测试宿主不随包交付。最小内容920×620点，19交付App实际滚动确认四语言最小单片及英/日最小批量共6组合的两预览按钮可达，截图独立复核无遮挡；所有次级窗口及长文案尚未全覆盖，X03/R08保留待测。两次截图误标原件已归档并独立复核替换图，不能以文件名代替画面。
- build12图库挂起、13–16冷启动崩溃的失败保留；`.automatic`及不调用分隔线setter的反例排除了简单的`.none`必要条件归因。17改为有限列宽的LazyVStack按行图库；18恢复`.none`后冷启动通过。19再次执行检查器展开的图库冷启动通过。18及正式19包另实际验证取消一张样张后切页/调整窗口、来源不变，样张仍未选。19实际冷启动图库与这条操作链完成后正常Quit/Discard1；before截图复选框在视口外，状态依据AX及操作记录，after空框可见。
- build18日志中的62,494条视图更新期间发布状态警告促成19导航更新修复。19实际PID33379在17:15:55–17:18:35记录该警告0条，但仍有负宽度135条、负高度135条，根因未定位；不能宣称原生日志无警告或所有路径已稳定。
- build19导入1000项合成队列后共1002项；搜索60999保持批量勾选2，30次Up/30次Down后队尾可见，Space使勾选3→2。再加入Conflict2共1004项，仅勾选两冲突影片，NFO开、图片关、原位置重命名开、覆写关；浅/深色预览均阻断且确认禁用，未execute，正常Quit并丢弃2项未保存编辑。离散注入不证明物理长按或性能达标。

**build18首次批量预览超时仍未定因。** 初次显示Working后出现`WORKER_UNAVAILABLE`，没有执行保存；Check Status于0.635秒恢复，同PID重新只读预览1.106秒、深色1.849秒得到正确阻断。独立全新四任务CLI会话0.0554秒完成阻断预览，未选Local任务完整快照不变。后续成功不抹去初次失败，也不能证明19修复了该超时。

SwiftUI/AppKit、随包C# Worker与共享Core边界保持。MovieJob审核revision、NFO round-trip、全队列冲突预检、文件事务与恢复journal仍约束保存；本轮只读GUI预览不代替Core/协议文件事务回归。WPF和Windows图片适配保留，不宣称Windows实机通过。

## 验证状态与边界

| 范围 | 已确认结果 | 边界 |
| --- | --- | --- |
| build18完整门禁 | 9套通过；Swift36、四语言358 keys/1462 checks；Worker63、retention5等日志保留 | 这是18，不预填19结果；正式日志 `native-final-complete-gate-build18.log` |
| build18打包/独立无.git源码重建 | 主包验证通过；中文空格目录独立重建exit0、213.524秒，生产49与retention5通过；278 selected inputs相同 | 独立构建的二进制不逐字节一致；证明源码/流程重建，不声称可复现相同签名ZIP |
| build19完整门禁/正式包 | 9套exit0：Core50/559（含私有16输入）、Swift37、Worker63、retention5、本地化358 keys/1462 checks、release36、manifest15；正式包49生产+5retention通过 | Worker63为49生产+14宿主；完整门禁156.172秒、打包145.252秒，无.git中文空格目录离线独立重建157.414秒exit0，49生产+5retention通过，两次包验证通过；278 selected inputs digest `fe2972b9f04412cb99b24232ee0f35ee6997b06c54f80da58b437cb701c01dd7` |
| 真实15/30分钟到期 | App9生产Worker实等1808.446秒通过，旧token/plan/operation拒绝、近期回执幂等，队列/revision/媒体保持 | 最终19与9的198个Worker payload文件中197个完全相同，apphost除嵌入签名外字节相同；不称19本身实等30分钟 |
| 文件保护 | QA10原12文件SHA/bytes不变；build13、18只读预览、QA18与最终19/QA19后2012文件SHA/bytes/mtime全同，新/缺失0 | 早期12文件基线没有mtime，不能倒补；最终回读只证明文件保持，不替代内存草稿或revision检查 |

RSS证据仅为限定时段观测：build18约600秒主界面与约120秒队列记录，另保留旧五分钟Worker循环。没有长期无泄漏、平台故障恢复或预定性能阈值结论。

## 输出与运行

本轮本地候选输出根为 `outputs/Mac-regression-layout-preview-2026-10-22`。18截图、历史候选及重建证据保留；19正式包与GUI/QA前端绑定、独立源码重建均已核对。此前build9及更早交付不覆盖；12–16失败候选不作为最终交付。

最终ZIP SHA256：`4ad3bf43de2a7cf7568154f377da5fb8cf49719a299b2a85abba14f2f284ea56`；ZIP manifest SHA256：`d11e5d135c8d29cd045a646fe4289f373ccb752a1677dbaa90d89863ece26484`。主包、交付App和ZIP解包的payload及签名扩展属性精确一致；独立重建的绝对路径与二进制不同，证明源码/流程可重建，不宣称二进制或签名ZIP逐字节可复现。全部原生验收客户端正常退出，受控图片服务在0 active、gates已释放后停止。

直接解压并打开 `JavMetaLite.app`；包内Worker自包含，运行不需安装.NET SDK。源码重建在仓库根使用精确.NET SDK 10.0.400：

```sh
export DOTNET=/absolute/path/to/dotnet
./scripts/macos/test.sh --arch arm64 --configuration Release
./scripts/macos/package.sh --arch arm64 --configuration Release --sign-mode adhoc
```

继续**只签名、不公证**：无有效Developer ID身份，ad-hoc签名不等于Gatekeeper分发验收；未公证上传、staple、移除quarantine或绕过系统保护。物理长按、中日IME、完整VoiceOver/纯键盘、多屏、实体外接盘/SMB/拔盘/磁盘满/断电、真实EIO硬件故障、Intel/macOS15、Windows实机与干净Mac分发仍未验证。
