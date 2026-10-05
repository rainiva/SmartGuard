# 名单滚轮与WPS整合窗口排查

## 问题和范围

用户实测：程序名单不能用滚轮滚动；PDF、表格和演示的窗口识别都返回wps.exe，截图确认PDF验证与wpspdf.exe名单项不同。

滚轮代码根因假设：外层contentScrollViewer的PreviewMouseWheel无条件Handled，在隧道路由阶段阻止内部ListBox原生滚动。以真实WPF嵌套列表路由复现后再修改同一ScrollViewerSmoothScroll入口。

WPS现有采集真实读取外层HWND所属进程，不读取文档标题。官方说明存在整合模式（不同文档集中同一窗口）和多组件模式；不能据此直接断言该机器PDF编辑区的实际进程。当前工具桌面GetForegroundWindow=0、EnumWindows数量0，虽然同Session能看到WPS进程，仍不能直接验收其真实窗口。需要只读窗口结构证据，不能用标题/扩展名/同目录子进程猜身份。

## Change Impact Matrix / Critical Module Check

| 域 | 本轮边界 |
|---|---|
| Function | 内层能消费方向时交给原生滚动，边界可滚动外层；普通页保留惯性。WPS仅按可验证窗口事实处理。 |
| Architecture | 滚轮仍归ScrollViewerSmoothScroll；前台采集仍归ForegroundProgramReader，Settings链接同一源码。 |
| Resource / Performance | 滚轮只沿实际视觉祖先，O(树深度)，不扫描名单、不新增Timer/订阅；WPS诊断为一次性、有数量上限的只读探针，不是生产监听。 |
| Time / Reliability | 不改变惯性寿命和关闭清理；未知身份/切换竞态不猜测授权。 |
| Data / Compatibility / Security | 不自动修改配置、WPS设置或名单，不读取文档内容/标题，不扩张授权。 |
| Recovery / Observability | 未支持的宿主整合窗口应给出可理解解释，不能误标WPS文字或虚构已验证其他EXE。 |
| Operations | 本轮不发布/安装；自动门禁与实际桌面验收分开记录。 |

配置、状态、日志所有权及Engine不干预语义保持不变；若采集语义有证据支持变化，先补RED与架构门禁再实现。

