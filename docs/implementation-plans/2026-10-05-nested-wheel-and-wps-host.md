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