依据：[WPS整合/多组件模式](https://www.wps.cn/learning/question/detail/id/331157)、[GetGUIThreadInfo](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getguithreadinfo)、[GetAncestor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getancestor)。

## 执行证据

### 根因与处理

- 滚轮根因已复现：外层预览事件将内部列表事件标为Handled。两项RED均失败于事件被提前处理；最小修复只沿输入来源的祖先链寻找能消费当前方向的内层ScrollViewer，保留其原生处理，边界交回外层。
- 横向覆盖了程序搜索/名单等嵌套滚动容器，并检查普通页面惯性、日志列表原生滚动；没有增加常驻订阅或名单全量工作。
- 用户补充：点击PDF内容区后仍识别为wps.exe。
- 后续只读探针用新线程绑定已打开的输入桌面，未切换用户桌面、未创建窗口、未修改WPS设置。枚举上限为2048个顶层窗口、每个相关窗口512个子窗口，不读取标题、命令行或文档内容。当前实际枚举940个顶层窗口；可见OpusApp外层HWND=18024692、PID=28176（wps），其可见PDF子窗口137386/137428/464976/3151400均属于PID=25628（wpspdf）。这是本次PDF实测，不代表表格和演示结构已完成验收。
- GetGUIThreadInfo查询外层窗口线程成功，但focus=0。因此不能把“读取外层线程焦点”当作已经验证可行的组件识别方案。
- 当前生产采集按外层窗口所有者返回wps.exe，与Engine一致；名称改为中性的WPS Office，并在不匹配提示/确认框说明整合宿主限制，避免虚构PDF组件已验证。没有改变Engine匹配语义、配置格式或名单授权。

### 自动测试

- `.work/program-window-debug/nested-wheel-red.trx`：2失败，符合RED预期。
- `.work/program-window-debug/wps-host-red.trx`：2失败，分别复现错误组件名称与缺少宿主解释。
- `.work/program-window-debug/program-window-green.trx`：37通过，0失败，0跳过。含滚轮两个方向、两个边界、已有惯性/原生日志滚动，以及搜索、精确验证、取消和名单保留。
- 第一次全量门禁因沙箱禁止Pester临时测试注册表访问而失败：Pester报告0通过/156失败/0跳过，其余三层NOT RUN；summary为`run-tests-20261005T034008209Z-90fb2ae42d3a4574822142a25425ecae.summary.json`。
- 正常权限重跑全量门禁通过，退出码0。新鲜summary：`.work/test-results/run-tests-20261005T034029991Z-d832a630f04447c586386582941a8d38.summary.json`。Pester 156、.NET 1943、Tray Integration 2、Installer Integration 9通过，各层0失败、0跳过。Settings为619通过。源码/文档已验证严格UTF-8无BOM、链接目标和`git diff --check`。

### 剩余功能缺口及下一步

整合窗口内部组件识别尚未实现；提示修正不能视为该功能完成。若继续支持，需要扩展唯一ForegroundProgramReader并让Settings/Engine同时采用同一语义，先确定以下边界再进入生产实现：

1. 补齐文字、PDF、表格、演示在整合/独立窗口模式的实际结构证据，以及标签切换、弹窗、多个可见组件、焦点为空时的样本。
2. 区分窗口宿主与当前文档组件；不能把所有可见或后台运行的子进程都当作前台命中，也不能只靠标题扩展名选择EXE。
3. 定义歧义、权限不足、子窗口变更和前台竞态的降级语义，保证不扩张精确路径授权。UI明确区分宿主匹配和组件匹配。
4. 先增加架构门禁与行为RED，确认高频采集的窗口/时间上限，记录当前基线和改后真实规模成本，再实现共享采集入口；保持观察期与守护不干预语义。
5. 自动门禁后验收实际四组件切换和名单规则命中，再考虑构建安装包。

本次实际用户滚轮、键盘焦点/DPI/高对比度/减少动画验收以及WPS四组件端到端规则命中为NOT RUN；PDF宿主结构已经只读验证。安装器集成门禁构建了同版本1.6.2测试安装包；本轮未递增版本、安装或发布正式新版本，未修改正式名单。

## 后续实际验收（2026-10-05，名单准入与观察行为已收口）

正式安装目录D:\Apps\SmartGuard，文件版本1.6.2；实际Engine PID=33716，未暂停、前台名单限制启用，名单包括wps.exe、et.exe、wpp.exe、wpspdf.exe和WINWORD.EXE，观察期180秒。本轮只读取配置/状态/日志，未改动名单。

- 用户确认“文字已测试”。正式日志11:56:40.606（北京时间）记录wps.exe匹配第1条规则；11:56:24.503记录自动守护、actionResult=already-satisfied，当前高性能计划符合设置，无需切换。这证明规则准入和自动决策工作，不代表实际发生了一次电源计划切换。
- 11:57:07.189记录wps.exe切至后台、进入180秒观察期；11:57:12.299记录actionResult=not-attempted，保留同一高性能计划。文字命中及切出后进入观察期已取得实际证据。
- 同一轮日志还出现11:56:21.799的et.exe第2条命中、11:56:25.963的wpspdf.exe第4条命中。尚未与用户逐项确认窗口操作，先记为实际EXE命中证据，不能宣称全部组件用户路径已验收。
- 这些日志表明至少存在实际外层窗口归属et.exe/wpspdf.exe的时刻，因此不能据此建议只保留wps.exe；整合窗口截图和本轮不同EXE命中均是真实样本，窗口模式/切换过程仍有差异。
- 用户随后确认“PDF已测试”。对应阶段11:59:07.823日志记录programFileName=wps.exe、第1条规则、observationState=returned；距前次观察开始约121秒，小于180秒，确实取消观察并恢复自动检测。11:59:08.411自动决策为already-satisfied；12:00:02.253切出后再次进入180秒观察期，12:00:11.797为not-attempted且计划GUID不变。本次PDF用户路径通过的是宿主wps.exe规则，没有分别验证内部wpspdf.exe。
- 正式1.6.2引擎仍将wps.exe描述为“WPS 文字”，因此上述PDF恢复日志的文字标签不能用于判断实际文档类型；应以用户操作与programFileName共同解读。本轮没有修改正式运行版本。
- 用户确认“表格已测试”。对应阶段12:01:56.092以wps.exe第1条规则返回，observationState=returned，距前次观察开始约114秒；12:01:57.221自动守护为already-satisfied。12:02:31.917切出后进入180秒观察期，12:02:43.996为not-attempted且计划GUID不变。本阶段规则准入及观察转换正常，但没有新增et.exe命中证据，不能称表格独立EXE验证通过。
- 本次表格阶段还在12:01:58.220和12:02:00.240出现wpspdf.exe命中，并与wps.exe交替；没有对应的退出观察事件。仅凭日志无法确定具体标签/弹窗切换过程，不将这两项擅自解释为表格进程。能确认的是多个名单EXE间转换未进入观察期。
- 用户确认“演示已测试”。对应阶段12:04:14.241以wps.exe第1条规则返回，observationState=returned，距前次观察开始约102秒；12:04:14.833为automatic/already-satisfied。12:04:46.043切出后进入180秒观察期，12:05:01.611为not-attempted且计划GUID不变。本阶段名单准入及观察转换正常，没有wpp.exe命中证据，不能称演示独立EXE验证通过。
- 演示阶段12:04:15.581另有et.exe第2条命中，随后恢复wps.exe；未出现名单内转换触发观察的记录。用户确认的文档类型与实际窗口EXE应分开记录，不从程序文件名猜测当前文档。
- 观察到期实测：12:04:46.043开始观察，12:07:46.111记录observationState=expired、guardReason=waiting-for-listed-program；同一时刻GuardStateChanged记录guardSource=none、actionResult=not-attempted。起止日志时间相差约180.07秒，符合180秒设置。期间没有新的返回名单记录。
- 到期前后分别只读执行powercfg.exe /getactivescheme，均为高性能GUID=8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c；正式状态文件同样保持该GUID并转为waiting-for-listed-program。到期后保留计划、不干预通过；系统后续自主如何改变计划不属于本次验证范围。

### 本轮结论与边界

四类文档均由用户确认已操作，对应各阶段见到名单准入、返回取消观察、自动决策和切出重新观察；180秒到期不干预也已实测。本轮“现有名单覆盖WPS使用场景与退出观察行为”验收收口。当前计划始终符合目标，所以没有刻意制造真实电源切换；already-satisfied不代表执行过切换。

对于四类文档执行相同规则的个人项目使用方式，暂缓整合窗口内部组件识别，没有必要仅为文档类型标签增加前台采集复杂度。保留已有四个WPS精确EXE名单项，不建议删到仅wps.exe：真实日志已出现et.exe和wpspdf.exe，可能存在不同外层窗口或切换阶段；现有证据不足以承诺一个EXE覆盖所有模式。

组件独立识别的真实用户路径仍未完整验证，尤其演示wpp.exe未见命中；本轮不据此宣称四组件均已独立识别。前述滚轮修复、DPI/可访问性实际验收仍NOT RUN。正式1.6.2日志的wps.exe名称仍为“WPS文字”，不能当作文档类型事实；升级日志名称属于独立改动，未在本轮修改生产代码或部署。
